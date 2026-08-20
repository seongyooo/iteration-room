using UnityEngine;

namespace IterationRoom
{
    // THE SLAB IN ROOM3-1'S NORTH CORRIDOR. Held up while a pad is held; it comes down the moment the
    // pad is let go, and it does not care what is underneath it.
    //
    // **WHY THIS IS NOT A `Door`.** Everything else about it is a door - it tracks its pads, it slides,
    // it freezes during the wake-up - and one thing is the opposite. `Door.PlayerInDoorway` exists so
    // that a slab REFUSES to shut on the player: "a CharacterController is not pushed by a moving
    // transform, so a slab closing through one leaves the player inside it". That reprieve is the
    // whole reason a door is safe, and it is exactly what this must not have. Inheriting it and then
    // switching it off would leave the safety of every door in the building one boolean away from a
    // corridor trap, so the trap is its own component and says so.
    //
    // **THE FIRST DEATH IN THE PROJECT** (2026-08-21, by request). There is no kill plane, no fall
    // damage and no Y bound anywhere else - fall into room2-3's pit and you sit at the bottom until
    // the clock runs out. So "dying" had to be given a meaning, and in a sixty-second loop there is
    // only one honest one: the iteration ends now. That is `LoopManager.EndCycleEarly`, which is also
    // what the N key does - the flag route, so `ElapsedTime` is never forced and the tail of the
    // recording stays valid.
    //
    // **AND THE RECORDING BECOMES A GHOST LIKE ANY OTHER, which is the point.** The run is not
    // discarded. Next iteration a past self walks into the corridor, the past self on the pad steps
    // off at the same moment it did before, the slab comes down on the same frame, and the timeline
    // ends there - so the death replays, every iteration, for the rest of the cycle. Nothing here
    // implements that. It falls out of the loop already being deterministic, and it is why this kills
    // through `EndCycleEarly` rather than through anything that would throw the recording away.
    public class CrushingBarrier : MonoBehaviour
    {
        public FloorButton[] pads;

        public Transform slab;
        // Where the slab sits when the pad is held. Up, into a recess in the corridor ceiling: a thing
        // that drops is read as a threat on sight, where a thing that slides sideways is read as a
        // door and this is not one.
        public Vector3 openLocalOffset = new Vector3(0f, 2.8f, 0f);
        // Deliberately quicker than a door's 1.0. It is not opening politely, it is being let go of.
        public float closeDuration = 0.45f;
        public float openDuration = 0.9f;

        public AudioSource audioSource;
        public AudioClip moveClip;
        public AudioClip crushClip;

        // The volume that kills, in the slab's own frame at its CLOSED position. Slightly wider than
        // the slab so nobody survives by hugging the corridor wall inside its footprint.
        public Vector3 killHalfExtents = new Vector3(1f, 1.4f, 0.35f);
        // How far down the slab has to be before it is lethal. Not zero: a slab that has only just
        // begun to move is still overhead, and killing on the first millimetre would make walking
        // under a fully open barrier a coin toss on frame timing.
        [Range(0f, 1f)] public float lethalBelow = 0.55f;

        private float openAmount;
        private Vector3 closedLocalPos;
        private Vector3 closedWorldCentre;
        private bool killedThisIteration;

        private void Awake()
        {
            if (slab == null) return;
            closedLocalPos = slab.localPosition;
            closedWorldCentre = slab.parent != null
                ? slab.parent.TransformPoint(closedLocalPos)
                : closedLocalPos;
            Apply();
        }

        // The loop's rewind. Snapped shut and silent, like `Door.Close` - this runs behind the closed
        // eyelids and a slab slamming under a black screen is the machinery showing through.
        public void ResetBarrier()
        {
            openAmount = 0f;
            killedThisIteration = false;
            Apply();
        }

        private void Update()
        {
            bool running = LoopManager.Instance == null || LoopManager.Instance.IterationRunning;
            if (!running) return;

            float target = FloorButton.AllActive(pads) ? 1f : 0f;
            if (!Mathf.Approximately(openAmount, target))
            {
                float duration = target > openAmount ? openDuration : closeDuration;
                float step = duration > 0f ? Time.deltaTime / duration : 1f;
                float next = Mathf.MoveTowards(openAmount, target, step);

                // ONLY FROM A STANDING START, at either end of the travel - otherwise a pad tapped
                // twice while the slab is mid-flight stacks a second copy of a two-second motor.
                bool wasAtRest = Mathf.Approximately(openAmount, 0f) || Mathf.Approximately(openAmount, 1f);
                if (wasAtRest && audioSource != null && moveClip != null)
                    audioSource.PlayOneShot(moveClip);

                openAmount = next;
                Apply();
            }

            CheckCrush();
        }

        // POLLED, NOT A TRIGGER, for the reason `FloorButton` documents at length: the loop teleports
        // the player by disabling and re-enabling the CharacterController inside one frame, so exit
        // callbacks are simply never delivered and a trigger-based version latches.
        private void CheckCrush()
        {
            if (killedThisIteration || openAmount > lethalBelow) return;

            Collider player = PlayerLookup.Collider;
            if (player == null || !player.enabled) return;

            Vector3 local = transform.InverseTransformPoint(player.bounds.center)
                          - transform.InverseTransformPoint(closedWorldCentre);
            if (Mathf.Abs(local.x) > killHalfExtents.x) return;
            if (Mathf.Abs(local.y) > killHalfExtents.y) return;
            if (Mathf.Abs(local.z) > killHalfExtents.z) return;

            killedThisIteration = true;
            if (audioSource != null && crushClip != null) audioSource.PlayOneShot(crushClip);
            // The whole of what dying is. See the class note: the recording keeps everything up to
            // this moment and becomes a ghost, so the death is replayed rather than erased.
            if (LoopManager.Instance != null) LoopManager.Instance.EndCycleEarly();
        }

        private void Apply()
        {
            if (slab != null) slab.localPosition = closedLocalPos + openLocalOffset * openAmount;
        }
    }
}
