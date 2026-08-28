// Storyloom Unity Kit — first-person player (3D, XZ ground plane).
// Mouse / right stick look, WASD / left stick move, Shift to run, E / Space interact with what you're looking at
// (a ray from the camera; falls back to the nearest Interactable in front of you). Freezes while a beat plays.
using UnityEngine;

namespace Storyloom
{
    [RequireComponent(typeof(CharacterController))]
    public class FirstPersonController : StoryloomPlayer
    {
        public Transform head;                       // the camera's parent (pitch); yaw is applied to this transform
        public float gravity = -20f, minPitch = -80f, maxPitch = 80f;
        public LayerMask interactMask = ~(1 << 2);   // everything except Ignore Raycast (zones live there)
        [Tooltip("Radius of the aim sweep from the camera. A zero-width ray slips past small props (and over anything shorter than eye height); this gives the crosshair some forgiveness.")]
        public float aimRadius = 0.22f;
        [Tooltip("Multiplier on the key-binds look sensitivity (the third-person camera uses 0.75; 1 keeps first person at the raw setting)")]
        public float lookScale = 1f;
        CharacterController _cc; CursorLock _cursor; float _vy, _pitch;
        public override GameStyle Style => GameStyle.FirstPerson;
        /// <summary>What the centre ray hits (for the crosshair).</summary>
        public bool LookingAtFocus { get; private set; }

        void Awake() { _cc = GetComponent<CharacterController>(); _cursor = GetComponent<CursorLock>(); if (keys == null) keys = StoryloomDirector.Instance ? StoryloomDirector.Instance.keys : StoryloomKeyBinds.Default(); if (!head && Camera.main) head = Camera.main.transform; }

        void Update()
        {
            bool busy = InBeat || InventoryOpen;
            // Look whenever the game is free-roaming, exactly like ThirdPersonCamera. This used to also require
            // Cursor.lockState == Locked, which the editor doesn't report until the Game view has been clicked into — so
            // mouse look was simply dead. CursorLock.Released (set by Cancel) is the real "the mouse is the player's now" flag.
            if (!busy && (!_cursor || !_cursor.Released))
            {
                var look = keys.LookAxis() * lookScale;
                transform.Rotate(0, look.x, 0);
                _pitch = Mathf.Clamp(_pitch - look.y, minPitch, maxPitch);
                if (head) head.localRotation = Quaternion.Euler(_pitch, 0, 0);
            }
            var axis = InBeat ? Vector2.zero : keys.MoveAxis();
            var dir = transform.forward * axis.y + transform.right * axis.x; if (dir.sqrMagnitude > 1) dir.Normalize();
            float speed = keys.Running() ? keys.runSpeed : keys.walkSpeed;
            if (_cc.isGrounded && _vy < 0) _vy = -2f; _vy += gravity * Time.deltaTime;
            _cc.Move((dir * speed + Vector3.up * _vy) * Time.deltaTime);

            // focus: what the camera is aimed at within reach, else the nearest interactable in front of us
            Interactable best = null; LookingAtFocus = false; float reach = Reach + 1.0f;
            var origin = head ? head.position : transform.position + Vector3.up * 1.4f; var fwd = head ? head.forward : transform.forward;
            best = AimedAt(origin, fwd, reach); LookingAtFocus = best != null;
            var near = Interactable.Nearest(transform.position, transform.forward, out float nd, true); NearestDistance = nd; NearestName = near ? near.name : "";
            // anything whose collider is in reach and isn't behind you
            if (!best)
            {
                var inReach = Interactable.Nearest(transform.position, transform.forward, out _, true, Reach);
                if (inReach) { var to = inReach.ClosestPointTo(transform.position, true) - transform.position; to.y = 0; if (to.sqrMagnitude < 1e-6f || Vector3.Dot(to.normalized, transform.forward) > -0.2f) best = inReach; }
            }
            SetFocus(best);
            HandleActionKeys();
        }

        /// <summary>The interactable the crosshair is on. Sweeps a small sphere instead of firing a zero-width ray: the placeholder
        /// props are knee-high and the camera sits at eye level, so a thin ray sailed straight over most of them.</summary>
        readonly RaycastHit[] _aimHits = new RaycastHit[16];   // reused: this runs every frame
        Interactable AimedAt(Vector3 origin, Vector3 forward, float reach)
        {
            int n = Physics.SphereCastNonAlloc(origin, Mathf.Max(0.01f, aimRadius), forward, _aimHits, reach, interactMask, QueryTriggerInteraction.Ignore);
            Interactable best = null; float bestDist = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                var h = _aimHits[i];
                if (!h.collider || h.collider.transform.IsChildOf(transform)) continue;   // never aim at our own body
                var it = h.collider.GetComponentInParent<Interactable>();
                if (!it || !it.enabled || !it.gameObject.activeInHierarchy) continue;     // walls and floor are skipped, not treated as blockers
                if (h.distance < bestDist) { bestDist = h.distance; best = it; }
            }
            return best;
        }
    }

    /// <summary>Centre-screen dot for the first-person style; brightens when something interactable is in reach.</summary>
    public class Crosshair : MonoBehaviour
    {
        public UnityEngine.UI.Image dot; public Color idle = new Color(1, 1, 1, .5f), active = new Color(1, .85f, .3f, 1f);
        void Update() { var p = StoryloomPlayer.Current as FirstPersonController; var d = StoryloomDirector.Instance; bool busy = d && (d.InBeat || d.InventoryOpen); if (dot) { dot.enabled = !busy; dot.color = p && p.Focus ? active : idle; dot.rectTransform.sizeDelta = p && p.Focus ? new Vector2(10, 10) : new Vector2(6, 6); } }
    }
}
