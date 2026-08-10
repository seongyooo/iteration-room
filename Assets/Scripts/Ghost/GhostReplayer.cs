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

        private List<RecordedFrame> timeline;
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

        public void Init(List<RecordedFrame> recordedTimeline, GhostInteractable[] ghostInteractables)
        {
            timeline = recordedTimeline;
            interactables = ghostInteractables;
            ResetPlayback();
        }

        public void ResetPlayback()
        {
            cursor = 0;
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
            SwingLimbs();
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
