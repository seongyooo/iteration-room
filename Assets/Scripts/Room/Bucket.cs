using System.Collections;
using UnityEngine;

namespace IterationRoom
{
    // A BUCKET, which is the errand this room is built around: it fills where the water is and empties
    // where the water is wanted, and the two are not the same place.
    //
    // WHY A BUCKET RATHER THAN A PIPE. A pipe from the tap to the tank would fill it with no one
    // present, which is a room that solves itself. The bucket is what makes the water into WORK - it
    // has to be carried, and carrying takes the one thing an iteration is short of. It is also why
    // this room needs past selves rather than merely benefiting from them: one bucket is one trip, the
    // tank takes several, and the minute holds fewer trips than the tank takes.
    //
    // Filling is a LEVEL and not an event: it happens continuously while the bucket is sitting under
    // running water, and stops when it is picked up or the tap is shut. Nothing about it is recorded -
    // it is derived from where the bucket is and whether the tap above it is on, exactly as
    // `FallingItem` derives a fall from a release rather than storing one.
    public class Bucket : MonoBehaviour
    {
        // The water inside, 0 to 1. Scaled into `waterBody` so the surface the player sees IS this.
        public float Level { get; private set; }
        public bool IsFull => Level >= 0.999f;

        public Transform waterBody;
        // Where the water sits when the bucket is full, in the bucket's own space - the inside floor
        // and the inside height, measured off the model rather than guessed.
        public float innerBottom = 0.02f;
        public float innerHeight = 0.22f;
        public float innerRadius = 0.14f;

        // How long a full bucket takes under one tap. Long enough to be an errand, short enough that
        // an iteration can start one and a later one can finish it.
        public float fillSeconds = 14f;

        // Runs while the bucket is full and still under the tap - the water has nowhere to go, so it
        // goes over the side. Switched, not spawned, so it costs nothing when idle.
        public GameObject overflow;

        public AudioSource audioSource;
        public AudioClip pourClip;

        // EMPTYING IT TAKES TIME AND IS SOMETHING YOU CAN WATCH.
        //
        // It used to be one call: click, and the level jumped from full to nothing while the tank
        // gained a fifth in the same frame. Nothing about that said WATER - it said a number moved.
        // What follows is the same transfer spread over a second and a half, with the bucket tipped
        // over its own rim and a stream falling out of it, and the tank rising while it does.
        //
        // The pivot is at the LIP rather than at the bucket's centre, so tipping swings the body up
        // and back over the rim the way an arm actually pours. `PourPivot` sits at the rim and its
        // child holds everything the bucket is made of - so the water inside tips with the pail, and
        // `Apply` below goes on working in a frame that has not moved.
        public Transform pourPivot;
        public float pourAngle = 104f;
        // Long enough to read as a deliberate action, short enough not to be a wait. The tilt runs
        // inside the pour rather than before it, so the water starts leaving as the bucket goes over.
        public float pourSeconds = 1.5f;
        public float pourTiltSeconds = 0.4f;

        // The falling water, hidden until there is some. Positioned in WORLD space each frame while
        // pouring: it has to hang straight down from a lip that is riding a hand, which no fixed
        // local pose can do.
        public Transform pourStream;
        public float streamWidth = 0.07f;
        // How far the stream may reach down toward whatever it is being poured into. Clamped rather
        // than unbounded, so pouring from a height does not draw a column across the room.
        public float maxStreamLength = 1.3f;

        // Nothing else may act on this while it is going over: a second click would start a second
        // pour, and E would throw a tipped bucket on the floor mid-stream.
        public bool IsPouring { get; private set; }

        // Set by BucketStand when this is seated under a tap, and cleared when it is lifted. The
        // bucket does not look for a tap itself: the stand knows which tap is above it, and a bucket
        // that searched would find one through a wall.
        private WaterTap feedingTap;
        private float tilt;

        public void SetFeed(WaterTap tap) => feedingTap = tap;

        private void Update()
        {
            // A bucket being emptied is not being filled, whatever it is standing under.
            bool filling = !IsPouring && feedingTap != null && feedingTap.IsOn;

            if (filling && !IsFull)
                Level = Mathf.Clamp01(Level + Time.deltaTime / Mathf.Max(0.01f, fillSeconds));

            // OVERFLOWING IS BEING FULL AND STILL FED, which is a different fact from being full - a
            // full bucket sitting in a corner is not spilling. Saying it this way also means it stops
            // the moment the tap is shut, with nothing to remember.
            if (overflow != null) overflow.SetActive(filling && IsFull);

            Apply();
        }

        // EVERYTHING THIS BUCKET HOLDS, TIPPED INTO THE TANK - over a second and a half rather than
        // in one frame, and the tank rises as it goes.
        //
        // Driven the same way for the player and for a past self: `BucketPlacer` starts it on a click,
        // `PourPoint` starts it off a replayed signal, and neither knows anything about the tilt. What
        // is re-evaluated at the ghost's end is the condition that enabled the pour in the first place
        // - a bucket with water in it, in that hand, right now (CLAUDE.md §1.3).
        //
        // Returns false when there is nothing to pour, so a caller can tell a refusal from a start.
        public bool BeginPour(WaterTank tank)
        {
            if (IsPouring || tank == null || Level <= 0.01f) return false;

            StopAllCoroutines();
            StartCoroutine(PourOut(tank));
            return true;
        }

