using UnityEngine;

namespace IterationRoom
{
    [RequireComponent(typeof(CharacterController))]
    public class FirstPersonController : MonoBehaviour
    {
        public Camera playerCamera;

        // WALK is the default and SPRINT is the old single speed, which is the conservative direction:
        // nothing that was tuned against 4.5 m/s can now happen FASTER than it was tested at, only
        // slower. 4.5 was always a run - a real walk is about 1.4 - and the room reads as a place you
        // are stuck in rather than a corridor you are clearing at 2.5.
        //
        // What it costs is clock. An empty room is ~10.85m, so crossing one goes from about 2.4s to
        // 4.3s of the sixty, and reaching Room4 from the bed roughly doubles. Sprint is what pays that
        // back, and the split earns its keep for a reason beyond speed: walking is for rooms you are
        // still solving, sprinting is for the ones past selves have already finished. The loop's whole
        // shape is rooms becoming solved, so a control that says "I am done here" fits it.
        public float walkSpeed = 2.5f;
        public float sprintSpeed = 4.5f;

        // SHIFT, not the Ctrl that TODO.md had pencilled in. That entry paired Ctrl-sprint with
        // Shift-crouch and flagged itself as inverted from near-universal convention; the inversion
        // only existed to free Shift for crouch, and there is no crouch. If one arrives it needs its
        // own key rather than this one back.
        public KeyCode sprintKey = KeyCode.LeftShift;

        // CROUCH is the eye and the speed, and DELIBERATELY NOT the collider.
        //
        // Nothing in the game has to be crouched under, so shrinking the CharacterController would buy
        // nothing and bring a whole failure mode with it: a shorter capsule can end up somewhere the
        // standing one does not fit, and then releasing the key has to either refuse or push the player
        // out of geometry. There is no good answer to that, so the situation is not created. If a room
        // ever needs a crouch-height gap, the capsule has to shrink then and that problem arrives with
        // it - it is not free.
        //
        // What it is for is the same thing the walk speed is for: being in the room rather than
        // crossing it. Lowering the eye by 0.6m changes what the panelling and the floor look like more
        // than any number in the renderer does.
        public KeyCode crouchKey = KeyCode.LeftControl;
        public float crouchEyeHeight = 1.0f;
        public float crouchSpeed = 1.2f;
        // Seconds to sit down or stand up. Long enough to be a movement rather than a cut; short enough
        // that it is not a wait. The eye eases, so this is also what stops the head bob jumping when
        // the base height changes underneath it.
        public float crouchTransition = 0.18f;

        public float gravity = -20f;
        public float jumpForce = 6f;

        // WALKING IS HELD, LOOKING IS NOT, while an action the player started plays out - see
        // HandleMove. Set by `Bucket` for the length of a pour and cleared by every path that can end
        // one, including the loop's rewind and a cycle going to sleep: a lock is world state like any
        // other, and one left set is a player who can never walk again.
        //
        // Deliberately NOT part of `LoopManager.AcceptsInput`. That gate means "the game is not taking
        // input at all" - the pause menu, the wake-up, the end of a run - and it stops the look, the
        // interact key and every fixture with it. This stops one of those things and nothing else.
        public bool MovementLocked { get; set; }

        // THE VIEW, held separately from the legs. Pouring a bucket locks both - a two-handed action
        // you are watching - while the balloon tool locks neither, so the two cannot be one flag.
        // `HandleLook` still RUNS while this is set (it maintains `pitch` and writes a zero roll every
        // frame, see its own note); it simply takes no input.
        public bool LookLocked { get; set; }

        // How long it takes to reach full speed, and to come back to a stop. SECONDS rather than
        // m/s^2 because this is a number tuned by feel, in the Inspector, while playing - and "a
        // seventh of a second to top speed" is a sentence a person can hold, where "32 m/s^2" is not.
        // Zero on either is legal and means instant.
        //
        // These exist because the two extremes were both tried and both wrong. Unity's Horizontal and
        // Vertical axes ship with gravity 3 and sensitivity 3, which is a third of a second each way:
        // that read as walking on ice, because in rooms this size almost every input is a short
        // corrective step and each one had a slide on the end of it. Removing the smoothing outright
        // fixed the ice and read as stiff. So the ramp is real, but it is a tenth of what came free.
        //
        // Stopping is deliberately QUICKER than starting. Symmetrical numbers feel floaty: the player
        // is asking for the stop, so it should arrive sooner than a speed they merely drifted up to.
        // At 4.5 m/s a 0.11s stop is about 0.25m of travel, against the 0.75m the default gave.
        public float moveAccelTime = 0.16f;
        public float moveDecelTime = 0.11f;

