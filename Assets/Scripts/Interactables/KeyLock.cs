using UnityEngine;

namespace IterationRoom
{
    // Room2's door is a key door, not a button door: no floor pad, no condition to hold open. You
    // either have what a balloon gave up or you do not.
    //
    // NOT a GhostInteractable, and that is the whole design of the thing. The rule the rest of the
    // room runs on is "record the attempt, re-evaluate the condition" - DoorButton records a press
    // and re-checks the pad when a ghost replays it. That only holds while the condition being
    // re-evaluated is the one that actually enabled the action. Here the enabling condition is the
    // player HOLDING the key, and a ghost has no pockets: carrying is not part of a recording, and
    // ghosts have no colliders to pick anything up with. An earlier version re-evaluated a ghost's
    // unlock against "has the key's balloon been burst yet", which is a different and much weaker
    // fact, and it quietly let a ghost do the one thing a ghost demonstrably cannot.
    //
    // Range is polled, not driven by trigger callbacks - see FloorButton for why.
    [RequireComponent(typeof(Collider))]
    public class KeyLock : MonoBehaviour, IInteractHintTarget
    {
        public Door door;
        public string requiredItemId = "Key";

        public Renderer lockRenderer;
        public Color idleColor = Color.white;
        public Color deniedColor = Color.red;
        public Color grantedColor = new Color(0.2f, 1f, 0.4f);

        // Where an accepted key is parked, over the keyhole on the plate. The insertion is literal
        // - the key object leaves the player's pocket and appears here - so that "a ghost cannot do
        // this" is visible on screen rather than only true in the code.
        public Transform keySocket;

        private Collider trigger;
        private Collider playerCollider;
        private PlayerHand hand;
        private bool playerInRange;

        // Read by DoorIndicator so the lamp above this door reports "you are carrying the key" the
        // same way the other one reports "the pad is held".
        public bool CanOpen => hand != null && hand.Has(requiredItemId);

        public bool WantsInteractHint => playerInRange && !IsSpent;
        public Transform HintAnchor => transform;

        // Once the door is open there is nothing left for E to do here. The loop shuts the door
        // again and takes the key back, so this is per-iteration, not permanent.
        private bool IsSpent => door != null && door.IsOpen;

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

            // Unlike DoorButton this has never fired on arrival, and DoorButton has now been
            // brought into line with it: walking into a locked door should not burn the attempt
            // before the player has understood that it is a lock.
            playerInRange = playerCollider != null
                && playerCollider.enabled
                && trigger.bounds.Intersects(playerCollider.bounds);
        }

        private void Update()
        {
            if (!playerInRange || IsSpent) return;
            if (LoopManager.Instance != null && !LoopManager.Instance.IterationRunning) return;

            if (Input.GetKeyDown(KeyCode.E)) TryUnlock();
        }

        private void TryUnlock()
        {
            if (!CanOpen)
            {
                Flash(deniedColor);
                return;
            }

            // Surrendered before the door moves, so the order on screen is the order of cause:
            // the key goes in, and then the door opens. It leaves the player's inventory for good
            // this iteration - the loop is what hands it back, at the top of the next one.
            CarryableItem key = hand.Surrender(requiredItemId);
            key?.InsertInto(keySocket);

            Flash(grantedColor);
            door?.Open();
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
