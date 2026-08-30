// Storyloom Unity Kit — playtest panel (Window ▸ Storyloom ▸ Playtest).
// Play-mode debugging without the walking: jump to any beat (PlayNode ignores strict order, so you can start anywhere),
// rewind to before any beat already played (the director snapshots full story state per beat), edit variables and
// inventory live, and flip the director's gating toggles. Iterating on beat 40 no longer means earning beats 1–39.
#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Storyloom.EditorTools
{
    public class StoryloomPlaytestWindow : EditorWindow
    {
        [MenuItem("Window/Storyloom Playtest")] public static void Open() => GetWindow<StoryloomPlaytestWindow>("Playtest");
        Vector2 _scroll; string _filter = ""; bool _showBeats = true, _showHistory = true, _showVars = true, _showItems = true, _showTranscript = true;
        static string TranscriptText(StoryloomDirector d) =>
            $"# {(d.Story != null ? d.Story.name : "Storyloom")} — playthrough {System.DateTime.Now:yyyy-MM-dd HH:mm}\n\n" + string.Join("\n", d.Transcript);

        void OnInspectorUpdate() { if (Application.isPlaying) Repaint(); }

        void OnGUI()
        {
            var d = StoryloomDirector.Instance;
            if (!Application.isPlaying || d == null || d.Runner == null || d.Story == null)
            {
                EditorGUILayout.HelpBox("Enter play mode in a scene with a Storyloom Director. This panel then lets you jump to any beat, rewind, and edit variables live.", MessageType.Info);
                return;
            }
            var s = d.Story;
            EditorGUILayout.LabelField($"{s.name}  ·  played {d.Played.Count}  ·  {(d.InBeat ? "IN BEAT" : "free")}  ·  story@ {d.CurrentLocationId}", EditorStyles.miniLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                d.strictOrder = GUILayout.Toggle(d.strictOrder, "Strict order", EditorStyles.miniButton);
                d.gateByLocation = GUILayout.Toggle(d.gateByLocation, "Location gate", EditorStyles.miniButton);
                d.gateDialogueByCharacter = GUILayout.Toggle(d.gateDialogueByCharacter, "Dialogue gate", EditorStyles.miniButton);
                d.autoPlaySceneBeatsOnEnter = GUILayout.Toggle(d.autoPlaySceneBeatsOnEnter, "Auto scene beats", EditorStyles.miniButton);
                if (GUILayout.Button(new GUIContent("Hot-reload story", "Rebuild the runner from the story asset without leaving play mode — variables, inventory and played beats survive. A live-link pull during play triggers this automatically."), EditorStyles.miniButton)) d.RequestHotReload();
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            _showTranscript = EditorGUILayout.Foldout(_showTranscript, $"Transcript ({d.Transcript.Count} lines)", true);
            if (_showTranscript)
            {
                d.recordTranscript = EditorGUILayout.ToggleLeft("Record transcript", d.recordTranscript);
                foreach (var line in d.Transcript.Skip(Mathf.Max(0, d.Transcript.Count - 12)))
                    EditorGUILayout.LabelField(line, EditorStyles.miniLabel);
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(d.Transcript.Count == 0))
                    {
                        if (GUILayout.Button("Copy", GUILayout.Width(60))) EditorGUIUtility.systemCopyBuffer = TranscriptText(d);
                        if (GUILayout.Button("Save…", GUILayout.Width(60)))
                        {
                            var path = EditorUtility.SaveFilePanel("Save transcript", "", (d.Story != null ? d.Story.name : "storyloom") + "-playthrough.md", "md");
                            if (!string.IsNullOrEmpty(path)) { System.IO.File.WriteAllText(path, TranscriptText(d)); EditorUtility.RevealInFinder(path); }
                        }
                        if (GUILayout.Button("Clear", GUILayout.Width(60))) d.Transcript.Clear();
                    }
                }
            }

            _showHistory = EditorGUILayout.Foldout(_showHistory, $"History — rewind ({d.History.Count})", true);
            if (_showHistory)
            {
                if (d.History.Count == 0) EditorGUILayout.LabelField("(no beats played yet)", EditorStyles.miniLabel);
                for (int i = d.History.Count - 1; i >= 0; i--)
                {
                    var r = d.History[i];
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        using (new EditorGUI.DisabledScope(d.InBeat))
                            if (GUILayout.Button("⟲", GUILayout.Width(26))) d.RewindTo(r);
                        EditorGUILayout.LabelField($"{r.time:0.0}s  {r.title}", EditorStyles.miniLabel);
                    }
                }
                if (d.History.Count > 0) EditorGUILayout.LabelField("Rewind restores story state fully; world objects a pickup destroyed stay gone (deactivated ones return).", EditorStyles.wordWrappedMiniLabel);
            }

            _showBeats = EditorGUILayout.Foldout(_showBeats, "Beats — play any node (bypasses strict order)", true);
            if (_showBeats)
            {
                _filter = EditorGUILayout.TextField("Filter", _filter);
                int shown = 0;
                foreach (var n in s.nodes ?? new StoryNode[0])
                {
                    if (n.IsCheck || n.IsRandom || n.IsJump) continue;   // mid-beat plumbing, not startable content
                    var title = string.IsNullOrEmpty(n.title) ? n.id : n.title;
                    if (!string.IsNullOrEmpty(_filter) && title.IndexOf(_filter, System.StringComparison.OrdinalIgnoreCase) < 0 && (n.type ?? "").IndexOf(_filter, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (++shown > 200) { EditorGUILayout.LabelField("… filter to see more", EditorStyles.miniLabel); break; }
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        using (new EditorGUI.DisabledScope(d.InBeat))
                            if (GUILayout.Button("▶", GUILayout.Width(26))) d.PlayNode(n.id);
                        var loc = s.GetLocation(n.locationId);
                        EditorGUILayout.LabelField($"{(d.Played.Contains(n.id) ? "✓ " : "   ")}{title}  <{n.type}>{(loc != null ? "  @ " + loc.name : "")}", EditorStyles.miniLabel);
                    }
                }
            }

            _showVars = EditorGUILayout.Foldout(_showVars, "Variables", true);
            if (_showVars)
            {
                foreach (var key in d.Runner.Variables.Keys.Where(k => !k.StartsWith(StoryRunner.ItemPrefix)).OrderBy(k => k).ToList())
                {
                    var v = d.Runner.Variables[key];
                    if (v is bool b) { var nb = EditorGUILayout.Toggle(key, b); if (nb != b) d.Runner.Set(key, nb); }
                    else if (v is double num) { var nn = EditorGUILayout.DoubleField(key, num); if (!nn.Equals(num)) d.Runner.Set(key, nn); }
                    else { var str = v?.ToString() ?? ""; var ns2 = EditorGUILayout.TextField(key, str); if (ns2 != str) d.Runner.Set(key, ns2); }
                }
            }

            _showItems = EditorGUILayout.Foldout(_showItems, "Inventory", true);
            if (_showItems)
            {
                foreach (var it in s.items ?? new Item[0])
                {
                    bool has = d.Runner.HasItem(it.id);
                    bool now = EditorGUILayout.Toggle(it.name, has);
                    if (now && !has) d.Runner.GiveItem(it.id);
                    else if (!now && has) d.Runner.TakeItem(it.id);
                }
            }

            EditorGUILayout.EndScrollView();
        }
    }
}
#endif
