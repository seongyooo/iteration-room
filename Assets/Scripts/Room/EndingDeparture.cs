using System.Collections;
using UnityEngine;

namespace IterationRoom
{
    // THE LAST FIVE MINUTES, IN ORDER. Owns nothing but the order.
    //
    // Everything it plays belongs to somebody else - the grade is `RunEvaluation`'s, the board is
    // `EvaluationBoard`'s, the vehicle is `CableCarRide`'s, the view is `FacilityExterior`'s - and
    // that split is the point. This file is where a reader finds out what happens after cycle 3
    // breaks, and it should be readable as a list.
    //
    // WHERE IT SITS IN THE GAME. `LoopManager.RunEnding` used to go: let the collapse go, play the
    // break, take control at the scrim, roll the card. The break is unchanged and the card is
    // unchanged; this goes in between them, and `RunEnding` yields on it. Everything here therefore
    // runs with `RunOver` set, the clock stopped, every fixture dead and the player still walking
    // around - which is exactly the state the break already leaves them in, deliberately: *"control
    // is not taken, and it must not be"*.
    //
    // THE PLAYER IS NEVER HURRIED. There is no timer anywhere in this sequence. They climb down when
    // they climb down, read the board or do not, and board the car when they board it. The only
    // thing that moves on its own is the car, once they are inside it.
    public class EndingDeparture : MonoBehaviour
    {
        public EvaluationBoard board;
        public CableCarRide car;
        public FacilityExterior exterior;

        // The pause between one ride line finishing and the next starting, on top of the 0.75s of
        // tail silence every clip already carries (`generate_narration.TAIL_SILENCE`).
        //
        // A short pause between the thirteen ride lines. The car now waits at its release point for
        // the final line to finish, so this value controls cadence rather than correctness.
        public float betweenRideLines = 0.45f;

        private Camera[] exteriorViews;
        private bool[] exteriorViewOcclusion;

        // The wall of room3-2N that comes apart to let the car in. A `CycleExit` - the same
        // component the floor hatches are, for the same reason: it opens once, is used once, and it
        // lands on the panel grid so it reads as the wall itself coming apart rather than as a door.
        // See `CycleExit`'s header, which makes that argument for the hatch and it holds here.
        public CycleExit breach;

        // The invisible wall across the breach, up from the moment the wall opens until the car has
        // finished docking - see `SceneBuilder.BuildBreachBlockers`. Without it the apron under the
        // opening is a floor leading out of a hole with no vehicle at the end of it, and play walked
        // out and was standing on the car's roof when it arrived.
        public GameObject breachGate;

        // **THE HOLE THE PLAYER CAME DOWN THROUGH, SHUT BEHIND THEM** (2026-09-01, by request).
        //
        // Room3-0 sits on top of room3-2N and the only way between them is the ladder shaft. The
        // ladder went with the rest of the structures, so the drop is one-way already - but a hole
        // standing open in the ceiling says otherwise, and the whole of this sequence is the building
        // closing itself off one route at a time.
        //
        // It rises INTO the hole from the service void underneath, so it is out of sight until it
        // moves and the player watches the ceiling seal from directly below it.
        public Transform shaftLid;
        public float shaftLidRise = 1.9f;
        public float shaftLidSeconds = 2.6f;

        public CameraShaker cameraShaker;


        // The break, held at the point where it would start emptying room3-2N - see
        // `FacilityFailure.held`. Released the moment the player is standing in that room.
        public FacilityFailure failure;
        // The PA, for one chime as the walls start printing - see `NarrationDirector.Attention`.
        public NarrationDirector narration;

        // How long after the board finishes before the wall opens. A beat: the verdict is the last
        // thing the facility says about the player, and something arriving on top of it would step
        // on the only line in the game that is about them.
        public float afterVerdict = 2.4f;

        // The longest this will wait for a player who is in the room and has not gone near the board.
        // Only reached by somebody deliberately standing still - a board that CANNOT finish is given
        // up on in a second, not in this.
        public float boardPatience = 150f;

