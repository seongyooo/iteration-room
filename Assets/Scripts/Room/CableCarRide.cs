using System.Collections;
using UnityEngine;

namespace IterationRoom
{
    // THE CAR THAT TAKES THE PLAYER OUT. It comes through the wall of room3-2N, waits, and climbs
    // back up the shaft past cycles 3, 2 and 1 to the surface.
    //
    // **IT IS `SlideRide` ON A LONGER PATH, AND THAT IS DELIBERATE.** The slide in room2-5 is
    // already "the only thing in this game that moves the player for them": a path measured at build
    // time, walked frame by frame, control taken and given back, and a guarantee that whoever gets
    // on ARRIVES. Every one of those is what this needs, so this is the second instance of that
    // pattern rather than a new idea. Read `SlideRide`'s header first - the reasoning there is the
    // reasoning here.
    //
    // WHAT IS DIFFERENT, and each difference is a decision rather than a variation:
    //
    //   - **BOARDING IS WALKING IN, NOT PRESSING E.** There is no `E` on this vehicle and there must
    //     not be. CLAUDE.md 1.8 was closed twice - Room4's plate and the calibration room's start
    //     button were the two fixtures that lived while `AcceptsInput` was false, and as of
    //     2026-08-31 there are none. This runs after `RunOver` is set, so an E fixture here would be
    //     the third exception to a rule that has been shut twice, for a press that says nothing a
    //     doorway does not already say. Step inside; the doors close.
    //
    //   - **THE PLAYER IS A PASSENGER, NOT A CAMERA ON A RAIL.** `SlideRide` takes `ControlEnabled`
    //     away and drives the body down a chute, which is right for a two-second fall. This is a
    //     minute-long ride whose entire purpose is looking at things, so nothing is taken: the player
    //     walks around the cabin, leans on whichever window they like, and turns their head freely.
    //
    //     **THE CAR CARRIES THEM, IT DOES NOT PLACE THEM.** Every frame the car moves, the same
    //     delta is handed to the player's own `CharacterController` - so they ride *with* the cabin
    //     while still being the thing that decides where in it they stand. The first version pinned
    //     them to a seat point, which is what "riding" looked like from the outside and nothing like
    //     it from the inside: the player was welded to the middle of the floor with the controls
    //     dead. See `CarryPlayer`.
    //
    //   - **IT DOES NOT HAND CONTROL BACK.** The slide is a passage between two rooms and the player
    //     keeps playing at the bottom. This ends the game; `EndingDeparture` takes it from here.
    //
    // UNSCALED TIME THROUGHOUT, like every sequence that outlives the loop.
    public class CableCarRide : MonoBehaviour
    {
        // The car's own body. Moved along the path; everything else here is a child of it.
        public Transform car;

        // WHERE THE PLAYER STANDS INSIDE IT. An empty at floor level in the cabin - the player is
        // pinned to this every frame of the ride rather than parented to the car, because a
        // `CharacterController` under a moving parent fights its own collision resolution and jitters.
        // Writing the position outright is what `SlideRide` does and for the same reason.
        public Transform seat;

        // THE PATH, in world space, from the mouth of the breach to wherever the ride ends. Written
        // by `SceneBuilder`, which is the only thing that knows where the cycles are - see
        // `BuildCableCarPath`. Points, not a spline: the car interpolates between them and the
        // spacing is what shapes the climb.
        public Vector3[] path;

        // How fast the car travels, in metres a second. Slow. The building is about 60m top to
        // bottom and there is a great deal to look at.
        public float speed = 3.2f;
        // Eased in and out over this fraction of the whole run, so the car neither jerks off the
        // platform nor stops dead at the top.
        public float easeFraction = 0.12f;

        // **WHERE IT COMES FROM, AND IT USED TO COME FROM INSIDE THE BUILDING.** This was
        // `(-14, 0, 0)`: fourteen metres in -X of the resting place, which is back through the
        // breach and into room3-2N. The car flew out of the room it was arriving at.
        //
        // Straight up the shaft instead - it rises into the platform along the rope it hangs from,
        // which is both what a cable car does and the only direction with nothing in it. Authored by
        // `SceneBuilder` from the ride's own path.
        public Vector3 arriveFrom = new Vector3(0f, -22f, 0f);
        public float arriveSeconds = 6.5f;

