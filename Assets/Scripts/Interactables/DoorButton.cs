using UnityEngine;

namespace IterationRoom
{
    // One-touch button: pressing attempts to open the door, but only succeeds
    // while the required FloorButton is active (held by the player or a ghost).
    [RequireComponent(typeof(Collider))]
    public class DoorButton : GhostInteractable
    {
        public FloorButton requiredFloorButton;
        public Door door;
        public Renderer buttonRenderer;
        public Color idleColor = Color.white;
        public Color deniedColor = Color.red;
        public Color grantedColor = new Color(0.2f, 1f, 0.4f);

        // How long a press stays "on" in the recording. A press is instantaneous, but a ghost
        // advances its timeline by elapsed time and can skip several recorded frames in one tick,
        // so a press held for a single frame would eventually be missed entirely. Stretching it
        // over a handful of frames makes it survive the scrub; keeping it short means two
        // deliberate presses still record as two.
        public float pressPulseDuration = 0.15f;

        private Collider trigger;
        private Collider playerCollider;
        private bool playerInRange;
        private float pressPulseUntil = -1f;

        // The stretched pulse, not "player is in range" - otherwise a second press made while
        // already standing at the button (tapping E after a denial) would never be recorded.
        public override bool PlayerSignal => Time.time < pressPulseUntil;

        // Only the rising edge carries meaning. This is a one-touch button, so the fall is just
        // the recorded pulse expiring, not the player letting go of anything.
        public override void SetGhostSignal(GhostReplayer ghost, bool active)
        {
            if (active) TryPress();
        }

        private void Awake()
        {
            trigger = GetComponent<Collider>();
        }

        // Range is polled (bounds overlap) rather than driven by OnTriggerEnter/Exit, for exactly
        // the reason FloorButton is - see the note there. The loop teleports the player by
        // disabling and re-enabling the CharacterController inside a single frame, so standing at
        // this button when the iteration ended never produced an exit callback: playerInRange
        // stayed true for the rest of the run and E then pressed the button from anywhere in the
        // room, including from the bed. Don't put this back on trigger events.
        private void FixedUpdate()
        {
            if (playerCollider == null)
            {
                GameObject player = GameObject.FindGameObjectWithTag("Player");
                if (player != null) playerCollider = player.GetComponent<Collider>();
            }

            bool inRange = playerCollider != null
                && playerCollider.enabled
                && trigger.bounds.Intersects(playerCollider.bounds);

            // One-touch: pressing happens on the rising edge, i.e. the moment the player arrives.
            if (inRange && !playerInRange) RegisterPlayerPress();
            playerInRange = inRange;
        }

        private void Update()
        {
            if (playerInRange && Input.GetKeyDown(KeyCode.E))
                RegisterPlayerPress();
        }

        // The player's own presses, and only those, raise the pulse that PlayerRecorder samples.
        // A ghost's replayed press goes straight to TryPress instead: route it through here and it
        // would be re-recorded as if the player had done it, so each iteration would inherit every
        // press of the one before and the door would open earlier and earlier until it opened by
        // itself on frame one.
        private void RegisterPlayerPress()
        {
            pressPulseUntil = Time.time + pressPulseDuration;
            TryPress();
        }

        private void TryPress()
        {
            bool active = requiredFloorButton != null && requiredFloorButton.IsActive;
            Flash(active ? grantedColor : deniedColor);
            if (active) door?.Open();
        }

        private void Flash(Color color)
        {
            if (buttonRenderer == null) return;
            buttonRenderer.material.color = color;
            CancelInvoke(nameof(ResetColor));
            Invoke(nameof(ResetColor), 0.3f);
        }

        private void ResetColor()
        {
            if (buttonRenderer != null) buttonRenderer.material.color = idleColor;
        }
    }
}
