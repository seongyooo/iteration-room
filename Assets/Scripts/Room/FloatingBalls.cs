using UnityEngine;

namespace IterationRoom
{
    // EVERYTHING FLOATING ON ROOM2-5'S POOL: the plastic balls, the rubber ducks and the beach balls.
    //
    // THEY ARE REAL RIGIDBODIES NOW (2026-08-19, second pass), and the reason is that play asked for
    // them to bump into each other. The first version wrote each object's position from a sine of the
    // clock, which cannot collide with anything by construction - two balls crossing simply passed
    // through one another, and so did the player.
    //
    // THIS DOES NOT BREAK THE NO-PHYSICS RULE, it is the same exception the tap's spray already takes.
    // CLAUDE.md §4 forbids rigidbodies on CARRYABLES, because the loop has to put every carryable back
    // exactly and PhysX solves in islands - where a dropped object settles depends on everything near
    // it, including a living player who moves differently every iteration. Nothing here is carried,
    // recorded, socketed or reset: a pool of balls that ends the iteration in a different arrangement
    // than it started is not a fact any past self depends on.
    //
    // WHAT THIS COMPONENT ADDS TO THE PHYSICS, in one FixedUpdate over its whole list:
    //   - **buoyancy**, as a damped spring to the height it was placed at rather than gravity plus a
    //     water volume. Gravity is off entirely, so nothing can sink and nothing can be knocked out of
    //     the pool by a hard enough shove;
    //   - **mooring**, a very weak pull back toward where it was placed. Without it a room somebody
    //     walks through for fifteen minutes ends with everything heaped in one corner; with it the
    //     spread survives, and at this strength no single frame of it is visible;
    //   - **bob and drift**, which are OFF for the plastic balls and on for the ducks and the beach
    //     balls. That is not an inconsistency: with both at zero a ball settles, stops being written
    //     to, and PhysX puts it to sleep - so 210 of them cost nothing until something disturbs them,
    //     and only the five big floats are awake all the time.
    //
    // THE PLAYER PUSHES THEM AND WALKS THROUGH THEM, which is `FirstPersonController.pushLayers` doing
    // the job it was built for with the balloons. They are on the balloon layer for exactly that: the
    // controller EXCLUDES that layer, so there is no wall to walk into, and the push sweep shoves
    // whatever is in reach out of the way.
    //
    // A GHOST DOES NOT DISTURB THE WATER. Ghosts have no colliders (CLAUDE.md §1.7) and are not going
    // to get one, so a past self wades through the pool leaving it flat. It is a real inconsistency,
    // accepted: a colliding ghost would change the recording being made against it, which is a much
    // worse thing to be wrong about than still water.
    public class FloatingBalls : MonoBehaviour
    {
        // The bodies, and where each was placed - in THIS transform's local space, converted once on
        // waking. Written by `SceneBuilder`.
        public Rigidbody[] bodies;
        public Vector3[] homes;
        // The carryable on each body, where there is one - the ducks and the beach balls can be picked
        // up and taken to room2-7's scale. Parallel to `bodies`, and null entries are fine: a plastic
        // ball is not something anybody can carry off.
        public CarryableItem[] carried;

        [Header("Buoyancy")]
        // A damped spring to the resting height. Stiff enough to look like water holding something up
        // and damped enough that a shove does not set up a bounce that never ends.
        public float buoyancy = 26f;
        public float buoyancyDamping = 7f;

        [Header("Staying put")]
        // The pull back toward where it was placed. Deliberately far weaker than the buoyancy: this is
        // a tidy-up that takes half a minute, not a rubber band.
        public float mooring = 0.35f;

        [Header("Bob and drift - zero for the balls, on for the big floats")]
        public float bobHeight = 0f;
        public float bobPeriod = 4f;
        public float drift = 0f;
        public float driftPeriod = 20f;

        [Header("Leaving the pool")]
        // WHAT COUNTS AS "NOT IN THE POOL ANY MORE" is now `CarryableItem.Released` - see the note in
        // FixedUpdate. Nothing to tune: an object is this component's while it is floating where it
        // was put, and the carryable machinery's from the moment a hand touches it until the loop
        // hands it back.

