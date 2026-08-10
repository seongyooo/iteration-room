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
        // The one line the facility only ever says once: the player got out.
        public AudioClip cycleBrokenLine;
        // Spoken the first time the player reaches Room3, alongside the same instruction lighting
        // up on all four of its walls.
        public AudioClip manualTerminationLine;
        public AudioClip announcementChime;

        // No chime in front of this one. It fires at the top of every iteration, which is the one
        // announcement the player will hear hundreds of times, and a two-note ding ahead of it made
        // the loop's most repeated moment its most decorated. The line opens the cycle by itself.
        public void AnnounceIteration(int number)
        {
            bool haveLine = iterationLines != null && number >= 1 && number <= iterationLines.Length;
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

        // "Containment failure. Cycle broken." Chimed, because it is the most important thing the
        // facility ever says and the only announcement a run hears exactly once. The wording is
        // deliberately built from the vocabulary the player already knows - every iteration has
        // opened with a cycle being initialized and some have ended with one terminated, so a cycle
        // being *broken* lands as the same voice admitting the machine failed.
        public void AnnounceCycleBroken()
        {
            Chime();
            Speak(cycleBrokenLine);
        }

        // "Manual termination available. Hold N to end the cycle." Chimed, because it is an
        // announcement rather than part of the loop's own patter, and because it lands mid-walk
        // when the player is not expecting the PA to say anything.
        //
        // "Termination", not "skip": that is the word this voice has already used for the same act
        // (see cycleTerminatedLine), and the facility should not start speaking the player's
        // language three rooms in.
        public void AnnounceManualTermination()
        {
            Chime();
            Speak(manualTerminationLine);
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
