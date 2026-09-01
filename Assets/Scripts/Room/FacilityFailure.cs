using System.Collections;
using UnityEngine;

namespace IterationRoom
{
    // WHAT CYCLE 3 DOES WHEN ITS CONSOLE FILLS: the room takes itself apart and the facility says
    // what the player has done.
    //
    // **THIS IS A BREAK, NOT A PUZZLE'S REWARD** (moved 2026-08-30). It ran off `BedlamCube` for a
    // day and halted the loop outright, which was the honest shape of a cycle with nowhere to go.
    // Cycle 3 has a `-0` now, so this is what `FinalRoomSequence.BreakOpen` plays instead of the
    // announcement and the panel glitch every other cycle gets - and it stops the loop by the
    // ordinary route rather than by a flag: `LoopManager.CrossToNextCycle` has already set
    // `CycleBreaking`, so the clock is stopped and every fixture is dead before this runs.
    //
    // **CONTROL IS NOT TAKEN, and it must not be.** That boundary deliberately has no timer on it -
    // "walking out through what you built is the whole of this beat" - and the hatch is open the
    // entire time this plays. The player watches the room leave and takes the way down when they
    // choose.
    //
    // THE ORDER IS THE ORDER OF CAUSE, and every beat waits for the one before it:
    //
    //   1. the room shakes        the same shaker the loop's own collapse uses
    //   2. the cube goes down     shaking, into the floor it was built on
    //   3. the structures go      some sideways, some through the floor, on staggered starts
    //   4. the announcement       and the panels go red, and the siren starts
    //
    // Driven on UNSCALED time throughout, like every other sequence that outlives the loop: the
    // pause menu must not be able to freeze a room half torn down.
    public class FacilityFailure : MonoBehaviour
    {
        // ONE THING THAT LEAVES, and which way. Built by `SceneBuilder`, which is the only thing that
        // knows what it put in this cycle - see `BuildFacilityFailure`.
        [System.Serializable]
        public struct Mover
        {
            public Transform what;
            // In the cycle's own frame. `Vector3.down` is "through the floor"; anything horizontal
            // is "off to the side". Not normalised here: the distance is its magnitude, so one
            // vector says both where and how far.
            public Vector3 travel;
            // Seconds after the teardown starts. Staggered so the room comes apart in a wave rather
            // than every piece of it moving on one frame, which reads as a scene being switched off.
            public float delay;
            public float duration;
        }

        public CameraShaker cameraShaker;
        public NarrationDirector narration;
        public WallPanelDisplay wallPanels;

        // The finished cube, which is the one thing here that is not scenery: the player built it,
        // and it goes down first and on its own so that they watch it go.
        public Transform cube;
        public float cubeShake = 1.1f;
        public float cubeSink = 2.4f;
        public float cubeSinkSeconds = 2.2f;
        // How far the cube jitters while it sinks. Small - it is a metre and a half of solid block
        // going into a floor, not a thing being rattled.
        public float cubeShakeAmplitude = 0.035f;

        public Mover[] movers;

        // How long the shake takes to come up before anything moves. The room warns first; a floor
        // that opened on the same frame as the last block landed would read as a trigger rather than
        // as a consequence.
        public float shakeLeadIn = 1.4f;
        // And what it settles to once the room is empty. Not zero: the alarm is still going, and a
        // building that stops shaking the moment it is finished has stopped being a building in
        // trouble.
        public float restingShake = 0.25f;

        // **WHAT TELLS THE PLAYER WHERE THE WAY OUT IS.** They are standing in room3-0 and the floor
        // that opens is in room3-2N, a storey below and out of sight - without this the break ends
        // with a player in a red room with nothing to do and no reason to climb back down.
        //
        // Revealed rather than always up: before the break there is no way down to point at, and a
        // sign that was there all along would be pointing at a floor.
        // **ON EVERY WALL OF ROOM3-0, NOT ONE** (2026-09-01, by request). The player is somewhere
        // in a room with no windows and a hole in the floor they cannot see from most of it, and
        // which way they happen to be facing when the break ends is not something this sequence
        // gets to choose. One sign is a sign half of them have their back to.
        public CanvasGroup[] wayDownSigns;

