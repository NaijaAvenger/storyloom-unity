// Storyloom Unity Kit — editor window (Window ▸ Storyloom).
// Import the .unity.json, bind entities to prefabs / sprites, validate, generate placeholders, and build a playable
// Stardew-style scene in one click. Everything it creates is ordinary Unity objects you can edit or replace.
#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Storyloom.EditorTools
{
    public class StoryloomEditorWindow : EditorWindow
    {
        [MenuItem("Window/Storyloom")] public static void Open() => GetWindow<StoryloomEditorWindow>("Storyloom");
        [MenuItem("Storyloom/Import story JSON…")] public static void ImportMenu() { Open(); GetWindow<StoryloomEditorWindow>().ImportJson(); }

        StoryloomBindings _b; Vector2 _scroll; int _tab; string[] _tabs = { "Characters", "Items", "Locations", "Discoverables", "Entities", "Variables", "Validate" };
        const string Root = "Assets/Storyloom";

        void OnEnable() { if (_b == null) _b = AssetDatabase.FindAssets("t:StoryloomBindings").Select(g => AssetDatabase.LoadAssetAtPath<StoryloomBindings>(AssetDatabase.GUIDToAssetPath(g))).FirstOrDefault(); }

        void OnGUI()
        {
            GUILayout.Space(6);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Import story JSON…", GUILayout.Height(26))) ImportJson();
                using (new EditorGUI.DisabledScope(_b == null))
                {
                    if (GUILayout.Button("Re-sync from story", GUILayout.Height(26))) { Undo.RecordObject(_b, "Sync"); int n = _b.SyncFromStory(); EditorUtility.SetDirty(_b); ShowNotification(new GUIContent($"{n} new entr{(n == 1 ? "y" : "ies")}")); }
                    if (GUILayout.Button("Create placeholder prefabs", GUILayout.Height(26))) CreatePlaceholders();
                    if (GUILayout.Button("Generate entity assets", GUILayout.Height(26))) GenerateEntityAssets();
                    // if (GUILayout.Button("Create Stardew-style scene", GUILayout.Height(26))) CreateScene();
                    if (GUILayout.Button("Create test scene", GUILayout.Height(26))) CreateScene(_b.gameStyle);
                    if (GUILayout.Button("Repair open scene", GUILayout.Height(26))) RepairScene();
                    if (Application.isPlaying && GUILayout.Button("Self-test (play mode)", GUILayout.Height(26))) SelfTest();
                }
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Simulator", GUILayout.Height(20))) StoryloomSimulatorWindow.Open();
                if (GUILayout.Button("Playtest panel", GUILayout.Height(20))) StoryloomPlaytestWindow.Open();
                if (GUILayout.Button("Welcome / guide", GUILayout.Height(20))) StoryloomWelcomeWindow.Open();
            }
            if (_b != null)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Game style", GUILayout.Width(80));
                    var ns = (GameStyle)GUILayout.Toolbar((int)_b.gameStyle, new[] { "Top-down (Stardew)", "Third person", "First person" }, GUILayout.Height(22));
                    if (ns != _b.gameStyle) { Undo.RecordObject(_b, "Game style"); _b.gameStyle = ns; EditorUtility.SetDirty(_b); }
                }
                EditorGUILayout.LabelField(_b.gameStyle == GameStyle.TopDown ? "Top-down: XY world, 2D physics, camera looks down. WASD move, E talk / pick up / examine." :
                                           _b.gameStyle == GameStyle.ThirdPerson ? "Third person: 3D world on XZ, camera orbits behind the player (mouse / right stick). WASD move relative to the camera." :
                                           "First person: 3D world on XZ, mouse look with a crosshair; interact with what you look at. Esc frees the mouse, click to grab it back.", EditorStyles.wordWrappedMiniLabel);
            }
#if !ENABLE_INPUT_SYSTEM
            EditorGUILayout.HelpBox("Active Input Handling is 'Input Manager (Old)': the kit falls back to legacy input (keyboard + mouse only). For the Input System and gamepads: Project Settings ▸ Player ▸ Active Input Handling → 'Input System Package' or 'Both'.", MessageType.Warning);
