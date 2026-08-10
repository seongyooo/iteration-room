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
