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

        public void ShowIteration(int number)
        {
            if (label != null) label.text = $"Iteration {number}";
            StopAllCoroutines();
            StartCoroutine(FadeRoutine());
        }

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
