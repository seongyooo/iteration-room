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
                    : $"TOTAL TIME {FormatTime(records[0].Seconds)}";

            // SEVERAL: what each one cost, and the sum under a rule.
            if (breakdown != null) breakdown.text = several ? Breakdown(records) : string.Empty;

            yield return Fade(scrimGroup, 1f, scrimFade);
            yield return Wait(blackHold);
            yield return Fade(cardGroup, 1f, cardFade);
            yield return Wait(cardHold);

            // Straight back to the title. Deliberately automatic rather than waiting on a key: the
            // player has finished, and a prompt would ask them to do one more thing in a game that
            // has just stopped asking. The menu unlocks the cursor itself in Start.
            SceneManager.LoadScene(menuScene);
        }

        // THE TABLE. Padded rather than laid out, because the typeface is monospaced and a space is
        // exactly one cell - the same fact `Space` above rests on.
        //
        // The total is the SUM OF THE PARTS, not a clock read at the end of the run. Those are not
        // the same number and the difference is not rounding: everything between cycles - the gas,
        // the collapse, the walk to the hatch, the wake-up - happens with the iteration clock
        // stopped, so a wall-clock total would include minutes the player was not being timed for.
        // What is added up here is what each cycle was actually charged.
        private static string Breakdown(System.Collections.Generic.IReadOnlyList<CycleRecord> records)
        {
            var lines = new System.Text.StringBuilder();
            int iterations = 0;
            float seconds = 0f;

            foreach (CycleRecord record in records)
            {
                iterations += record.Iterations;
                seconds += record.Seconds;
                lines.AppendLine(Row($"CYCLE {record.Cycle}", record.Iterations, record.Seconds));
            }

            // A blank line for the rule a monospace face cannot draw.
            lines.AppendLine();
            lines.Append(Row("TOTAL", iterations, seconds));
            return lines.ToString();
        }

        // `CYCLE 1` is seven cells, so everything else is padded to it and the three columns line up
        // whatever the numbers are. Iterations right-aligned in three, which covers a run nobody will
        // ever have; the clock is right-aligned in ten, which fits `59:59.999` with a cell to spare.
        private static string Row(string label, int iterations, float seconds) =>
            $"{label,-7}  {iterations,3} ITERATIONS  {FormatTime(seconds),10}";

        // `M:SS.mmm` since 2026-08-20, by request - the run's clock to the millisecond.
        //
        // FLOOR THE WHOLE SECONDS, ROUND THE MILLISECONDS, CLAMP THE CARRY. Three different rules for
        // three different reasons, and each of the other combinations is wrong:
        //   - flooring the seconds is the rule this had before and keeps: 14:59.9 read as 15:00 claims
        //     a minute the run did not spend;
        //   - flooring the milliseconds TOO is systematically a millisecond low, because a float never
        //     holds the value it was written as - 287.416 is stored as 287.41598, and truncating that
        //     prints `.415` for every run;
        //   - rounding them without a clamp can produce 1000, which prints as `59.1000`.
        // Clamped, the only case that is not nearest-millisecond is the top of a second, where it
        // floors - which is exactly where flooring was wanted in the first place.
        //
        // WHAT THE THIRD DECIMAL IS WORTH. It is the clock the GAME actually ran on, reported at full
        // resolution rather than a more precise measurement taken alongside: `ElapsedTime` is a float
        // summing `Time.deltaTime` frame by frame, so over a ten-minute run the accumulated rounding
        // is on the order of a millisecond or two. The digit is real and it is not a stopwatch.
        //
        // The fraction is taken as `seconds - total` rather than `seconds * 1000f` deliberately -
        // multiplying first does the arithmetic up at 600,000, where a float's own step is already
        // 0.07ms, and throws away precision this is trying to show.
        //
        // Negative-proofed only because the source is a float that has been through several thousand
        // additions; it should never actually go below zero.
        private static string FormatTime(float seconds)
        {
            int total = Mathf.Max(0, Mathf.FloorToInt(seconds));
            int minutes = total / 60;
            int secs = total % 60;
            int ms = Mathf.Clamp(Mathf.RoundToInt((seconds - total) * 1000f), 0, 999);
            return $"{minutes}:{secs:00}.{ms:000}";
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