        [Header("The drain")]
        // WHERE THE WATER IS GOING, while it is going. Set by `PoolDrain` for exactly as long as the
        // room is emptying: everything on the surface is dragged toward the hole and swung round it,
        // which is what a draining pool does to whatever is floating on it and is the only reason a
        // vortex reads as a vortex rather than as a texture spinning on the water.
        public Transform vortexCentre;
        public float vortexPull = 0f;
        public float vortexSwirl = 0f;

        // HOW FAR THE WATER HAS DROPPED, added to every resting height. Written by `WaterPool.SetLevel`
        // as room2-6 drains, so a duck ends the drain sitting on the floor rather than hanging where
        // the surface used to be. Nothing else may write it: the pool is the one owner of the level.
        private float waterOffset;

        public void SetWaterOffset(float offset)
        {
            if (Mathf.Approximately(offset, waterOffset)) return;
            waterOffset = offset;

            // WAKE THEM. A settled ball is ASLEEP (see FixedUpdate), and a sleeping body ignores a
            // target that has moved out from under it - the pool would drain and leave 210 balls
            // hanging in the air.
            if (bodies == null) return;
            foreach (Rigidbody body in bodies) if (body != null) body.WakeUp();
        }

        // Per-object phase, so no two are ever at the same point of the same cycle. Derived from the
        // index rather than randomised, so nothing has to be seeded or serialised.
        private float[] phase;
        private Vector3[] worldHomes;

        private void Awake() => Rebuild();

        private void Rebuild()
        {
            if (bodies == null || homes == null) { phase = null; return; }

            int count = Mathf.Min(bodies.Length, homes.Length);
            phase = new float[count];
            worldHomes = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                // Two incommensurate multipliers, so the phases do not fall into a pattern the eye can
                // pick up as a wave travelling across the pool.
                phase[i] = (i * 2.39996f) % (Mathf.PI * 2f) + (i % 7) * 0.41f;
                worldHomes[i] = transform.TransformPoint(homes[i]);
            }
        }

