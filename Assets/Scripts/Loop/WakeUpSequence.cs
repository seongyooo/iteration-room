using System.Collections;
using UnityEngine;

namespace IterationRoom
{
    // The transition between iterations: eyelids fall shut as time runs out, then open again on
    // the ceiling before the player sits up. Replaces what used to be an instant teleport.
    public class WakeUpSequence : MonoBehaviour
    {
        public RectTransform topLid;
        public RectTransform bottomLid;

        public float heldShutDuration = 0.7f;
        public float lookAtCeilingDuration = 0.6f;
        public float riseDuration = 1.9f;
        // A slight lean that leaves level and returns to it, so the horizon isn't nailed down while
        // the body moves.
        public float riseRoll = 3.5f;

        // Lid keyframes: x = position to reach (0 open, 1 shut), y = seconds to take getting there.
        //
        // Eyes do not close like a shutter. The lids fall, snap part of the way back, fall further,
        // and only then give out - and that flutter is what makes the loop taking you read as
        // involuntary rather than as a fade-to-black transition.
        public Vector2[] blinkShutKeys =
        {
            new Vector2(0.80f, 0.16f),
            new Vector2(0.42f, 0.10f),
            new Vector2(0.92f, 0.14f),
            new Vector2(0.60f, 0.08f),
            new Vector2(1.00f, 0.26f),
        };

        // Waking is the same shape in reverse, and heavier - the lids keep sagging back before they
        // finally stay up.
        public Vector2[] blinkOpenKeys =
        {
            new Vector2(0.60f, 0.30f),
            new Vector2(0.80f, 0.15f),
            new Vector2(0.20f, 0.32f),
            new Vector2(0.36f, 0.12f),
            new Vector2(0.00f, 0.38f),
        };

        // Eye height and pitch while lying on the bed. Negative pitch looks up in Unity.
        public float lyingEyeHeight = 0.5f;
        public float lyingPitch = -80f;

        // GOING DOWN UNDER THE GAS, which is the opposite of the blink and deliberately so.
        //
        // The loop taking you is instant and involuntary - lids, and you are gone. Gas is neither.
        // The body goes first and the eyes follow it, so what the player watches is the FLOOR coming
        // up, and only then the lids. Cutting straight to the blink would say the facility switched
        // them off; this says they lost.
        public float collapseDuration = 2.3f;
        public float collapsedEyeHeight = 0.26f;
        // Positive pitch looks DOWN. The head drops toward the floor on the way over, which is what a
        // body losing its legs does - it does not lie down neatly on its back.
        public float collapsedPitch = 62f;
        // Sideways as well, so it is a crumple rather than a lift going down.
        public float collapseRoll = 26f;

        // The same shape as blinkShutKeys and slower everywhere, with the flutter widened. Under gas
        // the lids do not snap - they sag, come back less each time, and stop.
        public Vector2[] gasShutKeys =
        {
            new Vector2(0.55f, 0.55f),
            new Vector2(0.28f, 0.30f),
            new Vector2(0.78f, 0.60f),
            new Vector2(0.50f, 0.34f),
            new Vector2(1.00f, 0.95f),
        };

        public WallPanelDisplay wallPanels;

        public AudioSource bodySource;
        public AudioClip gaspClip;
        public AudioClip sheetRustleClip;

        // Where the lids currently sit, so a blink keyframe can start from wherever the last one
        // left off rather than assuming fully open or fully shut.
        private float lidPosition;

        private void Awake()
        {
            SetLids(0f);
        }

        // SHUT WITH NO BLINK, for the frames before the game has decided what should be on screen.
        //
        // `RunLoop` cannot act until the cycle scenes have finished loading, and until it does the
        // player is standing wherever the prefab was built and the camera is rendering whatever
        // happens to be awake. Opening on that was reported twice: a flash of "some other map"
        // before waking in bed. The lids are the one thing in this game that already means "you are
        // not here yet", so the loading window borrows them.
        public void ShutInstantly() => SetLids(1f);
        public void OpenInstantly() => SetLids(0f);

        public IEnumerator CloseEyes()
        {
            yield return Blink(blinkShutKeys);
            yield return new WaitForSeconds(heldShutDuration);
        }