        // The doors, which are separate meshes in the model - `SC8_Door-R-LowPoly` and its left
        // twin. Slid rather than swung; the real SC8's are plug doors and nobody will be looking
        // that closely at the one moment they move.
        public Transform doorLeft;
        public Transform doorRight;
        // **THE LEAVES SLIDE ALONG THE CABIN'S LOCAL Y, WHICH IS SIDEWAYS - NOT ALONG X, WHICH IS
        // THE DOORWAY'S NORMAL.**
        //
        // This was `(0.62, 0, 0)` and that is the axis a door opens THROUGH, not the one it opens
        // ALONG: one leaf was travelling 0.62m straight out of the doorway and into the space the
        // player walks in by, and the other the same distance into the cabin. It read as the doors
        // not opening, and as something in the way of the entrance.
        //
        // Derived rather than guessed. The two leaves are measured at the same place in the model
        // except for their native Y (+-0.08 - the build logs it), so native Y is the doorway's WIDTH
        // axis. `SplitDoor` parents each pivot under the model with no rotation of its own, so a
        // `localPosition` written here is in the model's frame, and the model's Y is that same axis.
        public Vector3 doorOpenOffset = new Vector3(0f, 0.62f, 0f);
        public float doorSeconds = 1.6f;

        // **THE CAR TURNS SIDE-ON BEFORE IT LEAVES** (2026-09-01, by request).
        //
        // It docks with its doorway facing the breach, because that is the side the player walks in
        // through. That is the wrong way round for the next minute: the doorway end is the narrow
        // end, so a car that travelled as it docked would spend the whole ride showing the building
        // through a doorframe.
        //
        // A quarter turn puts the long glazed flank outward, which is the side the cabin has windows
        // on and the reason the glass was taken down to almost nothing. It is also what a real
        // gondola does - it hangs square to its rope and the doors face the platform.
        //
        // **IT TURNS WHERE THE ROPE TURNS** (2026-09-01, by request), not on the platform before it
        // leaves. The first leg of the path runs straight out from the wall and the car takes it nose
        // first, the way it arrived; the rope then bends upward, and that is where a real one would
        // swing round its own hanger. Turning early made the departure read as a pirouette.
        //
        // The bend is the first corner in the path, and `Ride` works out where along the run that
        // falls rather than being told - so a path with a different first leg turns in the right
        // place without this number changing.
        public float travelYaw = 90f;
        // How much of the whole run the turn is spread over, centred on the bend.
        public float turnSpan = 0.09f;

        // How tall the cabin is, measured off the model at build time. Read by `SceneBuilder` to
        // hang the rope at the top of the hanger arm rather than at a guessed height - the two have
        // to agree or the car dangles below its own cable.
        public float cabinHeight = 4.2f;

        // **THE VOLUME THAT COUNTS AS INSIDE, AND IT IS THE CABIN'S OWN INTERIOR - NOT A GENEROUS
        // BOX AROUND IT.**
        //
        // It was generous on the reasoning that this is the last thing the game asks and must not be
        // possible to fail. That was safe while the car waited 3.2m beyond the wall. It stopped
        // being safe the moment the car came in to DOCK against it: at a half-extent of 1.31m and a
        // cabin centre 1.0m beyond the wall, the volume reached 0.31m back INTO room3-2N - so a
        // player walking up to the breach was counted as aboard before they had left the room.
        //
        // What that looked like from the inside is exactly what play reported: the doors open, and
        // then an invisible wall. `WaitForBoarding` saw `Inside()` come back true, decided boarding
        // was done, and sealed the doorway (`SetDoorways(true)`) while the player was still standing
        // in front of it.
        //
        // Authored by `SceneBuilder` from the collision cage's INNER faces, so "inside" means inside
        // the box the player can actually stand in and nothing wider.
        public Vector3 boardingHalfExtents = new Vector3(0.7f, 1.9f, 0.58f);

