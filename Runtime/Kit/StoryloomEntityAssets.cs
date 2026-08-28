// Storyloom Unity Kit — per-entity ScriptableObjects ("entity assets").
// One asset per character, item, location and discoverable, generated from the imported story (Window ▸ Storyloom ▸
// Generate entity assets). They are typed, draggable handles into the story: drag one onto a GameObject in the hierarchy
// or scene view and it gains the matching interactable wired to that entity; drag one into a component's asset field to
// rebind it; or call asset.ApplyTo(go) from your own tooling. The story data itself stays in the StoryloomStoryAsset and
// the art in the StoryloomBindings — an entity asset *resolves* both, so there is exactly one source of truth and
// re-importing a newer export updates every asset in place (they are matched by entityId, not by name).
using UnityEngine;

namespace Storyloom
{
    public abstract class StoryloomEntityAsset : ScriptableObject
    {
        [Tooltip("The imported story this entity lives in")] public StoryloomStoryAsset story;
        [Tooltip("Art / prefab lookups for the whole story (portraits, sprites, prefabs)")] public StoryloomBindings bindings;
        [Tooltip("The Storyloom id this asset stands for. Stable across re-imports — don't edit by hand.")] public string entityId;

        public StoryloomStory Story => story ? story.Story : null;
        /// <summary>The entity's display name as the story knows it right now.</summary>
        public abstract string DisplayName { get; }
        /// <summary>False when the id no longer exists in the story (entity deleted upstream).</summary>
        public abstract bool Exists { get; }
        /// <summary>Attach (or rewire) the component this entity needs on `go` and return it. Safe in the editor and at runtime.</summary>
        public abstract Component ApplyTo(GameObject go);

        /// <summary>Which world plane the scene uses: the live player when there is one, else the bindings' style.</summary>
        protected bool UsesXZ()
        {
            var p = StoryloomPlayer.Current;
            if (p) return p.UsesXZ;
#if UNITY_EDITOR
            var scene = FindObjectOfType<StoryloomPlayer>(); if (scene) return scene.UsesXZ;
#endif
            return bindings && bindings.gameStyle != GameStyle.TopDown;
        }
        /// <summary>Fill the placeholder conveniences on `go` if they are empty: the sprite, the floating label, the [E] prompt.</summary>
        protected void Dress(GameObject go, Sprite worldSprite, string label)
        {
            var sr = go.GetComponentInChildren<SpriteRenderer>(); if (sr && !sr.sprite && worldSprite) sr.sprite = worldSprite;
            var tm = go.GetComponentInChildren<TextMesh>(); if (tm && string.IsNullOrEmpty(tm.text)) tm.text = label;
            var it = go.GetComponent<Interactable>(); if (it) { var pr = go.transform.Find("Prompt"); if (pr && !it.prompt) it.prompt = pr.gameObject; }
        }
    }

    [CreateAssetMenu(menuName = "Storyloom/Entity/Character", fileName = "Character")]
    public class StoryloomCharacterAsset : StoryloomEntityAsset
    {
        public Character Data => Story?.GetCharacter(entityId);
        public CharacterBinding Binding => bindings ? bindings.Character(entityId) : null;
        public override string DisplayName { get { var d = Data; return d != null ? d.name : entityId; } }
        public override bool Exists => Data != null;
        public Sprite Portrait => Binding?.portrait;
        public GameObject Prefab { get { var b = Binding; return b != null && b.prefab ? b.prefab : (bindings ? bindings.defaultNpcPrefab : null); } }

        public override Component ApplyTo(GameObject go)
        {
            var npc = go.GetComponent<NpcInteractable>(); if (!npc) npc = go.AddComponent<NpcInteractable>();
            npc.character = this; npc.characterId = entityId;
            StoryloomColliders.MatchPlane(go, UsesXZ());
            Dress(go, Binding?.worldSprite, DisplayName);
            return npc;
        }
    }

    [CreateAssetMenu(menuName = "Storyloom/Entity/Item", fileName = "Item")]
    public class StoryloomItemAsset : StoryloomEntityAsset
    {
        public Item Data => Story?.GetItem(entityId);
        public ItemBinding Binding => bindings ? bindings.Item(entityId) : null;
        public override string DisplayName { get { var d = Data; return d != null ? d.name : entityId; } }
        public override bool Exists => Data != null;
        public Sprite Icon => Binding?.icon;
        public GameObject Prefab { get { var b = Binding; return b != null && b.prefab ? b.prefab : (bindings ? bindings.defaultItemPrefab : null); } }