        // FIXEDUPDATE, because everything here is a force. Writing forces from Update applies them a
        // variable number of times per physics step, which makes the drift frame-rate dependent.
        private void FixedUpdate()
        {
            if (bodies == null || homes == null) return;
            if (phase == null || worldHomes == null || phase.Length != Mathf.Min(bodies.Length, homes.Length))
                Rebuild();
            if (phase == null) return;

            float t = Time.time;
            float bobRate = bobPeriod > 0.01f ? Mathf.PI * 2f / bobPeriod : 0f;
            float driftRate = driftPeriod > 0.01f ? Mathf.PI * 2f / driftPeriod : 0f;

            for (int i = 0; i < phase.Length; i++)
            {
                Rigidbody body = bodies[i];
                if (body == null) continue;

                Vector3 home = worldHomes[i];
                home.y += waterOffset;
                Vector3 at = body.position;

                // NOT WHILE SOMEBODY HAS IT, and not once it has left the water. Both are the same
                // handover: the object stops being a float and becomes an ordinary carryable, whose
                // drop is scripted rather than simulated (CLAUDE.md §4). Kinematic while that is true,
                // so nothing here and nothing there is fighting over the same transform.
                //
                // **ASKED BEFORE THE SLEEP CHECK, and that ordering is the whole of it.** A duck that
                // has settled is ASLEEP, and a sleeping body skipped here would be picked up and
                // parented into the player's hand while still dynamic - PhysX would go on solving a
                // body whose transform something else is writing every frame, which is the one
                // arrangement Unity has no defined answer for.
                // **THE LEASH ONLY APPLIES TO THINGS THAT CAN BE CARRIED**, and that restriction is
                // load-bearing rather than an optimisation. The drain drags everything toward the
                // middle of the room; a plastic ball that started in a corner ends up further from its
                // own spot than the leash allows, was declared "taken out of the pool", went kinematic
                // and FROZE IN MID-AIR over a room that was still emptying. A ball cannot be taken out
                // of the pool - there is no hand that could do it - so it has no business being asked.
                // **WHETHER IT HAS BEEN TAKEN, NOT HOW FAR IT IS FROM HOME.** The distance test this
                // replaces was wrong twice for the same reason: the drain drags everything to the
                // middle of the room, so anything that started near a wall ends up further from its
                // own spot than any sane leash allows, is declared "carried off", goes kinematic and
                // FREEZES IN MID-AIR over a room that is still emptying. Restricting it to carryables
                // fixed the plastic balls and left the ducks and the beach balls doing exactly it.
                //
                // `Released` is the fact actually wanted: it is set when somebody puts the object
                // down and cleared when the loop returns it to its origin, so it means "this is out
                // in the world now" - which is precisely when `FallingItem` should own it and this
                // should not. No distance, nothing to tune, and nothing the water can trigger.
                CarryableItem carryable = carried != null && i < carried.Length ? carried[i] : null;
                bool gone = carryable != null && (carryable.IsCarried || carryable.Released);
                if (gone)
                {
                    if (!body.isKinematic) body.isKinematic = true;
                    continue;
                }
                if (body.isKinematic)
                {
                    // Back home - the loop has returned it. Taking it back cleanly means clearing the
                    // velocity it had when it was let go of, or a duck put back in the pool carries
                    // the last iteration's shove into this one.
                    body.isKinematic = false;
                    body.linearVelocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                }

                // LET SLEEPING BODIES LIE. Waking one to apply a force that would not move it is the
                // whole cost of this component, and `AddForce` wakes a body whatever its magnitude.
                if (body.IsSleeping()) continue;

                float p = phase[i];
                Vector3 velocity = body.linearVelocity;

                // VERTICAL: a spring to the resting height, with the swell moving that height rather
                // than being added on top - so a float that has been shoved under comes back up to
                // the water rather than to a number.
                float targetY = home.y + (bobHeight > 0f ? Mathf.Sin(t * bobRate + p * 2.3f) * bobHeight : 0f);
                float up = (targetY - at.y) * buoyancy - velocity.y * buoyancyDamping;

                // HORIZONTAL: the mooring, and the drift if this thing has one. The drift is an
                // ellipse at two different rates - a circle traced at one rate reads as a turntable.
                Vector3 back = home - at;
                back.y = 0f;
                Vector3 push = back * mooring;
                if (drift > 0f)
                {
                    push.x += Mathf.Cos(t * driftRate + p) * drift;
                    push.z += Mathf.Sin(t * driftRate * 0.71f + p * 1.7f) * drift;
                }

                // THE DRAIN, while there is one. It overrides the mooring rather than adding to it -
                // two forces arguing about where a duck belongs is a duck that hovers.
                if (vortexCentre != null && vortexPull > 0f)
                {
                    Vector3 toDrain = vortexCentre.position - at;
                    toDrain.y = 0f;
                    float distance = toDrain.magnitude;
                    if (distance > 0.01f)
                    {
                        Vector3 inward = toDrain / distance;
                        // Round and in at once. The swirl is a right angle to the pull and falls off
                        // with distance the other way round from it - fastest at the middle, which is
                        // what makes the last few seconds a spin rather than a slide.
                        Vector3 round = new Vector3(-inward.z, 0f, inward.x);
                        push = inward * vortexPull + round * (vortexSwirl / Mathf.Max(0.6f, distance));
                    }
                }

                Vector3 accel = new Vector3(push.x, up, push.z);
                // AT REST AND NOTHING TO DO: skip the call entirely so the body can fall asleep. This
                // is what makes 210 balls free; without it they are 210 permanently awake rigidbodies.
                if (accel.sqrMagnitude < 0.0004f && velocity.sqrMagnitude < 0.0004f) continue;

                body.AddForce(accel, ForceMode.Acceleration);
            }
        }
    }
}