        // **THE WAY THE PLAYER CAME IN, SHUT AS PART OF THE TEARDOWN** (2026-09-01, by request).
        //
        // Room3-2N is entered through a barrier that a past self holds open by standing on a pad.
        // The past selves are gone by now and the loop is stopped, so nothing was driving it and it
        // simply stayed up - a room coming apart with a door still politely open behind you.
        //
        // It is the same slab `Cycle.barriers` snaps at a loop boundary; here it is asked to close
        // on screen, with the rest of the structures leaving, because it is one of them.
        public CrushingBarrier wayIn;

        // The evaluation board on room3-2N's wall. Powered on at the break; everything else about it
        // is its own - see `EvaluationBoard`.
        public EvaluationBoard board;

        // ================================================ WHAT ROOM3-0 LOOKS LIKE AFTERWARDS
        //
        // **A RED ROOM WITH ONE ARROW IN IT** (2026-08-31, by request). Before this the break left
        // room3-0 lit exactly as it had been - four white ceiling spots - with red wall panels under
        // them, which reads as a room with a warning light in it rather than as a room in trouble.
        //
        // The white light goes out and a single red one comes up. Nothing else in here changes: the
        // panels were already going red (`BeginAlarmGlitch`) and the only sign in the room is the
        // arrow, so what is left is exactly what was asked for.
        public Light[] roomLights;
        // The emissive faces of those same fixtures, which are what the eye actually reads as "the
        // lights are on" - killing the `Light` alone leaves four bright white squares in the ceiling
        // of a dark red room.
        //
        // **DRIVEN THROUGH A `MaterialPropertyBlock`, NEVER THE MATERIAL.** `CeilingFixtureCycle3` is
        // ONE material shared by every room in the cycle, so writing to it here would put out the
        // lights in room3-2N as well - which is the room the player is about to climb down into.
        // `ChessReward` dims cycle 1's fixtures the same way for the same reason.
        public Renderer[] roomFixtures;

        // The one light left. Red, pulsing on `alarmPeriod` between `alarmLow` and `alarmHigh` -
        // the same three numbers the panels pulse on, so the room and its walls breathe together.
        public Light alarmLight;

        // HOW LONG THE SIREN RUNS (2026-08-31, by request: it used to loop for ever). An alarm that
        // never stops stops being an alarm - it becomes the room tone, and the player has a board to
        // read and a climb to make with it howling over both.
        //
        // Faded rather than cut, over `sirenFade`, so it winds down like a real one rather than
        // being switched off mid-cycle.
        // **SHORTER AGAIN, 2026-09-01, by request.** 14s was already a retreat from looping for
        // ever, and it still ran the whole way down the ladder shaft and into the readout. The
        // alarm's job is to say the cycle has broken, and it has said it by the time the player has
        // decided to move.
        public float sirenSeconds = 7f;
        public float sirenFade = 2.5f;

        public AudioSource sirenSource;
        public AudioClip sirenClip;
        // The alarm's own pulse, and the siren's. One number, because they have to agree - a light
        // that swells out of time with the sound reads as two unrelated things.
        public float alarmPeriod = 1.6f;
        public Color alarmColor = new Color(0.85f, 0.06f, 0.06f);
        public float alarmLow = 0.18f;
        public float alarmHigh = 0.85f;

        // Once. `FinalRoomSequence.BreakOpen` fires it, and that is itself called once per run -
        // but a cycle boundary is not a thing to leave to one call site being disciplined.
        public bool Played { get; private set; }

        public void Play()
        {
            if (Played) return;
            Played = true;
            StartCoroutine(Run());
        }

        // Nothing to reset. A break is the last thing that happens in a cycle - there is no next
        // iteration for this one to be rewound into, and `LoopManager` destroys the whole cycle at
        // the boundary anyway. A reset that put the room back together would be undoing the one
        // thing in it that is meant to be permanent.

