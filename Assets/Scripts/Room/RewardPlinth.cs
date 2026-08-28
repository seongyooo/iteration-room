using UnityEngine;

namespace IterationRoom
{
    // A plinth that rises out of the floor carrying one of the escape objects. Room3's yellow
    // triangle and the cube room's blue sphere are both this; Room2West's red cube sits on the same
    // fixture without the rise, because its room opens the board instead.
    //
    // TWO WAYS TO ASK FOR IT, and each room states its own condition exactly once:
    //   - `pads`, which is `FloorButton.AllActive` - the SAME call `Door` and `DoorIndicator` make,
    //     so Room3 cannot end up with a door that says "go" while its reward says "not yet".
    //   - `Requested`, set by a room component that owns a rule of its own (`CubeRoom.IsSolved`).
    // Neither set means down, which is what a plinth wired to nothing should be.
    //
    // It RISES AND FALLS with its condition rather than latching once. For Room3 that is not softness
    // but consistency: a hold-door that shuts when a past self steps off, standing beside a reward
    // that stays out once earned, would be two different promises about the same two pads. What
    // actually protects the player is that taking the object is a PICKUP - once it is in their hands
    // the plinth can sink under them and they keep it.
    //
    // DERIVED STATE, so the loop needs no reset hook for the plinth itself. Where it is, and whether
    // the object is in play, are read from the condition every frame; at the top of an iteration every
    // pad is released and every room is unsolved, so both answers are already right before anything
    // asks. (A room with a `Requested` flag of its own still has to clear that flag - see
    // `CubeRoom.ResetRoom`.)
    //
    // AND IT KEEPS MOVING WHILE THE LOOP IS STOPPED, which is where it deliberately parts company with
    // the `Door` it shares Room3's condition with. That class freezes outright when the iteration is
    // not running, because a door sliding under the closed eyelids is the machinery showing through.
    // The same freeze here would be worse, not better: the pads release at the top of an iteration, so
    // a frozen plinth stays UP through the whole wake-up and then sinks in full view on the first
    // frame the player has control. Left running it settles under the black screen, which is the
    // outcome the door's rule is trying to buy in the first place.
    public class RewardPlinth : MonoBehaviour
    {
        public FloorButton[] pads;

        // Set by whatever room owns a rule of its own. A field rather than a property so SceneBuilder
        // and the inspector can both see it, and so a room can simply assign it every frame.
        public bool Requested;

        // Authored in its RAISED position and sunk in Awake, like Room4's - a scene whose one prop is
        // invisible cannot be checked without pressing Play.
        public Transform plinth;
        public float riseHeight = 1.25f;
        public float riseSeconds = 1.6f;

        // What it carries, and where. Optional: a plinth with nothing on it is a plinth.
        public CarryableItem key;
        public Transform keySeat;


        // **THE PLINTH OWNS ITS OBJECT ONLY WHILE THE OBJECT IS STILL ON IT** (2026-08-29, after play
        // reported the yellow triangle teleporting back here).
        //
        // It used to own it forever, and both halves of that were wrong:
        //
        //   - `HideKey` hid the object whenever the plinth was down. Put the triangle down anywhere
        //     in the building and let a past self step off a pad, and it VANISHED - a carryable
        //     nobody was holding, deleted from the world by a fixture two rooms away.
        //   - `ShowKey` revealed it at the seat whenever the plinth came back up. `RevealAt` refuses
        //     while the item is CARRIED, which is why holding it felt safe - but the moment it was
        //     set down, a past self stepping back onto the pads yanked it across the building and
        //     put it on the plinth again. That is the teleport play found, and for an escape object
        //     carried toward a console it can undo a whole iteration's work.
        //
        // `LoopManager` already knew about this and worked around it: its test-jump nulls
        // `plinth.key` before revealing an object elsewhere, with a comment explaining that a plinth
        // which is not raised calls `Hide()` on what it carries every frame. That was the bug being
        // routed around rather than fixed.
        //
        // **PARENTAGE, NOT POSITION.** The reward is authored as a CHILD of the plinth, so it is
        // parented here exactly while it is home: `AttachTo` moves it to a hand, a drop moves it to
        // `dropParent`, and `ReturnToOrigin` brings it back here at the top of an iteration. That
        // keeps this class's whole design intact - state derived every frame, no reset hook to
        // forget - and it is an identity test rather than a distance one (CLAUDE.md 1.4).
        private bool OwnsKey => key != null && plinth != null && key.transform.IsChildOf(plinth);

        private Vector3 upPosition, downPosition;
        private float blend;
        private bool offered;

        public bool Raised => blend >= 1f;

        private bool Wanted => Requested || FloorButton.AllActive(pads);

        private void Awake()
        {
            if (plinth == null) return;
            upPosition = plinth.localPosition;
            downPosition = upPosition + Vector3.down * riseHeight;
            plinth.localPosition = downPosition;
            HideKey();
        }

        private void Update()
        {
            if (plinth == null) return;

            blend = Mathf.MoveTowards(blend, Wanted ? 1f : 0f, Time.deltaTime / Mathf.Max(0.01f, riseSeconds));
            // Eased at both ends when it MOVES, so it neither starts nor stops with a jolt - this is
            // machinery being raised, not a prop being switched on. The raw blend is what the tests
            // below read, because "all the way up" has to mean exactly that.
            plinth.localPosition = Vector3.Lerp(downPosition, upPosition, Mathf.SmoothStep(0f, 1f, blend));

            // OUT OF PLAY UNTIL IT IS ALL THE WAY UP, and hidden rather than merely out of reach.
            // `CarryableItem.IsFreeForGhost` reads `visible`, so hiding is what stops a past self
            // reaching through the floor for it at a timestamp when the plinth happened to be down -
            // the same mechanism that keeps a key inside an unburst balloon. Out of reach alone would
            // hold the living player and not the ghosts.
            if (blend >= 1f) ShowKey();
            else HideKey();
        }

        private void ShowKey()
        {
            if (offered || !OwnsKey || keySeat == null) return;
            offered = true;
            // RevealAt refuses while the item is carried as well, so between the two there is no
            // path by which this pulls an object out of anybody's hands or in from anywhere else.
            key.RevealAt(keySeat.position);
        }

        private void HideKey()
        {
            if (!OwnsKey) return;
            offered = false;
            // NOT while someone is holding it. Hide() knows nothing about custody, and the plinth
            // sinking under a player who has just picked the object up would make what they are
            // carrying invisible for the rest of the run. The ownership test above already covers
            // this; kept because it is the cheaper question and states the rule at the point of use.
            if (!key.IsCarried) key.Hide();
        }
    }
}
