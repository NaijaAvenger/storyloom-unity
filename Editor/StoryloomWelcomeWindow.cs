// Storyloom Unity Kit — welcome & guide window.
// Opens automatically the first time the package lands in a project, and after that either on every editor start or never
// (the toggle at the bottom). Always available from Window ▸ Storyloom Welcome. The auto-show fires once per editor
// session (SessionState), so script recompiles and play-mode domain reloads don't keep reopening it.
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Storyloom.EditorTools
{
    [InitializeOnLoad]
    static class StoryloomWelcomeBoot
    {
        static StoryloomWelcomeBoot() { EditorApplication.delayCall += MaybeShow; }
        static void MaybeShow()
        {
            if (Application.isPlaying) return;
            if (SessionState.GetBool(StoryloomWelcomeWindow.SessionKey, false)) return;
            SessionState.SetBool(StoryloomWelcomeWindow.SessionKey, true);
            bool firstTime = !EditorPrefs.GetBool(StoryloomWelcomeWindow.SeenKey, false);
            if (firstTime || EditorPrefs.GetBool(StoryloomWelcomeWindow.BootKey, true)) StoryloomWelcomeWindow.Open();
            EditorPrefs.SetBool(StoryloomWelcomeWindow.SeenKey, true);
        }
    }

    public class StoryloomWelcomeWindow : EditorWindow
    {
        // per-project keys, so one machine's projects don't share the choice
        internal static string SeenKey => "Storyloom.Welcome.Seen." + PlayerSettings.productGUID;
        internal static string BootKey => "Storyloom.Welcome.OnBoot." + PlayerSettings.productGUID;
        internal const string SessionKey = "Storyloom.Welcome.SessionShown";

        [MenuItem("Window/Storyloom Welcome")] public static void Open()
        {
            var w = GetWindow<StoryloomWelcomeWindow>(true, "Welcome to Storyloom");
            w.minSize = new Vector2(520, 560); w.maxSize = new Vector2(520, 900);
        }

        Vector2 _scroll;
        static void H(string text) { GUILayout.Space(10); EditorGUILayout.LabelField(text, EditorStyles.boldLabel); }
        static void P(string text) => EditorGUILayout.LabelField(text, EditorStyles.wordWrappedLabel);
        static void Li(string text) => EditorGUILayout.LabelField("  ·  " + text, EditorStyles.wordWrappedMiniLabel);

        void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            var pkg = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(StoryloomDirector).Assembly);
            EditorGUILayout.LabelField("Storyloom → Unity" + (pkg != null ? "  ·  v" + pkg.version : ""), new GUIStyle(EditorStyles.boldLabel) { fontSize = 18 });
            P("Import a story exported from Storyloom, test-play it in one click, then wire the same data into your own game with drag-and-drop assets.");

            H("Quick start — playable in five steps");
            Li("1. Window ▸ Storyloom ▸ 'Import story JSON…' → pick your .unity.json export.");
            Li("2. Bind (optional): assign prefabs, portraits and sprites per character / item / location in the tabs. Skip this and placeholders are used.");
            Li("3. Pick a Game style: Top-down (Stardew), Third person, or First person.");
            Li("4. 'Create test scene' → press Play. WASD move, E interact, Tab inventory, hold M story map, F1 debug HUD.");
            Li("5. Re-import any time — bindings and scenes survive; everything is matched by id.");

            H("Into your own game — entity assets");
            P("'Generate entity assets' creates one ScriptableObject per character, item, location and discoverable. They are typed handles into the story: drag one onto a GameObject, onto a prefab in the Project window, or into a component's asset field, and that object becomes the entity — dialogue, gating, inventory and all. The Entities tab is a drag palette of them.");
            Li("Alt-drop a location = Signpost instead of a zone.");
            Li("Generated alongside: StoryIds.cs — compile-time constants (StoryIds.Characters.X) instead of raw id strings.");
            Li("LocationAnchor component: drop it in your real level, assign a location asset, 'Populate from story' places that location's cast and props at your hand-placed spawn points.");

            H("Testing the narrative");
            Li("Window ▸ Storyloom Simulator: explores every branch headlessly and reports soft-locks, unreachable endings, and beats no path plays.");
            Li("Window ▸ Storyloom Playtest (in play mode): jump to any beat, rewind to before any played beat, edit variables and inventory live, toggle gating.");
            Li("F1 in play mode: the debug HUD — nearest interactable, focus, zones containing the player, and a log of what the kit just did.");

            H("When something looks wrong");
            Li("'Repair open scene' (main window) rebuilds missing UI, colliders, zone triggers and asset references in place.");
            Li("'Self-test (play mode)' fires the toast, inventory and prompts directly and prints what happened.");
            Li("The README covers the JSON shape, the runtime API, and how to swap in your own controllers and UI.");

            GUILayout.Space(12);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Open Storyloom window", GUILayout.Height(26))) StoryloomEditorWindow.Open();
                if (GUILayout.Button("README", GUILayout.Height(26))) OpenDoc("README.md");
                if (GUILayout.Button("Changelog", GUILayout.Height(26))) OpenDoc("CHANGELOG.md");
            }

            GUILayout.Space(8);
            EditorGUILayout.BeginHorizontal();
            bool onBoot = EditorPrefs.GetBool(BootKey, true);
            bool now = EditorGUILayout.ToggleLeft("Show this window when the project opens", onBoot);
            if (now != onBoot) EditorPrefs.SetBool(BootKey, now);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField(now ? "Shown once per editor start. Untick to never show it automatically again." : "Won't show automatically. Reopen any time: Window ▸ Storyloom Welcome.", EditorStyles.miniLabel);
            if (GUILayout.Button("Close", GUILayout.Height(24))) Close();
            EditorGUILayout.EndScrollView();
        }

        static void OpenDoc(string file)
        {
            var pkg = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(StoryloomDirector).Assembly);
            if (pkg != null) { EditorUtility.OpenWithDefaultApp(System.IO.Path.Combine(pkg.resolvedPath, file)); return; }
            var asset = AssetDatabase.LoadAssetAtPath<Object>("Assets/" + file) ?? AssetDatabase.LoadAssetAtPath<Object>("Packages/com.storyloom.unity/" + file);
            if (asset) { Selection.activeObject = asset; EditorGUIUtility.PingObject(asset); }
            else Debug.LogWarning("Storyloom: couldn't locate " + file);
        }
    }
}
#endif
