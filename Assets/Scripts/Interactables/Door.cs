using UnityEngine;

namespace IterationRoom
{
    // A sliding door, opened one of two ways depending on which room it belongs to.
    //
    // Room2's key door LATCHES: KeyLock calls Open() and it stays open for the rest of the
    // iteration, because putting a key in a lock is not something walking away undoes.
    //
    // Room1's and Room3's doors TRACK THEIR PADS instead. Room1 passes one pad; Room3 passes two
    // and needs both held at once, which is that room's escalation - one person cannot stand in two
    // places, so it takes two past selves overlapping in time rather than merely existing.
    //
    // Neither has a control of its own. There was a DoorButton on the wall beside Room1's until a
    // play-test found that nobody could locate it, and that testers held the pad for an iteration,
    // walked to the door in the next and simply expected it to open. They were right to. The button
    // was a second gesture the room never explained and it bought the puzzle nothing: either way
    // the player has to spend one iteration holding the pad down and be at the door in the next.
    //
    // Tracking rather than latching is what keeps that true, and it is not a detail - a door that
    // latched open the instant the pad was touched could be solved in one iteration by stepping on
    // the pad and then strolling through, which deletes the premise the whole game rests on. Step
    // on, it opens across the room; step off, it shuts. The player learns the rule by watching it
    // in the first few seconds, without a line of text, and learns at the same moment why one
    // person cannot do both jobs at once.
    public class Door : MonoBehaviour
    {
        // WHAT HOLDS THIS DOOR OPEN, and a door has exactly one of them.
        //
        // Pads are the original: hold them all down at once. Everything else is a `RoomCondition` -
        // room2-1's number lock, room2-2's chorus. All of them are TRACKED rather than latched - the door
        // follows its condition and shuts again when the condition lapses - which is what makes
        // either of them a thing past selves can satisfy on the player's behalf.
        //
        // Kept as one property rather than two tests at the call site so that the door, its
        // indicator lamp and anything else reading it can never disagree about what "open" means -
        // the same reason `FloorButton.AllActive` lives in one place.
        // The room's own rule, whatever kind it is - a number lock, a chorus of levers, a ratchet.
        // One field rather than one per type: see RoomCondition for why Door asks the question once.
        public RoomCondition condition;

        // THIS DOOR'S ROOM HAS NO PUZZLE YET, so it stands open.
        //
        // Deliberately NOT the same as being wired to nothing. `FloorButton.AllActive` refuses an
        // empty array precisely so a mis-wired door is obvious - it stays shut and somebody notices.
        // A room that is genuinely unbuilt is a different fact, and saying it out loud is what keeps
        // the two from being confused: this flag is a to-do list in the scene.
        //
        // **Every one of these must come off as its room's puzzle lands.** A door left on this after
        // its room is finished is a puzzle that can be walked past.
        public bool openUntilPuzzled;

        public bool HeldOpen =>
            openUntilPuzzled
            || (condition != null ? condition.Satisfied : FloorButton.AllActive(requiredFloorButtons));

        public Transform doorPanel;
        public Vector3 openLocalOffset = new Vector3(0f, 2.2f, 0f);
        public float openDuration = 1.0f;

        // A FIRST PHASE, RUN BEFORE THE SLIDE, and it exists for room3-1's gates.
        //
        // Those are not doors set into a wall - they ARE the wall, built as the same panels at the
        // same depth out of the same material, so that a shut gate is indistinguishable from the
        // panelling beside it. The cost of that is they cannot simply slide: a panel flush with its
        // neighbours has nowhere to go sideways. It has to push back into the cavity first and
        // travel behind them, which is what a concealed panel does in the real world too.
        //
        // ZERO BY DEFAULT, so every ordinary door in the building is untouched - the whole travel is
        // the slide and `Apply` reduces to the single line it was.
        public Vector3 pushInOffset;
        // How much of the open/close travel the push spends. It is a much shorter distance than the
        // slide, so an equal split would look like a shove; a third reads as a latch letting go.
        [Range(0.05f, 0.9f)] public float pushFraction = 0.3f;
        // The lamp above the door is not driven from here - see DoorIndicator, which tracks the
        // floor-button condition rather than this door's state, so it can go green before anything
        // at this end of the room has moved.
        public AudioSource audioSource;
        public AudioClip openClip;

        // The pads that power this door, ALL of which must be held at once. Room1 passes one;
        // Room3 passes two, which is that room's entire puzzle - one person cannot stand in two
        // places, so it takes two past selves holding at the same moment. Room2's key door leaves
        // this empty and latches through Open() instead.
        public FloorButton[] requiredFloorButtons;

        // How close to the opening the player has to be to keep the door from shutting on them.
        // Generous on purpose, and it does double duty: it stops a ghost stepping off the pad from
        // closing the slab through someone standing in the doorway, and it means the intended solve
        // does not fail by half a second when the ghost's timing is a fraction short.
        public float doorwayClearance = 1.2f;

        // Set by Open() only. The pad never sets it - see the class note.
        private bool latched;
        // 0 shut, 1 fully open. The panel's position is derived from this rather than the other way
        // round, so a door caught mid-slide can reverse without a special case.
        private float openAmount;

        public bool IsOpen => latched || openAmount > 0.5f;

        private Vector3 closedLocalPos;
        private Vector3 doorwayCentre;

