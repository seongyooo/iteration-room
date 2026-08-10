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
        // Positional, parented to the nightstand, so the rattle comes from the props standing on
        // it rather than from nowhere.
        public AudioSource propRattleSource;

        public AudioClip ominousLoop;
        public AudioClip resetSting;
        public AudioClip machinesRev;
        public AudioClip propRattle;

        private void Start()
        {
            if (musicSource == null || ominousLoop == null) return;
            musicSource.clip = ominousLoop;
            musicSource.loop = true;
            musicSource.Play();
        }

        // Fired at the loop boundary, under the closed eyelids: the machines spin the room back
        // up, the glass on the nightstand rattles with them, and a sting cuts over the bed.
        public void PlayResetSting()
        {
            if (musicSource != null && resetSting != null) musicSource.PlayOneShot(resetSting);
            if (machineSource != null && machinesRev != null) machineSource.PlayOneShot(machinesRev);
            if (propRattleSource != null && propRattle != null) propRattleSource.PlayOneShot(propRattle);
        }
    }
}
