using UnityEngine;

namespace IterationRoom
{
    // The facility's PA announcer. Every iteration is announced on the same fixed schedule, so
    // this exposes discrete cues and lets LoopManager fire them rather than watching the clock
    // itself: the loop already owns ElapsedTime, and the announcer has to stay silent through the
    // wake-up sequence, which sits outside the timed window.
    public class NarrationDirector : MonoBehaviour
    {
        public AudioSource voiceSource;
        public AudioSource chimeSource;

        // "Iteration N, 60 seconds remaining.", iteration 1 at element 0.
        public AudioClip[] iterationLines;
        // Stands in once a run outlasts the recorded lines.
        public AudioClip iterationGenericLine;
        public AudioClip tenSecondsLine;
        // "Nine." down to "One.", nine at element 0.
        public AudioClip[] countdownLines;
        public AudioClip newCycleLine;
        // Spoken when the player ends a cycle themselves rather than running the clock out.
        public AudioClip cycleTerminatedLine;
        public AudioClip announcementChime;

        public void AnnounceIteration(int number)
        {
            bool haveLine = iterationLines != null && number >= 1 && number <= iterationLines.Length;
            Chime();
            Speak(haveLine ? iterationLines[number - 1] : iterationGenericLine);
        }

        public void AnnounceTenSeconds()
        {
            Chime();
            Speak(tenSecondsLine);
        }

        // secondsRemaining runs 9 down to 1. No chime - the digits come fast enough that one in
        // front of each turns the countdown into a rattle.
        public void AnnounceCountdown(int secondsRemaining)
        {
            if (countdownLines == null) return;
            int index = 9 - secondsRemaining;
            if (index < 0 || index >= countdownLines.Length) return;
            Speak(countdownLines[index]);
        }

        public void AnnounceNewCycle()
        {
            Speak(newCycleLine);
        }

        public void AnnounceCycleTerminated()
        {
            Chime();
            Speak(cycleTerminatedLine);
        }

        // Announcements replace each other instead of stacking. The countdown fires once a second
        // and PlayOneShot would leave the digits overlapping into a slur.
        private void Speak(AudioClip clip)
        {
            if (voiceSource == null || clip == null) return;
            voiceSource.Stop();
            voiceSource.clip = clip;
            voiceSource.Play();
        }

        private void Chime()
        {
            if (chimeSource != null && announcementChime != null)
                chimeSource.PlayOneShot(announcementChime);
        }
    }
}
