// Storyloom Unity Kit — update check.
// Once per editor session, fetches the package.json on the repo's main branch and compares its version against the
// installed one; when a newer version exists a small prompt opens with the two versions and an update path. The prompt
// carries a checkbox to silence itself ("don't check automatically") — after that, updates can still be checked by hand
// from the Welcome window or Window ▸ Storyloom Check for Updates. The check is fully non-blocking (UnityWebRequest
// pumped from EditorApplication.update) and fails silently when offline.
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace Storyloom.EditorTools
{
    [InitializeOnLoad]
    public static class StoryloomUpdateCheck
    {
        const string RemotePackageJson = "https://raw.githubusercontent.com/NaijaAvenger/storyloom-unity/main/package.json";
        const string RepoUrl = "https://github.com/NaijaAvenger/storyloom-unity";
        internal static string SilenceKey => "Storyloom.Updates.Silenced." + PlayerSettings.productGUID;
        const string SessionKey = "Storyloom.Updates.CheckedThisSession";

        static StoryloomUpdateCheck() { EditorApplication.delayCall += AutoCheck; }
        static void AutoCheck()
        {
            if (Application.isPlaying) return;
            if (SessionState.GetBool(SessionKey, false)) return;           // once per editor session, not per recompile
            SessionState.SetBool(SessionKey, true);
            if (EditorPrefs.GetBool(SilenceKey, false)) return;            // the user silenced the popup
            Check(manual: false);
        }

        [MenuItem("Window/Storyloom Check for Updates")] public static void ManualCheck() => Check(manual: true);

        static void Check(bool manual)
        {
            var local = InstalledVersion();
            if (string.IsNullOrEmpty(local)) { if (manual) EditorUtility.DisplayDialog("Storyloom", "Couldn't determine the installed Storyloom version.", "OK"); return; }
            var req = UnityWebRequest.Get(RemotePackageJson);
            req.timeout = 10;
            var op = req.SendWebRequest();
            void Pump()
            {
                if (!op.isDone) return;
                EditorApplication.update -= Pump;
                using (req)
                {
                    if (req.result != UnityWebRequest.Result.Success)
                    { if (manual) EditorUtility.DisplayDialog("Storyloom", "Update check failed: " + req.error, "OK"); return; }
                    var remote = ExtractVersion(req.downloadHandler.text);
                    if (string.IsNullOrEmpty(remote)) { if (manual) EditorUtility.DisplayDialog("Storyloom", "Couldn't read the remote version.", "OK"); return; }
                    if (Newer(remote, local)) StoryloomUpdatePrompt.Open(local, remote);
                    else if (manual) EditorUtility.DisplayDialog("Storyloom", $"You're up to date ({local}).", "Nice");
                }
            }
            EditorApplication.update += Pump;
        }

        internal static string InstalledVersion()
        {
            var pkg = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(StoryloomDirector).Assembly);
            return pkg != null ? pkg.version : null;
        }
        /// <summary>Pull "version": "x.y.z" out of a package.json without needing its full shape.</summary>
        internal static string ExtractVersion(string packageJson)
        {
            if (string.IsNullOrEmpty(packageJson)) return null;
            var m = System.Text.RegularExpressions.Regex.Match(packageJson, "\"version\"\\s*:\\s*\"([0-9]+\\.[0-9]+\\.[0-9]+[^\"]*)\"");
            return m.Success ? m.Groups[1].Value : null;
        }
        /// <summary>Is `a` a newer semver than `b`? Numeric per part; non-numeric suffixes ignored.</summary>
        internal static bool Newer(string a, string b)
        {
            int[] Parse(string v) { var parts = (v ?? "").Split('.', '-'); var r = new int[3]; for (int i = 0; i < 3 && i < parts.Length; i++) { int n = 0; foreach (var ch in parts[i]) { if (!char.IsDigit(ch)) break; n = n * 10 + (ch - '0'); } r[i] = n; } return r; }
            var va = Parse(a); var vb = Parse(b);
            for (int i = 0; i < 3; i++) { if (va[i] != vb[i]) return va[i] > vb[i]; }
            return false;
        }

        internal static void OpenRepo() => Application.OpenURL(RepoUrl);
    }

    /// <summary>The "update available" prompt: versions, how to update, and the silence checkbox.</summary>
    public class StoryloomUpdatePrompt : EditorWindow
    {
        string _local, _remote;
        public static void Open(string local, string remote)
        {
            var w = GetWindow<StoryloomUpdatePrompt>(true, "Storyloom update available");
            w._local = local; w._remote = remote;
            w.minSize = w.maxSize = new Vector2(420, 240);
        }
        void OnGUI()
        {
            GUILayout.Space(8);
            EditorGUILayout.LabelField($"Storyloom {_remote} is available", new GUIStyle(EditorStyles.boldLabel) { fontSize = 15 });
            EditorGUILayout.LabelField($"You have {_local}. Staying current keeps the importer in step with Storyloom's export format and picks up fixes.", EditorStyles.wordWrappedLabel);
            GUILayout.Space(6);
            EditorGUILayout.LabelField("To update, reinstall the package from its git URL (Package Manager ▸ + ▸ Add package from git URL), or pull if you cloned it locally. The changelog lists what's new.", EditorStyles.wordWrappedMiniLabel);
            GUILayout.Space(8);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Open repository / changelog", GUILayout.Height(26))) StoryloomUpdateCheck.OpenRepo();
                if (GUILayout.Button("Later", GUILayout.Height(26))) Close();
            }
            GUILayout.Space(8);
            bool silenced = EditorPrefs.GetBool(StoryloomUpdateCheck.SilenceKey, false);
            bool now = EditorGUILayout.ToggleLeft("Don't show this again (stop checking for updates automatically)", silenced);
            if (now != silenced) EditorPrefs.SetBool(StoryloomUpdateCheck.SilenceKey, now);
            EditorGUILayout.LabelField(now ? "Silenced. Check by hand any time: Window ▸ Storyloom Check for Updates." : "Checks once per editor start.", EditorStyles.miniLabel);
        }
    }
}
#endif
