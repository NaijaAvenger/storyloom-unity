// Storyloom Unity Kit — third-person player (3D, XZ ground plane).
// WASD / left stick move relative to the camera, Shift to run, mouse / right stick orbit the camera (ThirdPersonCamera),
// E / Space interact with the nearest Interactable in front of you. Freezes while a beat plays. Uses a CharacterController.
using UnityEngine;

namespace Storyloom
{
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController3D : StoryloomPlayer
    {
        public Transform cameraTransform;            // the camera whose yaw movement is relative to (defaults to Camera.main)
        public Transform visual;                     // rotated to face the move direction (optional)
        public float turnSpeed = 900f, gravity = -20f;
        public Animator animator;                    // optional: float Speed
        CharacterController _cc; float _vy; Vector3 _facing = Vector3.forward;
        public override GameStyle Style => GameStyle.ThirdPerson;

        void Awake() { _cc = GetComponent<CharacterController>(); if (keys == null) keys = StoryloomDirector.Instance ? StoryloomDirector.Instance.keys : StoryloomKeyBinds.Default(); }

        void Update()
        {
            var cam = cameraTransform ? cameraTransform : (Camera.main ? Camera.main.transform : null);
            var axis = InBeat ? Vector2.zero : keys.MoveAxis();
            Vector3 fwd = cam ? Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized : Vector3.forward, right = cam ? Vector3.ProjectOnPlane(cam.right, Vector3.up).normalized : Vector3.right;
            var dir = (fwd * axis.y + right * axis.x); if (dir.sqrMagnitude > 1) dir.Normalize();
            if (dir.sqrMagnitude > 0.01f) { _facing = dir.normalized; var target = Quaternion.LookRotation(_facing); var t = visual ? visual : transform; t.rotation = Quaternion.RotateTowards(t.rotation, target, turnSpeed * Time.deltaTime); }
            float speed = keys.Running() ? keys.runSpeed : keys.walkSpeed;
            if (_cc.isGrounded && _vy < 0) _vy = -2f; _vy += gravity * Time.deltaTime;
            _cc.Move((dir * speed + Vector3.up * _vy) * Time.deltaTime);
            if (animator) animator.SetFloat("Speed", dir.magnitude);

            // Standing still, the last move direction is a poor guess at what the player means — they orbit the camera to look at
            // things. Bias by the camera when idle, by the move direction when walking.
            var aim = dir.sqrMagnitude > 0.01f ? _facing : (cam ? fwd : _facing);
            var near = Interactable.Nearest(transform.position, aim, out float nd, true); NearestDistance = nd; NearestName = near ? near.name : "";
            var best = Interactable.Nearest(transform.position, aim, out _, true, Reach);   // range is applied while choosing, not afterwards
            SetFocus(best);
            HandleActionKeys();
        }
        void OnDrawGizmosSelected() { Gizmos.color = new Color(1, .8f, .2f, .5f); Gizmos.DrawWireSphere(transform.position, keys ? keys.interactRadius : 1.1f); }
    }

    /// <summary>Orbit camera for the third-person style: sits behind and above the target, mouse / right stick to look around.</summary>
    public class ThirdPersonCamera : MonoBehaviour
    {
        public Transform target; public StoryloomKeyBinds keys;
        public float distance = 3.2f, height = 1.55f, minPitch = -15f, maxPitch = 65f;
        [Tooltip("Sideways offset for the over-the-shoulder framing (positive = camera sits to the player's right)")] public float shoulder = 0.45f;
        [Tooltip("0 = the camera is glued to the player (crisp); higher = a little lag on position only. Rotation is never smoothed.")] public float smooth = 0f;
        [Tooltip("Multiplier on the key-binds look sensitivity for this camera")] public float lookScale = 0.75f;
        public LayerMask collideWith = 0;            // set to your environment layers to keep the camera out of walls (0 = off)
        float _yaw, _pitch = 18f;
        void Start() { if (!target) { var p = StoryloomPlayer.Current; if (p) target = p.transform; } if (!keys) { var p = StoryloomPlayer.Current; if (p) keys = p.keys; } if (target) _yaw = target.eulerAngles.y; Snap(); if (!target) Debug.LogWarning("Storyloom: ThirdPersonCamera has no target — assign the player"); }
        void Snap() { if (!target) return; var rot = Quaternion.Euler(_pitch, _yaw, 0); transform.position = target.position + Vector3.up * height + rot * new Vector3(shoulder, 0, -distance); transform.rotation = rot; }
        void LateUpdate()
        {
            if (!target) { var p = StoryloomPlayer.Current; if (p) target = p.transform; else return; }
            if (!keys) { var p = StoryloomPlayer.Current; if (p) keys = p.keys; }
            var d = StoryloomDirector.Instance; bool busy = (d && d.InBeat) || (d && d.InventoryOpen);
            // mouse turns the camera whenever the game is free-roaming (the CursorLock component locks the pointer; in the editor the
            // first click into the Game view grabs it — but look works even unlocked so the camera is never just frozen)
            if (keys && !busy) { var look = keys.LookAxis() * lookScale; _yaw += look.x; _pitch = Mathf.Clamp(_pitch - look.y, minPitch, maxPitch); }
            var rot = Quaternion.Euler(_pitch, _yaw, 0); var pivot = target.position + Vector3.up * height + rot * new Vector3(shoulder, 0, 0);
            float dist = distance;
            if (collideWith.value != 0 && Physics.SphereCast(pivot, .25f, rot * Vector3.back, out var hit, distance, collideWith)) dist = Mathf.Max(.5f, hit.distance);
            var want = pivot + rot * new Vector3(0, 0, -dist);
            transform.position = smooth <= 0f ? want : Vector3.Lerp(transform.position, want, 1 - Mathf.Exp(-smooth * Time.deltaTime));
            transform.rotation = rot;
        }
    }
}
