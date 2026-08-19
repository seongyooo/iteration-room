using UnityEngine;

namespace IterationRoom
{
    // A WHEEL VALVE ON THE WALL OF ROOM2-6. Press E and it turns one full turn; press again and it
    // turns the second. After that it is open and E does nothing here.
    //
    // ONE PRESS IS ONE TURN, and the turn is an ANIMATION rather than a state change with a wheel
    // pointing somewhere new. A valve that snapped would be a switch drawn as a wheel; the second and
    // third seconds of watching it wind round are what say this is heavy and slow and that the room is
    // being drained rather than toggled.
    //
    // MODELLED ON `LightSwitch`, which is the shape every recorded press in this game takes:
    //   - the press is an EVENT, so what is recorded is a stretched PULSE on the rising edge
    //     (CLAUDE.md §1.5) and a ghost replaying it turns the wheel exactly once;
    //   - the condition is RE-EVALUATED at the far end (§1.3) rather than replayed blind - a past self
    //     that turned this twice turns it twice again, and a past self whose turn would be the third
    //     simply does not get one;
    //   - it is per-valve, so three of them cost three bits of `RecordedFrame.signals`.
    //
    // WHY IT IS NOT A HOLD. "Hold E and the wheel spins" was the obvious alternative and it is a worse
    // fit for the loop: a hold is a LEVEL, and a level has to be sampled every frame and re-evaluated
    // against a ghost that may skip several frames in one tick. Two presses is two instants, which is
    // the thing this recorder is good at.
    [RequireComponent(typeof(Collider))]
    public class Valve : GhostInteractable, IInteractHintTarget
    {
        // The part that spins. Its own object under the model, so the model's import pose is left
        // alone - see SceneBuilder.BuildValve.
        public Transform wheel;
        public Transform hintAnchor;

        // TWO, by request. Kept as a number rather than a bool pair because "how far open is it" is
        // the question the drain asks, and a room that wanted three would be this number.
        public int turnsToOpen = 2;
        // Slow enough to be a mechanism. A quarter of a second reads as a spring; a second and a half
        // is somebody winding a wheel.
        public float turnDuration = 1.4f;
        // Which way the wheel's own axis points, in ITS local space. A wheel on a wall turns about the
        // axis facing out of the wall, and which of the model's axes that is depends on the model.
        public Vector3 spinAxis = Vector3.forward;

        public AudioSource audioSource;
        public AudioClip turnClip;
        public AudioClip lockClip;

        // Stretched over a handful of frames, like `LightSwitch.onPulseDuration`: a ghost advances by
        // elapsed time and can skip a one-frame pulse entirely.
        public float turnPulseDuration = 0.15f;

        public int Turns { get; private set; }
        public bool FullyOpen => Turns >= turnsToOpen;
        // While the wheel is winding round, this valve is busy. It is not merely cosmetic: a second
        // press landing mid-turn would stack two coroutines on one transform and the wheel would jump.
        public bool Turning { get; private set; }

        private Collider trigger;
        private bool playerInRange;
        private float pulseUntil = -1f;
        private Quaternion restRotation;

        public override bool PlayerSignal => Time.time < pulseUntil;

        // ELIGIBILITY ONLY - never the arbitration, which polls this property and would recurse. A
        // valve that is fully open, or already turning, has nothing for E to do.
        public bool WantsInteractHint =>
            playerInRange && !FullyOpen && !Turning && PlayerLookup.InView(HintAnchor);

        public Transform HintAnchor =>
            hintAnchor != null ? hintAnchor : (wheel != null ? wheel : transform);

        // Straight to the turn rather than through the player's path, for the reason
        // `LightSwitch.SetGhostSignal` gives: a replayed turn must not be re-recorded as this run's.
        public override void SetGhostSignal(GhostReplayer ghost, bool active)
        {
            if (active) TurnOnce();
        }

        private void Awake()
        {
            trigger = GetComponent<Collider>();
            if (wheel != null) restRotation = wheel.localRotation;
        }

        private void FixedUpdate() => playerInRange = PlayerLookup.InReach(trigger);

        private void Update()
        {
            if (!WantsInteractHint) return;
            if (LoopManager.Instance != null && !LoopManager.Instance.AcceptsInput) return;
            if (!PlayerLookup.PressGoesTo(this)) return;
            if (!GameInput.InteractPressed) return;
            if (PlayerLookup.InteractTaken) return;

            PlayerLookup.ClaimInteract();
            pulseUntil = Time.time + turnPulseDuration;
            TurnOnce();
        }

        // ONE TURN, or nothing at all. Refusing here rather than at the caller is what makes the
        // ghost path and the player path agree without either knowing about the other.
        public void TurnOnce()
        {
            if (FullyOpen || Turning) return;

            Turns++;
            StopAllCoroutines();
            StartCoroutine(Spin());
            Play(turnClip);
        }

        // The loop rewinding, called from `PoolDrain.ResetCondition`. Silent and instant, like
        // `LightSwitch.TurnOff` and `Door.Close`: this happens behind closed eyelids, and three valves
        // audibly winding themselves back would be the machinery showing through.
        public void ResetValve()
        {
            StopAllCoroutines();
            Turning = false;
            Turns = 0;
            pulseUntil = -1f;
            if (wheel != null) wheel.localRotation = restRotation;
        }

        private System.Collections.IEnumerator Spin()
        {
            Turning = true;

            if (wheel != null)
            {
                Quaternion from = wheel.localRotation;
                // A FULL TURN, in three thirds. Slerping to a 360-degree target is a no-op - the
                // shortest arc between a rotation and itself is nothing - so the turn is driven as an
                // angle rather than as a destination.
                for (float e = 0f; e < turnDuration; e += Time.deltaTime)
                {
                    float t = Mathf.Clamp01(e / turnDuration);
                    // Eased at both ends: a wheel this size does not start or stop instantly.
                    float eased = t * t * (3f - 2f * t);
                    wheel.localRotation = from * Quaternion.AngleAxis(eased * 360f, spinAxis);
                    yield return null;
                }
                // Back to exactly where it started, which a full turn is. Leaving the accumulated
                // rotation on would drift the wheel's pose by a fraction of a degree per turn.
                wheel.localRotation = from;
            }
            else
            {
                yield return new WaitForSeconds(turnDuration);
            }

            Turning = false;
            if (FullyOpen) Play(lockClip);
        }

        private void Play(AudioClip clip)
        {
            if (audioSource == null || clip == null) return;
            audioSource.pitch = Random.Range(0.95f, 1.05f);
            audioSource.PlayOneShot(clip);
        }
    }
}