        // **NOTHING HAPPENS UNTIL THE PLAYER HAS COME DOWN, AND EVERY BEAT BELOW DEPENDS ON IT.**
        //
        // The break happens in room3-0, a storey up. The report is on room3-2N's walls, the breach is
        // in room3-2N's wall and the car arrives at room3-2N's floor - all of it out of sight from
        // where the player is standing when it starts. Without this wait the whole ending played to
        // an empty room: play came down the ladder to find the ceilings already gone and the cable
        // car parked and waiting, which is every surprise in the sequence spent on nobody.
        //
        // The test is a height, and it is the plainest one available: room3-2N's floor is a storey
        // and a half below room3-0's, so "the player is under this line" cannot be true anywhere
        // except down there. `SceneBuilder` authors it from the room it built.
        public Transform player;
        public float descentY = -12f;

        // This departure's own cycle. Bound at runtime - see the note where it is used.
        [System.NonSerialized] public Cycle occupied;
        // Given up on eventually, so a player who somehow never descends is not stuck in a game with
        // no way out. Long, because there is no reason to hurry them - the climb down is theirs.
        public float descentPatience = 300f;

        public IEnumerator Play(Cycle[] cycles)
        {
            // 1. THE CLIMB DOWN, which is the player's and has no timer on it. See `descentY`.
            float waited = 0f;
            while (player != null && player.position.y > descentY && waited < descentPatience)
            {
                waited += EndingClock.Delta;
                yield return null;
            }

            // A beat once they are down, before the walls start talking. They have just dropped a
            // storey and a half through a hole in the dark; the room gets a moment to be a room.
            //
            // **AND NOW THE ROOM COMES APART, WITH SOMEBODY IN IT.** The break has been holding at
            // this point since the console filled - see `FacilityFailure.held`. Everything it does
            // from here is in this room: the cube into the floor, the decks, the lift, the corridor
            // sealing behind them.
            failure?.ReleaseStructures();

            // And the way back closes over their head while they take it - see `shaftLid`. Started
            // rather than waited on: it is something to notice, not something to wait for.
            StartCoroutine(SealTheShaft());

            // Long enough for the teardown to be most of the way through before the walls start
            // printing. Two things competing for the player's attention is neither of them.
            yield return Wait(6.5f);

            // 2. THE REPORT, on every wall of the room they have just landed in.
            if (board != null)
            {
                // The building says it is about to speak, and then the walls speak. Ahead of the
                // first line rather than under it, so the player has a moment to turn round.
                narration?.Attention();
                yield return Wait(1.1f);

                board.Begin();
                // **THE WAIT IS BOUNDED TWICE, AND THE SECOND BOUND IS THE ONE THAT MATTERS.**
                //
                // It cannot be unconditional: the player may never walk up to the board, and hanging
                // the end of the game on a wall panel somebody chose not to read is not a thing to
                // do. So there is a patience.
                //
                // But a patience alone is a bug waiting behind a timer, and it duly happened -
                // `FacilityFailure.board` was left unwired for one build, the readout could never
                // start, and this sat here for the full four minutes before opening the wall. The
                // report was "the cable car wall does not open", which is four minutes and one room
                // away from the missing line.
                //
                // So the wait now asks whether the board CAN still finish. A board that has not begun
                // and is not powered is one that never will, and this stops waiting for it at once -
                // the ending goes on without a grade rather than stalling on one.
                float patience = 0f;
                while (!board.Finished && patience < boardPatience)
                {
                    if (!board.Printing && patience > 1f && !board.Powered)
                    {
                        Debug.LogWarning("[EndingDeparture] The evaluation board never powered on - "
                                       + "`FacilityFailure.board` is not wired. Going on without it.");
                        break;
                    }
                    patience += EndingClock.Delta;
                    yield return null;
                }
                yield return Wait(afterVerdict);

                // **AND THEN IT TELLS THEM TO WAIT.** The report ends and nothing happens for a few
                // seconds while the wall shakes and the car climbs the shaft, and a player alone in a
                // wrecked room with no instruction reads that as being stuck. One line fixes it, and
                // it is the only thing the facility says at the end that is not a number.
                narration?.AnnounceTransportCalled();
                yield return Wait(4.2f);
            }

            // 3. THE WALL. Shaken first, then opened - the same order of cause the break upstairs
            //    uses, where the room warns before anything moves.
            // **NOTHING IS ANNOUNCED HERE, AND THAT IS A CHOICE.** `FacilityFailure` has already
            // played `AnnounceAllCyclesBroken` at the break - *"All cycles have been destroyed. You
            // will pay the price for destroying them."* - which is the only line in the game that
            // addresses the player, and it is the last thing this voice should ever say. A second
            // announcement here would need a clip that does not exist AND would talk over the one
            // that does. The wall opens in silence except for itself.
            // 3a. THE OUTSIDE, BUILT BEFORE THE WALL IS OPENED ON IT (2026-09-03, by request).
            //
            //     This used to run 3.2 seconds AFTER `breach.Open()`, on the reasoning that one
            //     frame of work is best hidden behind a wall panel in motion. What that actually
            //     bought was three seconds of an opening wall with NOTHING BEHIND IT - the player
            //     looking straight out of the map through a widening gap. The pop-in it was hiding
            //     is one frame; the hole it left was a hundred and ninety.
            //
            //     Every cycle woken, every cutaway wall taken off, every past self stood back up -
            //     all of it now finished before the first panel moves.
            if (exterior != null)
            {
                DisableOcclusionForExteriorView();
                exterior.cycles = cycles;
                // The cycle the player is standing in keeps its ceilings - see
                // `FacilityExterior.occupied`. Handed over by `CycleBinding` rather than found from
                // here: the `Cycle` component is a SIBLING of the world root this object lives
                // under, not a parent of it, so there is nothing above to walk up to.
                exterior.occupied = occupied;
                exterior.Reveal();
            }

            cameraShaker?.SetIntensity(1f);
            breach?.Open();
            yield return Wait(3.2f);
            cameraShaker?.SetIntensity(0.25f);

            // 5. THE CAR, in through the hole it made.
            if (car != null) yield return car.Arrive();

            // Docked. The way out is a way out now.
            if (breachGate != null) breachGate.SetActive(false);

            // 6. BOARDING AND THE CLIMB, as one step - the car leaves the moment somebody is
            //    aboard. See `CableCarRide.BoardAndDepart`. The PA talks the whole way up. The building is already lit - see `FacilityExterior.LightTheOutside`,
            //    which turns everything on at the reveal rather than a storey at a time as the car
            //    passes. Lighting it progressively was a pacing idea that cost the first half of the
            //    ride: the half of the building not yet reached was simply black.
            if (car != null)
            {
                yield return car.WaitForBoarding();

                // Aboard. Cycle 3 loses its lids too now - see
                // `FacilityExterior.CutAwayTheLastCycle` for why it could not before.
                exterior?.CutAwayTheLastCycle();

                car.holdReleaseForNarration = true;
                Coroutine talking = StartCoroutine(NarrateTheRide());
                yield return car.ShutTheDoors();
                yield return car.Ride();
                car.holdReleaseForNarration = false;
                StopCoroutine(talking);
            }

            RestoreOcclusionAfterExteriorView();

            // 7. **AND IT DOES NOT REACH THE SURFACE.** `Ride` returns having dropped the car.
            //    What follows - the blink, room1-1, the counter back at 1 - is `LoopManager`'s, not
            //    this object's: it is an ITERATION, and this class owns the order of the ending and
            //    nothing else (CLAUDE.md 2). Doing it here is what made the first attempt a diorama:
            //    the player stood in the right room with `AcceptsInput` false, so nothing could be
            //    picked up, no announcement fired, and the wake-up powered cycle 3's panels because
            //    that was still the cycle the loop thought it was in.

            // And it stops. `LoopManager` brings the scrim up from here, over a player sitting in a
            // cable car above the facility - which is the shot the whole ending is built to reach.
        }

