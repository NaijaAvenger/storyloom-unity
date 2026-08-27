// Storyloom Unity Kit — world interactables bound to Storyloom ids.
//   NpcInteractable          talk → Director.TalkTo(characterId)   (or the beats you list, in order)
//   ItemPickup               pick up → Director.Pickup(itemId), then hides/destroys itself
//   DiscoverableInteractable examine → Director.PlayNode(discoverable node)   (secrets, side quests, collectibles)
//   LocationTrigger          walking into it → Director.EnterLocation(locationId)  (banner + auto scene beats)
//   Signpost                 examine → shows a location's description as narration
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace Storyloom
{
    public abstract class Interactable : MonoBehaviour
    {
        // Every enabled interactable registers here; the player picks the nearest one in reach by distance, so focus works
        // whatever colliders, layers or physics mode the prefab uses.
        public static readonly List<Interactable> All = new List<Interactable>();
        protected virtual void Awake() { CacheColliders(); Register(); }
        protected virtual void OnEnable() { CacheColliders(); Register(); }
        protected virtual void OnDisable() { All.Remove(this); }
        void Register() { All.RemoveAll(x => x == null); if (!All.Contains(this)) All.Add(this); }
        // With "Enter Play Mode Options" set to skip the domain reload, statics survive between play sessions: start each run empty.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void ResetStatics() { All.Clear(); }

        // ---- geometry: reach is measured to the object's collider, not to its pivot ---------------------------------
        // Pivot-to-pivot distance is what made reach feel random: a wide NPC, an off-centre pivot, or a prefab whose mesh
        // sits above its root all read as further away than they look. Every candidate is now measured to the nearest point
        // on its collider (2D colliders for top-down, 3D colliders for the third-/first-person styles).
        Collider[] _cols3; Collider2D[] _cols2;
        /// <summary>Re-read the colliders; call after adding or removing one at runtime.</summary>
        public void CacheColliders() { _cols3 = GetComponentsInChildren<Collider>(true); _cols2 = GetComponentsInChildren<Collider2D>(true); }
        /// <summary>The point on this object's colliders closest to `from`, or its position when it has none.</summary>
        public Vector3 ClosestPointTo(Vector3 from, bool xz)
        {
            if (_cols3 == null || _cols2 == null) CacheColliders();
            var best = transform.position; float bestSqr = 0f; bool any = false;
            void Consider(Bounds b) { var p = b.ClosestPoint(from); var v = p - from; if (xz) v.y = 0; else v.z = 0; float sq = v.sqrMagnitude; if (!any || sq < bestSqr) { any = true; bestSqr = sq; best = p; } }
            // prefer the colliders that match the world plane; fall back to the other kind so hand-built prefabs still work
            if (xz) { foreach (var c in _cols3) if (c && c.enabled) Consider(c.bounds); if (!any) foreach (var c in _cols2) if (c && c.enabled) Consider(c.bounds); }
            else { foreach (var c in _cols2) if (c && c.enabled) Consider(c.bounds); if (!any) foreach (var c in _cols3) if (c && c.enabled) Consider(c.bounds); }
            return best;
        }
        /// <summary>Distance from `from` to this object's collider surface, flattened onto the world plane.</summary>
        public float DistanceTo(Vector3 from, bool xz) { var v = ClosestPointTo(from, xz) - from; if (xz) v.y = 0; else v.z = 0; return v.magnitude; }

        /// <summary>Nearest enabled interactable to a point (XY plane, top-down), any distance. Used by the player and the debug HUD.</summary>
        public static Interactable Nearest(Vector2 origin, Vector2 facing, out float dist) => Nearest(new Vector3(origin.x, origin.y, 0), new Vector3(facing.x, facing.y, 0), out dist, false, float.PositiveInfinity);
        /// <summary>Nearest enabled interactable within `maxDistance` of a point (XY plane, top-down).</summary>
        public static Interactable Nearest(Vector2 origin, Vector2 facing, out float dist, float maxDistance) => Nearest(new Vector3(origin.x, origin.y, 0), new Vector3(facing.x, facing.y, 0), out dist, false, maxDistance);
        /// <summary>Nearest enabled interactable, measured on the XY plane (top-down) or the XZ plane (3D styles), preferring what the player faces.</summary>
        public static Interactable Nearest(Vector3 origin, Vector3 facing, out float dist, bool xz) => Nearest(origin, facing, out dist, xz, float.PositiveInfinity);
        /// <summary>Nearest enabled interactable whose collider is within `maxDistance`, preferring what the player faces. The range
        /// test happens *before* the winner is picked, so a reachable object is never lost to a closer-scoring one that is out of reach.</summary>
        public static Interactable Nearest(Vector3 origin, Vector3 facing, out float dist, bool xz, float maxDistance)
        {
            Interactable best = null; float bestScore = float.MaxValue; dist = float.MaxValue;
            var f = facing; if (xz) f.y = 0; else f.z = 0; f = f.sqrMagnitude > 0.0001f ? f.normalized : Vector3.zero;
            for (int i = All.Count - 1; i >= 0; i--)
            {
                var it = All[i];
                if (it == null) { All.RemoveAt(i); continue; }
                if (!it.enabled || !it.gameObject.activeInHierarchy) continue;
                var to = it.ClosestPointTo(origin, xz) - origin; if (xz) to.y = 0; else to.z = 0;
                float d = to.magnitude;
                if (d > maxDistance) continue;                                                              // out of reach: never competes
                float score = d - 0.15f * (d > 0.001f && f != Vector3.zero ? Vector3.Dot(to / d, f) : 0f);   // nearest wins; facing only breaks near-ties
                if (score < bestScore) { bestScore = score; best = it; dist = d; }
            }
            return best;
        }
        [Tooltip("Shown above the object when the player can interact (optional)")] public GameObject prompt;
        public UnityEvent onInteract;
        public virtual string Verb => "Interact";
        // public virtual void SetFocused(bool on) { if (prompt) prompt.SetActive(on); }
        public virtual void SetFocused(bool on) { if (on && !prompt) EnsurePrompt(); if (prompt) prompt.SetActive(on); }
        /// <summary>Finds the prefab's "Prompt" child, or builds a small billboard "[E]" label above the object, so focus is always visible.</summary>
        public void EnsurePrompt()
        {
            if (prompt) return;
            var t = transform.Find("Prompt"); if (t) { prompt = t.gameObject; return; }
            var p = StoryloomPlayer.Current; bool xz = p && p.UsesXZ;
            var go = new GameObject("Prompt"); go.transform.SetParent(transform, false); go.transform.localPosition = xz ? new Vector3(0, 1.35f, 0) : new Vector3(0, 1.05f, -1f);
            var tm = go.AddComponent<TextMesh>(); tm.text = "[E]"; tm.characterSize = .08f; tm.fontSize = 40; tm.anchor = TextAnchor.LowerCenter; tm.color = new Color(1, .85f, .3f); go.GetComponent<MeshRenderer>().sortingOrder = 5;
            if (xz) go.AddComponent<Billboard>();
            go.SetActive(false); prompt = go;
        }
        // public void Interact(PlayerController2D player) { onInteract?.Invoke(); OnInteract(player); }
        // protected abstract void OnInteract(PlayerController2D player);
        /// <summary>Any kit player (top-down, third person, first person) can interact.</summary>
        public void Interact(StoryloomPlayer player) { onInteract?.Invoke(); OnInteract(player); }
        protected abstract void OnInteract(StoryloomPlayer player);
        protected StoryloomDirector D => StoryloomDirector.Instance;
    }

    public class NpcInteractable : Interactable
    {
        public string characterId;
        [Tooltip("Optional: beats to try first, in order (node ids). Otherwise the director picks the best unplayed beat for this character here.")]
        public List<string> preferredNodeIds = new List<string>();
        public override string Verb => "Talk";
        protected override void OnInteract(StoryloomPlayer p) { if (D) D.TalkTo(characterId, preferredNodeIds.Count > 0 ? preferredNodeIds : null); }
    }

    public class ItemPickup : Interactable
    {
        public string itemId;
        public bool destroyOnPickup = true;
        public override string Verb => "Pick up";
        protected override void OnInteract(StoryloomPlayer p) { if (D) { var beat = D.GivingBeat(itemId); if (beat != null && D.strictOrder && !D.Played.Contains(beat.id) && !D.Available(beat)) { if (D.dialogue) D.dialogue.ShowBark("", "Not yet — this belongs to a part of the story you haven't reached.", null); return; } D.Pickup(itemId); } if (destroyOnPickup) Destroy(gameObject); else gameObject.SetActive(false); }
        void Start() { if (D && D.Runner != null && D.Runner.HasItem(itemId) && destroyOnPickup) Destroy(gameObject); }   // already owned (e.g. after a load)
    }

    public class DiscoverableInteractable : Interactable
    {
        public string nodeId;
        public bool onceOnly = true;
        [Tooltip("Optional: a TextMesh under this object that shows the title, kind and reward (the placeholder prefab has one)")] public TextMesh label;
        public override string Verb => "Examine";
        StoryNode Node => D ? D.Story.GetNode(nodeId) : null;

        void Start() { RefreshLabel(); }
        public void RefreshLabel()
        {
            var n = Node; if (n == null) return;
            if (!label) label = GetComponentInChildren<TextMesh>();
            if (label) { var reward = D.RewardSummary(n); label.text = n.title + "\n<size=60%>" + (string.IsNullOrEmpty(n.discoverKind) ? "discoverable" : n.discoverKind) + (string.IsNullOrEmpty(reward) ? "" : " · " + reward) + (D.Played.Contains(nodeId) ? " · found" : "") + "</size>"; label.richText = true; }
        }
        public override void SetFocused(bool on)
        {
            base.SetFocused(on);
            if (!on || !prompt || !D) return; var n = Node; var tm = prompt.GetComponent<TextMesh>(); if (!tm || n == null) return;
            var why = D.LockReason(n); tm.text = D.Played.Contains(nodeId) && onceOnly ? "[E] found" : string.IsNullOrEmpty(why) ? "[E] " + Verb : "[E] locked: " + why;
        }
        protected override void OnInteract(StoryloomPlayer p)
        {
            if (!D) return; var n = Node; if (n == null) { Debug.LogWarning($"Storyloom: discoverable node {nodeId} not in this story"); return; }
            if (onceOnly && D.Played.Contains(nodeId)) { if (D.dialogue) D.dialogue.ShowBark("", "Nothing more here.", null); return; }
            var why = D.LockReason(n); if (!string.IsNullOrEmpty(why)) { if (D.dialogue) D.dialogue.ShowBark("", "You can't yet — " + why + ".", null); return; }
            if (!D.Available(n)) { if (D.dialogue) D.dialogue.ShowBark("", "Nothing here yet.", null); return; }
            D.PlayNode(nodeId);
            if (onceOnly) StartCoroutine(RelabelWhenDone());
        }
        System.Collections.IEnumerator RelabelWhenDone() { while (D && D.InBeat) yield return null; RefreshLabel(); }
    }

    // A trigger volume (2D collider for top-down scenes, 3D collider for the third-/first-person scenes) that tells the director where the player is.
    public class LocationTrigger : MonoBehaviour
    {
        public string locationId;
        [Tooltip("How often the zone re-checks whether the player is inside (seconds). The poll backs up the physics trigger events, which CharacterControllers and sleeping bodies can miss.")]
        public float pollInterval = 0.1f;
        [Tooltip("Slack added to the volume once the player is inside, so standing on the boundary doesn't flip the zone (and re-show the banner) over and over.")]
        public float exitSlack = 0.35f;
        void Reset() { foreach (var c in GetComponents<Collider2D>()) c.isTrigger = true; foreach (var c in GetComponents<Collider>()) c.isTrigger = true; }

        // The generated zones put the trigger on the same object as this component. Children are *not* swept: in the top-down
        // scenes the location root is also the parent of every NPC and prop, and their colliders are not part of the volume.
        Collider[] _cols3; Collider2D[] _cols2;
        void OnEnable() { CacheVolume(); _inside = false; _next = 0f; }
        /// <summary>Re-read the trigger volume; call after adding or removing one of its colliders at runtime.</summary>
        public void CacheVolume()
        {
            _cols3 = GetComponents<Collider>(); _cols2 = GetComponents<Collider2D>();
            if (_cols3.Length == 0 && _cols2.Length == 0)   // hand-built zone with the volume on a child
            { _cols3 = GetComponentsInChildren<Collider>(true); _cols2 = GetComponentsInChildren<Collider2D>(true); }
        }

        // Both the physics callbacks and the poll below funnel through these, so `_inside` always matches what the director
        // believes. Previously only the poll maintained it, and the two could disagree for a whole tick — long enough to lose
        // an arrival (or fire a spurious exit) when the player crossed between two zones.
        void MarkEntered()
        {
            if (_inside) return; _inside = true;
            var d = StoryloomDirector.Instance; if (!d) return;
            d.PlayerLocationId = locationId; d.EnterLocation(locationId);
        }
        void MarkExited()
        {
            if (!_inside) return; _inside = false;
            var d = StoryloomDirector.Instance; if (!d) return;
            d.ExitLocation(locationId);
        }
        static bool IsPlayer(Component other) => other && other.GetComponentInParent<StoryloomPlayer>();
        void OnTriggerEnter2D(Collider2D other) { if (IsPlayer(other)) MarkEntered(); }
        void OnTriggerExit2D(Collider2D other) { if (IsPlayer(other)) MarkExited(); }
        void OnTriggerEnter(Collider other) { if (IsPlayer(other)) MarkEntered(); }
        void OnTriggerExit(Collider other) { if (IsPlayer(other)) MarkExited(); }

        // Physics trigger events can be missed (CharacterControllers, sleeping bodies, layer matrices), so every zone also polls
        // whether the player is inside it — 3D and 2D alike. Whichever notices first wins; the other is a no-op.
        bool _inside; float _next;
        void Update()
        {
            var p = StoryloomPlayer.Current; if (!p) return;
            if (Time.time < _next) return; _next = Time.time + Mathf.Max(0.01f, pollInterval);
            if (Contains(p, _inside ? Mathf.Max(0f, exitSlack) : 0f)) MarkEntered(); else MarkExited();
        }
        /// <summary>Is the player's body inside this zone (within `slack` of it)? Tests the player's collider centre and its feet,
        /// so a shallow zone volume — or a player whose pivot sits at the floor — still registers.</summary>
        public bool Contains(StoryloomPlayer p, float slack = 0f)
        {
            if (!p) return false;
            if (_cols3 == null || _cols2 == null) CacheVolume();
            var feet = p.transform.position;
            var mid = p.BodyCentre;
            if (p.UsesXZ)
            {
                foreach (var c in _cols3) if (c && c.enabled && (Inside(c, mid, slack) || Inside(c, feet, slack))) return true;
                return false;
            }
            foreach (var c in _cols2) if (c && c.enabled && (Inside(c, mid, slack) || Inside(c, feet, slack))) return true;
            return false;
        }
        // ClosestPoint returns the point itself when it is inside the collider — unlike bounds.Contains this respects the zone's
        // rotation and scale. (Non-convex mesh colliders don't support it and fall back to the bounding box.)
        static bool Inside(Collider c, Vector3 point, float slack)
        {
            var b = c.bounds; b.Expand(slack * 2f); if (!b.Contains(point)) return false;
            var mc = c as MeshCollider; if (mc && !mc.convex) return true;
            return (c.ClosestPoint(point) - point).sqrMagnitude <= slack * slack + 1e-6f;
        }
        static bool Inside(Collider2D c, Vector3 point, float slack)
        {
            var b = c.bounds; b.Expand(slack * 2f); if (!b.Contains(new Vector3(point.x, point.y, b.center.z))) return false;
            if (slack <= 0f) return c.OverlapPoint(point);
            return ((Vector2)c.ClosestPoint(point) - (Vector2)point).sqrMagnitude <= slack * slack + 1e-6f;
        }
        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(.3f, .8f, 1f, .35f);
            foreach (var c in GetComponents<Collider>()) Gizmos.DrawWireCube(c.bounds.center, c.bounds.size);
            foreach (var c in GetComponents<Collider2D>()) Gizmos.DrawWireCube(c.bounds.center, c.bounds.size);
        }
    }

    public class Signpost : Interactable
    {
        public string locationId;
        public override string Verb => "Read";
        protected override void OnInteract(StoryloomPlayer p)
        {
            if (!D) return; var loc = D.Story.GetLocation(locationId); if (loc == null) { StoryloomDirector.Note("Signpost: location " + locationId + " not in the story"); return; }
            var text = StoryloomDirector.LocationBlurb(loc);
            if (string.IsNullOrEmpty(text)) text = (string.IsNullOrEmpty(loc.kind) ? "" : loc.kind + ". ") + "Nothing is written about this place yet — add a description or atmosphere in Storyloom's World tab.";
            // the same top-of-screen popup the zones use (re-reading the sign is allowed: banner, not dialogue)
            if (!D.banner) D.ResolveUI();
            if (D.banner) D.banner.Show(loc.name, "", null, text);
            else if (D.dialogue) D.dialogue.ShowNarration(loc.name, text);
        }
    }
}