        // **THE TWO FACES THAT OPEN TO LET SOMEBODY IN, AND SHUT BEFORE THE CAR MOVES.**
        //
        // The cabin is a sealed box of colliders. These two - the cabin's near and far sides - are
        // off while the doors stand open and ON from the moment somebody is aboard, so that from
        // departure onward there is no gap in the cage anywhere.
        //
        // **WHY TWO AND NOT ONE, which is the interesting part.** The obvious build opens the side
        // the doorway is on. Nothing in this project can say which side that is: the model's two door
        // leaves report an identical offset of (0.08, -2.05, 0.00) from the cabin centre - they are
        // two leaves of one sliding door, modelled nearly shut and near the cabin's own middle - so
        // the horizontal signal is 0.08 in a hull 1.74 wide, which is noise. Deriving a facing from
        // it produced a car that play reported as backwards.
        //
        // Opening both sides removes the question rather than answering it. Whichever way the model's
        // doorway actually points, the player can walk in; and because both shut together the instant
        // they are inside, the cabin is closed for every second the floor is moving. The gangway runs
        // under the whole car for the same reason - see `BuildGangway`.
        //
        // It matters because the failure is not cosmetic: the player fell out repeatedly, sixty
        // metres up, in a game with no fall damage and no kill plane.
        public Collider[] doorwayWalls;

        public Transform player;
        public FirstPersonController controller;

        public AudioSource audioSource;
        public AudioClip arriveClip;
        public AudioClip doorClip;
        public AudioClip departClip;
        // The hum, looped under the whole ride and faded up as the car takes the load.
        public AudioSource motor;

        // How far along the path the car is, 0 to 1, for whoever is pacing the reveal off it -
        // `EndingDeparture` lights the shaft a storey at a time from this.
        public float Progress { get; private set; }
        public bool Boarded { get; private set; }
        public bool Arrived { get; private set; }

        private void Awake()
        {
            if (car != null) car.gameObject.SetActive(false);
        }

        // THE CAR COMES IN. Called once the wall is open.
        public IEnumerator Arrive()
        {
            if (car == null) yield break;
            car.gameObject.SetActive(true);

            Vector3 rest = PathPoint(0f);
            Vector3 from = rest + arriveFrom;
            if (audioSource != null && arriveClip != null) audioSource.PlayOneShot(arriveClip);

            float t = 0f;
            while (t < arriveSeconds)
            {
                t += EndingClock.Delta;
                // Eased to a stop rather than a linear slide: a car that arrives at constant speed
                // and stops on a frame reads as a prop being placed.
                car.position = Vector3.Lerp(from, rest, Mathf.SmoothStep(0f, 1f, t / arriveSeconds));
                yield return null;
            }
            car.position = rest;

            yield return Doors(true);
            Arrived = true;
        }

        // WAITS FOR THE PLAYER TO WALK IN. No timer on it at all - the same courtesy the break
        // upstairs already extends, where "walking out through what you built is the whole of this
        // beat" and nothing hurries it. A player who wants to stand in the wrecked room and look at
        // the facility for five minutes before boarding may.
        public IEnumerator WaitForBoarding()
        {
            // Open while it is being boarded, which is the whole of what makes it boardable.
            SetDoorways(false);

            while (!Inside()) yield return null;
            Boarded = true;

            // **SEALED BEFORE THE DOORS EVEN START MOVING.** The colliders go in first and the
            // leaves follow: the doors take `doorSeconds` and the car pulls away as they close, and
            // an open side during those seconds is exactly when the floor is accelerating hardest
            // under the player's feet.
            SetDoorways(true);

            yield return Doors(false);
        }

        private void SetDoorways(bool solid)
        {
            if (doorwayWalls == null) return;
            foreach (Collider wall in doorwayWalls)
                if (wall != null) wall.enabled = solid;
        }

        private bool Inside()
        {
            if (player == null || seat == null) return false;
            Vector3 local = seat.InverseTransformPoint(player.position);
            return Mathf.Abs(local.x) <= boardingHalfExtents.x
                && Mathf.Abs(local.y) <= boardingHalfExtents.y
                && Mathf.Abs(local.z) <= boardingHalfExtents.z;
        }

