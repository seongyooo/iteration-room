using UnityEngine;

namespace IterationRoom
{
    [System.Serializable]
    public struct RecordedFrame
    {
        public float time;
        public Vector3 position;
        public float yaw;
        public bool floorButtonHeld;

        public RecordedFrame(float time, Vector3 position, float yaw, bool floorButtonHeld)
        {
            this.time = time;
            this.position = position;
            this.yaw = yaw;
            this.floorButtonHeld = floorButtonHeld;
        }
    }
}