        // FALL FIRST, THEN THE EYES. Called at a cycle boundary once the gas is in the air.
        //
        // Control is taken HERE rather than when the gas starts, so the player has the first seconds
        // of it with their legs still working - long enough to look up and see where it is coming
        // from. Losing control is the gas working, and it should be felt as that rather than as the
        // moment a cutscene began.
        //
        // The FALL runs on unscaled time, because it happens while no iteration is running and must
        // not be at the mercy of a time scale the pause menu owns. The lids after it go through the
        // ordinary `Blink`, which is scaled - the same path every other blink in the game takes, and
        // there is no reason for this one to behave differently once the player is already down.
        public IEnumerator Collapse(FirstPersonController player)
        {
            if (player != null) player.ControlEnabled = false;

            float fromHeight = player != null ? player.standingEyeHeight : 1.6f;
            float t = 0f;

            while (t < collapseDuration)
            {
                t += Time.unscaledDeltaTime;
                float u = Mathf.Clamp01(t / collapseDuration);

                // EASED IN, not smoothed. A knee gives slowly and then not at all, so the first
                // third of this barely moves and the last third is most of the drop - which is the
                // difference between falling and being lowered.
                float drop = u * u * u;
                // The roll leads the drop a little, so the body tips before it goes.
                float lean = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(u * 1.35f));

                player?.SetEyePose(Mathf.Lerp(fromHeight, collapsedEyeHeight, drop),
                                   Mathf.Lerp(0f, collapsedPitch, drop),
                                   Mathf.Lerp(0f, collapseRoll, lean));
                yield return null;
            }

            player?.SetEyePose(collapsedEyeHeight, collapsedPitch, collapseRoll);

            // And only now the lids, at their own pace.
            yield return Blink(gasShutKeys);
            yield return new WaitForSeconds(heldShutDuration);
        }

        private IEnumerator Blink(Vector2[] keys)
        {
            if (keys == null || keys.Length == 0) yield break;

            foreach (Vector2 key in keys)
            {
                yield return Sweep(lidPosition, key.x, key.y);
            }
        }

        public IEnumerator WakeUp(FirstPersonController player)
        {
            SetLids(1f);

            // Killed behind the closed lids, so the reset itself is never seen - the player only
            // ever witnesses the room coming back, never it going out.
            wallPanels?.PowerDown();

            if (player != null)
            {
                player.ControlEnabled = false;
                player.SetEyePose(lyingEyeHeight, lyingPitch);
            }

            // The gasp lands on the eyes starting to open, not on the rise - it's the reaction to
            // being back, which is the same beat the film puts between "New cycle initialized"
            // and the iteration announcement.
            PlayBody(gaspClip);

            // Open onto the ceiling first and hold there, so the player registers where they are
            // before anything moves.
            yield return Blink(blinkOpenKeys);
            yield return new WaitForSeconds(lookAtCeilingDuration);

            // The room boots as the body sits up, so the two motions happen together rather than
            // queueing. The panels are dark for the whole eyes-open beat before this, which is what
            // makes the sweep land.
            wallPanels?.PowerUp();

            if (player != null)
            {
                PlayBody(sheetRustleClip);

                float elapsed = 0f;
                while (elapsed < riseDuration)
                {
                    elapsed += Time.deltaTime;
                    float k = Mathf.Clamp01(elapsed / riseDuration);

                    // Body and head are deliberately decoupled. The body comes up on an ease-out -
                    // quick off the pillow, settling as it arrives - while the head lags a quarter
                    // of the way in and only levels once you are most of the way up. Driving both
                    // from one SmoothStep, as this used to, is exactly what made it read as a
                    // camera on rails instead of a person sitting up.
                    float bodyK = 1f - Mathf.Pow(1f - k, 3f);
                    float headK = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((k - 0.25f) / 0.75f));
                    float roll = Mathf.Sin(k * Mathf.PI) * riseRoll;

                    player.SetEyePose(
                        Mathf.Lerp(lyingEyeHeight, player.standingEyeHeight, bodyK),
                        Mathf.Lerp(lyingPitch, 0f, headK),
                        roll);
                    yield return null;
                }

                player.SetEyePose(player.standingEyeHeight, 0f, 0f);
                player.ControlEnabled = true;
            }
        }

        private void PlayBody(AudioClip clip)
        {
            if (bodySource != null && clip != null) bodySource.PlayOneShot(clip);
        }

        private IEnumerator Sweep(float from, float to, float duration)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                SetLids(Mathf.SmoothStep(from, to, elapsed / duration));
                yield return null;
            }
            SetLids(to);
        }

        // t = 0 fully open, t = 1 shut. Driven through the anchors rather than pixel offsets so it
        // covers the screen at any resolution.
        private void SetLids(float t)
        {
            t = Mathf.Clamp01(t);
            lidPosition = t;

            if (topLid != null)
            {
                topLid.anchorMin = new Vector2(0f, 1f - t * 0.5f);
                topLid.anchorMax = Vector2.one;
                topLid.offsetMin = Vector2.zero;
                topLid.offsetMax = Vector2.zero;
            }

            if (bottomLid != null)
            {
                bottomLid.anchorMin = Vector2.zero;
                bottomLid.anchorMax = new Vector2(1f, t * 0.5f);
                bottomLid.offsetMin = Vector2.zero;
                bottomLid.offsetMax = Vector2.zero;
            }
        }
    }
}
