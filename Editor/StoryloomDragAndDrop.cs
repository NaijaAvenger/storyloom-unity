// Storyloom Unity Kit — drag-and-drop for entity assets.
// Drag a character / item / location / discoverable asset (Window ▸ Storyloom ▸ Generate entity assets):
//   · onto a GameObject in the Hierarchy or Scene view → that object gains the matching interactable, wired to the entity
//     (an existing interactable of that kind is rebound, your own components and visuals are untouched)
//   · onto empty ground in the Scene view → the entity's bound prefab (or the default placeholder) is spawned there, wired
//   · a location asset normally makes the target a zone (LocationTrigger + trigger volume); hold Alt to make a Signpost
// Everything goes through asset.ApplyTo(go), so your own tools can do the same in code.
#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Storyloom.EditorTools
{
    [InitializeOnLoad]
    public static class StoryloomDragAndDrop
    {
        static StoryloomDragAndDrop()
        {
            EditorApplication.hierarchyWindowItemOnGUI += OnHierarchyItem;
            SceneView.duringSceneGui += OnSceneView;
        }

        static StoryloomEntityAsset Dragged() => DragAndDrop.objectReferences.OfType<StoryloomEntityAsset>().FirstOrDefault();

        static void OnHierarchyItem(int instanceId, Rect rect)
        {
            var e = Event.current;
            if (e.type != EventType.DragUpdated && e.type != EventType.DragPerform) return;
            if (!rect.Contains(e.mousePosition)) return;
            var asset = Dragged(); if (!asset) return;
            var go = EditorUtility.InstanceIDToObject(instanceId) as GameObject; if (!go) return;
            DragAndDrop.visualMode = DragAndDropVisualMode.Link;
            if (e.type == EventType.DragPerform) { DragAndDrop.AcceptDrag(); Bind(asset, go, e.alt); }
            e.Use();
        }

        static void OnSceneView(SceneView sv)
        {
            var e = Event.current;
            if (e.type != EventType.DragUpdated && e.type != EventType.DragPerform) return;
            var asset = Dragged(); if (!asset) return;
            var target = HandleUtility.PickGameObject(e.mousePosition, false);
            DragAndDrop.visualMode = target ? DragAndDropVisualMode.Link : DragAndDropVisualMode.Copy;
            if (e.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                if (target) Bind(asset, target, e.alt);
                else Spawn(asset, GroundPoint(e.mousePosition, out bool xz), xz);
            }
            e.Use();
        }

        static void Bind(StoryloomEntityAsset asset, GameObject go, bool alt)
        {
            Undo.RegisterFullObjectHierarchyUndo(go, "Storyloom: bind " + asset.DisplayName);
            var c = asset is StoryloomLocationAsset loc && alt ? loc.ApplySignpost(go) : asset.ApplyTo(go);
            EditorUtility.SetDirty(go); MarkDirty(go);
            Debug.Log($"Storyloom: bound '{asset.DisplayName}' → {go.name} ({c.GetType().Name})", go);
        }

        static void Spawn(StoryloomEntityAsset asset, Vector3 at, bool xz)
        {
            GameObject prefab = asset is StoryloomCharacterAsset ch ? ch.Prefab
                              : asset is StoryloomItemAsset it ? it.Prefab
                              : asset is StoryloomDiscoverableAsset di ? di.Prefab : null;
            GameObject go = prefab ? (GameObject)PrefabUtility.InstantiatePrefab(prefab) : new GameObject();
            string kind = asset is StoryloomCharacterAsset ? "NPC" : asset is StoryloomItemAsset ? "Item"
                        : asset is StoryloomDiscoverableAsset ? "Discoverable" : "Zone";
            go.name = kind + " · " + asset.DisplayName;   // the repair passes recognise these names
            if (xz && !(asset is StoryloomLocationAsset)) at.y = 0.6f;   // props sit on the ground, like the generated scenes
            go.transform.position = at;
            Undo.RegisterCreatedObjectUndo(go, "Storyloom: place " + asset.DisplayName);
            asset.ApplyTo(go);
            Selection.activeGameObject = go; MarkDirty(go);
            Debug.Log($"Storyloom: placed '{go.name}' at {at}", go);
        }

        // where the drop lands on the world plane: z = 0 for top-down (XY), y = 0 for the 3D styles (XZ)
        static Vector3 GroundPoint(Vector2 mouse, out bool xz)
        {
            var ray = HandleUtility.GUIPointToWorldRay(mouse);
            var player = Object.FindObjectOfType<StoryloomPlayer>();
            xz = player ? player.UsesXZ : GuessStyleXZ();
            var plane = xz ? new Plane(Vector3.up, 0f) : new Plane(Vector3.forward, 0f);
            if (plane.Raycast(ray, out float d)) return ray.GetPoint(d);
            return ray.origin + ray.direction * 10f;
        }
        static bool GuessStyleXZ()
        {
            var b = AssetDatabase.FindAssets("t:StoryloomBindings").Select(g => AssetDatabase.LoadAssetAtPath<StoryloomBindings>(AssetDatabase.GUIDToAssetPath(g))).FirstOrDefault();
            return b && b.gameStyle != GameStyle.TopDown;
        }
        static void MarkDirty(GameObject go) { if (!Application.isPlaying) EditorSceneManager.MarkSceneDirty(go.scene); }
    }

    /// <summary>Shows what an entity asset resolves to — its live story data — under the raw fields, and warns when the id
    /// no longer exists in the story (deleted upstream) or the asset is missing its story/bindings references.</summary>
    [CustomEditor(typeof(StoryloomEntityAsset), true), CanEditMultipleObjects]
    public class StoryloomEntityAssetEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            if (targets.Length > 1) return;
            var a = (StoryloomEntityAsset)target;
            GUILayout.Space(6);
            if (!a.story) { EditorGUILayout.HelpBox("No story asset assigned — this handle can't resolve anything.", MessageType.Error); return; }
            if (!a.Exists) { EditorGUILayout.HelpBox($"Id '{a.entityId}' is not in '{a.story.name}' (removed upstream, or the wrong story is assigned).", MessageType.Warning); return; }
            EditorGUILayout.LabelField("Resolved from the story", EditorStyles.boldLabel);
            switch (a)
            {
                case StoryloomCharacterAsset c: { var d = c.Data; Row("Character", d.name); Row("Role", d.roleType); Row("Description", d.description); Row("Portrait", c.Portrait ? c.Portrait.name : "(none bound)"); Row("Prefab", c.Prefab ? c.Prefab.name : "(default placeholder)"); break; }
                case StoryloomItemAsset i: { var d = i.Data; Row("Item", d.name); Row("Kind", d.kind); Row("Description", string.IsNullOrEmpty(d.description) ? d.effect : d.description); Row("Icon", i.Icon ? i.Icon.name : "(none bound)"); break; }
                case StoryloomLocationAsset l: { var d = l.Data; Row("Location", d.name); Row("Kind", d.kind); Row("Description", Storyloom.StoryloomDirector.LocationBlurb(d)); break; }
                case StoryloomDiscoverableAsset n: { var d = n.Node; Row("Discoverable", d.title); Row("Kind", d.discoverKind); break; }
            }
            if (!a.bindings) EditorGUILayout.HelpBox("No bindings assigned — art and prefab lookups will come back empty.", MessageType.Info);
            EditorGUILayout.HelpBox("Drag this asset onto a GameObject (Hierarchy or Scene view) to wire it up, or onto empty ground in the Scene view to place it. Alt-drop a location for a Signpost instead of a zone.", MessageType.None);
        }
        static void Row(string label, string value) { if (!string.IsNullOrEmpty(value)) EditorGUILayout.LabelField(label, value, EditorStyles.wordWrappedLabel); }
    }
}
#endif
