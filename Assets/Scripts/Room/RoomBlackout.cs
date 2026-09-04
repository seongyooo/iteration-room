using UnityEngine;

namespace IterationRoom
{
    // **A ROOM WHOSE LIGHTS ARE OFF SHOULD LOOK LIKE IT** (2026-09-04, by request).
    //
    // Room2-1 starts with all four fixtures out and stays that way until its switches are found, and
    // until then it was barely darker than a lit room. The reason is written down all over this
    // project: `RenderSettings.ambient` is doing nearly all of the wall and ceiling lighting in this
    // building, and it does not care whether a fixture is on. Measured in `docs/gotchas.md`: with
    // the fixtures off but ambient on, the walls still read 149 of 255. Switching four lights off
    // and having the room stay bright is the fixtures failing to mean anything.
    //
    // So the ambient goes with them, and comes back when they do.
    //
    // **20% RATHER THAN ZERO, AND THAT IS A PLAYABILITY LIMIT NOT A TASTE ONE.** The switches are at
    // 1.85m on the walls and `SceneBuilder` already records them as "low and easy to miss in a dark
    // room". At zero the room is a puzzle you cannot see the pieces of; at a fifth it reads
    // unmistakably as lights-out while leaving the walls and the switches in silhouette.
    //
    // **THIS IS A GLOBAL SETTING USED FOR A LOCAL EFFECT, AND TWO THINGS MAKE THAT SAFE HERE:**
    //
    //  1. **The room is sealed while it is dark.** Its only way out - `Door2_1` - hangs on the same
    //     condition this watches, so nobody can be looking at another room while this is applied.
    //     A second room that ever wants this treatment while the player can see past it would need a
    //     different mechanism, not a second copy of this one.
    //  2. **It lives inside its cycle's world root, which ships asleep.** Cycle scenes are loaded
    //     additively and the CORE scene stays active, so `RenderSettings` here is the whole game's -
    //     if this ran while cycle 2 was merely loaded, it would darken the cycle the player is
    //     actually in. A disabled GameObject does not tick, and `OnDisable` restores, so waking and
    //     sleeping the cycle is what scopes this in time.
    //
    // Whatever is on the settings when this wakes is what it restores, rather than any value written
    // here - so it cannot fight `ApplyEnvironment`, and re-tuning the room's lighting needs no edit
    // to this file.
    public class RoomBlackout : MonoBehaviour
    {
        // The room's own lit condition. Null is not a failure state: with nothing to watch, this
        // does nothing at all rather than guessing that the room is dark.
        public RoomCondition litWhen;

        // How much of the authored ambient survives with the lights out.
        [Range(0f, 1f)] public float darkFraction = 0.2f;

        // Long enough to read as the room changing rather than as a cut, short enough that a player
        // who has just flipped the last switch is not waiting for the room.
        public float seconds = 0.35f;

        private Color litSky, litEquator, litGround;
        private bool captured;
        private float t = 1f;             // 1 = fully lit, 0 = fully dark

        private void OnEnable()
        {
            // Captured on every wake rather than once, because the cycle can be slept and woken and
            // the authored values are whatever the active scene has by then.
            litSky = RenderSettings.ambientSkyColor;
            litEquator = RenderSettings.ambientEquatorColor;
            litGround = RenderSettings.ambientGroundColor;
            captured = true;

            // Arrive already correct: a room woken with its lights out should not fade down from lit
            // in front of the player.
            t = Lit ? 1f : 0f;
            Apply();
        }

        private void OnDisable()
        {
            if (!captured) return;
            Write(litSky, litEquator, litGround);
        }

        private bool Lit => litWhen == null || litWhen.Satisfied;

        private void Update()
        {
            if (!captured) return;

            float target = Lit ? 1f : 0f;
            if (Mathf.Approximately(t, target)) return;

            float step = seconds > 0f ? Time.deltaTime / seconds : 1f;
            t = Mathf.MoveTowards(t, target, step);
            Apply();
        }

        private void Apply()
        {
            float k = Mathf.Lerp(darkFraction, 1f, Mathf.SmoothStep(0f, 1f, t));
            Write(litSky * k, litEquator * k, litGround * k);
        }

        private void Write(Color sky, Color equator, Color ground)
        {
            RenderSettings.ambientSkyColor = sky;
            RenderSettings.ambientEquatorColor = equator;
            RenderSettings.ambientGroundColor = ground;
            // Assigning the colours does NOT rebuild the ambient probe - the same line, for the same
            // reason, as in `SceneBuilder.ApplyEnvironment`. Without it none of this reaches a shader.
            DynamicGI.UpdateEnvironment();
        }
    }
}
