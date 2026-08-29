using UnityEngine;

namespace IterationRoom
{
    // THE SLIDE IN ROOM2-5, RIDDEN - and the only thing in this game that moves the player for them.
    //
    // WHY IT IS SCRIPTED AT ALL. A `CharacterController` does not slide. It has no mass, its
    // `slopeLimit` decides only whether a surface is walkable, and nothing in Unity makes it run down
    // a chute - a player who steps onto the slide simply stands on it. So the ride is driven here,
    // frame by frame, along a path measured off the chute's own surface at build time.
    //
    // IT IS A PASSAGE, NOT AN ORNAMENT. The chute's foot goes through the hall's south wall, so this
    // is how the room on the other side is entered - see SceneBuilder.BuildSlide and BuildSlideRoom.
    // That is why it takes control rather than nudging: what it has to guarantee is that whoever gets
    // on ARRIVES, in the room, on the floor, facing into it.
    //
    // NOTHING ABOUT IT IS RECORDED, AND IT STILL REPLAYS. `PlayerRecorder` writes a position and a yaw
    // every frame, and the ride is nothing but positions and yaws - so a past self rides it exactly as
    // the living player did, with no `GhostInteractable`, no signal bit and no re-evaluated condition.
    // It costs nothing against the 32-bit budget in CLAUDE.md 1.6 for the same reason walking does.
    //
    // WHO OWNS CONTROL, which is the whole risk in a component like this:
    //   - the ride takes it with `ControlEnabled = false` and gives it back at the end;
    //   - if the ITERATION ends mid-ride, the ride lets go WITHOUT restoring control, because from
    //     that moment `LoopManager` owns it - it disables control itself over the blink, teleports the
    //     player to the bed, and `WakeUpSequence` is what turns it back on. Restoring it here would be
    //     a second writer fighting the loop over the same flag on the same frame.
    //   - a PAUSE is not an abort. `Time.deltaTime` is zero at `timeScale` 0, so the ride simply
    //     stands still and resumes, which is what pausing halfway down a slide should do.
    public class SlideRide : MonoBehaviour
    {
        // The ride, in THIS transform's local space: the chute's surface sampled head to foot, then
        // the run-out across the floor of the room it lands in. Written by `SceneBuilder`, which
        // raycasts the chute rather than describing it - so a slide that moves or changes shape gets
        // a new path without anybody editing numbers.
        public Vector3[] path;

        [Header("Speed along the path")]
        // Stepping ON is slow, the chute is fast, and the run-out stops you. All three are distances
        // rather than times: the ride has to end AT the last waypoint whatever the frame rate, and a
        // speed curve in time cannot promise that.
        public float entrySpeed = 1.8f;
        public float topSpeed = 6.5f;
        public float exitSpeed = 1.2f;
        public float accelDistance = 2.0f;
        public float brakeDistance = 2.5f;
        // How hard the speed itself may change, so a short segment cannot snap it.
        public float speedChangeRate = 24f;

        [Header("The pose while riding")]
        // Sitting. The eye is posed every frame rather than once: `SetEyePose` writes the same field
        // the crouch ramp uses, and posing it once would be undone the moment anything else moved it.
        public float rideEyeHeight = 0.85f;
        public float ridePitch = 16f;
        // Turning to face down the chute, rather than snapping - a rider who stepped on sideways
        // swings round over the first metre instead of being spun on the spot.
        public float turnRate = 540f;

        // The rider, and the fact that there IS one. Null means nobody is on the slide.
        private FirstPersonController rider;
        // The route this ride is actually taking, in WORLD space: where the player stepped on,
        // followed by `path`. Built per ride, because the first point is wherever they were standing.
        private Vector3[] route;
        // Cumulative distance to each point of `route`, so a distance can be turned into a position
        // without walking the whole polyline every frame.
        private float[] marks;
        private float total;
        private float travelled;
        private float speed;
        private float yaw;

        public bool Riding => rider != null;

        // WHICH WAYPOINT TO CARRY ON FROM, given where somebody stepped on. The first waypoint PAST
        // the nearest point on the chute - projected onto each segment rather than snapped to the
        // nearest corner, because a rider who is a third of the way along a segment should not be
        // pulled back to its start.
        private int NearestSegment(Vector3 worldPosition)
        {
            int best = 0;
            float bestDistance = float.MaxValue;

            for (int i = 0; i < path.Length - 1; i++)
            {
                Vector3 a = transform.TransformPoint(path[i]);
                Vector3 b = transform.TransformPoint(path[i + 1]);
                Vector3 ab = b - a;

                float lengthSqr = Mathf.Max(1e-6f, ab.sqrMagnitude);
                float t = Mathf.Clamp01(Vector3.Dot(worldPosition - a, ab) / lengthSqr);
                float d = (worldPosition - (a + ab * t)).sqrMagnitude;

                if (d >= bestDistance) continue;
                bestDistance = d;
                // Past the projection: the segment's far end. Joining at the very top gives 1, so the
                // route is the player's step-on point and then every waypoint - which is exactly what
                // this did before, and is why the top of the chute is unchanged.
                best = i + 1;
            }

            return Mathf.Clamp(best, 0, path.Length - 1);
        }

