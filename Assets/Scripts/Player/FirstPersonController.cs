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

        // Wading through the balloons in Room2. A CharacterController does not push rigidbodies on
        // its own, so without this they are scenery you walk through, which is most of the reason
        // for them being physical at all.
        //
        // This is an explicit overlap query rather than OnControllerColliderHit, and the balloons
        // are on their own layer that the controller's collider excludes - so the player never
        // touches one. That is deliberate and it is what kills a whole class of bug rather than one
        // instance of it: the first version derived its push direction from hit.moveDirection, and
        // standing still the controller's only motion is gravity, i.e. (0,-1,0). Both horizontal
        // components were zero, so the "token lift" meant to roll a balloon over the player's foot
        // normalised into a pure upward shove, re-applied every frame - the balloon rose, stepOffset
        // stepped the player up onto it, and you rode it into the ceiling. With no contact at all
        // there is nothing to stand on and nothing to be lifted by.
        //
        // Expressed as a speed, not a force, and capped. An early version applied an 0.55 impulse
        // to a 0.04kg balloon - an 11 m/s kick, so they shot across the room off a brushed shin.
        // The cap also means walking through a crowd nudges each balloon once rather than
        // accelerating it every frame of contact.
        public float pushSpeed = 1.1f;
        public float pushAccel = 0.22f;
        // How far past the controller's own radius the sweep reaches. Enough that balloons part
        // ahead of the player rather than intersecting them first and being shoved out afterwards.
        public float pushReach = 0.35f;
        // Set by SceneBuilder to the balloon layer. Left at 0 this does nothing at all, which is
        // the right failure: no push is better than pushing the furniture around.
        public LayerMask pushLayers;

        // Cleared while a cutscene owns the camera (the wake-up at the start of each iteration).
        public bool ControlEnabled { get; set; } = true;

        private CharacterController controller;
        private float pitch;
        private float verticalVelocity;

        // The horizontal part of this frame's movement, kept so the FixedUpdate push can ask
        // whether the player is actually walking into anything. Pushing while stationary would let
        // a player stand in a crowd of balloons and blow them outwards for free.
        private Vector3 horizontalMove;
        private readonly Collider[] pushHits = new Collider[24];

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
            if (!ControlEnabled)
            {
                // Cleared rather than left holding its last value: the wake-up takes control away
                // mid-stride, and a stale vector would leave the player shoving balloons around
                // with their eyes shut.
                horizontalMove = Vector3.zero;
                return;
            }

            // Browsers only grant pointer lock from inside a user gesture, and refuse it for a
            // while after the player has pressed Escape to leave it - so the lock requested in
            // Start is routinely denied on WebGL, and resuming from the pause menu can be denied
            // too. Clicking asks again, which is the one thing a player with a loose cursor will
            // naturally do. On desktop this branch never runs.
            if (Cursor.lockState != CursorLockMode.Locked)
            {
                // Visible while loose. The pause menu turns the cursor off on resume, and if that
                // resume did not get its lock back, an invisible free pointer is the worst of both.
                Cursor.visible = true;

                if (Input.GetMouseButtonDown(0))
                {
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                }

                // Looking is skipped entirely while loose: the mouse is a free pointer travelling
                // across the page, and feeding that motion to the camera swings the view across
                // the room as it goes.
                HandleMove();
                return;
            }

            HandleLook();
            HandleMove();
        }

        private void FixedUpdate()
        {
            PushOverlapping();
        }

        // Shoves whatever is on pushLayers out of the player's way. Direction comes from the
        // geometry - the horizontal offset from the controller's axis to the body's centre - rather
        // than from the direction of travel, so it is well defined however the player is moving and
        // it always points outwards. Purely horizontal: a balloon that is not collided with has no
        // foot to be rolled over, so there is nothing for a lift to fix and an upward component
        // would only push it into the ceiling.
        private void PushOverlapping()
        {
            if (pushLayers.value == 0) return;
            if (horizontalMove.sqrMagnitude < 0.0001f) return;

            // The controller's capsule, grown by pushReach. TransformPoint rather than adding
            // controller.center directly: the offset is a local one, and it only happens to survive
            // being treated as a world offset because this player is unscaled and yaws about Y.
            float half = Mathf.Max(0f, controller.height / 2f - controller.radius);
            Vector3 centre = transform.TransformPoint(controller.center);
            Vector3 top = centre + Vector3.up * half;
            Vector3 bottom = centre - Vector3.up * half;

            int count = Physics.OverlapCapsuleNonAlloc(
                bottom, top, controller.radius + pushReach, pushHits, pushLayers, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                Rigidbody body = pushHits[i].attachedRigidbody;
                if (body == null || body.isKinematic) continue;
                // Already moving away fast enough: nudging it again is what turned a walk through a
                // crowd into a shotgun blast.
                if (body.linearVelocity.magnitude >= pushSpeed) continue;

                Vector3 away = body.worldCenterOfMass - centre;
                away.y = 0f;
                // Dead on the axis - directly overhead or underfoot. Fall back to where the player
                // is heading rather than picking an arbitrary direction.
                Vector3 dir = away.sqrMagnitude > 0.0001f ? away.normalized : horizontalMove.normalized;

                // At the centre of mass, not the contact point: off-centre this mostly spins a
                // sphere instead of moving it.
                body.AddForce(dir * pushAccel, ForceMode.VelocityChange);
            }
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
            horizontalMove = move;

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
