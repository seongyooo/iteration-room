using UnityEngine;

namespace IterationRoom
{
    // EVERY VERB THIS GAME HAS, asked once and answered by whichever device is driving.
    //
    // WHY THIS EXISTS. Seventeen files polled `Input` directly - eight of them for the same E press -
    // so adding touch meant editing every fixture in the building and getting all of them right. The
    // verbs themselves are few (move, look, interact, use, jump, sprint, pause, end iteration), and
    // naming them once is what lets a second input device be one new file rather than a sweep.
    //
    // **SAMPLED ONCE PER FRAME, LAZILY, AND THAT IS LOAD-BEARING.** Every fixture polls E in its own
    // `Update`, and Unity's script execution order is arbitrary - so a touch flag raised part-way
    // through the frame would be seen by whichever fixtures happened to run after it and missed by
    // the rest. That is the same coin-toss `PlayerLookup.ClaimInteract` exists to stop, one level
    // lower down. `Pump` takes the reading on the first access of each frame and everything after it
    // sees the identical answer, whoever asked first.
    //
    // Keyboard and mouse are untouched by any of this: with no touch controls registered every
    // property below is the expression that used to be written at the call site.
    public static class GameInput
    {
        // Registered by TouchControls itself, the way CarryableItem registers with ItemRegistry -
        // there is nothing for SceneBuilder to wire and nothing to keep in step.
        private static TouchControls touch;

        public static void Register(TouchControls controls) => touch = controls;
        public static void Unregister(TouchControls controls)
        {
            if (touch == controls) touch = null;
        }

        // THE TOUCH LAYER IS DRIVING. False on a desktop, and false on a touch device while the pause
        // menu is up - see TouchControls.Active, which is what decides.
        public static bool TouchActive => touch != null && touch.Active;

        // THE HARDWARE, not the moment. True on a phone whether or not the game is currently taking
        // input - which is what makes it usable before the loop has started, where `TouchActive`
        // cannot be. `LoopManager` asks this to skip the mouse-only sensitivity step.
        public static bool TouchDevice => touch != null && touch.Present;

        private static int sampledFrame = -1;

        // First read of the frame takes the reading. Public because TouchControls calls it too, as a
        // backstop for a frame on which nothing else asks - an edge must not survive into the next
        // frame just because nobody was listening.
        public static void Pump()
        {
            if (sampledFrame == Time.frameCount) return;
            sampledFrame = Time.frameCount;
            if (touch != null) touch.Sample();
        }

        // WHERE THE PLAYER IS ASKING TO GO, as a direction with a MAGNITUDE. The keyboard can only
        // ever answer 0 or 1 per axis; a stick answers anything in between, and the caller scales
        // its speed by it - which is the whole of analogue movement.
        //
        // **FOUR KEYS, NOT `GetAxisRaw`, since the bindings page existed.** The legacy Input Manager's
        // axes are configured in `ProjectSettings/InputManager.asset` and cannot be reassigned at
        // runtime, so an axis read is a movement key the player is not allowed to change. Polling the
        // four bound keys is exactly equivalent for a keyboard - `GetAxisRaw` is already unsmoothed
        // 0/±1 - and it is what makes WASD rebindable at all.
        //
        // What it drops: the joystick contribution the stock `Horizontal`/`Vertical` axes carry. That
        // costs nothing today, because nothing else in this project reads a gamepad - look is
        // `Mouse X`/`Mouse Y` and every verb below is a key. **If a gamepad is ever supported, it is
        // a third branch here beside touch**, not a return to the axis, or movement silently stops
        // obeying the bindings page again.
        public static Vector2 Move
        {
            get
            {
                Pump();
                if (TouchActive) return touch.Move;

                float x = (Held(GameAction.MoveRight) ? 1f : 0f) - (Held(GameAction.MoveLeft) ? 1f : 0f);
                float y = (Held(GameAction.MoveForward) ? 1f : 0f) - (Held(GameAction.MoveBack) ? 1f : 0f);
                return new Vector2(x, y);
            }
        }

        // The two shapes every verb below is one of. Kept here so no call site repeats the lookup and
        // so "which key" has exactly one answer per verb.
        private static bool Held(GameAction action) => Input.GetKey(InputBindings.Get(action));
        private static bool Pressed(GameAction action) => Input.GetKeyDown(InputBindings.Get(action));