        private void Awake()
        {
            if (doorPanel == null) return;

            closedLocalPos = doorPanel.localPosition;
            // Cached in world space from the CLOSED position, so it stays put while the slab
            // slides. The door root sits at the room's centre rather than at the opening, so this
            // cannot be measured from the root's own transform.
            doorwayCentre = doorPanel.parent != null
                ? doorPanel.parent.TransformPoint(closedLocalPos)
                : closedLocalPos;
        }

        // Set by Seal() while the final room is shutting this door in view. Update stands off
        // entirely for the duration: the pads are irrelevant by then (ghosts are still standing on
        // Room3's), and two things driving openAmount would fight for it every frame.
        private bool sealing;

        private void Update()
        {
            if (sealing) return;

            float target = latched ? 1f : 0f;

            // Silent and STILL through the wake-up, like everything else the facility does: the
            // loop closes the doors and releases every ghost's signal behind the closed eyelids,
            // and a door moving under a black screen is the machinery showing through.
            //
            // Frozen outright, not merely stopped from tracking its pads. Falling through with
            // an untracked target of 0 slides an open door shut on its own the instant the loop
            // stops - which was invisible while the ending faded out on the same frame, and is
            // not now: the run ends on Room3's threshold and the player then walks into Room4
            // with this door standing open behind them. Shutting it is the last button's job
            // (Seal), and it must not have happened already.
            bool running = LoopManager.Instance == null || LoopManager.Instance.IterationRunning;
            if (!running) return;

            if (!latched)
            {
                if (HeldOpen) target = 1f;
                // Refuses to shut on the player. A CharacterController is not pushed by a moving
                // transform, so a slab closing through one leaves the player inside it, to be
                // squeezed out sideways on the next frame.
                //
                // The openAmount test is load-bearing, not a shortcut: without it, walking up to a
                // shut door would hold it open forever. With it, the reprieve only extends a door
                // that is already open - and it cannot be used to reach one, since the run from the
                // pad to this doorway is 7.7m (1.7s) against a 1.0s close.
                else if (openAmount > 0f && PlayerInDoorway()) target = 1f;
            }

            if (Mathf.Approximately(openAmount, target)) return;

            float step = openDuration > 0f ? Time.deltaTime / openDuration : 1f;
            float next = Mathf.MoveTowards(openAmount, target, step);

            // Only on the way open, and only from a standing start. The door shutting is the pad
            // being released, which the pad already announces with its own clunk from wherever the
            // player happens to be standing.
            if (openAmount <= 0f && next > 0f && audioSource != null && openClip != null)
                audioSource.PlayOneShot(openClip);

            openAmount = next;
            Apply();
        }

        // Latches the door open. KeyLock's route, and the only one - the pad drives Update above.
        public void Open() => latched = true;

        // Snaps the door back shut. The loop calls this at the top of every iteration, while the
        // eyelids are still closed, so the reset is never seen. Without it the door is world state
        // the loop forgets to rewind and every iteration after the first solve starts open.
        //
        // Deliberately silent: this is the loop rewinding world state behind a black screen, not
        // the door being shut. A sound here would draw attention to the seam.
        public void Close()
        {
            latched = false;
            openAmount = 0f;
            Apply();
        }

        // Slides shut IN VIEW, at the end of the run, with the player watching it - which is why
        // this exists next to Close() rather than being a call to it. Close() snaps and is
        // deliberately silent because it is the loop rewinding world state behind a black screen;
        // this is the way back closing, and it has to move and be heard.
        //
        // Unscaled, like everything else in the ending: nothing should be able to freeze it, and
        // PauseMenu is locked out for the duration anyway.
        public void Seal()
        {
            latched = false;
            sealing = true;
            // The slab's own motor, the same sound it opened with. The class note's reason for a
            // silent close does not hold here: it says the shutting is announced by the pad's
            // clunk, and nothing has touched a pad - this door is being shut AT the player.
            if (audioSource != null && openClip != null) audioSource.PlayOneShot(openClip);
            StartCoroutine(SealRoutine());
        }

        private System.Collections.IEnumerator SealRoutine()
        {
            while (openAmount > 0f)
            {
                float step = openDuration > 0f ? Time.unscaledDeltaTime / openDuration : 1f;
                openAmount = Mathf.MoveTowards(openAmount, 0f, step);
                Apply();
                yield return null;
            }
        }

        // TWO PHASES IN SEQUENCE, not two motions blended. A single diagonal move would start
        // travelling sideways while still flush and clip through the neighbouring panel for the
        // first few centimetres - the gap between panels is one groove, 0.05, and a linear diagonal
        // has only receded 0.002 by the time it has used that up.
        private void Apply()
        {
            if (doorPanel == null) return;

            if (pushInOffset == Vector3.zero)
            {
                doorPanel.localPosition = closedLocalPos + openLocalOffset * openAmount;
                return;
            }

            float split = Mathf.Clamp(pushFraction, 0.01f, 0.99f);
            float push = Mathf.Clamp01(openAmount / split);
            float slide = Mathf.Clamp01((openAmount - split) / (1f - split));
            doorPanel.localPosition = closedLocalPos + pushInOffset * push + openLocalOffset * slide;
        }

        private bool PlayerInDoorway()
        {
            Collider playerCollider = PlayerLookup.Collider;
            if (playerCollider == null) return false;

            // Horizontal only - the player's pivot is on the floor and the slab's centre is at
            // door height, so including Y would put them permanently 1.25m away.
            Vector3 offset = playerCollider.transform.position - doorwayCentre;
            offset.y = 0f;
            return offset.sqrMagnitude < doorwayClearance * doorwayClearance;
        }
    }
}
