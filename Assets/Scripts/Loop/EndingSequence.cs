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

        public IEnumerator Play(int iterationNumber)
        {
            // Spaced out in the string, as everything in this typeface is - uGUI's Text has no
            // tracking control at all, and in a monospace face a space is exactly one cell.
            if (headline != null) headline.text = Space("CYCLE BROKEN");
            if (detail != null)
                detail.text = iterationNumber == 1
                    // Not reachable by a player, but it is one comparison against a line that would
                    // otherwise read "ESCAPED ON ITERATION 1" in the singular-plural sense wrong.
                    ? "ESCAPED ON THE FIRST ITERATION"
                    : $"ESCAPED ON ITERATION {iterationNumber}";

            yield return Fade(scrimGroup, 1f, scrimFade);
            yield return Wait(blackHold);
            yield return Fade(cardGroup, 1f, cardFade);
            yield return Wait(cardHold);

            // Straight back to the title. Deliberately automatic rather than waiting on a key: the
            // player has finished, and a prompt would ask them to do one more thing in a game that
            // has just stopped asking. The menu unlocks the cursor itself in Start.
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
