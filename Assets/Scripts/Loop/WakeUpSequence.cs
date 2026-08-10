using System.Collections;
using UnityEngine;

namespace IterationRoom
{
    // The transition between iterations: eyelids fall shut as time runs out, then open again on
    // the ceiling before the player sits up. Replaces what used to be an instant teleport.
    public class WakeUpSequence : MonoBehaviour
    {
        public RectTransform topLid;
        public RectTransform bottomLid;

        public float closeDuration = 0.9f;
        public float heldShutDuration = 0.7f;
        public float openDuration = 1.1f;
        public float lookAtCeilingDuration = 0.6f;
        public float riseDuration = 1.6f;

        // Eye height and pitch while lying on the bed. Negative pitch looks up in Unity.
        public float lyingEyeHeight = 0.5f;
        public float lyingPitch = -80f;

        public WallPanelDisplay wallPanels;

        public AudioSource bodySource;
        public AudioClip gaspClip;
        public AudioClip sheetRustleClip;

        private void Awake()
        {
            SetLids(0f);
        }

        public IEnumerator CloseEyes()
        {
            yield return Sweep(0f, 1f, closeDuration);
            yield return new WaitForSeconds(heldShutDuration);
        }

        public IEnumerator WakeUp(FirstPersonController player)
        {
            SetLids(1f);

            // Killed behind the closed lids, so the reset itself is never seen - the player only
            // ever witnesses the room coming back, never it going out.
            wallPanels?.PowerDown();

            if (player != null)
            {
                player.ControlEnabled = false;
                player.SetEyePose(lyingEyeHeight, lyingPitch);
            }

            // The gasp lands on the eyes starting to open, not on the rise - it's the reaction to
            // being back, which is the same beat the film puts between "New cycle initialized"
            // and the iteration announcement.
            PlayBody(gaspClip);

            // Open onto the ceiling first and hold there, so the player registers where they are
            // before anything moves.
            yield return Sweep(1f, 0f, openDuration);
            yield return new WaitForSeconds(lookAtCeilingDuration);

            // The room boots as the body sits up, so the two motions happen together rather than
            // queueing. The panels are dark for the whole eyes-open beat before this, which is what
            // makes the sweep land.
            wallPanels?.PowerUp();

            if (player != null)
            {
                PlayBody(sheetRustleClip);

                float elapsed = 0f;
                while (elapsed < riseDuration)
                {
                    elapsed += Time.deltaTime;
                    float k = Mathf.SmoothStep(0f, 1f, elapsed / riseDuration);
                    player.SetEyePose(
                        Mathf.Lerp(lyingEyeHeight, player.standingEyeHeight, k),
                        Mathf.Lerp(lyingPitch, 0f, k));
                    yield return null;
                }

                player.SetEyePose(player.standingEyeHeight, 0f);
                player.ControlEnabled = true;
            }
        }

        private void PlayBody(AudioClip clip)
        {
            if (bodySource != null && clip != null) bodySource.PlayOneShot(clip);
        }

        private IEnumerator Sweep(float from, float to, float duration)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                SetLids(Mathf.SmoothStep(from, to, elapsed / duration));
                yield return null;
            }
            SetLids(to);
        }

        // t = 0 fully open, t = 1 shut. Driven through the anchors rather than pixel offsets so it
        // covers the screen at any resolution.
        private void SetLids(float t)
        {
            t = Mathf.Clamp01(t);

            if (topLid != null)
            {
                topLid.anchorMin = new Vector2(0f, 1f - t * 0.5f);
                topLid.anchorMax = Vector2.one;
                topLid.offsetMin = Vector2.zero;
                topLid.offsetMax = Vector2.zero;
            }

            if (bottomLid != null)
            {
                bottomLid.anchorMin = Vector2.zero;
                bottomLid.anchorMax = new Vector2(1f, t * 0.5f);
                bottomLid.offsetMin = Vector2.zero;
                bottomLid.offsetMax = Vector2.zero;
            }
        }
    }
}