        // IN DEGREES BEFORE SENSITIVITY, which is what `GetAxis("Mouse X")` already is once the
        // caller multiplies by it. The touch side converts a drag in pixels into the same units, so
        // `FirstPersonController.HandleLook` needs no idea which device it is reading.
        public static Vector2 Look
        {
            get
            {
                Pump();
                if (TouchActive) return touch.Look;
                return new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"));
            }
        }

        // PUSH THE STICK FURTHER, RATHER THAN A SECOND BUTTON. A run is "more of the same input", not
        // a different one, so it costs no screen space and needs no thumb of its own - which is the
        // argument for it over a sprint button, and it is why the touch layer answers this rather
        // than the controller working it out.
        public static bool SprintHeld
        {
            get
            {
                Pump();
                if (TouchActive) return touch.Sprint;
                return Held(GameAction.Sprint);
            }
        }

        // **NO CROUCH ON TOUCH**, by design rather than omission: it is the one verb this game never
        // requires - nothing is under anything - so on a screen where every button costs a thumb it
        // is the first thing to go. The key still crouches on a desktop.
        public static bool CrouchHeld
        {
            get
            {
                Pump();
                if (TouchActive) return false;
                return Held(GameAction.Crouch);
            }
        }

        public static bool JumpPressed
        {
            get
            {
                Pump();
                if (TouchActive) return touch.JumpPressed;
                return Pressed(GameAction.Jump);
            }
        }

        // THE PRESS EIGHT FIXTURES ARE ALL ASKING ABOUT. True for the WHOLE of the frame it happened
        // in and no part of any other, exactly like `GetKeyDown` - see the note on Pump for why that
        // is not automatic once the answer comes from a screen.
        public static bool InteractPressed
        {
            get
            {
                Pump();
                if (TouchActive) return touch.InteractPressed;
                return Pressed(GameAction.Interact);
            }
        }

        // E HELD RATHER THAN E PRESSED, for the one fixture that is not a press: room3-2N's levers
        // are pulled and KEPT pulled, and the panels they raise follow that state frame by frame.
        //
        // **A HOLD IS NOT A SECOND KIND OF PRESS.** Which fixture a press belongs to is arbitrated
        // once, on the rising edge, through `PlayerLookup.PressGoesTo` - see CLAUDE.md SS1.2. This
        // answers only "is that key still down", and a fixture that grabbed the press is the only one
        // entitled to ask. Nothing may use this to START an interaction.
        public static bool InteractHeld
        {
            get
            {
                Pump();
                if (TouchActive) return touch.InteractPressed;
                return Held(GameAction.Interact);
            }
        }

        // "Do the thing this object is FOR" - swing the pin, place a piece, stand a bucket down. The
        // left button on a desktop.
        public static bool UsePressed
        {
            get
            {
                Pump();
                if (TouchActive) return touch.UsePressed;
                return Pressed(GameAction.Use);
            }
        }

        // **ESCAPE ALWAYS PAUSES, whatever PAUSE is bound to**, and that is a safety rail rather than
        // an oversight. Pause is the only verb whose loss cannot be recovered from inside the game:
        // every other one just stops working, but a player who cannot open the menu cannot get back
        // to the page that would fix it. So the binding is honoured *in addition to* Escape, never
        // instead of it - and `InputBindings.Listenable` correspondingly refuses to bind Escape onto
        // anything else, so the rail can never be the key some other verb is sitting on.
        public static bool PausePressed
        {
            get
            {
                Pump();
                if (TouchActive && touch.PausePressed) return true;
                return Pressed(GameAction.Pause) || Input.GetKeyDown(KeyCode.Escape);
            }
        }

        // THE LOOP'S OWN CONTROL, held rather than pressed - `EndCycleControl` charges while it is
        // down. It is here for the reason everything else is: it was the last verb still naming its
        // own key, on a serialised field that the bindings page had no way to reach.
        public static bool EndIterationHeld
        {
            get
            {
                Pump();
                return Held(GameAction.EndIteration);
            }
        }

        // ENDING AN ITERATION EARLY IS NOT HERE, and that is deliberate. `EndCycleControl` is a
        // uGUI element with its own IPointerDown/Up handlers, so a thumb held on it already works
        // through the EventSystem - it is the one control that needed nothing. See
        // TouchControls.OverInteractiveUI for the guard that stops that same touch turning the view.
    }
}
