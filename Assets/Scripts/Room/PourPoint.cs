using UnityEngine;

namespace IterationRoom
{
    // WHERE A BUCKET IS EMPTIED INTO THE TANK, and the only reason room2-2 is a puzzle rather than a
    // chore.
    //
    // WHY THIS IS A SIGNAL AND NOT A SURRENDER. Every other hand-over in this game gives the object
    // away - a key goes into a lock and stays there - so `CarryKind.Surrender` and an `IItemSocket`
    // carry it. Pouring gives nothing away: the bucket is still in the hand afterwards, emptier. So
    // there is no custody change to record, and without something else recording it a past self
    // walked to the tank holding a full bucket and simply stood there. The tank could only ever be
    // filled by the living player, inside one minute, which is precisely the thing `WaterTank` says
    // cannot be done.
    //
    // A pour is an INSTANT performed WITH an object, which is the same shape as a balloon pop - so it
    // is recorded the way presses are (a stretched pulse, see Drawer.openPulseDuration, because a
    // ghost advances by elapsed time and would skip a one-frame one) and re-evaluated at the other
    // end against the condition that actually enabled it: a bucket with water in it, in that ghost's
    // hand, right now. Take the bucket off a past self and its pour stops happening, exactly as its
    // pops stop when the pin is taken.
    public class PourPoint : GhostInteractable
    {
        public WaterTank tank;
        public string bucketItemId = "Bucket";

        // Stretched over a handful of frames, like every other press in this game.
        public float pulseDuration = 0.2f;

        private float pulseUntil = -1f;

        public override bool PlayerSignal => Time.time < pulseUntil;

        // The living player's pour, and only theirs - raised by `BucketPlacer` when the click lands.
        // A ghost's replayed pour must never come back through here or every iteration would inherit
        // the last one's, which is the fault `Drawer.SetGhostSignal` documents.
        public void RegisterPlayerPour() => pulseUntil = Time.time + pulseDuration;

        // The loop rewinding. A pulse is measured against `Time.time`, which does not reset with the
        // iteration, so one still standing at the boundary would fire into the new iteration's first
        // frames.
        public void ResetPulse() => pulseUntil = -1f;

        public override void SetGhostSignal(GhostReplayer ghost, bool active)
        {
            if (!active || ghost == null || tank == null) return;

            // THE OBJECT, not a remembered fact about it. `EquippedItem` verifies against the item
            // itself, so a bucket the living player has taken out of this ghost's hands cannot pour.
            CarryableItem item = ghost.EquippedItem(bucketItemId);
            Bucket bucket = item != null ? item.GetComponent<Bucket>() : null;
            // An empty bucket refuses inside BeginPour, which is where that judgement belongs - the
            // ghost simply goes through the motion of a pour that has nothing to give, the same way
            // its recorded take of an unavailable object does nothing.
            if (bucket != null) bucket.BeginPour(tank);
        }
    }
}
