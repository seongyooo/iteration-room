using UnityEngine;

namespace IterationRoom
{
    // Shakes the view as the cycle collapses. Lives on a rig between the player and the camera,
    // because FirstPersonController writes the camera's own localPosition and localEulerAngles
    // every frame (look, and SetEyePose during the wake-up) - anything applied directly to the
    // camera would be overwritten the moment either of those ran.
    public class CameraShaker : MonoBehaviour
    {
        public float maxPositionShake = 0.055f;
        public float maxRotationShake = 1.4f;
        public float frequency = 16f;

        private float intensity;

        public void SetIntensity(float t)
        {
            intensity = Mathf.Clamp01(t);
        }

        // LateUpdate, so it runs after the controller has posed the camera for this frame.
        private void LateUpdate()
        {
            if (intensity <= 0f)
            {
                transform.localPosition = Vector3.zero;
                transform.localRotation = Quaternion.identity;
                return;
            }

            // Perlin rather than Random: it is continuous, so this reads as a building judder.
            // Per-frame random values look like a broken frame rate, not like the room moving.
            float t = Time.time * frequency;
            float x = Mathf.PerlinNoise(t, 0f) - 0.5f;
            float y = Mathf.PerlinNoise(0f, t) - 0.5f;
            float z = Mathf.PerlinNoise(t, t) - 0.5f;

            float amount = intensity * intensity;
            transform.localPosition = new Vector3(x, y, 0f) * 2f * maxPositionShake * amount;
            transform.localRotation = Quaternion.Euler(
                y * 2f * maxRotationShake * amount,
                x * 2f * maxRotationShake * amount,
                z * 2f * maxRotationShake * amount);
        }
    }
}
