using UnityEngine;
using UnityEngine.UI;

namespace IterationRoom
{
    // THE CAMERA CREW: the HUD off, and the eye off the player's head.
    //
    // WHY THIS IS A COMPONENT AND NOT A CHECKLIST. Every frame of a trailer is a frame the game drew,
    // and the game draws a clock, a carried slot, an end-cycle control and an E disc over it. Turning
    // those off by hand means finding five objects on a canvas and remembering to put them back -
    // which is the kind of thing that gets got wrong once, in the middle of the only good take.
    //
    // WHY IT IS NOT GATED ON `AcceptsInput`. CLAUDE.md 1.8 requires every Update reading a key to
    // gate on `LoopManager.AcceptsInput`, and this is a deliberate exception in the company of the
    // calibration plate: the whole point of the rig is to work while the loop runs, while it is
    // paused, and before it has started. It reads no GAME verb - see below - so it can never take a
    // press away from something that does.
    //
    // WHY THE TWO TOGGLE KEYS ARE CONSTS HERE AND NOT IN `InputBindings`. That rule exists because a
    // `public KeyCode` field is "a second answer the player cannot reach". These are the opposite
    // case: they are not verbs, and a player must NOT reach them - a settings page listing HIDE HUD
    // and DETACH CAMERA is a capture rig shipped as a feature. `TakenByAVerb` below is what keeps the
    // exception honest: F9 and F10 are both in `InputBindings.Listenable`, so if somebody has bound a
    // verb onto one, the rig gives the key up rather than fighting the game for it.
    //
    // Nothing the rig does is recorded: it writes no `CarryEvent`, sets no signal bit and moves no
    // item, so a timeline made with the HUD hidden is the same timeline as one made with it up.
    public sealed class CaptureRig : MonoBehaviour
    {
        // ~~A THREE-STATE CYCLE (Full -> Clean -> Bare)~~ **REPLACED WITH TWO PLAIN TOGGLES, and the
        // reason is worth keeping.** The cycle read as broken in use: press once and the HUD goes,
        // press again expecting it back and you get BARE - which is also hidden, and hides more. Only
        // the third press returned it. Two of the three states looked identical from the chair, so
        // the control had no discoverable way back.
        //
        // The card still gets its own switch, because it is the one piece of HUD that is also a
        // trailer asset - red type announcing ITERATION 12 over a white room is the clearest single
        // frame this game has. But independent, not a cycle: each key is its own answer.

        // **PLAIN UNMODIFIED LETTERS, and that is the third attempt at this.** Both earlier schemes
        // failed before Unity ever saw the key:
        //
        // - **F9..F12 are media keys** on most laptops. They need Fn held to arrive as function keys
        //   at all; without it the press goes to the system volume, and the only clue is a volume
        //   overlay appearing while the rig looks dead.
        // - **Alt+letter is a menu accelerator.** Windows routes Alt combinations to the window's
        //   menu bar, and the Editor has one, so the chord is swallowed upstream of the game.
        //
        // What is left is a bare letter with no modifier on it: nothing in the OS or the Editor
        // claims one, and `TakenByAVerb` keeps it honest if a verb is ever bound onto it. Chosen
        // adjacent on the home row so they can be found without looking away from the shot.
        private const KeyCode HudKey = KeyCode.K;
        private const KeyCode CameraKey = KeyCode.L;
        private const KeyCode CardKey = KeyCode.J;
        // HOLD, not toggle - the only one of the four that is. Chosen next to the WASD block it
        // works alongside rather than next to K/L/J.
        private const KeyCode DollyKey = KeyCode.O;
        // Kept as aliases for a keyboard with Fn-lock on, where the F-keys do arrive normally.
        private const KeyCode HudAlias = KeyCode.F9;
        private const KeyCode CameraAlias = KeyCode.F10;

