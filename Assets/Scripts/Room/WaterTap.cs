using System.Collections;
using UnityEngine;

namespace IterationRoom
{
    // A tap. Press E in range to turn it on, press again to turn it off, and the water shows and hides
    // with it. Range is polled rather than driven by trigger callbacks - see FloorButton for why.
    //
    // A GhostInteractable, so a past self turns it on for you - which is the whole point of putting a
    // tap in this game rather than a button. Filling something takes longer than sixty seconds, and
    // the only way that is ever finished is by the water already running when you arrive.
    //
    // A TOGGLE RECORDED AS AN EVENT, not as a level. `PlayerSignal` is a stretched pulse on the press
    // (see Drawer.openPulseDuration for why a one-frame pulse is not enough), and a ghost toggles on
    // the rising edge. What is reproduced is the PRESS, not the resulting state - which is the honest
    // reading of "record the attempt, re-evaluate the condition": a past self reaches out and turns the
    // handle, and what that does depends on how it found the tap, exactly as it would for the player.
    [RequireComponent(typeof(Collider))]
    public class WaterTap : GhostInteractable, IInteractHintTarget
    {
        public Transform tapVisual;
        // The water, which is NOT simply switched off with the tap - see WaterFlow. The stream stops
        // with the valve; the puddle it left behind dries in its own time.
        public WaterFlow waterFlow;

        // THE HANDLE, when the model has one that can move on its own. `modern_faucet_high_poly` is a
        // single merged mesh and leaves this null, so every tap in the room today turns a lever CUT
        // OUT of its own geometry instead (see SceneBuilder.SplitMeshAtHeight). `boiling_water_tap`
        // had a real `Water Knob_5` node and used this; it was the floor-standing tap and is gone.
        // Turning the whole fixture instead was considered and is worse than nothing - a tap that
        // swings off the wall reads as breaking.
        public Transform handle;
        // In the handle's OWN space, pointing away from the body - a knob turns about the spindle it
        // is mounted on, and which axis that is depends on how the model was authored. Measured at
        // build time rather than assumed.
        public Vector3 handleAxis = Vector3.up;
        public float handleAngle = 100f;
        public float handleDuration = 0.25f;

        public AudioSource audioSource;
        // Two clips rather than one pitched two ways: a tap opening and a tap closing are the same
        // squeak swept in opposite directions, and a pitch shift cannot turn one into the other.
        public AudioClip openClip;
        public AudioClip closeClip;

        // Stretched over a handful of frames, like Drawer's openPulseDuration - a ghost advances by
        // elapsed time and can skip a one-frame pulse entirely.
        public float pressPulseDuration = 0.15f;

        // Its own object rather than the model's pivot, which on an imported tap sits wherever the
        // exporter left it - for the wall mixer, inside the wall.
        public Transform hintAnchor;

        public bool IsOn { get; private set; }

        private Collider trigger;
        private bool playerInRange;
        private float pressPulseUntil = -1f;
        private Quaternion handleOff;
        private Quaternion handleOn;

        public override bool PlayerSignal => Time.time < pressPulseUntil;

        // ...AND NOTHING NEARER WANTS THE PRESS. A stand sits directly under this spout so the water
        // lands in the bucket, which puts a takeable bucket well inside the tap's own reach volume -
        // and the tap was winning that race, so a filled bucket under a running tap could not be
        // picked up. Stated on the HINT rather than only on the press, so the disc stands down with
        // the interaction and the two cannot disagree. See PlayerLookup.TakeableIsNearer.
        public bool WantsInteractHint =>
            playerInRange && PlayerLookup.InView(HintAnchor) && !PlayerLookup.TakeableIsNearer(HintAnchor);
        public Transform HintAnchor =>
            hintAnchor != null ? hintAnchor : (tapVisual != null ? tapVisual : transform);

        // Routed straight to Toggle rather than through the player's own path - see
        // Drawer.SetGhostSignal for why a replayed press must not be re-recorded as the player's.
        // Route it back through the player's path and every iteration inherits the last one's presses.
        public override void SetGhostSignal(GhostReplayer ghost, bool active)
        {
            if (active) Toggle();
        }

        private void Awake()
        {
            trigger = GetComponent<Collider>();

            if (handle != null)
            {
                handleOff = handle.localRotation;
                handleOn = handleOff * Quaternion.AngleAxis(handleAngle, handleAxis);
            }

        }

        private void FixedUpdate()
        {
            Collider playerCollider = PlayerLookup.Collider;

            playerInRange = playerCollider != null
                && playerCollider.enabled
                && trigger.bounds.Intersects(playerCollider.bounds);
        }

        private void Update()
        {
            // ON SCREEN, NOT MERELY IN REACH - and gated on the same property the PROMPT is, so the
            // two can never disagree. `playerInRange` alone let a press behind your back turn the tap,
            // which is precisely what CLAUDE.md §1.2 forbids: an interaction is available exactly when
            // its disc would be showing.
            if (!WantsInteractHint) return;
            if (LoopManager.Instance != null && !LoopManager.Instance.AcceptsInput) return;

            if (!GameInput.InteractPressed) return;
            if (PlayerLookup.InteractTaken) return;

            PlayerLookup.ClaimInteract();
            RegisterPlayerPress();
        }

        // The player's own press, and only theirs, raises the pulse PlayerRecorder samples.
        private void RegisterPlayerPress()
        {
            pressPulseUntil = Time.time + pressPulseDuration;
            Toggle();
        }

        public void Toggle()
        {
            IsOn = !IsOn;
            waterFlow?.SetRunning(IsOn);

            if (handle != null)
            {
                StopAllCoroutines();
                StartCoroutine(TurnHandle(IsOn ? handleOn : handleOff));
            }

            AudioClip valve = IsOn ? openClip : closeClip;
            if (audioSource != null && valve != null)
            {
                // A little scatter, so two taps in the same room are not obviously the same recording.
                audioSource.pitch = Random.Range(0.95f, 1.06f);
                audioSource.PlayOneShot(valve);
            }
        }

        // The loop rewinding, at the top of an iteration - called from Cycle.ResetRooms with the
        // drawers. WITHOUT THIS A TAP LEFT RUNNING STAYS RUNNING, which is un-rewound world state and
        // the one thing the loop must never have: the second iteration would open on a room already
        // flooded by the first, and the ghosts would then turn it on again on top of that.
        //
        // Silent and instant, like Door.Close(): this is behind the closed eyelids, not somebody
        // shutting a tap.
        public void ShutOff()
        {
            StopAllCoroutines();
            IsOn = false;
            pressPulseUntil = -1f;
            waterFlow?.ResetInstant();
            if (handle != null) handle.localRotation = handleOff;
        }

        private IEnumerator TurnHandle(Quaternion target)
        {
            Quaternion start = handle.localRotation;
            for (float e = 0f; e < handleDuration; e += Time.deltaTime)
            {
                handle.localRotation = Quaternion.Slerp(start, target, Mathf.Clamp01(e / handleDuration));
                yield return null;
            }
            handle.localRotation = target;
        }
    }
}
