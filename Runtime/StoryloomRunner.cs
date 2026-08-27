// Storyloom runtime for Unity.
// Walks a StoryloomStory exactly the way the in-app "Play" panel does:
//  - entering a node applies its effects
//  - Choice nodes expose one option per output; options/links/targets can be locked by conditions
//  - Check nodes evaluate their conditions and take the "pass" or "fail" port
//  - any other node exposes its outgoing links (usually one "Continue")
//
// Minimal use:
//   var runner = new StoryRunner(story);
//   runner.OnNodeEntered += node => ShowNode(node);
//   runner.Start();
//   foreach (var opt in runner.GetOptions()) { /* draw button; disable if opt.locked */ }
//   runner.Choose(opt);   // advance
//
// Variables live in runner.Variables (name → object: bool, double or string).
// Save/load a play-through by serializing SnapshotState() / RestoreState().

using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Storyloom
{
    /// <summary>A selectable way forward from the current node.</summary>
    public class StoryOption
    {
        public string label;
        public Link link;
        public ChoiceOption choiceOption;   // set for Choice nodes
        public StoryNode target;
        public bool locked;
        public string lockReason;           // human-readable, e.g. "needs gold >= 10"
        public bool isDiscoverable;         // from GetDiscoverables(): optional side content at this node
        public bool found;                  // discoverable already visited this run
        public bool isReturn;               // unlinked discoverable: goes back to its host node
        public override string ToString() => locked ? $"{label} (locked: {lockReason})" : label;
    }

    [Serializable]
    public class StoryRunnerState
    {
        public string currentNodeId;
        public List<string> history = new List<string>();
        public List<string> varNames = new List<string>();
        public List<string> varValues = new List<string>();
    }

    public class StoryRunner
    {
        public readonly StoryloomStory Story;
        public readonly Dictionary<string, object> Variables = new Dictionary<string, object>();
        public readonly List<string> History = new List<string>();
        public StoryNode Current { get; private set; }

        public event Action<StoryNode> OnNodeEntered;
        public event Action<string, object> OnVariableChanged;
        public event Action<StoryNode> OnEnding;
        public event Action<string, StoryNode> OnEvent;   // Event nodes: (eventName, node)
        public Func<double> Random = () => UnityEngine.Random.value;   // swap for a seeded RNG in tests

        private readonly Dictionary<string, StoryVariable> _varDecl = new Dictionary<string, StoryVariable>();

        public StoryRunner(StoryloomStory story)
        {
            Story = story ?? throw new ArgumentNullException(nameof(story));
            if (story.variables != null) foreach (var v in story.variables) _varDecl[v.name] = v;
            ResetVariables();
        }

        // ------------------------------------------------------------------ flow

        public void Start()
        {
            ResetVariables();
            History.Clear();
            Current = null;
            var start = Story.StartNode;
            if (start == null) { Debug.LogError("Storyloom: story has no start node."); return; }
            Enter(start);
        }

        /// <summary>Jump to a node by id (applies its effects). Useful for debugging or chapter select.</summary>
        public void GoTo(string nodeId)
        {
            var n = Story.GetNode(nodeId);
            if (n == null) { Debug.LogError($"Storyloom: no node '{nodeId}'."); return; }
            Enter(n);
        }

        public void Choose(StoryOption option)
        {
            if (option == null || option.locked || option.target == null) return;
            Enter(option.target);
        }

        /// <summary>Convenience: follow the first unlocked option (a "Continue").</summary>
        public bool Continue()
        {
            foreach (var o in GetOptions()) if (!o.locked) { Choose(o); return true; }
            return false;
        }

        private void Enter(StoryNode node, int hops = 0)
        {
            Current = node;
            History.Add(node.id);
            ApplyEffects(node.effects);
            if (node.IsJump)
            {
                // Jump nodes are pass-through: apply their effects, then continue at the target (loop guard: 20 hops).
                var target = Story.GetNode(node.jumpToNodeId);
                if (target != null && hops < 20) { Enter(target, hops + 1); return; }
                Debug.LogWarning($"Storyloom: jump '{node.title}' has no valid target (or too many chained jumps).");
            }
            if (node.IsEvent) OnEvent?.Invoke(node.eventName ?? "", node);
            OnNodeEntered?.Invoke(node);
            if (node.IsEnding) OnEnding?.Invoke(node);
        }

        /// <summary>All ways forward from the current node, locked ones included so UI can show why.</summary>
        public List<StoryOption> GetOptions()
        {
            var result = new List<StoryOption>();
            var n = Current;
            if (n == null || n.IsEnding) return result;

            if (n.IsCheck)
            {
                bool pass = Evaluate(n.conditions, n.conditionMode, out _);
                foreach (var l in n.LinksFrom(pass ? "pass" : "fail"))
                    result.Add(MakeOption(l, string.IsNullOrEmpty(l.label) ? (pass ? "Continue (pass)" : "Continue (fail)") : l.label, null));
                return result;
            }

            if (n.IsRandom)
            {
                // all possible outcomes, so UI/debuggers can show them; use PickRandom() to advance
                if (n.options == null) return result;
                foreach (var opt in n.options)
                {
                    if (opt.weight <= 0 || !Evaluate(opt.conditions, opt.conditionMode, out _)) continue;
                    foreach (var l in n.LinksFrom(opt.id)) result.Add(MakeOption(l, opt.label, opt));
                }
                return result;
            }

            if (n.IsChoice)
            {
                if (n.options == null) return result;
                foreach (var opt in n.options)
                {
                    string optLock = Evaluate(opt.conditions, opt.conditionMode, out _) ? null : Reason(opt.conditions, opt.conditionMode);
                    foreach (var l in n.LinksFrom(opt.id))
                    {
                        var o = MakeOption(l, opt.label, opt);
                        if (optLock != null && !o.locked) { o.locked = true; o.lockReason = optLock; }
                        result.Add(o);
                    }
                }
                return result;
            }

            foreach (var l in n.LinksFrom("out"))
                result.Add(MakeOption(l, string.IsNullOrEmpty(l.label) ? "Continue" : l.label, null));
            // an unlinked Discoverable returns to the node it was found at
            if (n.IsDiscoverable && result.Count == 0 && !string.IsNullOrEmpty(n.hostNodeId))
            {
                var host = Story.GetNode(n.hostNodeId);
                if (host != null) result.Add(new StoryOption { label = "Back to " + host.title, target = host, isReturn = true });
            }
            return result;
        }

        /// <summary>Optional side content available at the current node (Discoverable nodes hosted here), locked ones included.</summary>
        public List<StoryOption> GetDiscoverables()
        {
            var result = new List<StoryOption>();
            if (Current == null || Current.IsDiscoverable) return result;
            foreach (var d in Story.DiscoverablesAt(Current.id))
            {
                bool ok = Evaluate(d.conditions, d.conditionMode, out _);
                result.Add(new StoryOption { label = d.title, target = d, isDiscoverable = true, found = History.Contains(d.id), locked = !ok, lockReason = ok ? null : Reason(d.conditions, d.conditionMode) });
            }
            return result;
        }

        /// <summary>For Random nodes: roll by weight among the unlocked outcomes and return it (null if none). Call Choose() on the result.</summary>
        public StoryOption PickRandom()
        {
            var opts = GetOptions().FindAll(o => !o.locked && o.choiceOption != null);
            if (opts.Count == 0) return null;
            double total = 0; foreach (var o in opts) total += o.choiceOption.weight;
            double r = Random() * total;
            foreach (var o in opts) { r -= o.choiceOption.weight; if (r <= 0) return o; }
            return opts[opts.Count - 1];
        }

        /// <summary>For Check nodes: does the current node's test pass right now?</summary>
        public bool CurrentCheckPasses() => Current != null && Current.IsCheck && Evaluate(Current.conditions, Current.conditionMode, out _);

        private StoryOption MakeOption(Link link, string label, ChoiceOption opt)
        {
            var target = Story.GetNode(link.toNodeId);
            var o = new StoryOption { label = label, link = link, choiceOption = opt, target = target };
            if (target == null) { o.locked = true; o.lockReason = "link target missing"; return o; }
            if (!Evaluate(link.conditions, link.conditionMode, out _)) { o.locked = true; o.lockReason = Reason(link.conditions, link.conditionMode); return o; }
            // Entry requirements on the target (Check nodes use their conditions for branching, not gating).
            if (!target.IsCheck && !Evaluate(target.conditions, target.conditionMode, out _)) { o.locked = true; o.lockReason = Reason(target.conditions, target.conditionMode); }
            return o;
        }

        // ------------------------------------------------------------------ variables

        public const string ItemPrefix = "item:";

        public void ResetVariables()
        {
            Variables.Clear();
            foreach (var v in _varDecl.Values) Variables[v.name] = Coerce(v, v.defaultValue);
            if (Story.items != null) foreach (var it in Story.items) Variables[ItemPrefix + it.id] = it.startOwned;
        }

        // ---- inventory: items live in Variables as "item:<id>" → bool, so conditions, effects and save states all see them ----
        public bool HasItem(string itemId) => Get(ItemPrefix + itemId) is bool b && b;
        public void GiveItem(string itemId) { Variables[ItemPrefix + itemId] = true; OnVariableChanged?.Invoke(ItemPrefix + itemId, true); }
        public void TakeItem(string itemId) { Variables[ItemPrefix + itemId] = false; OnVariableChanged?.Invoke(ItemPrefix + itemId, false); }
        public IEnumerable<Item> Inventory()
        {
            if (Story.items == null) yield break;
            foreach (var it in Story.items) if (HasItem(it.id)) yield return it;
        }

        public object Get(string name) => Variables.TryGetValue(name, out var v) ? v : null;
        public bool GetBool(string name) => Get(name) is bool b && b;
        public double GetNumber(string name) => Get(name) is double d ? d : 0;
        public int GetInt(string name) => (int)Math.Truncate(GetNumber(name));
        public float GetFloat(string name) => (float)GetNumber(name);
        public string GetString(string name) => Get(name)?.ToString() ?? "";

        public void Set(string name, object value)
        {
            Variables[name] = _varDecl.TryGetValue(name, out var decl) ? Coerce(decl, value) : value;
            OnVariableChanged?.Invoke(name, Variables[name]);
        }

        private void ApplyEffects(Effect[] effects)
        {
            if (effects == null) return;
            foreach (var e in effects)
            {
                if (string.IsNullOrEmpty(e.variable)) continue;
                if (e.variable.StartsWith(ItemPrefix))
                {
                    if (e.op == "take") TakeItem(e.variable.Substring(ItemPrefix.Length)); else GiveItem(e.variable.Substring(ItemPrefix.Length));
                    continue;
                }
                _varDecl.TryGetValue(e.variable, out var decl);
                object cur = Get(e.variable);
                switch (e.op)
                {
                    case "set": Set(e.variable, Coerce(decl, e.value)); break;
                    // case "add": Set(e.variable, ToNumber(cur) + ToNumber(e.value)); break;
                    // case "subtract": Set(e.variable, ToNumber(cur) - ToNumber(e.value)); break;
                    case "add": Set(e.variable, Coerce(decl, ToNumber(cur) + ToNumber(e.value))); break;       // Coerce truncates for "int"
                    case "subtract": Set(e.variable, Coerce(decl, ToNumber(cur) - ToNumber(e.value))); break;
                    case "toggle": Set(e.variable, !(cur is bool b && b)); break;
                    default: Debug.LogWarning($"Storyloom: unknown effect op '{e.op}'"); break;
                }
            }
        }

        // ------------------------------------------------------------------ conditions

        public bool Evaluate(Condition[] rules, string mode, out List<Condition> failed)
        {
            failed = new List<Condition>();
            if (rules == null || rules.Length == 0) return true;
            bool any = false;
            foreach (var c in rules)
            {
                bool ok = EvaluateOne(c);
                if (ok) any = true; else failed.Add(c);
            }
            bool result = mode == "any" ? any : failed.Count == 0;
            if (result) failed.Clear();
            return result;
        }

        public bool EvaluateOne(Condition c)
        {
            if (c == null || string.IsNullOrEmpty(c.variable)) return true;
            if (c.variable.StartsWith(ItemPrefix))
            {
                bool has = HasItem(c.variable.Substring(ItemPrefix.Length));
                return c.op == "lacks" ? !has : has;
            }
            _varDecl.TryGetValue(c.variable, out var decl);
            object cur = Get(c.variable);
            bool isSet = decl != null && decl.type == "bool" ? cur is bool b && b
                       : cur != null && !(cur is string s && s.Length == 0) && !(cur is double d0 && d0 == 0) && !(cur is bool b2 && !b2);
            switch (c.op)
            {
                case "is set": return isSet;
                case "not set": return !isSet;
            }
            object rhs = Coerce(decl, c.value);
            if (cur is double a && rhs is double bnum)
            {
                switch (c.op)
                {
                    case "==": return Math.Abs(a - bnum) < 1e-9;
                    case "!=": return Math.Abs(a - bnum) >= 1e-9;
                    case ">": return a > bnum;
                    case "<": return a < bnum;
                    case ">=": return a >= bnum;
                    case "<=": return a <= bnum;
                }
            }
            string ls = cur?.ToString() ?? "", rs = rhs?.ToString() ?? "";
            switch (c.op)
            {
                case "==": return string.Equals(ls, rs, StringComparison.OrdinalIgnoreCase);
                case "!=": return !string.Equals(ls, rs, StringComparison.OrdinalIgnoreCase);
                case ">": return string.CompareOrdinal(ls, rs) > 0;
                case "<": return string.CompareOrdinal(ls, rs) < 0;
                case ">=": return string.CompareOrdinal(ls, rs) >= 0;
                case "<=": return string.CompareOrdinal(ls, rs) <= 0;
            }
            Debug.LogWarning($"Storyloom: unknown condition op '{c.op}'");
            return true;
        }

        public string Reason(Condition[] rules, string mode)   // public: the kit shows it on locked discoverables
        {
            if (rules == null || rules.Length == 0) return "";
            var parts = new List<string>();
            foreach (var c in rules)
            {
                if (c.variable != null && c.variable.StartsWith(ItemPrefix))
                {
                    var it = Story.GetItem(c.variable.Substring(ItemPrefix.Length));
                    parts.Add((c.op == "lacks" ? "lacks " : "has ") + (it != null ? it.name : c.variable));
                }
                else parts.Add(c.ToString());
            }
            return (mode == "any" ? "needs any of: " : "needs ") + string.Join(mode == "any" ? " | " : ", ", parts);
        }

        // ------------------------------------------------------------------ helpers

        private static object Coerce(StoryVariable decl, object raw)
        {
            string s = raw?.ToString() ?? "";
            if (decl == null)
            {
                if (bool.TryParse(s, out var b)) return b;
                if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) return d;
                return s;
            }
            switch (decl.type)
            {
                case "bool": return raw is bool bb ? bb : string.Equals(s, "true", StringComparison.OrdinalIgnoreCase);
                // case "number": return raw is double dd ? dd : ToNumber(s);
                case "int": return Math.Truncate(raw is double di ? di : ToNumber(s));   // integers stay whole
                case "float":
                case "number": return raw is double dd ? dd : ToNumber(s);
                default: return s;
            }
        }

        private static double ToNumber(object v)
        {
            if (v is double d) return d;
            if (v is bool b) return b ? 1 : 0;
            return double.TryParse(v?.ToString() ?? "", NumberStyles.Float, CultureInfo.InvariantCulture, out var r) ? r : 0;
        }

        // ------------------------------------------------------------------ save / load

        public StoryRunnerState SnapshotState()
        {
            var st = new StoryRunnerState { currentNodeId = Current?.id ?? "" };
            st.history.AddRange(History);
            foreach (var kv in Variables)
            {
                st.varNames.Add(kv.Key);
                st.varValues.Add(kv.Value is double d ? d.ToString(CultureInfo.InvariantCulture) : kv.Value?.ToString() ?? "");
            }
            return st;
        }

        /// <summary>Restore without re-applying the current node's effects.</summary>
        public void RestoreState(StoryRunnerState st)
        {
            if (st == null) return;
            Variables.Clear();
            for (int i = 0; i < st.varNames.Count; i++)
            {
                _varDecl.TryGetValue(st.varNames[i], out var decl);
                Variables[st.varNames[i]] = Coerce(decl, st.varValues[i]);
            }
            History.Clear(); History.AddRange(st.history);
            Current = Story.GetNode(st.currentNodeId);
            if (Current != null) OnNodeEntered?.Invoke(Current);
        }
    }
}
