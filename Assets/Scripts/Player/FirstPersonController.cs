using UnityEngine;

namespace IterationRoom
{
    [RequireComponent(typeof(CharacterController))]
    public class FirstPersonController : MonoBehaviour
    {
        public Camera playerCamera;
        public float moveSpeed = 4.5f;
        public float mouseSensitivity = 2f;
        public float gravity = -20f;
        public float jumpForce = 6f;

        public float standingEyeHeight = 1.6f;

        // A CharacterController does not push rigidbodies on its own - it just slides past them.
        // Without this the balloons in Room2 are scenery you walk through, which is most of the
        // reason for them being physical at all.
        //
        // Expressed as a speed, not a force, and capped. The first version applied an 0.55 impulse
        // to a 0.05kg balloon - which is a 11 m/s kick, and they shot across the room off a
        // brushed shin. A speed cap also means walking through a crowd nudges each balloon once
        // rather than accelerating it every frame of contact.
        public float pushSpeed = 1.1f;
        public float pushAccel = 0.22f;

        // Cleared while a cutscene owns the camera (the wake-up at the start of each iteration).
        public bool ControlEnabled { get; set; } = true;

        private CharacterController controller;
        private float pitch;
        private float verticalVelocity;

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
        }

        private void Start()
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private void Update()
        {
            if (!ControlEnabled) return;
            HandleLook();
            HandleMove();
        }

        private void OnControllerColliderHit(ControllerColliderHit hit)
        {
            Rigidbody body = hit.collider.attachedRigidbody;
            if (body == null || body.isKinematic) return;
            if (body.linearVelocity.magnitude >= pushSpeed) return;

            // Horizontal only, plus a token lift so a balloon rolls up over the foot rather than
            // being driven into the floor and juddering there. At the centre of mass rather than
            // the contact point: off-centre it mostly spins the balloon instead of moving it.
            Vector3 push = new Vector3(hit.moveDirection.x, 0.22f, hit.moveDirection.z).normalized;
            body.AddForce(push * pushAccel, ForceMode.VelocityChange);
        }

        // Places the eye at a given height and pitch. Writing back into `pitch` matters: without
        // it, the first mouse movement after control returns would snap the view back to whatever
        // angle the controller last held.
        // roll is cutscene-only: HandleLook writes the camera's euler angles with a zero Z every
        // frame, so any lean is dropped the instant control returns - which is what we want.
        public void SetEyePose(float eyeHeight, float pitchDegrees, float rollDegrees = 0f)
        {
            pitch = pitchDegrees;
            if (playerCamera == null) return;
            playerCamera.transform.localPosition = new Vector3(0f, eyeHeight, 0f);
            playerCamera.transform.localEulerAngles = new Vector3(pitch, 0f, rollDegrees);
        }

        private void HandleLook()
        {
            float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity;
            float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity;

            transform.Rotate(Vector3.up * mouseX);

            pitch = Mathf.Clamp(pitch - mouseY, -85f, 85f);
            if (playerCamera != null)
                playerCamera.transform.localEulerAngles = new Vector3(pitch, 0f, 0f);
        }

        private void HandleMove()
        {
            float x = Input.GetAxis("Horizontal");
            float z = Input.GetAxis("Vertical");
            Vector3 move = transform.right * x + transform.forward * z;
            move = Vector3.ClampMagnitude(move, 1f) * moveSpeed;

            if (controller.isGrounded && verticalVelocity < 0f)
                verticalVelocity = -1f;

            if (controller.isGrounded && Input.GetButtonDown("Jump"))
                verticalVelocity = jumpForce;

            verticalVelocity += gravity * Time.deltaTime;

            move.y = verticalVelocity;
            controller.Move(move * Time.deltaTime);
        }

        public void Teleport(Vector3 position, Quaternion rotation)
        {
            controller.enabled = false;
            transform.SetPositionAndRotation(position, rotation);
            verticalVelocity = 0f;
            controller.enabled = true;
        }
    }
}
