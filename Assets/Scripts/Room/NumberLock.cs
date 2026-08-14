using UnityEngine;

namespace IterationRoom
{
    // ROOM2-1'S RULE: five pads, five digits scrawled on the wall behind them, and a door that opens
    // only while every pad reads the digit in front of it.
    //
    // The room owns the rule and nothing else. Where the pads are, what they look like and how a
    // press is counted are `CountPad`'s; whether the door moves is `Door`'s. This is only the
    // conjunction, held in one place so the door and any readout can never disagree about it - the
    // same reason `FloorButton.AllActive` exists rather than each caller re-deriving it.
    //
    // WHY IT ACCUMULATES. A pad keeps its count for the whole iteration, so a past self can walk in,
    // step on one pad three times and leave, and that pad still reads 3 when the living player
    // arrives. Five pads is therefore about five iterations of work divided across past selves -
    // which is the shape every room in this game is meant to have.
    //
    // AND WHY IT CANNOT DEAD-END. See `CountPad`: the counter wraps at ten, so a pad a ghost has
    // overshot is never stuck, only wrong. Without that, one miscounted iteration would poison the
    // room permanently - the ghost would replay the wrong count forever and nothing could subtract
    // from it.
    public class NumberLock : MonoBehaviour
    {
        public CountPad[] pads;

        // Every pad reads what the wall in front of it says.
        //
        // An empty or null array is NOT solved, for the reason `FloorButton.AllActive` refuses one: a
        // door wired to no pads should stay shut rather than stand permanently open, which is what
        // "all zero of them are correct" would otherwise vacuously mean.
        public bool Solved
        {
            get
            {
                if (pads == null || pads.Length == 0) return false;
                foreach (CountPad pad in pads)
                    if (pad == null || !pad.Met) return false;
                return true;
            }
        }

        // The loop rewinding. Called from `Cycle.ResetRooms` with the other room resets, after the
        // item sweep - the counts are world state exactly like a door's position or a drawer's.
        public void ResetLock()
        {
            if (pads == null) return;
            foreach (CountPad pad in pads) pad?.ResetCount();
        }
    }
}