        [Header("What gets hidden")]
        // Named by SceneBuilder rather than gathered by type or found by name, for the reason `Cycle`
        // names its own particle systems (CLAUDE.md 2): a rig that swept the canvas would also hide
        // the eyelids, the gas and the pause menu, and would silently start hiding whatever gets
        // added to the canvas next.
        public GameObject[] hud;
        public GameObject[] cards;

        [Header("The detached camera")]
        public Camera playerCamera;
        public Camera captureCamera;

        [Header("Flight")]
        // Metres per second at the default step. Slower than `walkSpeed` on purpose: a camera move
        // reads as either deliberate or as a stumble, and 2.5 is already brisk for something the eye
        // is meant to follow. The scroll wheel scales it in flight - see `Fly`.
        public float flySpeed = 1.6f;
        public float boostMultiplier = 4f;
        public float minSpeed = 0.15f;
        public float maxSpeed = 12f;
        // Degrees per unit of mouse delta. Deliberately NOT `GameSettings.MouseSensitivity`: that is
        // tuned for finding things quickly in a room, and a camera turning at gameplay speed is the
        // most amateur-looking thing a trailer can do.
        public float lookSensitivity = 0.6f;

        [Header("The auto pull-back dolly")]
        // WHY THIS EXISTS: a hand-flown wide shot carries every twitch of the mouse into the take,
        // and the money shot (six of you, swinging) is exactly the frame that most needs to hold
        // still. Holding O glides the camera backward - and a little upward, "backed off and
        // slightly high" per the shotlist - along wherever it was AIMED the moment the key went
        // down. Release and it eases back to a stop instead of a hard cut in the motion.
        //
        // IT REUSES `speed`, the same number the scroll wheel already tunes, rather than inventing
        // a second speed to remember - so "how far it goes" is just how long O stays down at
        // whatever cruise speed the shot already wanted, and that answers "per room" for free: a
        // cramped room and the tree hall both get their own comfortable speed from the same wheel,
        // with no per-room number anywhere in code.
        //
        // MOUSE AND WASD ARE LOCKED OUT FOR THE WHOLE GLIDE, ramp and release included - the point
        // is a stretch of footage with nothing but the dolly moving the camera. Aim before pressing
        // O; steering mid-glide is exactly the jitter this is for removing.
        public float dollyRiseRatio = 0.25f;
        public float dollyEaseTime = 1.1f;

        public bool HudHidden { get; private set; }
        public bool CardHidden { get; private set; }
        public bool Detached { get; private set; }
        // True from the moment O goes down until the eased-out speed reaches zero - not just while
        // held - so the release glide still counts as "the dolly is driving" and Fly() leaves the
        // mouse alone through it.
        public bool Dollying => dollyHeld || dollySpeed > 0.01f;

        private float yaw;
        private float pitch;
        private float speed;
        private bool armed;

        private bool dollyHeld;
        private float dollySpeed;
        private float dollySpeedVelocity;
        private Vector3 dollyDirection;

        // WHAT ACTUALLY GETS TURNED OFF: the `Graphic`s, never the GameObjects.
        //
        // `SetActive(false)` was the obvious mechanism and it is wrong twice over. `IterationLabel.
        // Show` calls `StartCoroutine`, and Unity throws on a coroutine started against an inactive
        // object - so hiding the card would log an exception at every single iteration boundary, in
        // exactly the takes it exists for. And deactivating `TouchControls` unregisters it from
        // `GameInput` on the way down, which is a lifecycle change the rig has no business making.
        //
        // Disabling the graphics changes nothing except what is drawn: Updates keep running,
        // coroutines keep ticking, registrations stand, and anything that writes its own
        // `CanvasGroup.alpha` every frame can carry on doing so against a Graphic that draws nothing.
        private Graphic[] hudGraphics;
        private Graphic[] cardGraphics;

