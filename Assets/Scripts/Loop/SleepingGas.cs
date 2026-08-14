using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace IterationRoom
{
    // WHAT PUTS THE PLAYER OUT AT A CYCLE BOUNDARY, and the one rule about it is that it arrives with
    // no warning whatsoever.
    //
    // Not announced, not telegraphed, no prompt, no countdown. The player has dropped into a room they
    // have never seen, there is a bed in it, and before they can decide anything the room takes the
    // decision. That is the facility doing to them at the boundary exactly what the loop does to them
    // every sixty seconds - and the reason the eyelids can then close on the ordinary machinery is
    // that being taken is already the game's grammar.
    //
    // THIS IS NOT THE EYELIDS. `WakeUpSequence.CloseEyes` is the loop taking you and it runs after
    // this, unchanged. The gas is what makes a boundary read as the facility's decision rather than as
    // a level transition; the blink then says the familiar thing about what follows.
    //
    // ALL ON `unscaledDeltaTime`: no iteration is running, and a pause here must not leave the player
    // conscious in a room that has already decided otherwise.
    public class SleepingGas : MonoBehaviour
    {
        // A full-screen wash, held over the HUD. Authored transparent.
        public Image haze;
        public Color hazeColour = new Color(0.92f, 0.94f, 0.96f, 1f);

        // Deliberately unequal. The hiss and the first of the haze land together and fast - the point
        // is that it is already happening before it can be understood - and then it takes its time
        // closing over, because that part is the player losing rather than the room acting.
        public float onsetDuration = 0.35f;
        public float onsetOpacity = 0.35f;
        public float fillDuration = 2.6f;
        public float fillOpacity = 0.92f;

        public AudioSource audioSource;
        public AudioClip hissClip;

        public IEnumerator Administer()
        {
            if (audioSource != null && hissClip != null) audioSource.PlayOneShot(hissClip);

            yield return Wash(0f, onsetOpacity, onsetDuration);
            yield return Wash(onsetOpacity, fillOpacity, fillDuration);
        }

        // Cleared by whoever wakes the player, so the next cycle does not open behind a white sheet.
        public void Clear()
        {
            if (haze == null) return;
            Color c = hazeColour;
            c.a = 0f;
            haze.color = c;
        }

        private IEnumerator Wash(float from, float to, float duration)
        {
            if (haze == null)
            {
                yield return new WaitForSecondsRealtime(duration);
                yield break;
            }

            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                Color c = hazeColour;
                c.a = Mathf.Lerp(from, to, Mathf.Clamp01(t / duration));
                haze.color = c;
                yield return null;
            }

            Color end = hazeColour;
            end.a = to;
            haze.color = end;
        }
    }
}