        private IEnumerator Run()
        {
            // THE ROOM WARNS. The same ramp the loop's own collapse runs, so a player who has felt
            // that a dozen times reads this instantly - and then it does not stop, which is how they
            // find out it is not that.
            float t = 0f;
            while (t < shakeLeadIn)
            {
                t += EndingClock.Delta;
                cameraShaker?.SetIntensity(Mathf.Clamp01(t / Mathf.Max(0.01f, shakeLeadIn)));
                yield return null;
            }
            cameraShaker?.SetIntensity(1f);

            // THE CUBE GOES DOWN, on its own and before anything else. It is the only object in the
            // room the player made, so it is the only one worth watching leave.
            yield return SinkCube();

            // AND THEN THE ROOM. Every mover on its own clock, all of them at once - a coroutine per
            // structure rather than a queue, because the stagger is in the delays and a queue would
            // make it a sequence instead.
            int running = 0;
            if (movers != null)
                foreach (Mover m in movers)
                {
                    if (m.what == null) continue;
                    running++;
                    StartCoroutine(RunMover(m, () => running--));
                }

            // The announcement lands while the room is still emptying, not after it. What the
            // facility is describing is happening on screen.
            narration?.AnnounceAllCyclesBroken();
            // EVERY PANEL IN THE CYCLE, WHICH IS BOTH ROOMS. `WallPanelDisplay` is assembled per
            // cycle out of every wall in it, so one call fails room3-0's panels and room3-2N's
            // together - the player is standing in one and the thing they have to find is in the
            // other, and the facility has to be saying the same thing in both.
            //
            // The origin is the console: the wave spreads from the thing that was just filled.
            wallPanels?.BeginAlarmGlitch(alarmColor, transform.position, alarmHigh);

            // AND THE BOARD DOWNSTAIRS LIGHTS UP. Powered here, printed later: it is in room3-2N, a
            // storey below the room the player is standing in, and it starts its readout when
            // somebody is close enough to read it (`EvaluationBoard.Update`). What this call buys is
            // that the wall is already glowing on the way down the ladder rather than switching on
            // in the player's face when they arrive.
            board?.PowerOn();

            // AND THE SIGNS THAT SAY WHERE. Up with the announcement, because it is the same
            // sentence: the facility says what has happened and the room says where to go.
            if (wayDownSigns != null)
                foreach (CanvasGroup sign in wayDownSigns)
                    if (sign != null) sign.alpha = 1f;

            // And the way back shuts. It is scenery leaving like everything else, so it goes with
            // the wave rather than before it.
            wayIn?.CloseNow();

            // THE WHITE LIGHT GOES OUT AND THE RED ONE COMES UP, on the same beat as the
            // announcement - the facility saying it and the room showing it are one event.
            KillRoomLights();
            StartCoroutine(PulseAlarm());

            if (sirenSource != null && sirenClip != null)
            {
                sirenSource.clip = sirenClip;
                sirenSource.loop = true;
                sirenSource.Play();
                StartCoroutine(RunSiren());
            }

            while (running > 0) yield return null;

            // Down to a tremor, over the same lead-in it came up on.
            float from = 1f;
            t = 0f;
            while (t < shakeLeadIn)
            {
                t += EndingClock.Delta;
                cameraShaker?.SetIntensity(Mathf.Lerp(from, restingShake,
                                                      Mathf.Clamp01(t / Mathf.Max(0.01f, shakeLeadIn))));
                yield return null;
            }
            cameraShaker?.SetIntensity(restingShake);

            // AND IT STOPS HERE: a RED room, one red light, one red arrow, and a siren that runs
            // down. The way out is the hole in room3-0's floor, which is the hole the player climbed
            // in through, and the arrow is the only thing in the room that says so. Nothing here
            // pushes them toward it and nothing hurries them.
        }

        // Out, and their faces with them. Once - there is nothing that turns them back on.
        private void KillRoomLights()
        {
            if (roomLights != null)
                foreach (Light light in roomLights)
                    if (light != null) light.enabled = false;

            if (roomFixtures == null) return;

            var block = new MaterialPropertyBlock();
            foreach (Renderer fixture in roomFixtures)
            {
                if (fixture == null) continue;
                fixture.GetPropertyBlock(block);
                block.SetColor(LitPropertyIds.EmissionColor, Color.black);
                fixture.SetPropertyBlock(block);
            }
        }

