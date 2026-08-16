using UnityEngine;

namespace IterationRoom
{
    // ROOM2-2'S RULE: a glass column that has to be filled to a mark, and the door that opens when it
    // is. The room's first puzzle, and the first thing in this game that ONE ITERATION CANNOT DO.
    //
    // WHY A TANK IS THE RIGHT SHAPE FOR THIS GAME. Every puzzle in cycle 1 is fetch-and-place, and the
    // one thing they all have in common is that a single person could do them given enough time - the
    // loop only makes them faster. This one cannot be done alone at all: the tank takes several
    // bucketloads, a bucket takes time to fill AND time to carry, and the minute holds fewer trips
    // than the tank takes. The only way to finish it is for past selves to be running trips at the
    // same time you are. That is the loop being load bearing rather than decorative.
    //
    // NOTHING IS STORED ACROSS ITERATIONS, and that is deliberate and settled - see docs/decisions.md,
    // "accumulated work is RE-PERFORMED, never stored". The level resets with everything else at the
    // top of an iteration. What accumulates is not water, it is HANDS: iteration N has N past selves,
    // so more taps can be running at once, and once enough are the tank fills inside the minute - and
    // fills again every minute after, because the ghosts always redo it.
    public class WaterTank : RoomCondition
    {
        // HOW MUCH OF THE TANK ONE FULL BUCKET IS. This is the number that decides how many trips the
        // room costs, so it is the room's length dial - and it lives in SceneBuilder like every other
        // tuned value in this game.
        public float bucketFraction = 0.2f;

        // How full counts as full. Not 1.0 - a tank you have to fill to the absolute brim reads as
        // unfinished right up until the last instant, and the mark is what the player is aiming at.
        public float targetLevel = 0.75f;

        // The column of water inside the glass. Scaled and raised from Level, so its top face IS the
        // waterline the player reads.
        public Transform waterBody;
        // The tank's inside height, in metres - what Level is a fraction of.
        public float innerHeight = 2.2f;

        // WHERE THE INSIDE STARTS, in the tank's own space. The glass stands on a plinth, so the
        // water does not begin at the tank's origin - and `Apply` writes the column's position
        // outright, so leaving this out did not merely offset the water, it DELETED the offset the
        // build had placed it at. The column filled from the floor and the waterline sat a plinth's
        // height below the mark the player is aiming at, which is the one thing in the room that has
        // to be readable.
        public float baseLocalY = 0.12f;

        // WHERE THE PLAYER IS AIMING WHEN THEY AIM AT THIS. The tank's own transform is on the floor,
        // and this column is a metre and a half tall - so "look at the tank" and "have the tank's
        // origin on screen" are different things, and a player standing at the glass looking at the
        // mark had the point that decides below their feet. Put at the mark itself.
        public Transform aimAnchor;
        public Transform Aim => aimAnchor != null ? aimAnchor : transform;

        // Where the pour into this tank is RECORDED, so a past self does it again. A pour is an
        // instant performed with an object - the same shape as a balloon pop - and it lives on its
        // own component because `RoomCondition` and `GhostInteractable` are both MonoBehaviours and
        // this is already one of them.
        public PourPoint pourPoint;

        public float Level { get; private set; }

        // THE TOP OF THE WATER, in world space - what a stream being poured in has to reach. Derived
        // from Level rather than measured off the renderer, so it is right on the frame it is asked
        // and does not depend on the column having been rebuilt yet.
        public float SurfaceWorldY
        {
            get
            {
                Transform frame = waterBody != null && waterBody.parent != null ? waterBody.parent : transform;
                return frame.TransformPoint(new Vector3(0f, baseLocalY + Level * innerHeight, 0f)).y;
            }
        }

        // Tracked so the door can be told the moment it is met rather than every frame after.
        public override bool Satisfied => Level >= targetLevel;

        // A bucket being emptied into it. `amount` is how full that bucket was, so a half-full one
        // adds half as much - the tank cannot be filled faster by pouring early, which would make
        // waiting for a full bucket pointless.
        public void Pour(float amount)
        {
            Level = Mathf.Clamp01(Level + amount * bucketFraction);
            Apply();
        }

        // THE LOOP REWINDING. Empty, instantly, behind the closed eyelids - the tank is world state
        // exactly like an open door, and left standing it would let the second iteration start from
        // wherever the first one got to. That is the one thing docs/decisions.md rules out: the water
        // is not a deposit, the ghosts are.
        public override void ResetCondition()
        {
            Level = 0f;
            // The recorded pour with it: a pulse outlives the iteration it was raised in, because it
            // is measured against a clock the loop does not rewind.
            if (pourPoint != null) pourPoint.ResetPulse();
            Apply();
        }

        private void Apply()
        {
            if (waterBody == null) return;

            // A primitive cylinder is 2 units tall, so its Y scale is the half-height - and it grows
            // from its own centre, which is why the position has to move with it or the column would
            // fill from the middle outward.
            float height = Mathf.Max(0.0001f, Level * innerHeight);
            Vector3 s = waterBody.localScale;
            waterBody.localScale = new Vector3(s.x, height / 2f, s.z);

            Vector3 p = waterBody.localPosition;
            waterBody.localPosition = new Vector3(p.x, baseLocalY + height / 2f, p.z);

            // Hidden entirely when empty: a zero-height cylinder is still a visible disc, and a disc
            // sitting in the bottom of an empty tank reads as water that is already there.
            Renderer r = waterBody.GetComponent<Renderer>();
            if (r != null) r.enabled = Level > 0.001f;
        }
    }
}
