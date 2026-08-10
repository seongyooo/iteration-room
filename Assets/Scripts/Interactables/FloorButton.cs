using System.Collections.Generic;
using UnityEngine;

namespace IterationRoom
{
    // Hold-type button: active only while the real player OR at least one ghost
    // is currently occupying it. No toggle state - releasing deactivates instantly.
    public class FloorButton : GhostInteractable
    {
        public Renderer buttonRenderer;
        public Color inactiveColor = Color.white;
        public Color activeColor = new Color(0.2f, 1f, 0.4f);

        // Standing on the pad means the player's own centre is over the disc. SceneBuilder derives
        // this from the visual radius so the two can't drift apart.
        //
        // This used to be a CapsuleCollider tested with Bounds.Intersects, which was wrong twice
        // over: the capsule's height (0.5) was under 2x its radius so Unity silently clamped it to
        // a sphere, and an AABB test added the player's own 0.3 radius on top, giving a +-0.8m
        // square around a disc of radius 0.35. You could stand half a metre clear of the button
        // and it lit up. There is no collider here at all now - the pad is a logical volume.
        public float activationRadius = 0.4f;
        // Measured against the player's feet, not their centre, so the pad doesn't stay lit while
        // they jump off it. The jump clears 0.9m, well past this.
        public float footClearance = 0.35f;

        public bool PlayerHolding { get; private set; }
        private readonly HashSet<GhostReplayer> ghostsHolding = new HashSet<GhostReplayer>();

        public bool IsActive => PlayerHolding || ghostsHolding.Count > 0;

        // A hold button records the level directly - the ghost stands on it for exactly as long as
        // the player did, which is the whole mechanic (spec §5).
        public override bool PlayerSignal => PlayerHolding;

        private Collider playerCollider;

        private void Start()
        {
            UpdateVisual();
        }

        // Deliberately polled rather than driven by OnTriggerEnter/Exit. The loop teleports the
        // player by disabling and re-enabling the CharacterController inside a single frame, so if
        // they were standing on the button when the iteration ended, the exit callback never
        // arrives - the button then stays lit forever with nobody on it, and because PlayerRecorder
        // samples PlayerHolding, every ghost recorded afterwards holds it forever too.
        private void FixedUpdate()
        {
            if (playerCollider == null)
            {
                GameObject player = GameObject.FindGameObjectWithTag("Player");
                if (player != null) playerCollider = player.GetComponent<Collider>();
            }

            bool holding = playerCollider != null && playerCollider.enabled && IsStandingOnPad();

            if (holding != PlayerHolding)
            {
                PlayerHolding = holding;
                UpdateVisual();
            }
        }

        public override void SetGhostSignal(GhostReplayer ghost, bool active)
        {
            if (active) ghostsHolding.Add(ghost);
            else ghostsHolding.Remove(ghost);
            UpdateVisual();
        }

        private bool IsStandingOnPad()
        {
            Bounds player = playerCollider.bounds;
            Vector3 pad = transform.position;

            float dx = player.center.x - pad.x;
            float dz = player.center.z - pad.z;
            if (dx * dx + dz * dz > activationRadius * activationRadius) return false;

            float feet = player.min.y;
            return feet >= pad.y - footClearance && feet <= pad.y + footClearance;
        }

        private void UpdateVisual()
        {
            if (buttonRenderer != null)
                buttonRenderer.material.color = IsActive ? activeColor : inactiveColor;
        }
    }
}
