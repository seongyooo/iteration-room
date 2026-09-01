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

        // The wall of room3-2N that comes apart to let the car in. A `CycleExit` - the same
        // component the floor hatches are, for the same reason: it opens once, is used once, and it
        // lands on the panel grid so it reads as the wall itself coming apart rather than as a door.
        // See `CycleExit`'s header, which makes that argument for the hatch and it holds here.
        public CycleExit breach;

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
            // And the way back closes over their head while they take it - see `shaftLid`. Started
            // rather than waited on: it is something to notice, not something to wait for.
            StartCoroutine(SealTheShaft());
            yield return Wait(1.6f);

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
            cameraShaker?.SetIntensity(1f);
            breach?.Open();
            yield return Wait(3.2f);

            // 4. THE OUTSIDE, revealed behind the opening wall. Every cycle woken, every cutaway
            //    wall taken off, every past self stood back up. One frame's work, hidden behind a
            //    wall panel in motion - see `FacilityExterior.Reveal`.
            if (exterior != null)
            {
                exterior.cycles = cycles;
                // The cycle the player is standing in keeps its ceilings - see
                // `FacilityExterior.occupied`. Handed over by `CycleBinding` rather than found from
                // here: the `Cycle` component is a SIBLING of the world root this object lives
                // under, not a parent of it, so there is nothing above to walk up to.
                exterior.occupied = occupied;
                exterior.Reveal();
            }
            cameraShaker?.SetIntensity(0.25f);

            // 5. THE CAR, in through the hole it made.
            if (car != null) yield return car.Arrive();

            // 6. BOARDING AND THE CLIMB, as one step - the car leaves the moment somebody is
            //    aboard. See `CableCarRide.BoardAndDepart`. The building is already lit - see `FacilityExterior.LightTheOutside`,
            //    which turns everything on at the reveal rather than a storey at a time as the car
            //    passes. Lighting it progressively was a pacing idea that cost the first half of the
            //    ride: the half of the building not yet reached was simply black.
            if (car != null) yield return car.BoardAndDepart();

            // And it stops. `LoopManager` brings the scrim up from here, over a player sitting in a
            // cable car above the facility - which is the shot the whole ending is built to reach.
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