        // **THE RIDE IS A MINUTE LONG AND IT WAS SILENT.**
        //
        // Five lines spread across the climb, keyed off where the car actually is rather than off a
        // clock - so they stay where they were put if the speed or the path ever changes, and the
        // last one does not land after the ride has finished.
        //
        // Nothing waits on them: a line that overran would otherwise hold up the one below it and
        // the set would drift late. `NarrationDirector.Speak` cancels whatever is talking, so a
        // late line is cut rather than queued, which is the right way round here - the car is
        // somewhere specific and the line is about being there.
        private IEnumerator NarrateTheRide()
        {
            if (narration == null || car == null)
            {
                if (car != null) car.holdReleaseForNarration = false;
                yield break;
            }

            // **BACK TO BACK, NOT AT MARKS** (2026-09-03, by request: the PA should not stop
            // talking for the whole climb).
            //
            // It used to fire five lines at five points along the path. Keying off position rather
            // than a clock was right when there were five - they stayed where they were put if the
            // speed ever changed - but it left about twenty seconds of speech in a sixty second
            // ride, and the silences read as the facility having run out of things to say.
            //
            // Fourteen lines is roughly forty-five seconds, so they are simply queued: the first
            // waits for the doors to be shut and the car moving, and every one after it waits for
            // the line before it to FINISH. That is what makes it one address instead of fourteen
            // announcements - and it is why `Speaking` had to become a fact this could ask
            // (see `NarrationDirector.Speaking`), because a fixed gap would either cut a long line
            // off or leave a hole after a short one.
            //
            // Nothing waits forever: the ride ending stops this coroutine wherever it has got to.
            while (car.Progress < 0.03f) yield return null;

            for (int i = 0; i < narration.RideLineCount; i++)
            {
                narration.AnnounceRideLine(i);
                // A beat to let the source actually start - `Speaking` is false for a frame or two
                // after `Play()`, the same reason `PaSubtitle` carries a minimum.
                yield return Wait(0.35f);
                while (narration.Speaking) yield return null;
                yield return Wait(betweenRideLines);
            }

            car.holdReleaseForNarration = false;
        }