#endif
            _b = (StoryloomBindings)EditorGUILayout.ObjectField("Bindings", _b, typeof(StoryloomBindings), false);
            if (_b == null) { EditorGUILayout.HelpBox("Import a .unity.json exported from Storyloom (File ▸ Export Unity JSON) to create a Story asset and a Bindings asset.", MessageType.Info); return; }
            if (_b.story == null || _b.story.Story == null) { EditorGUILayout.HelpBox("Bindings has no story asset.", MessageType.Warning); return; }
            var s = _b.story.Story;
            EditorGUILayout.LabelField($"{s.name} — {s.nodes?.Length ?? 0} nodes · {s.characters?.Length ?? 0} characters · {s.items?.Length ?? 0} items · {s.locations?.Length ?? 0} locations · {_b.discoverables.Count} discoverables", EditorStyles.miniLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                var kb = AssetDatabase.LoadAssetAtPath<StoryloomKeyBinds>(Root + "/Data/StoryloomKeyBinds.asset");
                EditorGUILayout.LabelField("Key binds", kb ? kb.HelpLine() : "(created with the scene)", EditorStyles.wordWrappedMiniLabel);
                if (kb && GUILayout.Button("Reset to defaults", GUILayout.Width(120))) { Undo.RecordObject(kb, "Reset binds"); var d = ScriptableObject.CreateInstance<StoryloomKeyBinds>(); EditorUtility.CopySerialized(d, kb); DestroyImmediate(d); EditorUtility.SetDirty(kb); AssetDatabase.SaveAssets(); }
            }
            _b.defaultNpcPrefab = (GameObject)EditorGUILayout.ObjectField("Default NPC prefab", _b.defaultNpcPrefab, typeof(GameObject), false);
            _b.defaultItemPrefab = (GameObject)EditorGUILayout.ObjectField("Default item prefab", _b.defaultItemPrefab, typeof(GameObject), false);
            _b.defaultDiscoverablePrefab = (GameObject)EditorGUILayout.ObjectField("Default discoverable prefab", _b.defaultDiscoverablePrefab, typeof(GameObject), false);
            GUILayout.Space(4);
            _tab = GUILayout.Toolbar(_tab, _tabs);
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUI.BeginChangeCheck();
            switch (_tab)
            {
                case 0: foreach (var c in _b.characters) Row(c.name, c.roleType, () => { c.prefab = Obj("Prefab", c.prefab); c.portrait = Spr("Portrait", c.portrait); c.worldSprite = Spr("World sprite", c.worldSprite); c.voiceBark = (AudioClip)EditorGUILayout.ObjectField("Voice bark", c.voiceBark, typeof(AudioClip), false); }); break;
                case 1: foreach (var i in _b.items) Row(i.name, i.kind, () => { i.prefab = Obj("Prefab", i.prefab); i.icon = Spr("Icon", i.icon); i.stackable = EditorGUILayout.Toggle("Stackable", i.stackable); }); break;
                case 2: foreach (var l in _b.locations) Row(l.name, l.kind, () => { l.sceneName = EditorGUILayout.TextField("Scene name", l.sceneName); l.prefab = Obj("Trigger prefab", l.prefab); l.banner = Spr("Banner art", l.banner); l.ambience = (AudioClip)EditorGUILayout.ObjectField("Ambience", l.ambience, typeof(AudioClip), false); }); break;
                case 3: foreach (var d in _b.discoverables) Row(d.title, d.kind + (string.IsNullOrEmpty(d.hostNodeId) ? "" : " · at " + (s.GetNode(d.hostNodeId)?.title ?? d.hostNodeId)), () => { d.prefab = Obj("Prefab", d.prefab); d.worldSprite = Spr("World sprite", d.worldSprite); }); break;
                case 4: EntitiesTab(); break;
                case 5: VariablesTab(s); break;
                case 6: Validate(s); break;
            }
            if (EditorGUI.EndChangeCheck()) EditorUtility.SetDirty(_b);
            EditorGUILayout.EndScrollView();
        }

        void Row(string title, string sub, System.Action body)
        {
            using (new EditorGUILayout.VerticalScope("box"))
            {
                EditorGUILayout.LabelField(title, EditorStyles.boldLabel); if (!string.IsNullOrEmpty(sub)) EditorGUILayout.LabelField(sub, EditorStyles.miniLabel);
                body();
            }
        }
        GameObject Obj(string l, GameObject v) => (GameObject)EditorGUILayout.ObjectField(l, v, typeof(GameObject), false);
        Sprite Spr(string l, Sprite v) => (Sprite)EditorGUILayout.ObjectField(l, v, typeof(Sprite), false);

        // ------------------------------------------------------------------ import
        void ImportJson()
        {
            var path = EditorUtility.OpenFilePanel("Storyloom Unity JSON", "", "json"); if (string.IsNullOrEmpty(path)) return;
            var text = File.ReadAllText(path);
            StoryloomStory parsed; try { parsed = StoryloomStory.FromJson(text); } catch (System.Exception e) { EditorUtility.DisplayDialog("Storyloom", "Not a Storyloom Unity export: " + e.Message, "OK"); return; }
            if (parsed.format != "storyloom-unity") { EditorUtility.DisplayDialog("Storyloom", "This file is not a Unity export (format '" + parsed.format + "'). In Storyloom use File ▸ Export Unity JSON.", "OK"); return; }
            Directory.CreateDirectory(Root); Directory.CreateDirectory(Root + "/Data");
            var slug = new string(parsed.name.ToLower().Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray()).Trim('-');
            var jsonPath = $"{Root}/Data/{slug}.unity.json"; File.WriteAllText(jsonPath, text); AssetDatabase.ImportAsset(jsonPath);
            var ta = AssetDatabase.LoadAssetAtPath<TextAsset>(jsonPath);
            var storyPath = $"{Root}/Data/{slug}.story.asset";
            var story = AssetDatabase.LoadAssetAtPath<StoryloomStoryAsset>(storyPath);
            if (story == null) { story = CreateInstance<StoryloomStoryAsset>(); AssetDatabase.CreateAsset(story, storyPath); }
            story.json = ta; story.sourceInfo = $"{Path.GetFileName(path)} · imported {System.DateTime.Now:yyyy-MM-dd HH:mm} · export v{parsed.version}"; story.Invalidate(); EditorUtility.SetDirty(story);
            var bindPath = $"{Root}/Data/{slug}.bindings.asset";
            var b = AssetDatabase.LoadAssetAtPath<StoryloomBindings>(bindPath);
            if (b == null) { b = CreateInstance<StoryloomBindings>(); AssetDatabase.CreateAsset(b, bindPath); }
            b.story = story; int added = b.SyncFromStory(); EditorUtility.SetDirty(b); AssetDatabase.SaveAssets();
            _b = b; ShowNotification(new GUIContent($"Imported {parsed.name}: {added} new bindings"));
        }

        // ------------------------------------------------------------------ starting variables
        void VariablesTab(StoryloomStory s)
        {
            EditorGUILayout.HelpBox("Starting values for this game. Blank = the story's default. Applied whenever the runner resets (scene start, ResetStory()).", MessageType.None);
            foreach (var v in s.variables ?? new StoryVariable[0])
            {
                var o = _b.Starting(v.name);
                using (new EditorGUILayout.HorizontalScope("box"))
                {
                    EditorGUILayout.LabelField(v.name, EditorStyles.boldLabel, GUILayout.Width(150)); EditorGUILayout.LabelField(v.type + " · default " + v.defaultValue + (v.tracked ? " · 🔧" : ""), EditorStyles.miniLabel, GUILayout.Width(170));
                    string cur = o != null ? o.value : ""; string nv;
                    if (v.type == "bool") { int i = EditorGUILayout.Popup(cur == "true" ? 1 : cur == "false" ? 2 : 0, new[] { "(default)", "true", "false" }, GUILayout.Width(90)); nv = i == 1 ? "true" : i == 2 ? "false" : ""; }
                    else nv = EditorGUILayout.TextField(cur, GUILayout.Width(110));
                    if (nv != cur) { Undo.RecordObject(_b, "Starting value"); if (string.IsNullOrEmpty(nv)) { if (o != null) _b.startingValues.Remove(o); } else { if (o == null) { o = new StartingValue { name = v.name }; _b.startingValues.Add(o); } o.value = nv; o.enabled = true; } EditorUtility.SetDirty(_b); }
                }
            }
            EditorGUILayout.LabelField("Starting inventory", EditorStyles.boldLabel);
            foreach (var it in s.items ?? new Item[0])
            {
                var key = StoryRunner.ItemPrefix + it.id; var o = _b.Starting(key);
                using (new EditorGUILayout.HorizontalScope("box"))
                {
                    EditorGUILayout.LabelField(it.name, EditorStyles.boldLabel, GUILayout.Width(150)); EditorGUILayout.LabelField((it.startOwned ? "starts owned" : "not owned") + " in the story", EditorStyles.miniLabel, GUILayout.Width(170));
                    string cur = o != null ? o.value : ""; int i = EditorGUILayout.Popup(cur == "true" ? 1 : cur == "false" ? 2 : 0, new[] { "(default)", "owned", "not owned" }, GUILayout.Width(110)); string nv = i == 1 ? "true" : i == 2 ? "false" : "";
                    if (nv != cur) { Undo.RecordObject(_b, "Starting item"); if (string.IsNullOrEmpty(nv)) { if (o != null) _b.startingValues.Remove(o); } else { if (o == null) { o = new StartingValue { name = key }; _b.startingValues.Add(o); } o.value = nv; o.enabled = true; } EditorUtility.SetDirty(_b); }
                }
            }
            if (_b.startingValues.Count > 0 && GUILayout.Button("Clear all overrides")) { Undo.RecordObject(_b, "Clear"); _b.startingValues.Clear(); EditorUtility.SetDirty(_b); }
        }

        // ------------------------------------------------------------------ validate
        void Validate(StoryloomStory s)
        {
            var problems = new List<string>(_b.Unbound());
            if (s.StartNode == null) problems.Add("Story has no start node.");
            var ids = new HashSet<string>(s.nodes.Select(n => n.id));
            foreach (var n in s.nodes) { foreach (var l in n.links ?? new Link[0]) if (!ids.Contains(l.toNodeId)) problems.Add($"'{n.title}' links to a missing node {l.toNodeId}"); if (n.IsDiscoverable && !string.IsNullOrEmpty(n.hostNodeId) && !ids.Contains(n.hostNodeId)) problems.Add($"Discoverable '{n.title}' is hosted at a missing node"); }
            foreach (var n in s.nodes) if (!string.IsNullOrEmpty(n.locationId) && s.GetLocation(n.locationId) == null) problems.Add($"'{n.title}' refers to a missing location");
            var reachable = new HashSet<string>(); var q = new Queue<string>(); if (s.StartNode != null) { q.Enqueue(s.StartNode.id); reachable.Add(s.StartNode.id); }
            while (q.Count > 0) { var n = s.GetNode(q.Dequeue()); if (n == null) continue; foreach (var l in n.links ?? new Link[0]) if (reachable.Add(l.toNodeId)) q.Enqueue(l.toNodeId); if (n.IsJump && !string.IsNullOrEmpty(n.jumpToNodeId) && reachable.Add(n.jumpToNodeId)) q.Enqueue(n.jumpToNodeId); foreach (var d in s.DiscoverablesAt(n.id)) if (reachable.Add(d.id)) q.Enqueue(d.id); }
            int unreachable = s.nodes.Count(n => !reachable.Contains(n.id));
            EditorGUILayout.HelpBox(problems.Count == 0 ? "Ready to play: every entity has a prefab (or a default), links resolve, start node set." : string.Join("\n", problems), problems.Count == 0 ? MessageType.Info : MessageType.Warning);
            EditorGUILayout.LabelField($"{reachable.Count} nodes reachable from start · {unreachable} not reachable · {s.variables?.Length ?? 0} variables · {s.nodes.Count(n => n.IsEvent)} engine events: {string.Join(", ", s.nodes.Where(n => n.IsEvent).Select(n => n.eventName))}", EditorStyles.wordWrappedMiniLabel);
            var discs = s.nodes.Where(n => n.IsDiscoverable).ToList();
            if (discs.Count > 0)
            {
                EditorGUILayout.LabelField("Discoverables (where they'll be placed · what they do):", EditorStyles.boldLabel);
                foreach (var dn in discs) { var locId = EffectiveLocation(s, dn); var reward = StoryloomDirector.RewardSummary(s, dn); EditorGUILayout.LabelField($"• {dn.title} ({(string.IsNullOrEmpty(dn.discoverKind) ? "discoverable" : dn.discoverKind)}) — {(string.IsNullOrEmpty(locId) ? "no location (Backstage)" : s.GetLocation(locId)?.name ?? locId)}{(string.IsNullOrEmpty(reward) ? "" : " · " + reward)}{(dn.conditions != null && dn.conditions.Length > 0 ? " · needs " + string.Join(", ", dn.conditions.Select(c => c.variable + " " + c.op + " " + c.value)) : "")}", EditorStyles.wordWrappedMiniLabel); }
            }
            var chars = s.characters ?? new Character[0];
            EditorGUILayout.LabelField("Who says what where:", EditorStyles.boldLabel);
            foreach (var c in chars)
            {
                var beats = s.nodes.Where(n => n.speakerId == c.id || (n.lines != null && n.lines.Any(l => l.speakerId == c.id)) || (n.characterIds != null && n.characterIds.Contains(c.id))).ToList();
                EditorGUILayout.LabelField($"• {c.name} ({c.roleType}) — {beats.Count} beat{(beats.Count == 1 ? "" : "s")}: {string.Join(", ", beats.Take(6).Select(n => n.title + (string.IsNullOrEmpty(n.locationId) ? "" : " @ " + (s.GetLocation(n.locationId)?.name ?? "?"))))}{(beats.Count > 6 ? " …" : "")}", EditorStyles.wordWrappedMiniLabel);
            }
        }

        // ------------------------------------------------------------------ placeholders
        static Sprite Square(Color c, string name)
        {
            var dir = Root + "/Placeholders"; Directory.CreateDirectory(dir); var p = $"{dir}/{name}.png";
            if (!File.Exists(p)) { var tex = new Texture2D(32, 32); var px = new Color[32 * 32]; for (int i = 0; i < px.Length; i++) { int x = i % 32, y = i / 32; px[i] = (x < 2 || y < 2 || x > 29 || y > 29) ? Color.black : c; } tex.SetPixels(px); tex.Apply(); File.WriteAllBytes(p, tex.EncodeToPNG()); AssetDatabase.ImportAsset(p); }
            var ti = AssetImporter.GetAtPath(p) as TextureImporter;
            if (ti != null && (ti.textureType != TextureImporterType.Sprite || ti.spritePixelsPerUnit != 32)) { ti.textureType = TextureImporterType.Sprite; ti.spriteImportMode = SpriteImportMode.Single; ti.spritePixelsPerUnit = 32; ti.filterMode = FilterMode.Point; ti.SaveAndReimport(); }
            return AssetDatabase.LoadAssetAtPath<Sprite>(p);
        }
        // Material that renders in any pipeline (URP / HDRP / built-in): tries the pipeline's Lit shader, then Standard.
        static Material Mat(Color c, string name)
        {
            var dir = Root + "/Placeholders"; Directory.CreateDirectory(dir); var p = $"{dir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (m == null)
            {
                Shader sh = null;
                if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null) { var n = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline.GetType().Name; sh = Shader.Find(n.Contains("HD") ? "HDRP/Lit" : "Universal Render Pipeline/Lit"); }
                if (sh == null) sh = Shader.Find("Standard"); if (sh == null) sh = Shader.Find("Sprites/Default");
                m = new Material(sh); AssetDatabase.CreateAsset(m, p);
            }
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c); if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            EditorUtility.SetDirty(m); return m;
        }
        // Primitive placeholder: a lit 3D primitive (capsule / cube / sphere) that lives on the XY plane with 2D physics so the
        // top-down player and interactables work unchanged. Replace with real prefabs in the bindings whenever you like.
        static GameObject Primitive(string name, PrimitiveType type, Color c, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(type); go.name = name;
            var col3 = go.GetComponent<Collider>(); if (col3) DestroyImmediate(col3);
            go.transform.localScale = scale; go.GetComponent<MeshRenderer>().sharedMaterial = Mat(c, name.ToLower().Replace(' ', '-'));
            return go;
        }
        // Top-down placeholders live on the XY plane with 2D colliders (labels pushed toward the camera on -z); 3D placeholders keep the
        // primitive's 3D collider and carry billboard labels above them. Each style gets its own prefab folder.
        GameObject Placeholder(string name, PrimitiveType type, Color color, System.Type interactable) => Placeholder(name, type, color, interactable, GameStyle.TopDown);
        GameObject Placeholder(string name, PrimitiveType type, Color color, System.Type interactable, GameStyle style)
        {
            bool xz = style != GameStyle.TopDown;
            var dir = Root + "/Placeholders" + (xz ? "/3D" : ""); Directory.CreateDirectory(dir);
            var go = xz ? Primitive3D(name, type, color, type == PrimitiveType.Capsule ? new Vector3(.7f, .7f, .7f) : new Vector3(.8f, .8f, .8f)) : Primitive(name, type, color, type == PrimitiveType.Capsule ? new Vector3(.7f, .7f, .7f) : new Vector3(.8f, .8f, .8f));
            if (!xz) { var col = go.AddComponent<BoxCollider2D>(); col.size = Vector2.one * .9f; }
            if (interactable != null) go.AddComponent(interactable);
            go.AddComponent<StoryloomPlaceholder>().style = style;
            var label = new GameObject("Label"); label.transform.SetParent(go.transform); label.transform.localPosition = xz ? new Vector3(0, 1.0f, 0) : new Vector3(0, .75f, -1f);   // top-down: z toward the camera so the mesh never hides it
            var tm = label.AddComponent<TextMesh>(); tm.text = name; tm.characterSize = .08f; tm.fontSize = 48; tm.anchor = TextAnchor.LowerCenter; tm.color = Color.white; label.GetComponent<MeshRenderer>().sortingOrder = 5; if (xz) label.AddComponent<Billboard>();
            var prompt = new GameObject("Prompt"); prompt.transform.SetParent(go.transform); prompt.transform.localPosition = xz ? new Vector3(0, 1.35f, 0) : new Vector3(0, 1.05f, -1f); var pt = prompt.AddComponent<TextMesh>(); pt.text = "[E]"; pt.characterSize = .08f; pt.fontSize = 40; pt.anchor = TextAnchor.LowerCenter; pt.color = new Color(1, .85f, .3f); prompt.GetComponent<MeshRenderer>().sortingOrder = 5; if (xz) prompt.AddComponent<Billboard>(); prompt.SetActive(false);
            var it = go.GetComponent<Interactable>(); if (it) it.prompt = prompt;
            var path = $"{dir}/{name}.prefab"; var prefab = PrefabUtility.SaveAsPrefabAsset(go, path); DestroyImmediate(go); return prefab;
        }
        // 3D primitive that keeps its collider (for CharacterController collisions and the first-person look ray)
        static GameObject Primitive3D(string name, PrimitiveType type, Color c, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(type); go.name = name; go.transform.localScale = scale; go.GetComponent<MeshRenderer>().sharedMaterial = Mat(c, name.ToLower().Replace(' ', '-')); return go;
        }
        static bool IsKitPlaceholderOf(GameObject prefab, GameStyle style) { if (!prefab) return false; var m = prefab.GetComponent<StoryloomPlaceholder>(); return m && m.style == style; }
        static bool IsKitPlaceholder(GameObject prefab) => prefab && prefab.GetComponent<StoryloomPlaceholder>();
        void CreatePlaceholders() => CreatePlaceholders(_b.gameStyle);
        void CreatePlaceholders(GameStyle style)
        {
            Sprite npcS = Square(new Color(.95f, .75f, .35f), "npc"), itemS = Square(new Color(.45f, .85f, .55f), "item"), discS = Square(new Color(.9f, .8f, .3f), "discoverable");
            // a default that is empty, or a kit placeholder made for another style, is (re)created for this style; your own prefabs are left alone
            bool Swap(GameObject cur)
            {
                if (cur == null) return true;
                if (IsKitPlaceholder(cur)) return !IsKitPlaceholderOf(cur, style);
                // placeholders from v0.3 have no marker; they were all top-down and live in the Placeholders root
                return style != GameStyle.TopDown && AssetDatabase.GetAssetPath(cur).StartsWith(Root + "/Placeholders/") && !AssetDatabase.GetAssetPath(cur).Contains("/3D/");
            }
            if (Swap(_b.defaultNpcPrefab)) _b.defaultNpcPrefab = Placeholder("NPC", PrimitiveType.Capsule, new Color(.95f, .75f, .35f), typeof(NpcInteractable), style);
            if (Swap(_b.defaultItemPrefab)) _b.defaultItemPrefab = Placeholder("Item", PrimitiveType.Cube, new Color(.45f, .85f, .55f), typeof(ItemPickup), style);
            if (Swap(_b.defaultDiscoverablePrefab)) _b.defaultDiscoverablePrefab = Placeholder("Discoverable", PrimitiveType.Sphere, new Color(.9f, .8f, .3f), typeof(DiscoverableInteractable), style);
            foreach (var c in _b.characters) if (c.worldSprite == null) c.worldSprite = npcS;
            foreach (var i in _b.items) if (i.icon == null) i.icon = itemS;
            foreach (var d in _b.discoverables) if (d.worldSprite == null) d.worldSprite = discS;
            EditorUtility.SetDirty(_b); AssetDatabase.SaveAssets(); ShowNotification(new GUIContent("Placeholders created"));
        }

        // ------------------------------------------------------------------ entity assets
        // One ScriptableObject per character / item / location / discoverable — the typed handles you drag onto GameObjects
        // (hierarchy, scene view, or a component's asset field) to wire your own prefabs to the story. Assets are matched by
        // entityId, so re-importing the story and re-running this renames them but never breaks a scene reference; nothing
        // is ever deleted here (an entity removed upstream keeps its asset, flagged by the inspector as missing).
        static string SafeFileName(string s) { foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '-'); return string.IsNullOrWhiteSpace(s) ? "unnamed" : s.Trim(); }
        void GenerateEntityAssets()
        {
            var s = _b.story.Story; var root = Root + "/Entities"; int created = 0, updated = 0;
            T Ensure<T>(string folder, string id, string nm) where T : StoryloomEntityAsset
            {
                Directory.CreateDirectory($"{root}/{folder}");
                var existing = AssetDatabase.FindAssets("t:" + typeof(T).Name, new[] { root })
                    .Select(g => AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(g)))
                    .FirstOrDefault(a => a && a.entityId == id);
                if (existing == null)
                {
                    existing = CreateInstance<T>(); existing.entityId = id;
                    AssetDatabase.CreateAsset(existing, AssetDatabase.GenerateUniqueAssetPath($"{root}/{folder}/{SafeFileName(nm)}.asset")); created++;
                }
                else updated++;
                existing.story = _b.story; existing.bindings = _b;
                var want = SafeFileName(nm);
                if (existing.name != want && !AssetDatabase.LoadAssetAtPath<T>($"{root}/{folder}/{want}.asset")) AssetDatabase.RenameAsset(AssetDatabase.GetAssetPath(existing), want);
                EditorUtility.SetDirty(existing); return existing;
            }
            foreach (var c in s.characters ?? new Character[0]) Ensure<StoryloomCharacterAsset>("Characters", c.id, c.name);
            foreach (var i in s.items ?? new Item[0]) Ensure<StoryloomItemAsset>("Items", i.id, i.name);
            foreach (var l in s.locations ?? new Location[0]) Ensure<StoryloomLocationAsset>("Locations", l.id, l.name);
            foreach (var n in (s.nodes ?? new StoryNode[0]).Where(n => n.IsDiscoverable)) Ensure<StoryloomDiscoverableAsset>("Discoverables", n.id, n.title);
            AssetDatabase.SaveAssets();
            int stamped = StampEntityAssets();
            GenerateStoryIds();
            ShowNotification(new GUIContent($"Entity assets: {created} created, {updated} refreshed" + (stamped > 0 ? $", {stamped} scene ref(s) filled" : "") + " · StoryIds.cs written"));
            EditorUtility.FocusProjectWindow(); Selection.activeObject = AssetDatabase.LoadAssetAtPath<Object>(root);
        }

        // ------------------------------------------------------------------ id constants
        // Generated C# constants for every story id, so game code says StoryIds.Characters.SisterElowen instead of a raw
        // string — typos become compile errors and renames upstream become compile errors instead of silent no-ops.
        void GenerateStoryIds()
        {
            var s = _b.story.Story; var sb = new System.Text.StringBuilder();
            sb.AppendLine("// Auto-generated by Storyloom (\"Generate entity assets\" in Window ▸ Storyloom). Do not edit — regenerate instead.");
            sb.AppendLine("// Compile-time ids for the story '" + s.name + "': use StoryIds.Characters.X instead of raw id strings.");
            sb.AppendLine("namespace Storyloom");
            sb.AppendLine("{");
            sb.AppendLine("    public static class StoryIds");
            sb.AppendLine("    {");
            void Emit(string group, IEnumerable<(string name, string id)> rows)
            {
                var list = rows.Where(r => !string.IsNullOrEmpty(r.id)).ToList(); if (list.Count == 0) return;
                sb.AppendLine($"        public static class {group}");
                sb.AppendLine("        {");
                var used = new HashSet<string>();
                foreach (var (nm, id) in list)
                {
                    var ident = Identifier(nm); int k = 2; var final = ident;
                    while (!used.Add(final)) final = ident + "_" + k++;
                    sb.AppendLine($"            public const string {final} = \"{id.Replace("\\", "\\\\").Replace("\"", "\\\"")}\";");
                }
                sb.AppendLine("        }");
            }
            var nodes = s.nodes ?? new StoryNode[0];
            Emit("Characters", (s.characters ?? new Character[0]).Select(c => (c.name, c.id)));
            Emit("Items", (s.items ?? new Item[0]).Select(i => (i.name, i.id)));
            Emit("Locations", (s.locations ?? new Location[0]).Select(l => (l.name, l.id)));
            Emit("Discoverables", nodes.Where(n => n.IsDiscoverable).Select(n => (n.title, n.id)));
            Emit("Endings", nodes.Where(n => n.IsEnding).Select(n => (n.title, n.id)));
            Emit("EntryPoints", nodes.Where(n => n.entry || n == s.StartNode).Select(n => (n.title, n.id)));
            Emit("Events", nodes.Where(n => n.IsEvent && !string.IsNullOrEmpty(n.eventName)).Select(n => (n.eventName, n.eventName)).GroupBy(x => x.Item2).Select(g => g.First()));
            Emit("Variables", (s.variables ?? new StoryVariable[0]).Select(v => (v.name, v.name)));
            sb.AppendLine("    }");
            sb.AppendLine("}");
            var path = Root + "/StoryIds.cs";
            File.WriteAllText(path, sb.ToString());
            AssetDatabase.ImportAsset(path);
        }
        static string Identifier(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Unnamed";
            var sb = new System.Text.StringBuilder(); bool up = true;
            foreach (var ch in name)
            {
                if (char.IsLetterOrDigit(ch)) { sb.Append(up ? char.ToUpperInvariant(ch) : ch); up = false; }
                else up = true;   // word break
            }
            if (sb.Length == 0) return "Unnamed";
            if (char.IsDigit(sb[0])) sb.Insert(0, '_');
            return sb.ToString();
        }

        /// <summary>Every generated entity asset, keyed by "TypeName:entityId". Empty when none have been generated yet.</summary>
        internal static Dictionary<string, StoryloomEntityAsset> LoadEntityAssets()
        {
            var map = new Dictionary<string, StoryloomEntityAsset>();
            if (!AssetDatabase.IsValidFolder(Root + "/Entities")) return map;
            foreach (var g in AssetDatabase.FindAssets("t:StoryloomEntityAsset", new[] { Root + "/Entities" }))
            {
                var a = AssetDatabase.LoadAssetAtPath<StoryloomEntityAsset>(AssetDatabase.GUIDToAssetPath(g));
                if (a) map[a.GetType().Name + ":" + a.entityId] = a;
            }
            return map;
        }
        /// <summary>Fill the empty asset fields on every interactable / zone in the open scene from their id strings, so generated
        /// and repaired scenes reference the typed handles instead of bare ids. Never overwrites an asset already assigned.</summary>
        static int StampEntityAssets()
        {
            var map = LoadEntityAssets(); if (map.Count == 0) return 0;
            int n = 0;
            T Find<T>(string id) where T : StoryloomEntityAsset => map.TryGetValue(typeof(T).Name + ":" + id, out var a) ? a as T : null;
            void Did(Object o) { EditorUtility.SetDirty(o); n++; }
            foreach (var x in Object.FindObjectsOfType<NpcInteractable>(true)) { if (x.character) continue; var a = Find<StoryloomCharacterAsset>(x.characterId); if (a) { x.character = a; Did(x); } }
            foreach (var x in Object.FindObjectsOfType<ItemPickup>(true)) { if (x.item) continue; var a = Find<StoryloomItemAsset>(x.itemId); if (a) { x.item = a; Did(x); } }
            foreach (var x in Object.FindObjectsOfType<DiscoverableInteractable>(true)) { if (x.discoverable) continue; var a = Find<StoryloomDiscoverableAsset>(x.nodeId); if (a) { x.discoverable = a; Did(x); } }
            foreach (var x in Object.FindObjectsOfType<Signpost>(true)) { if (x.location) continue; var a = Find<StoryloomLocationAsset>(x.locationId); if (a) { x.location = a; Did(x); } }
            foreach (var x in Object.FindObjectsOfType<LocationTrigger>(true)) { if (x.location) continue; var a = Find<StoryloomLocationAsset>(x.locationId); if (a) { x.location = a; Did(x); } }
            return n;
        }

        // The Entities tab: a palette of the generated assets. Rows are drag sources — drag one onto a GameObject, a prefab
        // in the Project window, or empty ground in the Scene view, exactly like dragging the asset file itself.
        void EntitiesTab()
        {
            var map = LoadEntityAssets();
            if (map.Count == 0)
            {
                EditorGUILayout.HelpBox("No entity assets yet. Generate one ScriptableObject per character / item / location / discoverable — typed handles you drag onto GameObjects and prefabs to wire them to the story.", MessageType.Info);
                if (GUILayout.Button("Generate entity assets", GUILayout.Height(26))) GenerateEntityAssets();
                return;
            }
            EditorGUILayout.LabelField("Drag a row onto a GameObject (Hierarchy / Scene view), onto a prefab in the Project window, or onto empty ground in the Scene view to place it. Alt-drop a location for a Signpost instead of a zone.", EditorStyles.wordWrappedMiniLabel);
            if (GUILayout.Button("Re-generate / refresh", GUILayout.Width(160))) GenerateEntityAssets();
            var groups = new (string title, System.Type type)[] {
                ("Characters", typeof(StoryloomCharacterAsset)), ("Items", typeof(StoryloomItemAsset)),
                ("Locations", typeof(StoryloomLocationAsset)), ("Discoverables", typeof(StoryloomDiscoverableAsset)) };
            foreach (var (title, type) in groups)
            {
                var list = map.Values.Where(a => a.GetType() == type).OrderBy(a => a.DisplayName).ToList();
                if (list.Count == 0) continue;
                GUILayout.Space(6); EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
                foreach (var a in list) EntityRow(a);
            }
        }
        void EntityRow(StoryloomEntityAsset a)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                var rect = GUILayoutUtility.GetRect(new GUIContent(a.DisplayName), EditorStyles.label, GUILayout.ExpandWidth(true), GUILayout.Height(18));
                GUI.Label(rect, (a.Exists ? "≡ " : "≡ (missing) ") + a.DisplayName, a.Exists ? EditorStyles.label : EditorStyles.centeredGreyMiniLabel);
                EditorGUIUtility.AddCursorRect(rect, MouseCursor.MoveArrow);
                var e = Event.current;
                if (e.type == EventType.MouseDrag && rect.Contains(e.mousePosition))
                {
                    DragAndDrop.PrepareStartDrag();
                    DragAndDrop.objectReferences = new Object[] { a };
                    DragAndDrop.StartDrag(a.DisplayName);
                    e.Use();
                }
                if (GUILayout.Button("Ping", GUILayout.Width(44))) { EditorGUIUtility.PingObject(a); Selection.activeObject = a; }
            }
        }

        // ------------------------------------------------------------------ scene
        static Font UiFont() { var f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); if (f == null) f = Resources.GetBuiltinResource<Font>("Arial.ttf"); return f; }
        static Text MakeText(Transform parent, string name, string txt, int size, TextAnchor anchor, Color col)
        {
            var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false); var t = go.AddComponent<Text>(); t.font = UiFont(); t.text = txt; t.fontSize = size; t.alignment = anchor; t.color = col; t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Overflow; return t;
        }
        static RectTransform Fit(GameObject go, Vector2 aMin, Vector2 aMax, Vector2 offMin, Vector2 offMax) { var r = go.GetComponent<RectTransform>(); r.anchorMin = aMin; r.anchorMax = aMax; r.offsetMin = offMin; r.offsetMax = offMax; return r; }
        static GameObject Panel(Transform parent, string name, Color c) { var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false); var img = go.AddComponent<Image>(); img.color = c; return go; }

        // Where a node "is": its own location, else its host's, else the nearest upstream beat with a location (walking links backwards).
        internal static string EffectiveLocation(StoryloomStory s, StoryNode n)
        {
            if (n == null) return "";
            if (!string.IsNullOrEmpty(n.locationId)) return n.locationId;
            var host = s.GetNode(n.hostNodeId); if (host != null && !string.IsNullOrEmpty(host.locationId)) return host.locationId;
            var seen = new HashSet<string> { n.id }; var q = new Queue<StoryNode>(); q.Enqueue(host ?? n);
            while (q.Count > 0)
            {
                var cur = q.Dequeue(); if (!string.IsNullOrEmpty(cur.locationId)) return cur.locationId;
                foreach (var p in s.nodes.Where(x => (x.links ?? new Link[0]).Any(l => l.toNodeId == cur.id) || (x.IsJump && x.jumpToNodeId == cur.id))) if (seen.Add(p.id)) q.Enqueue(p);
            }
            return "";
        }
        static string RewardLabel(StoryloomStory s, StoryNode n) { var r = StoryloomDirector.RewardSummary(s, n); return n.title + "\n<size=60%>" + (string.IsNullOrEmpty(n.discoverKind) ? "discoverable" : n.discoverKind) + (string.IsNullOrEmpty(r) ? "" : " · " + r) + "</size>"; }

        /* --- previous single-style CreateScene (kept for reference; the top-down world code below is the same, moved into BuildTopDownWorld) ---
        void CreateScene()
        {
            if (_b.defaultNpcPrefab == null || _b.defaultItemPrefab == null || _b.defaultDiscoverablePrefab == null) CreatePlaceholders();
            var s = _b.story.Story;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var cam = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener)); cam.tag = "MainCamera"; var c = cam.GetComponent<Camera>(); c.orthographic = true; c.orthographicSize = 6; c.backgroundColor = new Color(.16f, .24f, .16f); c.clearFlags = CameraClearFlags.SolidColor; cam.transform.position = new Vector3(0, 0, -10);
            var follow = cam.AddComponent<SimpleFollow>();

            // director
            var dirGo = new GameObject("Storyloom Director"); var d = dirGo.AddComponent<StoryloomDirector>(); d.bindings = _b; d.keys = KeysAsset(); d.persistAcrossScenes = false; d.playStartNodeOnLoad = true;

            // player
            var light = new GameObject("Directional Light", typeof(Light)); var lt = light.GetComponent<Light>(); lt.type = LightType.Directional; lt.intensity = 1.1f; light.transform.rotation = Quaternion.Euler(50, -30, 0);
            var player = Primitive("Player", PrimitiveType.Capsule, new Color(.4f, .7f, 1f), new Vector3(.7f, .7f, .7f)); player.transform.position = new Vector3(0, -2, 0);
            var rb = player.AddComponent<Rigidbody2D>(); rb.gravityScale = 0; rb.freezeRotation = true; rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            var pc = player.AddComponent<CircleCollider2D>(); pc.radius = .4f;
            var ctrl = player.AddComponent<PlayerController2D>(); ctrl.keys = d.keys;
            follow.target = player.transform;

            // ground
            var ground = Primitive("Ground", PrimitiveType.Quad, new Color(.32f, .45f, .28f), new Vector3(60, 40, 1)); ground.transform.position = new Vector3(0, 0, 1f);

            // world objects: one cluster per location, laid out left → right; NPCs whose home is that location, items, discoverables hosted at beats set there
            var locs = (s.locations ?? new Location[0]).ToList(); if (locs.Count == 0) locs.Add(new Location { id = "", name = "The World" });
            float x0 = -(locs.Count - 1) * 6f;
            var placed = new HashSet<string>();
            for (int li = 0; li < locs.Count; li++)
            {
                var loc = locs[li]; var cx = x0 + li * 12f; var root = new GameObject("Location · " + loc.name); root.transform.position = new Vector3(cx, 2, 0);
                var trig = root.AddComponent<BoxCollider2D>(); trig.isTrigger = true; trig.size = new Vector2(11, 12); root.AddComponent<LocationTrigger>().locationId = loc.id;
                var floor = Primitive("Floor", PrimitiveType.Quad, new Color(.2f + .1f * (li % 3), .35f, .3f), new Vector3(11, 12, 1)); floor.transform.SetParent(root.transform); floor.transform.localPosition = new Vector3(0, 0, .5f);
                var sign = Instantiate(_b.defaultDiscoverablePrefab, root.transform); sign.name = "Signpost · " + loc.name; sign.transform.localPosition = new Vector3(0, -5, 0); DestroyImmediate(sign.GetComponent<DiscoverableInteractable>()); var sp = sign.AddComponent<Signpost>(); sp.locationId = loc.id; sp.prompt = sign.transform.Find("Prompt")?.gameObject; var smr = sign.GetComponent<MeshRenderer>(); if (smr) smr.sharedMaterial = Mat(new Color(.75f, .6f, .45f), "signpost"); sign.GetComponentInChildren<TextMesh>().text = loc.name;
                int k = 0;
                foreach (var ch in s.characters ?? new Character[0])
                {
                    bool here = ch.homeLocationId == loc.id || (string.IsNullOrEmpty(ch.homeLocationId) && s.nodes.Any(n => n.locationId == loc.id && (n.speakerId == ch.id || (n.lines != null && n.lines.Any(l => l.speakerId == ch.id)))));
                    if (!here || placed.Contains("c:" + ch.id) || ch.IsProtagonist) continue; placed.Add("c:" + ch.id);
                    var b = _b.Character(ch.id); var go = Instantiate(b != null && b.prefab ? b.prefab : _b.defaultNpcPrefab, root.transform); go.name = "NPC · " + ch.name; go.transform.localPosition = new Vector3(-4 + (k % 5) * 2, 3 - (k / 5) * 2, 0); k++;
                    var npc = go.GetComponent<NpcInteractable>() ?? go.AddComponent<NpcInteractable>(); npc.characterId = ch.id; var tm = go.GetComponentInChildren<TextMesh>(); if (tm) tm.text = ch.name; if (b != null && b.worldSprite && go.GetComponent<SpriteRenderer>()) go.GetComponent<SpriteRenderer>().sprite = b.worldSprite;
                }
                foreach (var it in s.items ?? new Item[0])
                {
                    bool here = !it.startOwned && s.nodes.Any(n => n.locationId == loc.id && ((n.itemIds != null && n.itemIds.Contains(it.id)) || (n.effects != null && n.effects.Any(e => e.variable == "item:" + it.id && e.op == "give"))));
                    if (!here || placed.Contains("i:" + it.id)) continue; placed.Add("i:" + it.id);
                    var b = _b.Item(it.id); var go = Instantiate(b != null && b.prefab ? b.prefab : _b.defaultItemPrefab, root.transform); go.name = "Item · " + it.name; go.transform.localPosition = new Vector3(-4 + (k % 5) * 2, 3 - (k / 5) * 2, 0); k++;
                    var pk = go.GetComponent<ItemPickup>() ?? go.AddComponent<ItemPickup>(); pk.itemId = it.id; var tm = go.GetComponentInChildren<TextMesh>(); if (tm) tm.text = it.name;
                }
                // discoverables: placed at their own / host's / nearest upstream location, labelled with kind and what they reward
                foreach (var dn in s.nodes.Where(n => n.IsDiscoverable))
                {
                    bool here = EffectiveLocation(s, dn) == loc.id;
                    if (!here || placed.Contains("d:" + dn.id)) continue; placed.Add("d:" + dn.id);
                    var b = _b.Discoverable(dn.id); var go = Instantiate(b != null && b.prefab ? b.prefab : _b.defaultDiscoverablePrefab, root.transform); go.name = "Discoverable · " + dn.title; go.transform.localPosition = new Vector3(-4 + (k % 5) * 2, 3 - (k / 5) * 2, 0); k++;
                    var di = go.GetComponent<DiscoverableInteractable>() ?? go.AddComponent<DiscoverableInteractable>(); di.nodeId = dn.id; var tm = go.GetComponentInChildren<TextMesh>(); if (tm) { tm.richText = true; tm.text = RewardLabel(s, dn); di.label = tm; }
                    if (b != null && b.worldSprite && go.GetComponent<SpriteRenderer>()) go.GetComponent<SpriteRenderer>().sprite = b.worldSprite;
                }
            }
            // spawn the player where the story starts (the start node's location cluster), just below its signpost
            { var startLoc = s.StartNode != null ? EffectiveLocation(s, s.StartNode) : ""; int si = locs.FindIndex(l => l.id == startLoc); if (si >= 0) player.transform.position = new Vector3(x0 + si * 12f, -1.5f, 0); }
            // anything unplaced goes in a "Backstage" cluster so nothing is lost
            var back = new GameObject("Backstage (unplaced)"); back.transform.position = new Vector3(0, -9, 0); int bk = 0;
            foreach (var ch in s.characters ?? new Character[0]) { if (placed.Contains("c:" + ch.id) || ch.IsProtagonist) continue; var go = Instantiate(_b.defaultNpcPrefab, back.transform); go.name = "NPC · " + ch.name; go.transform.localPosition = new Vector3(-6 + bk++ * 2, 0, 0); go.GetComponent<NpcInteractable>().characterId = ch.id; go.GetComponentInChildren<TextMesh>().text = ch.name; }
            foreach (var it in s.items ?? new Item[0]) { if (placed.Contains("i:" + it.id) || it.startOwned) continue; var go = Instantiate(_b.defaultItemPrefab, back.transform); go.name = "Item · " + it.name; go.transform.localPosition = new Vector3(-6 + bk++ * 2, 0, 0); go.GetComponent<ItemPickup>().itemId = it.id; go.GetComponentInChildren<TextMesh>().text = it.name; }
            foreach (var dn in s.nodes.Where(n => n.IsDiscoverable)) { if (placed.Contains("d:" + dn.id)) continue; var go = Instantiate(_b.defaultDiscoverablePrefab, back.transform); go.name = "Discoverable · " + dn.title; go.transform.localPosition = new Vector3(-6 + bk++ * 2, 0, 0); var di = go.GetComponent<DiscoverableInteractable>(); di.nodeId = dn.id; var tm = go.GetComponentInChildren<TextMesh>(); if (tm) { tm.richText = true; tm.text = RewardLabel(s, dn); di.label = tm; } }
            if (bk == 0) DestroyImmediate(back);

            // UI
            var canvasGo = new GameObject("Storyloom UI", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster)); var canvas = canvasGo.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; var scaler = canvasGo.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1280, 720);
            // if (Object.FindObjectOfType<UnityEngine.EventSystems.EventSystem>() == null) new GameObject("EventSystem", typeof(UnityEngine.EventSystems.EventSystem), typeof(UnityEngine.EventSystems.StandaloneInputModule));   // legacy input
#if ENABLE_INPUT_SYSTEM
            if (Object.FindObjectOfType<UnityEngine.EventSystems.EventSystem>() == null) new GameObject("EventSystem", typeof(UnityEngine.EventSystems.EventSystem), typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));
#else
            if (Object.FindObjectOfType<UnityEngine.EventSystems.EventSystem>() == null) new GameObject("EventSystem", typeof(UnityEngine.EventSystems.EventSystem), typeof(UnityEngine.EventSystems.StandaloneInputModule));
#endif
            // dialogue box (bottom, Stardew-ish: portrait left, name, body, choices)
            var box = Panel(canvasGo.transform, "Dialogue", new Color(.96f, .9f, .74f, .97f));  Fit(box, new Vector2(.08f, 0), new Vector2(.92f, 0), new Vector2(0, 24), new Vector2(0, 220)); box.GetComponent<Image>().color = new Color(.98f, .93f, .78f, .98f);
            var portrait = Panel(box.transform, "Portrait", Color.white);  Fit(portrait, new Vector2(0, 0), new Vector2(0, 1), new Vector2(14, 14), new Vector2(174, -14)); portrait.GetComponent<Image>().preserveAspect = true;
            var nameT = MakeText(box.transform, "Name", "", 22, TextAnchor.UpperLeft, new Color(.35f, .2f, .05f));  Fit(nameT.gameObject, new Vector2(0, 1), new Vector2(1, 1), new Vector2(190, -44), new Vector2(-20, -12)); nameT.fontStyle = FontStyle.Bold;
            var emoT = MakeText(box.transform, "Emotion", "", 16, TextAnchor.UpperRight, new Color(.5f, .4f, .25f));  Fit(emoT.gameObject, new Vector2(0, 1), new Vector2(1, 1), new Vector2(190, -44), new Vector2(-20, -12));
            var bodyT = MakeText(box.transform, "Body", "", 20, TextAnchor.UpperLeft, new Color(.2f, .12f, .05f));  Fit(bodyT.gameObject, new Vector2(0, 0), new Vector2(1, 1), new Vector2(190, 16), new Vector2(-20, -48)); bodyT.verticalOverflow = VerticalWrapMode.Truncate;
            var promptT = MakeText(box.transform, "Prompt", "▼", 18, TextAnchor.LowerRight, new Color(.5f, .35f, .1f));  Fit(promptT.gameObject, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-40, 8), new Vector2(-12, 30));
            // choices sit in the lower half of the box, full width, below the body text (the body shrinks to the top half while choosing)
            var choices = new GameObject("Choices", typeof(RectTransform), typeof(VerticalLayoutGroup)); choices.transform.SetParent(box.transform, false);  Fit(choices, new Vector2(0, 0), new Vector2(1, .5f), new Vector2(190, 10), new Vector2(-20, 0)); var vl = choices.GetComponent<VerticalLayoutGroup>(); vl.childAlignment = TextAnchor.LowerLeft; vl.spacing = 4; vl.childForceExpandHeight = false; vl.childControlHeight = true; vl.childForceExpandWidth = true; vl.childControlWidth = true;
            var cb = Panel(choices.transform, "ChoiceButton", new Color(1, 1, 1, .6f)); var btn = cb.AddComponent<Button>(); var le = cb.AddComponent<LayoutElement>(); le.preferredHeight = 30; var ct = MakeText(cb.transform, "Text", "Option", 18, TextAnchor.MiddleLeft, new Color(.2f, .12f, .05f));  Fit(ct.gameObject, Vector2.zero, Vector2.one, new Vector2(12, 0), new Vector2(-12, 0)); cb.SetActive(false);
            var dui = canvasGo.AddComponent<DialogueUI>(); dui.panel = box; dui.portrait = portrait.GetComponent<Image>(); dui.nameText = nameT; dui.emotionText = emoT; dui.bodyText = bodyT; dui.promptText = promptT; dui.choicesParent = choices.transform; dui.choiceButtonPrefab = btn; dui.audioSource = canvasGo.AddComponent<AudioSource>();
            // banner (top)
            var ban = Panel(canvasGo.transform, "Location banner", new Color(0, 0, 0, .55f));  Fit(ban, new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(-220, -90), new Vector2(220, -20)); var bg = ban.AddComponent<CanvasGroup>(); bg.alpha = 0;
            var bn = MakeText(ban.transform, "Name", "", 26, TextAnchor.MiddleCenter, Color.white);  Fit(bn.gameObject, Vector2.zero, Vector2.one, new Vector2(10, 22), new Vector2(-10, -4)); bn.fontStyle = FontStyle.Bold;
            var bs = MakeText(ban.transform, "Sub", "", 14, TextAnchor.LowerCenter, new Color(1, .9f, .7f));  Fit(bs.gameObject, Vector2.zero, Vector2.one, new Vector2(10, 4), new Vector2(-10, -38));
            var banner = canvasGo.AddComponent<LocationBanner>(); banner.group = bg; banner.nameText = bn; banner.subText = bs; ban.SetActive(false);
            // toast (top-right)
            var to = Panel(canvasGo.transform, "Pickup toast", new Color(0, 0, 0, .6f));  Fit(to, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-320, -70), new Vector2(-16, -16)); var tg = to.AddComponent<CanvasGroup>();
            var ti = Panel(to.transform, "Icon", Color.white);  Fit(ti, new Vector2(0, 0), new Vector2(0, 1), new Vector2(8, 8), new Vector2(46, -8)); ti.GetComponent<Image>().preserveAspect = true;
            var tt = MakeText(to.transform, "Text", "", 18, TextAnchor.MiddleLeft, Color.white);  Fit(tt.gameObject, Vector2.zero, Vector2.one, new Vector2(56, 0), new Vector2(-8, 0));
            var toast = canvasGo.AddComponent<PickupToast>(); toast.group = tg; toast.text = tt; toast.icon = ti.GetComponent<Image>(); to.SetActive(false);
            // inventory (right)
            var inv = Panel(canvasGo.transform, "Inventory", new Color(.98f, .93f, .78f, .97f));  Fit(inv, new Vector2(1, .5f), new Vector2(1, .5f), new Vector2(-320, -180), new Vector2(-16, 180));
            var ih = MakeText(inv.transform, "Header", "Inventory  (Tab)", 20, TextAnchor.UpperLeft, new Color(.35f, .2f, .05f));  Fit(ih.gameObject, new Vector2(0, 1), new Vector2(1, 1), new Vector2(12, -36), new Vector2(-12, -8)); ih.fontStyle = FontStyle.Bold;
            var list = new GameObject("List", typeof(RectTransform), typeof(VerticalLayoutGroup)); list.transform.SetParent(inv.transform, false);  Fit(list, Vector2.zero, Vector2.one, new Vector2(12, 12), new Vector2(-12, -44)); var ll = list.GetComponent<VerticalLayoutGroup>(); ll.childAlignment = TextAnchor.UpperLeft; ll.spacing = 4; ll.childForceExpandHeight = false; ll.childControlHeight = true; ll.childForceExpandWidth = true; ll.childControlWidth = true;
            var row = new GameObject("Row", typeof(RectTransform)); row.transform.SetParent(list.transform, false); row.AddComponent<LayoutElement>().preferredHeight = 28; var ri = Panel(row.transform, "Icon", Color.white);  Fit(ri, new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, 2), new Vector2(24, -2)); var rt = MakeText(row.transform, "Text", "", 16, TextAnchor.MiddleLeft, new Color(.2f, .12f, .05f));  Fit(rt.gameObject, Vector2.zero, Vector2.one, new Vector2(30, 0), Vector2.zero); row.SetActive(false);
            var hud = canvasGo.AddComponent<InventoryHUD>(); hud.panel = inv; hud.listParent = list.transform; hud.rowPrefab = row; inv.SetActive(false);
            // story map overlay (hold M): full-screen dim, two columns — here / next on the left, recent / endings / progress on the right
            var mapGo = Panel(canvasGo.transform, "Story map", new Color(.06f, .05f, .04f, .88f));  Fit(mapGo, Vector2.zero, Vector2.one, new Vector2(40, 40), new Vector2(-40, -40)); var mg = mapGo.AddComponent<CanvasGroup>(); mg.alpha = 0; mg.blocksRaycasts = false;
            var mTitle = MakeText(mapGo.transform, "Title", "", 24, TextAnchor.UpperLeft, new Color(1, .9f, .7f));  Fit(mTitle.gameObject, new Vector2(0, 1), new Vector2(1, 1), new Vector2(24, -50), new Vector2(-24, -14)); mTitle.fontStyle = FontStyle.Bold;
            var mHereH = MakeText(mapGo.transform, "HereH", "WHERE YOU ARE", 13, TextAnchor.UpperLeft, new Color(1, .8f, .5f));  Fit(mHereH.gameObject, new Vector2(0, 1), new Vector2(.55f, 1), new Vector2(24, -74), new Vector2(-12, -56));
            var mHere = MakeText(mapGo.transform, "Here", "", 17, TextAnchor.UpperLeft, Color.white);  Fit(mHere.gameObject, new Vector2(0, 1), new Vector2(.55f, 1), new Vector2(24, -170), new Vector2(-12, -76));
            var mNextH = MakeText(mapGo.transform, "NextH", "WHAT'S NEXT", 13, TextAnchor.UpperLeft, new Color(1, .8f, .5f));  Fit(mNextH.gameObject, new Vector2(0, 1), new Vector2(.55f, 1), new Vector2(24, -196), new Vector2(-12, -178));
            var mNext = MakeText(mapGo.transform, "Next", "", 15, TextAnchor.UpperLeft, Color.white);  Fit(mNext.gameObject, new Vector2(0, 0), new Vector2(.55f, 1), new Vector2(24, 40), new Vector2(-12, -200)); mNext.verticalOverflow = VerticalWrapMode.Truncate;
            var mRecH = MakeText(mapGo.transform, "RecentH", "RECENTLY", 13, TextAnchor.UpperLeft, new Color(1, .8f, .5f));  Fit(mRecH.gameObject, new Vector2(.58f, 1), new Vector2(1, 1), new Vector2(0, -74), new Vector2(-24, -56));
            var mRec = MakeText(mapGo.transform, "Recent", "", 15, TextAnchor.UpperLeft, new Color(.85f, .85f, .85f));  Fit(mRec.gameObject, new Vector2(.58f, 1), new Vector2(1, 1), new Vector2(0, -130), new Vector2(-24, -76));
            var mEndH = MakeText(mapGo.transform, "EndingsH", "ENDINGS", 13, TextAnchor.UpperLeft, new Color(1, .8f, .5f));  Fit(mEndH.gameObject, new Vector2(.58f, 1), new Vector2(1, 1), new Vector2(0, -156), new Vector2(-24, -138));
            var mEnd = MakeText(mapGo.transform, "Endings", "", 15, TextAnchor.UpperLeft, Color.white);  Fit(mEnd.gameObject, new Vector2(.58f, 0), new Vector2(1, 1), new Vector2(0, 40), new Vector2(-24, -160));
            var mProg = MakeText(mapGo.transform, "Progress", "", 16, TextAnchor.LowerLeft, new Color(1, .9f, .7f));  Fit(mProg.gameObject, new Vector2(0, 0), new Vector2(1, 0), new Vector2(24, 12), new Vector2(-24, 36));
            var map = canvasGo.AddComponent<StoryMapUI>(); map.group = mg; map.titleText = mTitle; map.hereText = mHere; map.nextText = mNext; map.recentText = mRec; map.endingsText = mEnd; map.progressText = mProg;
            var help = MakeText(canvasGo.transform, "Help", d.keys.HelpLine(style), 14, TextAnchor.LowerLeft, new Color(1, 1, 1, .8f)); canvasGo.AddComponent<HelpLine>().text = help;  Fit(help.gameObject, new Vector2(0, 0), new Vector2(1, 0), new Vector2(12, 6), new Vector2(-12, 26));

            d.dialogue = dui; d.banner = banner; d.toast = toast; d.inventoryHud = hud; d.map = map;
            dirGo.AddComponent<StoryloomDebugHud>();
            Directory.CreateDirectory(Root + "/Scenes"); var scenePath = $"{Root}/Scenes/{s.name} (Stardew kit).unity"; EditorSceneManager.SaveScene(scene, scenePath);
            ShowNotification(new GUIContent("Scene created — press Play")); Selection.activeObject = dirGo;
        }

        --- */
        void CreateScene() => CreateScene(_b.gameStyle);
        void CreateScene(GameStyle style)
        {
            CreatePlaceholders(style);   // (re)creates the defaults for this style; leaves your own prefabs alone
            var s = _b.story.Story;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var dirGo = new GameObject("Storyloom Director"); var d = dirGo.AddComponent<StoryloomDirector>(); d.bindings = _b; d.keys = KeysAsset(); d.persistAcrossScenes = false; d.playStartNodeOnLoad = true;
            var light = new GameObject("Directional Light", typeof(Light)); var lt = light.GetComponent<Light>(); lt.type = LightType.Directional; lt.intensity = 1.1f; light.transform.rotation = Quaternion.Euler(50, -30, 0);
            if (style == GameStyle.TopDown) BuildTopDownWorld(s, d); else Build3DWorld(s, d, style);
            MatchPropColliders(style != GameStyle.TopDown);   // bound prefabs made for the other style carry the wrong collider kind
            StampEntityAssets();                              // reference the typed entity handles, not just bare ids (no-op if none generated)
            BuildUI(d, style);
            dirGo.AddComponent<StoryloomDebugHud>();
            Directory.CreateDirectory(Root + "/Scenes");
            var suffix = style == GameStyle.TopDown ? "Stardew kit" : style == GameStyle.ThirdPerson ? "Third person kit" : "First person kit";
            var scenePath = $"{Root}/Scenes/{s.name} ({suffix}).unity"; EditorSceneManager.SaveScene(scene, scenePath);
            ShowNotification(new GUIContent("Scene created — press Play")); Selection.activeObject = dirGo;
        }

        // ---- top-down (Stardew-style): XY plane, orthographic camera looking down -z, 2D physics
        const float Lane = 18f;   // distance between location clusters (zones are 11 wide → 7 units of open ground between them)
        void BuildTopDownWorld(StoryloomStory s, StoryloomDirector d)
        {
            var cam = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener)); cam.tag = "MainCamera"; var c = cam.GetComponent<Camera>(); c.orthographic = true; c.orthographicSize = 6; c.backgroundColor = new Color(.16f, .24f, .16f); c.clearFlags = CameraClearFlags.SolidColor; cam.transform.position = new Vector3(0, 0, -10);
            var follow = cam.AddComponent<SimpleFollow>();
            var player = Primitive("Player", PrimitiveType.Capsule, new Color(.4f, .7f, 1f), new Vector3(.7f, .7f, .7f)); player.transform.position = new Vector3(0, -2, 0);
            var rb = player.AddComponent<Rigidbody2D>(); rb.gravityScale = 0; rb.freezeRotation = true; rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous; rb.sleepMode = RigidbodySleepMode2D.NeverSleep; rb.interpolation = RigidbodyInterpolation2D.Interpolate;   // a sleeping body stops firing zone triggers
            var pc = player.AddComponent<CircleCollider2D>(); pc.radius = .4f;
            var ctrl = player.AddComponent<PlayerController2D>(); ctrl.keys = d.keys;
            follow.target = player.transform;
            var ground = Primitive("Ground", PrimitiveType.Quad, new Color(.32f, .45f, .28f), new Vector3(Mathf.Max(60, (s.locations?.Length ?? 1) * Lane + 30), 40, 1)); ground.transform.position = new Vector3(0, 0, 1f);
            // world objects: one cluster per location, laid out left → right; NPCs whose home is that location, items, discoverables hosted at beats set there
            var locs = (s.locations ?? new Location[0]).ToList(); if (locs.Count == 0) locs.Add(new Location { id = "", name = "The World" });
            float x0 = -(locs.Count - 1) * (Lane / 2f);
            var placed = new HashSet<string>();
            for (int li = 0; li < locs.Count; li++)
            {
                var loc = locs[li]; var cx = x0 + li * Lane; var root = new GameObject("Location · " + loc.name); root.transform.position = new Vector3(cx, 2, 0);
                var trig = root.AddComponent<BoxCollider2D>(); trig.isTrigger = true; trig.size = new Vector2(11, 12); root.AddComponent<LocationTrigger>().locationId = loc.id;
                var floor = Primitive("Floor", PrimitiveType.Quad, new Color(.2f + .1f * (li % 3), .35f, .3f), new Vector3(11, 12, 1)); floor.transform.SetParent(root.transform); floor.transform.localPosition = new Vector3(0, 0, .5f);
                var sign = Instantiate(_b.defaultDiscoverablePrefab, root.transform); sign.name = "Signpost · " + loc.name; sign.transform.localPosition = new Vector3(0, -5, 0); DestroyImmediate(sign.GetComponent<DiscoverableInteractable>()); var sp = sign.AddComponent<Signpost>(); sp.locationId = loc.id; sp.prompt = sign.transform.Find("Prompt")?.gameObject; var smr = sign.GetComponent<MeshRenderer>(); if (smr) smr.sharedMaterial = Mat(new Color(.75f, .6f, .45f), "signpost"); sign.GetComponentInChildren<TextMesh>().text = loc.name;
                int k = 0;
                foreach (var ch in s.characters ?? new Character[0])
                {
                    bool here = ch.homeLocationId == loc.id || (string.IsNullOrEmpty(ch.homeLocationId) && s.nodes.Any(n => n.locationId == loc.id && (n.speakerId == ch.id || (n.lines != null && n.lines.Any(l => l.speakerId == ch.id)))));
                    if (!here || placed.Contains("c:" + ch.id) || ch.IsProtagonist) continue; placed.Add("c:" + ch.id);
                    var b = _b.Character(ch.id); var go = Instantiate(b != null && b.prefab ? b.prefab : _b.defaultNpcPrefab, root.transform); go.name = "NPC · " + ch.name; go.transform.localPosition = new Vector3(-4 + (k % 5) * 2, 3 - (k / 5) * 2, 0); k++;
                    var npc = go.GetComponent<NpcInteractable>() ?? go.AddComponent<NpcInteractable>(); npc.characterId = ch.id; var tm = go.GetComponentInChildren<TextMesh>(); if (tm) tm.text = ch.name; if (b != null && b.worldSprite && go.GetComponent<SpriteRenderer>()) go.GetComponent<SpriteRenderer>().sprite = b.worldSprite;
                }
                foreach (var it in s.items ?? new Item[0])
                {
                    bool here = !it.startOwned && s.nodes.Any(n => n.locationId == loc.id && ((n.itemIds != null && n.itemIds.Contains(it.id)) || (n.effects != null && n.effects.Any(e => e.variable == "item:" + it.id && e.op == "give"))));
                    if (!here || placed.Contains("i:" + it.id)) continue; placed.Add("i:" + it.id);
                    var b = _b.Item(it.id); var go = Instantiate(b != null && b.prefab ? b.prefab : _b.defaultItemPrefab, root.transform); go.name = "Item · " + it.name; go.transform.localPosition = new Vector3(-4 + (k % 5) * 2, 3 - (k / 5) * 2, 0); k++;
                    var pk = go.GetComponent<ItemPickup>() ?? go.AddComponent<ItemPickup>(); pk.itemId = it.id; var tm = go.GetComponentInChildren<TextMesh>(); if (tm) tm.text = it.name;
                }
                // discoverables: placed at their own / host's / nearest upstream location, labelled with kind and what they reward
                foreach (var dn in s.nodes.Where(n => n.IsDiscoverable))
                {
                    bool here = EffectiveLocation(s, dn) == loc.id;
                    if (!here || placed.Contains("d:" + dn.id)) continue; placed.Add("d:" + dn.id);
                    var b = _b.Discoverable(dn.id); var go = Instantiate(b != null && b.prefab ? b.prefab : _b.defaultDiscoverablePrefab, root.transform); go.name = "Discoverable · " + dn.title; go.transform.localPosition = new Vector3(-4 + (k % 5) * 2, 3 - (k / 5) * 2, 0); k++;
                    var di = go.GetComponent<DiscoverableInteractable>() ?? go.AddComponent<DiscoverableInteractable>(); di.nodeId = dn.id; var tm = go.GetComponentInChildren<TextMesh>(); if (tm) { tm.richText = true; tm.text = RewardLabel(s, dn); di.label = tm; }
                    if (b != null && b.worldSprite && go.GetComponent<SpriteRenderer>()) go.GetComponent<SpriteRenderer>().sprite = b.worldSprite;
                }
            }
            // spawn the player where the story starts (the start node's location cluster), just below its signpost
            { var startLoc = s.StartNode != null ? EffectiveLocation(s, s.StartNode) : ""; int si = locs.FindIndex(l => l.id == startLoc); if (si >= 0) player.transform.position = new Vector3(x0 + si * Lane, -1.5f, 0); }
            // anything unplaced goes in a "Backstage" cluster so nothing is lost
            var back = new GameObject("Backstage (unplaced)"); back.transform.position = new Vector3(0, -9, 0); int bk = 0;
            foreach (var ch in s.characters ?? new Character[0]) { if (placed.Contains("c:" + ch.id) || ch.IsProtagonist) continue; var go = Instantiate(_b.defaultNpcPrefab, back.transform); go.name = "NPC · " + ch.name; go.transform.localPosition = new Vector3(-6 + bk++ * 2, 0, 0); go.GetComponent<NpcInteractable>().characterId = ch.id; go.GetComponentInChildren<TextMesh>().text = ch.name; }
            foreach (var it in s.items ?? new Item[0]) { if (placed.Contains("i:" + it.id) || it.startOwned) continue; var go = Instantiate(_b.defaultItemPrefab, back.transform); go.name = "Item · " + it.name; go.transform.localPosition = new Vector3(-6 + bk++ * 2, 0, 0); go.GetComponent<ItemPickup>().itemId = it.id; go.GetComponentInChildren<TextMesh>().text = it.name; }
            foreach (var dn in s.nodes.Where(n => n.IsDiscoverable)) { if (placed.Contains("d:" + dn.id)) continue; var go = Instantiate(_b.defaultDiscoverablePrefab, back.transform); go.name = "Discoverable · " + dn.title; go.transform.localPosition = new Vector3(-6 + bk++ * 2, 0, 0); var di = go.GetComponent<DiscoverableInteractable>(); di.nodeId = dn.id; var tm = go.GetComponentInChildren<TextMesh>(); if (tm) { tm.richText = true; tm.text = RewardLabel(s, dn); di.label = tm; } }
            if (bk == 0) DestroyImmediate(back);
        }

        // ---- third / first person: XZ plane (y up), 3D physics, CharacterController. Same clusters and objects as top-down, laid on the ground.
        void Build3DWorld(StoryloomStory s, StoryloomDirector d, GameStyle style)
        {
            const float Y = 0.6f;   // object centres sit on the ground
            // player: capsule body with a CharacterController; the visual is a child so it can turn without affecting the controller
            var player = new GameObject("Player"); player.transform.position = new Vector3(0, 1.0f, -2);
            var cc = player.AddComponent<CharacterController>(); cc.height = 1.6f; cc.radius = .35f; cc.center = new Vector3(0, .8f, 0);
            var body = Primitive3D("Body", PrimitiveType.Capsule, new Color(.4f, .7f, 1f), new Vector3(.7f, .8f, .7f)); DestroyImmediate(body.GetComponent<Collider>()); body.transform.SetParent(player.transform, false); body.transform.localPosition = new Vector3(0, .8f, 0);
            var nose = Primitive3D("Nose", PrimitiveType.Cube, new Color(.2f, .4f, .8f), new Vector3(.2f, .2f, .2f)); DestroyImmediate(nose.GetComponent<Collider>()); nose.transform.SetParent(body.transform, false); nose.transform.localPosition = new Vector3(0, .6f, .5f);   // shows which way the player faces
            var cam = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener)); cam.tag = "MainCamera"; var c = cam.GetComponent<Camera>(); c.backgroundColor = new Color(.55f, .7f, .9f); c.clearFlags = CameraClearFlags.Skybox; c.nearClipPlane = .05f;
            if (style == GameStyle.ThirdPerson)
            {
                var ctrl = player.AddComponent<PlayerController3D>(); ctrl.keys = d.keys; ctrl.visual = body.transform; ctrl.cameraTransform = cam.transform;
                cam.transform.position = player.transform.position + new Vector3(0, 3, -5); var orbit = cam.AddComponent<ThirdPersonCamera>(); orbit.target = player.transform; orbit.keys = d.keys; c.fieldOfView = 60;
            }
            else
            {
                body.GetComponent<MeshRenderer>().enabled = false; nose.GetComponent<MeshRenderer>().enabled = false;   // you can't see yourself in first person
                var head = new GameObject("Head"); head.transform.SetParent(player.transform, false); head.transform.localPosition = new Vector3(0, 1.45f, 0);
                cam.transform.SetParent(head.transform, false); c.fieldOfView = 70;
                var ctrl = player.AddComponent<FirstPersonController>(); ctrl.keys = d.keys; ctrl.head = head.transform;
            }
            var cl = player.AddComponent<CursorLock>(); cl.keys = d.keys;
            // ground (a Plane primitive is 10×10, keeps its MeshCollider so the CharacterController can stand on it)
            var ground = Primitive3D("Ground", PrimitiveType.Plane, new Color(.32f, .45f, .28f), new Vector3(Mathf.Max(6, ((s.locations?.Length ?? 1) * Lane + 30) / 10f), 1, 4)); ground.transform.position = new Vector3(0, -0.01f, 0);
            var locs = (s.locations ?? new Location[0]).ToList(); if (locs.Count == 0) locs.Add(new Location { id = "", name = "The World" });
            float x0 = -(locs.Count - 1) * (Lane / 2f);
            var placed = new HashSet<string>();
            for (int li = 0; li < locs.Count; li++)
            {
                var loc = locs[li]; var cx = x0 + li * Lane; var root = new GameObject("Location · " + loc.name); root.transform.position = new Vector3(cx, 0, 2);
                // zone: a trigger volume on its own object (Ignore Raycast layer so the first-person look ray and the interaction ray never hit it;
                // kinematic rigidbody so trigger enter/exit fires reliably against the CharacterController)
                var zone = new GameObject("Zone"); zone.transform.SetParent(root.transform, false); zone.layer = 2;
                var trig = zone.AddComponent<BoxCollider>(); trig.isTrigger = true; trig.size = new Vector3(11, 4, 12); trig.center = new Vector3(0, 2, 0);
                var zrb = zone.AddComponent<Rigidbody>(); zrb.isKinematic = true; zrb.useGravity = false;
                zone.AddComponent<LocationTrigger>().locationId = loc.id;
                var floor = Primitive3D("Floor", PrimitiveType.Plane, new Color(.2f + .1f * (li % 3), .35f, .3f), new Vector3(1.1f, 1, 1.2f)); DestroyImmediate(floor.GetComponent<Collider>()); floor.transform.SetParent(root.transform); floor.transform.localPosition = new Vector3(0, 0.005f, 0);
                var sign = Instantiate(_b.defaultDiscoverablePrefab, root.transform); sign.name = "Signpost · " + loc.name; sign.transform.localPosition = new Vector3(0, Y, -5); DestroyImmediate(sign.GetComponent<DiscoverableInteractable>()); var sp = sign.AddComponent<Signpost>(); sp.locationId = loc.id; sp.prompt = sign.transform.Find("Prompt")?.gameObject; var smr = sign.GetComponent<MeshRenderer>(); if (smr) smr.sharedMaterial = Mat(new Color(.75f, .6f, .45f), "signpost"); sign.GetComponentInChildren<TextMesh>().text = loc.name;
                int k = 0;
                Vector3 Slot() { var v = new Vector3(-4 + (k % 5) * 2, Y, 3 - (k / 5) * 2); k++; return v; }
                foreach (var ch in s.characters ?? new Character[0])
                {
                    bool here = ch.homeLocationId == loc.id || (string.IsNullOrEmpty(ch.homeLocationId) && s.nodes.Any(n => n.locationId == loc.id && (n.speakerId == ch.id || (n.lines != null && n.lines.Any(l => l.speakerId == ch.id)))));
                    if (!here || placed.Contains("c:" + ch.id) || ch.IsProtagonist) continue; placed.Add("c:" + ch.id);
                    var b = _b.Character(ch.id); var go = Instantiate(b != null && b.prefab ? b.prefab : _b.defaultNpcPrefab, root.transform); go.name = "NPC · " + ch.name; go.transform.localPosition = Slot();
                    var npc = go.GetComponent<NpcInteractable>() ?? go.AddComponent<NpcInteractable>(); npc.characterId = ch.id; var tm = go.GetComponentInChildren<TextMesh>(); if (tm) tm.text = ch.name;
                }
                foreach (var it in s.items ?? new Item[0])
                {
                    bool here = !it.startOwned && s.nodes.Any(n => n.locationId == loc.id && ((n.itemIds != null && n.itemIds.Contains(it.id)) || (n.effects != null && n.effects.Any(e => e.variable == "item:" + it.id && e.op == "give"))));
                    if (!here || placed.Contains("i:" + it.id)) continue; placed.Add("i:" + it.id);
                    var b = _b.Item(it.id); var go = Instantiate(b != null && b.prefab ? b.prefab : _b.defaultItemPrefab, root.transform); go.name = "Item · " + it.name; go.transform.localPosition = Slot();
                    var pk = go.GetComponent<ItemPickup>() ?? go.AddComponent<ItemPickup>(); pk.itemId = it.id; var tm = go.GetComponentInChildren<TextMesh>(); if (tm) tm.text = it.name;
                }
                foreach (var dn in s.nodes.Where(n => n.IsDiscoverable))
                {
                    bool here = EffectiveLocation(s, dn) == loc.id;
                    if (!here || placed.Contains("d:" + dn.id)) continue; placed.Add("d:" + dn.id);
                    var b = _b.Discoverable(dn.id); var go = Instantiate(b != null && b.prefab ? b.prefab : _b.defaultDiscoverablePrefab, root.transform); go.name = "Discoverable · " + dn.title; go.transform.localPosition = Slot();
                    var di = go.GetComponent<DiscoverableInteractable>() ?? go.AddComponent<DiscoverableInteractable>(); di.nodeId = dn.id; var tm = go.GetComponentInChildren<TextMesh>(); if (tm) { tm.richText = true; tm.text = RewardLabel(s, dn); di.label = tm; }
                }
                // a low wall segment on each side so clusters read as places (no collider — walk through)
                for (int w = 0; w < 2; w++) { var wall = Primitive3D("Edge", PrimitiveType.Cube, new Color(.5f, .42f, .3f), new Vector3(.3f, .6f, 12)); DestroyImmediate(wall.GetComponent<Collider>()); wall.transform.SetParent(root.transform); wall.transform.localPosition = new Vector3(w == 0 ? -5.5f : 5.5f, .3f, 0); }
            }
            { var startLoc = s.StartNode != null ? EffectiveLocation(s, s.StartNode) : ""; int si = locs.FindIndex(l => l.id == startLoc); if (si >= 0) player.transform.position = new Vector3(x0 + si * Lane, 1.0f, -1.5f); }
            var back = new GameObject("Backstage (unplaced)"); back.transform.position = new Vector3(0, 0, -9); int bk = 0;
            foreach (var ch in s.characters ?? new Character[0]) { if (placed.Contains("c:" + ch.id) || ch.IsProtagonist) continue; var go = Instantiate(_b.defaultNpcPrefab, back.transform); go.name = "NPC · " + ch.name; go.transform.localPosition = new Vector3(-6 + bk++ * 2, Y, 0); go.GetComponent<NpcInteractable>().characterId = ch.id; go.GetComponentInChildren<TextMesh>().text = ch.name; }
            foreach (var it in s.items ?? new Item[0]) { if (placed.Contains("i:" + it.id) || it.startOwned) continue; var go = Instantiate(_b.defaultItemPrefab, back.transform); go.name = "Item · " + it.name; go.transform.localPosition = new Vector3(-6 + bk++ * 2, Y, 0); go.GetComponent<ItemPickup>().itemId = it.id; go.GetComponentInChildren<TextMesh>().text = it.name; }
            foreach (var dn in s.nodes.Where(n => n.IsDiscoverable)) { if (placed.Contains("d:" + dn.id)) continue; var go = Instantiate(_b.defaultDiscoverablePrefab, back.transform); go.name = "Discoverable · " + dn.title; go.transform.localPosition = new Vector3(-6 + bk++ * 2, Y, 0); var di = go.GetComponent<DiscoverableInteractable>(); di.nodeId = dn.id; var tm = go.GetComponentInChildren<TextMesh>(); if (tm) { tm.richText = true; tm.text = RewardLabel(s, dn); di.label = tm; } }
            if (bk == 0) DestroyImmediate(back);
        }

        // Only the three *default* placeholder prefabs are swapped when the style changes — a prefab you bound to a particular
        // character / item / discoverable is left alone, by design. That means a character bound to a 3D placeholder lands in a
        // top-down scene carrying a CapsuleCollider, and Unity refuses to put a BoxCollider2D beside it (AddComponent logs
        // "conflicts with the existing …" and returns null, which the old repair pass then dereferenced). Reconcile every prop the
        // generator placed, so a scene is never built inconsistent in the first place.
        static int MatchPropColliders(bool xz)
        {
            var swapped = new List<string>();
            foreach (var go in Object.FindObjectsOfType<GameObject>(true))
            {
                if (go.scene != SceneManager.GetActiveScene()) continue;
                if (!(go.name.StartsWith("NPC · ") || go.name.StartsWith("Item · ") || go.name.StartsWith("Discoverable · ") || go.name.StartsWith("Signpost · "))) continue;
                if (StoryloomColliders.MatchPlane(go, xz)) swapped.Add(go.name);
            }
            if (swapped.Count > 0) Debug.Log($"Storyloom: gave {swapped.Count} object(s) the {(xz ? "3D" : "2D")} collider this style needs — their bound prefab was made for the other style: " + string.Join(", ", swapped));
            return swapped.Count;
        }

        // ---- UI (shared by every style). Each piece is created only if missing, so "Repair open scene" can rebuild the UI of an
        // older generated scene (missing InventoryHUD / PickupToast were why Tab and pickup popups silently did nothing).
        void BuildUI(StoryloomDirector d, GameStyle style) => EnsureUI(d, style);
        void EnsureUI(StoryloomDirector d, GameStyle style)
        {
            var canvasGo = Object.FindObjectOfType<Canvas>()?.gameObject;
            if (canvasGo == null)
            {
                canvasGo = new GameObject("Storyloom UI", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster)); var canvas = canvasGo.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; var scaler = canvasGo.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1280, 720);
            }
