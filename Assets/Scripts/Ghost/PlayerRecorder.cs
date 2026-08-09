using System.Collections.Generic;
using UnityEngine;

namespace IterationRoom
{
    // Samples the player's transform and floor-button hold state at a fixed
    // interval while a loop is running, so it can be handed to a GhostReplayer
    // for exact playback next iteration.
    public class PlayerRecorder : MonoBehaviour
    {
        public FloorButton floorButton;
        public float sampleInterval = 0.02f;

        private readonly List<RecordedFrame> frames = new List<RecordedFrame>();
        private float sampleTimer;
        private bool recording;

        public void BeginRecording()
        {
            frames.Clear();
            sampleTimer = 0f;
            recording = true;
        }

        public List<RecordedFrame> EndRecording()
        {
            recording = false;
            return new List<RecordedFrame>(frames);
        }

        private void Update()
        {
            if (!recording || LoopManager.Instance == null) return;

            sampleTimer -= Time.deltaTime;
            if (sampleTimer > 0f) return;
            sampleTimer = sampleInterval;

            bool holding = floorButton != null && floorButton.PlayerHolding;
            frames.Add(new RecordedFrame(
                LoopManager.Instance.ElapsedTime,
                transform.position,
                transform.eulerAngles.y,
                holding));
        }
    }
}