        // The baked data describes closed rooms viewed from their interiors. During the departure
        // those same scenes are woken additively, their cutaway walls are hidden, and the camera
        // travels outside the baked view cells. Umbra can then keep treating a removed wall as an
        // occluder: looking straight into a room hides it while an oblique angle happens to select a
        // different visibility cell and draws it. Static occlusion is valuable for normal play, so
        // disable it only for the exterior shot and put every player camera back afterwards.
        private void DisableOcclusionForExteriorView()
        {
            exteriorViews = player != null
                ? player.GetComponentsInChildren<Camera>(true)
                : System.Array.Empty<Camera>();
            exteriorViewOcclusion = new bool[exteriorViews.Length];

            for (int i = 0; i < exteriorViews.Length; i++)
            {
                Camera view = exteriorViews[i];
                if (view == null) continue;
                exteriorViewOcclusion[i] = view.useOcclusionCulling;
                view.useOcclusionCulling = false;
            }
        }

        private void RestoreOcclusionAfterExteriorView()
        {
            if (exteriorViews == null || exteriorViewOcclusion == null) return;

            int count = Mathf.Min(exteriorViews.Length, exteriorViewOcclusion.Length);
            for (int i = 0; i < count; i++)
                if (exteriorViews[i] != null)
                    exteriorViews[i].useOcclusionCulling = exteriorViewOcclusion[i];

            exteriorViews = null;
            exteriorViewOcclusion = null;
        }

        // The lid, up into the ceiling hole. On the ending's own clock like everything else here.
        private IEnumerator SealTheShaft()
        {
            if (shaftLid == null) yield break;

            // Drawn from here on. It is built with its renderer off because where it waits turns out
            // to be visible from below - see the note in `BuildShaftLid`.
            Renderer lid = shaftLid.GetComponent<Renderer>();
            if (lid != null) lid.enabled = true;

            Vector3 from = shaftLid.localPosition;
            Vector3 to = from + Vector3.up * shaftLidRise;

            float t = 0f;
            while (t < shaftLidSeconds)
            {
                t += EndingClock.Delta;
                shaftLid.localPosition =
                    Vector3.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t / shaftLidSeconds));
                yield return null;
            }
            shaftLid.localPosition = to;
        }

        private static IEnumerator Wait(float seconds)
        {
            float t = 0f;
            while (t < seconds)
            {
                t += EndingClock.Delta;
                yield return null;
            }
        }
    }
}
