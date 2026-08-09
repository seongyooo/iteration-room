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
        private FloorButton floorButton;
        private int cursor;
        private bool holdingButton;
        private Vector3 lastPosition;
        private float walkPhase;

        public void Init(List<RecordedFrame> recordedTimeline, FloorButton button)
        {
            timeline = recordedTimeline;
            floorButton = button;
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
            SetHolding(false);
        }

        public void Tick(float elapsedLoopTime)
        {
            if (timeline == null || timeline.Count == 0) return;

            while (cursor < timeline.Count - 1 && timeline[cursor + 1].time <= elapsedLoopTime)
                cursor++;

            RecordedFrame frame = timeline[cursor];
            transform.position = frame.position;
            transform.rotation = Quaternion.Euler(0f, frame.yaw, 0f);
            SetHolding(frame.floorButtonHeld);
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

        private void SetHolding(bool holding)
        {
            if (holding == holdingButton) return;
            holdingButton = holding;
            if (floorButton != null) floorButton.SetGhostHolding(this, holding);
        }

        private void OnDestroy()
        {
            if (floorButton != null) floorButton.SetGhostHolding(this, false);
        }
    }
}
