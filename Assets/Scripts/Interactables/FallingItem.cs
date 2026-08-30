using UnityEngine;

namespace IterationRoom
{
    // A carryable that falls when nothing is holding it up - let go of in mid-air, or standing on
    // another carryable that has just been picked up.
    //
    // It began as `StackedItem`, for the two cube-room towers alone. Putting objects DOWN made it
    // general: every carryable is now something the player can release at hand height, and an object
    // that teleports to the floor the instant you let go is the same wrong answer the towers gave
    // when they hung in mid-air.
    //
    // WHY NOT A RIGIDBODY. Carryables deliberately have none: the loop has to be able to put every
    // object back exactly, and a simulated fall settles somewhere slightly different every time.
    // PhysX is reproducible in principle - same build, same fixed steps, same input order - and this
    // game cannot supply the third of those. Physics solves in islands, so where a dropped object
    // ends up depends on every other body near it, and one of those is a LIVING PLAYER who moves
    // differently every iteration. A ghost's drop would land somewhere new each run, and "a past self
    // does exactly what you did" is the contract the whole accumulation rests on. So: one axis, real
    // gravity, one known height. The same shape of answer Door and RewardPlinth already give.
    //
    // Nothing here is recorded, and nothing needs to be. A fall is DERIVED - it happens because the
    // thing holding the object up went away - and a ghost repeating the release makes it happen
    // again by itself.
    [RequireComponent(typeof(CarryableItem))]
    public class FallingItem : MonoBehaviour
    {
        public CarryableItem item;

        // Another carryable this one was BUILT standing on, if any. Null is the common case and
        // simply means the floor is the only thing under it.
        public CarryableItem support;

        // Real gravity. There is no reason to invent a number: these falls are a metre or so, which
        // comes out at just under half a second, and anything slower reads as an object being
        // lowered rather than dropped.
        public float gravity = 9.81f;

        // How far the support may be from directly underneath and still count as underneath. Wide
        // enough to survive the jitter a stack is built with, narrow enough that a support dropped
        // somewhere else in the room is not still holding this up from across the floor.
        public float supportRadius = 0.45f;

        public AudioSource audioSource;
        public AudioClip landClip;

        // The height this was BUILT at, captured rather than configured - the same number
        // SceneBuilder already had to work out to place it, and reading it back is one fewer value
        // to keep in step. Only meaningful when there is a support; a floor-standing object's
        // resting height is its own RestingY. `stackedY` is a world height and needs no floor base of
        // its own - it was captured from where the object was actually built.
        private float stackedY;
        private float speed;

        // The arc of a deliberate put-down. A drop starts AT THE HAND - where the player can see the
        // object - and travels to a spot in front of them as it falls, so it leaves the hand instead
        // of appearing in mid-air already falling. Both are driven off the fall's own progress, so
        // the object arrives horizontally and rotationally at the exact moment it lands.
        private bool gliding;
        // What is under the glide's DESTINATION, captured at the release. See Update.
        private float glideFloor;
        private Vector3 glideFrom, glideTo;
        private Quaternion spinFrom, spinTo;
        private float glideTop, glideBottom;

        private void Awake()
        {
            if (item == null) item = GetComponent<CarryableItem>();
            stackedY = transform.position.y;
        }

        // Called by PlayerHand the moment it lets go, with where the object should come to rest and
        // the pose it had in the hand. DropAt has already run, so the transform is at the hand's
        // position with its resting rotation - this captures both ends and drives the change.
        public void Release(Vector3 restXZ, Quaternion fromWorldRotation)
        {
            Vector3 p = transform.position;
            glideFrom = p;
            glideTo = new Vector3(restXZ.x, p.y, restXZ.z);
            spinFrom = fromWorldRotation;
            spinTo = transform.rotation;
            glideTop = p.y;
            // **THE HIGHER OF THE TWO FLOORS - the one under the hand and the one under where it is
            // going.** They are the same number everywhere in this building except at a LEDGE, and
            // at a ledge getting it wrong is what makes a drop over the edge unreliable.
            //
            // Play found it in room3-2N (2026-08-29): blocks dropped off deck A and deck B "some
            // fall and some do not". The horizontal arc is paced by the descent from `glideTop` to
            // `glideBottom`, so with the FAR floor (the ground, six metres down) the object had
            // barely moved sideways by the time it reached the deck it was standing on - and it
            // settled there, on the lip, having gone nowhere. With the NEAR floor the sideways move
            // finishes within the drop from the hand to the deck, which is what clears the edge.
            //
            // The far case is the mirror image and wants the same answer: stepping off a lift onto a
            // deck, the near floor is the ground far below, and pacing the arc against it would
            // leave the object hanging over the drop when it reached the deck it was aimed at. The
            // maximum is right for both, because it is always the first floor the arc could END on.
            float underHand = item != null ? SurfaceUnder(transform.position) : 0f;
            float underRest = item != null
                ? SurfaceUnder(new Vector3(restXZ.x, p.y, restXZ.z))
                : 0f;
            // `float.NegativeInfinity` is "nothing under there at all" - a drop into a shaft. Max
            // takes the real floor whenever either end has one, and leaves the sentinel alone when
            // neither does, which is what makes the object keep going down the hole.
            glideBottom = Mathf.Max(underHand, underRest);
            gliding = glideTop > glideBottom + 0.01f;
            glideFloor = underRest;

            if (gliding) transform.rotation = spinFrom;
            else transform.position = new Vector3(restXZ.x, p.y, restXZ.z);
            speed = 0f;
        }

