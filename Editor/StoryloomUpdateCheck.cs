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
            var pkg = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(StoryloomDirector).Assembly);
            if (pkg == null) { if (manual) EditorUtility.DisplayDialog("Storyloom", "Couldn't determine the installed Storyloom version.", "OK"); return; }
            // Installed from a git URL (possibly a private repo, where anonymous web requests see 404): ask git itself —
            // ls-remote goes through the same credential helper the install and the user's pushes use.
            if (pkg.source == UnityEditor.PackageManager.PackageSource.Git && pkg.git != null && !string.IsNullOrEmpty(pkg.git.hash)) GitCheck(pkg, manual);
            else HttpCheck(pkg.version, manual);
        }

        static string Short(string hash) => string.IsNullOrEmpty(hash) ? "?" : hash.Substring(0, Mathf.Min(8, hash.Length));
        static void Fail(bool manual, string detail)
        {
            if (manual) EditorUtility.DisplayDialog("Storyloom", "Update check failed — you may be offline, or the repository is private and couldn't be reached anonymously.\n\n" + detail, "OK");
            else Debug.Log("Storyloom: automatic update check skipped (" + detail + ")");
        }

        static void GitCheck(UnityEditor.PackageManager.PackageInfo pkg, bool manual)
        {
            // packageId is "com.storyloom.unity@<git url>[#revision]"
            var id = pkg.packageId ?? ""; int at = id.IndexOf('@');
            var url = at >= 0 ? id.Substring(at + 1) : null;
            if (!string.IsNullOrEmpty(url)) { int h = url.IndexOf('#'); if (h >= 0) url = url.Substring(0, h); }
            if (string.IsNullOrEmpty(url)) { HttpCheck(pkg.version, manual); return; }
            string rev = string.IsNullOrEmpty(pkg.git.revision) ? "HEAD" : pkg.git.revision;
            var psi = new System.Diagnostics.ProcessStartInfo("git", $"ls-remote \"{url}\" \"{rev}\"")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            psi.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0";   // fail fast instead of waiting on a hidden credential prompt
            System.Diagnostics.Process p;
            try { p = System.Diagnostics.Process.Start(psi); }
            catch (System.Exception e) { Fail(manual, "git not available: " + e.Message); return; }
            double t0 = EditorApplication.timeSinceStartup; bool killed = false;
            void Pump()
            {
                if (!p.HasExited)
                {
                    if (EditorApplication.timeSinceStartup - t0 > 20 && !killed) { killed = true; try { p.Kill(); } catch { } }
                    return;
                }
                EditorApplication.update -= Pump;
                string outp, err; int code;
                using (p) { outp = p.StandardOutput.ReadToEnd(); err = p.StandardError.ReadToEnd(); code = p.ExitCode; }
                if (killed || code != 0 || string.IsNullOrWhiteSpace(outp)) { Fail(manual, killed ? "git ls-remote timed out" : ("git ls-remote: " + (string.IsNullOrWhiteSpace(err) ? "no output" : err.Trim()))); return; }
                var remoteHash = outp.Trim().Split('\t', ' ')[0].Trim();
                var localHash = pkg.git.hash;
                bool same = remoteHash == localHash || remoteHash.StartsWith(localHash) || localHash.StartsWith(remoteHash);
                if (!same) StoryloomUpdatePrompt.Open($"{pkg.version} · commit {Short(localHash)}", $"newer commit {Short(remoteHash)} on {rev}");
                else if (manual) EditorUtility.DisplayDialog("Storyloom", $"You're up to date ({pkg.version}, commit {Short(localHash)}).", "Nice");
            }
            EditorApplication.update += Pump;
        }

        static void HttpCheck(string local, bool manual)
        {
            var req = UnityWebRequest.Get(RemotePackageJson);
            req.timeout = 10;
            var op = req.SendWebRequest();
            void Pump()
            {
                if (!op.isDone) return;
                EditorApplication.update -= Pump;
                using (req)
                {
                    if (req.result != UnityWebRequest.Result.Success) { Fail(manual, req.error); return; }
                    var remote = ExtractVersion(req.downloadHandler.text);
                    if (string.IsNullOrEmpty(remote)) { Fail(manual, "couldn't read the remote version"); return; }
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
            EditorGUILayout.LabelField("A newer Storyloom is available", new GUIStyle(EditorStyles.boldLabel) { fontSize = 15 });
            EditorGUILayout.LabelField($"Installed:  {_local}\nAvailable:  {_remote}", EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField("Staying current keeps the importer in step with Storyloom's export format and picks up fixes.", EditorStyles.wordWrappedMiniLabel);
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
