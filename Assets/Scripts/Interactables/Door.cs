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
        public Transform doorPanel;
        public Vector3 openLocalOffset = new Vector3(0f, 2.2f, 0f);
        public float openDuration = 1.0f;
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
        private Transform player;

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

        private void Update()
        {
            float target = latched ? 1f : 0f;

            // Silent and still through the wake-up, like everything else the facility does: the
            // loop closes the doors and releases every ghost's signal behind the closed eyelids,
            // and a door moving under a black screen is the machinery showing through.
            bool running = LoopManager.Instance == null || LoopManager.Instance.IterationRunning;

            if (!latched && running)
            {
                if (FloorButton.AllActive(requiredFloorButtons)) target = 1f;
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

        private void Apply()
        {
            if (doorPanel != null)
                doorPanel.localPosition = closedLocalPos + openLocalOffset * openAmount;
        }

        private bool PlayerInDoorway()
        {
            if (player == null)
            {
                // Found lazily rather than wired by SceneBuilder: the player is built after the
                // doors are, and this is asked for only while a door is actually standing open.
                GameObject go = GameObject.FindGameObjectWithTag("Player");
                if (go == null) return false;
                player = go.transform;
            }

            // Horizontal only - the player's pivot is on the floor and the slab's centre is at
            // door height, so including Y would put them permanently 1.25m away.
            Vector3 offset = player.position - doorwayCentre;
            offset.y = 0f;
            return offset.sqrMagnitude < doorwayClearance * doorwayClearance;
        }
    }
}
