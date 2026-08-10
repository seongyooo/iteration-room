using UnityEngine;

namespace IterationRoom
{
    // Room tone and the machinery around the loop boundary. Kept separate from NarrationDirector
    // because they are unrelated channels: the announcer must never be cut off by a music sting,
    // and the two get mixed and revised independently.
    public class RoomAmbience : MonoBehaviour
    {
        public AudioSource musicSource;
        public AudioSource machineSource;

        public AudioClip ominousLoop;
        public AudioClip pullIn;
        public AudioClip powerDown;

        private void Start()
        {
            if (musicSource == null || ominousLoop == null) return;
            musicSource.clip = ominousLoop;
            musicSource.loop = true;
            musicSource.Play();
        }

        // The loop boundary is two sounds, not one, and they are deliberately split across the
        // blackout: the pull starts while the player can still see, the shutdown lands once they
        // cannot. Together with the panels booting during the wake-up that gives the reset a shape -
        // taken, switched off, switched back on - rather than one undifferentiated noise.

        // As the eyelids begin to fall, so it is heard against the collapse rather than after it.
        public void PlayPullIn()
        {
            if (musicSource != null && pullIn != null) musicSource.PlayOneShot(pullIn);
        }

        // Under the black. Nothing to look at, which is the point - you only hear the room go out.
        public void PlayPowerDown()
        {
            if (machineSource != null && powerDown != null) machineSource.PlayOneShot(powerDown);
        }

        // The ending, and the only thing that ever stops the tone. It has been under every second
        // of every iteration, so its absence is the quietest and clearest signal that this cycle is
        // not turning over - which is why it fades rather than cutting. A cut reads as a sound
        // failing; a fade reads as the room being switched off around you.
        public void FadeOutTone(float seconds)
        {
            if (musicSource == null) return;
            StopAllCoroutines();
            StartCoroutine(FadeTone(seconds));
        }

        private System.Collections.IEnumerator FadeTone(float seconds)
        {
            float from = musicSource.volume;
            float t = 0f;
            while (t < seconds)
            {
                t += Time.deltaTime;
                musicSource.volume = Mathf.Lerp(from, 0f, seconds > 0f ? t / seconds : 1f);
                yield return null;
            }
            musicSource.volume = 0f;
            musicSource.Stop();
        }
    }
}
