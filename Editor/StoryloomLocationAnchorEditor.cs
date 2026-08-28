// Storyloom Unity Kit — LocationAnchor inspector: Populate from story / Clear generated.
// Membership matches the scene generator: NPCs whose home is this location (or who speak at beats set here), items given
// here, discoverables whose effective location is here. Spawns land on unused StoryloomSpawnPoints first (id-reserved
// ones matched first), then on a grid around the anchor. Everything spawned carries a StoryloomAnchorSpawn marker, so
// Populate is idempotent and Clear removes only what Populate created.
#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Storyloom.EditorTools
{
    [CustomEditor(typeof(LocationAnchor))]
    public class StoryloomLocationAnchorEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var a = (LocationAnchor)target;
            GUILayout.Space(6);
            if (!a.location) { EditorGUILayout.HelpBox("Assign a location entity asset (Window ▸ Storyloom ▸ Generate entity assets), or drag one onto this object.", MessageType.Info); return; }
            var s = a.location.Story;
            if (s == null) { EditorGUILayout.HelpBox("The location asset can't resolve its story.", MessageType.Warning); return; }
            var loc = a.location.Data;
            EditorGUILayout.LabelField(loc != null ? $"'{loc.name}' — {Members(a, s).Count()} entit(ies) belong here" : $"Id '{a.location.entityId}' is not in the story", EditorStyles.miniLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Populate from story", GUILayout.Height(26))) Populate(a, s);
                if (GUILayout.Button("Clear generated", GUILayout.Height(26))) Clear(a);
            }
            EditorGUILayout.HelpBox("Add child GameObjects with a StoryloomSpawnPoint to choose where entities land; set preferredEntityId to reserve a spot. Populate skips anything it already placed.", MessageType.None);
        }

        // (kind, entityId, displayName) of everything that belongs at this anchor's location
        static System.Collections.Generic.IEnumerable<(char kind, string id, string name)> Members(LocationAnchor a, StoryloomStory s)
        {
            string locId = a.location.entityId;
            if (a.placeNpcs)
                foreach (var ch in s.characters ?? new Character[0])
                {
                    if (ch.IsProtagonist) continue;
                    bool here = ch.homeLocationId == locId || (string.IsNullOrEmpty(ch.homeLocationId) && s.nodes.Any(n => n.locationId == locId && (n.speakerId == ch.id || (n.lines != null && n.lines.Any(l => l.speakerId == ch.id)))));
                    if (here) yield return ('c', ch.id, ch.name);
                }
            if (a.placeItems)
                foreach (var it in s.items ?? new Item[0])
                {
                    bool here = !it.startOwned && s.nodes.Any(n => n.locationId == locId && ((n.itemIds != null && n.itemIds.Contains(it.id)) || (n.effects != null && n.effects.Any(e => e.variable == StoryRunner.ItemPrefix + it.id && e.op == "give"))));
                    if (here) yield return ('i', it.id, it.name);
                }
            if (a.placeDiscoverables)
                foreach (var dn in s.nodes.Where(n => n.IsDiscoverable))
                    if (StoryloomEditorWindow.EffectiveLocation(s, dn) == locId) yield return ('d', dn.id, dn.title);
        }

        static void Populate(LocationAnchor a, StoryloomStory s)
        {
            var assets = StoryloomEditorWindow.LoadEntityAssets();
            T Asset<T>(string id) where T : StoryloomEntityAsset => assets.TryGetValue(typeof(T).Name + ":" + id, out var x) ? x as T : null;
            var b = a.location.bindings;
            var player = Object.FindObjectOfType<StoryloomPlayer>();
            bool xz = player ? player.UsesXZ : (b && b.gameStyle != GameStyle.TopDown);
            float y = xz ? 0.6f : 0f;

            var existing = a.GetComponentsInChildren<StoryloomAnchorSpawn>(true).Select(m => m.key).ToHashSet();
            var usedPoints = a.GetComponentsInChildren<StoryloomAnchorSpawn>(true).Where(m => m.usedPoint).Select(m => m.usedPoint).ToHashSet();
            var points = a.GetComponentsInChildren<StoryloomSpawnPoint>().Where(p => !usedPoints.Contains(p)).ToList();
            int placed = 0, grid = 0;

            Vector3 GridSlot()
            {
                var v = new Vector3(-2f * a.spacing + (grid % 5) * a.spacing, 0, 0); var row = grid / 5; grid++;
                if (xz) { v.y = y; v.z = 1.5f * a.spacing - row * a.spacing; }
                else v.y = 1.5f * a.spacing - row * a.spacing;
                return a.transform.position + v;
            }
            Vector3 SpotFor(string id, out StoryloomSpawnPoint point)
            {
                point = points.FirstOrDefault(p => p.preferredEntityId == id) ?? points.FirstOrDefault(p => string.IsNullOrEmpty(p.preferredEntityId));
                if (point) { points.Remove(point); return point.transform.position; }
                return GridSlot();
            }
            GameObject Mark(GameObject go, string key, StoryloomSpawnPoint point)
            {
                var m = go.AddComponent<StoryloomAnchorSpawn>(); m.key = key; m.usedPoint = point;
                go.transform.SetParent(a.transform, true);
                Undo.RegisterCreatedObjectUndo(go, "Storyloom populate"); placed++;
                return go;
            }

            // zone + signpost
            if (a.addZoneTrigger && !existing.Contains("zone") && !a.GetComponentInChildren<LocationTrigger>())
            {
                var zone = new GameObject("Zone · " + a.location.DisplayName);
                zone.transform.position = a.transform.position;
                a.location.ApplyTo(zone);
                var c3 = zone.GetComponent<BoxCollider>(); if (c3) { c3.size = a.zoneSize; c3.center = new Vector3(0, a.zoneSize.y * .5f, 0); }
                var c2 = zone.GetComponent<BoxCollider2D>(); if (c2) c2.size = new Vector2(a.zoneSize.x, a.zoneSize.z);
                Mark(zone, "zone", null);
            }
            if (a.placeSignpost && !existing.Contains("sign"))
            {
                var prefab = b ? b.defaultDiscoverablePrefab : null;
                var sign = prefab ? (GameObject)PrefabUtility.InstantiatePrefab(prefab) : new GameObject();
                sign.name = "Signpost · " + a.location.DisplayName;
                var di = sign.GetComponent<DiscoverableInteractable>(); if (di) Object.DestroyImmediate(di);
                sign.transform.position = a.transform.position + (xz ? new Vector3(0, y, -a.zoneSize.z * .4f) : new Vector3(0, -a.zoneSize.z * .4f, 0));
                a.location.ApplySignpost(sign);
                var tm = sign.GetComponentInChildren<TextMesh>(); if (tm) tm.text = a.location.DisplayName;
                Mark(sign, "sign", null);
            }

            foreach (var (kind, id, name) in Members(a, s).ToList())
            {
                string key = kind + ":" + id;
                if (existing.Contains(key)) continue;
                var pos = SpotFor(id, out var point);
                GameObject prefab = null; StoryloomEntityAsset asset = null; string label = "?";
                switch (kind)
                {
                    case 'c': { var ca = Asset<StoryloomCharacterAsset>(id); asset = ca; prefab = ca ? ca.Prefab : (b?.Character(id)?.prefab ?? b?.defaultNpcPrefab); label = "NPC"; break; }
                    case 'i': { var ia = Asset<StoryloomItemAsset>(id); asset = ia; prefab = ia ? ia.Prefab : (b?.Item(id)?.prefab ?? b?.defaultItemPrefab); label = "Item"; break; }
                    case 'd': { var da = Asset<StoryloomDiscoverableAsset>(id); asset = da; prefab = da ? da.Prefab : (b?.Discoverable(id)?.prefab ?? b?.defaultDiscoverablePrefab); label = "Discoverable"; break; }
                }
                var go = prefab ? (GameObject)PrefabUtility.InstantiatePrefab(prefab) : new GameObject();
                go.name = label + " · " + name; go.transform.position = pos;
                if (asset) asset.ApplyTo(go);
                else   // entity assets not generated (or missing this id): wire by id directly
                {
                    if (kind == 'c') go.AddComponent<NpcInteractable>().characterId = id;
                    else if (kind == 'i') go.AddComponent<ItemPickup>().itemId = id;
                    else go.AddComponent<DiscoverableInteractable>().nodeId = id;
                    StoryloomColliders.MatchPlane(go, xz);
                }
                var tm2 = go.GetComponentInChildren<TextMesh>(); if (tm2 && string.IsNullOrEmpty(tm2.text)) tm2.text = name;
                Mark(go, key, point);
            }
            EditorSceneManager.MarkSceneDirty(a.gameObject.scene);
            Debug.Log($"Storyloom: anchor '{a.name}' populated {placed} object(s) for '{a.location.DisplayName}'" + (assets.Count == 0 ? " (tip: Generate entity assets first so spawns reference the typed handles)" : ""), a);
        }

        static void Clear(LocationAnchor a)
        {
            var marks = a.GetComponentsInChildren<StoryloomAnchorSpawn>(true);
            foreach (var m in marks) Undo.DestroyObjectImmediate(m.gameObject);
            EditorSceneManager.MarkSceneDirty(a.gameObject.scene);
            Debug.Log($"Storyloom: anchor '{a.name}' cleared {marks.Length} generated object(s)", a);
        }
    }
}
#endif