        private IEnumerator PourOut(WaterTank tank)
        {
            IsPouring = true;
            if (overflow != null) overflow.SetActive(false);

            if (audioSource != null && pourClip != null)
            {
                audioSource.pitch = Random.Range(0.94f, 1.07f);
                audioSource.PlayOneShot(pourClip);
            }

            CarryableItem item = GetComponent<CarryableItem>();

            // HELD STILL FOR THE LENGTH OF IT, and only when it is the LIVING PLAYER pouring. A ghost
            // runs this same routine, and a past self tipping a bucket two rooms away must not take
            // the player's legs.
            HoldPlayerStill(item);

            // What this pour owes the tank. Taken once, so a bucket that is somehow topped up
            // mid-pour still delivers exactly what it had - no rounding can invent water.
            float owed = Level;
            float given = 0f;
            float elapsed = 0f;

            while (given < owed - 0.0001f)
            {
                // INTERRUPTED. Put down, taken off this ghost, or swept home by the loop - in every
                // case something else now owns where this object is, and a pour that carried on would
                // be pouring out of a bucket standing on the floor.
                if (item != null && !item.IsCarried) break;

                elapsed += Time.deltaTime;
                float t = elapsed / Mathf.Max(0.01f, pourTiltSeconds);
                SetTilt(t);
                PosePourHold(item, Mathf.Clamp01(t));

                float step = Mathf.Min(owed - given, owed * Time.deltaTime / Mathf.Max(0.05f, pourSeconds));
                given += step;
                Level = Mathf.Max(0f, Level - step);
                tank.Pour(step);

                ShowStream(tank);
                Apply();
                yield return null;
            }

            HideStream();

            // BACK UPRIGHT, and it is worth the extra frames: a bucket that snapped level the instant
            // it emptied would read as the tilt having been a slide rather than a movement.
            float from = tilt;
            for (float e = 0f; e < pourTiltSeconds; e += Time.deltaTime)
            {
                float k = e / Mathf.Max(0.01f, pourTiltSeconds);
                SetTilt(Mathf.Lerp(from, 0f, k));
                PosePourHold(item, 1f - k);
                yield return null;
            }

            SetTilt(0f);
            PosePourHold(item, 0f);
            IsPouring = false;
            ReleasePlayer();
        }

        // The player cannot walk while their own bucket is going over. Remembered as the CONTROLLER
        // that was locked rather than as a bool, so the release cannot possibly clear somebody else's
        // lock - and so a bucket that never locked anything (a ghost's) has nothing to release.
        private FirstPersonController heldPlayer;

        private void HoldPlayerStill(CarryableItem item)
        {
            if (item == null || !item.IsCarriedByPlayer) return;

            heldPlayer = PlayerLookup.Controller;
            if (heldPlayer == null) return;
            heldPlayer.MovementLocked = true;
            // AND THE VIEW. Pouring is a two-handed action the player is watching; being able to look
            // away mid-pour made it read as something happening to somebody else.
            heldPlayer.LookLocked = true;
        }

        // EVERY WAY A POUR CAN END has to come through here, which is why it is called from the
        // routine's own tail, from the loop's rewind and from OnDisable rather than trusted to the
        // coroutine finishing. A lock left set is a player who can never walk again, and the two
        // paths that kill this coroutine outright do not run its last line.
        // WHERE THE POUR HAPPENS, and it is not where the bucket is carried.
        //
        // `HandPoseFor` holds a big object LOW and out to the side - correct for walking around with
        // a pail, and wrong for tipping one: a 104-degree tilt about the rim from down there swings
        // the body below the camera, and play reported the whole action happening "at the ground".
        // So the bucket is brought up and in front for the length of the pour and returned after.
        //
        // WRITTEN ABSOLUTELY from `handLocalPosition` every frame, never accumulated - the held pose
        // is set once at pickup by `CarryableItem.AttachTo` and nothing re-poses it, so an offset that
        // was added would compound. Same rule as PlayerHand.ApplySwing, same reason.
        public Vector3 pourHoldOffset = new Vector3(-0.14f, 0.46f, 0.10f);

