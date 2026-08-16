using UnityEngine;

namespace IterationRoom
{
    // Room2's door is a key door: no floor pad, no condition to hold open, and unlike Room1's it
    // does not open by itself. You either have what a balloon gave up or you do not.
    //
    // Still NOT a GhostInteractable - a signal is a level sampled into a bit, and this is an act
    // performed with an object. But A GHOST CAN OPEN IT, through AcceptFromGhost.
    //
    // That reverses what this file used to say. The rule everything here runs on is "record the
    // attempt, re-evaluate the condition", and it only holds while the condition re-evaluated is the
    // one that actually enabled the action. The enabling condition is the key IN HAND. An early
    // version re-evaluated a ghost's unlock against "has the key's balloon been burst yet" - a
    // different and much weaker fact - and that was taken as proof that a ghost could never do this
    // at all. It was only ever proof that the wrong condition had been chosen. Possession is
    // recorded state now, so the right one is available: AcceptFromGhost fires only when the ghost
    // is genuinely holding the key it recorded picking up, and has it equipped rather than stowed -
    // exactly what the living player has to do. See docs/ghost-possession-design.md.
    //
    // Range is polled, not driven by trigger callbacks - see FloorButton for why.
    [RequireComponent(typeof(Collider))]
    public class KeyLock : MonoBehaviour, IInteractHintTarget, IItemSocket
    {
        public Door door;
        public string requiredItemId = "Key";

        public Renderer lockRenderer;
        public Color deniedColor = Color.red;
        public Color grantedColor = new Color(0.2f, 1f, 0.4f);

        // THE PLATE'S REST COLOUR IS THE PLATE'S OWN MATERIAL, captured in Awake rather than set as
        // a field. It used to be a serialized `idleColor` defaulting to white, which nothing ever
        // assigned - so the first flash of ANY kind, granted or denied, faded the plate to white
        // 0.3s later and left it white for the rest of the iteration. On a door whose colour is the
        // only instruction the player gets about which key it wants, that is the one thing the
        // fixture must not do: a lock that has been opened once stops saying what it is.
        //
        // Reading it off the renderer is not the component choosing a value - SceneBuilder still
        // authors the colour, from the same KeySpec the key itself is built from. It is the
        // component remembering what it was, which is exactly the kind of thing it should own, and
        // it makes a third coloured door impossible to get wrong by forgetting a field.
        private Color idleColor = Color.white;

        // Where an accepted key ends up, seated in the keyhole. The insertion is literal - the key
        // object leaves the player's pocket and goes in - so that "a ghost cannot do this" is
        // visible on screen rather than only true in the code.
        //
        // The socket's frame is set up by SceneBuilder.BuildKeyModel: +Z is into the lock, so the
        // insertion is a slide along local Z and the turn is a roll about it. Nothing here has to
        // know which way the model was authored.
        public Transform keySocket;

        // Metres the key travels on the way in - the length of it that ends up buried. Set from the
        // model's own measurements at build time rather than picked.
        public float insertTravel = 0.13f;
        public float insertDuration = 0.35f;
        // A beat with the key seated and not yet turning. Without it the slide and the turn read as
        // one continuous swipe; with it, the key arrives, and then it is turned.
        public float settleDuration = 0.08f;
        public float turnDuration = 0.3f;
        // Clockwise from where the player stands. Positive about +Z is clockwise seen from behind,
        // which is where the player is.
        public float turnAngle = 90f;

        private Collider trigger;
        private PlayerHand hand;
        private bool playerInRange;
        private bool inserting;
        private CarryableItem insertingItem;

        // `inserting` is set by a coroutine, and a coroutine only discovers that the world moved
        // underneath it on its NEXT frame. An iteration can end mid-turn, and the reset takes the
        // key straight back out of the socket - between those two moments the flag says an insertion
        // is in progress when the key is already back in its balloon, and every gate below refuses.
        //
        // In the running game the wake-up hands over dozens of frames and the coroutine always wins
        // that race. This makes it not a race: the key having left the socket IS the end of the
        // insertion, whatever the flag still says.
        private bool Inserting
        {
            get
            {
                if (inserting && (insertingItem == null || insertingItem.transform.parent != keySocket))
                    inserting = false;
                return inserting;
            }
        }

        // Read by DoorIndicator so the lamp above this door reports "you are carrying the key" the
        // same way the other one reports "the pad is held".
        //
        // HOLDING, not Has. The key has to be OUT - Tab it into your hand and then put it in the
        // hole. Carrying it in a pocket is no longer enough: the lock is operated with an object
        // rather than satisfied by an inventory check.
        //
        // True through the insertion as well, and that is not a fudge: the key is in the lock, so
        // the door can open. Reading it off the hand alone would drop the lamp to red for the three
        // quarters of a second between the key leaving the player and the door moving - announcing
        // a failure in the middle of a success.
        public bool CanOpen => Inserting || (hand != null && hand.Holding(requiredItemId));

        // On screen as well as in reach, like every other E fixture - see PlayerLookup.InView.
        public bool WantsInteractHint =>
            playerInRange && !IsSpent && !Inserting && PlayerLookup.InView(HintAnchor);
        public Transform HintAnchor => transform;

        // Once the door is open there is nothing left for E to do here. The loop shuts the door
        // again and takes the key back, so this is per-iteration, not permanent.
        private bool IsSpent => door != null && door.IsOpen;

        public string AcceptedItemId => requiredItemId;

        private void Awake()
        {
            trigger = GetComponent<Collider>();
            // sharedMaterial, deliberately: `material` instantiates a copy, and doing that in Awake
            // would leak one per lock on every load for no reason. Flash() is where the instance is
            // wanted, and it is only reached if the player touches the thing.
            if (lockRenderer != null && lockRenderer.sharedMaterial != null)
                idleColor = lockRenderer.sharedMaterial.color;
        }

