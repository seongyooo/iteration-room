using UnityEngine;

namespace IterationRoom
{
    // ROOM2'S RULE: three levers down at the same moment.
    //
    // Each one comes back up on its own a few seconds after it is pulled, so a single person can
    // never have more than one of them down for long. Two past selves, pulling at the right times,
    // is the whole of the answer - and it is the first thing in this game that a ghost cannot help
    // with simply by standing somewhere.
    //
    // IT LATCHES, and that is the one soft edge in the design. The alignment lasts a couple of
    // seconds at best, and a door that only stood open for those seconds would make the puzzle
    // "align three levers AND be at the far door already", which is a sprint bolted onto a timing
    // problem rather than a harder version of it. So the moment the three agree, the room is answered
    // for this iteration; the loop takes it back at the boundary like everything else.
    public class Chorus : RoomCondition
    {
        public ChorusLever[] levers;

        // What the room does when it agrees. Optional: driven by the core's first clamp once that
        // exists, and nothing at all while this room is only a door.
        public RewardPlinth payout;

        private bool latched;

        public override bool Satisfied => latched;

        private void Update()
        {
            if (latched || levers == null || levers.Length == 0) return;

            foreach (ChorusLever lever in levers)
                if (lever == null || !lever.IsDown) return;

            latched = true;
            if (payout != null) payout.Requested = true;
        }

        public override void ResetCondition()
        {
            latched = false;
            if (payout != null) payout.Requested = false;
            if (levers == null) return;
            foreach (ChorusLever lever in levers) lever?.ResetLever();
        }
    }
}
