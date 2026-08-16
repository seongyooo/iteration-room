using UnityEngine;

namespace IterationRoom
{
    // THE SPOT UNDER A TAP WHERE A BUCKET CATCHES THE WATER.
    //
    // A named place rather than "anywhere below the spout", and that is a design choice rather than a
    // shortcut. A bucket that filled wherever it happened to be under falling water would make the
    // player fight the placement - nudging an object around a floor to find the invisible column that
    // counts. A stand is a target: it is drawn, it is aimed at, and standing a bucket on it either
    // works or does not.
    //
    // It also settles which tap is filling, which the bucket must not have to work out for itself. A
    // bucket searching for a tap above it would find one through a wall.
    //
    // AND IT IS AN `IItemSocket`, which is what makes the room work at all. A past self's recorded
    // "I put the bucket down here" is a Surrender, and a Surrender replays by asking `ItemRegistry`
    // for the socket it named. Without one registered, every ghost that ever carried a bucket simply
    // kept carrying it to the end of its timeline and dropped it wherever it stopped - so no past
    // self ever filled anything, and the one room in this game that CANNOT be done alone had no way
    // to be done at all.
    //
    // Unlike every socket before it this one is not final: a bucket is put here in order to be taken
    // away again a few seconds later. `Release` is the way back out, and `Update` notices every route
    // that does not go through it.
    public class BucketStand : MonoBehaviour, IItemSocket
    {
        // The tap whose water lands here. Wired at build time, because the geometry knows and nothing
        // at runtime should have to re-derive it.
        public WaterTap tap;

        // Where a bucket sits when it is on this stand.
        public Transform seat;

        // WHERE THE PLAYER IS AIMING WHEN THEY AIM AT THIS, which is not the same point as where the
        // bucket sits. Both this and the tank were targeted by their own transforms - and both of
        // those are on the FLOOR, so standing at arm's length and looking at the fixture put the only
        // point that counted below the bottom of the screen: the click did nothing and no prompt
        // explained why. Raised to about where a bucket standing here would be.
        public Transform aimAnchor;
        public Transform Aim => aimAnchor != null ? aimAnchor : (seat != null ? seat : transform);

        // What this stand will take. Matched against `CarryableItem.itemId`, so it is the id and not
        // the component that decides - the same test every other socket in the game applies.
        public string bucketItemId = "Bucket";

        // WHAT AN OVERFLOWING PAIL PUTS ON THE FLOOR. Fed while a FULL bucket is sitting here under a
        // RUNNING tap, which is exactly the condition `Bucket.overflow` draws water running down the
        // staves for - the two are the same event seen at the rim and at the floor, and showing only
        // the first one made the water stop existing halfway down.
        //
        // On the stand rather than on the bucket, because a puddle belongs to the FLOOR: pick the
        // bucket up mid-overflow and the water stays where it was poured, which is what would happen.
        public SpreadingPuddle overflowPuddle;

        public Bucket Occupant { get; private set; }
        public bool IsFree => Occupant == null;

        public string AcceptedItemId => bucketItemId;

        // Registered by name, because a bucket has two stands to choose between and a ghost has to
        // reproduce the one it actually used. `name` is fixed at build time ("Stand_Wall",
        // "Stand_South") and is what `PlayerHand.Surrender` writes into the recording.
        //
        // In OnEnable rather than Awake, like KeyLock: a sleeping cycle's stands must not answer for
        // an id in the cycle the player is actually in.
        private void OnEnable() => ItemRegistry.RegisterSocket(this, name);
        private void OnDisable() => ItemRegistry.UnregisterSocket(this);

        // A PAST SELF STANDING ITS BUCKET HERE. Refusing when this stand is taken is a real outcome
        // rather than an error - another past self, or the living player, got here first - and the
        // ghost then carries the bucket on exactly as it does with a key whose lock is already open.
        public bool AcceptFromGhost(CarryableItem item)
        {
            if (item == null || item.itemId != bucketItemId || !IsFree) return false;

            Bucket bucket = item.GetComponent<Bucket>();
            if (bucket == null) return false;

            Place(bucket);
            return true;
        }

        // Seats a bucket, and tells it what is feeding it. The bucket is parented to the seat so it
        // moves with the room rather than being left at a world position the loop would have to fix.
        public void Place(Bucket bucket)
        {
            if (bucket == null || !IsFree) return;

            Occupant = bucket;
            Transform t = bucket.transform;
            t.SetParent(seat != null ? seat : transform, false);
            t.localPosition = Vector3.zero;
            t.localRotation = Quaternion.identity;
            // GIVEN BACK ITS TRIGGER AND ITS BODY. `PlayerHand.Surrender` does not do this - every
            // socket before the bucket kept what it was given - so without it the bucket sits here
            // with the collider it had in the hand, which is switched off, and can never be picked up
            // again. See CarryableItem.StandOnPerch.
            CarryableItem item = bucket.GetComponent<CarryableItem>();
            if (item != null) item.StandOnPerch();

            bucket.SetFeed(tap);
            TellTap(true, bucket);
        }

        // The bucket leaving, by any route - picked up, or swept back to its origin by the loop. The
        // feed is cleared HERE rather than by whoever took it, so there is one place that can forget.
        public void Release()
        {
            if (Occupant != null) Occupant.SetFeed(null);
            Occupant = null;
            TellTap(false, null);
        }

        // THE TAP HAS TO KNOW SOMETHING IS UNDER IT, or it goes on pouring onto the floor while the
        // bucket fills - which looks exactly like the bucket doing nothing. The rim height comes from
        // the bucket's own bounds rather than a number kept here, so a different bucket catches the
        // water at its own height.
        private void TellTap(bool caught, Bucket bucket)
        {
            if (tap == null || tap.waterFlow == null) return;

            float rimY = transform.position.y;
            if (caught && bucket != null)
            {
                Renderer r = bucket.GetComponentInChildren<Renderer>();
                if (r != null) rimY = r.bounds.max.y;
            }
            tap.waterFlow.SetCatch(caught, rimY);
        }

        // The loop rewinding: whatever was standing here has been sent back to its origin by
        // `ItemRegistry.ReturnAllToOrigin`, so the stand has to stop believing it is occupied. Run
        // AFTER the sweep, with the other room resets - see Cycle.ResetRooms.
        public void ResetStand() => Release();

        private void Update()
        {
            // A bucket can leave without the stand being told: the player takes it, a ghost takes it,
            // or the loop returns it to its origin. Rather than every one of those having to call
            // Release, the stand notices it is empty - the same reasoning KeyLock uses for a key that
            // has left its socket.
            if (Occupant != null && Occupant.transform.parent != (seat != null ? seat : transform))
                Release();

            // The overflow. `SetFed` is a level rather than an event, so this can simply state the
            // condition every frame and let the puddle grow, hold, and dry on its own schedule -
            // nothing here has to remember whether it started.
            if (overflowPuddle != null)
                overflowPuddle.SetFed(Occupant != null && Occupant.IsFull
                                      && tap != null && tap.IsOn);
        }
    }
}
