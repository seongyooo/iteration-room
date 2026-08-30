using UnityEngine;

namespace IterationRoom
{
    // LEFT CLICK STANDS THE LADDER IN ITS SHAFT.
    //
    // On the player, beside `ChessPlacer`, `BucketPlacer`, `BedlamPlacer` and `BalloonTool`, and it
    // cannot clash with any of them: all five are silent unless the hand holds the one thing they
    // act on, and `PlayerHand` holds exactly one thing at a time (CLAUDE.md §4).
    //
    // NEAREST IN VIEW rather than a raycast, matching every other placer here: there is one mount,
    // so nothing is being arbitrated, and `PlayerLookup.InView` is what keeps the press and the
    // prompt answering the same question.
    public class LadderPlacer : MonoBehaviour
    {
        public PlayerHand hand;

        // THIS CYCLE'S MOUNT, handed over by `CycleBinding` rather than serialised: this component is
        // on the PLAYER, in the core scene, and a reference from there into a cycle scene is dropped
        // on save. The same crossing `ChessPlacer.board` and `BedlamPlacer.cube` make.
        public LadderMount mount;

        // A ladder is a two-metre object put into a marked spot on a deck. Short, like the bucket's
        // stand: at this range the aim is unambiguous and there is exactly one place it can go.
        public float reach = 3.2f;

        // Read by ControlHintDisplay, which shares the left-button disc with the pin, the board, the
        // buckets, the tree and the cube - at most one of them can want it, because the hand holds
        // one object.
        public bool WantsPlaceHint => Target != null;
        public Transform PlaceAnchor => mount != null ? mount.HintAnchor : null;

        private LadderMount Target
        {
            get
            {
                if (!Live) return null;
                return mount.CanAccept(hand.Held.itemId) && InReach ? mount : null;
            }
        }

        // Everything the press needs EXCEPT whether the mount will take it. Split out so a refusal
        // can be told apart from a press that was never aimed here.
        private bool Live
        {
            get
            {
                if (mount == null || hand == null) return false;
                if (LoopManager.Instance != null && !LoopManager.Instance.AcceptsInput) return false;

                CarryableItem held = hand.Held;
                return held != null && held.itemId == mount.acceptedItemId;
            }
        }

        private bool InReach
        {
            get
            {
                // OFF THE HINT ANCHOR, not the mount's origin, and both halves for one reason:
                // the origin sits on the deck, where the range is measured through the player's own
                // height and the view ray grazes the floor past whatever is standing on the mark.
                // See `LadderMount.hintAnchor`.
                Camera eye = PlayerLookup.Eye;
                Vector3 from = eye != null ? eye.transform.position : transform.position;
                Transform at = mount.HintAnchor;
                return (at.position - from).sqrMagnitude <= reach * reach
                    && PlayerLookup.InView(at, mount.transform);
            }
        }

        private void Update()
        {
            if (!Live || !GameInput.UsePressed || !InReach) return;

            CarryableItem held = hand.Held;
            if (!mount.CanAccept(held.itemId)) return;

            // Surrendered rather than parented straight out of the hand: the hand has bookkeeping of
            // its own, and a surrender is what writes the event into the recording. That is the whole
            // of how a past self stands the ladder up in every later iteration.
            //
            // Asked AFTER CanAccept, never before - a recorded surrender that could not be completed
            // is a ghost doing something the player did not.
            CarryableItem ladder = hand.Surrender(held.itemId);
            if (ladder == null) return;

            // Cannot fail - CanAccept was true a line ago - but the ladder is out of the hand's books
            // by now, so a hole here would lose it until the loop swept it back.
            if (!mount.Accept(ladder)) ladder.DropAt(transform.position + transform.forward * 0.6f);
        }
    }
}
