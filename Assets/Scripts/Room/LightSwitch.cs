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

        // SMALL ON PURPOSE. `old_light_switch.glb` merges its plate and its lever into ONE mesh, so
        // there is no lever to turn on its own - whatever angle goes here tips the whole wall plate
        // with it, and at 25 the plate visibly came off the wall. A few degrees reads as the switch
        // taking the press; the light coming on is the real feedback.
        public float flipAngle = 6f;
        public float flipDuration = 0.12f;

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
        public bool WantsInteractHint => playerInRange && !IsOn && PlayerLookup.InView(HintAnchor);
        public Transform HintAnchor => switchVisual != null ? switchVisual : transform;

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
            Collider playerCollider = PlayerLookup.Collider;

            playerInRange = playerCollider != null
                && playerCollider.enabled
                && trigger.bounds.Intersects(playerCollider.bounds);
        }

        private void Update()
        {
            if (!playerInRange || IsOn) return;
            if (LoopManager.Instance != null && !LoopManager.Instance.AcceptsInput) return;

            if (!Input.GetKeyDown(KeyCode.E)) return;
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
        }

        // The loop rewinding, at the top of an iteration - called from AllLightsOn.ResetCondition
        // rather than latched world state this component keeps on its own. Silent and instant, like
        // Door.Close(): this is behind the closed eyelids, not somebody flipping the switch back.
        public void TurnOff()
        {
            StopAllCoroutines();
            IsOn = false;
            onPulseUntil = -1f;
            if (switchVisual != null) switchVisual.localRotation = offRotation;
            ApplyState();
        }

        private void ApplyState()
        {
            if (controlledLight != null) controlledLight.enabled = IsOn;

            Material wanted = IsOn ? litMaterial : darkMaterial;
            if (controlledPanel != null && wanted != null) controlledPanel.sharedMaterial = wanted;
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
