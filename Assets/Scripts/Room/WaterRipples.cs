using UnityEngine;

namespace IterationRoom
{
    // RINGS SPREADING FROM WHEREVER SOMEBODY MOVES IN THE POOL.
    //
    // WHY THIS AND NOT THE SHADER. The water's surface already has moving noise in it, and that noise
    // is what makes it read as water at all - but it is the same everywhere and it answers to nothing.
    // A ripple has to start WHERE the player is and grow from there, which means either feeding the
    // shader a list of disturbance centres and their ages, or drawing the rings. Drawing them is one
    // component and a quad, it composes with a surface shader nobody has to touch, and the count is
    // bounded by construction.
    //
    // A FIXED POOL OF QUADS, cycled oldest-first. No allocation, no `Instantiate` inside a walk, and a
    // hard cap on how much transparent overdraw a player running in circles can ask for - which is the
    // one real cost of drawing water this way.
    //
    // NOTHING ABOUT IT IS RECORDED OR RESET. A ripple is two seconds long and belongs to nobody; the
    // loop has no opinion on it. Ghosts leave none for the reason they leave no wake at all: they have
    // no collider and this is driven from the living player's own movement (see `WaterPool`).
    public class WaterRipples : MonoBehaviour
    {
        // The rings, face-up quads with the ripple texture on them. Written by `SceneBuilder`.
        public Transform[] rings;
        public Renderer[] renderers;

        [Header("How a ring grows")]
        // Starts at about a leg's width and ends about as wide as the player can see it being theirs.
        public float startRadius = 0.22f;
        public float endRadius = 1.9f;
        public float life = 1.9f;
        // The ring's own opacity at birth. It fades to nothing over its life on a curve that holds
        // near the start - a ripple is brightest just after it is made, not at the instant of it.
        public float peakAlpha = 0.55f;

        // WHERE THE WATER IS. Rings are drawn a hair above it, and it moves as room2-6 drains - a ripple
        // spreading at the height the surface used to be is worse than no ripple at all.
        public float skim = 0.012f;

        private float[] born;      // when each ring started, or negative if it is free
        private int next;
        private MaterialPropertyBlock block;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private void Awake()
        {
            block = new MaterialPropertyBlock();
            born = new float[rings != null ? rings.Length : 0];
            for (int i = 0; i < born.Length; i++)
            {
                born[i] = -1f;
                if (renderers != null && i < renderers.Length && renderers[i] != null)
                    renderers[i].enabled = false;
            }
        }

        // Told by `WaterPool`, which is the one owner of the level.
        public void SetHeight(float localY)
        {
            Vector3 at = transform.localPosition;
            transform.localPosition = new Vector3(at.x, localY + skim, at.z);
        }

        // START ONE, at a point on the water. Oldest-first when they are all in flight, because the
        // oldest is the one closest to being invisible anyway.
        public void Ring(Vector3 worldPoint)
        {
            if (rings == null || rings.Length == 0 || born == null) return;

            int slot = -1;
            for (int i = 0; i < rings.Length; i++)
            {
                int at = (next + i) % rings.Length;
                if (born[at] < 0f) { slot = at; break; }
            }
            if (slot < 0) { slot = next; }
            next = (slot + 1) % rings.Length;

            if (rings[slot] == null) return;

            // Moved, never turned. The quad is authored lying flat and stays that way - writing a
            // rotation here would stand every ripple on its edge, and there is nothing a ring's facing
            // could usefully say anyway.
            rings[slot].position = worldPoint;
            rings[slot].localScale = Vector3.one * (startRadius * 2f);
            born[slot] = Time.time;

            if (renderers != null && slot < renderers.Length && renderers[slot] != null)
                renderers[slot].enabled = true;
        }

        private void Update()
        {
            if (born == null) return;

            for (int i = 0; i < born.Length; i++)
            {
                if (born[i] < 0f) continue;

                float age = (Time.time - born[i]) / Mathf.Max(0.05f, life);
                if (age >= 1f)
                {
                    born[i] = -1f;
                    if (renderers != null && i < renderers.Length && renderers[i] != null)
                        renderers[i].enabled = false;
                    continue;
                }

                // GROWTH SLOWS. A real ring spreads fast at first and stalls as it loses energy, and a
                // linear one reads as an animation playing rather than as something water is doing.
                float radius = Mathf.Lerp(startRadius, endRadius, 1f - (1f - age) * (1f - age));
                if (rings[i] != null) rings[i].localScale = Vector3.one * (radius * 2f);

                if (renderers == null || i >= renderers.Length || renderers[i] == null) continue;

                // Held near full for the first third and gone by the end. Squared, so it thins out
                // rather than switching off.
                float fade = 1f - Mathf.Clamp01((age - 0.25f) / 0.75f);
                renderers[i].GetPropertyBlock(block);
                block.SetColor(BaseColorId, new Color(1f, 1f, 1f, peakAlpha * fade * fade));
                renderers[i].SetPropertyBlock(block);
            }
        }
    }
}