        private void Awake()
        {
            // EDITOR AND DEVELOPMENT BUILDS ONLY. The component still exists in a release player -
            // deleting it there would leave SceneBuilder's wiring pointing at nothing - but it reads
            // no keys and touches no camera, so a player cannot stumble into it.
            armed = Application.isEditor || Debug.isDebugBuild;
            speed = flySpeed;

            hudGraphics = GraphicsUnder(hud);
            cardGraphics = GraphicsUnder(cards);

            // SAYS IT IS ALIVE, AND SAYS WHICH KEYS. Two rounds of "the rig does nothing" were both
            // keys never arriving rather than the rig being broken, and there was no way to tell those
            // apart from the chair. One line in the console at startup separates them: if this is not
            // there, the rig is not running; if it is there and a key does nothing, the key is being
            // eaten upstream.
            if (armed)
                Debug.Log($"[CaptureRig] armed. {KeyName(HudKey)}: HUD, {KeyName(CameraKey)}: camera, "
                        + $"{KeyName(CardKey)}: iteration card, hold {KeyName(DollyKey)} while detached: "
                        + "pull-back dolly. "
                        + $"({KeyName(HudAlias)}/{KeyName(CameraAlias)} also work if Fn-lock is on.) "
                        + $"{hudGraphics.Length} HUD graphics, {cardGraphics.Length} card graphics.");
        }

        private static string KeyName(KeyCode key) => InputBindings.KeyLabel(key);

        private static Graphic[] GraphicsUnder(GameObject[] roots)
        {
            var found = new System.Collections.Generic.List<Graphic>();
            if (roots == null) return found.ToArray();

            foreach (GameObject go in roots)
                if (go != null)
                    // Inactive ones included: the touch layer's artwork and the two prompt discs are
                    // off on a desktop and would otherwise be missed, then switched on mid-take by
                    // whichever component owns them.
                    found.AddRange(go.GetComponentsInChildren<Graphic>(true));

            return found.ToArray();
        }

        private void Update()
        {
            if (!armed) return;

            if (Hit(HudKey, HudAlias)) SetHudHidden(!HudHidden);
            if (Hit(CameraKey, CameraAlias)) SetDetached(!Detached);
            if (Hit(CardKey)) SetCardHidden(!CardHidden);

            if (Detached) Fly();
        }

        // EVERY WAY OUT LANDS SOMEWHERE, which for this rig means the player gets their camera and
        // their keyboard back. `GameInput.Suspended` is a static, and a domain reload is not
        // guaranteed to clear it - with Enter Play Mode Options on, it does not - so stopping play
        // while detached would otherwise leave the next session unable to move.
        private void OnDisable()
        {
            SetDetached(false);
            // AND UNCONDITIONALLY, because `SetDetached` gives up early if the cameras have gone -
            // which is exactly what has happened by the time this runs on a scene teardown. Restoring
            // a destroyed camera is not possible and does not matter; leaving the keyboard suspended
            // into the next session does.
            GameInput.Suspended = false;
        }

        // The key, or its F-key alias where there is one. Both are checked against `InputBindings`
        // first, so a player who has bound a verb onto one keeps the verb and the rig stands aside.
        private static bool Hit(KeyCode key, KeyCode alias = KeyCode.None)
        {
            if (Input.GetKeyDown(key) && !TakenByAVerb(key)) return true;
            return alias != KeyCode.None && Input.GetKeyDown(alias) && !TakenByAVerb(alias);
        }

        private void SetHudHidden(bool hide)
        {
            HudHidden = hide;
            // Re-enabled in one sweep on the way back. Anything that owns its own graphic's enabled
            // state re-asserts it on its next update - `IterationLabel` sets `cycleLabel.enabled` at
            // every iteration - and until then the group's alpha is 0 anyway.
            if (!hide) Draw(hudGraphics, true);
            Debug.Log($"[CaptureRig] HUD {(hide ? "HIDDEN" : "SHOWN")}");
        }

        private void SetCardHidden(bool hide)
        {
            CardHidden = hide;
            if (!hide) Draw(cardGraphics, true);
            Debug.Log($"[CaptureRig] ITERATION card {(hide ? "HIDDEN" : "SHOWN")}");
        }