        public override Component ApplyTo(GameObject go)
        {
            var pk = go.GetComponent<ItemPickup>(); if (!pk) pk = go.AddComponent<ItemPickup>();
            pk.item = this; pk.itemId = entityId;
            StoryloomColliders.MatchPlane(go, UsesXZ());
            Dress(go, Binding?.icon, DisplayName);
            return pk;
        }
    }

    [CreateAssetMenu(menuName = "Storyloom/Entity/Location", fileName = "Location")]
    public class StoryloomLocationAsset : StoryloomEntityAsset
    {
        public Location Data => Story?.GetLocation(entityId);
        public LocationBinding Binding => bindings ? bindings.Location(entityId) : null;
        public override string DisplayName { get { var d = Data; return d != null ? d.name : entityId; } }
        public override bool Exists => Data != null;

        /// <summary>Dropping a location onto an object makes it a zone (LocationTrigger). Use ApplySignpost for a readable sign instead.</summary>
        public override Component ApplyTo(GameObject go)
        {
            var z = go.GetComponent<LocationTrigger>(); if (!z) z = go.AddComponent<LocationTrigger>();
            z.location = this; z.locationId = entityId;
            EnsureVolume(go); z.CacheVolume();
            return z;
        }
        public Signpost ApplySignpost(GameObject go)
        {
            var sp = go.GetComponent<Signpost>(); if (!sp) sp = go.AddComponent<Signpost>();
            sp.location = this; sp.locationId = entityId;
            StoryloomColliders.MatchPlane(go, UsesXZ());
            Dress(go, null, DisplayName);
            return sp;
        }
        // a zone needs a trigger volume; give a bare object the same box the generated scenes use
        void EnsureVolume(GameObject go)
        {
            bool xz = UsesXZ();
            if (xz)
            {
                foreach (var w in go.GetComponents<Collider2D>()) { if (Application.isPlaying) Destroy(w); else DestroyImmediate(w); }
                var c = go.GetComponent<Collider>(); if (!c) { var b = go.AddComponent<BoxCollider>(); if (b) { b.size = new Vector3(11, 4, 12); b.center = new Vector3(0, 2, 0); c = b; } }
                if (c) c.isTrigger = true;
                if (c && !go.GetComponent<Rigidbody>()) { var rb = go.AddComponent<Rigidbody>(); rb.isKinematic = true; rb.useGravity = false; }
                if (c) go.layer = 2;   // Ignore Raycast: the first-person aim sweep must pass through zones
            }
            else
            {
                foreach (var w in go.GetComponents<Collider>()) { if (Application.isPlaying) Destroy(w); else DestroyImmediate(w); }
                var c = go.GetComponent<Collider2D>(); if (!c) { var b = go.AddComponent<BoxCollider2D>(); if (b) { b.size = new Vector2(11, 12); c = b; } }
                if (c) c.isTrigger = true;
            }
        }
    }

    [CreateAssetMenu(menuName = "Storyloom/Entity/Discoverable", fileName = "Discoverable")]
    public class StoryloomDiscoverableAsset : StoryloomEntityAsset
    {
        public StoryNode Node => Story?.GetNode(entityId);
        public DiscoverableBinding Binding => bindings ? bindings.Discoverable(entityId) : null;
        public override string DisplayName { get { var n = Node; return n != null ? n.title : entityId; } }
        public override bool Exists { get { var n = Node; return n != null && n.IsDiscoverable; } }
        public GameObject Prefab { get { var b = Binding; return b != null && b.prefab ? b.prefab : (bindings ? bindings.defaultDiscoverablePrefab : null); } }

        public override Component ApplyTo(GameObject go)
        {
            var di = go.GetComponent<DiscoverableInteractable>(); if (!di) di = go.AddComponent<DiscoverableInteractable>();
            di.discoverable = this; di.nodeId = entityId;
            StoryloomColliders.MatchPlane(go, UsesXZ());
            Dress(go, Binding?.worldSprite, DisplayName);
            if (!di.label) di.label = go.GetComponentInChildren<TextMesh>();
            return di;
        }
    }
}
