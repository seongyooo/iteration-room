using UnityEngine;

namespace IterationRoom
{
    // A plate the player walks up to and presses with E. There are two: the one that starts the run
    // in the calibration room, and the one that ends it in the final room.
    //
    // A physical button rather than a key prompt on the screen, for the same reason the control list
    // and the gauge are on the wall: the room is built out of displays, and a fixture the player
    // walks up to and operates is the room saying it, where an overlay would be the game saying it.
    //
    // It is an IInteractHintTarget like every other E fixture, so the same grey disc that prompts
    // over drawers and locks prompts here.
    //
    // NEITHER GATES ON LoopManager.AcceptsInput, which is what every interactable in the game proper
    // uses, and that is the whole reason they share a base. Both live OUTSIDE the loop - one before
    // the first iteration, one after the last - and AcceptsInput is false for exactly the moments
    // they have to work. Each subclass says when it is live instead.
    public abstract class PressPlate : MonoBehaviour, IInteractHintTarget
    {
        public Renderer buttonRenderer;
        public AudioSource audioSource;
        public AudioClip pressClip;

        public float interactRadius = 1.4f;

        // Near-black at rest, matching GrooveDark and the plate's own material, so the cell reads
        // as a switched-off display among white panels rather than as a coloured object dropped
        // into the room. It flashes RED, not green: red on near-black is the whole room's palette -
        // the HUD, the wall displays, the word on the panel above it - and a green button was the
        // one thing borrowed from a different game.
        public Color idleColor = new Color(0.05f, 0.05f, 0.055f);
        public Color pressedColor = new Color(0.85f, 0.12f, 0.12f);
        public float litEmission = 2.6f;

        // When E here would do something. The prompt and the press read the same answer, so a disc
        // can never appear over a plate that would ignore the key.
        protected abstract bool IsLive { get; }

        protected abstract void OnPressed();

        private Transform player;
        private bool inRange;
        private float litUntil = float.NegativeInfinity;

        private MaterialPropertyBlock block;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        // Lazily, not in Awake: a MaterialPropertyBlock is not serialized, so a script reload during
        // play mode nulls it without Awake running again and every SetPropertyBlock throws after.
        private MaterialPropertyBlock Block => block ??= new MaterialPropertyBlock();

        public bool WantsInteractHint => inRange && IsLive;

        public Transform HintAnchor => transform;

        protected virtual void Start() => Paint(false);

        // Range is polled rather than driven by trigger callbacks, matching every other volume in
        // the project - see FloorButton. Horizontal distance only: a plate is above the floor and
        // the player's pivot is on it, so including Y would put them permanently a metre away.
        protected virtual void FixedUpdate()
        {
            if (player == null)
            {
                GameObject go = GameObject.FindGameObjectWithTag("Player");
                if (go == null) return;
                player = go.transform;
            }

            Vector3 offset = player.position - transform.position;
            offset.y = 0f;
            inRange = offset.sqrMagnitude <= interactRadius * interactRadius;
        }

        protected virtual void Update()
        {
            if (WantsInteractHint && Input.GetKeyDown(KeyCode.E))
            {
                litUntil = Time.unscaledTime + 0.2f;
                if (audioSource != null && pressClip != null) audioSource.PlayOneShot(pressClip);
                OnPressed();
            }

            Paint(Time.unscaledTime < litUntil);
        }

        private void Paint(bool lit)
        {
            if (buttonRenderer == null) return;

            MaterialPropertyBlock b = Block;
            buttonRenderer.GetPropertyBlock(b);
            b.SetColor(BaseColorId, lit ? pressedColor : idleColor);
            // Emission carries the press - albedo alone reads as a different colour rather than as
            // the plate lighting up. Pushed past 1 so it clears the room's high bloom threshold.
            b.SetColor(EmissionId, lit ? pressedColor * litEmission : Color.black);
            buttonRenderer.SetPropertyBlock(b);
        }
    }
}