        private void Update()
        {
            // In a hand, on a ghost, or seated in a recess. Someone else owns where this is, and a
            // fall competing with them would fight the hand anchor for the transform.
            if (item == null || item.IsCarried) { speed = 0f; gliding = false; return; }

            // NOTHING FALLS OFF ITS OWN SHELF. Being above RestingY is not the same fact as having
            // been let go of, and treating them as one is exactly what dropped the pin out of its
            // drawer the moment the scene loaded - along with the escape objects off their plinths.
            // A support is the other reason to fall, and it is the one a stacked cube has: it was
            // never dropped, the thing underneath it left.
            if (support == null && !item.Released) { speed = 0f; gliding = false; return; }

            // ONLY EVER DOWN. A support put back underneath does not lift this off the floor again -
            // that would be an object climbing, and the only thing entitled to rebuild a tower is
            // the loop's own rewind, which sets the position outright.
            //
            // **WHILE GLIDING, THE FLOOR IS THE ONE UNDER WHERE IT IS GOING, not the one under where
            // it is now.** The object is on its way somewhere; asking what is beneath its CURRENT
            // position asks about a spot it is leaving, and at a ledge the two disagree for the
            // whole of the arc. That is the other half of the drop-off-a-deck fault the note in
            // `Release` describes: even with the arc paced right, a per-frame probe under the object
            // saw the deck for the first few frames and settled it there.
            float target = Supported ? stackedY
                         : (gliding ? glideFloor : SurfaceUnder(transform.position));
            Vector3 p = transform.position;
            // A target of negative infinity is "there is nothing under this" - see SurfaceUnder. The
            // comparison below would be false forever, which is exactly right, but say it out loud so
            // the next reader does not take it for an oversight.
            if (p.y <= target + 0.001f) { Settle(); speed = 0f; return; }

            speed += gravity * Time.deltaTime;
            float y = p.y - speed * Time.deltaTime;

            bool landed = y <= target;
            if (landed) y = target;

            Vector3 next = new Vector3(p.x, y, p.z);
            if (gliding)
            {
                // Progress measured off the DESCENT rather than off a clock, so the two halves of
                // the arc cannot drift apart - however long the fall takes, the object is over its
                // resting spot exactly when it reaches the floor.
                float u = Mathf.Clamp01((glideTop - y) / Mathf.Max(0.001f, glideTop - glideBottom));
                Vector3 across = Vector3.Lerp(glideFrom, glideTo, u);
                next.x = across.x;
                next.z = across.z;
                transform.rotation = Quaternion.Slerp(spinFrom, spinTo, u);
            }

            transform.position = next;

            if (!landed) return;
            Settle();
            speed = 0f;
            if (audioSource != null && landClip != null) audioSource.PlayOneShot(landClip);
        }

