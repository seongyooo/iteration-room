using UnityEngine;

namespace IterationRoom
{
    // LEFT CLICK JOINS THE BLOCK IN YOUR HAND TO THE ONES ALREADY STACKED.
    //
    // On the player, beside `ChessPlacer`, `BucketPlacer` and `BalloonTool`, and it cannot clash
    // with any of them: all four are silent unless the hand holds the one thing they act on, and
    // `PlayerHand` holds exactly one thing at a time (CLAUDE.md §4).
    //
    // WHY LEFT CLICK AND NOT E, which this game's other "put it where it goes" fixtures use. E
    // already means take and put down, and it is how the block got into the hand in the first
    // place. More to the point, the target here is not a fixture you stand at - it is the growing
    // cube across the room, aimed at - and left click is the button this game already uses for
    // acting on what you are pointing at.
    //
    // NEAREST IN VIEW rather than a raycast, matching `BucketPlacer` and every E fixture: there is
    // one cube, so nothing is being arbitrated, and `PlayerLookup.InView` is what makes the press
    // and the prompt answer the same question. A raycast would additionally require the growing
    // cube to be solid, which it is not - a half-built cube is a dozen loose blocks sharing an
    // anchor, and a ray that missed between two of them would refuse a click the disc had offered.
    public class BedlamPlacer : MonoBehaviour
    {
        public PlayerHand hand;

        // THIS CYCLE'S CUBE, handed over by `CycleBinding` rather than serialised. This component
        // is on the PLAYER, in the core scene, and a reference from there into a cycle scene is
        // dropped on save - the same crossing `ChessPlacer.board` and `BucketPlacer.stands` make.
        public BedlamCube cube;

        // **HOW FAR YOU CAN BE AND STILL ADD A BLOCK.** Longer than the bucket's 3.2 and shorter
        // than the chess board's 6: the cube is a waist-high object on a plinth you walk up to, not
        // a 5.4m board whose far squares are out of arm's reach from any edge.
        public float reach = 3.5f;

        // Read by ControlHintDisplay, which shares the left-button disc with the pin, the chess
        // board, the buckets and the tree - at most one of them can want it, because the hand holds
        // one object.
        public bool WantsPlaceHint => Target != null;
        public Transform PlaceAnchor => cube != null ? cube.aim : null;

        // The cube, but only while a click at it would actually do something. Recomputed every
        // frame rather than cached: the block can leave the hand by routes this component never
        // sees - a ghost taking it, the loop rewinding - and a prompt for a press that cannot
        // succeed is worse than no prompt, because it teaches that the control is unreliable.
        private BedlamCube Target
        {
            get
            {
                if (!Live) return null;
                return cube.CanSeat(hand.Held.itemId) && InReach ? cube : null;
            }
        }

        // Everything the press needs EXCEPT whether this particular block can join yet. Split out
        // because a refusal has to be told apart from a press that was never aimed at the cube: the
        // first flashes red and is an answer, the second is not this fixture's press at all.
        private bool Live
        {
            get
            {
                if (cube == null || hand == null || cube.aim == null) return false;
                if (LoopManager.Instance != null && !LoopManager.Instance.AcceptsInput) return false;

                CarryableItem held = hand.Held;
                return held != null && cube.IndexOf(held.itemId) >= 0;
            }
        }

        private bool InReach
        {
            get
            {
                Camera eye = PlayerLookup.Eye;
                Vector3 from = eye != null ? eye.transform.position : transform.position;
                // The AIM point, not the cube's transform, and `InView` is given the cube as its
                // owner: the blocks already seated are children of it, so without that the cube
                // occludes its own middle and the prompt would only appear from angles where the
                // centre happened to be uncovered.
                return (cube.aim.position - from).sqrMagnitude <= reach * reach
                    && PlayerLookup.InView(cube.aim, cube.transform);
            }
        }

        private void Update()
        {
            if (!Live || !GameInput.UsePressed) return;

            CarryableItem held = hand.Held;

            // Aimed away: not this fixture's press. Left for whatever else the button might mean,
            // and deliberately NOT flashed - a refusal you did not ask for is noise.
            if (!InReach) return;

            // Aimed at the cube with a block that has nowhere to go yet. That IS a refusal: the
            // player asked this cube a question and it answered no, and the flash is the answer.
            if (!cube.CanSeat(held.itemId))
            {
                RunTally.Answer(false);
                cube.ShowRefused();
                return;
            }

            // Surrendered rather than parented straight out of the hand, because the HAND has
            // bookkeeping of its own that `CarryableItem` knows nothing about - and because a
            // surrender is what writes the event into the recording. That is the whole of how a
            // past self repeats this: the id resolves to this block's own place through
            // ItemRegistry, and the ghost puts it there without knowing what a cube is.
            //
            // Asked AFTER CanSeat, never before: a recorded surrender that could not be completed
            // is a ghost doing something the player did not.
            CarryableItem block = hand.Surrender(held.itemId);
            if (block == null) return;

            // The block belonged and the player knew it did. Counted here rather than beside
            // `ShowRefused` above so that both branches of the same question are recorded by the
            // same method - see `RunTally.Answer`, which exists as one call for exactly this reason.
            RunTally.Answer(true);

            // Cannot fail - CanSeat was true a line ago and nothing between can have changed it -
            // but the block is out of the hand's books by now, so a hole here would lose it until
            // the loop swept it back. Put it down where the player stands instead.
            if (!cube.Seat(block)) block.DropAt(transform.position + transform.forward * 0.6f);
        }
    }
}
