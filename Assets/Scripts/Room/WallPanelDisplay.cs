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

        // How hard the panels blow out as a cycle collapses. Well past 1 so it clears the
        // deliberately high bloom threshold and the whole room floods.
        public float flareEmission = 7f;

        // THE BREAK, at the very end of the run: every panel a different saturated hue, resampled
        // several times a second, with a scatter of dead black cells through it. A dropped-signal
        // pattern rather than anything the facility would ever choose to display - which is the
        // only way a room built entirely out of working displays can show that it has stopped
        // working. See FinalRoomSequence.
        //
        // Deliberately NOT the power-down. That is the loop's signature, the cell being switched
        // off before being switched back on, and using it here would say the cycle continued.
        public float glitchEmission = 3.2f;
        // Resamples per second. Low enough to read as discrete broken frames rather than as noise;
        // at 60 it greys out into an average and the colour stops being visible at all.
        public float glitchSpeed = 11f;
        // Fraction of cells black at FULL severity. The dark is what makes it read as a signal
        // failing rather than as a colourful screensaver. Scaled by the envelope, so the first
        // stutters are colour and the late ones are half holes.
        public float glitchDropout = 0.16f;

        // How long the room takes to go. It does NOT switch on - it degrades, and the two look
        // nothing alike: a wall that snaps to full colour reads as an effect starting, where one
        // that stutters, recovers a little worse each time and eventually stops recovering reads as
        // equipment failing. 6.5 against the ten seconds the final room holds, so the last few are
        // spent fully gone rather than still on the way.
        public float glitchOnset = 6.5f;
        // Burst slots per second. THE SLOT LENGTH IS WHAT MAKES A BURST A STUTTER - at 7 a single
        // lit slot is 0.14s, about the length of a dropped frame. What grows over the onset is the
        // duty cycle, not the burst length: one slot in sixteen at the start, nine in ten by the
        // end, so the same effect reads first as interference and then as failure without ever
        // changing character.
        public float glitchBurstRate = 7f;
        public float glitchBurstDutyStart = 0.06f;
        public float glitchBurstDutyEnd = 0.9f;

        private float[] onsets;
        private float powered;
        private float flare;
        private float glitch;
        // How far gone the room is, 0..1, separate from `glitch` because that one flickers with
        // the bursts and the dropout has to follow the SLOW curve rather than the stutter.
        private float glitchEnvelope;

        // Where the collapse had got to. The ending ramps down from whatever this is rather than
        // from 1, so escaping at t=52 releases a half-built flare instead of snapping it to full
        // first and then letting go.
        public float Flare => flare;
        private MaterialPropertyBlock block;
        // URP/Lit's albedo is _BaseColor. Setting "_Color" here would silently do nothing.
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        // Lazily, not in Awake: a MaterialPropertyBlock is not serialized, so recompiling a script
        // while play mode is running nulls it without Awake ever running again, and every
        // SetPropertyBlock call after that throws. Editor-only, but that is exactly the moment it
        // bites - mid-session, while tuning something.
        private MaterialPropertyBlock Block => block ??= new MaterialPropertyBlock();

        private void Awake()
        {
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

            // THE INVARIANT: every onset must be <= 1 - panelFade, because PowerUpRoutine stops at
            // powered = 1 and a panel only finishes when (powered - onset) / panelFade reaches 1.
            //
            // Getting this wrong does not look like a bug, which is why it shipped. The formula was
            // height * (1 - panelFade) + jitter, so the top row could land as high as 0.90 against
            // a 0.78 ceiling - those panels froze at k = 0.45, i.e. albedo 0.506, and simply stayed
            // MID GREY for the rest of the run. Half of the top row's 66 panels, in a room whose
            // whole point is that it is white.
            //
            // The jitter is subtracted from the ramp rather than clamped off the top, so the
            // scatter survives intact at the ceiling instead of half the top row landing on exactly
            // the same onset.
            float ceiling = Mathf.Max(0f, 1f - panelFade);
            float ramp = Mathf.Max(0f, ceiling - onsetJitter);

            Random.State previous = Random.state;
            // Fixed seed: the scatter should be arbitrary but identical on every run.
            Random.InitState(20260810);
            for (int i = 0; i < panels.Length; i++)
            {
                if (panels[i] == null) continue;
                float height = (panels[i].bounds.center.y - minY) / span;
                onsets[i] = Mathf.Clamp(height * ramp + Random.Range(-onsetJitter, onsetJitter), 0f, ceiling);
            }
            Random.state = previous;
        }

        // t = 0 fully dark, 1 fully lit.
        public void SetPowered(float t)
        {
            powered = t;
            Apply();
        }

        // 0 = normal, 1 = blown out. Ramped over the last seconds of a cycle: the displays are the
        // facility tearing the room down, and they go far brighter than "on" ever is.
        public void SetFlare(float t)
        {
            flare = Mathf.Clamp01(t);
            Apply();
        }

        private void Apply()
        {
            if (panels == null || onsets == null) return;

            Color flareColor = Color.white * (flare * flare * flareEmission);
            // One step index for the whole wall, so every panel resamples on the same frame. Per
            // panel it would smear into continuous noise and stop reading as broken FRAMES.
            int step = glitch > 0f ? Mathf.FloorToInt(Time.unscaledTime * glitchSpeed) : 0;

            for (int i = 0; i < panels.Length; i++)
            {
                if (panels[i] == null) continue;

                float k = panelFade > 0f
                    ? Mathf.Clamp01((powered - onsets[i]) / panelFade)
                    : (powered >= onsets[i] ? 1f : 0f);

                Color albedo = Color.Lerp(offColor, onColor, Mathf.SmoothStep(0f, 1f, k));
                // The flare overrides the boot state entirely - a panel still dark from the sweep
                // shouldn't stay dark while the room is blowing out around it.
                albedo = Color.Lerp(albedo, Color.white, flare);
                Color emission = flareColor;

                if (glitch > 0f)
                {
                    // Full saturation and value: this is a signal, not a light, and a washed-out
                    // hue reads as a tinted wall rather than as a broken screen.
                    Color bar = Color.HSVToRGB(Hash(i, step), 1f, 1f);
                    bool dead = Hash(i + 977, step) < glitchDropout * glitchEnvelope;
                    albedo = Color.Lerp(albedo, dead ? Color.black : bar, glitch);
                    emission = Color.Lerp(emission, dead ? Color.black : bar * glitchEmission, glitch);
                }

                MaterialPropertyBlock b = Block;
                panels[i].GetPropertyBlock(b);
                b.SetColor(BaseColorId, albedo);
                b.SetColor(EmissionId, emission);
                panels[i].SetPropertyBlock(b);
            }
        }

        // Integer hash rather than Random, and that is not a micro-optimisation: BuildOnsets is
        // careful to save and restore Random.state so the panel scatter is identical every run,
        // and pulling thousands of samples a second out of the global generator here would undo
        // exactly what that care is for.
        private static float Hash(int a, int b)
        {
            uint h = (uint)(a * 73856093) ^ (uint)(b * 19349663);
            h ^= h >> 13;
            h *= 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFFu) / 16777216f;
        }

        // Starts the break and never stops it. There is nothing after this - EndingSequence's
        // scrim comes up over a room still coming apart, and the scene is unloaded from under it.
        public void BeginGlitch()
        {
            StopAllCoroutines();
            StartCoroutine(GlitchRoutine());
        }

        private IEnumerator GlitchRoutine()
        {
            float t = 0f;
            while (true)
            {
                // Unscaled: the ending must not be freezable, and Apply has to run every frame or
                // the resample never lands on screen.
                t += Time.unscaledDeltaTime;

                // Squared, so the first seconds are a display that stutters and the last are one
                // that has stopped being a display. Linear spends too much of the onset in a
                // half-broken middle that reads as neither.
                float k = glitchOnset > 0f ? Mathf.Clamp01(t / glitchOnset) : 1f;
                glitchEnvelope = k * k;

                // One hash per slot, so a burst is a hard cut in and out rather than a fade -
                // interference does not ease.
                int slot = Mathf.FloorToInt(t * glitchBurstRate);
                bool burst = Hash(9001, slot) < Mathf.Lerp(glitchBurstDutyStart, glitchBurstDutyEnd, k);

                // BETWEEN bursts the panels do not snap back to white - they sit at the baseline,
                // which climbs with the envelope. That is the getting-worse the bursts alone cannot
                // say: a stutter that always recovers perfectly is a display having a moment, not
                // one dying. A burst is strong from the FIRST one (0.75) - what grows is how often they come,
                // how bad the floor between them is, and how many cells drop out. A burst is never weaker
                // than the baseline, so the effect can never appear to improve.
                float baseline = glitchEnvelope * 0.5f;
                glitch = burst ? Mathf.Max(baseline, Mathf.Lerp(0.75f, 1f, k)) : baseline;

                Apply();
                yield return null;
            }
        }

        public void PowerDown()
        {
            StopAllCoroutines();
            flare = 0f;
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
