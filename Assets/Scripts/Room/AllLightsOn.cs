using UnityEngine;

namespace IterationRoom
{
    // Room2_1's rule: every scattered switch must be flipped on at once. Refuses an empty array for
    // the same reason FloorButton.AllActive does - a mis-wired door should stay shut and be noticed,
    // not stand open by default.
    //
    // TRACKED, not latched here - see RoomCondition. It reads as latched anyway, because a switch
    // stays on for the rest of the iteration once flipped (LightSwitch.IsOn never falls on its own),
    // the same way NumberLock's pads hold their counts rather than the condition remembering them.
    public class AllLightsOn : RoomCondition
    {
        public LightSwitch[] switches;

        public override bool Satisfied
        {
            get
            {
                if (switches == null || switches.Length == 0) return false;
                foreach (LightSwitch s in switches)
                {
                    if (s == null || !s.IsOn) return false;
                }
                return true;
            }
        }

        // Every switch back off and its light with it - run at the top of an iteration, after the
        // item sweep, like every other RoomCondition. See Cycle.ResetRooms for the ordering.
        public override void ResetCondition()
        {
            if (switches == null) return;
            foreach (LightSwitch s in switches) s?.TurnOff();
        }
    }
}
