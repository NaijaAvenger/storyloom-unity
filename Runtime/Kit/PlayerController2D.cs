// Storyloom Unity Kit — top-down player (Stardew-style).
// WASD/arrows, Shift to run, E/Space to interact with the nearest Interactable in front of you. Freezes while a beat plays.
// v0.4: derives from StoryloomPlayer (shared with the third- and first-person controllers). Public surface unchanged.
using UnityEngine;

namespace Storyloom
{
    [RequireComponent(typeof(Rigidbody2D))]
    public class PlayerController2D : StoryloomPlayer
    {
        // public StoryloomKeyBinds keys;                 // now on StoryloomPlayer
        public Transform interactOrigin;             // usually the feet; defaults to this transform
        public LayerMask interactMask = ~0;
        public SpriteRenderer sprite;                // flipped for left/right
        public Animator animator;                    // optional: floats MoveX, MoveY, Speed

        Rigidbody2D _rb; Vector2 _facing = Vector2.down;
        // Interactable _focus; public Interactable Focus => _focus; public float NearestDistance { get; private set; } public string NearestName { get; private set; } = "";   // now on StoryloomPlayer
        public override GameStyle Style => GameStyle.TopDown;

        void Awake() { _rb = GetComponent<Rigidbody2D>(); _rb.gravityScale = 0; _rb.freezeRotation = true; if (keys == null) keys = StoryloomDirector.Instance ? StoryloomDirector.Instance.keys : StoryloomKeyBinds.Default(); }

        void Update()
        {
            var dir = InBeat ? Vector2.zero : keys.MoveAxis();
            if (dir.sqrMagnitude > 0.01f) _facing = dir;
            float speed = keys.Running() ? keys.runSpeed : keys.walkSpeed;
            _rb.velocity = dir * speed;
            if (sprite && Mathf.Abs(dir.x) > 0.01f) sprite.flipX = dir.x < 0;
            if (animator) { animator.SetFloat("MoveX", _facing.x); animator.SetFloat("MoveY", _facing.y); animator.SetFloat("Speed", dir.magnitude); }

            // focus the closest interactable within reach, preferring what we face
            var origin = interactOrigin ? (Vector2)interactOrigin.position : (Vector2)transform.position;
            float reach = Reach;
            Interactable best = Interactable.Nearest(origin, _facing, out float nd); NearestDistance = nd; NearestName = best ? best.name : "";
            if (best && NearestDistance > reach) best = null;
            if (best == null)   // physics fallback (colliders on any layer)
                foreach (var h in Physics2D.OverlapCircleAll(origin, reach, interactMask)) { var it = h.GetComponentInParent<Interactable>(); if (it != null && it.enabled) { best = it; break; } }
            SetFocus(best);
            HandleActionKeys();
        }

        void OnDrawGizmosSelected() { Gizmos.color = new Color(1, .8f, .2f, .5f); Gizmos.DrawWireSphere(interactOrigin ? interactOrigin.position : transform.position, keys ? keys.interactRadius : 1.1f); }
    }
}
