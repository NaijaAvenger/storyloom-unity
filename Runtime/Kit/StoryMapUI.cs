// Storyloom Unity Kit — story map overlay.
// Hold the map key (M) to see where you are in the narrative, what's reachable next (with locks), what you played
// recently, which endings you've reached, and "all routes completed" once every reachable ending has been seen.
// Built by "Create Stardew-style scene"; restyle freely — only the field references matter.
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace Storyloom
{
    public class StoryMapUI : MonoBehaviour
    {
        public CanvasGroup group;
        public Text titleText, hereText, nextText, recentText, endingsText, progressText;
        public bool alsoToggleWithDoubleTap = false;
        [Tooltip("Show this overlay automatically when the story reaches an ending")] public bool showOnEnding = true;

        StoryloomDirector D => StoryloomDirector.Instance;
        bool _shown, _pinned;

        void Update()
        {
            if (!D || D.keys == null) return;
            bool want = D.keys.MapHeld() || _pinned;
            if (want != _shown) { _shown = want; if (group) { group.alpha = want ? 1 : 0; group.blocksRaycasts = want; group.gameObject.SetActive(true); } }
            if (_shown) Refresh();
        }
        public void Pin(bool on) { _pinned = on; }

        public void Refresh()
        {
            var s = D.Story; var r = D.Runner; if (s == null || r == null) return;
            var cur = r.Current ?? (D.Played.Count > 0 ? s.GetNode(D.Played.Last()) : null);
            if (titleText) titleText.text = s.name + "  —  story map";

            // where you are
            if (hereText) hereText.text = cur == null ? "Not started yet." : $"<b>{cur.title}</b>  <i>({Type(cur)}{(string.IsNullOrEmpty(cur.locationId) ? "" : " · " + Loc(cur.locationId))})</i>\n{Short(cur.text, 220)}";

            // what's next from here
            var sb = new StringBuilder();
            if (cur != null)
            {
                foreach (var l in cur.links ?? new Link[0])
                {
                    var t = s.GetNode(l.toNodeId); if (t == null) continue;
                    string via = cur.IsChoice ? (cur.options?.FirstOrDefault(o => o.id == l.port)?.label ?? l.port) : cur.IsCheck ? (l.port == "pass" ? "if passed" : "if failed") : (l.label ?? "");
                    bool locked = !r.Evaluate(l.conditions, l.conditionMode, out _) || (!t.IsCheck && !r.Evaluate(t.conditions, t.conditionMode, out _));
                    if (cur.IsChoice) { var opt = cur.options?.FirstOrDefault(o => o.id == l.port); if (opt != null && !r.Evaluate(opt.conditions, opt.conditionMode, out _)) locked = true; }
                    sb.Append(D.Played.Contains(t.id) ? "✓ " : locked ? "🔒 " : "→ ").Append("<b>").Append(t.title).Append("</b>");
                    if (!string.IsNullOrEmpty(via)) sb.Append("  <i>").Append(via).Append("</i>");
                    if (!string.IsNullOrEmpty(t.locationId)) sb.Append("  · ").Append(Loc(t.locationId));
                    var st = Short(t.text, 90); if (!string.IsNullOrEmpty(st)) sb.Append("\n     ").Append(st);
                    sb.Append('\n');
                }
                if (cur.IsJump) { var t = s.GetNode(cur.jumpToNodeId); if (t != null) sb.Append("⤳ <b>").Append(t.title).Append("</b>\n"); }
                foreach (var d in s.DiscoverablesAt(cur.id)) sb.Append(D.Played.Contains(d.id) ? "✓ " : "🔍 ").Append("<b>").Append(d.title).Append("</b>  <i>").Append(d.discoverKind).Append("</i>\n");
                if (cur.IsEnding) sb.Append("— ending —\n");
            }
            if (nextText) nextText.text = sb.Length > 0 ? sb.ToString().TrimEnd() : "(nothing linked)";

            // recent
            if (recentText) { var recent = r.History.Where(id => s.GetNode(id) != null).Reverse().Skip(cur != null ? 1 : 0).Take(4).Select(id => s.GetNode(id).title).ToList(); recentText.text = recent.Count > 0 ? string.Join("  ←  ", recent) : "—"; }

            // endings + progress
            var reachable = Reachable(s); var endings = s.nodes.Where(n => n.IsEnding && reachable.Contains(n.id)).ToList();
            int seen = endings.Count(e => D.Played.Contains(e.id));
            if (endingsText) endingsText.text = endings.Count == 0 ? "(no endings)" : string.Join("\n", endings.Select(e => (D.Played.Contains(e.id) ? "✓ " : "○ ") + e.title));
            int playedReach = reachable.Count(id => D.Played.Contains(id));
            bool allDone = endings.Count > 0 && seen == endings.Count;
            if (progressText) progressText.text = allDone ? $"<b>ALL ROUTES COMPLETED</b>  —  {seen}/{endings.Count} endings · {playedReach}/{reachable.Count} beats" : $"{playedReach}/{reachable.Count} beats played  ·  {seen}/{endings.Count} endings reached";
            if (allDone && showOnEnding) _pinned = true;
        }

        HashSet<string> _reach; StoryloomStory _reachFor;
        HashSet<string> Reachable(StoryloomStory s)
        {
            if (_reach != null && _reachFor == s) return _reach;
            _reach = new HashSet<string>(); _reachFor = s; if (s.StartNode == null) return _reach;
            var q = new Queue<string>(); q.Enqueue(s.StartNode.id); _reach.Add(s.StartNode.id);
            while (q.Count > 0) { var n = s.GetNode(q.Dequeue()); if (n == null) continue; foreach (var l in n.links ?? new Link[0]) if (_reach.Add(l.toNodeId)) q.Enqueue(l.toNodeId); if (n.IsJump && !string.IsNullOrEmpty(n.jumpToNodeId) && _reach.Add(n.jumpToNodeId)) q.Enqueue(n.jumpToNodeId); foreach (var d in s.DiscoverablesAt(n.id)) if (_reach.Add(d.id)) q.Enqueue(d.id); }
            return _reach;
        }
        string Loc(string id) { var l = D.Story.GetLocation(id); return l != null ? l.name : id; }
        static string Type(StoryNode n) => char.ToUpper(n.type[0]) + n.type.Substring(1);
        static string Short(string t, int max) { if (string.IsNullOrEmpty(t)) return ""; t = t.Replace("\n", " "); return t.Length <= max ? t : t.Substring(0, max - 1) + "…"; }
    }
}