#if ENABLE_INPUT_SYSTEM
            if (Object.FindObjectOfType<UnityEngine.EventSystems.EventSystem>() == null) new GameObject("EventSystem", typeof(UnityEngine.EventSystems.EventSystem), typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));
#else
            if (Object.FindObjectOfType<UnityEngine.EventSystems.EventSystem>() == null) new GameObject("EventSystem", typeof(UnityEngine.EventSystems.EventSystem), typeof(UnityEngine.EventSystems.StandaloneInputModule));
#endif
            d.dialogue = Object.FindObjectOfType<DialogueUI>(true) ?? BuildDialogue(canvasGo);
            d.banner = Object.FindObjectOfType<LocationBanner>(true) ?? BuildBanner(canvasGo);
            if (d.banner && d.banner.descText == null) UpgradeBanner(d.banner);   // pre-0.4.5 banner: add the description line
            d.toast = Object.FindObjectOfType<PickupToast>(true) ?? BuildToast(canvasGo);
            d.inventoryHud = Object.FindObjectOfType<InventoryHUD>(true) ?? BuildInventory(canvasGo);
            d.map = Object.FindObjectOfType<StoryMapUI>(true) ?? BuildMap(canvasGo);
            if (Object.FindObjectOfType<HelpLine>(true) == null) { var help = MakeText(canvasGo.transform, "Help", d.keys ? d.keys.HelpLine(style) : "", 14, TextAnchor.LowerLeft, new Color(1, 1, 1, .8f)); canvasGo.AddComponent<HelpLine>().text = help; Fit(help.gameObject, new Vector2(0, 0), new Vector2(1, 0), new Vector2(12, 6), new Vector2(-12, 26)); }
            if (style == GameStyle.FirstPerson && Object.FindObjectOfType<Crosshair>(true) == null) { var dotGo = Panel(canvasGo.transform, "Crosshair", new Color(1, 1, 1, .5f)); Fit(dotGo, new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(-3, -3), new Vector2(3, 3)); dotGo.GetComponent<Image>().raycastTarget = false; canvasGo.AddComponent<Crosshair>().dot = dotGo.GetComponent<Image>(); }
            EditorUtility.SetDirty(d);
        }
        DialogueUI BuildDialogue(GameObject canvasGo)
        {
            var box = Panel(canvasGo.transform, "Dialogue", new Color(.96f, .9f, .74f, .97f));  Fit(box, new Vector2(.08f, 0), new Vector2(.92f, 0), new Vector2(0, 24), new Vector2(0, 220)); box.GetComponent<Image>().color = new Color(.98f, .93f, .78f, .98f);
            var portrait = Panel(box.transform, "Portrait", Color.white);  Fit(portrait, new Vector2(0, 0), new Vector2(0, 1), new Vector2(14, 14), new Vector2(174, -14)); portrait.GetComponent<Image>().preserveAspect = true;
            var nameT = MakeText(box.transform, "Name", "", 22, TextAnchor.UpperLeft, new Color(.35f, .2f, .05f));  Fit(nameT.gameObject, new Vector2(0, 1), new Vector2(1, 1), new Vector2(190, -44), new Vector2(-20, -12)); nameT.fontStyle = FontStyle.Bold;
            var emoT = MakeText(box.transform, "Emotion", "", 16, TextAnchor.UpperRight, new Color(.5f, .4f, .25f));  Fit(emoT.gameObject, new Vector2(0, 1), new Vector2(1, 1), new Vector2(190, -44), new Vector2(-20, -12));
            var bodyT = MakeText(box.transform, "Body", "", 20, TextAnchor.UpperLeft, new Color(.2f, .12f, .05f));  Fit(bodyT.gameObject, new Vector2(0, 0), new Vector2(1, 1), new Vector2(190, 16), new Vector2(-20, -48)); bodyT.verticalOverflow = VerticalWrapMode.Truncate;
            var promptT = MakeText(box.transform, "Prompt", "▼", 18, TextAnchor.LowerRight, new Color(.5f, .35f, .1f));  Fit(promptT.gameObject, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-40, 8), new Vector2(-12, 30));
            var choices = new GameObject("Choices", typeof(RectTransform), typeof(VerticalLayoutGroup)); choices.transform.SetParent(box.transform, false);  Fit(choices, new Vector2(0, 0), new Vector2(1, .5f), new Vector2(190, 10), new Vector2(-20, 0)); var vl = choices.GetComponent<VerticalLayoutGroup>(); vl.childAlignment = TextAnchor.LowerLeft; vl.spacing = 4; vl.childForceExpandHeight = false; vl.childControlHeight = true; vl.childForceExpandWidth = true; vl.childControlWidth = true;
            var cb = Panel(choices.transform, "ChoiceButton", new Color(1, 1, 1, .6f)); var btn = cb.AddComponent<Button>(); var le = cb.AddComponent<LayoutElement>(); le.preferredHeight = 30; var ct = MakeText(cb.transform, "Text", "Option", 18, TextAnchor.MiddleLeft, new Color(.2f, .12f, .05f));  Fit(ct.gameObject, Vector2.zero, Vector2.one, new Vector2(12, 0), new Vector2(-12, 0)); cb.SetActive(false);
            var dui = canvasGo.AddComponent<DialogueUI>(); dui.panel = box; dui.portrait = portrait.GetComponent<Image>(); dui.nameText = nameT; dui.emotionText = emoT; dui.bodyText = bodyT; dui.promptText = promptT; dui.choicesParent = choices.transform; dui.choiceButtonPrefab = btn; dui.audioSource = canvasGo.AddComponent<AudioSource>();
            box.SetActive(false); return dui;
        }
        // Zone popup: a strip across the top — name, region, and the location's description; holds, then fades (LocationBanner does the timing).
        LocationBanner BuildBanner(GameObject canvasGo)
        {
            var ban = Panel(canvasGo.transform, "Location banner", new Color(0, 0, 0, .62f));  Fit(ban, new Vector2(.12f, 1), new Vector2(.88f, 1), new Vector2(0, -132), new Vector2(0, -16)); var bg = ban.AddComponent<CanvasGroup>(); bg.alpha = 0;
            var bn = MakeText(ban.transform, "Name", "", 26, TextAnchor.UpperCenter, Color.white);  Fit(bn.gameObject, new Vector2(0, 1), new Vector2(1, 1), new Vector2(12, -40), new Vector2(-12, -6)); bn.fontStyle = FontStyle.Bold;
            var bs = MakeText(ban.transform, "Sub", "", 13, TextAnchor.UpperCenter, new Color(1, .9f, .7f));  Fit(bs.gameObject, new Vector2(0, 1), new Vector2(1, 1), new Vector2(12, -58), new Vector2(-12, -42));
            var bd = MakeText(ban.transform, "Description", "", 16, TextAnchor.UpperCenter, new Color(.94f, .92f, .86f));  Fit(bd.gameObject, new Vector2(0, 0), new Vector2(1, 1), new Vector2(18, 8), new Vector2(-18, -60));
            var banner = canvasGo.AddComponent<LocationBanner>(); banner.group = bg; banner.nameText = bn; banner.subText = bs; banner.descText = bd; banner.hold = 3f; ban.SetActive(false); return banner;
        }
        void UpgradeBanner(LocationBanner banner)
        {
            var root = banner.group ? banner.group.gameObject : null; if (!root) return;
            Fit(root, new Vector2(.12f, 1), new Vector2(.88f, 1), new Vector2(0, -132), new Vector2(0, -16));
            var bd = MakeText(root.transform, "Description", "", 16, TextAnchor.UpperCenter, new Color(.94f, .92f, .86f));  Fit(bd.gameObject, new Vector2(0, 0), new Vector2(1, 1), new Vector2(18, 8), new Vector2(-18, -60));
            if (banner.nameText) { banner.nameText.alignment = TextAnchor.UpperCenter; Fit(banner.nameText.gameObject, new Vector2(0, 1), new Vector2(1, 1), new Vector2(12, -40), new Vector2(-12, -6)); }
            if (banner.subText) { banner.subText.alignment = TextAnchor.UpperCenter; Fit(banner.subText.gameObject, new Vector2(0, 1), new Vector2(1, 1), new Vector2(12, -58), new Vector2(-12, -42)); }
            banner.descText = bd; banner.hold = 3f; EditorUtility.SetDirty(banner);
        }
        PickupToast BuildToast(GameObject canvasGo)
        {
            var to = Panel(canvasGo.transform, "Pickup toast", new Color(0, 0, 0, .6f));  Fit(to, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-320, -70), new Vector2(-16, -16)); var tg = to.AddComponent<CanvasGroup>();
            var ti = Panel(to.transform, "Icon", Color.white);  Fit(ti, new Vector2(0, 0), new Vector2(0, 1), new Vector2(8, 8), new Vector2(46, -8)); ti.GetComponent<Image>().preserveAspect = true;
            var tt = MakeText(to.transform, "Text", "", 18, TextAnchor.MiddleLeft, Color.white);  Fit(tt.gameObject, Vector2.zero, Vector2.one, new Vector2(56, 0), new Vector2(-8, 0));
            var toast = canvasGo.AddComponent<PickupToast>(); toast.group = tg; toast.text = tt; toast.icon = ti.GetComponent<Image>(); to.SetActive(false); return toast;
        }
        InventoryHUD BuildInventory(GameObject canvasGo)
        {
            var inv = Panel(canvasGo.transform, "Inventory", new Color(.98f, .93f, .78f, .97f));  Fit(inv, new Vector2(1, .5f), new Vector2(1, .5f), new Vector2(-320, -180), new Vector2(-16, 180));
            var ih = MakeText(inv.transform, "Header", "Inventory  (Tab)", 20, TextAnchor.UpperLeft, new Color(.35f, .2f, .05f));  Fit(ih.gameObject, new Vector2(0, 1), new Vector2(1, 1), new Vector2(12, -36), new Vector2(-12, -8)); ih.fontStyle = FontStyle.Bold;
            var list = new GameObject("List", typeof(RectTransform), typeof(VerticalLayoutGroup)); list.transform.SetParent(inv.transform, false);  Fit(list, Vector2.zero, Vector2.one, new Vector2(12, 12), new Vector2(-12, -44)); var ll = list.GetComponent<VerticalLayoutGroup>(); ll.childAlignment = TextAnchor.UpperLeft; ll.spacing = 4; ll.childForceExpandHeight = false; ll.childControlHeight = true; ll.childForceExpandWidth = true; ll.childControlWidth = true;
            var row = new GameObject("Row", typeof(RectTransform)); row.transform.SetParent(list.transform, false); row.AddComponent<LayoutElement>().preferredHeight = 28; var ri = Panel(row.transform, "Icon", Color.white);  Fit(ri, new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, 2), new Vector2(24, -2)); var rt = MakeText(row.transform, "Text", "", 16, TextAnchor.MiddleLeft, new Color(.2f, .12f, .05f));  Fit(rt.gameObject, Vector2.zero, Vector2.one, new Vector2(30, 0), Vector2.zero); row.SetActive(false);
            var hud = canvasGo.AddComponent<InventoryHUD>(); hud.panel = inv; hud.listParent = list.transform; hud.rowPrefab = row; inv.SetActive(false); return hud;
        }
        StoryMapUI BuildMap(GameObject canvasGo)
        {
            var mapGo = Panel(canvasGo.transform, "Story map", new Color(.06f, .05f, .04f, .88f));  Fit(mapGo, Vector2.zero, Vector2.one, new Vector2(40, 40), new Vector2(-40, -40)); var mg = mapGo.AddComponent<CanvasGroup>(); mg.alpha = 0; mg.blocksRaycasts = false;
            var mTitle = MakeText(mapGo.transform, "Title", "", 24, TextAnchor.UpperLeft, new Color(1, .9f, .7f));  Fit(mTitle.gameObject, new Vector2(0, 1), new Vector2(1, 1), new Vector2(24, -50), new Vector2(-24, -14)); mTitle.fontStyle = FontStyle.Bold;
            var mHereH = MakeText(mapGo.transform, "HereH", "WHERE YOU ARE", 13, TextAnchor.UpperLeft, new Color(1, .8f, .5f));  Fit(mHereH.gameObject, new Vector2(0, 1), new Vector2(.55f, 1), new Vector2(24, -74), new Vector2(-12, -56));
            var mHere = MakeText(mapGo.transform, "Here", "", 17, TextAnchor.UpperLeft, Color.white);  Fit(mHere.gameObject, new Vector2(0, 1), new Vector2(.55f, 1), new Vector2(24, -170), new Vector2(-12, -76));
            var mNextH = MakeText(mapGo.transform, "NextH", "WHAT'S NEXT", 13, TextAnchor.UpperLeft, new Color(1, .8f, .5f));  Fit(mNextH.gameObject, new Vector2(0, 1), new Vector2(.55f, 1), new Vector2(24, -196), new Vector2(-12, -178));
            var mNext = MakeText(mapGo.transform, "Next", "", 15, TextAnchor.UpperLeft, Color.white);  Fit(mNext.gameObject, new Vector2(0, 0), new Vector2(.55f, 1), new Vector2(24, 40), new Vector2(-12, -200)); mNext.verticalOverflow = VerticalWrapMode.Truncate;
            var mRecH = MakeText(mapGo.transform, "RecentH", "RECENTLY", 13, TextAnchor.UpperLeft, new Color(1, .8f, .5f));  Fit(mRecH.gameObject, new Vector2(.58f, 1), new Vector2(1, 1), new Vector2(0, -74), new Vector2(-24, -56));
            var mRec = MakeText(mapGo.transform, "Recent", "", 15, TextAnchor.UpperLeft, new Color(.85f, .85f, .85f));  Fit(mRec.gameObject, new Vector2(.58f, 1), new Vector2(1, 1), new Vector2(0, -130), new Vector2(-24, -76));
            var mEndH = MakeText(mapGo.transform, "EndingsH", "ENDINGS", 13, TextAnchor.UpperLeft, new Color(1, .8f, .5f));  Fit(mEndH.gameObject, new Vector2(.58f, 1), new Vector2(1, 1), new Vector2(0, -156), new Vector2(-24, -138));
            var mEnd = MakeText(mapGo.transform, "Endings", "", 15, TextAnchor.UpperLeft, Color.white);  Fit(mEnd.gameObject, new Vector2(.58f, 0), new Vector2(1, 1), new Vector2(0, 40), new Vector2(-24, -160));
            var mProg = MakeText(mapGo.transform, "Progress", "", 16, TextAnchor.LowerLeft, new Color(1, .9f, .7f));  Fit(mProg.gameObject, new Vector2(0, 0), new Vector2(1, 0), new Vector2(24, 12), new Vector2(-24, 36));
            var map = canvasGo.AddComponent<StoryMapUI>(); map.group = mg; map.titleText = mTitle; map.hereText = mHere; map.nextText = mNext; map.recentText = mRec; map.endingsText = mEnd; map.progressText = mProg; return map;
        }
        // Re-adds anything the kit needs in the open scene (older generated scenes, hand-built ones, missing-script prefabs):
        // missing UI (InventoryHUD / PickupToast / banner — the silent "Tab does nothing" case), world components, colliders, zones.
        void RepairScene()
        {
            int fixedCount = 0; var swappedColliders = new List<string>();
            // Repair makes the scene consistent with the player that is in it — it does not convert a scene from one style to
            // another (that is what "Create test scene" does). Say so, because generating as top-down while the open scene is
            // still the third-person one is exactly how you end up with 3D props in a 2D world.
            var player = Object.FindObjectOfType<StoryloomPlayer>(); bool xz = player && player.UsesXZ;
            if (player && _b != null && player.Style != _b.gameStyle)
                Debug.LogWarning($"Storyloom repair: the open scene's player is {player.Style} but the bindings' style is {_b.gameStyle}. Repairing as {player.Style} — use \"Create test scene\" to build a {_b.gameStyle} scene.");
            var dir0 = Object.FindObjectOfType<StoryloomDirector>();
            if (dir0)
            {
                bool hadHud = Object.FindObjectOfType<InventoryHUD>(true), hadToast = Object.FindObjectOfType<PickupToast>(true), hadBanner = Object.FindObjectOfType<LocationBanner>(true);
                if (dir0.keys == null) dir0.keys = KeysAsset();
                EnsureUI(dir0, player ? player.Style : _b.gameStyle);
                if (!hadHud || !hadToast || !hadBanner) { fixedCount++; Debug.Log("Storyloom repair: rebuilt missing UI (" + (!hadHud ? "inventory " : "") + (!hadToast ? "toast " : "") + (!hadBanner ? "banner" : "") + ")"); }
            }
            foreach (var go in Object.FindObjectsOfType<GameObject>(true))
            {
                if (go.scene != SceneManager.GetActiveScene()) continue;
                bool npc = go.name.StartsWith("NPC · "), item = go.name.StartsWith("Item · "), disc = go.name.StartsWith("Discoverable · "), sign = go.name.StartsWith("Signpost · ");
                if (!(npc || item || disc || sign)) continue;
                // if (!go.GetComponent<Collider2D>()) { var c = go.AddComponent<BoxCollider2D>(); c.size = Vector2.one * .9f; fixedCount++; }
                // Objects instantiated from a prefab bound for another style carry the other dimension's collider; asking for
                // the missing one on top of it makes Unity refuse (and return null, which then threw). Switch them over.
                if (StoryloomColliders.MatchPlane(go, xz)) { fixedCount++; swappedColliders.Add(go.name); }
                if (!go.GetComponent<Interactable>())
                {
                    var nm = go.name.Substring(go.name.IndexOf('·') + 2); var s = _b.story.Story; fixedCount++;
                    if (npc) { var ch = s.characters?.FirstOrDefault(x => x.name == nm); go.AddComponent<NpcInteractable>().characterId = ch != null ? ch.id : ""; }
                    else if (item) { var it = s.items?.FirstOrDefault(x => x.name == nm); go.AddComponent<ItemPickup>().itemId = it != null ? it.id : ""; }
                    else if (disc) { var n = s.nodes?.FirstOrDefault(x => x.IsDiscoverable && x.title == nm); go.AddComponent<DiscoverableInteractable>().nodeId = n != null ? n.id : ""; }
                    else { var l = s.locations?.FirstOrDefault(x => x.name == nm); go.AddComponent<Signpost>().locationId = l != null ? l.id : ""; }
                    var pr = go.transform.Find("Prompt"); var inter = go.GetComponent<Interactable>(); if (pr && inter) inter.prompt = pr.gameObject;
                }
                // foreach (var tm in go.GetComponentsInChildren<TextMesh>(true)) { var lp = tm.transform.localPosition; if (lp.z > -0.5f) { tm.transform.localPosition = new Vector3(lp.x, lp.y, -1f); } }
                foreach (var tm in go.GetComponentsInChildren<TextMesh>(true)) { var lp = tm.transform.localPosition; if (!xz && lp.z > -0.5f) tm.transform.localPosition = new Vector3(lp.x, lp.y, -1f); if (xz && !tm.GetComponent<Billboard>()) tm.gameObject.AddComponent<Billboard>(); }
            }
            // var player = Object.FindObjectOfType<PlayerController2D>();
            if (player && !xz) { if (!player.GetComponent<Collider2D>()) { var pc = player.gameObject.AddComponent<CircleCollider2D>(); if (pc) { pc.radius = .4f; fixedCount++; } } var rb = player.GetComponent<Rigidbody2D>(); if (rb) { rb.gravityScale = 0; rb.freezeRotation = true; if (rb.sleepMode != RigidbodySleepMode2D.NeverSleep) { rb.sleepMode = RigidbodySleepMode2D.NeverSleep; fixedCount++; } rb.interpolation = RigidbodyInterpolation2D.Interpolate; rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous; } }
            if (player && xz && !player.GetComponent<CursorLock>()) { player.gameObject.AddComponent<CursorLock>().keys = player.keys; fixedCount++; }
            foreach (var z in Object.FindObjectsOfType<LocationTrigger>()) { var c3 = z.GetComponent<Collider>(); if (c3) { if (!c3.isTrigger) { c3.isTrigger = true; fixedCount++; } if (!z.GetComponent<Rigidbody>()) { var rb = z.gameObject.AddComponent<Rigidbody>(); rb.isKinematic = true; rb.useGravity = false; fixedCount++; } if (z.gameObject.layer != 2) { z.gameObject.layer = 2; fixedCount++; } } var c2 = z.GetComponent<Collider2D>(); if (c2 && !c2.isTrigger) { c2.isTrigger = true; fixedCount++; } }
            // objects renamed by hand: match their label (TextMesh) text against character / item / location names and attach the right component
            if (_b != null && _b.story != null && _b.story.Story != null)
            {
                var st = _b.story.Story;
                foreach (var tm in Object.FindObjectsOfType<TextMesh>(true))
                {
                    var goP = tm.transform.parent ? tm.transform.parent.gameObject : null; if (!goP || goP.scene != SceneManager.GetActiveScene()) continue;
                    if (goP.GetComponent<Interactable>() || goP.GetComponentInParent<StoryloomPlayer>()) continue;
                    var label = (tm.text ?? "").Split('\n')[0].Trim(); if (label.Length == 0) continue;
                    var ch = st.characters?.FirstOrDefault(x => x.name == label); var it = st.items?.FirstOrDefault(x => x.name == label); var dnn = st.nodes?.FirstOrDefault(x => x.IsDiscoverable && x.title == label);
                    if (ch != null && !ch.IsProtagonist) { goP.AddComponent<NpcInteractable>().characterId = ch.id; fixedCount++; }
                    else if (it != null) { goP.AddComponent<ItemPickup>().itemId = it.id; fixedCount++; }
                    else if (dnn != null) { goP.AddComponent<DiscoverableInteractable>().nodeId = dnn.id; fixedCount++; }
                    else continue;
                    if (StoryloomColliders.MatchPlane(goP, xz)) swappedColliders.Add(goP.name);
                    var pr2 = goP.transform.Find("Prompt"); var inter2 = goP.GetComponent<Interactable>(); if (pr2 && inter2) inter2.prompt = pr2.gameObject;
                }
            }
            var dir = Object.FindObjectOfType<StoryloomDirector>(); if (dir && !dir.GetComponent<StoryloomDebugHud>()) { dir.gameObject.AddComponent<StoryloomDebugHud>(); fixedCount++; }
            fixedCount += StampEntityAssets();   // fill empty entity-asset references from ids (no-op when none are generated)
            if (swappedColliders.Count > 0) Debug.Log($"Storyloom repair: gave {swappedColliders.Count} object(s) the {(xz ? "3D" : "2D")} collider this style needs (they came from a prefab bound for the other style): " + string.Join(", ", swappedColliders));
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            ShowNotification(new GUIContent($"Repaired {fixedCount} thing{(fixedCount == 1 ? "" : "s")}"));
        }
        // In play mode: exercise the toast, the inventory and focus directly, and print what happened — separates "input never arrives" from "UI missing".
        void SelfTest()
        {
            var d = StoryloomDirector.Instance; var p = StoryloomPlayer.Current ? StoryloomPlayer.Current : Object.FindObjectOfType<StoryloomPlayer>();
            var sb = new System.Text.StringBuilder("Storyloom self-test\n");
            if (!d) { sb.Append("no StoryloomDirector in the scene\n"); Debug.Log(sb); return; }
            d.ResolveUI();
            sb.Append($"player: {(p ? p.GetType().Name + " (" + p.Style + ")" : "MISSING")}  keys: {(p && p.keys ? p.keys.name : "none")}  backend: {StoryloomKeyBinds.Backend}\n");
            sb.Append($"toast: {(d.toast ? "ok" : "MISSING")}  inventoryHud: {(d.inventoryHud ? "ok" : "MISSING")}  dialogue: {(d.dialogue ? "ok" : "MISSING")}\n");
            if (d.toast) { try { d.toast.Show("Self-test toast", null); sb.Append("toast.Show → called (should be visible top-right for ~2 s)\n"); } catch (System.Exception e) { sb.Append("toast.Show threw: " + e.Message + "\n"); } }
            if (d.inventoryHud) { try { d.inventoryHud.Toggle(); sb.Append("inventory Toggle → " + (d.inventoryHud.IsOpen ? "OPEN" : "closed") + "\n"); } catch (System.Exception e) { sb.Append("inventory Toggle threw: " + e.Message + "\n"); } }
            int live = Interactable.All.Count(i => i != null && i.enabled && i.gameObject.activeInHierarchy); sb.Append($"interactables live: {live}\n");
            if (p) { var near = Interactable.Nearest(p.transform.position, p.transform.forward, out float nd, p.UsesXZ); sb.Append($"nearest: {(near ? near.name : "—")} at {nd:0.00} (reach {p.Reach:0.00}); focus now: {(p.Focus ? p.Focus.name : "—")}; prompt object on nearest: {(near && near.prompt ? "yes" : "NO")}\n"); if (near) { near.EnsurePrompt(); near.SetFocused(true); sb.Append("forced the nearest one's [E] prompt on for this frame — look for it above the object\n"); } }
            // physics sanity: colliders on interactables, zones, overlaps between placed objects
            var its = Interactable.All.Where(i => i != null).ToList(); int noCol = 0;
            foreach (var i in its) { bool has3 = i.GetComponent<Collider>(), has2 = i.GetComponent<Collider2D>(); if (!(p && p.UsesXZ ? has3 : has2)) noCol++; }
            sb.Append($"interactables without a {(p && p.UsesXZ ? "3D" : "2D")} collider: {noCol}\n");
            var zones = Object.FindObjectsOfType<LocationTrigger>(); sb.Append($"zones: {zones.Length}");
            foreach (var z in zones) { var c3 = z.GetComponent<Collider>(); var c2 = z.GetComponent<Collider2D>(); sb.Append($"  [{z.locationId}: {(c3 ? (c3.isTrigger ? "3D trigger" : "3D SOLID!") : c2 ? (c2.isTrigger ? "2D trigger" : "2D SOLID!") : "NO COLLIDER")}{(c3 && !z.GetComponent<Rigidbody>() && !(p is PlayerController2D) ? " no-rb" : "")}]"); }
            sb.Append("\n");
            var close = new List<string>();
            for (int a = 0; a < its.Count; a++) for (int b2 = a + 1; b2 < its.Count; b2++) { var dd = its[a].transform.position - its[b2].transform.position; if (p && p.UsesXZ) dd.y = 0; else dd.z = 0; if (dd.magnitude < 1.2f) close.Add(its[a].name + " ↔ " + its[b2].name + $" ({dd.magnitude:0.0})"); }
            sb.Append(close.Count == 0 ? "no interactables placed within 1.2 of each other\n" : "OVERLAPPING placements (focus will flip between them): " + string.Join("; ", close) + "\n");
            if (p) { var cc = p.GetComponent<CharacterController>(); var rb2 = p.GetComponent<Rigidbody2D>(); sb.Append($"player body: {(cc ? $"CharacterController r={cc.radius} h={cc.height}" : rb2 ? "Rigidbody2D" : "NONE")}  layer {LayerMask.LayerToName(p.gameObject.layer)}\n"); }
            sb.Append("recent log:\n  " + string.Join("\n  ", StoryloomDirector.Log));
            Debug.Log(sb.ToString()); ShowNotification(new GUIContent("Self-test written to the Console"));
        }
        StoryloomKeyBinds KeysAsset()
        {
            var p = Root + "/Data/StoryloomKeyBinds.asset"; var k = AssetDatabase.LoadAssetAtPath<StoryloomKeyBinds>(p);
            if (k != null) { EditorUtility.SetDirty(k); AssetDatabase.SaveAssets(); }   // triggers the KeyCode→Key migration on assets from v0.2
            if (k == null) { k = CreateInstance<StoryloomKeyBinds>(); Directory.CreateDirectory(Root + "/Data"); AssetDatabase.CreateAsset(k, p); AssetDatabase.SaveAssets(); }
            return k;
        }
    }
}
#endif
