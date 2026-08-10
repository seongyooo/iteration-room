using System.Collections;
using UnityEngine;

namespace IterationRoom
{
    // The wall panels are displays, and they boot at the start of every iteration: dark when you
    // wake, then sweeping white as you sit up. It is the visual half of the announcer's
    // "New cycle initialized" - the facility powering the cell back up around you.
    //
    // Panels come up from the floor rows first with a little scatter, rather than all together. A
    // uniform fade reads as someone turning a dimmer; a staggered one reads as separate screens
    // waking, which is the point.
    public class WallPanelDisplay : MonoBehaviour
    {
        public Renderer[] panels;

        public Color offColor = new Color(0.13f, 0.135f, 0.15f);
        public Color onColor = Color.white;

        public float sweepDuration = 1.4f;
        // How long a single panel takes to come up, as a fraction of the whole sweep. Larger
        // values overlap the panels more and soften the wave.
        public float panelFade = 0.22f;
        // Scatter on each panel's start time, so the wave isn't a clean straight wipe.
        public float onsetJitter = 0.12f;

        private float[] onsets;
        private MaterialPropertyBlock block;
        // URP/Lit's albedo is _BaseColor. Setting "_Color" here would silently do nothing.
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private void Awake()
        {
            block = new MaterialPropertyBlock();
            BuildOnsets();
            SetPowered(0f);
        }

        // Ordered by height so the room fills upward. Computed once: the panels never move, and a
        // stable order means the boot looks the same every iteration, which a loop should.
        private void BuildOnsets()
        {
            if (panels == null) { onsets = new float[0]; return; }

            onsets = new float[panels.Length];
            float minY = float.MaxValue, maxY = float.MinValue;
            foreach (Renderer r in panels)
            {
                if (r == null) continue;
                minY = Mathf.Min(minY, r.bounds.center.y);
                maxY = Mathf.Max(maxY, r.bounds.center.y);
            }

            float span = Mathf.Max(0.001f, maxY - minY);
            Random.State previous = Random.state;
            // Fixed seed: the scatter should be arbitrary but identical on every run.
            Random.InitState(20260810);
            for (int i = 0; i < panels.Length; i++)
            {
                if (panels[i] == null) continue;
                float height = (panels[i].bounds.center.y - minY) / span;
                onsets[i] = Mathf.Clamp01(height * (1f - panelFade) + Random.Range(-onsetJitter, onsetJitter));
            }
            Random.state = previous;
        }

        // t = 0 fully dark, 1 fully lit.
        public void SetPowered(float t)
        {
            if (panels == null || onsets == null) return;

            for (int i = 0; i < panels.Length; i++)
            {
                if (panels[i] == null) continue;

                float k = panelFade > 0f
                    ? Mathf.Clamp01((t - onsets[i]) / panelFade)
                    : (t >= onsets[i] ? 1f : 0f);

                panels[i].GetPropertyBlock(block);
                block.SetColor(BaseColorId, Color.Lerp(offColor, onColor, Mathf.SmoothStep(0f, 1f, k)));
                panels[i].SetPropertyBlock(block);
            }
        }

        public void PowerDown()
        {
            StopAllCoroutines();
            SetPowered(0f);
        }

        public void PowerUp()
        {
            StopAllCoroutines();
            StartCoroutine(PowerUpRoutine());
        }

        private IEnumerator PowerUpRoutine()
        {
            float elapsed = 0f;
            while (elapsed < sweepDuration)
            {
                elapsed += Time.deltaTime;
                SetPowered(elapsed / sweepDuration);
                yield return null;
            }
            SetPowered(1f);
        }
    }
}