        // The red light, for the rest of the game. It has no end condition because the room has no
        // next state: the player leaves it down the shaft and never comes back up.
        private IEnumerator PulseAlarm()
        {
            if (alarmLight == null) yield break;

            alarmLight.color = alarmColor;
            alarmLight.enabled = true;

            float t = 0f;
            while (true)
            {
                t += EndingClock.Delta;
                // A sine on `alarmPeriod`, which is the number the panels already pulse on - a light
                // swelling out of time with the walls reads as two unrelated things, which is the
                // note the siren's own comment makes about the sound.
                float u = 0.5f - 0.5f * Mathf.Cos(t / Mathf.Max(0.05f, alarmPeriod) * 2f * Mathf.PI);
                alarmLight.intensity = Mathf.Lerp(alarmLow, alarmHigh, u) * alarmIntensity;
                yield return null;
            }
        }

        // How bright the red light is at `alarmHigh`. The pulse bounds are 0-1 fractions shared with
        // the panels, and a Light wants watts - so this is the one number that is only the light's.
        public float alarmIntensity = 9f;

        // Runs, then winds down and stops. See `sirenSeconds`.
        private IEnumerator RunSiren()
        {
            float volume = sirenSource.volume;

            float t = 0f;
            while (t < sirenSeconds)
            {
                t += EndingClock.Delta;
                yield return null;
            }

            t = 0f;
            while (t < sirenFade)
            {
                t += EndingClock.Delta;
                sirenSource.volume = volume * (1f - Mathf.Clamp01(t / sirenFade));
                yield return null;
            }

            sirenSource.Stop();
            // Put back, so nothing that looks at this source later finds it silent for a reason it
            // cannot see.
            sirenSource.volume = volume;
        }

        // The cube shudders into the floor it was built on. **Not `SetActive(false)` at the end** -
        // it is driven under the floor slab, which is opaque, so what hides it is the building. An
        // object switched off vanishes; an object that goes down is taken.
        private IEnumerator SinkCube()
        {
            if (cube == null) yield break;

            Vector3 from = cube.position;
            Vector3 to = from + Vector3.down * cubeSink;

            float t = 0f;
            while (t < cubeShake)
            {
                t += EndingClock.Delta;
                // Jitter about its own resting place, not a drift: the amplitude is constant and the
                // direction is fresh every frame, which is a thing being shaken rather than a thing
                // being moved.
                cube.position = from + Random.insideUnitSphere * cubeShakeAmplitude;
                yield return null;
            }

            t = 0f;
            while (t < cubeSinkSeconds)
            {
                t += EndingClock.Delta;
                float u = Mathf.Clamp01(t / cubeSinkSeconds);
                // Eased IN rather than out - it lets go slowly and then drops, which is what
                // something being swallowed does. The shake rides the whole way down and dies with
                // it, so the last thing seen is the top face going flat into the floor.
                cube.position = Vector3.Lerp(from, to, u * u)
                              + Random.insideUnitSphere * (cubeShakeAmplitude * (1f - u));
                yield return null;
            }

            cube.position = to;
            cube.gameObject.SetActive(false);
        }

        // ONE STRUCTURE LEAVING. Switched off at the end rather than left sitting two rooms away:
        // these travel far enough to be out of sight and no further, so what makes them GONE is the
        // deactivation and what makes it read as leaving is the travel.
        private IEnumerator RunMover(Mover m, System.Action done)
        {
            float t = 0f;
            while (t < m.delay)
            {
                t += EndingClock.Delta;
                yield return null;
            }

            Vector3 from = m.what.position;
            Vector3 to = from + m.travel;
            float duration = Mathf.Max(0.01f, m.duration);

            t = 0f;
            while (t < duration)
            {
                t += EndingClock.Delta;
                float u = Mathf.Clamp01(t / duration);
                // Same ease-in as the cube's, for the same reason and so the whole teardown moves
                // with one weight: nothing here is being pushed, it is all being let go of.
                m.what.position = Vector3.Lerp(from, to, u * u);
                yield return null;
            }

            m.what.gameObject.SetActive(false);
            done?.Invoke();
        }
    }
}