        // BOARDED, DOORS SHUT, GONE - as one call, so nothing sits between the player stepping in
        // and the car leaving. It read as a vehicle waiting to be told twice; **it departs the
        // moment somebody is aboard** (2026-09-01, by request).
        //
        // The doors close WHILE it pulls away rather than before, which is both quicker and what a
        // real one does.
        public IEnumerator BoardAndDepart()
        {
            yield return WaitForBoarding();
            yield return ShutTheDoors();
            yield return Ride();
        }

        // **THE DOORS, SHUT AND THEN NAILED SHUT.** `WaitForBoarding` slides them closed over
        // `doorSeconds`; an animation is something that can be interrupted - by a pause, by a
        // coroutine being stopped, by the car not being active for a frame - and play twice reported
        // the car leaving with its doors open. This waits for the slide and then writes the shut pose
        // outright, which cannot be interrupted.
        public IEnumerator ShutTheDoors()
        {
            float t = 0f;
            while (t < doorSeconds)
            {
                t += EndingClock.Delta;
                yield return null;
            }

            if (doorLeft != null) doorLeft.localPosition = shutLeft;
            if (doorRight != null) doorRight.localPosition = shutRight;
        }

        // How far along the whole run the path's first corner falls. The car travels nose-first up to
        // it and has finished turning by the time it is past - see `travelYaw`.
        private float BendProgress()
        {
            if (path == null || path.Length < 3) return 0f;
            return Vector3.Distance(path[0], path[1]) / Mathf.Max(0.01f, PathLength());
        }

        // THE RIDE. The car walks the path; the player rides in it and is otherwise left alone.
        public IEnumerator Ride()
        {
            if (car == null || path == null || path.Length < 2) yield break;

            // **CONTROL IS NOT TAKEN AND `Riding` IS NOT SET.** Both were, and both were wrong: the
            // point of this minute is to be inside the cabin looking out, and a passenger who cannot
            // walk to the other window is a photograph. What keeps them in the car is the cabin's own
            // colliders (`SceneBuilder.BuildCableCar` builds a cage) plus `CarryPlayer` below.
            if (audioSource != null && departClip != null) audioSource.PlayOneShot(departClip);

            lastCarPosition = car.position;

            float length = PathLength();
            float duration = Mathf.Max(1f, length / Mathf.Max(0.1f, speed));

            // Where it docked, and where along the run the rope turns - both fixed for the ride.
            Quaternion docked = car.rotation;
            float bend = BendProgress();

            float t = 0f;
            while (t < duration)
            {
                t += EndingClock.Delta;
                float raw = Mathf.Clamp01(t / duration);
                // Eased at both ends only. The middle is deliberately constant speed - a cable car
                // that accelerates through the interesting part is one the eye cannot track.
                Progress = Ease(raw);
                car.position = PathPoint(Progress);

                // AND IT SWINGS AT THE CORNER. Eased across `turnSpan` centred on the bend, so the
                // cabin is still nose-first as it leaves the platform and side-on by the time the
                // rope is vertical - which is the one moment there is anything to look at through the
                // flank.
                float turn = Mathf.SmoothStep(0f, 1f,
                    Mathf.InverseLerp(bend - turnSpan * 0.5f, bend + turnSpan * 0.5f, Progress));
                car.rotation = Quaternion.Slerp(docked, docked * Quaternion.Euler(0f, travelYaw, 0f),
                                                turn);
                if (motor != null) motor.volume = Mathf.Clamp01(raw * 8f) * (1f - Mathf.Clamp01((raw - 0.9f) * 10f));
                CarryPlayer();
                yield return null;
            }

            Progress = 1f;
            car.position = PathPoint(1f);
            CarryPlayer();
        }

        // **THE CABIN TAKES THE PLAYER WITH IT, AND THEY KEEP THEIR FEET.**
        //
        // The car's movement this frame is handed to the player's own `CharacterController` as a
        // `Move`, which is the difference between being carried and being placed: collisions still
        // resolve, so the cabin wall stops them at the wall, and whatever they were doing with the
        // keys still happens on top of it.
        //
        // WHY IT CANNOT BE PARENTING. A `CharacterController` under a moving parent fights its own
        // collision resolution and jitters - it resolves against a world that moved between its own
        // sweep and its own commit. Adding the delta is the same answer `SlideRide` reaches by a
        // different route.
        //
        // WHY IT CANNOT BE LEFT TO THE FLOOR. A floor that rises into a standing character does not
        // push it: `CharacterController` is not a rigidbody and takes no impulse from a collider that
        // moves into it. Without this the cabin would climb out from under the player and leave them
        // standing in the air where the car used to be.
        private Vector3 lastCarPosition;

