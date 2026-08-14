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
    // every sixty seconds.
    //
    // TWO HALVES, AND THEY ARE NOT THE SAME EVENT. The vapour is the room being gassed; the wash is
    // the player breathing it. The room fills FIRST, visibly, while the player still has their legs -
    // and only once it has reached them does the wash deepen and the body go. Running the two together
    // was the first version's mistake: gas that knocks you down the instant it appears is not a gas,
    // it is a cut to black with a hiss on it.
    //
    // ALL ON `unscaledDeltaTime`: no iteration is running, and a pause must not leave the player
    // conscious in a room that has already decided otherwise.
    public class SleepingGas : MonoBehaviour
    {
        // A full-screen wash, held over the HUD. Authored transparent.
        public Image haze;
        public Color hazeColour = new Color(0.92f, 0.94f, 0.96f, 1f);

        // THE ROOM FILLING. Four particle systems, one per wall, pouring in and down from the top.
        //
        // A lit slot along each wall was built for this first and taken out again - in a white room
        // four near-black strips are the most noticeable thing in it, permanently, in exchange for a
        // moment. A translucent box that grew downward replaced it and was worse: at zero height it
        // still caught a specular highlight, so the band came back. Both were trying to fake a volume
        // with a surface, and a surface always has an edge you can find. See
        // SceneBuilder.BuildGasEmitters.
        public ParticleSystem[] emitters;

        // How long the room has to itself before the player starts going under. Long enough to look
        // up, find where it is coming from, and watch it reach the middle of the room.
        public float fillDuration = 5.5f;
        // Barely anything while it fills. They can see it; they have not had much of it yet.
        public float fillOpacity = 0.16f;

        // And then it takes them.
        public float overcomeOpacity = 0.95f;

        public AudioSource audioSource;
        public AudioClip hissClip;

        // THE ROOM IS GASSED. Returns when the vapour has crossed the room, not when the player is
        // down - going down is Overcome, below, and the collapse it runs under.
        public IEnumerator Fill()
        {
            if (audioSource != null && hissClip != null) audioSource.PlayOneShot(hissClip);

            if (emitters != null)
                foreach (ParticleSystem ps in emitters)
                    if (ps != null) ps.Play();

            yield return Wash(0f, fillOpacity, fillDuration);
        }

        // THE PLAYER BREATHING IT. Started rather than yielded, so the wash deepens WHILE the body is
        // falling instead of before it - the two are one event and should not take turns.
        public void Overcome(float seconds)
        {
            StartCoroutine(Wash(fillOpacity, overcomeOpacity, Mathf.Max(0.1f, seconds)));
        }

        // Cleared by whoever wakes the player, so the next cycle does not open behind a white sheet -
        // and so the room it opens in is not still full of the gas that put them there.
        public void Clear()
        {
            StopAllCoroutines();
            SetHaze(0f);

            if (emitters == null) return;
            foreach (ParticleSystem ps in emitters)
            {
                if (ps == null) continue;
                // Cleared as well as stopped: stopping alone leaves whatever is already in the air to
                // finish its lifetime, and the player would wake into the tail of it.
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }

        private IEnumerator Wash(float from, float to, float duration)
        {
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                SetHaze(Mathf.Lerp(from, to, Mathf.Clamp01(t / duration)));
                yield return null;
            }
            SetHaze(to);
        }

        private void SetHaze(float alpha)
        {
            if (haze == null) return;
            Color c = hazeColour;
            c.a = alpha;
            haze.color = c;
        }
    }
}
