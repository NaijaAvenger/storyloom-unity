// Storyloom Unity Kit — the journal / codex (J).
// Pure display over state the runner already tracks: lore learned (lore: flags), places visited (loc: flags), people met
// (talk visits + speakers of played beats) with their relationships and current mood, and the items held. Toggled with
// the journal key; counts as a modal panel (StoryloomDirector.UiBusy), so movement and look pause while it is open.
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace Storyloom
{
    public class StoryJournalUI : MonoBehaviour
    {
        public GameObject panel;
        public Text peopleText, placesText, loreText, itemsText;
        [Tooltip("Trim long descriptions to roughly this many characters")] public int trimAt = 140;

        StoryloomDirector D => StoryloomDirector.Instance;
        public bool IsOpen => panel && panel.activeSelf;
        public void Toggle()
        {
            if (!panel) { Debug.LogWarning("Storyloom: StoryJournalUI has no panel assigned"); return; }
            panel.SetActive(!panel.activeSelf);
            if (panel.activeSelf) { try { Refresh(); } catch (System.Exception e) { Debug.LogError("Storyloom: journal refresh failed — " + e); } }
        }

        void Awake()
        {
            if (!panel) { var t = transform.Find("Journal"); if (t) panel = t.gameObject; }
            if (panel && panel.activeSelf) panel.SetActive(false);   // never start open
        }

        string Trim(string s) => string.IsNullOrEmpty(s) ? "" : s.Length <= trimAt ? s : s.Substring(0, trimAt) + "…";

        public void Refresh()
        {
            var d = D; if (d == null || d.Story == null || d.Runner == null) return;
            var s = d.Story;

            if (peopleText)
            {
                // met = talked to, or they spoke in a beat that has played
                var met = new HashSet<string>(d.TalkVisits.Keys);
                foreach (var n in s.nodes ?? new StoryNode[0])
                {
                    if (!d.Played.Contains(n.id)) continue;
                    if (!string.IsNullOrEmpty(n.speakerId)) met.Add(n.speakerId);
                    if (n.lines != null) foreach (var l in n.lines) if (!string.IsNullOrEmpty(l.speakerId)) met.Add(l.speakerId);
                }
                var sb = new StringBuilder();
                foreach (var c in s.characters ?? new Character[0])
                {
                    if (c.IsProtagonist || !met.Contains(c.id)) continue;
                    var mood = d.Runner.GetString(StoryRunner.MoodPrefix + c.id);
                    var faction = s.GetFaction(c.factionId);
                    sb.Append("<b>").Append(c.name).Append("</b>");
                    if (!string.IsNullOrEmpty(c.roleType)) sb.Append("  <color=#c8b88a>").Append(c.roleType).Append("</color>");
                    if (!string.IsNullOrEmpty(mood)) sb.Append("  (").Append(mood).Append(")");
                    if (faction != null) sb.Append("\n   ").Append(faction.name);
                    if (c.relationships != null)
                        foreach (var r in c.relationships)
                        { var other = s.GetCharacter(r.characterId); if (other != null) sb.Append("\n   ").Append(r.kind).Append(" of ").Append(other.name); }
                    sb.Append('\n');
                }
                peopleText.text = sb.Length > 0 ? sb.ToString() : "(no one yet — go talk to somebody)";
            }
            if (placesText)
            {
                var sb = new StringBuilder();
                foreach (var l in s.locations ?? new Location[0])
                {
                    if (!d.Runner.GetBool(StoryRunner.LocPrefix + l.id)) continue;
                    sb.Append("<b>").Append(l.name).Append("</b>");
                    if (!string.IsNullOrEmpty(l.kind)) sb.Append("  <color=#c8b88a>").Append(l.kind).Append("</color>");
                    if (l.id == d.CurrentLocationId) sb.Append("  · here");
                    var blurb = StoryloomDirector.LocationBlurb(l);
                    if (!string.IsNullOrEmpty(blurb)) sb.Append("\n   <size=12>").Append(Trim(blurb)).Append("</size>");
                    sb.Append('\n');
                }
                placesText.text = sb.Length > 0 ? sb.ToString() : "(nowhere visited yet)";
            }
            if (loreText)
            {
                var sb = new StringBuilder();
                foreach (var lo in s.lore ?? new Lore[0])
                {
                    if (!d.Runner.GetBool(StoryRunner.LorePrefix + lo.id)) continue;
                    sb.Append("<b>").Append(lo.name).Append("</b>");
                    if (!string.IsNullOrEmpty(lo.kind)) sb.Append("  <color=#c8b88a>").Append(lo.kind).Append("</color>");
                    if (!string.IsNullOrEmpty(lo.description)) sb.Append("\n   <size=12>").Append(Trim(lo.description)).Append("</size>");
                    sb.Append('\n');
                }
                loreText.text = sb.Length > 0 ? sb.ToString() : "(nothing learned yet)";
            }
            if (itemsText)
            {
                var owned = d.Runner.Inventory().ToList();
                itemsText.text = owned.Count > 0
                    ? string.Join("\n", owned.Select(it => "<b>" + it.name + "</b>" + (string.IsNullOrEmpty(it.kind) ? "" : "  <color=#c8b88a>" + it.kind + "</color>")))
                    : "(empty-handed)";
            }
        }
    }
}
