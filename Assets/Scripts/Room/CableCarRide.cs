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

        // **THE CAR DOES NOT REACH THE TOP** (2026-09-03, by request). It sways further and further
        // as it climbs, and then it drops, with the player in it. See `Sway` and `Fall`.
        //
        // The sway is a ROLL ABOUT THE DIRECTION OF TRAVEL, which is what a gondola on a rope does -
        // it is hung from one point above its centre of mass, so the only thing it can do freely is
        // swing side to side. Growing amplitude rather than constant: a car that wobbles the same
        // amount for a minute reads as an idle animation, and one that wobbles more every ten
        // seconds reads as something coming loose.
        public float swayDegrees = 16f;
        // Swings per second. Slow - a loaded cabin on 200m of rope has a long period, and a fast
        // wobble reads as a physics glitch rather than as mass.
        public float swayRate = 0.28f;
        // Where it starts being visible. Nothing for the first third: the ride has to feel like it
        // is working before it stops working.
        public float swayFrom = 0.30f;

        // Where the climb ends. Sized so the PA finishes first - the fourteen ride lines are 54.0s
        // of audio in a 68s climb - because a voice cut off mid-word reads as a bug, and a voice
        // that finishes its sentence and THEN lets go does not. Check both if either changes.
        public float fallAt = 0.92f;
        // 2.8 -> 5.5 (2026-09-03, by request: it hit the bottom far too soon). At real gravity that
        // is about 148m of drop against 38m, which is the right order for a building the exterior
        // measures at 164m tall - the car lets go near the top and falls most of it.
        public float fallSeconds = 5.5f;
        // Real gravity. The drop is the one moment in this game that is allowed to be violent, and
        // an eased one would read as the car being lowered.
        public float fallGravity = 9.81f;
        // How much harder it swings on the way down, as a multiple of `swayDegrees`.
        public float fallSwayGain = 2.5f;

        // **AND IT NO LONGER FALLS THROUGH THE BUILDING** (2026-09-03, by request).
        //
        // The drop used to be a timer and a `Vector3.up * vy` - nothing else - so the cabin passed
        // clean through every cell rack under it. What is below the release point is a lattice of
        // rooms the exterior builds (`SceneBuilder.BuildExterior`), and a cabin dropping 148m
        // through the middle of it should be hitting them.
        //
        // **THE OBSTACLES ARE DATA, NOT COLLIDERS**, and that is deliberate. Making the racks solid
        // would mean a layer of their own and a mask on every cast, because the player's own
        // controller sits inside the cabin and a sweep starting at the car begins overlapping them;
        // that is a lot of machinery, all of it able to go wrong quietly, for a fall that lasts five
        // seconds and happens once. `SceneBuilder` hands over the boxes in the fall corridor and the
        // resolution below is a page of arithmetic that cannot miss what it was given.
        //
        // It is also the same answer `FallingItem` reaches, for the same reason CLAUDE.md 4 gives:
        // a fall in this project is DERIVED, never simulated.
        public Bounds[] fallObstacles;
        // The cabin's own half extents for that test. Not the cage's - the cage has a metre of
        // collision skirt reaching back toward the room that has nothing to do with what the cabin
        // would strike.
        public Vector3 fallHalfExtents = new Vector3(1.2f, 2.1f, 1.06f);

        // **AND WHERE THAT BOX SITS, WHICH IS NOT ON THE PIVOT. THIS WAS THE PASS-THROUGH.**
        //
        // `BuildCableCar` recentres the model so the cabin's FLOOR is at the car's pivot - a vehicle
        // the player stands in wants its floor where the seat is. The cabin therefore occupies
        // `pivot` to `pivot + height`, and nothing of it is below the pivot at all.
        //
        // `Strike` was testing a box CENTRED on the pivot, which put it from `pivot - 2.1` to
        // `pivot + 2.1`: half of it in empty air under the car, and the top half of the cabin - the
        // half you are looking out of - outside the test entirely. On screen the cabin went through
        // a structure while the box that was supposed to stop it had already passed underneath.
        //
        // Play reported it three times as the car falling through rooms, and three fixes were aimed
        // at WHICH structures the car was told about (the corridor, the shaft, the clamp) while the
        // set was right the whole time and the box was in the wrong place. Every one of those fixes
        // measured something and every measurement agreed, because none of them was measuring this.
        //
        // Half the cabin's height, up. Authored by the build off the model that actually loaded.
        public float fallCentreOffset = 2.1f;
        // How much of the speed survives a bounce. Low: this is a steel box hitting concrete, not a
        // ball, and a lively one reads as comedy at the exact moment the game stops being funny.
        public float fallRestitution = 0.32f;
        // What a glancing blow throws sideways, as a share of the speed it arrived with. It is what
        // makes the second impact possible - a cabin that only ever bounced straight up would come
        // back down the hole it made.
        public float fallSideways = 0.45f;
        // **AND NO BOUNCE MAY EXCEED THIS, WHATEVER IT WAS DOING WHEN IT LANDED.** A share of the
        // arrival speed is the right shape and the wrong magnitude at the bottom of a two-hundred
        // metre drop: a third of 55m/s is 18m/s, which is a fifteen-metre hop and reads as a ball.
        // Capped, the cabin clips a structure and carries on down, which is what it is meant to look
        // like - and it is what bounds how much time the bouncing can add to the fall.
        public float fallBounceCap = 6f;
        // Where the drop ends. Authored from the bottom of the rack column, so the car lands on the
        // lowest thing in the world rather than at a time.
        public float fallGroundY = -200f;
        // A cap on the whole thing, in case the bouncing costs more height than gravity buys back.
        // Not the length of the fall - `fallGroundY` is.
        public float fallTimeout = 16f;
        // **THE DROP IS HELD INSIDE THE SHAFT THAT WAS CUT FOR IT.**
        //
        // Play (2026-09-03): the car struck the first structure and then never came near another,
        // and passed through rooms after that. Both are the same fault. `SlideOff` gives the cabin
        // the sideways speed it needs to leave a roof and NOTHING TOOK IT AWAY AGAIN - there is no
        // drag in a scripted fall - so after one hit the car drifted in a straight line for the rest
        // of the drop. It left the cleared shaft, and everything outside that shaft is rack that was
        // never handed over as an obstacle, so it went straight through it.
        //
        // Two numbers fix it and they are not interchangeable. The DAMPING is what makes the car
        // fall roughly where it was falling, so the obstacles below it are still in its way. The
        // CLAMP is the guarantee: the car physically cannot leave the radius the build cleared, so
        // the only structures it can ever reach are the ones it was told about. Without the clamp
        // the damping is a hope; without the damping the clamp is a wall the car slides down.
        // **BACK TO 0.7** (2026-09-03, by request: the falling motion and the impacts were better
        // before). It went to 1.6 chasing a pass-through that turned out to be the collision box
        // sitting 2.1m below the cabin - see `fallCentreOffset` - so the drag was never the fault
        // and killing the drift only made the drop stiffer.
        public float fallDrag = 0.7f;              // per second, exponential
        public Vector3 fallAxis;                   // the line it drops down, authored by the build
        public float fallShaftRadius = 10f;        // how far off that line it may ever get

        // No integration step may move the car further than this. At terminal speed a frame is over
        // a metre, and a cell is five - so without substeps a fast enough cabin steps over a rack
        // between two frames and the whole thing is decorative. Cheap: it is five seconds, once.
        public float fallMaxStep = 0.4f;

        public AudioClip impactClip;
        // What a bounce off a rack sounds like, as against the landing. Two, alternating with the
        // hit count rather than at random: they are seconds apart and a repeat would be heard.
        public AudioClip[] hitClips;
        // The rush under the whole drop, looped and faded up as it starts.
        public AudioSource fallSource;
        // **ITS OWN SOURCE, AND A LOUD ONE.** The landing is the last thing this game says and it
        // was arriving at the same level as a door. Kept off `audioSource` so raising it cannot
        // raise the doors and the departure with it.
        public AudioSource impactSource;

        // **THE HANGER COMPLAINING, ONCE PER SWING** (2026-09-03, by request: there should be a
        // warning before it lets go). Three clips, uneven, picked at random - the same reason the
        // footsteps and the splashes are three, which is that two identical creaks read as a sample
        // rather than as a joint.
        //
        // Fired at the ENDS of the swing, where a real one would be: the hanger is loaded hardest
        // where the cabin stops and turns round, and a creak in the middle of the arc would be a
        // sound effect on a timer. Loudness follows the amplitude, so the first ones are barely
        // there and the last ones are the loudest thing before the drop.
        public AudioClip[] creakClips;
        public float creakFrom = 0.45f;

        // Which half-swing last creaked, so each one fires once.
        private int lastCreakHalf = int.MinValue;

        // True once the car has let go. `EndingDeparture` waits on the ride and then on this.
        public bool Fell { get; private set; }
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
                Quaternion aimed = Quaternion.Slerp(docked, docked * Quaternion.Euler(0f, travelYaw, 0f),
                                                    turn);
                float swayK = Mathf.InverseLerp(swayFrom, 1f, Progress);
                car.rotation = Sway(aimed, t, swayK);
                Creak(t, swayK);
                if (motor != null) motor.volume = Mathf.Clamp01(raw * 8f) * (1f - Mathf.Clamp01((raw - 0.9f) * 10f));
                CarryPlayer();
                yield return null;

                // **AND IT LETS GO BEFORE THE TOP.** Everything above this is the ride working; the
                // loop simply stops running it. `Progress` is left where it stopped rather than
                // being driven to 1, because it is what `EndingDeparture` paces the narration off
                // and the last thing that should happen is a line firing at a car that is falling.
                if (Progress >= fallAt)
                {
                    yield return Fall(car.rotation, t);
                    yield break;
                }
            }

            Progress = 1f;
            car.position = PathPoint(1f);
            CarryPlayer();
        }

        // A ROLL ABOUT THE DIRECTION OF TRAVEL, which is the only thing a cabin hung from one point
        // above its centre of mass can do freely.
        //
        // The axis is MEASURED off the path rather than taken from the model - the doors already
        // cost a day to the assumption that a local axis means what its name says, and the car is
        // rotated inside its own root. Two points on the path an instant apart is the travel
        // direction whatever the model thinks.
        //
        // Amplitude grows with `k` squared: linear growth reads as a fader being pushed, the same
        // trap `pull_in`'s riser and `LoopManager`'s collapse ramp both document.
        private Quaternion Sway(Quaternion aimed, float t, float k)
        {
            k = Mathf.Clamp01(k);
            if (k <= 0f) return aimed;

            Vector3 ahead = PathPoint(Mathf.Min(1f, Progress + 0.01f));
            Vector3 along = ahead - car.position;
            if (along.sqrMagnitude < 0.0001f) along = car.forward;

            float roll = Mathf.Sin(t * swayRate * Mathf.PI * 2f) * swayDegrees * k * k;
            return Quaternion.AngleAxis(roll, along.normalized) * aimed;
        }

        // One creak per half-swing, at the turn rather than in the middle of the arc.
        //
        // `sin` peaks at the quarter and three-quarter points, so the half-swing index advances at
        // exactly those moments - which is where a loaded hanger actually protests. Tracking the
        // index rather than testing the value is what keeps it to one creak per turn however the
        // frame rate falls.
        private void Creak(float t, float k)
        {
            if (creakClips == null || creakClips.Length == 0 || audioSource == null) return;
            if (k < Mathf.InverseLerp(swayFrom, 1f, creakFrom)) return;

            int half = Mathf.FloorToInt(t * swayRate * 2f + 0.5f);
            if (half == lastCreakHalf) return;
            lastCreakHalf = half;

            AudioClip clip = creakClips[Random.Range(0, creakClips.Length)];
            if (clip != null) audioSource.PlayOneShot(clip, Mathf.Clamp01(0.25f + k * 0.75f));
        }

        // **THE DROP.** Real gravity, no easing: this is the one moment in the game allowed to be
        // violent, and an eased fall reads as the car being lowered rather than released.
        //
        // The sway keeps running and gets worse, off the same clock, so the swing that was building
        // through the climb is visibly what came loose rather than a separate animation starting.
        // `CarryPlayer` is called exactly as it is during the ride, so the player goes down inside
        // the cabin without anything special being done to them - the cage is already around them.
        private IEnumerator Fall(Quaternion held, float t)
        {
            Fell = true;
            if (motor != null) motor.volume = 0f;

            // The rush, up from nothing over the first half second. Started here rather than looped
            // from the beginning of the ride because it is the sound of falling, and until this
            // moment the car was not.
            if (fallSource != null && fallSource.clip != null)
            {
                fallSource.volume = 0f;
                fallSource.loop = true;
                fallSource.Play();
            }

            Vector3 along = car.up;                 // the rope's direction at the moment it let go
            Vector3 velocity = Vector3.zero;        // three axes now: a bounce throws it sideways
            float elapsed = 0f;
            int hits = 0;
            float drift = 0f;                       // the most it ever got off the line
            // The swing keeps building on the clock it built on during the climb, and a strike adds
            // to it - so a cabin that has hit something is visibly more out of control than one that
            // has not, without a second animation saying so.
            float shake = 0f;

            while (elapsed < fallTimeout && car.position.y > fallGroundY)
            {
                float frame = EndingClock.Delta;

                // **SUBSTEPPED, AND THAT IS WHAT MAKES THE OBSTACLES REAL.** See `fallMaxStep`.
                int steps = Mathf.Clamp(
                    Mathf.CeilToInt(velocity.magnitude * frame / Mathf.Max(0.01f, fallMaxStep)),
                    1, 40);
                float dt = frame / steps;

                for (int i = 0; i < steps; i++)
                {
                    velocity.y -= fallGravity * dt;

                    // Sideways speed bleeds off; downward speed does not. A falling body does not
                    // keep travelling horizontally for ten seconds because it clipped something
                    // once, and a scripted fall has nothing else to take it away.
                    float keep = Mathf.Exp(-fallDrag * dt);
                    velocity.x *= keep;
                    velocity.z *= keep;

                    car.position += velocity * dt;
                    HoldInShaft(ref velocity);
                    drift = Mathf.Max(drift, new Vector2(car.position.x - fallAxis.x,
                                                         car.position.z - fallAxis.z).magnitude);

                    if (car.position.y <= fallGroundY) break;
                    if (Strike(ref velocity)) { hits++; shake = 1f; Bang(hits); }
                }

                elapsed += frame;
                t += frame;
                shake = Mathf.Max(0f, shake - frame * 0.55f);

                if (fallSource != null)
                    fallSource.volume = Mathf.MoveTowards(fallSource.volume, 1f, frame / 0.5f);

                float roll = Mathf.Sin(t * swayRate * Mathf.PI * 2f)
                           * swayDegrees * fallSwayGain
                           * Mathf.Clamp01(elapsed / fallSeconds) * (1f + shake);
                car.rotation = Quaternion.AngleAxis(roll, along) * held;

                CarryPlayer();
                yield return null;
            }

            // **ONLY IF IT GOT THERE.** `fallTimeout` is a guard against a bounce pattern that
            // costs more height than gravity buys back, and snapping the car down on that path
            // would teleport it - a jump cut at the one moment the ending needs to be continuous.
            // It lands where it is instead, and the bang plays either way: the fall ends with an
            // impact whichever of the two conditions stopped it.
            if (car.position.y <= fallGroundY)
                car.position = new Vector3(car.position.x, fallGroundY, car.position.z);
            CarryPlayer();

            if (fallSource != null) fallSource.Stop();
            // The landing, on its own source at its own level - see `impactSource`.
            AudioSource landing = impactSource != null ? impactSource : audioSource;
            if (landing != null && impactClip != null) landing.PlayOneShot(impactClip);

            Vector2 endOff = new Vector2(car.position.x - fallAxis.x, car.position.z - fallAxis.z);
            int corridor = fallObstacles != null ? fallObstacles.Length : 0;
            Debug.Log($"[CableCarRide] The car fell {elapsed:0.0}s to y={fallGroundY:0.#} and struck "
                    + $"{hits} structure(s) on the way, out of {corridor} in the corridor. It "
                    + $"drifted {drift:0.0}m off the line at most ({fallShaftRadius:0.#}m allowed) "
                    + $"and finished {endOff.magnitude:0.0}m off it.");
        }

        // **ONE BOX, RESOLVED ON ITS SHALLOWEST AXIS.**
        //
        // The cabin is an axis-aligned box for this and so is every rack cell, so an overlap is
        // three penetration depths and the answer is the smallest of them: push out that way and
        // reflect that component. It is the standard resolution, and it is the right one here
        // because it cannot produce a wrong ANSWER, only an ugly one - whatever it does, the car
        // ends the step outside the box, which is the whole of what was asked for.
        //
        // The sideways kick is taken from the axes that were NOT resolved, so a square landing on a
        // roof throws the car very little and a clip off a corner throws it a long way. Derived
        // rather than random: this fall is watched once and has to be the same fall every time,
        // which is the argument `FallingItem` already makes for every other drop in the game.
        private bool Strike(ref Vector3 velocity)
        {
            if (fallObstacles == null || fallObstacles.Length == 0) return false;

            // THE CABIN'S MIDDLE, not the car's pivot - see `fallCentreOffset`.
            Vector3 at = car.position + Vector3.up * fallCentreOffset;
            for (int i = 0; i < fallObstacles.Length; i++)
            {
                Bounds b = fallObstacles[i];
                Vector3 d = at - b.center;

                // Y FIRST, and it is not a style choice. The corridor is a column hundreds of metres
                // tall and the cabin is inside one storey of it at a time, so this one compare
                // rejects all but a handful - and it runs on every substep of every frame of the
                // fall. The full test is three abs and three subtracts; this is one.
                float oy = b.extents.y + fallHalfExtents.y - Mathf.Abs(d.y);
                if (oy <= 0f) continue;

                Vector3 overlap = new Vector3(
                    b.extents.x + fallHalfExtents.x - Mathf.Abs(d.x), oy,
                    b.extents.z + fallHalfExtents.z - Mathf.Abs(d.z));
                if (overlap.x <= 0f || overlap.z <= 0f) continue;

                // The shallowest axis is the face it came in through.
                int axis = overlap.x < overlap.y ? (overlap.x < overlap.z ? 0 : 2)
                                                 : (overlap.y < overlap.z ? 1 : 2);
                float sign = d[axis] >= 0f ? 1f : -1f;

                at[axis] = b.center[axis] + sign * (b.extents[axis] + fallHalfExtents[axis]);
                car.position = at - Vector3.up * fallCentreOffset;

                // Only if it was actually moving INTO that face. A box already resting against one
                // must not be handed its own speed back every step.
                if (velocity[axis] * sign < 0f)
                {
                    float speed = Mathf.Abs(velocity[axis]);
                    velocity[axis] = Mathf.Min(speed * fallRestitution, fallBounceCap) * sign;

                    if (axis == 1 && sign > 0f) SlideOff(b, d, ref velocity);
                    else
                        // A side or underside strike deflects on the axes it did not resolve, away
                        // from the box's centre. Nothing has to be guaranteed here: a car that hit a
                        // wall is already travelling past it.
                        for (int k = 0; k < 3; k++)
                        {
                            if (k == axis) continue;
                            float off = b.extents[k] > 0.01f
                                ? Mathf.Clamp(d[k] / b.extents[k], -1f, 1f) : 0f;
                            if (Mathf.Abs(off) < 0.1f) off = k == 0 ? 0.1f : -0.1f;
                            velocity[k] += off * speed * fallSideways;
                        }
                }
                return true;
            }
            return false;
        }

        // **A ROOF IS THE ONE HIT THAT CAN END THE FALL, SO IT IS THE ONE THAT IS GUARANTEED.**
        //
        // Every other face deflects a car that is already going past. Land square on top of a cell
        // and nothing does: the bounce is capped, the sideways kick off a centred hit is almost
        // nothing, and the cabin settles onto the roof and stays there until `fallTimeout`. That is a
        // six-metre drop with eighteen seconds of nothing after it.
        //
        // So a roof strike is given exactly the sideways speed that clears the roof within the hop it
        // just made - distance to the nearer edge over the airtime the bounce buys. The car leaves
        // every roof it touches, by arithmetic rather than by hoping the offset was big enough, and
        // it leaves by the SHORTER way off, so a clip near an edge barely changes its line while a
        // square landing is visibly shouldered aside.
        private void SlideOff(Bounds b, Vector3 d, ref Vector3 velocity)
        {
            float airtime = Mathf.Max(0.3f, 2f * velocity.y / Mathf.Max(0.01f, fallGravity));

            // The horizontal axis it is nearest to being off already.
            int k = (b.extents.x + fallHalfExtents.x - Mathf.Abs(d.x))
                  < (b.extents.z + fallHalfExtents.z - Mathf.Abs(d.z)) ? 0 : 2;
            // Half a metre past the edge, so it is clear rather than balanced on it.
            float need = b.extents[k] + fallHalfExtents[k] - Mathf.Abs(d[k]) + 0.5f;
            float away = d[k] >= 0f ? 1f : -1f;

            velocity[k] = away * Mathf.Min(need / airtime, 14f);
        }

        // **THE GUARANTEE BEHIND THE WHOLE COLLISION SET.** See `fallShaftRadius`.
        //
        // Everything the cabin is allowed to meet was chosen at build time from inside a cylinder
        // round the fall line. If the car can leave that cylinder, the set stops being a description
        // of what is in its way and becomes a description of what USED to be, which is how a cabin
        // ends up passing through a rack it was never told about.
        //
        // So the cylinder is enforced rather than assumed: pushed back to its wall, with the outward
        // component of the speed removed so it slides along rather than pressing into it.
        private void HoldInShaft(ref Vector3 velocity)
        {
            if (fallShaftRadius <= 0f) return;

            Vector3 at = car.position;
            Vector2 off = new Vector2(at.x - fallAxis.x, at.z - fallAxis.z);
            if (off.sqrMagnitude <= fallShaftRadius * fallShaftRadius) return;

            Vector2 out2 = off.normalized;
            Vector2 held = out2 * fallShaftRadius;
            car.position = new Vector3(fallAxis.x + held.x, at.y, fallAxis.z + held.y);

            float outward = velocity.x * out2.x + velocity.z * out2.y;
            if (outward <= 0f) return;
            velocity.x -= out2.x * outward;
            velocity.z -= out2.y * outward;
        }

        // A strike, as against the landing. Alternated rather than randomised: they are seconds
        // apart, so a repeat would be heard as a repeat.
        private void Bang(int hits)
        {
            if (hitClips == null || hitClips.Length == 0) return;
            AudioClip clip = hitClips[(hits - 1) % hitClips.Length];
            AudioSource on = impactSource != null ? impactSource : audioSource;
            if (on != null && clip != null) on.PlayOneShot(clip, 0.8f);
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