        // The player's own capsule is what fires this - a ghost has no collider (CLAUDE.md 1.7), so a
        // past self riding the slide can never start a second ride.
        private void OnTriggerEnter(Collider other)
        {
            if (rider != null || path == null || path.Length < 2) return;

            FirstPersonController controller = other.GetComponentInParent<FirstPersonController>();
            if (controller == null) return;

            // The same gate every interactable in the game uses. Getting on during the wake-up, the
            // pause or the collapse would drive the player through a room the loop is resetting.
            LoopManager loop = LoopManager.Instance;
            if (loop != null && !loop.AcceptsInput) return;

            Begin(controller);
        }

        private void Begin(FirstPersonController controller)
        {
            rider = controller;

            // FROM WHERE THEY ARE, not from the top of the chute. The trigger is a volume, so the
            // player can enter it a step short of the first waypoint - starting at the waypoint would
            // jerk them onto it. This makes the approach part of the ride.
            //
            // **AND FROM WHERE THEY ARE ALONG IT, not from the beginning of it** (2026-08-29, by
            // request: getting on halfway down should slide you down from halfway).
            //
            // The route used to be the player's position followed by the WHOLE path, so somebody
            // stepping on at the middle was dragged back up to the top and started again - the ride
            // was correct and the entry was a lie. Finding the nearest point on the polyline and
            // keeping only the waypoints past it makes joining partway the same act as joining at the
            // top, with no second code path: everything below - the speed ramp, the marks, the pose -
            // reads `route` and does not care where it came from.
            int from = NearestSegment(controller.transform.position);

            route = new Vector3[path.Length - from + 1];
            route[0] = controller.transform.position;
            for (int i = from; i < path.Length; i++) route[i - from + 1] = transform.TransformPoint(path[i]);

            marks = new float[route.Length];
            for (int i = 1; i < route.Length; i++)
                marks[i] = marks[i - 1] + Vector3.Distance(route[i - 1], route[i]);
            total = marks[marks.Length - 1];

            travelled = 0f;
            speed = entrySpeed;
            yaw = controller.transform.eulerAngles.y;

            controller.ControlEnabled = false;
            controller.SetEyePose(rideEyeHeight, ridePitch);
        }

        private void Update()
        {
            if (rider == null) return;

            // THE ITERATION ENDING IS NOT AN ABORT THIS COMPONENT HANDLES - it is a handover. See the
            // note at the top: control belongs to the loop from here, and touching it would fight.
            LoopManager loop = LoopManager.Instance;
            if (loop != null && !loop.IterationRunning) { rider = null; return; }

            float dt = Time.deltaTime;
            if (dt <= 0f) return;     // paused: stand still on the chute rather than sliding blind

            float remaining = total - travelled;
            float wanted = accelDistance > 0f
                ? Mathf.Lerp(entrySpeed, topSpeed, Mathf.Clamp01(travelled / accelDistance))
                : topSpeed;
            if (brakeDistance > 0f && remaining < brakeDistance)
                wanted = Mathf.Lerp(exitSpeed, wanted, remaining / brakeDistance);
            speed = Mathf.MoveTowards(speed, wanted, speedChangeRate * dt);

            travelled = Mathf.Min(total, travelled + speed * dt);
            Vector3 at = PointAt(travelled, out Vector3 heading);

            heading.y = 0f;
            if (heading.sqrMagnitude > 0.0001f)
                yaw = Mathf.MoveTowardsAngle(yaw, Mathf.Atan2(heading.x, heading.z) * Mathf.Rad2Deg,
                                             turnRate * dt);

            // Through `Teleport`, which disables the controller for the write: a `Move` would be
            // stopped by the chute it is meant to be sliding along, and by the wall it goes through.
            rider.Teleport(at, Quaternion.Euler(0f, yaw, 0f));
            rider.SetEyePose(rideEyeHeight, ridePitch);

            if (travelled >= total - 0.0001f) Finish();
        }

        private void Finish()
        {
            // Standing again, but still looking where the ride was looking. Levelling the pitch here
            // as well would snap the view up on the frame control returns, which reads as a second
            // event happening to the player after the one they took part in.
            rider.SetEyePose(rider.standingEyeHeight, ridePitch);
            rider.ControlEnabled = true;
            rider = null;
        }

        // Where `distance` along the route is, and which way it is going there.
        private Vector3 PointAt(float distance, out Vector3 heading)
        {
            for (int i = 1; i < route.Length; i++)
            {
                if (distance > marks[i] && i < route.Length - 1) continue;

                float span = marks[i] - marks[i - 1];
                float t = span > 0.0001f ? Mathf.Clamp01((distance - marks[i - 1]) / span) : 1f;
                heading = route[i] - route[i - 1];
                return Vector3.Lerp(route[i - 1], route[i], t);
            }

            heading = Vector3.zero;
            return route[route.Length - 1];
        }

        // The path as it will be ridden, drawn in the editor. This is the only view of it there is -
        // it is a float array on a component, and a mistake in it is invisible in the scene otherwise.
        private void OnDrawGizmosSelected()
        {
            if (path == null || path.Length < 2) return;
            Gizmos.color = Color.cyan;
            for (int i = 1; i < path.Length; i++)
                Gizmos.DrawLine(transform.TransformPoint(path[i - 1]), transform.TransformPoint(path[i]));
        }
    }
}
