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

        // Positional, so the pad can be heard going down from across the room. That is the point of
        // it: the door lamp already says "the condition is met", but only if you happen to be
        // looking at the door. The clunk tells you a ghost just stepped on the pad behind you.
        public AudioSource audioSource;
        public AudioClip pressClip;
        public AudioClip releaseClip;

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
        // Last state the pad was heard in, so the clunk fires on the change rather than on the
        // level - UpdateVisual runs on every ghost signal and every FixedUpdate poll.
        private bool wasActive;

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
            bool active = IsActive;

            if (buttonRenderer != null)
                buttonRenderer.material.color = active ? activeColor : inactiveColor;

            if (active != wasActive)
            {
                wasActive = active;
                PlayStateChange(active);
            }
        }

        // The pad is a physical thing, so it makes the same noise whoever stands on it - the player
        // or a ghost. Only the edges make a sound; a hold button that ticked while held would be
        // unbearable across a 60-second iteration.
        //
        // Silent unless the clock is actually running, for the same reason Door.Close() is silent:
        // the loop releases every ghost during the reset, and a rack of pads letting go behind the
        // closed eyelids is the machinery of the loop showing through rather than a sound the room
        // makes.
        private void PlayStateChange(bool pressed)
        {
            if (audioSource == null) return;
            if (LoopManager.Instance != null && !LoopManager.Instance.IterationRunning) return;

            AudioClip clip = pressed ? pressClip : releaseClip;
            if (clip != null) audioSource.PlayOneShot(clip);
        }
    }
}
