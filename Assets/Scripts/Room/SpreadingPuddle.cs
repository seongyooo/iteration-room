using UnityEngine;

namespace IterationRoom
{
    // Water spreading out from where the stream lands, for as long as it is running. A flat cylinder
    // whose radius grows on its own - nothing measures the stream, because nothing has to: the
    // puddle only exists while the tap is on, and the tap switches the whole flow root off again.
    //
    // RESET IN OnEnable RATHER THAN ON A CALL, so it needs no wiring at all. Turning the tap off
    // deactivates the root and turning it back on starts the spread from nothing, which is also what
    // the loop's own reset gets for free - a new iteration's flow has never run before.
    public class SpreadingPuddle : MonoBehaviour
    {
        public float startRadius = 0.1f;
        // Metres of radius per second. Slow enough to read as spreading rather than as a disc being
        // scaled, and it reaches the far wall in about the length of one iteration.
        public float growthRate = 0.4f;
        public float maxRadius = 3.5f;
        public float thickness = 0.02f;

        private float radius;

        private void OnEnable()
        {
            radius = startRadius;
            Apply();
        }

        private void Update()
        {
            if (radius >= maxRadius) return;
            radius = Mathf.Min(maxRadius, radius + growthRate * Time.deltaTime);
            Apply();
        }

        // A primitive cylinder is 1 across and 2 tall, so X and Z take the DIAMETER and Y takes the
        // half-height.
        private void Apply()
        {
            transform.localScale = new Vector3(radius * 2f, thickness / 2f, radius * 2f);
        }
    }
}
