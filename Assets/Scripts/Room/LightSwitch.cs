using UnityEngine;

namespace IterationRoom
{
    // Press E once to flip it on: the light and its ceiling panel come on with it, and it cannot be
    // flipped off again except by the loop. One of four scattered around room2-1 - AllLightsOn is
    // the room's rule that all four must be lit at once.
    //
    // Modelled on Drawer: a toggle is an EVENT (the flip), not a level, so the recorded signal is a
    // short pulse on the rising edge and a ghost's replayed flip turns the light on exactly once,
    // never off. Range is polled rather than driven by trigger callbacks - see FloorButton for why.
    [RequireComponent(typeof(Collider))]
    public class LightSwitch : GhostInteractable, IInteractHintTarget
    {
        public Transform switchVisual;
        public Light controlledLight;
        public Renderer controlledPanel;

        // TWO SHARED MATERIALS SWAPPED, rather than one material's emission written per switch.
        // Writing emission means touching `renderer.material`, which instantiates a copy - harmless
        // once at runtime, but the build does the same thing to set the room dark and leaves the
        // orphans in the scene file. Two assets authored by `SceneBuilder` keeps the values where
        // CLAUDE.md §2 says values live and leaks nothing at either end.
        public Material litMaterial;
        public Material darkMaterial;

        // `switchVisual` is the LEVER'S PIVOT, not the plate - the switch is built rather than
        // imported precisely so those are two objects (see SceneBuilder.BuildLightSwitch). A real
        // angle is affordable again now that the plate stays put: this used to be held at 6 degrees,
        // which was the most a whole-plate tilt could get away with and read as nothing at all.
        public float flipAngle = 30f;
        public float flipDuration = 0.12f;

        // Where the E disc hangs. Its own object rather than the lever, which sits at the plate's
        // face and drew the prompt low - under the fixture it was labelling.
        public Transform hintAnchor;

        // The click. A switch that moves in silence is the clearest possible statement that nothing
        // happened - and in a room where the point is that a light came on somewhere behind you, the
        // sound is what tells you a past self did it.
        public AudioSource audioSource;
        public AudioClip onClip;
        public AudioClip offClip;

        // Stretched over a handful of frames, like Drawer's openPulseDuration - a ghost advances by
        // elapsed time and can skip a one-frame pulse entirely.
        public float onPulseDuration = 0.15f;

        public bool IsOn { get; private set; }

        private Collider trigger;
        private bool playerInRange;
        private float onPulseUntil = -1f;
        private Quaternion offRotation;
        private Quaternion onRotation;

        public override bool PlayerSignal => Time.time < onPulseUntil;

        // Once it is on there is nothing left for E to do here - see Drawer.WantsInteractHint for
        // the same shape.
        // ELIGIBILITY ONLY. Whether this switch is the thing the press is FOR - against a dropped
        // object lying in front of it, or against another fixture on the same wall - is asked once
        // for everything in `PlayerLookup.IsAimedAt`, in the press path below. It must not be asked
        // here: that arbiter polls this property, so a fixture that consulted it would recurse.
        public bool WantsInteractHint =>
            playerInRange && !IsOn && PlayerLookup.InView(HintAnchor);
        public Transform HintAnchor =>
            hintAnchor != null ? hintAnchor : (switchVisual != null ? switchVisual : transform);

        // Routed straight to TurnOn rather than through the player's own path when a ghost calls it -
        // see Drawer.SetGhostSignal for why a replayed flip must not be re-recorded as the player's.
        public override void SetGhostSignal(GhostReplayer ghost, bool active)
        {
            if (active) TurnOn();
        }

        private void Awake()
        {
            trigger = GetComponent<Collider>();

            if (switchVisual != null)
            {
                offRotation = switchVisual.localRotation;
                // PRE-multiplied, so the tilt happens about the HOLDER's X - the horizontal axis
                // running along the wall - whatever roll the mesh carries to stand the plate upright.
                // Post-multiplying would tilt about the mesh's own X, which the roll has turned into
                // the wall's vertical, and the plate would swing sideways like a gate.
                onRotation = Quaternion.Euler(flipAngle, 0f, 0f) * offRotation;
            }

            ApplyState();
        }

        private void FixedUpdate()
        {
            playerInRange = PlayerLookup.InReach(trigger);
        }

        private void Update()
        {
            // ON SCREEN, NOT MERELY IN REACH, and gated on the same property the prompt is - see
            // WaterTap.Update, which had the same fault. Proximity alone let a switch behind you
            // answer a press.
            if (!WantsInteractHint) return;
            if (LoopManager.Instance != null && !LoopManager.Instance.AcceptsInput) return;

            // ON SCREEN, NOT BEHIND ANYTHING, AND THE THING BEING LOOKED AT - the same answer the
            // prompt disc is drawn from, so E acts exactly where the mark is. See
            // PlayerLookup.PressGoesTo.
            if (!PlayerLookup.PressGoesTo(this)) return;

            if (!GameInput.InteractPressed) return;
            if (PlayerLookup.InteractTaken) return;

            PlayerLookup.ClaimInteract();
            RegisterPlayerOn();
        }

        private void RegisterPlayerOn()
        {
            onPulseUntil = Time.time + onPulseDuration;
            TurnOn();
        }

        public void TurnOn()
        {
            if (IsOn) return;
            IsOn = true;
            StopAllCoroutines();
            StartCoroutine(Flip(onRotation));
            ApplyState();
            Click(onClip);
        }

        // The loop rewinding, at the top of an iteration - called from AllLightsOn.ResetCondition
        // rather than latched world state this component keeps on its own. Only a real on-to-off
        // transition makes a sound; initial setup and repeated resets remain silent.
        public void TurnOff()
        {
            bool wasOn = IsOn;
            StopAllCoroutines();
            IsOn = false;
            onPulseUntil = -1f;
            if (switchVisual != null) switchVisual.localRotation = offRotation;
            ApplyState();
            if (wasOn) Click(offClip);
        }

        private void ApplyState()
        {
            if (controlledLight != null) controlledLight.enabled = IsOn;

            Material wanted = IsOn ? litMaterial : darkMaterial;
            if (controlledPanel != null && wanted != null) controlledPanel.sharedMaterial = wanted;
        }

        // Both transitions use the existing authored switch clips.
        private void Click(AudioClip clip)
        {
            if (audioSource == null || clip == null) return;
            // Scattered a little, so four switches in one room are not audibly the same recording.
            audioSource.pitch = Random.Range(0.94f, 1.07f);
            audioSource.PlayOneShot(clip);
        }

        private System.Collections.IEnumerator Flip(Quaternion target)
        {
            if (switchVisual == null) yield break;

            Quaternion start = switchVisual.localRotation;
            for (float e = 0f; e < flipDuration; e += Time.deltaTime)
            {
                switchVisual.localRotation = Quaternion.Slerp(start, target, Mathf.Clamp01(e / flipDuration));
                yield return null;
            }
            switchVisual.localRotation = target;
        }
    }
}
