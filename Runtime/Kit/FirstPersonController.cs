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
        CharacterController _cc; float _vy, _pitch;
        public override GameStyle Style => GameStyle.FirstPerson;
        /// <summary>What the centre ray hits (for the crosshair).</summary>
        public bool LookingAtFocus { get; private set; }

        void Awake() { _cc = GetComponent<CharacterController>(); if (keys == null) keys = StoryloomDirector.Instance ? StoryloomDirector.Instance.keys : StoryloomKeyBinds.Default(); if (!head && Camera.main) head = Camera.main.transform; }

        void Update()
        {
            bool busy = InBeat || InventoryOpen;
            if (!busy && Cursor.lockState == CursorLockMode.Locked) { var look = keys.LookAxis(); transform.Rotate(0, look.x, 0); _pitch = Mathf.Clamp(_pitch - look.y, minPitch, maxPitch); if (head) head.localRotation = Quaternion.Euler(_pitch, 0, 0); }
            var axis = InBeat ? Vector2.zero : keys.MoveAxis();
            var dir = transform.forward * axis.y + transform.right * axis.x; if (dir.sqrMagnitude > 1) dir.Normalize();
            float speed = keys.Running() ? keys.runSpeed : keys.walkSpeed;
            if (_cc.isGrounded && _vy < 0) _vy = -2f; _vy += gravity * Time.deltaTime;
            _cc.Move((dir * speed + Vector3.up * _vy) * Time.deltaTime);

            // focus: what the camera ray hits within reach, else the nearest interactable in front of us
            Interactable best = null; LookingAtFocus = false; float reach = Reach + 1.0f;
            var origin = head ? head.position : transform.position + Vector3.up * 1.4f; var fwd = head ? head.forward : transform.forward;
            if (Physics.Raycast(origin, fwd, out var hit, reach, interactMask, QueryTriggerInteraction.Ignore)) { best = hit.collider.GetComponentInParent<Interactable>(); if (best && !best.enabled) best = null; LookingAtFocus = best != null; }
            var near = Interactable.Nearest(transform.position, transform.forward, out float nd, true); NearestDistance = nd; NearestName = near ? near.name : "";
            // if (!best && near && nd <= Reach) { var to = near.transform.position - transform.position; to.y = 0; if (Vector3.Dot(to.normalized, transform.forward) > 0.3f) best = near; }
            if (!best && near && nd <= Reach) { var to = near.transform.position - transform.position; to.y = 0; if (Vector3.Dot(to.normalized, transform.forward) > -0.2f) best = near; }   // anything in reach that isn't behind you
            SetFocus(best);
            HandleActionKeys();
        }
    }

    /// <summary>Centre-screen dot for the first-person style; brightens when something interactable is in reach.</summary>
    public class Crosshair : MonoBehaviour
    {
        public UnityEngine.UI.Image dot; public Color idle = new Color(1, 1, 1, .5f), active = new Color(1, .85f, .3f, 1f);
        void Update() { var p = StoryloomPlayer.Current as FirstPersonController; var d = StoryloomDirector.Instance; bool busy = d && (d.InBeat || (d.inventoryHud && d.inventoryHud.IsOpen)); if (dot) { dot.enabled = !busy; dot.color = p && p.Focus ? active : idle; dot.rectTransform.sizeDelta = p && p.Focus ? new Vector2(10, 10) : new Vector2(6, 6); } }
    }
}
