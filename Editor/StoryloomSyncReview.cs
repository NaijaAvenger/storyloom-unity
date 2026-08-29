// Storyloom Unity Kit — live-link sync review.
// A pull no longer applies silently: the incoming export is diffed against the imported story and a review window lists
// every change — added / removed / modified entities and nodes, each with a checkbox to bring it in or ignore it — plus
// re-identification detection: an entity that vanished while one with the same name appeared (the site regenerated its
// id) is offered as a MIGRATION, updating your binding row and entity asset to the new id in place instead of
// duplicating them, which is what silent pulls used to do.
//
// Honesty notes, stated in the window too:
//   · ignoring changes splices the incoming export and re-serializes it from Unity's data model — export fields the model
//     doesn't parse are dropped from the local copy until the next fully-accepted pull
//   · accepting a removal also removes the entity's binding row; its entity asset file is left in place (scenes may
//     reference it) and its inspector flags it as missing from the story
#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Storyloom.EditorTools
{
    public static class StoryloomSyncReview
    {
        public class Entry
        {
            public string cat;        // Characters | Items | Locations | Nodes
            public char kind;         // '+' added  '-' removed  '~' changed  'R' re-identified (id churn)
            public string id;         // new id ('+','~','R') or the removed id ('-')
            public string oldId;      // 'R' only: the id your project currently uses
            public string name;
            public bool accept = true;
        }

        public static List<Entry> Diff(StoryloomStory oldS, StoryloomStory newS)
        {
            var entries = new List<Entry>();
            if (oldS == null || newS == null) return entries;
            void Cat<T>(string cat, T[] olds, T[] news, System.Func<T, string> id, System.Func<T, string> name)
            {
                var o = (olds ?? new T[0]).Where(x => x != null).ToDictionary(id, x => x);
                var n = (news ?? new T[0]).Where(x => x != null).ToDictionary(id, x => x);
                var added = n.Keys.Where(k => !o.ContainsKey(k)).ToList();
                var removed = o.Keys.Where(k => !n.ContainsKey(k)).ToList();
                // re-identification: same name on both sides of an add/remove pair = the id changed upstream
                var removedByName = removed.GroupBy(k => name(o[k])).Where(g => g.Count() == 1).ToDictionary(g => g.Key, g => g.First());
                foreach (var k in added.ToList())
                {
                    var nm = name(n[k]);
                    if (!string.IsNullOrEmpty(nm) && removedByName.TryGetValue(nm, out var oldKey))
                    { entries.Add(new Entry { cat = cat, kind = 'R', id = k, oldId = oldKey, name = nm }); added.Remove(k); removed.Remove(oldKey); removedByName.Remove(nm); }
                }
                foreach (var k in added) entries.Add(new Entry { cat = cat, kind = '+', id = k, name = name(n[k]) });
                foreach (var k in removed) entries.Add(new Entry { cat = cat, kind = '-', id = k, name = name(o[k]) });
                foreach (var k in n.Keys.Where(k => o.ContainsKey(k)))
                    if (JsonUtility.ToJson(o[k]) != JsonUtility.ToJson(n[k])) entries.Add(new Entry { cat = cat, kind = '~', id = k, name = name(n[k]) });
            }
            Cat("Characters", oldS.characters, newS.characters, c => c.id, c => c.name);
            Cat("Items", oldS.items, newS.items, i => i.id, i => i.name);
            Cat("Locations", oldS.locations, newS.locations, l => l.id, l => l.name);
            Cat("Nodes", oldS.nodes, newS.nodes, x => x.id, x => string.IsNullOrEmpty(x.title) ? x.id : x.title);
            return entries;
        }

        /// <summary>Apply the review decisions. Splices ignored changes out of `incoming` (returns true when it did — the
        /// caller must then re-serialize instead of writing the fetched text), migrates re-identified ids onto the project's
        /// binding rows and entity assets, and prunes binding rows for accepted removals.</summary>
        public static bool ApplyDecisions(StoryloomStory old, StoryloomStory incoming, List<Entry> entries, StoryloomBindings b)
        {
            bool spliced = false;
            T[] Splice<T>(T[] oldArr, T[] newArr, string cat, System.Func<T, string> id)
            {
                var list = (newArr ?? new T[0]).ToList();
                var oldById = (oldArr ?? new T[0]).Where(x => x != null).ToDictionary(id, x => x);
                foreach (var e in entries.Where(e => e.cat == cat && !e.accept))
                {
                    spliced = true;
                    if (e.kind == '+') list.RemoveAll(x => id(x) == e.id);                                          // don't bring it in
                    else if (e.kind == '-') { if (oldById.TryGetValue(e.id, out var keep)) list.Add(keep); }        // keep it alive locally
                    else if (e.kind == '~') { if (oldById.TryGetValue(e.id, out var mine)) { var i = list.FindIndex(x => id(x) == e.id); if (i >= 0) list[i] = mine; } }   // keep my version
                    else if (e.kind == 'R') { }                                                                    // unaccepted migration = plain add+remove, both already accepted-by-default elsewhere
                }
                return list.ToArray();
            }
            incoming.characters = Splice(old?.characters, incoming.characters, "Characters", c => c.id);
            incoming.items = Splice(old?.items, incoming.items, "Items", i => i.id);
            incoming.locations = Splice(old?.locations, incoming.locations, "Locations", l => l.id);
            incoming.nodes = Splice(old?.nodes, incoming.nodes, "Nodes", n => n.id);
            // an ignored added node must not be dangled into by links
            var nodeIds = new HashSet<string>(incoming.nodes.Select(n => n.id));
            foreach (var n in incoming.nodes) if (n.links != null) n.links = n.links.Where(l => nodeIds.Contains(l.toNodeId)).ToArray();

            // migrations: point the project's rows/assets at the new id — assignments survive, nothing duplicates
            var assets = StoryloomEditorWindow.LoadEntityAssets();
            foreach (var e in entries.Where(e => e.kind == 'R' && e.accept))
            {
                if (e.cat == "Characters") { var r = b.Character(e.oldId); if (r != null) r.id = e.id; Migrate<StoryloomCharacterAsset>(assets, e); }
                else if (e.cat == "Items") { var r = b.Item(e.oldId); if (r != null) r.id = e.id; Migrate<StoryloomItemAsset>(assets, e); }
                else if (e.cat == "Locations") { var r = b.Location(e.oldId); if (r != null) r.id = e.id; Migrate<StoryloomLocationAsset>(assets, e); }
                else if (e.cat == "Nodes") { var r = b.Discoverable(e.oldId); if (r != null) r.nodeId = e.id; Migrate<StoryloomDiscoverableAsset>(assets, e); }
            }
            // accepted removals: prune the binding rows (assets are left, flagged by their inspector) and stale unassigned rows
            var gone = entries.Where(e => e.kind == '-' && e.accept).Select(e => e.id).ToHashSet();
            b.characters.RemoveAll(r => gone.Contains(r.id));
            b.items.RemoveAll(r => gone.Contains(r.id));
            b.locations.RemoveAll(r => gone.Contains(r.id));
            b.discoverables.RemoveAll(r => gone.Contains(r.nodeId));
            // duplicates from earlier silent pulls: rows whose id no longer exists anywhere AND carry no assignments are noise
            var live = new HashSet<string>(incoming.characters.Select(c => c.id).Concat(incoming.items.Select(i => i.id)).Concat(incoming.locations.Select(l => l.id)).Concat(incoming.nodes.Select(n => n.id)));
            b.characters.RemoveAll(r => !live.Contains(r.id) && !r.prefab && !r.portrait && !r.worldSprite && !r.voiceBark);
            b.items.RemoveAll(r => !live.Contains(r.id) && !r.prefab && !r.icon);
            b.locations.RemoveAll(r => !live.Contains(r.id) && !r.prefab && !r.banner && !r.ambience && string.IsNullOrEmpty(r.sceneName));
            b.discoverables.RemoveAll(r => !live.Contains(r.nodeId) && !r.prefab && !r.worldSprite);
            // and never two rows for one id (earlier silent pulls could double up) — a row with assignments outranks a bare one
            Dedup(b.characters, r => r.id, r => r.prefab || r.portrait || r.worldSprite || r.voiceBark);
            Dedup(b.items, r => r.id, r => r.prefab || r.icon);
            Dedup(b.locations, r => r.id, r => r.prefab || r.banner || r.ambience || !string.IsNullOrEmpty(r.sceneName));
            Dedup(b.discoverables, r => r.nodeId, r => r.prefab || r.worldSprite);
            EditorUtility.SetDirty(b);
            return spliced;
        }
        static void Dedup<T>(List<T> rows, System.Func<T, string> id, System.Func<T, bool> assigned)
        {
            var keep = new Dictionary<string, T>();
            foreach (var r in rows) { var k = id(r) ?? ""; if (!keep.ContainsKey(k) || (!assigned(keep[k]) && assigned(r))) keep[k] = r; }
            if (keep.Count != rows.Count) { rows.Clear(); rows.AddRange(keep.Values); }
        }
        static void Migrate<T>(Dictionary<string, StoryloomEntityAsset> assets, Entry e) where T : StoryloomEntityAsset
        {
            if (assets.TryGetValue(typeof(T).Name + ":" + e.oldId, out var a) && a) { a.entityId = e.id; EditorUtility.SetDirty(a); }
        }
    }

    /// <summary>The review popup a live-link pull opens when the incoming export differs: every change with a checkbox,
    /// re-identifications preselected as migrations, Apply selected / Ignore this pull.</summary>
    public class StoryloomSyncReviewWindow : EditorWindow
    {
        List<StoryloomSyncReview.Entry> _entries; System.Action<bool> _done; string _title; Vector2 _scroll; bool _fired;
        public static void Open(string storyName, List<StoryloomSyncReview.Entry> entries, System.Action<bool> done)
        {
            var w = GetWindow<StoryloomSyncReviewWindow>(true, "Live Link — review changes");
            w._title = storyName; w._entries = entries; w._done = done; w._fired = false;
            w.minSize = new Vector2(460, 320); w.maxSize = new Vector2(560, 800);
        }
        void OnGUI()
        {
            if (_entries == null) { Close(); return; }
            EditorGUILayout.LabelField($"'{_title}' — {_entries.Count} change(s) in the workbook", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Checked = bring the change in. Unchecked additions/removals/edits are ignored this pull (the local copy is then rewritten from Unity's model). ↻ rows migrate your bindings and entity assets to an entity whose id changed upstream — unchecked, they duplicate instead.", EditorStyles.wordWrappedMiniLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("All", GUILayout.Width(50))) _entries.ForEach(e => e.accept = true);
                if (GUILayout.Button("None", GUILayout.Width(50))) _entries.ForEach(e => e.accept = false);
            }
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            foreach (var group in _entries.GroupBy(e => e.cat))
            {
                EditorGUILayout.LabelField(group.Key, EditorStyles.boldLabel);
                foreach (var e in group)
                {
                    string glyph = e.kind == '+' ? "＋ added" : e.kind == '-' ? "－ removed" : e.kind == '~' ? "≈ changed" : "↻ id changed — migrate bindings/assets";
                    e.accept = EditorGUILayout.ToggleLeft($"{glyph}:  {e.name}", e.accept);
                }
                GUILayout.Space(4);
            }
            EditorGUILayout.EndScrollView();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button($"Apply selected ({_entries.Count(e => e.accept)}/{_entries.Count})", GUILayout.Height(28))) Finish(true);
                if (GUILayout.Button("Ignore this pull", GUILayout.Height(28))) Finish(false);
            }
        }
        void Finish(bool apply) { _fired = true; var d = _done; _done = null; Close(); d?.Invoke(apply); }
        void OnDestroy() { if (!_fired && _done != null) _done(false); }   // closing the window = ignore
    }
}
#endif