        // HEAD BOB, DRIVEN BY DISTANCE RATHER THAN BY TIME. That is the whole difference between a
        // gait and a floating camera: a time-driven bob keeps its rhythm when the player slows down,
        // so the feet and the cadence disagree and the eye reads it as the room moving instead of the
        // body. Advanced by metres travelled, a step happens every bobStepLength however fast you are
        // going, and the cadence follows the speed for free.
        //
        // It also means walkSpeed and sprintSpeed set the cadence, not these: at 1.35m per step a 2.5
        // walk is about 1.9 steps a second and a 4.5 sprint about 3.3. Change either speed and the
        // rhythm follows on its own, with nothing to retune here.
        public float bobStepLength = 1.35f;
        // Small enough to be felt rather than seen: 2.2cm of rise against a 1.6m eye is well under a
        // degree of view movement. Zero any of the three to remove that axis entirely.
        public float bobVertical = 0.022f;
        public float bobLateral = 0.013f;
        // The ROLL is what makes it read as a body rather than a camera on a rail, and it is the axis
        // to turn down first if it is too much - a lean is the part of a gait people notice being
        // simulated. Half a degree, synced to the sway so the head tips into the foot it is over.
        //
        // Note this is a change of intent: SetEyePose's roll was cutscene-only, on the grounds that
        // HandleLook writes a zero Z every frame and "any lean is dropped the instant control returns,
        // which is what we want". That was true when the only lean was a scripted one.
        public float bobRoll = 0.5f;

        // Footsteps, fired from the same step phase the bob is drawn from - so what you hear and what
        // you feel are the same event, not two systems that happen to agree. Cycled rather than
        // pitch-shifted: see Tools/generate_sfx.py.
        public AudioSource footstepSource;
        public AudioClip[] footstepClips;
        // Walking is quieter than running, and the gap is wide because it is doing double duty - it is
        // the only feedback that says which of the two speeds is currently on.
        public float footstepWalkVolume = 0.34f;
        public float footstepSprintVolume = 0.62f;

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

        // STANDING ON SOMETHING. Read by LoopManager at a cycle boundary, which holds the gas until
        // the player has actually landed in the room below rather than firing it at someone still
        // falling - the drop is the last thing they chose to do, and it should finish.
        public bool IsGrounded => controller != null && controller.isGrounded;
        // Metres walked, ever. The gait's phase, and the reason it is distance and not time.
        private float bobDistance;
        private int lastStepIndex;
        // The eye's CURRENT base height, eased between standing and crouching. The bob is added on top
        // of this rather than on top of standingEyeHeight, which is what keeps the two from fighting.
        private float eyeHeight;

        // The horizontal part of this frame's movement, kept so the FixedUpdate push can ask
        // whether the player is actually walking into anything. Pushing while stationary would let
        // a player stand in a crowd of balloons and blow them outwards for free.
        //
        // Also the RAMP'S STATE now, not just a readout: HandleMove eases this towards the direction
        // being asked for rather than assigning it. So it lags the keys by moveDecelTime on release,
        // which means the push survives a fraction of a second past letting go - correct, since the
        // player is still moving - and cannot be held open, because it decays to zero on its own.
        private Vector3 horizontalMove;
        private readonly Collider[] pushHits = new Collider[24];

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
            eyeHeight = standingEyeHeight;
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
            // NO POINTER TO LOCK ON A TOUCH SCREEN, so this whole branch has to stand aside there:
            // `Cursor.lockState` never becomes Locked on a phone, and the early return below would
            // then skip HandleLook on every single frame - a game that cannot turn its head.
            if (!GameInput.TouchActive && Cursor.lockState != CursorLockMode.Locked)
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
            // Adopted as the crouch ramp's current value too, so control returning after a cutscene
            // does not ease the eye from wherever the crouch last left it up to where the cutscene has
            // already put it. Whoever poses the eye owns both, or the two disagree on the handover.
            this.eyeHeight = eyeHeight;
            if (playerCamera == null) return;
            playerCamera.transform.localPosition = new Vector3(0f, eyeHeight, 0f);
            playerCamera.transform.localEulerAngles = new Vector3(pitch, 0f, rollDegrees);
        }