        private void CarryPlayer()
        {
            if (player == null || car == null) return;

            Vector3 delta = car.position - lastCarPosition;
            lastCarPosition = car.position;

            CharacterController cc = player.GetComponent<CharacterController>();
            if (cc != null && cc.enabled) cc.Move(delta);
            else player.position += delta;

            KeepAboard();
        }

        // **THE ONE THING THAT MUST NOT HAPPEN IS FALLING OUT AT SIXTY METRES.**
        //
        // The cage should make it impossible and this is here anyway, because the cost of being
        // wrong is the last minute of the game ending with the player dropping past the facility.
        // Nothing in this project catches a fall - there is no kill plane and no fall damage - so a
        // player outside the cabin at this point is a player watching the ending from underneath it.
        //
        // Deliberately a SNAP and not a nudge: if this ever runs, something has already gone wrong,
        // and the honest repair is to put them back on the floor rather than to slide them there
        // gently while they are somewhere they cannot be.
        private void KeepAboard()
        {
            if (seat == null || player == null) return;

            Vector3 local = seat.InverseTransformPoint(player.position);
            bool overboard = Mathf.Abs(local.x) > boardingHalfExtents.x + 0.5f
                          || Mathf.Abs(local.z) > boardingHalfExtents.z + 0.5f
                          || local.y < -0.8f || local.y > boardingHalfExtents.y + 1.5f;
            if (!overboard) return;

            CharacterController cc = player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            player.position = seat.position + Vector3.up * 0.1f;
            if (cc != null) cc.enabled = true;
        }

        private IEnumerator Doors(bool open)
        {
            if (audioSource != null && doorClip != null) audioSource.PlayOneShot(doorClip);

            Vector3 leftShut = doorLeft != null ? doorLeft.localPosition : Vector3.zero;
            Vector3 rightShut = doorRight != null ? doorRight.localPosition : Vector3.zero;
            // Captured on the first call, which is the open one, so the shut pose is the authored
            // one and re-shutting returns exactly to it.
            if (open)
            {
                shutLeft = leftShut;
                shutRight = rightShut;
            }

            float t = 0f;
            while (t < doorSeconds)
            {
                t += EndingClock.Delta;
                float amount = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / doorSeconds));
                if (!open) amount = 1f - amount;
                if (doorLeft != null) doorLeft.localPosition = shutLeft - doorOpenOffset * amount;
                if (doorRight != null) doorRight.localPosition = shutRight + doorOpenOffset * amount;
                yield return null;
            }
        }

        private Vector3 shutLeft, shutRight;

        private float Ease(float x)
        {
            float e = Mathf.Clamp(easeFraction, 0.001f, 0.49f);
            if (x < e) return Mathf.SmoothStep(0f, 1f, x / e) * e;
            if (x > 1f - e) return 1f - Mathf.SmoothStep(0f, 1f, (1f - x) / e) * e;
            return x;
        }

        private float PathLength()
        {
            float length = 0f;
            for (int i = 1; i < path.Length; i++) length += Vector3.Distance(path[i - 1], path[i]);
            return length;
        }

        // Distance-parameterised, not index-parameterised: the waypoints are not evenly spaced (the
        // cycles are not evenly spaced), and stepping by index would make the car crawl through the
        // close ones and bolt through the long ones.
        private Vector3 PathPoint(float amount)
        {
            if (path == null || path.Length == 0) return transform.position;
            if (path.Length == 1) return path[0];

            float target = PathLength() * Mathf.Clamp01(amount);
            float travelled = 0f;
            for (int i = 1; i < path.Length; i++)
            {
                float segment = Vector3.Distance(path[i - 1], path[i]);
                if (travelled + segment >= target)
                {
                    float into = segment > 0f ? (target - travelled) / segment : 0f;
                    return Vector3.Lerp(path[i - 1], path[i], into);
                }
                travelled += segment;
            }
            return path[path.Length - 1];
        }
    }
}
