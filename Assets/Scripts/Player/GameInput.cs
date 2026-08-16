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
        public static Vector2 Move
        {
            get
            {
                Pump();
                if (TouchActive) return touch.Move;
                return new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
            }
        }

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
                return Input.GetKey(KeyCode.LeftShift);
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
                return Input.GetKey(KeyCode.LeftControl);
            }
        }

        public static bool JumpPressed
        {
            get
            {
                Pump();
                if (TouchActive) return touch.JumpPressed;
                return Input.GetButtonDown("Jump");
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
                return Input.GetKeyDown(KeyCode.E);
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
                return Input.GetMouseButtonDown(0);
            }
        }

        public static bool PausePressed
        {
            get
            {
                Pump();
                if (TouchActive && touch.PausePressed) return true;
                return Input.GetKeyDown(KeyCode.Escape);
            }
        }

        // ENDING AN ITERATION EARLY IS NOT HERE, and that is deliberate. `EndCycleControl` is a
        // uGUI element with its own IPointerDown/Up handlers, so a thumb held on it already works
        // through the EventSystem - it is the one control that needed nothing. See
        // TouchControls.OverInteractiveUI for the guard that stops that same touch turning the view.
    }
}
