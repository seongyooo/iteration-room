using UnityEngine;

namespace IterationRoom
{
    // Room2's door is a key door, not a button door: no floor pad, no condition to hold open. You
    // either have what a balloon gave up or you do not.
    //
    // Range is polled, not driven by trigger callbacks - see FloorButton for why.
    [RequireComponent(typeof(Collider))]
    public class KeyLock : GhostInteractable
    {
        public Door door;
        public string requiredItemId = "Key";

        public Renderer lockRenderer;
        public Color idleColor = Color.white;
        public Color deniedColor = Color.red;
        public Color grantedColor = new Color(0.2f, 1f, 0.4f);

        // Same pulse-stretching as DoorButton, for the same reason.
        public float pressPulseDuration = 0.15f;

        private Collider trigger;
        private Collider playerCollider;
        private PlayerHand hand;
        private bool playerInRange;
        private float pressPulseUntil = -1f;

        public override bool PlayerSignal => Time.time < pressPulseUntil;

        // A ghost cannot carry anything - carrying is not part of a recording - so its unlock is
        // re-evaluated against the one condition that IS reproducible: has the key been let out of
        // its balloon yet this iteration. The ghost could only have unlocked this door by holding
        // the key, and it could only have held the key if that balloon had been burst, which the
        // ghost chain reproduces exactly. Re-checking rather than blindly opening also keeps the
        // rule the rest of the room follows: record the attempt, re-evaluate the condition.
        public override void SetGhostSignal(GhostReplayer ghost, bool active)
        {
            if (!active) return;
            if (BalloonField.Instance != null && BalloonField.Instance.KeyRevealed) door?.Open();
        }

        // Read by DoorIndicator so the lamp above this door reports "you are carrying the key"
        // the same way the other one reports "the pad is held".
        public bool CanOpen => hand != null && hand.Has(requiredItemId);

        private void Awake()
        {
            trigger = GetComponent<Collider>();
        }

        private void FixedUpdate()
        {
            if (playerCollider == null)
            {
                GameObject player = GameObject.FindGameObjectWithTag("Player");
                if (player != null)
                {
                    playerCollider = player.GetComponent<Collider>();
                    hand = player.GetComponent<PlayerHand>();
                }
            }

            bool inRange = playerCollider != null
                && playerCollider.enabled
                && trigger.bounds.Intersects(playerCollider.bounds);

            // Unlike DoorButton this does not fire on arrival. Walking into a locked door should
            // not burn the attempt before the player has understood it is a lock.
            playerInRange = inRange;
        }

        private void Update()
        {
            if (!playerInRange) return;
            if (LoopManager.Instance != null && !LoopManager.Instance.IterationRunning) return;

            if (Input.GetKeyDown(KeyCode.E)) TryUnlock();
        }

        private void TryUnlock()
        {
            pressPulseUntil = Time.time + pressPulseDuration;
            bool ok = CanOpen;
            Flash(ok ? grantedColor : deniedColor);
            if (ok) door?.Open();
        }

        private void Flash(Color color)
        {
            if (lockRenderer == null) return;
            lockRenderer.material.color = color;
            CancelInvoke(nameof(ResetColor));
            Invoke(nameof(ResetColor), 0.3f);
        }

        private void ResetColor()
        {
            if (lockRenderer != null) lockRenderer.material.color = idleColor;
        }
    }
}
