using System.Collections;
using UnityEngine;

namespace IterationRoom
{
    public class Door : MonoBehaviour
    {
        public Transform doorPanel;
        public Vector3 openLocalOffset = new Vector3(0f, 2.2f, 0f);
        public float openDuration = 1.0f;
        public Renderer indicatorRenderer;
        public Color closedColor = Color.red;
        public Color openColor = new Color(0.2f, 1f, 0.4f);

        public AudioSource audioSource;
        public AudioClip openClip;

        public bool IsOpen { get; private set; }
        private Vector3 closedLocalPos;

        private void Awake()
        {
            if (doorPanel != null) closedLocalPos = doorPanel.localPosition;
            SetIndicator(closedColor);
        }

        public void Open()
        {
            if (IsOpen) return;
            IsOpen = true;
            StopAllCoroutines();
            StartCoroutine(AnimateOpen());
            SetIndicator(openColor);

            if (audioSource != null && openClip != null) audioSource.PlayOneShot(openClip);
        }

        // Snaps the door back shut. The loop calls this at the top of every iteration, while the
        // eyelids are still closed, so the reset is never seen. Without it the door is world state
        // the loop forgets to rewind: IsOpen stays true, Open() early-outs on it, and every
        // iteration after the first solve begins with the door already standing open.
        //
        // Deliberately silent: this is the loop rewinding world state behind a black screen, not
        // the door being shut. A sound here would draw attention to the seam.
        public void Close()
        {
            StopAllCoroutines();
            IsOpen = false;
            if (doorPanel != null) doorPanel.localPosition = closedLocalPos;
            SetIndicator(closedColor);
        }

        private IEnumerator AnimateOpen()
        {
            if (doorPanel == null) yield break;
            Vector3 start = doorPanel.localPosition;
            Vector3 end = closedLocalPos + openLocalOffset;
            float t = 0f;
            while (t < openDuration)
            {
                t += Time.deltaTime;
                doorPanel.localPosition = Vector3.Lerp(start, end, t / openDuration);
                yield return null;
            }
            doorPanel.localPosition = end;
        }

        private void SetIndicator(Color color)
        {
            if (indicatorRenderer != null) indicatorRenderer.material.color = color;
        }
    }
}
