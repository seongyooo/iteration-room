using UnityEngine;

namespace IterationRoom
{
    // Footsteps paced by distance travelled rather than by a timer, so stride length stays
    // constant instead of the steps machine-gunning as speed rises - the same reason
    // GhostReplayer.SwingLimbs advances its walk phase with distance.
    //
    // It reads nothing but the transform, which is deliberate: that makes the identical component
    // work on the player and on a replaying ghost. A ghost has no CharacterController and no
    // velocity to ask for, only a position being scrubbed along a recorded timeline.
    public class FootstepPlayer : MonoBehaviour
    {
        public AudioSource source;
        public AudioClip[] footstepClips;

        public float strideLength = 0.8f;
        // Under this the character is shuffling against a wall rather than walking, and stepping
        // sounds there read as a stuck loop.
        public float minSpeed = 0.6f;
        // A loop reset teleports metres in a single frame. Anything above this is not walking, so
        // the reset doesn't fire a burst of steps.
        public float teleportSpeed = 20f;
        public Vector2 pitchJitter = new Vector2(0.92f, 1.08f);

        private Vector3 lastPosition;
        private float distanceSinceStep;
        private int lastClipIndex = -1;

        private void OnEnable()
        {
            lastPosition = transform.position;
            distanceSinceStep = 0f;
        }

        private void Update()
        {
            if (Time.deltaTime <= 0f) return;

            Vector3 delta = transform.position - lastPosition;
            lastPosition = transform.position;
            delta.y = 0f;

            float distance = delta.magnitude;
            float speed = distance / Time.deltaTime;

            if (speed < minSpeed || speed > teleportSpeed)
            {
                distanceSinceStep = 0f;
                return;
            }

            distanceSinceStep += distance;
            if (distanceSinceStep < strideLength) return;

            distanceSinceStep -= strideLength;
            PlayStep();
        }

        private void PlayStep()
        {
            if (source == null || footstepClips == null || footstepClips.Length == 0) return;

            int index = Random.Range(0, footstepClips.Length);
            // Never the same sample twice running - a repeat is what makes footsteps sound canned.
            if (footstepClips.Length > 1 && index == lastClipIndex)
                index = (index + 1) % footstepClips.Length;
            lastClipIndex = index;

            AudioClip clip = footstepClips[index];
            if (clip == null) return;

            source.pitch = Random.Range(pitchJitter.x, pitchJitter.y);
            source.PlayOneShot(clip);
        }
    }
}
