using UnityEngine;

namespace IterationRoom
{
    // Water spreading out from where the stream lands, and DRYING when it stops.
    //
    // The puddle outlives the tap on purpose. Shutting a tap does not delete the water that already
    // came out of it, and a spill that vanished on the same frame as the press made the whole thing
    // read as a switch turning a decal on and off. It holds at its size for a moment - nothing about
    // standing water reacts instantly - then draws back toward the drain point and fades as it goes.
    //
    // SIZE IS THE TRANSFORM, DEPTH IS THE ALPHA, and both are needed: shrinking alone looks like the
    // water is being sucked into a hole, fading alone leaves a full-width ghost. Together they read as
    // a shallow film getting shallower from the edges in.
    //
    // Scaled in X and Z only. `RippleSurface` puts its wave amplitude in metres and Y must stay 1, or
    // a puddle three metres across would carry a swell three metres high.
    public class SpreadingPuddle : MonoBehaviour
    {
        public float startRadius = 0.12f;
        // Metres of radius per second while the tap is running.
        public float growthRate = 0.4f;
        public float maxRadius = 3.5f;

        // How long it sits at full size after the tap is shut, before it starts going. Long enough to
        // be clearly a separate event from the press.
        public float lingerSeconds = 2.5f;
        // And how long it then takes to go. Slow - this is evaporation and drainage, not a wipe.
        public float dryDuration = 6f;

        // Read off the material at Awake so the fade has something to fade FROM, rather than a second
        // copy of a number SceneBuilder already chose.
        private float fullAlpha = 0.32f;

        private bool fed;
        private float radius;
        private float dryTimer;
        private Renderer rend;
        private MaterialPropertyBlock block;

        public float Radius => radius;

        private void Awake()
        {
            rend = GetComponent<Renderer>();
            block = new MaterialPropertyBlock();
            if (rend != null && rend.sharedMaterial != null && rend.sharedMaterial.HasProperty("_BaseColor"))
                fullAlpha = rend.sharedMaterial.GetColor("_BaseColor").a;
            ResetInstant();
        }

        // The tap running, or not. Turning it back on while the puddle is still drying picks it up
        // where it is rather than starting again - the water on the floor did not go anywhere.
        public void SetFed(bool value)
        {
            if (value && !fed)
            {
                dryTimer = 0f;
                // ...UNLESS IT HAD ALREADY GONE. Picking up where it is, is the right answer for a
                // puddle caught halfway through drying - the water on the floor did not go anywhere.
                // It is the wrong answer once the floor is dry: the radius is still whatever the last
                // spill reached, so re-opening the tap popped a full three-metre puddle into being at
                // full opacity in a single frame. The renderer being off is exactly the fact that
                // says this water is gone rather than going.
                if (rend == null || !rend.enabled) radius = startRadius;
            }

            fed = value;
            if (value && rend != null) rend.enabled = true;
        }

        // THE LOOP REWINDING, which is a different thing from the tap being shut and must not use the
        // drying at all. This happens behind closed eyelids at the top of an iteration; a puddle
        // politely evaporating over six seconds would still be there when the player opens their eyes.
        public void ResetInstant()
        {
            fed = false;
            radius = startRadius;
            dryTimer = 0f;
            Apply(0f);
            if (rend != null) rend.enabled = false;
        }

        private void Update()
        {
            if (fed)
            {
                radius = Mathf.Min(maxRadius, radius + growthRate * Time.deltaTime);
                Apply(1f);
                return;
            }

            if (rend == null || !rend.enabled) return;

            dryTimer += Time.deltaTime;
            if (dryTimer < lingerSeconds)
            {
                Apply(1f);
                return;
            }

            float k = Mathf.Clamp01((dryTimer - lingerSeconds) / Mathf.Max(0.01f, dryDuration));
            // Eased, so it lets go slowly and finishes quickly rather than shrinking at a constant
            // rate - which is what a film of water on a hard floor actually does.
            float fade = 1f - k * k;
            radius = Mathf.Lerp(radius, startRadius, k * 0.04f);
            Apply(fade);

            if (k >= 1f) rend.enabled = false;
        }

        private void Apply(float fade)
        {
            transform.localScale = new Vector3(radius * 2f, 1f, radius * 2f);

            if (rend == null) return;
            rend.GetPropertyBlock(block);
            Color c = rend.sharedMaterial != null && rend.sharedMaterial.HasProperty("_BaseColor")
                ? rend.sharedMaterial.GetColor("_BaseColor")
                : Color.white;
            c.a = fullAlpha * fade;
            block.SetColor("_BaseColor", c);
            rend.SetPropertyBlock(block);
        }
    }
}
