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

        // THE ROOM-SIDE HALF, and the reason it exists: a wash with no source is a screen effect, and
        // the player has to be able to see where this came from.
        //
        // A lit slot along each wall was built for this first and taken out again - in a white room
        // four near-black strips are the most noticeable thing in it, permanently, in exchange for a
        // moment. The plume says everything the slot said: it appears at the ceiling line and pours
        // down, so where it came from is not in question, and at rest there is nothing to see.
        //
        // Pivoted at the wall head, so scaling Y grows them downward. Driven 0..1 alongside the wash.
        public Transform[] plumes;
        public float plumeAlpha = 0.34f;

        public AudioSource audioSource;
        public AudioClip hissClip;

        public IEnumerator Administer()
        {
            if (audioSource != null && hissClip != null) audioSource.PlayOneShot(hissClip);

            yield return Wash(0f, onsetOpacity, onsetDuration, 0f, 0.25f);
            yield return Wash(onsetOpacity, fillOpacity, fillDuration, 0.25f, 1f);
        }

        // Cleared by whoever wakes the player, so the next cycle does not open behind a white sheet -
        // and so the room it opens in is not still full of the gas that put them there.
        public void Clear()
        {
            if (haze != null)
            {
                Color c = hazeColour;
                c.a = 0f;
                haze.color = c;
            }

            SetRoom(0f);
        }

        // The plumes, on one 0..1.
        private void SetRoom(float t)
        {
            if (plumes == null) return;
            foreach (Transform plume in plumes)
            {
                if (plume == null) continue;
                // Grows out of its slot rather than out of its own middle - the pivot is at the top.
                plume.localScale = new Vector3(1f, t, 1f);

                Renderer r = plume.GetComponentInChildren<Renderer>();
                if (r == null) continue;
                var block = new MaterialPropertyBlock();
                r.GetPropertyBlock(block);
                Color c = plumeColour;
                c.a = plumeAlpha * t;
                block.SetColor(BaseColorId, c);
                r.SetPropertyBlock(block);
            }
        }

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly Color plumeColour = new Color(0.93f, 0.95f, 0.97f, 1f);

        // roomFrom/roomTo run the plumes on their own curve, because the room fills FASTER
        // than the player goes under - the gas is visible in the air well before it has done anything.
        private IEnumerator Wash(float from, float to, float duration, float roomFrom, float roomTo)
        {
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                float u = Mathf.Clamp01(t / duration);

                if (haze != null)
                {
                    Color c = hazeColour;
                    c.a = Mathf.Lerp(from, to, u);
                    haze.color = c;
                }
                SetRoom(Mathf.Lerp(roomFrom, roomTo, u));
                yield return null;
            }

            if (haze != null)
            {
                Color end = hazeColour;
                end.a = to;
                haze.color = end;
            }
            SetRoom(roomTo);
        }
    }
}