        // No deltaTime here, and that is correct rather than an oversight: GetAxis("Mouse X") is
        // already the delta accumulated since the last frame, so a given sweep of the mouse turns
        // the view by the same amount however fast the game is running.
        //
        // The sensitivity is a player setting rather than a serialized field. It cannot be a
        // constant - see GameSettings for what WebGL does to the numbers arriving here.
        private void HandleLook()
        {
            // Both axes, not just yaw: a pour that let the player keep looking up and down would
            // still be a pour they could aim away from the tank.
            float sensitivity = GameSettings.MouseSensitivity;
            // Whichever device is driving, in the same units: `GameInput.Look` hands over degrees
            // before sensitivity, which is what the mouse axis already was. A touch drag is
            // converted to match rather than this having to know which it is reading.
            Vector2 look = LookLocked ? Vector2.zero : GameInput.Look;
            float mouseX = look.x * sensitivity;
            float mouseY = look.y * sensitivity;

            transform.Rotate(Vector3.up * mouseX);

            // Only the yaw and the pitch VALUE are set here. Putting the pitch on the transform is
            // ApplyWalkPose's job, because the gait contributes a roll to the same euler angles and two
            // writers would mean whichever ran last silently won. ApplyWalkPose runs after this on
            // every path that reaches here, so nothing is left unposed.
            pitch = Mathf.Clamp(pitch - mouseY, -85f, 85f);
        }

        private void HandleMove()
        {
            // RAW, and the ramp is applied here instead. Unity's Horizontal/Vertical axes carry their
            // own smoothing (gravity 3, sensitivity 3 - a third of a second each way), and the whole
            // point is that this game's ramp is moveAccelTime/moveDecelTime and not a value inherited
            // from the Input Manager by accident. Reading the axes raw is what makes those two fields
            // the only thing in charge.
            // WITH A MAGNITUDE, which the keyboard cannot express and a thumb stick can: the pair
            // below is 0 or 1 per axis from keys, and anything in between from a stick. What uses it
            // is the ClampMagnitude further down, which was already written to take a vector of any
            // length - so analogue movement costs nothing here.
            Vector2 wanted = GameInput.Move;
            float x = wanted.x;
            float z = wanted.y;

            // ...UNLESS THE PLAYER IS IN THE MIDDLE OF SOMETHING THEY STARTED. Pouring a bucket takes
            // a second and a half and draws a stream from the bucket's lip to the tank's waterline;
            // walking off mid-pour drags that stream across the room and pours the water through open
            // air. Held STILL rather than interrupted, because the pour is a commitment the click
            // already made.
            //
            // The axes are zeroed rather than the whole method skipped, so the ramp still runs and the
            // player DECELERATES into the stop instead of being stapled to the floor - and gravity,
            // the ground check and the walk pose below all go on working.
            if (MovementLocked) { x = 0f; z = 0f; }

            // Held, not toggled. A toggle would survive the loop's teleport and leave the player
            // sprinting out of bed having never pressed anything.
            //
            // Crouch beats sprint when both are down. Holding two speed keys is an ambiguous request,
            // and the slower one is the safe reading - a player who wanted to go fast can let go of one
            // key, where a player who gets launched at sprint speed while crouched has been lied to.
            // `crouchKey` and `sprintKey` still name the desktop bindings; GameInput reads them and
            // answers for the stick as well - a run is the stick pushed PAST a threshold rather than
            // a button of its own, and there is no crouch on touch at all. See GameInput.
            bool crouching = GameInput.CrouchHeld;
            float topSpeed = crouching ? crouchSpeed
                          : GameInput.SprintHeld ? sprintSpeed
                          : walkSpeed;

            // Eased, not snapped, and this is the base the head bob rides on.
            float targetEye = crouching ? crouchEyeHeight : standingEyeHeight;
            eyeHeight = crouchTransition > 0f
                ? Mathf.MoveTowards(eyeHeight, targetEye,
                      Mathf.Abs(standingEyeHeight - crouchEyeHeight) / crouchTransition * Time.deltaTime)
                : targetEye;

            // ClampMagnitude, so holding two keys is not 1.41x speed on the diagonal.
            Vector3 target = Vector3.ClampMagnitude(transform.right * x + transform.forward * z, 1f) * topSpeed;

            // MoveTowards rather than Lerp: a linear approach ARRIVES, in a time this can state, where
            // an exponential one only ever gets close and makes "time to full speed" a lie.
            //
            // Which rate applies is decided by whether the target is faster than the current velocity,
            // so a turn at constant speed takes the decel rate. That is deliberate rather than
            // incidental - a direction change is a correction, and correcting wants the crisper of the
            // two numbers.
            bool speedingUp = target.sqrMagnitude > horizontalMove.sqrMagnitude;
            float rampTime = speedingUp ? moveAccelTime : moveDecelTime;
            // The rate is derived from the speed being ASKED FOR, so "0.16s to full speed" is true of
            // a walk and of a sprint alike rather than of only one of them.
            horizontalMove = rampTime > 0f
                ? Vector3.MoveTowards(horizontalMove, target, topSpeed / rampTime * Time.deltaTime)
                : target;

            Vector3 move = horizontalMove;

            if (controller.isGrounded && verticalVelocity < 0f)
                verticalVelocity = -1f;

            if (controller.isGrounded && GameInput.JumpPressed)
                verticalVelocity = jumpForce;

            verticalVelocity += gravity * Time.deltaTime;

            move.y = verticalVelocity;
            controller.Move(move * Time.deltaTime);

            // Last thing, and after the Move: the pose is about where the body ENDED UP this frame.
            ApplyWalkPose();
        }

