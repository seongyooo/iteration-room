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
    // **AND IT RESTORES TO STATED VALUES, NOT TO WHATEVER IT FOUND** - see `litSky` below for the
    // ordering that makes the difference, and for what a stale one costs the ending.
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

        // **THE LIT VALUES ARE HANDED IN, NOT SAMPLED, AND THAT IS A CORRECTNESS FIX RATHER THAN
        // TIDINESS** (2026-09-04). The first version read `RenderSettings` at `OnEnable` and restored
        // whatever it found. That is fine exactly once and wrong the moment the ordering is not what
        // you pictured: `LoopManager` wakes the NEXT cycle before it sleeps the current one, so there
        // is a window with this component still holding the ambient down - and anything that reads
        // the settings in that window reads a darkened room as if it were the authored one, and
        // writes it back forever.
        //
        // What that costs when it goes wrong is not local. `RenderSettings` is per SCENE and
        // therefore per everything, which `FacilityExterior.LightTheOutside` says in as many words -
        // it leaves the ambient alone deliberately, because the ending's outside "wants the same even
        // white fill the inside has always had". A stale value here darkens the ending.
        //
        // Set by `SceneBuilder` from the same constants `ApplyEnvironment` writes.
        public Color litSky = new Color(0.356f, 0.356f, 0.356f);
        public Color litEquator = new Color(0.763f, 0.763f, 0.763f);
        public Color litGround = new Color(0.521f, 0.521f, 0.521f);

        // **AND IT ONLY APPLIES WHILE THE PLAYER IS ACTUALLY IN THE ROOM** (2026-09-04, after play
        // found the cycle 3 ending rendering at a fifth of its ambient - walls measured 32 of 255
        // where the documented figure with ambient is 149, which is this component's own fraction).
        //
        // The first version leaned on the cycle sleeping to scope this in time, and that reasoning
        // did not survive contact: `LoopManager` wakes the NEXT cycle before the current one sleeps,
        // the test shortcut enters a cycle directly, and tracing which of those left it live was
        // costing more than making the question irrelevant. A global setting driven by a LOCAL fact
        // should be gated on that local fact, not on a chain of ownership that has to stay true.
        //
        // The camera is asked rather than a player reference being wired, because this object lives
        // in a cycle scene and the player lives in the core one - `docs/gotchas.md` records that
        // Unity nulls serialized references across that boundary.
        public Vector3 roomCentre;
        public Vector3 roomSize = new Vector3(9f, 5.4f, 11f);

        private float t = 1f;             // 1 = fully lit, 0 = fully dark

        private void OnEnable()
        {
            // Arrive already correct: a room woken with its lights out should not fade down from lit
            // in front of the player.
            t = Lit ? 1f : 0f;
            Apply();
        }

        // **RESTORED ON DISABLE, WHATEVER STATE THE ROOM WAS IN.** The cycle sleeping is the last
        // moment this can hand the ambient back, and it is unconditional for that reason.
        private void OnDisable() => Write(litSky, litEquator, litGround);

        // Outside the room counts as LIT: the blackout is this room's business and nobody else's.
        private bool Lit => litWhen == null || litWhen.Satisfied || !PlayerInside;

        private bool PlayerInside
        {
            get
            {
                Camera cam = Camera.main;
                if (cam == null) return false;
                Vector3 d = cam.transform.position - roomCentre;
                return Mathf.Abs(d.x) <= roomSize.x * 0.5f
                    && Mathf.Abs(d.y) <= roomSize.y * 0.5f
                    && Mathf.Abs(d.z) <= roomSize.z * 0.5f;
            }
        }

        private void Update()
        {
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
