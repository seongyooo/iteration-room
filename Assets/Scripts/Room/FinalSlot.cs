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
    // AN IItemSocket, like every other place in the game an object gets handed over. It used to
    // deliberately not be one, on the reasoning that these slots were "past the end of the run,
    // where no ghost can ever reach" - which stopped being true the moment the clock started
    // running through Room4. With no socket registered, GhostReplayer's completed-errand rule
    // (`ItemRegistry.FindSocket(itemId) != null`) never engaged for these three ids at all: a ghost
    // that picked up an escape object without delivering it kept re-taking it every iteration
    // afterwards and could never hand it back, holding it hostage for the rest of that ghost's
    // timeline. Registering the socket is what lets a past self do this errand properly - take it,
    // carry it here, put it in - exactly as ghosts already do for the key and every accumulation
    // room's pieces, and `RecordedTimeline.Delivers` already decides correctly whether a given
    // recording actually finished the hand-over.
    //
    // `ItemRegistry` maps an id to exactly ONE socket, so this is only safe because nothing else in
    // the game currently registers a socket for a red cube, blue sphere or yellow triangle id - if
    // one of their home rooms ever grows a socket of its own, the two would collide silently.
    //
    // INSIDE THE LOOP, which it did not used to be. Room4 was past the end of the run and this
    // gated on `FinalRoomSequence.Active` alone, because `AcceptsInput` was false for every moment
    // the fixture was alive. The clock runs through Room4 now, so this gates on BOTH: the loop
    // accepting input at all, and the console being up - a recess sunk under the floor takes
    // nothing, from a ghost as much as from the player. A ghost's recorded delivery only succeeds
    // if THIS iteration's console has already risen by the time its timestamp comes round - which
    // means the living player has to have reached Room4 first - and a delivery that cannot land
    // yet is refused exactly like Room2's lock refuses an already-open door: the ghost keeps
    // carrying it to the end of its own timeline.
    //
    // Range is polled, not driven by trigger callbacks - see FloorButton for why.
    [RequireComponent(typeof(Collider))]
    public class FinalSlot : MonoBehaviour, IInteractHintTarget, IItemSocket
    {
        // WHICH object this recess takes. Empty means "nothing yet" - the shape is built and the
        // fixture is wired, but the object that fits it does not exist in the game. An unconfigured
        // slot accepts nothing, prompts for nothing and counts for nothing.
        public string acceptedItemId = string.Empty;

        // WHAT IT WILL TAKE A PRESS FOR, which is not the same question. Empty - every recess built
        // before room2-0 - means the two are the same and the recess is only interested in the one
        // object that fits it.
        //
        // ROOM2-0 IS WHY THIS EXISTS. Its four pedestals all want a billiard ball and each wants a
        // DIFFERENT one, and which is which is the puzzle: the ball's number has to match how many of
        // the object drawn on the pedestal's side cycle 2 contains. Left as it was, this fixture gave
        // that away for free - `WantsInteractHint` requires the right object in hand, so the rim
        // lighting up WAS the answer, readable by walking the row holding each ball in turn without
        // ever pressing anything or reading a pictogram.
        //
        // Naming the whole family instead makes every ball a question the player has to ASK. The
        // prompt appears for any of them, the press either lands or is refused, and a refusal costs
        // the walk back to the drawer - which is what makes counting the axes cheaper than guessing.
        //
        // A REFUSAL IS NOT A DEAD END, and that is why this rather than "the pedestal keeps whatever
        // it is given". A wrong ball that STAYED would be reproduced by that iteration's ghost for
        // the rest of the run, blocking a pedestal the living player then has to clear by hand every
        // sixty seconds. Refused, the wrong delivery simply fails again each time and costs nothing.
        // See docs/puzzle-design.md.
        public string[] offerItemIds;

        // The colour of no. Held for `refusedSeconds`, after which the rim goes back to saying
        // whatever is true - the same instrument `KeyLock` answers a wrong key with, for the same
        // reason: the player asked this fixture a question and it owes them an answer.
        public Color refusedColor = new Color(0.88f, 0.11f, 0.11f);
        public float refusedEmission = 1.8f;
        public float refusedSeconds = 0.45f;
        public AudioClip refuseClip;

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

        // How the object arrives - see SymbolSlot for the same three, and SocketInsert for what they
        // mean. Up, here, because the recesses are wells in the top of the console.
        public Vector3 insertOffer = new Vector3(0f, 0.40f, 0f);
        public Vector3 insertTilt = new Vector3(6f, 20f, -5f);
        public float insertDuration = 0.5f;

        public bool Filled { get; private set; }

        // Declared means "there is an object in the game that belongs here". An unconfigured slot is
        // a hole in a console, not a requirement.
        public bool Declared => !string.IsNullOrEmpty(acceptedItemId);

        private Collider trigger;
        private bool playerInRange;
        private float refusedUntil;
        private readonly LitRendererPainter painter = new LitRendererPainter();

        private bool Live => sequence != null && sequence.Active
            && (LoopManager.Instance == null || LoopManager.Instance.AcceptsInput);

        // E here would OFFER the held object. Everything is required: the room has to be live, the
        // object has to be one this recess entertains and IN HAND rather than merely carried, and the
        // recess has to be empty.
        // ...and it has to be ON SCREEN, like every other E fixture - see PlayerLookup.InView.
        //
        // OFFERED, NOT ACCEPTED. With no family declared the two are the same test and this reads
        // exactly as it always did; with one, the press is a question and the answer is below.
        public bool WantsInteractHint =>
            Live && Declared && !Filled && playerInRange
            && hand != null && hand.Held != null && Offers(hand.Held.itemId)
            && PlayerLookup.InView(HintAnchor);

        // "Would this recess take a press for that object?" An empty family means only the accepted
        // one, which is what every recess outside room2-0 wants.
        public bool Offers(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return false;
            if (offerItemIds == null || offerItemIds.Length == 0) return itemId == acceptedItemId;

            for (int i = 0; i < offerItemIds.Length; i++)
                if (offerItemIds[i] == itemId) return true;

            return false;
        }

        public Transform HintAnchor => seat != null ? seat : transform;

        // IItemSocket. Only meaningful when Declared - see OnEnable/OnDisable, which only register
        // this slot at all once it names a real object.
        public string AcceptedItemId => acceptedItemId;

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

        // Undeclared slots register nothing: an empty acceptedItemId has no business claiming a
        // socket, and ItemRegistry.RegisterSocket already no-ops on an empty id, but skipping it
        // here says so directly rather than relying on that guard.
        private void OnEnable()
        {
            if (Declared) ItemRegistry.RegisterSocket(this);
        }

        private void OnDisable()
        {
            if (Declared) ItemRegistry.UnregisterSocket(this);
        }

        private void FixedUpdate()
        {
            playerInRange = PlayerLookup.InReach(trigger);
        }

        private void Update()
        {
            if (Time.time < refusedUntil) ApplyRim(refusedColor, refusedEmission);
            else if (Filled) ApplyRim(filledColor, filledEmission);
            else if (WantsInteractHint) ApplyRim(readyColor, readyEmission);
            else ApplyRim(idleColor, idleEmission);

            if (!WantsInteractHint) return;
            // ON SCREEN, NOT BEHIND ANYTHING, AND THE THING BEING LOOKED AT - the same answer the
            // prompt disc is drawn from, so E acts exactly where the mark is. See
            // PlayerLookup.PressGoesTo.
            if (!PlayerLookup.PressGoesTo(this)) return;

            if (!GameInput.InteractPressed) return;
            // Checked as well as claimed - see PlayerLookup.InteractTaken.
            if (PlayerLookup.InteractTaken) return;

            // CLAIMED HERE, and claimed for a refusal as much as for an acceptance. That is the
            // narrow case CLAUDE.md SS1.2 allows: "only when it actually acts" is about presses this
            // fixture was never offered, and `WantsInteractHint` has just said this one was. Reading
            // the same press as "put the ball down" on top of a red flash would be two answers to one
            // question - which is the argument `KeyLock` already makes for a wrong key.
            hand.MarkInteract();

            if (hand.Held.itemId != acceptedItemId) { Refuse(); return; }

            // Surrendered rather than taken straight out of the hand: PlayerHand owns `carried`,
            // `carriedItems` and which item is out, and CarryableItem knows about none of it.
            CarryableItem given = hand.Surrender(acceptedItemId);
            if (given == null) return;

            Accept(given);
        }

        // The wrong ball. Nothing changes hands and nothing is recorded - a refusal is not a
        // `CarryEvent`, so no ghost ever replays a guess, which is the other half of why a wrong
        // answer here cannot accumulate across a run.
        private void Refuse()
        {
            refusedUntil = Time.time + refusedSeconds;
            painter.ForceNextRepaint();
            if (audioSource != null && refuseClip != null) audioSource.PlayOneShot(refuseClip);
        }

        // A past self's own delivery, replaying the same errand the player performs above. Refuses
        // rather than throws when it cannot be finished right now - the recess is not live yet, the
        // ghost is carrying the wrong thing, or someone already filled it - which leaves the ghost
        // holding the object exactly the way Room2's lock leaves a ghost holding an already-spent
        // key: a real outcome, not an error.
        public bool AcceptFromGhost(CarryableItem item)
        {
            if (item == null || item.itemId != acceptedItemId) return false;
            if (!Live || Filled) return false;

            Accept(item);
            return true;
        }

        // InsertInto, not DropAt - the object is out of play now, visible where it was put and not
        // takeable back out. There is no loop left to rewind it. Shared by the player's own
        // placement and a ghost's, so both land in exactly one piece of code.
        private void Accept(CarryableItem item)
        {
            Transform socket = seat != null ? seat : transform;
            item.InsertInto(socket);
            Filled = true;

            // `Filled` is set before the slide rather than after it, and that is the answer to "what
            // if the third object is still moving when the console decides the run is over": the
            // console's condition is about custody, which InsertInto has already settled. The object
            // finishes seating itself while the ending plays, which is what it would look like
            // anyway. Straight DOWN, because these recesses are wells in the top of the console -
            // see SocketInsert on why each caller states its own direction.
            StartCoroutine(SocketInsert.Slide(item, socket, insertOffer, insertTilt, insertDuration,
                                              audioSource, insertClip));
        }

        // The loop rewinding. The object itself is already back on its own plinth by now; this only
        // forgets that it was ever here. It exists because Room4 is something an iteration can now
        // fail at and come back to.
        public void Clear()
        {
            Filled = false;
            refusedUntil = 0f;
            painter.ForceNextRepaint();
        }

        // Through a property block, because the three rims share one material and each says
        // something different about itself.
        private void ApplyRim(Color color, float emission) => painter.Paint(rimRenderer, color, color * emission);
    }
}
