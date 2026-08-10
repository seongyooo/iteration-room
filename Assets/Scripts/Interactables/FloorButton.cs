using System.Collections.Generic;
using UnityEngine;

namespace IterationRoom
{
    // Hold-type button: active only while the real player OR at least one ghost
    // is currently occupying it. No toggle state - releasing deactivates instantly.
    [RequireComponent(typeof(Collider))]
    public class FloorButton : GhostInteractable
    {
        public Renderer buttonRenderer;
        public Color inactiveColor = Color.white;
        public Color activeColor = new Color(0.2f, 1f, 0.4f);

        public bool PlayerHolding { get; private set; }
        private readonly HashSet<GhostReplayer> ghostsHolding = new HashSet<GhostReplayer>();

        public bool IsActive => PlayerHolding || ghostsHolding.Count > 0;

        // A hold button records the level directly - the ghost stands on it for exactly as long as
        // the player did, which is the whole mechanic (spec §5).
        public override bool PlayerSignal => PlayerHolding;

        private Collider trigger;
        private Collider playerCollider;

        private void Awake()
        {
            trigger = GetComponent<Collider>();
        }

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

            bool holding = playerCollider != null
                && playerCollider.enabled
                && trigger.bounds.Intersects(playerCollider.bounds);

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

        private void UpdateVisual()
        {
            if (buttonRenderer != null)
                buttonRenderer.material.color = IsActive ? activeColor : inactiveColor;
        }
    }
}
