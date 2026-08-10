using UnityEngine;

namespace IterationRoom
{
    [System.Serializable]
    public struct RecordedFrame
    {
        public float time;
        public Vector3 position;
        public float yaw;

        // One bit per GhostInteractable, indexed by its position in the shared interactable list.
        // A bitmask rather than a bool array because a 60-second timeline is ~3,600 frames and an
        // array per frame would mean 3,600 allocations per ghost, every ghost, forever.
        public uint signals;

        public RecordedFrame(float time, Vector3 position, float yaw, uint signals)
        {
            this.time = time;
            this.position = position;
            this.yaw = yaw;
            this.signals = signals;
        }

        public bool HasSignal(int index) => (signals & (1u << index)) != 0u;
    }
}
