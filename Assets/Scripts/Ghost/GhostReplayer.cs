using System.Collections.Generic;
using UnityEngine;

namespace IterationRoom
{
    // Replays a recorded timeline from a previous iteration. Ghosts never read
    // input or physics - they scrub through recorded frames and report their
    // recorded floor-button hold state to the shared FloorButton each frame.
    public class GhostReplayer : MonoBehaviour
    {
        // Limb pivots, sitting at the shoulders and hips. Optional - playback works without them.
        public Transform leftArm;
        public Transform rightArm;
        public Transform leftLeg;
        public Transform rightLeg;

        public float strideRate = 4f;
        public float maxSwingAngle = 32f;
        // Speed at which the swing reaches full amplitude; below it the limbs move proportionally
        // less, so a ghost standing still stands still instead of marching on the spot.
        public float fullSwingSpeed = 4.5f;

        // How long a ghost's arm stays mid-swing after one of its recorded pops fires. Purely
        // presentational: without it a balloon bursts near a ghost that is just standing there.
        public float popSwingDuration = 0.28f;

        private List<RecordedFrame> timeline;
        private List<PopEvent> pops;
        private int popCursor;
        private float swingUntil = -1f;
        // Shared with the PlayerRecorder that produced the timeline - an interactable's index here
        // is its bit in RecordedFrame.signals.
        private GhostInteractable[] interactables;
        private int cursor;
        private uint activeSignals;
        private bool finished;
        private Vector3 lastPosition;
        private float walkPhase;
        private Renderer[] renderers;

        private void Awake()
        {
            renderers = GetComponentsInChildren<Renderer>(true);
        }

        // Every timeline's frame 0 is the bed spawn, because recording starts the instant the
        // wake-up hands control back - so ResetPlayback parks every ghost ever made on the exact
        // spot the player wakes up on. Without hiding them, opening your eyes means looking
        // through a stack of dark figures standing inside you, every single iteration.
        public void SetVisible(bool visible)
        {
            if (renderers == null) return;
            foreach (Renderer r in renderers)
                if (r != null) r.enabled = visible;
        }

        public void Init(RecordedTimeline recorded, GhostInteractable[] ghostInteractables)
        {
            timeline = recorded != null ? recorded.frames : null;
            pops = recorded != null ? recorded.pops : null;
            interactables = ghostInteractables;
            ResetPlayback();
        }

        public void ResetPlayback()
        {
            cursor = 0;
            popCursor = 0;
            swingUntil = -1f;
            if (timeline != null && timeline.Count > 0)
            {
                transform.position = timeline[0].position;
                transform.rotation = Quaternion.Euler(0f, timeline[0].yaw, 0f);
            }
            lastPosition = transform.position;
            walkPhase = 0f;
            finished = false;
            // Release everything before replaying from the top, so a ghost that ended the previous
            // pass standing on a button doesn't leave it stuck on.
            ApplySignals(0u);
        }

        public void Tick(float elapsedLoopTime)
        {
            if (timeline == null || timeline.Count == 0) return;

            // Drained before the end-of-timeline check below, not after: a tick can jump past
            // several recorded frames at once, and the last pops of a recording sit right up
            // against its final frame. Checked first, a single long frame would retire the ghost
            // with its closing pops never fired, and those balloons would stay up for good.
            DrainPops(elapsedLoopTime);

            // Once the recording runs out this ghost is done: it lets go of everything and leaves.
            //
            // Holding the last frame instead - which is what clamping the cursor does on its own -
            // makes ending a cycle early strictly better than seeing it out. Quit at t=5s while
            // standing on the floor pad and that final frame's signal would stay applied from t=5
            // to t=60 of every future iteration: five seconds of your time buying a 55-second hold,
            // which deletes the "spend a whole iteration on this" premise the puzzle is built on.
            if (elapsedLoopTime > timeline[timeline.Count - 1].time)
            {
                if (!finished)
                {
                    finished = true;
                    ApplySignals(0u);
                    SetVisible(false);
                }
                return;
            }

            while (cursor < timeline.Count - 1 && timeline[cursor + 1].time <= elapsedLoopTime)
                cursor++;

            RecordedFrame frame = timeline[cursor];
            transform.position = frame.position;
            transform.rotation = Quaternion.Euler(0f, frame.yaw, 0f);
            ApplySignals(frame.signals);

            // Ghosts are deterministic, so once one of them has walked into Room2 the balloons come
            // down at the same instant every iteration - which is what keeps every later recording
            // aligned with the field it was made against.
            if (BalloonField.Instance != null) BalloonField.Instance.TriggerIfInside(transform.position);

            SwingLimbs();
        }

        // Replays this ghost's pops by balloon identity, so it bursts exactly the balloons the
        // player did - wherever physics has carried them this iteration. Popping an already-burst
        // balloon is a no-op, which is what happens when the living player gets to one first.
        private void DrainPops(float elapsedLoopTime)
        {
            if (pops == null) return;

            while (popCursor < pops.Count && pops[popCursor].time <= elapsedLoopTime)
            {
                if (BalloonField.Instance != null) BalloonField.Instance.PopById(pops[popCursor].balloonId);
                popCursor++;
                swingUntil = Time.time + popSwingDuration;
            }
        }

        // Only recorded position and yaw exist, so the walk is inferred from how far the ghost
        // moved. Phase advances with distance rather than time, which keeps the stride length
        // constant instead of the legs spinning faster the quicker it goes.
        private void SwingLimbs()
        {
            if (leftLeg == null || rightLeg == null) return;

            Vector3 delta = transform.position - lastPosition;
            lastPosition = transform.position;
            delta.y = 0f;

            float distance = delta.magnitude;
            float speed = Time.deltaTime > 0f ? distance / Time.deltaTime : 0f;
            float amplitude = Mathf.Clamp01(speed / fullSwingSpeed);

            walkPhase += distance * strideRate;
            float swing = Mathf.Sin(walkPhase) * maxSwingAngle * amplitude;

            leftLeg.localRotation = Quaternion.Euler(swing, 0f, 0f);
            rightLeg.localRotation = Quaternion.Euler(-swing, 0f, 0f);

            // Arms counter-swing against the legs, and less far.
            if (leftArm != null) leftArm.localRotation = Quaternion.Euler(-swing * 0.6f, 0f, 0f);
            if (rightArm != null) rightArm.localRotation = Quaternion.Euler(swing * 0.6f, 0f, 0f);

            // A recent pop overrides the walk swing on the tool arm only, so a ghost bursting
            // balloons on the move still walks.
            if (rightArm != null && Time.time < swingUntil)
                rightArm.localRotation = Quaternion.Euler(-70f, 0f, 0f);
        }

        // Reports only the bits that actually changed, so each interactable sees clean edges: a
        // hold button gets one add and one remove, and a one-touch button fires once per rise
        // rather than every frame the pulse is up.
        private void ApplySignals(uint signals)
        {
            if (interactables != null)
            {
                int count = Mathf.Min(interactables.Length, 32);
                for (int i = 0; i < count; i++)
                {
                    uint bit = 1u << i;
                    bool now = (signals & bit) != 0u;
                    if (now == ((activeSignals & bit) != 0u)) continue;
                    if (interactables[i] != null) interactables[i].SetGhostSignal(this, now);
                }
            }

            activeSignals = signals;
        }

        private void OnDestroy()
        {
            ApplySignals(0u);
        }
    }
}
