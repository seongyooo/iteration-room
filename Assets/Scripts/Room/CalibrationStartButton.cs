using UnityEngine;

namespace IterationRoom
{
    // The plate under the calibration room's wall display. E at it ends the calibration step and
    // starts iteration 1 - it is the only way out of that step, and the only thing in the room E
    // can be pressed at.
    //
    // A physical button rather than a key prompt on the screen, for the same reason the control
    // list and the gauge are on the wall: the room is built out of displays, and a fixture the
    // player walks up to and operates is the room saying "begin" where an overlay would be the game
    // saying it. It also means the player's very first E press in the run happens here, against a
    // real fixture, which is what the control list above it has just finished describing.
    //
    // It is an IInteractHintTarget like every other E fixture, so the same grey disc that prompts
    // over drawers and locks prompts here - the player meets the game's prompt before the game.
    //
    // It gates on SensitivityCalibration.Active rather than LoopManager.AcceptsInput, which is what
    // every interactable in the game proper uses. That is the deliberate exception: AcceptsInput is
    // false for the whole of calibration, which is exactly when this has to work.
    public class CalibrationStartButton : MonoBehaviour, IInteractHintTarget
    {
        public SensitivityCalibration calibration;
        public Renderer buttonRenderer;
        public AudioSource audioSource;
        public AudioClip pressClip;

        public float interactRadius = 1.4f;

        // Near-black at rest, matching GrooveDark and the plate's own material, so the cell reads
        // as a switched-off display among white panels rather than as a coloured object dropped
        // into the room. It flashes RED, not green: red on near-black is the whole room's palette -
        // the HUD, the wall displays, the word on this very panel - and a green button was the one
        // thing in here borrowed from a different game.
        public Color idleColor = new Color(0.05f, 0.05f, 0.055f);
        public Color pressedColor = new Color(0.85f, 0.12f, 0.12f);
        public float litEmission = 2.6f;

        private Transform player;
        private bool inRange;
        private float litUntil = float.NegativeInfinity;

        private MaterialPropertyBlock block;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        // Lazily, not in Awake: a MaterialPropertyBlock is not serialized, so a script reload during
        // play mode nulls it without Awake running again and every SetPropertyBlock throws after.
        private MaterialPropertyBlock Block => block ??= new MaterialPropertyBlock();

        // Only while the step is actually open. Once it has been pressed the room is behind the
        // player forever, but the prompt must not linger over it on the way out.
        public bool WantsInteractHint =>
            inRange && calibration != null && calibration.Active;

        public Transform HintAnchor => transform;

        private void Start() => Paint(false);

        // Range is polled rather than driven by trigger callbacks, matching every other volume in
        // the project - see FloorButton. Horizontal distance only: the plate is at 1.2m and the
        // player's pivot is on the floor, so including Y would put them permanently 1.2m away.
        private void FixedUpdate()
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

        private void Update()
        {
            if (WantsInteractHint && Input.GetKeyDown(KeyCode.E))
            {
                litUntil = Time.unscaledTime + 0.2f;
                if (audioSource != null && pressClip != null) audioSource.PlayOneShot(pressClip);
                calibration.Confirm();
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
