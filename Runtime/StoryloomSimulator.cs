// Storyloom runtime — headless story simulation.
// Explores the story graph the way the director would play it, without a scene: from each reachable state it tries every
// beat the world could start (talking to an NPC, entering a location, examining a discoverable, picking up an item) and
// follows every choice and random outcome. Because states are deduplicated by (played beats + variables), independent
// beat orderings converge and the search stays tractable; a cap keeps pathological graphs from running away.
//
// What it finds — the bugs static validation can't see:
//   · soft-locks: states where no beat is available, nothing more can ever start, and no ending was reached
//   · endings that no explored path reaches, and beats never played on any path
//   · with strict order on, content locked out by the order the player did things in
//
// Model (documented assumptions):
//   · the player can walk anywhere and talk to anyone — location/dialogue gates pause a beat, they never block content,
//     so the simulation plays beats straight through and ignores gating
//   · played beats are not replayed (replaying re-applies effects and would make the state space infinite)
//   · every unlocked branch of a choice / check / random is explored, not a sampled one
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Storyloom
{
    public class StorySimulator
    {
        public class Options
        {
            public int maxStates = 4000;           // hard cap on distinct (played, variables) states explored
            public int maxNodesPerBeat = 300;      // loop guard inside one beat
            public bool strictOrder = true;        // mirror StoryloomDirector.strictOrder
            public Action<StoryRunner> configureStart;   // e.g. bindings.ApplyStartingValues
        }

        public class Terminal                      // a state the search could not extend
        {
            public bool reachedEnding;             // false = soft-lock candidate
            public List<string> trail = new List<string>();   // beat ids in play order, for reproduction
            public string variables = "";          // human-readable snapshot
            public int unplayedContent;            // content beats still unplayed when it stalled
        }

        public class Result
        {
            public int statesExplored, statesDeduped;
            public bool truncated;                 // hit maxStates: coverage below is a lower bound
            public HashSet<string> nodesPlayed = new HashSet<string>();
            public HashSet<string> endingsReached = new HashSet<string>();
            public List<string> endingsMissed = new List<string>();     // ending node ids never reached
            public List<string> neverPlayed = new List<string>();       // node ids never played on any path
            public List<Terminal> softLocks = new List<Terminal>();     // stalled before any ending
            public int completions;                // terminal states that had reached an ending
        }

        readonly StoryloomStory _story; readonly Options _o; readonly StoryRunner _runner;
        public StorySimulator(StoryloomStory story, Options options = null)
        {
            _story = story ?? throw new ArgumentNullException(nameof(story));
            _o = options ?? new Options();
            _runner = new StoryRunner(story);
        }

        class Sim
        {
            public StoryRunnerState runner;
            public HashSet<string> played;
            public List<string> trail;
            public bool endingSeen;
        }

        public Result Run()
        {
            var result = new Result();
            var seen = new HashSet<string>();
            var frontier = new Queue<Sim>();

            _runner.ResetVariables(); _o.configureStart?.Invoke(_runner);
            var root = new Sim { runner = _runner.SnapshotState(), played = new HashSet<string>(), trail = new List<string>(), endingSeen = false };
            seen.Add(Key(root)); frontier.Enqueue(root);

            while (frontier.Count > 0)
            {
                if (result.statesExplored >= _o.maxStates) { result.truncated = true; break; }
                var s = frontier.Dequeue(); result.statesExplored++;

                var moves = StartableBeats(s).ToList();
                if (moves.Count == 0) { Terminalise(s, result); continue; }
                foreach (var m in moves)
                {
                    foreach (var next in PlayBeat(s, m))
                    {
                        var key = Key(next);
                        if (!seen.Add(key)) { result.statesDeduped++; continue; }
                        foreach (var id in next.played) result.nodesPlayed.Add(id);
                        frontier.Enqueue(next);
                    }
                }
            }

            foreach (var e in _endings) result.endingsReached.Add(e);
            foreach (var n in _story.nodes ?? new StoryNode[0])
            {
                if (n.IsEnding && !result.endingsReached.Contains(n.id)) result.endingsMissed.Add(n.id);
                if (!result.nodesPlayed.Contains(n.id)) result.neverPlayed.Add(n.id);
            }
            // keep the few shortest soft-lock repros; hundreds of near-duplicates help no one
            result.softLocks = result.softLocks.OrderBy(t => t.trail.Count).Take(10).ToList();
            return result;
        }

        void Terminalise(Sim s, Result result)
        {
            if (s.endingSeen) { result.completions++; return; }
            Restore(s);
            int unplayed = (_story.nodes ?? new StoryNode[0]).Count(n => !s.played.Contains(n.id) && IsContent(n));
            if (unplayed == 0 && s.played.Count == 0) return;   // empty story
            result.softLocks.Add(new Terminal { reachedEnding = false, trail = new List<string>(s.trail), variables = VariablesSummary(), unplayedContent = unplayed });
        }

        static bool IsContent(StoryNode n) => !(n.IsCheck || n.IsRandom || n.IsJump);

        // ---- what can start a beat from this state (mirrors the director's world surfaces) --------------------------
        IEnumerable<StoryNode> StartableBeats(Sim s)
        {
            Restore(s);
            var start = _story.StartNode;
            foreach (var n in _story.nodes ?? new StoryNode[0])
            {
                if (s.played.Contains(n.id)) continue;                        // no replays (see model notes)
                if (!Startable(n, start)) continue;                           // only nodes the world can begin a beat at
                if (!Available(n, s.played, start)) continue;                 // strict story order
                if (!Ok(n)) continue;                                         // entry conditions
                yield return n;
            }
        }
        static bool Involves(StoryNode n) => !string.IsNullOrEmpty(n.speakerId) || (n.characterIds != null && n.characterIds.Length > 0) || (n.lines != null && n.lines.Any(l => !string.IsNullOrEmpty(l.speakerId)));
        static bool Startable(StoryNode n, StoryNode start) =>
            n == start || n.entry || n.IsDiscoverable || n.type == "scene" || n.type == "event" || n.type == "unlock" || Involves(n);
        bool Ok(StoryNode n) => n != null && (n.IsCheck || _runner.Evaluate(n.conditions, n.conditionMode, out _));
        bool Available(StoryNode n, HashSet<string> played, StoryNode start)
        {
            if (!_o.strictOrder) return true;
            if (n == start || n.entry) return true;
            return Predecessors(n).Any(p => played.Contains(p.id));
        }
        IEnumerable<StoryNode> Predecessors(StoryNode n)
        {
            foreach (var p in _story.nodes)
            {
                if (p.links != null && p.links.Any(l => l.toNodeId == n.id)) yield return p;
                else if (p.IsJump && p.jumpToNodeId == n.id) yield return p;
            }
            if (n.IsDiscoverable && !string.IsNullOrEmpty(n.hostNodeId)) { var h = _story.GetNode(n.hostNodeId); if (h != null) yield return h; }
        }

        // ---- play one beat from `s`, branching at every choice / check side / random outcome ------------------------
        List<Sim> PlayBeat(Sim s, StoryNode first)
        {
            var results = new List<Sim>();
            Restore(s);
            _runner.GoTo(first.id);
            Walk(Clone(s), 0, results);
            return results;
        }
        // Continues from the runner's Current, mutating `sim` as it goes; snapshots before branch points so every branch
        // resumes from the same spot. Ends a beat exactly where the director would (ending, dead end, return-to-host).
        void Walk(Sim sim, int hops, List<Sim> results)
        {
            while (true)
            {
                var n = _runner.Current; if (n == null) { End(sim, results); return; }
                sim.played.Add(n.id); sim.trail.Add(n.id);
                if (n.IsEnding) { sim.endingSeen = true; End(sim, results, n.id); return; }
                if (++hops > _o.maxNodesPerBeat) { End(sim, results); return; }   // loop guard

                var opts = _runner.GetOptions().Where(o => !o.locked).ToList();
                if (opts.Count == 0) { End(sim, results); return; }
                if (opts.Any(o => o.isReturn)) { End(sim, results); return; }     // discoverable's way back: beat over

                bool branch = n.IsChoice || n.IsRandom || (n.IsCheck && opts.Count > 1);
                if (!branch) { _runner.Choose(opts[0]); continue; }

                var here = _runner.SnapshotState();
                foreach (var o in opts)
                {
                    _runner.RestoreState(here);
                    var fork = Clone(sim);
                    _runner.Choose(o);
                    Walk(fork, hops, results);
                }
                return;
            }
        }
        void End(Sim sim, List<Sim> results, string endingId = null)
        {
            sim.runner = _runner.SnapshotState();
            results.Add(sim);
            if (endingId != null) _endings.Add(endingId);
        }
        readonly HashSet<string> _endings = new HashSet<string>();

        // ---- plumbing -----------------------------------------------------------------------------------------------
        void Restore(Sim s) => _runner.RestoreState(s.runner);
        static Sim Clone(Sim s) => new Sim { runner = s.runner, played = new HashSet<string>(s.played), trail = new List<string>(s.trail), endingSeen = s.endingSeen };
        static string Key(Sim s)
        {
            var sb = new StringBuilder();
            foreach (var id in s.played.OrderBy(x => x, StringComparer.Ordinal)) sb.Append(id).Append('|');
            sb.Append('#');
            var pairs = new List<string>(s.runner.varNames.Count);
            for (int i = 0; i < s.runner.varNames.Count; i++) pairs.Add(s.runner.varNames[i] + "=" + s.runner.varValues[i]);
            pairs.Sort(StringComparer.Ordinal);
            foreach (var p in pairs) sb.Append(p).Append(';');
            return sb.ToString();
        }
        string VariablesSummary()
        {
            var parts = _runner.Variables.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Where(kv => !(kv.Value is bool b && !b))   // hide the noise of everything-false
                .Select(kv => kv.Key + " = " + kv.Value);
            return string.Join(", ", parts);
        }
    }
}
