using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace IterationRoom
{
    // What happens when the player gets out. The run's only ending, and the only place the loop
    // stops rather than turning over.
    //
    // The shape of it is chosen against the reset the player has by now seen dozens of times, which
    // is the only vocabulary the game has built. A reset is: the room flares, the view judders, the
    // lids blink shut, the panels go dark, and it all starts again. So the ending deliberately does
    // NONE of that.
    //   - The collapse LETS GO instead of peaking. LoopManager ramps the flare and the shake down
    //     to nothing rather than holding them at full, so a player who escapes in the last ten
    //     seconds watches the room stop coming apart.
    //   - The panels do NOT power down. That is the loop's signature - the facility switching the
    //     cell off before switching it back on - and using it here would say the cycle continued.
    //   - The lids do NOT blink. The blink is the loop taking you; it is involuntary and it belongs
    //     to the thing that just failed. This fades instead, slowly, on its own scrim.
    //   - The ghosts are left standing. They are visible under the scrim as it comes up, holding
    //     their pads, and that is the last image of the run: the people it took to get out.
    //
    // Everything here runs on unscaled time. Nothing should be able to pause it, and PauseMenu is
    // locked out for the duration anyway - but a coroutine that can be frozen with no way to
    // unfreeze it is a soft lock, and this one ends the game.
    public class EndingSequence : MonoBehaviour
    {
        public CanvasGroup scrimGroup;
        public CanvasGroup cardGroup;
        // The prompt's own group, so it can arrive after the card without the card's own fade
        // touching it.
        public CanvasGroup promptGroup;
        public Text headline;
        public Text detail;
        // Beneath `detail`. Added 2026-08-13, by request: the run's own clock total, folded across
        // every iteration rather than read off any one of them - LoopManager.TotalElapsedTime is the
        // number, this only formats and shows it.
        public Text timeDetail;

        // THE PER-CYCLE BREAKDOWN, and the total under it. Blank on a one-cycle run, where the
        // breakdown would be one line saying what `detail` and `timeDetail` already say between them
        // - so a run through a single cycle looks exactly as it always did, and the table appears the
        // moment there is more than one thing to compare.
        //
        // One multi-line Text rather than a row per cycle, because the count is not known until the
        // run ends and building rects at that point would be laying out UI inside the ending.
        public Text breakdown;

        // "CLICK TO CONTINUE", under the card. It arrives AFTER `cardHold` rather than with the card:
        // the numbers are the last thing the run has to say and they get their moment before anything
        // asks the player to move on.
        public Text prompt;

        public float scrimFade = 2.4f;
        // A beat of pure black between the room going and the card arriving. Without it the two
        // read as one crossfade and the ending has no punctuation.
        public float blackHold = 0.9f;
        public float cardFade = 1.4f;
        public float cardHold = 6.5f;

        public string menuScene = "MainMenu";

        private void Awake()
        {
            if (scrimGroup != null) scrimGroup.alpha = 0f;
            if (cardGroup != null) cardGroup.alpha = 0f;
        }

        // `records` is one entry per cycle broken, in order - see LoopManager.CycleRecords. The last
        // of them is the cycle the player has just come out of, which is the one `detail` announces;
        // the rest are what the breakdown is for.
        public IEnumerator Play(System.Collections.Generic.IReadOnlyList<CycleRecord> records)
        {
            int lastIterations = records != null && records.Count > 0
                ? records[records.Count - 1].Iterations : 0;

            // Spaced out in the string, as everything in this typeface is - uGUI's Text has no
            // tracking control at all, and in a monospace face a space is exactly one cell.
            if (headline != null) headline.text = Space("CYCLE BROKEN");
            if (detail != null)
                detail.text = lastIterations == 1
                    // Not reachable by a player, but it is one comparison against a line that would
                    // otherwise read "ESCAPED ON ITERATION 1" in the singular-plural sense wrong.
                    ? "ESCAPED ON THE FIRST ITERATION"
                    : $"ESCAPED ON ITERATION {lastIterations}";

            bool several = records != null && records.Count > 1;

            // ONE CYCLE: exactly the line this card has always carried. Not spaced out either,
            // matching `detail` - the facility's record of the run, not its verdict. M:SS rather than
            // clock-style MM:SS: this game has never run long enough for an hour digit, and a leading
            // zero on the minute would claim a precision the number has nothing to back up.
            if (timeDetail != null)
                timeDetail.text = several || records == null || records.Count == 0
                    ? string.Empty
                    : $"TOTAL TIME {RunReport.FormatTime(records[0].Seconds)}";

            // SEVERAL: what each one cost, and the sum under a rule. Formatted by `RunReport`, which
            // the title screen's RECORD page reads from as well - two screens saying the same thing
            // out of two copies of the padding is two copies that drift.
            if (breakdown != null) breakdown.text = several ? RunReport.Table(records) : string.Empty;

            yield return Fade(scrimGroup, 1f, scrimFade);
            yield return Wait(blackHold);
            yield return Fade(cardGroup, 1f, cardFade);
            yield return Wait(cardHold);

            // **AND THEN IT WAITS, 2026-08-20, by request.** This used to load the title screen on a
            // timer, on the reasoning that "a prompt would ask the player to do one more thing in a
            // game that has just stopped asking". That was right when the card carried one number.
            // It carries a TABLE now - every cycle, its iterations and its clock, and the total - and
            // a table that takes itself away after six and a half seconds is a table nobody finishes
            // reading. The run is over; the player can leave it up as long as they like.
            if (prompt != null)
            {
                prompt.text = "CLICK TO CONTINUE";
                yield return Fade(promptGroup, 1f, 0.8f);
            }

            // ANY click or key. Unscaled and polled rather than driven by an input event, like
            // everything else in this coroutine - see the note at the top of the class.
            while (!Input.anyKeyDown) yield return null;

            // The menu unlocks the cursor itself in Start.
            SceneManager.LoadScene(menuScene);
        }

        private static string Space(string text)
        {
            // A single space between words becomes three, which is what keeps the word break
            // readable once every character has one after it. Same treatment as IterationLabel.
            return string.Join(" ", text.ToCharArray());
        }

        private static IEnumerator Fade(CanvasGroup group, float target, float duration)
        {
            if (group == null) yield break;

            float from = group.alpha;
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                group.alpha = Mathf.Lerp(from, target, duration > 0f ? t / duration : 1f);
                yield return null;
            }
            group.alpha = target;
        }

        private static IEnumerator Wait(float seconds)
        {
            float t = 0f;
            while (t < seconds)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }
        }
    }
}
