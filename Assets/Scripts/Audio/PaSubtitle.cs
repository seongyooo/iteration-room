using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace IterationRoom
{
    // WHAT THE PA JUST SAID, IN THE PLAYER'S LANGUAGE.
    //
    // **THE VOICE IS ENGLISH IN EVERY LANGUAGE, AND THAT IS THE POINT** (2026-09-02, by request).
    // The PA is the facility talking to itself, not the game talking to the player - the same
    // category as `ROOM 2` over a doorway and `FIRE AXE` on a pictogram, all of which stay English
    // on purpose (see `Loc`). A building that switched to Korean because its subject speaks Korean
    // would be a building that knows who is in it and is trying to be helpful, which this one is not.
    //
    // So the audio is fixed and the TEXT is localised, which is how a foreign-language announcement
    // is subtitled anywhere else. It also retires the whole Korean voice set: one language of clips
    // to generate, one tannoy trim to tune, and no half-translated PA (`docs/audio.md`).
    //
    // **IT IS NOT A TRANSCRIPT.** Nothing here reads the audio; `NarrationDirector` hands over the
    // line as it fires the clip, so the two are one call and cannot drift apart. A line with no
    // subtitle key simply shows nothing rather than showing the key.
    //
    // **AND IT IS OFF IN ENGLISH** (2026-09-02, by request). This is a SUBTITLE - it exists because
    // the audio is in a language the player may not have picked, which is true for exactly one of
    // the two. An English player was being handed a written copy of a sentence they had just heard
    // in their own language, twice a minute, for the length of a run.
    //
    // The rule for a new line does not change: still `Caption("pa.<key>")` beside the clip, because
    // the Korean player still needs it and a missing key is silent in both. What changed is only
    // whether the finished text is drawn.
    public class PaSubtitle : MonoBehaviour
    {
        public CanvasGroup group;
        public Text label;

        // Who is talking, so this can wait for them to stop instead of guessing a duration. A clip's
        // length is knowable but an ASSEMBLED line's is not - the report's "cycle one, nine
        // iterations, four minutes" is six clips and a gap - and a subtitle that vanished halfway
        // through the sentence it is subtitling would be worse than none.
        public NarrationDirector narration;

        public float fadeIn = 0.12f;
        public float fadeOut = 0.45f;

        // Held after the voice stops, so the last word can be read rather than glimpsed.
        public float hold = 1.1f;

        // **AND A FLOOR, BECAUSE `Speaking` IS FALSE FOR A FRAME OR TWO AFTER `Play()`.** The source
        // has not started when the caller returns, so a subtitle that only waited on `Speaking` would
        // appear and vanish inside one frame. Also covers a missing clip: the text still gets read.
        public float minimum = 1.6f;

        // A ceiling on the wait, so a stuck source cannot leave a line on screen for the rest of the
        // run. Long enough that only a fault reaches it.
        public float maximum = 20f;

        private Coroutine showing;

        private void Awake()
        {
            if (group != null) group.alpha = 0f;
        }

        // The picker is on the title screen and in the pause menu, so a language CAN move while a
        // line is on screen. Switching to English mid-sentence should take the sentence with it.
        private void OnEnable() => Loc.Changed += OnLanguageChanged;
        private void OnDisable() => Loc.Changed -= OnLanguageChanged;

        private void OnLanguageChanged()
        {
            if (!Wanted) Clear();
        }

        // **WHETHER A SUBTITLE IS WANTED AT ALL** - see the note on the class. Asked per line rather
        // than cached, because the answer changes with a setting and this is one comparison.
        private static bool Wanted => Loc.Current != GameLanguage.English;

        // **ON THE ENDING'S CLOCK, NOT `Time.deltaTime`.** Half of what the PA says happens after the
        // loop has stopped - the report, the transport line, the five on the way up in the cable car -
        // where `Time.timeScale` is whatever the ending left it at. `EndingClock` is unscaled and
        // stops on a real pause, which is what every other ending-side timer in this game uses.
        public void Show(string text)
        {
            if (label == null || group == null) return;
            if (string.IsNullOrEmpty(text)) return;
            // Gated HERE rather than at `NarrationDirector.Caption`: the announcer's job is to say
            // what it is saying, and whether that gets drawn is this component's own question.
            if (!Wanted) return;

            if (showing != null) StopCoroutine(showing);
            showing = StartCoroutine(Run(text));
        }

        // Clears immediately, without a fade. For the loop boundary: the iteration line that was on
        // screen belongs to a cycle that has just ended.
        public void Clear()
        {
            if (showing != null) { StopCoroutine(showing); showing = null; }
            if (group != null) group.alpha = 0f;
        }

        private IEnumerator Run(string text)
        {
            label.text = text;

            yield return Fade(0f, 1f, fadeIn);

            // The voice, then the floor, then the hold - in that order, so a long line is followed to
            // its end and a short one is still readable.
            float t = 0f;
            while (t < maximum)
            {
                bool talking = narration != null && narration.Speaking;
                if (!talking && t >= minimum) break;
                t += EndingClock.Delta;
                yield return null;
            }

            float held = 0f;
            while (held < hold) { held += EndingClock.Delta; yield return null; }

            yield return Fade(1f, 0f, fadeOut);
            showing = null;
        }

        private IEnumerator Fade(float from, float to, float seconds)
        {
            if (seconds <= 0f) { group.alpha = to; yield break; }

            float t = 0f;
            while (t < seconds)
            {
                t += EndingClock.Delta;
                group.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(t / seconds));
                yield return null;
            }
            group.alpha = to;
        }
    }
}
