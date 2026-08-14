using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace IterationRoom
{
    public class IterationLabel : MonoBehaviour
    {
        public CanvasGroup canvasGroup;
        public Text label;

        // CYCLE N, on its OWN LINE ABOVE the iteration rather than beside it. Two reasons, and the
        // second is the one that decided it.
        //
        // It reads better - the cycle is which bed you are in and the iteration is which attempt, so
        // stacking them says "here, again" rather than running one number into another. And a single
        // line would have blown the width budget: every character is spaced out below, so
        // "CYCLE 2 - ITERATION 1" is about forty cells, and a fixed-width HUD label that silently
        // rewraps has bitten this project twice. Stacked, neither line is longer than the one this
        // label already rendered.
        public Text cycleLabel;

        public float holdDuration = 1.2f;
        public float fadeDuration = 0.5f;

        // Set upper case and spaced out, which is the whole of the styling and is deliberately done
        // here rather than in the font. uGUI's Text has no tracking control at all, so the spacing
        // has to be in the string - and a plain space between characters is exactly right in a
        // monospace face, where it lands as one full cell. The single space between the two words
        // becomes three, which keeps the word break readable.
        //
        // Uppercase because this is the facility labelling you, not a caption. "ITERATION 12" reads
        // as stencilled onto the room; "Iteration 12" reads as a subtitle.
        public bool spacedCaps = true;

        public void Show(int cycle, int iteration)
        {
            if (label != null) label.text = Style($"Iteration {iteration}");

            // CYCLE 1 SAYS NOTHING, the same way iteration 1 gets no reset announcement: there is
            // nothing to distinguish it from yet, and naming it would raise a question about cycles
            // before the player has any reason to have one. The line appears the first time it is
            // true that there has been more than one bed.
            if (cycleLabel != null)
            {
                bool show = cycle > 1;
                cycleLabel.enabled = show;
                if (show) cycleLabel.text = Style($"Cycle {cycle}");
            }

            StopAllCoroutines();
            StartCoroutine(FadeRoutine());
        }

        private string Style(string text) => spacedCaps ? Space(text.ToUpperInvariant()) : text;

        private static string Space(string text) => string.Join(" ", text.ToCharArray());

        private IEnumerator FadeRoutine()
        {
            if (canvasGroup == null) yield break;
            yield return Fade(0f, 1f, fadeDuration);
            yield return new WaitForSeconds(holdDuration);
            yield return Fade(1f, 0f, fadeDuration);
        }

        private IEnumerator Fade(float from, float to, float duration)
        {
            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                canvasGroup.alpha = Mathf.Lerp(from, to, t / duration);
                yield return null;
            }
            canvasGroup.alpha = to;
        }
    }
}
