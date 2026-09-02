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

            // **THE REPORT, AS THREE PHRASES** (2026-09-02). It was six word clips and five joins
            // - "Cycle", "One", "Nine", "iterations", "Four", "minutes" - and it sounded like a list
            // being read out, because every one of those words was synthesised ALONE and a word on
            // its own is read flat and falling. No gap length fixes that; the fault is inside each
            // clip. See the note in `Tools/generate_narration.py`.
            //
            // Now: "Cycle one," + "nine iterations," + "four minutes." Each is a phrase the
            // synthesizer read AS a phrase, and the commas are load-bearing - a clause ending in one
            // is read as continuing, so the three arrive as one sentence.
            //
            // Indexed BY VALUE with element 0 unused, so `cycleLines[3]` is cycle three and there is
            // no arithmetic to get wrong at four in the morning.
            public AudioClip[] cycleLines;
            public AudioClip[] iterationCountLines;
            public AudioClip[] minuteLines;

            // "Total," - the same shape as a `cycleLines` entry, for the row that is not a cycle.
            public AudioClip totalLine;

            public AudioClip transportCalledLine;

            // What it says on the way out, in order. See `EndingDeparture.NarrateTheRide`.
            public AudioClip[] rideLines;
        }

        // **ONE SET, AND IT IS ENGLISH IN EVERY LANGUAGE** (2026-09-02, by request). There was a
        // Korean set beside this; it is gone, along with `korean`, `koreanTannoy` and the per-call
        // fallback that chose between them. The PA is the facility talking to ITSELF - the same
        // category as the signage, which has always stayed English (see `Loc`) - and what a player
        // who does not speak it gets is a SUBTITLE, which is how a foreign announcement is handled
        // anywhere else. `PaSubtitle` is that, and every `Announce*` below feeds it.
        //
        // The Korean clips were generated and played and the verdict was that they were awkward.
        // That is the honest reason this changed; the design argument above is why the change is an
        // improvement rather than a retreat.
        public VoiceSet english;

        // Where the words go. Optional - a scene without one has a talking PA and no captions,
        // which is what every build before this was.
        public PaSubtitle subtitle;

        // HOW MUCH THE HORN RINGS. It was per LANGUAGE until 2026-09-02, because Korean puts more
        // syllables in the same second and a 105ms slap with a 2.1s tail under that was a smear; with
        // the Korean voice retired there is one voice and one trim.
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
        public TannoyTrim tannoy;

        // Applied on wake and again if the language moves under a live scene. It cannot today - the
        // picker is on the title screen and this lives in the game scene - but a filter left on the
        // other language's settings is silent and would be found by ear months later.
        private void Awake() => ApplyTannoy();
        private void OnEnable() => Loc.Changed += ApplyTannoy;
        private void OnDisable() => Loc.Changed -= ApplyTannoy;

        private void ApplyTannoy()
        {
            TannoyTrim trim = tannoy;
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

        // One set now - see the note on `english`. Kept as a property rather than flattened to the
        // field so every call site below reads the same as it did.
        private VoiceSet Lines => english;

        // The caption for whatever is being said, in the player's language. Called by every
        // `Announce*`, right beside the clip it belongs to, so the two cannot drift apart.
        //
        // `Loc.Get` falls through to English for a missing Korean key, so a half-translated build
        // captions in English rather than showing a key - the same rule the rest of the UI follows.
        private void Caption(string key, params object[] args)
        {
            if (subtitle == null || string.IsNullOrEmpty(key)) return;

            string line = Loc.Get(key);
            subtitle.Show(args != null && args.Length > 0 ? string.Format(line, args) : line);
        }

        // No chime in front of this one. It fires at the top of every iteration, which is the one
        // announcement the player will hear hundreds of times, and a two-note ding ahead of it made
        // the loop's most repeated moment its most decorated. The line opens the cycle by itself.
        public void AnnounceIteration(int number)
        {
            bool haveLine = Lines.iterationLines != null && number >= 1 && number <= Lines.iterationLines.Length;
            Speak(haveLine ? Lines.iterationLines[number - 1] : Lines.iterationGenericLine);
            // The number is in the caption whether or not there is a clip for it - the generic line
            // says "new iteration" because there is no recording of "thirty-one", but the wall knows
            // which one it is and so does the player.
            Caption(haveLine ? "pa.iteration" : "pa.iterationGeneric", number);
        }

        public void AnnounceTenSeconds()
        {
            Chime();
            Speak(Lines.tenSecondsLine);
            Caption("pa.tenSeconds");
        }

        // secondsRemaining runs 9 down to 1. No chime - the digits come fast enough that one in
        // front of each turns the countdown into a rattle.
        public void AnnounceCountdown(int secondsRemaining)
        {
            if (Lines.countdownLines == null) return;
            int index = 9 - secondsRemaining;
            if (index < 0 || index >= Lines.countdownLines.Length) return;
            // **NO CAPTION.** The countdown is nine digits a second apart and the clock is already on
            // screen counting them. A subtitle here would be a second clock, redrawn nine times, over
            // the most tense ten seconds of the loop.
            Speak(Lines.countdownLines[index]);
        }

        public void AnnounceNewCycle()
        {
            Speak(Lines.newCycleLine);
            Caption("pa.newCycle");
        }

        public void AnnounceCycleTerminated()
        {
            Chime();
            Speak(Lines.cycleTerminatedLine);
            Caption("pa.cycleTerminated");
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
            Caption("pa.cycleBroken");
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
            Caption("pa.allCyclesBroken");
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
            Caption("pa.manualTermination");
        }

        // Announcements replace each other instead of stacking. The countdown fires once a second
        // and PlayOneShot would leave the digits overlapping into a slur.
        private void Speak(AudioClip clip)
        {
            if (voiceSource == null || clip == null) return;
            // **AND IT CANCELS A SEQUENCE.** `Speak` has always stopped the source; what it did not do
            // was stop the coroutine feeding it, so an assembled line went on writing clip after clip
            // over whatever this was saying. That is the other half of the cutting-off.
            if (speaking != null) { StopCoroutine(speaking); speaking = null; }
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

        // **WHETHER THE VOICE IS STILL TALKING**, so a caller can wait for it instead of racing it.
        //
        // This is what the evaluation board needed and did not have: it fired a cycle's line and then
        // printed the next row on a timer, so a line that ran a shade long was cut off mid-word by
        // the next one. A tuned pause is a guess about clip lengths; this is the fact.
        public bool Speaking => speaking != null
                             || (voiceSource != null && voiceSource.isPlaying);

        // One of the ride lines, by index. Out of range is silence rather than an error - the ride is
        // paced by where the car IS, so asking for a line that does not exist means the path grew.
        public void AnnounceRideLine(int index)
        {
            if (Lines.rideLines == null || index < 0 || index >= Lines.rideLines.Length) return;

            Chime();
            SpeakSequence(new List<AudioClip> { Lines.rideLines[index] });
            Caption("pa.ride" + index);
        }

        // Chimed, because it is the last thing the facility says to the subject and the one piece of
        // it that is an instruction rather than a figure.
        public void AnnounceTransportCalled()
        {
            Chime();
            Speak(Lines.transportCalledLine);
            Caption("pa.transportCalled");
        }

        // ======================================================== THE REPORT, READ ALOUD
        //
        // **THE FACILITY SAYS WHAT THE WALLS ARE PRINTING** (2026-09-01, by request). One call per
        // cycle as its row appears, so the voice and the wall arrive together rather than the voice
        // summarising something already finished.
        //
        // "Cycle one, nine iterations, four minutes" is THREE clips played back to back - see the
        // note on `VoiceSet.cycleLines` for why three and not six. It is assembled rather than
        // pre-rendered whole because the numbers are the run's, and a facility that reads out a
        // figure it could not have known before the player produced it is the only kind of
        // announcement worth making here.
        //
        // MINUTES ONLY, rounded. The wall carries the seconds; a PA that reads "four minutes and
        // twenty seconds point six" is a PA nobody listens to the end of.
        //
        // **~~PHRASED, AND THE CLIPS TRIMMED~~ BOTH REVERTED 2026-09-01, by request: it came out
        // WORSE.** Every clip carries about 0.12s of lead and 0.83s of tail from the synthesizer, so
        // six of them back to back put a second of silence between every word. That was measured, and
        // cutting it looked like the obvious fix - along with a longer beat where a comma belongs.
        //
        // Played, it was worse. Which is the finding: a second between words is what makes this read
        // as a TANNOY reading a figure off a screen rather than a person saying a sentence, and the
        // padding SAPI adds is doing work nobody chose but everybody had got used to. The delivery
        // was never the problem the measurement said it was.
        //
        // Both halves are one revert and they go together - trimmed clips at the old uniform spacing
        // would be the same sentence spoken at a gallop. `git log` has the code if it is ever wanted.
        public void AnnounceCycleResult(int cycle, int iterations, float seconds)
        {
            int minutes = Mathf.Max(1, Mathf.RoundToInt(seconds / 60f));

            SpeakSequence(new List<AudioClip>
            {
                Pick(Lines.cycleLines, cycle),
                Pick(Lines.iterationCountLines, iterations),
                Pick(Lines.minuteLines, minutes),
            });

            // The same figures the wall is printing, in the player's language. Rounded to minutes
            // like the speech, not like the board - the board has room for the seconds and this does
            // not, and a caption that disagreed with the voice over it would read as a mistake.
            Caption("pa.cycleResult", cycle, iterations, minutes);
        }

        public void AnnounceTotalResult(int iterations, float seconds)
        {
            int minutes = Mathf.Max(1, Mathf.RoundToInt(seconds / 60f));

            SpeakSequence(new List<AudioClip>
            {
                Lines.totalLine,
                Pick(Lines.iterationCountLines, iterations),
                Pick(Lines.minuteLines, minutes),
            });

            Caption("pa.totalResult", iterations, minutes);
        }

        // One phrase by the number it names. **CLAMPED, NOT DROPPED**: a run past the highest
        // recording is a run this should still be able to describe, and "ninety-nine iterations" is a
        // better failure than a gap in the middle of the sentence. Null if the set is short, which
        // `PlayInOrder` skips - so a half-generated folder shortens the line instead of stalling it.
        private static AudioClip Pick(AudioClip[] clips, int value)
        {
            if (clips == null || clips.Length <= 1) return null;
            return clips[Mathf.Clamp(value, 1, clips.Length - 1)];
        }

        // How long to leave between the clips of an assembled line. Short - these are the words of
        // one sentence, not separate announcements - and the clips are rendered without a full stop
        // so the synthesizer does not put its own pause on the end of each.
        // **0.06 -> 0.14, because the pieces are phrases now** (2026-09-02). Six words at 0.06 was
        // as tight as a single breath, which is right for words; three clauses want the beat a comma
        // gets. The clips already end in commas - this is the pause those commas ask for.
        public float clipGap = 0.14f;

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
