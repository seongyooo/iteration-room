using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace IterationRoom
{
    public class IterationLabel : MonoBehaviour
    {
        public CanvasGroup canvasGroup;
        public Text label;
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

        public void ShowIteration(int number)
        {
            if (label != null)
            {
                string text = $"Iteration {number}";
                label.text = spacedCaps ? Space(text.ToUpperInvariant()) : text;
            }
            StopAllCoroutines();
            StartCoroutine(FadeRoutine());
        }

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
