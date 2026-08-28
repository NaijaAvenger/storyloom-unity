// Storyloom Unity Kit — story simulator (Window ▸ Storyloom Simulator).
// Runs the headless StorySimulator over the imported story: explores every startable beat, choice branch and random
// outcome from every reachable state (deduplicated, capped), then reports what static validation can't see —
// soft-locks, endings no path reaches, and beats never played. No scene or play mode needed.
#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Storyloom.EditorTools
{
    public class StoryloomSimulatorWindow : EditorWindow
    {
        [MenuItem("Window/Storyloom Simulator")] public static void Open() => GetWindow<StoryloomSimulatorWindow>("Story Simulator");

        StoryloomBindings _b; int _maxStates = 4000; bool _strictOrder = true, _applyStartingValues = true;
        StorySimulator.Result _result; double _seconds; Vector2 _scroll;

        void OnEnable() { if (!_b) _b = AssetDatabase.FindAssets("t:StoryloomBindings").Select(g => AssetDatabase.LoadAssetAtPath<StoryloomBindings>(AssetDatabase.GUIDToAssetPath(g))).FirstOrDefault(); }

        void OnGUI()
        {
            _b = (StoryloomBindings)EditorGUILayout.ObjectField("Bindings", _b, typeof(StoryloomBindings), false);
            var s = _b && _b.story ? _b.story.Story : null;
            if (s == null) { EditorGUILayout.HelpBox("Assign a Bindings asset with an imported story.", MessageType.Info); return; }

            _maxStates = EditorGUILayout.IntSlider(new GUIContent("Max states", "Cap on distinct (played beats + variables) states explored. Raise it for big branchy stories; results below the cap are exact, at the cap they are a lower bound."), _maxStates, 500, 50000);
            _strictOrder = EditorGUILayout.Toggle(new GUIContent("Strict order", "Mirror the director's strictOrder — beats only become available in story order"), _strictOrder);
            _applyStartingValues = EditorGUILayout.Toggle(new GUIContent("Apply starting values", "Apply the bindings' starting-variable overrides before simulating"), _applyStartingValues);

            if (GUILayout.Button("Simulate", GUILayout.Height(28)))
            {
                var t0 = EditorApplication.timeSinceStartup;
                var sim = new StorySimulator(s, new StorySimulator.Options
                {
                    maxStates = _maxStates,
                    strictOrder = _strictOrder,
                    configureStart = _applyStartingValues && _b ? (System.Action<StoryRunner>)_b.ApplyStartingValues : null,
                });
                _result = sim.Run();
                _seconds = EditorApplication.timeSinceStartup - t0;
            }
            if (_result == null) return;
            var r = _result;
            string Title(string id) { var n = s.GetNode(id); return n == null ? id : (string.IsNullOrEmpty(n.title) ? id : n.title); }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUILayout.LabelField($"{r.statesExplored} states explored ({r.statesDeduped} deduped) in {_seconds:0.00}s · {r.completions} completed run(s){(r.truncated ? "  ·  TRUNCATED at cap — coverage is a lower bound" : "")}", EditorStyles.wordWrappedLabel);

            var endings = (s.nodes ?? new StoryNode[0]).Where(n => n.IsEnding).ToList();
            EditorGUILayout.LabelField($"Endings: {r.endingsReached.Count}/{endings.Count} reached", EditorStyles.boldLabel);
            foreach (var e in endings) EditorGUILayout.LabelField((r.endingsReached.Contains(e.id) ? "  ✓ " : "  ✗ NEVER REACHED — ") + Title(e.id), EditorStyles.miniLabel);

            if (r.softLocks.Count > 0)
            {
                EditorGUILayout.LabelField($"Soft-locks: {r.softLocks.Count} state(s) stall with no ending and no available beat", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox("The story can reach a state where nothing more can start and no ending was reached. Shortest reproductions below — play the beats in this order (the Playtest panel can jump you through them).", MessageType.Error);
                foreach (var t in r.softLocks)
                {
                    EditorGUILayout.LabelField($"· stalls after {t.trail.Count} node(s), {t.unplayedContent} content beat(s) locked out", EditorStyles.miniBoldLabel);
                    EditorGUILayout.LabelField("  path: " + string.Join(" → ", t.trail.Select(Title).Distinct()), EditorStyles.wordWrappedMiniLabel);
                    if (!string.IsNullOrEmpty(t.variables)) EditorGUILayout.LabelField("  vars: " + t.variables, EditorStyles.wordWrappedMiniLabel);
                }
            }
            else EditorGUILayout.LabelField("Soft-locks: none found" + (r.truncated ? " (within the explored cap)" : ""), EditorStyles.boldLabel);

            if (r.neverPlayed.Count > 0)
            {
                EditorGUILayout.LabelField($"Never played on any explored path: {r.neverPlayed.Count} node(s)", EditorStyles.boldLabel);
                foreach (var id in r.neverPlayed.Take(60)) { var n = s.GetNode(id); EditorGUILayout.LabelField($"  {Title(id)}  <{n?.type}>", EditorStyles.miniLabel); }
                if (r.neverPlayed.Count > 60) EditorGUILayout.LabelField($"  … and {r.neverPlayed.Count - 60} more", EditorStyles.miniLabel);
                EditorGUILayout.HelpBox("Unreached content is either genuinely dead (no path leads there), condition-locked in every explored state, or beyond the state cap. Cross-check the Validate tab for statically dangling nodes.", MessageType.Info);
            }
            EditorGUILayout.EndScrollView();
        }
    }
}
#endif
