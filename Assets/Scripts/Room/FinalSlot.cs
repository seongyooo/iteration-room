using UnityEngine;

namespace IterationRoom
{
    // One shaped recess in Room4's console, and one of the three objects the escape wants in it.
    //
    // Operated with E while the object is IN HAND, which is `KeyLock`'s idiom and not the chess
    // room's left click. The difference is not a preference: the board is a surface you AIM at from
    // across a room, and a socket is a fixture you STAND at - the same distinction that already
    // separates the lock from the balloon tool.
    //
    // NOT AN IItemSocket, and that is deliberate rather than an omission.
    //
    //   - `ItemRegistry` maps an id to exactly ONE socket. If the red cube ever gains a socket back
    //     in the room that produced it, a second one here would silently replace it.
    //   - A registered socket changes the COMPLETED-ERRAND RULE for that id: a ghost only replays a
    //     pickup it also surrendered. These slots are past the end of the run, where no ghost can
    //     ever reach, so letting them arbitrate what ghosts may carry inside the loop would be a
    //     rule imposed from outside the thing it governs.
    //
    // INSIDE THE LOOP, which it did not used to be. Room4 was past the end of the run and this
    // gated on `FinalRoomSequence.Active` alone, because `AcceptsInput` was false for every moment
    // the fixture was alive. The clock runs through Room4 now, so this gates on BOTH: the loop
    // accepting input at all, and the console being up - a recess sunk under the floor takes
    // nothing.
    //
    // Range is polled, not driven by trigger callbacks - see FloorButton for why.
    [RequireComponent(typeof(Collider))]
    public class FinalSlot : MonoBehaviour, IInteractHintTarget
    {
        // WHICH object this recess takes. Empty means "nothing yet" - the shape is built and the
        // fixture is wired, but the object that fits it does not exist in the game. An unconfigured
        // slot accepts nothing, prompts for nothing and counts for nothing.
        public string acceptedItemId = string.Empty;

        // The run is over by the time this room exists, so this is the liveness test - there is no
        // iteration to ask.
        public FinalRoomSequence sequence;
        public PlayerHand hand;

        // Where an accepted object ends up: parented at local identity, so this transform's own
        // frame decides how it sits. Sunk into the recess by SceneBuilder.
        public Transform seat;

        // The rim around the hole. It lights when the object that fits it is in the hand, which is
        // the whole of how the player learns which shape goes where without being told.
        public Renderer rimRenderer;
        public Color idleColor = new Color(0.24f, 0.24f, 0.27f);
        public float idleEmission = 0.15f;
        public Color readyColor = Color.white;
        public float readyEmission = 1.8f;
        public Color filledColor = Color.white;
        public float filledEmission = 0.9f;

        public AudioSource audioSource;
        public AudioClip insertClip;

        public bool Filled { get; private set; }

        // Declared means "there is an object in the game that belongs here". An unconfigured slot is
        // a hole in a console, not a requirement.
        public bool Declared => !string.IsNullOrEmpty(acceptedItemId);

        private Collider trigger;
        private bool playerInRange;
        private readonly LitRendererPainter painter = new LitRendererPainter();

        private bool Live => sequence != null && sequence.Active
            && (LoopManager.Instance == null || LoopManager.Instance.AcceptsInput);

        // E here would put the held object in. Everything is required: the room has to be live, the
        // object has to be the RIGHT one and IN HAND rather than merely carried, and the recess has
        // to be empty.
        public bool WantsInteractHint =>
            Live && Declared && !Filled && playerInRange
            && hand != null && hand.Holding(acceptedItemId);

        public Transform HintAnchor => seat != null ? seat : transform;

        // ALL of them, and an empty or null array is deliberately NOT satisfied - the same rule
        // `FloorButton.AllActive` states for its pads, for the same reason. "All zero are filled"
        // vacuously meaning "the escape is ready" is how a device with nothing wired to it opens.
        //
        // Undeclared slots are skipped rather than counted as unfilled: a console with three holes
        // and no objects yet in the game is not a puzzle nobody can solve, it is a console waiting
        // for its objects. That distinction is why `Declared` exists.
        public static bool AllFilled(FinalSlot[] slots)
        {
            if (slots == null || slots.Length == 0) return false;

            int declared = 0;
            foreach (FinalSlot slot in slots)
            {
                if (slot == null || !slot.Declared) continue;
                declared++;
                if (!slot.Filled) return false;
            }

            return declared > 0;
        }

        private void Awake()
        {
            trigger = GetComponent<Collider>();
            ApplyRim(idleColor, idleEmission);
        }

        private void FixedUpdate()
        {
            Collider playerCollider = PlayerLookup.Collider;

            playerInRange = playerCollider != null
                && playerCollider.enabled
                && trigger != null
                && trigger.bounds.Intersects(playerCollider.bounds);
        }

        private void Update()
        {
            if (Filled) ApplyRim(filledColor, filledEmission);
            else if (WantsInteractHint) ApplyRim(readyColor, readyEmission);
            else ApplyRim(idleColor, idleEmission);

            if (!WantsInteractHint) return;
            if (!Input.GetKeyDown(KeyCode.E)) return;

            // Surrendered rather than taken straight out of the hand: PlayerHand owns `carried`,
            // `carriedItems` and which item is out, and CarryableItem knows about none of it.
            CarryableItem given = hand.Surrender(acceptedItemId);
            if (given == null) return;

            // InsertInto, not DropAt - the object is out of play now, visible where it was put and
            // not takeable back out. There is no loop left to rewind it.
            given.InsertInto(seat != null ? seat : transform);
            Filled = true;

            if (audioSource != null && insertClip != null) audioSource.PlayOneShot(insertClip);
        }

        // The loop rewinding. The object itself is already back on its own plinth by now; this only
        // forgets that it was ever here. It exists because Room4 is something an iteration can now
        // fail at and come back to.
        public void Clear()
        {
            Filled = false;
            painter.ForceNextRepaint();
        }

        // Through a property block, because the three rims share one material and each says
        // something different about itself.
        private void ApplyRim(Color color, float emission) => painter.Paint(rimRenderer, color, color * emission);
    }
}