        // FORCED EVERY FRAME WHILE HIDDEN, in LateUpdate so it lands after the components that own
        // these graphics have had their say. A one-shot disable would be undone the first time
        // `ControlHintDisplay` showed a prompt or `IterationLabel` announced a cycle - which is to
        // say, in the middle of the take.
        private void LateUpdate()
        {
            if (!armed) return;
            if (HudHidden) Draw(hudGraphics, false);
            if (CardHidden) Draw(cardGraphics, false);
        }

        private static void Draw(Graphic[] graphics, bool visible)
        {
            if (graphics == null) return;
            foreach (Graphic g in graphics)
                if (g != null && g.enabled != visible) g.enabled = visible;
        }

        private void SetDetached(bool detach)
        {
            if (detach == Detached) return;
            if (playerCamera == null || captureCamera == null) return;

            Detached = detach;

            if (detach)
            {
                // Start where the player is standing, so a detach is a handover rather than a cut to
                // somewhere else in the building. Matching the optics too: a wider or narrower lens
                // than the game's would make the footage stop being footage OF the game.
                Transform eye = playerCamera.transform;
                captureCamera.transform.SetPositionAndRotation(eye.position, eye.rotation);
                captureCamera.fieldOfView = playerCamera.fieldOfView;
                captureCamera.nearClipPlane = playerCamera.nearClipPlane;
                captureCamera.farClipPlane = playerCamera.farClipPlane;

                Vector3 angles = eye.eulerAngles;
                pitch = angles.x > 180f ? angles.x - 360f : angles.x;
                yaw = angles.y;
                speed = flySpeed;
            }
            else
            {
                // GOING BACK TO THE PLAYER. Left running, a glide from the last take would resume
                // mid-air the next time the camera detaches, aimed at nothing this shot chose.
                dollyHeld = false;
                dollySpeed = 0f;
                dollySpeedVelocity = 0f;
            }

            // THE COMPONENT, NEVER THE GAMEOBJECT. The `AudioListener` lives on `PlayerCamera`, so
            // deactivating that object would record a silent trailer of a game whose PA announcer is
            // half of what the facility is. Disabling the Camera alone leaves the listener running,
            // and leaves `PlayerLookup.Eye` resolving - `GetComponentInChildren` returns a disabled
            // component on an active object - so the mirrors and the prompts keep their referent.
            playerCamera.enabled = !detach;
            captureCamera.enabled = detach;

            // ONE LEVER FOR THE WHOLE KEYBOARD. `GameInput` is the single place every verb is read
            // (the seventeen fixtures that used to poll `Input` all go through it), so suspending it
            // there freezes the player, the hand and all eight E fixtures at once. Disabling
            // `FirstPersonController` instead would stop the walking and leave E still picking things
            // up off the floor beside a player who is not there any more.
            //
            // Escape stays live: `GameInput.PausePressed` checks the raw key beside the bound one,
            // which is the safety rail 3 asks for and the way out if the rig ever misbehaves.
            GameInput.Suspended = detach;

            Debug.Log($"[CaptureRig] camera: {(detach ? "DETACHED - player frozen" : "back on the player")}");
        }

