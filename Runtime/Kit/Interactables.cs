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
        protected virtual void Awake() { Register(); }
        protected virtual void OnEnable() { Register(); }
        protected virtual void OnDisable() { All.Remove(this); }
        void Register() { All.RemoveAll(x => x == null); if (!All.Contains(this)) All.Add(this); }
        /// <summary>Nearest enabled interactable to a point (XY plane, top-down), any distance. Used by the player and the debug HUD.</summary>
        public static Interactable Nearest(Vector2 origin, Vector2 facing, out float dist) => Nearest(new Vector3(origin.x, origin.y, 0), new Vector3(facing.x, facing.y, 0), out dist, false);
        /// <summary>Nearest enabled interactable, measured on the XY plane (top-down) or the XZ plane (3D styles), preferring what the player faces.</summary>
        public static Interactable Nearest(Vector3 origin, Vector3 facing, out float dist, bool xz)
        {
            Interactable best = null; float bestScore = float.MaxValue; dist = float.MaxValue;
            foreach (var it in All)
            {
                if (it == null || !it.enabled || !it.gameObject.activeInHierarchy) continue;
                var to = it.transform.position - origin; if (xz) to.y = 0; else to.z = 0;
                var f = facing; if (xz) f.y = 0; else f.z = 0;
                float d = to.magnitude;
                // float score = d - 0.5f * (d > 0.001f ? Vector3.Dot(to / d, f.normalized) : 0f);
                float score = d - 0.15f * (d > 0.001f ? Vector3.Dot(to / d, f.normalized) : 0f);   // nearest wins; facing only breaks near-ties
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
        void Reset() { var c2 = GetComponent<Collider2D>(); if (c2) c2.isTrigger = true; var c3 = GetComponent<Collider>(); if (c3) c3.isTrigger = true; }
        void Enter(Component other) { if (other.GetComponentInParent<StoryloomPlayer>() && StoryloomDirector.Instance) { StoryloomDirector.Instance.PlayerLocationId = locationId; StoryloomDirector.Instance.EnterLocation(locationId); } }
        void Exit(Component other) { if (other.GetComponentInParent<StoryloomPlayer>() && StoryloomDirector.Instance && StoryloomDirector.Instance.PlayerLocationId == locationId) StoryloomDirector.Instance.PlayerLocationId = ""; }
        void OnTriggerEnter2D(Collider2D other) { Enter(other); }
        void OnTriggerExit2D(Collider2D other) { Exit(other); }
        void OnTriggerEnter(Collider other) { Enter(other); }
        void OnTriggerExit(Collider other) { Exit(other); }
        // Physics trigger events can be missed (CharacterControllers, sleeping bodies, layer matrices), so every zone also polls whether the
        // player is inside it — 3D and 2D alike. Whichever fires first wins; the other is ignored because the player's zone already matches.
        bool _inside; float _next;
        void Update()
        {
            var p = StoryloomPlayer.Current; if (!p) return;
            if (Time.time < _next) return; _next = Time.time + 0.1f;
            bool inside;
            if (p.UsesXZ) { var c3 = GetComponent<Collider>(); if (!c3) return; inside = c3.bounds.Contains(p.transform.position + Vector3.up * 0.5f); }
            else { var c2 = GetComponent<Collider2D>(); if (!c2) return; inside = c2.OverlapPoint(p.transform.position); }
            var d = StoryloomDirector.Instance;
            if (inside && !_inside) { _inside = true; if (d && d.PlayerLocationId != locationId) Enter(p); }
            else if (!inside && _inside) { _inside = false; Exit(p); }
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