        // A GHOST'S POUR IS LIFTED TOO, and it has to be done separately because the two are held in
        // different frames. The player's bucket rests at `handLocalPosition` in the hand anchor; a
        // ghost's rests at the ORIGIN of its carry anchor (`GhostReplayer.LayOutCarried` attaches it
        // with a zero offset). Lifting only the player's is what left a past self's pour happening
        // down at the floor while the living player's happened at the chest - reported as the two
        // motions not matching.
        private void PosePourHold(CarryableItem item, float amount)
        {
            if (item == null) return;

            if (item.IsCarriedByPlayer)
            {
                item.transform.localPosition = item.handLocalPosition + pourHoldOffset * amount;
                return;
            }

            if (item.HeldByGhost == null) return;

            // A GHOST'S LIFT IS COMPUTED IN WORLD SPACE AND THEN BROUGHT BACK, and that is the whole
            // fix. The previous version applied `pourHoldOffset` in the anchor's LOCAL frame - and a
            // ghost's carry anchor is a WRIST BONE, whose "up" is wherever the arm happens to be
            // pointing. With the arm hanging at the side, "up" was sideways and the 0.46 of lift went
            // nowhere useful: the bucket stayed at hip height and tipped from there, which reads
            // exactly as pouring off the floor. It was reported twice, because the first fix moved it
            // in the wrong direction rather than not at all.
            //
            // The offset is also BIGGER here than the player's. A first-person pour only has to clear
            // the bottom of the screen; a ghost seen from across the room has to visibly raise a pail
            // to a tank, and the arm it is attached to is not going to help.
            Transform anchor = item.transform.parent;
            if (anchor == null) return;

            Vector3 worldLift = Vector3.up * (ghostPourLift * amount)
                              + anchor.forward * (ghostPourReach * amount);
            item.transform.localPosition = anchor.InverseTransformVector(worldLift);
        }

        // How far a past self raises the pail, in METRES of world height - not in the wrist's axes.
        public float ghostPourLift = 0.78f;
        public float ghostPourReach = 0.18f;

        private void ReleasePlayer()
        {
            if (heldPlayer != null)
            {
                heldPlayer.MovementLocked = false;
                heldPlayer.LookLocked = false;
            }
            heldPlayer = null;
        }

        // The lip's own rotation, 0 upright and 1 fully over. Everything the bucket is made of hangs
        // under this, so the water inside goes with it.
        private void SetTilt(float amount)
        {
            tilt = Mathf.Clamp01(amount);
            if (pourPivot != null)
                pourPivot.localRotation = Quaternion.AngleAxis(pourAngle * tilt, Vector3.right);
        }

        // The falling water, from the lip down to whatever surface it is landing on. Placed in world
        // space because both ends are: the lip is riding a hand that moves, and the tank's waterline
        // rises while this is running.
        private void ShowStream(WaterTank tank)
        {
            if (pourStream == null || pourPivot == null) return;
            if (!pourStream.gameObject.activeSelf) pourStream.gameObject.SetActive(true);

            Vector3 lip = pourPivot.position;
            float landing = tank != null ? tank.SurfaceWorldY : lip.y - 0.4f;
            float length = Mathf.Clamp(lip.y - landing, 0.12f, maxStreamLength);

            // The mesh is a unit column centred on its own middle, so it hangs from the lip when its
            // centre is half a length below it.
            pourStream.position = lip - Vector3.up * (length * 0.5f);
            // Straight DOWN, whatever the bucket is doing - this is falling water, not a decoration
            // stuck to a tipped pail.
            pourStream.rotation = Quaternion.identity;

            // Local, because the stream is a child of a root scaled to the size the game wants the
            // bucket at. The scale is uniform, so one divide undoes all of it.
            float parentScale = transform.lossyScale.x;
            if (parentScale < 0.0001f) parentScale = 1f;
            pourStream.localScale = new Vector3(streamWidth, length, streamWidth) / parentScale;
        }

        private void HideStream()
        {
            if (pourStream != null) pourStream.gameObject.SetActive(false);
        }

        // A CYCLE GOING TO SLEEP kills every coroutine under it without telling them (Cycle.SetAwake
        // deactivates the whole world root). Left as it was, this bucket would wake up still believing
        // it was mid-pour and refuse every pour after it - a flag left set by a path that never runs
        // the code that clears it, which is the failure §6 asks to trace after every change.
        private void OnDisable()
        {
            IsPouring = false;
            SetTilt(0f);
            HideStream();
            ReleasePlayer();
        }

        // The loop rewinding. `ItemRegistry.ReturnAllToOrigin` puts the bucket back; this is what
        // forgets what was in it. Without it the second iteration starts with a full bucket standing
        // where the first one filled it, which is water stored across a boundary - the one thing
        // docs/decisions.md rules out.
        public void EmptyInstant()
        {
            // A pour in flight is world state like any other, and it must not survive the boundary -
            // the object it is tipping is about to be teleported back to its origin, so the coroutine
            // would spend the next second emptying a bucket the player is not holding.
            StopAllCoroutines();
            IsPouring = false;
            SetTilt(0f);
            HideStream();
            ReleasePlayer();

            Level = 0f;
            feedingTap = null;
            if (overflow != null) overflow.SetActive(false);
            Apply();
        }

        private void Apply()
        {
            if (waterBody == null) return;

            float h = Level * innerHeight;
            // A primitive cylinder is 2 units tall, so Y scale is the half-height, and it grows about
            // its own centre - which is why the position moves with it.
            waterBody.localScale = new Vector3(innerRadius * 2f, Mathf.Max(0.0001f, h) / 2f, innerRadius * 2f);
            waterBody.localPosition = new Vector3(0f, innerBottom + h / 2f, 0f);

            Renderer r = waterBody.GetComponent<Renderer>();
            if (r != null) r.enabled = Level > 0.005f;
        }
    }
}