        private void Fly()
        {
            // UNSCALED, so the camera still flies while the game is paused. A frozen tableau of six
            // past selves mid-swing is a shot the running game will not hold still for.
            float dt = Time.unscaledDeltaTime;
            Transform cam = captureCamera.transform;

            // SCROLL ALWAYS TUNES `speed`, dolly running or not - it is what the dolly's own cruise
            // speed is read from, so easing it up or down mid-glide is a real creative option and
            // not a bug.
            float scroll = Input.mouseScrollDelta.y;
            if (!Mathf.Approximately(scroll, 0f))
                speed = Mathf.Clamp(speed * Mathf.Pow(1.15f, scroll), minSpeed, maxSpeed);

            HandleDolly(cam);
            if (Dollying)
            {
                cam.position += dollyDirection * dollySpeed * dt;
                return;
            }

            yaw += Input.GetAxis("Mouse X") * lookSensitivity;
            pitch = Mathf.Clamp(pitch - Input.GetAxis("Mouse Y") * lookSensitivity, -89f, 89f);
            cam.rotation = Quaternion.Euler(pitch, yaw, 0f);

            // READ STRAIGHT OFF `InputBindings`, going round `GameInput` - legitimate here and
            // nowhere else, because the rig is what just told `GameInput` to answer nothing. It means
            // the flight keys are the operator's OWN movement keys: rebind WASD and the camera
            // follows, rather than the rig keeping a private copy of the defaults.
            Vector3 move = Vector3.zero;
            if (Input.GetKey(InputBindings.Get(GameAction.MoveForward))) move += cam.forward;
            if (Input.GetKey(InputBindings.Get(GameAction.MoveBack)))    move -= cam.forward;
            if (Input.GetKey(InputBindings.Get(GameAction.MoveRight)))   move += cam.right;
            if (Input.GetKey(InputBindings.Get(GameAction.MoveLeft)))    move -= cam.right;
            // World up, not the camera's: a rise that tilts with the pitch is impossible to fly
            // level, and level is what nearly every shot wants.
            if (Input.GetKey(InputBindings.Get(GameAction.Jump)))        move += Vector3.up;
            if (Input.GetKey(InputBindings.Get(GameAction.Crouch)))      move -= Vector3.up;

            if (move.sqrMagnitude < 0.0001f) return;

            float rate = speed * (Input.GetKey(InputBindings.Get(GameAction.Sprint)) ? boostMultiplier : 1f);
            cam.position += move.normalized * rate * dt;
        }

        // CAPTURES ITS DIRECTION ONCE, on the down-press, and never reads the camera's facing again
        // until the next press. Reading it every frame would let the mouse steer the glide, which is
        // the exact jitter this exists to remove - aim happens before O, not during it.
        private void HandleDolly(Transform cam)
        {
            if (Input.GetKeyDown(DollyKey) && !TakenByAVerb(DollyKey))
            {
                dollyHeld = true;

                Vector3 forward = cam.forward;
                Vector3 flat = new Vector3(forward.x, 0f, forward.z);
                // Levelled off, so retreating never dives into the floor or climbs into the ceiling
                // just because the shot was aimed up or down slightly. The rise on top of it is
                // `dollyRiseRatio`'s job, not the pitch the operator happened to be holding.
                Vector3 flatBack = flat.sqrMagnitude > 0.0001f ? -flat.normalized : -forward;
                dollyDirection = (flatBack + Vector3.up * dollyRiseRatio).normalized;
            }
            else if (Input.GetKeyUp(DollyKey))
            {
                dollyHeld = false;
            }

            // SAME EASE CONSTANT BOTH WAYS: ramping up to speed reads as a deliberate start and
            // ramping down after release reads as a deliberate stop, rather than a cut in the motion
            // either way a hand-flown shot would have.
            float target = dollyHeld ? speed : 0f;
            dollySpeed = Mathf.SmoothDamp(dollySpeed, target, ref dollySpeedVelocity, dollyEaseTime);
        }

        // F9 and F10 are both bindable (`InputBindings.Listenable` offers F1..F12), so the rig has to
        // check rather than assume. Walked only on a press of that exact key, so it costs nothing.
        private static bool TakenByAVerb(KeyCode key)
        {
            foreach (GameAction action in InputBindings.All)
                if (InputBindings.Get(action) == key)
                {
                    Debug.LogWarning($"[CaptureRig] {InputBindings.KeyLabel(key)} is bound to "
                                   + $"{InputBindings.Label(action)}, so the rig is leaving it alone. "
                                   + "Rebind that verb to use the capture controls.");
                    return true;
                }

            return false;
        }
    }
}
