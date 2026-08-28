// Storyloom Unity Kit — game styles and the common player base.
// The kit can build three kinds of test scene from the same story: top-down (Stardew-style, the original), third person
// (camera behind the player, mouse / right stick to orbit) and first person (mouse look, crosshair). Interactables, the
// director and the UI are shared; only the player controller, camera and world plane differ.
using UnityEngine;

namespace Storyloom
{
    public enum GameStyle { TopDown = 0, ThirdPerson = 1, FirstPerson = 2 }

    /// <summary>Base for every kit player (2D top-down, 3D third person, 3D first person). Interactables and the director talk to this.</summary>
    public abstract class StoryloomPlayer : MonoBehaviour
    {
        public StoryloomKeyBinds keys;
        public static StoryloomPlayer Current { get; private set; }
        public abstract GameStyle Style { get; }
        /// <summary>True when the world lies on the XZ plane (3D styles); false for the XY top-down plane.</summary>
        public bool UsesXZ => Style != GameStyle.TopDown;
        public Interactable Focus { get; protected set; }
        public float NearestDistance { get; protected set; }
        public string NearestName { get; protected set; } = "";
        /// <summary>How far the player can reach: the bind's radius plus room for the two bodies. Measured from the player to the
        /// nearest point on the target's collider (see <see cref="Interactable.DistanceTo"/>), not centre to centre.</summary>
        public float Reach => (keys ? keys.interactRadius : 1.1f) + 1.1f;
        /// <summary>The middle of the player's body — the CharacterController / collider centre, else roughly chest height.
        /// Zones test this as well as the feet, so a shallow trigger volume can't be stepped over.</summary>
        public Vector3 BodyCentre
        {
            get
            {
                var cc = GetComponent<CharacterController>(); if (cc) return transform.TransformPoint(cc.center);
                var c2 = GetComponent<Collider2D>(); if (c2 && c2.enabled) return c2.bounds.center;
                var c3 = GetComponent<Collider>(); if (c3 && c3.enabled) return c3.bounds.center;
                return transform.position + (UsesXZ ? Vector3.up * 0.8f : Vector3.zero);
            }
        }

        protected virtual void OnEnable() { Current = this; }
        protected virtual void OnDisable() { if (Current == this) Current = null; }
        protected bool InBeat => StoryloomDirector.Instance && StoryloomDirector.Instance.InBeat;
        protected bool InventoryOpen => StoryloomDirector.Instance && StoryloomDirector.Instance.InventoryOpen;

        protected void SetFocus(Interactable best)
        {
            if (best == Focus) return;
            if (Focus) Focus.SetFocused(false);
            Focus = best;
            if (Focus) { Focus.SetFocused(true); StoryloomDirector.Note($"Focus → {Focus.name}{(Focus.prompt ? "" : " (no prompt object on it)")}"); }
        }
        /// <summary>Interact / inventory keys, shared by every style.</summary>
        protected void HandleActionKeys()
        {
            if (keys.InteractDown()) { if (InBeat) StoryloomDirector.Note("Interact pressed during a beat — ignored"); else if (Focus) { StoryloomDirector.Note($"Interact → {Focus.name} ({Focus.Verb})"); Focus.Interact(this); } else { StoryloomDirector.Note($"Interact: nothing in reach (nearest {NearestName} at {NearestDistance:0.0}, reach {Reach:0.0})"); StoryloomDirector.Instance?.Dialogue?.ShowBark("", "Nothing to interact with here.", null); } }
            if (keys.InventoryDown())
            {
                var d = StoryloomDirector.Instance; if (d && d.Inventory == null) d.ResolveUI();
                var inv = d ? d.Inventory : null;
                if (inv != null) { inv.Toggle(); StoryloomDirector.Note("Inventory key → " + (inv.IsOpen ? "opened" : "closed")); }
                else { StoryloomDirector.Note("Inventory key pressed but no inventory UI in the scene"); Debug.LogWarning("Storyloom: inventory key pressed but the director has no inventory UI (built-in or IInventoryUI override)"); }
            }
        }
    }

    /// <summary>Collider housekeeping shared by the runtime self-repair and the editor's "Repair open scene".
    ///
    /// Unity will not hold 2D and 3D colliders on one GameObject: <c>AddComponent</c> logs "conflicts with the existing …"
    /// and returns <c>null</c>. Objects instantiated from a prefab bound for another style (a per-character prefab is never
    /// style-swapped, so a 3D placeholder can end up in a top-down scene) carry the wrong kind, and blindly asking for the
    /// other one both spammed the console and threw a NullReferenceException on the returned null. These helpers switch the
    /// object over instead.</summary>
    public static class StoryloomColliders
    {
        // DestroyImmediate in both edit and play mode, deliberately: a plain Destroy is deferred to the end of the frame, so the
        // conflicting collider would still be attached when we ask for its replacement and Unity would refuse all over again.
        // This runs in the one-shot repair pass, never per frame.
        static void Kill(UnityEngine.Object o) { if (o) UnityEngine.Object.DestroyImmediate(o); }