        // WHAT IS ACTUALLY UNDERNEATH, rather than the one floor height this cycle was told about.
        //
        // `item.RestingY` is `floorBaseY + floorY`, and `floorBaseY` is set ONCE PER CYCLE by
        // `SceneBuilder.SetFloorBase`. That was true of cycle 1 and true of cycle 2 right up until
        // 2026-08-19, when room2-6 and room2-7 were hung a further storey down: an axe carried into
        // either of them was released and fell to a height 3.7m ABOVE their floor, and stopped there,
        // in mid-air. The same arithmetic put anything dropped onto room2-7's scale INSIDE the scale.
        //
        // So the height is found rather than remembered: one ray straight down, and whatever it hits
        // is the floor - a room's slab, a plinth, the platform of a weighing scale. `RestingY` remains
        // the fallback for the case that finds nothing, which is what it always was.
        //
        // STILL DETERMINISTIC, which is the whole reason a fall is scripted at all (see the note at
        // the top): a raycast against the room is a question about geometry, not a simulation, and it
        // answers the same for a ghost's release as for the player's.
        // PUBLIC AND POSITIONAL, since 2026-08-20. It used to ask only about where the object
        // already is, which is every question this class has - but `CarryableItem.DropAt` needs the
        // same answer about a point the object has not been moved to yet, and there is no second
        // right way to ask "what is under here". See the clamp in `DropAt`.
        // **SAMPLED ALONG THE OBJECT, not asked once at its origin.** One ray answers for anything
        // that fits in a hand, and for anything long it answers about one END: a ladder dropped
        // across a bed had its pivot over clear floor, found the floor, and lay at floor height with
        // the bed passing through it - reported from play as "it goes under the bed".
        //
        // The highest of the samples wins, which is what resting ON something means. A span that is
        // partly over a hole still rests on whatever is under the rest of it, which is also right -
        // and if NOTHING is under any of it the sentinel survives the maximum untouched, so an object
        // let go over a shaft still goes down it.
        public float SurfaceUnder(Vector3 at)
        {
            if (item == null || item.heldSpan == Vector3.zero) return ProbeUnder(at);

            // END TO END ACROSS THE OBJECT. `heldSpan` is centred on the origin, so the samples run
            // from half a span behind the point asked about to half a span past it.
            Vector3 span = transform.TransformVector(item.heldSpan);
            Vector3 from = at - span * 0.5f;

            float best = float.NegativeInfinity;
            for (int i = 0; i <= spanSamples; i++)
                best = Mathf.Max(best, ProbeUnder(from + span * (i / (float)spanSamples)));

            return best;
        }

        // How many points along a long object are asked about, past its origin. Five is a rung every
        // 1.2m on the ladder, which is finer than anything it could come to rest on.
        public int spanSamples = 5;

        private float ProbeUnder(Vector3 at)
        {
            Vector3 from = at + Vector3.up * 0.05f;
            int count = Physics.RaycastNonAlloc(from, Vector3.down, floorHits, floorProbe, ~0,
                                                QueryTriggerInteraction.Ignore);

            float best = float.NegativeInfinity;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = floorHits[i];
                // Never its own collider, and never anything else hanging off this object - a
                // carryable's blocker is under its own feet by definition.
                if (hit.transform == null || hit.transform.IsChildOf(transform)) continue;
                // Nor the thing it is stacked ON, which `stackedY` already answers for.
                if (support != null && hit.transform.IsChildOf(support.transform)) continue;
                if (hit.point.y > best) best = hit.point.y;
            }

            // NOTHING UNDERNEATH MEANS KEEP FALLING, not settle where you are.
            //
            // Falling back to `RestingY` - the one floor height the cycle was told about - is what
            // made an object dropped over the tree hall's pit STOP IN MID-AIR over the hole, at the
            // height the floor would have been if the floor were there. `float.NegativeInfinity` is
            // the honest answer: there is no surface, so the fall has no end and the object goes down
            // the shaft, which is what a hole is for.
            //
            // Nothing is lost by it. `ItemRegistry.ReturnAllToOrigin` sweeps every object home at the
            // top of the next iteration, so an axe thrown into the pit is gone for this iteration and
            // back for the following one - exactly what CLAUDE.md §1.2 says happens to an axe a past
            // self carried in there.
            return best > float.NegativeInfinity ? best + item.floorY : float.NegativeInfinity;
        }

        // How far down to look. Deep enough to find the bottom of the tree hall's pit, which is 26m
        // of shaft with a real floor at the end of it - at the 9m this started as, a drop into the pit
        // found nothing and the fallback above stopped the object at the lip.
        public float floorProbe = 32f;
        private readonly RaycastHit[] floorHits = new RaycastHit[8];

        // The arc's end state, written outright rather than left to the last lerp - a fall that is
        // interrupted by a landing one frame early would otherwise leave the object a few
        // centimetres and a few degrees short of where it belongs.
        private void Settle()
        {
            if (!gliding) return;
            gliding = false;
            transform.position = new Vector3(glideTo.x, transform.position.y, glideTo.z);
            transform.rotation = spinTo;
        }

        // Still under this and still free-standing. `IsCarried` covers all three ways a support can
        // stop being furniture at once - picked up, taken by a ghost, seated in its recess - and the
        // distance test covers the fourth, which is being put back down somewhere else.
        private bool Supported
        {
            get
            {
                if (support == null || support.IsCarried) return false;

                Vector3 d = support.transform.position - transform.position;
                return d.y < 0f
                    && Mathf.Abs(d.x) < supportRadius
                    && Mathf.Abs(d.z) < supportRadius;
            }
        }
    }
}
