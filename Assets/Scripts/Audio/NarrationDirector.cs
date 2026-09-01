using System.Collections.Generic;
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

        // ONE SET OF LINES PER LANGUAGE, both wired into the scene, one picked at runtime.
        //
        // The alternative was loading clips by path when the language changes, which would have meant
        // a `Resources` folder for the voice and a load stall at the moment of the switch. Both sets
        // are 45 clips of 22 kHz mono - the whole Korean folder is a couple of megabytes - so holding
        // both costs less than the machinery to avoid holding both.
        [System.Serializable]
        public class VoiceSet
        {
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

            // The line room3-2N's cube buys. Not a bigger `cycleBrokenLine` - that one is one cycle
            // failing and the way out opening, which is the machine losing a round. This is every
            // cycle at once and a threat attached, which is the machine losing patience.
            public AudioClip allCyclesBrokenLine;
            // Spoken the first time the player reaches Room3, alongside the same instruction lighting
            // up on all four of its walls.
            public AudioClip manualTerminationLine;

            // **THE PIECES THE END-OF-RUN REPORT IS BUILT OUT OF.** Nothing else in this game says a
            // number the writer did not know in advance; the evaluation says four of them per run
            // and cannot know any. See `AnnounceCycleResult`.
            //
            // Indexed BY VALUE, not packed: 0-19 sit at their own index and the tens at 20, 30 ... 90,
            // so `numbers[n]` is the clip for n whenever there is one. The gaps in between are null
            // and never asked for - `Number` decomposes anything past nineteen into a ten and a unit.
            public AudioClip[] numbers;
            public AudioClip cycleWord;
            public AudioClip iterationsWord;
            public AudioClip minutesWord;
            public AudioClip totalWord;
        }

        public VoiceSet english;
        public VoiceSet korean;

        // HOW MUCH THE HORN RINGS, PER LANGUAGE (2026-08-21, by request: the Korean PA was too
        // reverberant). The tannoy chain is otherwise shared - see `SceneBuilder.AddTannoyFilters` -
        // and only these three values move.
        //
        // **The band-limiting is what makes it a PA, not the tail.** `docs/audio.md` records that
        // finding: high-pass 340 / low-pass 3600 is what the ear reads as "coming out of a speaker",
        // and the echo and reverb are the room around it. So the tail can be cut for one language
        // without either of them stopping sounding like the same announcer.
        //
        // Korean needs less of it because it puts more syllables in the same second - the countdown
        // digits are single syllables and the sentences are dense - and a 105ms slap with a 2.1s tail
        // under that is a smear where under English it is a room. The echo DELAY is not touched: it
        // is the round trip across a room this size, and the room is the same room.
        [System.Serializable]
        public class TannoyTrim
        {
            public float echoWetMix;
            public float reverbDecayTime;
            public float reverbLevel;
        }

        public AudioEchoFilter voiceEcho;
        public AudioReverbFilter voiceReverb;
        public TannoyTrim englishTannoy;
        public TannoyTrim koreanTannoy;

        // Applied on wake and again if the language moves under a live scene. It cannot today - the
        // picker is on the title screen and this lives in the game scene - but a filter left on the
        // other language's settings is silent and would be found by ear months later.
        private void Awake() => ApplyTannoy();
        private void OnEnable() => Loc.Changed += ApplyTannoy;
        private void OnDisable() => Loc.Changed -= ApplyTannoy;

        private void ApplyTannoy()
        {
            TannoyTrim trim = GameSettings.Language == GameLanguage.Korean && koreanTannoy != null
                ? koreanTannoy
                : englishTannoy;
            if (trim == null) return;

            if (voiceEcho != null) voiceEcho.wetMix = trim.echoWetMix;
            if (voiceReverb != null)
            {
                voiceReverb.decayTime = trim.reverbDecayTime;
                voiceReverb.reverbLevel = trim.reverbLevel;
            }
        }

        // The chime is not in a `VoiceSet`: it is two notes, and two notes are the same two notes in
        // every language.
        public AudioClip announcementChime;

        // **RESOLVED PER CALL, NOT CACHED IN Awake.** The language can be changed from the title
        // screen while this object already exists in a loaded scene, and a cached set would keep
        // announcing in the language the run started in. It is a field read and a null check.
        //
        // Korean falls back to English rather than going silent, so a build whose `Voice/ko` folder
        // was never generated still has a talking PA - the same rule `Loc` follows for missing
        // strings, and for the same reason: half-translated should look unfinished, not broken.
        private VoiceSet Lines
        {
            get
            {
                if (GameSettings.Language == GameLanguage.Korean
                    && korean != null && korean.iterationGenericLine != null) return korean;
                return english;
            }
        }

        // No chime in front of this one. It fires at the top of every iteration, which is the one
        // announcement the player will hear hundreds of times, and a two-note ding ahead of it made
        // the loop's most repeated moment its most decorated. The line opens the cycle by itself.
        public void AnnounceIteration(int number)
        {
            bool haveLine = Lines.iterationLines != null && number >= 1 && number <= Lines.iterationLines.Length;
            Speak(haveLine ? Lines.iterationLines[number - 1] : Lines.iterationGenericLine);
        }

        public void AnnounceTenSeconds()
        {
            Chime();
            Speak(Lines.tenSecondsLine);
        }

        // secondsRemaining runs 9 down to 1. No chime - the digits come fast enough that one in
        // front of each turns the countdown into a rattle.
        public void AnnounceCountdown(int secondsRemaining)
        {
            if (Lines.countdownLines == null) return;
            int index = 9 - secondsRemaining;
            if (index < 0 || index >= Lines.countdownLines.Length) return;
            Speak(Lines.countdownLines[index]);
        }

        public void AnnounceNewCycle()
        {
            Speak(Lines.newCycleLine);
        }

        public void AnnounceCycleTerminated()
        {
            Chime();
            Speak(Lines.cycleTerminatedLine);
        }

        // "Containment failure. Cycle broken." Chimed, because it is the most important thing the
        // facility ever says and the only announcement a run hears exactly once. The wording is
        // deliberately built from the vocabulary the player already knows - every iteration has
        // opened with a cycle being initialized and some have ended with one terminated, so a cycle
        // being *broken* lands as the same voice admitting the machine failed.
        public void AnnounceCycleBroken()
        {
            Chime();
            Speak(Lines.cycleBrokenLine);
        }

        // "All cycles have been destroyed. You will pay the price for destroying them." Chimed, like
        // the line above and for the same reason - it is heard once in a run, if ever.
        //
        // **THE ONLY LINE IN THE GAME THAT THREATENS THE PLAYER.** Everything this voice has said up
        // to here is procedure: cycles initialized, terminated, ten seconds remaining. It reads the
        // machine out loud and never addresses anybody. Breaking that is the whole point of this one,
        // so it must not be softened into more procedure - the second sentence is second person and
        // stays that way.
        public void AnnounceAllCyclesBroken()
        {
            Chime();
            Speak(Lines.allCyclesBrokenLine);
        }

        // "Manual termination available. Hold N to end the cycle." Chimed, because it is an
        // announcement rather than part of the loop's own patter, and because it lands mid-walk
        // when the player is not expecting the PA to say anything.
        //
        // "Termination", not "skip": that is the word this voice has already used for the same act
        // (see `cycleTerminatedLine`), and the facility should not start speaking the player's
        // language three rooms in.
        public void AnnounceManualTermination()
        {
            Chime();
            Speak(Lines.manualTerminationLine);
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

        // **THE FACILITY CLEARING ITS THROAT, WITH NOTHING AFTER IT.**
        //
        // Used when the evaluation starts printing on room3-2N's walls. Every important line this
        // voice has ever said is preceded by this sound, so on its own it means *the building is
        // about to tell you something* - which is exactly what a wall full of type is.
        //
        // A chime rather than a spoken line because there is no clip for one, and inventing a line
        // here would mean either a silent fixture or a borrowed sentence that says the wrong thing.
        // The words are on the wall; this is only what makes the player look up.
        public void Attention() => Chime();

        // ======================================================== THE REPORT, READ ALOUD
        //
        // **THE FACILITY SAYS WHAT THE WALLS ARE PRINTING** (2026-09-01, by request). One call per
        // cycle as its row appears, so the voice and the wall arrive together rather than the voice
        // summarising something already finished.
        //
        // "CYCLE ONE, NINE ITERATIONS, FOUR MINUTES" is five clips played back to back. It is
        // assembled rather than pre-rendered because the numbers are the run's, and a facility that
        // reads out a figure it could not have known before the player produced it is the only kind
        // of announcement worth making here.
        //
        // MINUTES ONLY, rounded. The wall carries the seconds; a PA that reads "four minutes and
        // twenty seconds point six" is a PA nobody listens to the end of.
        public void AnnounceCycleResult(int cycle, int iterations, float seconds)
        {
            var line = new List<AudioClip> { Lines.cycleWord };
            Number(cycle, line);
            Number(iterations, line);
            line.Add(Lines.iterationsWord);
            Number(Mathf.Max(1, Mathf.RoundToInt(seconds / 60f)), line);
            line.Add(Lines.minutesWord);
            SpeakSequence(line);
        }

        public void AnnounceTotalResult(int iterations, float seconds)
        {
            var line = new List<AudioClip> { Lines.totalWord };
            Number(iterations, line);
            line.Add(Lines.iterationsWord);
            Number(Mathf.Max(1, Mathf.RoundToInt(seconds / 60f)), line);
            line.Add(Lines.minutesWord);
            SpeakSequence(line);
        }

        // A number as clips. 0-19 are whole words in both languages, because "thirteen" is not "ten
        // three" and 십삼 assembled out of 십 and 삼 sounds like spelling rather than speaking. Past
        // that both languages are regular, so a ten and a unit covers everything to 99.
        //
        // Anything larger is clamped rather than dropped: a run of a hundred iterations is a run this
        // should still be able to describe, and "ninety nine" is a better failure than silence.
        private void Number(int value, List<AudioClip> into)
        {
            if (Lines.numbers == null) return;
            value = Mathf.Clamp(value, 0, 99);

            if (value < 20) { Add(into, value); return; }

            Add(into, value / 10 * 10);
            if (value % 10 != 0) Add(into, value % 10);
        }

        private void Add(List<AudioClip> into, int index)
        {
            if (index >= 0 && index < Lines.numbers.Length && Lines.numbers[index] != null)
                into.Add(Lines.numbers[index]);
        }

        // How long to leave between the clips of an assembled line. Short - these are the words of
        // one sentence, not separate announcements - and the clips are rendered without a full stop
        // so the synthesizer does not put its own pause on the end of each.
        public float clipGap = 0.06f;

        // **PLAYED IN SEQUENCE, WHICH `Speak` CANNOT DO.** That method deliberately STOPS whatever is
        // talking and starts the new clip, because announcements replace each other - a countdown
        // that stacked would slur. An assembled sentence is the opposite case: five clips that are
        // one utterance, and each has to finish.
        //
        // On `EndingClock`, so it stops with a pause rather than talking over a frozen game.
        private Coroutine speaking;

        private void SpeakSequence(List<AudioClip> clips)
        {
            if (voiceSource == null || clips == null || clips.Count == 0) return;
            if (speaking != null) StopCoroutine(speaking);
            speaking = StartCoroutine(PlayInOrder(clips));
        }

        private System.Collections.IEnumerator PlayInOrder(List<AudioClip> clips)
        {
            foreach (AudioClip clip in clips)
            {
                if (clip == null) continue;

                voiceSource.Stop();
                voiceSource.clip = clip;
                voiceSource.Play();

                float wait = clip.length + clipGap;
                float t = 0f;
                while (t < wait)
                {
                    t += EndingClock.Delta;
                    yield return null;
                }
            }
            speaking = null;
        }

        private void Chime()
        {
            if (chimeSource != null && announcementChime != null)
                chimeSource.PlayOneShot(announcementChime);
        }
    }
}