        private void OnEnable() => ItemRegistry.RegisterSocket(this);
        private void OnDisable() => ItemRegistry.UnregisterSocket(this);

        // A past self putting the key in. The player does not have to be anywhere near, and after
        // the first iteration that solves Room2 they never have to do it again - which is the point
        // of the change, and what a play-tester expected the room to do in the first place.
        //
        // Refuses rather than throws when there is nothing to do: another ghost may already have
        // opened this door, and a second key going into an open lock would be two keys. The refusal
        // is a real outcome - the ghost keeps carrying, and drops the key when its timeline ends.
        public bool AcceptFromGhost(CarryableItem item)
        {
            if (item == null || keySocket == null) return false;
            if (item.itemId != requiredItemId) return false;
            if (IsSpent || Inserting) return false;

            item.InsertInto(keySocket);
            StartCoroutine(InsertAndTurn(item));
            return true;
        }

        private void FixedUpdate()
        {
            Collider playerCollider = PlayerLookup.Collider;
            hand = PlayerLookup.Hand;

            // Range only - arriving is deliberately not an attempt. Walking into a locked door
            // should not burn the try before the player has understood that it is a lock. This is
            // now the only door in the game that asks for a press at all; Room1's opens on its pad.
            playerInRange = playerCollider != null
                && playerCollider.enabled
                && trigger.bounds.Intersects(playerCollider.bounds);
        }

        private void Update()
        {
            // inserting is in the guard as well as in WantsInteractHint: the door is not open yet
            // during the animation, so IsSpent is still false and a second press would surrender a
            // key the player no longer has and start the whole thing again on top of itself.
            if (!playerInRange || IsSpent || Inserting) return;
            if (LoopManager.Instance != null && !LoopManager.Instance.AcceptsInput) return;

            if (!GameInput.InteractPressed) return;
            // Checked as well as claimed - see PlayerLookup.InteractTaken.
            if (PlayerLookup.InteractTaken) return;

            TryUnlock();
        }

        private void TryUnlock()
        {
            if (!CanOpen)
            {
                // EMPTY HANDS ARE NOT A REFUSAL, they are not a press at this fixture at all - and
                // claiming one would deadlock the room. A key lying on the floor beside its own lock
                // is exactly where the player wants to press E to PICK IT UP, and a lock that ate
                // every press to flash red at an empty hand would make that key unreachable.
                //
                // Holding the wrong thing IS a refusal, and gets the flash and the press: the player
                // asked this fixture a question and it answered no, and reading that same press as
                // "put it down" on top of the red flash would be two answers to one key.
                if (hand == null || !hand.HandsFull) return;

                hand.MarkInteract();
                Flash(deniedColor);
                return;
            }

            // Claimed here rather than in Update, so that everything above this line - out of range,
            // spent, mid-insert, empty-handed - leaves the press for whoever else wants it.
            hand.MarkInteract();

            // Surrendered before anything moves, so the order on screen is the order of cause: the
            // key leaves the player, goes in, is turned, and then the door opens. It is gone from
            // the inventory for good this iteration - the loop hands it back at the top of the next.
            CarryableItem key = hand.Surrender(requiredItemId);
            key?.InsertInto(keySocket);

            // CanOpen said yes, so the door owes the player an opening whatever happened to the
            // object. Without the fallback a missing key would leave a lock that accepts the press
            // and does nothing, which is the worst failure this door has.
            if (key == null || keySocket == null)
            {
                Flash(grantedColor);
                door?.Open();
                return;
            }

            StartCoroutine(InsertAndTurn(key));
        }

        // Slide in, settle, turn, and only then open. Scaled time throughout, like everything else
        // that moves in this room - pausing mid-turn freezes the key with it.
        private System.Collections.IEnumerator InsertAndTurn(CarryableItem key)
        {
            inserting = true;
            insertingItem = key;

            Transform t = key.transform;
            Vector3 seated = Vector3.zero;
            Vector3 offered = new Vector3(0f, 0f, -insertTravel);
            Quaternion straight = Quaternion.identity;
            Quaternion turned = Quaternion.Euler(0f, 0f, turnAngle);

            t.localPosition = offered;
            t.localRotation = straight;

            for (float e = 0f; e < insertDuration; e += Time.deltaTime)
            {
                if (!StillInSocket(key)) { inserting = false; yield break; }
                // Eased out, because a key does not arrive at a constant speed: it goes in fast and
                // the last few millimetres are the pins taking it.
                float u = Mathf.Clamp01(e / insertDuration);
                t.localPosition = Vector3.Lerp(offered, seated, 1f - (1f - u) * (1f - u));
                yield return null;
            }
            t.localPosition = seated;

            yield return new WaitForSeconds(settleDuration);

            for (float e = 0f; e < turnDuration; e += Time.deltaTime)
            {
                if (!StillInSocket(key)) { inserting = false; yield break; }
                t.localRotation = Quaternion.Slerp(straight, turned,
                    Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(e / turnDuration)));
                yield return null;
            }
            t.localRotation = turned;

            inserting = false;
            Flash(grantedColor);
            // The door's own sound lands here rather than a separate lock click, which is the right
            // read anyway: the turn is what releases the door, so the door is what you hear.
            door?.Open();
        }

        // An iteration can end mid-turn. PlayerHand.ReturnAll reparents the key back to where it
        // came from, and a coroutine still writing localPosition would drag it off across the room
        // - and then open a door the loop had just shut. Losing the socket is the signal to stop.
        private bool StillInSocket(CarryableItem key)
        {
            return key != null && keySocket != null && key.transform.parent == keySocket;
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