        // Writes the camera's whole local pose - position and angles - and is the only thing that does
        // so during play. HandleLook maintains `pitch`; this puts it on the transform along with the
        // gait, which is why it runs after both. The wake-up owns the pose instead, through SetEyePose,
        // and this never runs then because HandleMove does not run without ControlEnabled.
        //
        // CameraShaker is unaffected and composes on top: it writes the RIG between the player and the
        // camera, precisely so the two cannot overwrite each other.
        private void ApplyWalkPose()
        {
            if (playerCamera == null) return;

            float speed = horizontalMove.magnitude;
            bool onFoot = controller.isGrounded;

            // Distance only accumulates on the ground - there are no footsteps in the air - so a jump
            // freezes the cadence where it was rather than restarting it on landing.
            if (onFoot) bobDistance += speed * Time.deltaTime;

            // Normalised against SPRINT, not against the current top speed. Against the current one a
            // full walk and a full sprint would both come out at 1 and bob identically; against the
            // maximum, a walk sits near 0.55 and running visibly throws the head about more. The
            // amplitude riding speed is also what makes the bob arrive and leave with the movement ramp
            // and need no fade of its own - come to a stop and it is already gone, mid-stride or not.
            float amount = onFoot && sprintSpeed > 0f ? Mathf.Clamp01(speed / sprintSpeed) : 0f;

            float phase = bobStepLength > 0f ? bobDistance / bobStepLength : 0f;
            Footstep(phase, amount);
            // Vertical peaks once per FOOTSTEP. The sway and the lean take TWO steps to come back,
            // because a sway belongs to a foot - left, then right. That two-to-one is the whole trick:
            // matched frequencies read as a bouncing ball, and the figure-of-eight reads as walking.
            float vertical = Mathf.Sin(phase * Mathf.PI * 2f) * bobVertical * amount;
            float lateral = Mathf.Sin(phase * Mathf.PI) * bobLateral * amount;
            float roll = -Mathf.Sin(phase * Mathf.PI) * bobRoll * amount;

            playerCamera.transform.localPosition = new Vector3(lateral, eyeHeight + vertical, 0f);
            playerCamera.transform.localEulerAngles = new Vector3(pitch, 0f, roll);
        }

        // A foot lands whenever the step phase crosses a whole number. Driven off the phase rather than
        // off a timer, so the sound cannot drift out of step with the bob it belongs to: they are the
        // same number read twice.
        //
        // The amount gate is what keeps a shuffle silent. Nudging the stick barely moves the phase, and
        // a footstep for two centimetres of travel is a footstep nobody took.
        private void Footstep(float phase, float amount)
        {
            if (footstepSource == null || footstepClips == null || footstepClips.Length == 0) return;
            if (amount < 0.18f) { lastStepIndex = Mathf.FloorToInt(phase); return; }

            int step = Mathf.FloorToInt(phase);
            if (step == lastStepIndex) return;
            lastStepIndex = step;

            // Cycled by step number, so consecutive footfalls are always different files - three of
            // them means the pattern only repeats every third step, and unevenly weighted clips stop
            // that being audible as a pattern at all.
            AudioClip clip = footstepClips[((step % footstepClips.Length) + footstepClips.Length) % footstepClips.Length];
            if (clip == null) return;

            // Pitch varies with the step too, not randomly: a random pitch per step reads as a broken
            // sample player, where a small alternation reads as two feet.
            footstepSource.pitch = 1f + (step % 2 == 0 ? 0.03f : -0.03f);
            footstepSource.PlayOneShot(clip, Mathf.Lerp(footstepWalkVolume, footstepSprintVolume, amount));
        }

        public void Teleport(Vector3 position, Quaternion rotation)
        {
            controller.enabled = false;
            transform.SetPositionAndRotation(position, rotation);
            verticalVelocity = 0f;
            // Cleared for the same reason verticalVelocity is, and it only became state worth clearing
            // when the ramp went in: this is called at the top of every iteration to put the player
            // back at the bed, and a velocity carried across that boundary would have them drift out
            // of bed for a tenth of a second before the wake-up takes control. Momentum is not one of
            // the things an iteration inherits.
            horizontalMove = Vector3.zero;
            // And the crouch, for the same reason: an iteration that ended crouched would begin with
            // the eye on the floor, easing up while the wake-up is trying to pose it.
            eyeHeight = standingEyeHeight;
            controller.enabled = true;
        }
    }
}