        /// <summary>Give `go` the collider kind the world plane needs, removing the other kind from the object first.
        /// Returns true when something changed. A CharacterController is left alone — that is a body, not a prop collider.</summary>
        public static bool MatchPlane(GameObject go, bool xz, float size = .9f)
        {
            if (!go || go.GetComponent<CharacterController>()) return false;
            bool changed = false;
            if (xz)
            {
                foreach (var wrong in go.GetComponents<Collider2D>()) { Kill(wrong); changed = true; }
                if (!go.GetComponent<Collider>()) { var c = go.AddComponent<BoxCollider>(); if (c) { c.size = Vector3.one * size; changed = true; } else Blocked(go, "3D"); }
            }
            else
            {
                foreach (var wrong in go.GetComponents<Collider>()) { Kill(wrong); changed = true; }
                if (!go.GetComponent<Collider2D>()) { var c = go.AddComponent<BoxCollider2D>(); if (c) { c.size = Vector2.one * size; changed = true; } else Blocked(go, "2D"); }
            }
            if (changed) { var it = go.GetComponent<Interactable>(); if (it) it.CacheColliders(); }
            return changed;
        }
        // AddComponent returns null instead of throwing when it is refused; say which object so it can be fixed by hand.
        static void Blocked(GameObject go, string kind) => Debug.LogWarning($"Storyloom: couldn't give '{go.name}' a {kind} collider — something on it (a RequireComponent, or a collider of the other dimension that can't be removed) is in the way. Give it the right collider by hand, or bind a prefab made for this style.");
        /// <summary>True when `go` carries colliders of the wrong dimension for this world plane, or none at all.</summary>
        public static bool NeedsPlaneFix(GameObject go, bool xz)
        {
            if (!go || go.GetComponent<CharacterController>()) return false;
            return xz ? (go.GetComponent<Collider2D>() || !go.GetComponent<Collider>())
                      : (go.GetComponent<Collider>() || !go.GetComponent<Collider2D>());
        }
    }

    /// <summary>Keeps a label (TextMesh) facing the camera. Used by the 3D placeholder prefabs.</summary>
    public class Billboard : MonoBehaviour
    {
        public bool yAxisOnly = false;
        void LateUpdate()
        {
            var cam = Camera.main; if (!cam) return;
            if (yAxisOnly) { var f = transform.position - cam.transform.position; f.y = 0; if (f.sqrMagnitude > 0.001f) transform.rotation = Quaternion.LookRotation(f); }
            else transform.rotation = Quaternion.LookRotation(transform.position - cam.transform.position, cam.transform.up);
        }
    }

    /// <summary>Marks a prefab generated by the kit for a given style, so "Create scene" can swap placeholders when the style changes without touching your own prefabs.</summary>
    public class StoryloomPlaceholder : MonoBehaviour { public GameStyle style; }

    /// <summary>Locks the mouse while free-roaming in the 3D styles; releases it during beats, with the inventory open, or after Cancel (click to grab it again).</summary>
    public class CursorLock : MonoBehaviour
    {
        public StoryloomKeyBinds keys; public bool enabledLock = true; bool _released;
        /// <summary>True while the mouse belongs to the player rather than to the game: locking is switched off, or Cancel handed
        /// the pointer back (click / interact takes it again). The look controllers read this instead of Cursor.lockState — in the
        /// editor the pointer often isn't actually captured until the first click into the Game view, and gating look on the real
        /// lock state left first person unable to turn at all.</summary>
        public bool Released => !enabledLock || _released;
        void Update()
        {
            if (!enabledLock) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; return; }
            var d = StoryloomDirector.Instance;
            bool busy = (d && d.InBeat) || (d && d.InventoryOpen);
            if (keys && keys.CancelDown() && !busy) _released = true;
            if (_released && !busy && keys && keys.AdvanceDown()) _released = false;   // click / interact grabs the mouse again
            bool free = !busy && !_released;
            Cursor.lockState = free ? CursorLockMode.Locked : CursorLockMode.None; Cursor.visible = !free;
        }
        void OnDisable() { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
    }
}
