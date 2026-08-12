using UnityEngine;
using UnityEngine.UI;

namespace IterationRoom
{
    // A calibration step before the first iteration: look around the room, set the sensitivity,
    // press ENTER to start the loop.
    //
    // It exists because the pause-menu slider - which is the real fix for what WebGL does to mouse
    // deltas, see GameSettings - can only be reached by a player who has already spent an iteration
    // fighting the default. The first sixty seconds are the ones that decide whether someone keeps
    // playing, and on itch.io they were the ones being lost.
    //
    // IT RUNS INSIDE THE ROOM, ON THE REAL PLAYER, AND THAT IS THE SECOND ATTEMPT. The first put it
    // on the title screen and gave it something to look at: at first the menu's background still,
    // panned within about 16 degrees, which is not a test at all - sensitivity is the relationship
    // between hand travel and how far the world goes round, and it cannot be judged inside a window
    // narrower than one flick. Then Room1's baked reflection cubemap hung as a skybox, which does
    // turn a full circle but shows nothing: sampled, its floor face runs 0.940 to 0.944 and its
    // ceiling 0.690 to 0.734. The room is a white box, so its reflection probe is a blank field.
    // That probe exists to be smeared across glossy walls, not to be looked at.
    //
    // Here there is nothing to fake. The player is in a purpose-built room looking through the
    // actual player camera with the actual look code - so there is no fov to keep matched, no
    // exposure to guess, and the thing being calibrated is the thing being used. Control is left
    // fully on, walking included: the loop teleports everyone to the bed at the top of iteration 1
    // anyway, and feeling the movement speed is no bad thing either.
    //
    // ALMOST NONE OF IT IS ON THE SCREEN. The control list, the gauge and the value are on the
    // room's south wall - the wall the player spawns facing - and the step is ended by pressing E
    // at a plate below that display (CalibrationStartButton). The room is built out of wall
    // displays, so the facility explaining itself on one is the same move Room3 makes, and it
    // leaves the screen carrying a single line that only appears when the browser has refused
    // pointer capture, which is the one thing the wall cannot usefully say.
    //
    // Every other control is already silent here, because they all gate on
    // LoopManager.AcceptsInput and the clock has not started - E opens nothing, N charges nothing,
    // the mouse swings nothing. The sensitivity buttons are the deliberate exception.
    public class SensitivityCalibration : MonoBehaviour
    {
        // The one thing still drawn on the screen: the prompt to begin, which doubles as the
        // pointer-lock readout. Everything else moved onto the room's own wall - but this has to be
        // legible whatever the player happens to be facing, since it is how they leave.
        public CanvasGroup group;
        public Text lockHint;

        // The wall displays. The south wall carries the control list, the gauge and the value - the
        // gauge and value live there rather than on the screen because the buttons that drive them are
        // on that wall too, readout and control in one place. The two SIDE walls carry one control
        // each, sprint and crouch.
        //
        // An array because there are three of them and they are one thing: they light together and go
        // out together, and a player who found the sprint sign still lit after the run had started
        // would be reading an instruction from a screen that had finished talking.
        public CanvasGroup[] wallGroups;
        public Image fill;
        public Text valueLabel;

        // The rest of the HUD, switched off for the duration. See the note where SceneBuilder
        // fills it in for what is left on.
        public GameObject[] hideWhileActive;

        // Arrow keys and A/D were tried for this and had to go - both are bound to the Horizontal
        // axis, so every press that nudged the number also strafed the player. The wheel is the one
        // input on a mouse that is not already spoken for while looking around.
        public float wheelStep = 0.2f;

        public bool Confirmed { get; private set; }

        // Read by CalibrationButton, which cannot use LoopManager.AcceptsInput like everything else -
        // that is false for the whole of calibration, which is exactly when the ball must answer.
        public bool Active => active;

        private bool active;

        private void SetWallAlpha(float alpha)
        {
            if (wallGroups == null) return;
            foreach (CanvasGroup wall in wallGroups)
                if (wall != null) wall.alpha = alpha;
        }

        public void Begin()
        {
            active = true;
            Confirmed = false;
            if (group != null)
            {
                group.alpha = 1f;
                // Nothing here is clickable - with the pointer captured there is no cursor to click
                // with, which is why the sensitivity is on physical buttons in the room instead.
                group.blocksRaycasts = false;
            }
            SetWallAlpha(1f);
            SetHudVisible(false);
            Show();
        }

        public void End()
        {
            active = false;
            if (group != null) group.alpha = 0f;
            SetWallAlpha(0f);
            SetHudVisible(true);
            // Committed to disk here rather than on every wheel notch, for the reason GameSettings
            // documents: each save is a storage flush on WebGL. This is the one exit from the page,
            // so nothing can leave without passing through it.
            GameSettings.Save();
        }

        private void Update()
        {
            if (!active) return;

            // FirstPersonController owns the lock and the click-to-retry - browsers only grant
            // pointer capture inside a user gesture, so the one it asks for on Start is routinely
            // refused. All this does is say so, since a player looking at a room that will not turn
            // has no way to guess that a click is what fixes it.
            // The screen carries ONE line, and only when it has something to say. Pointer capture
            // is the browser's to give and it is routinely refused; a player looking at a room that
            // will not turn has no way to guess that a click fixes it. Everything else the step has
            // to tell them is on the wall.
            bool locked = Cursor.lockState == CursorLockMode.Locked;
            if (lockHint != null) lockHint.text = locked ? string.Empty : "CLICK TO ENABLE MOUSE LOOK";

            Adjust();
            Show();
        }

        // Called by CalibrationStartButton, which is the only way out of this step. Gated on the
        // pointer being captured, so nobody can commit a number they were never able to test - if
        // the browser has refused the lock the button does nothing and the screen line says why.
        public void Confirm()
        {
            if (Cursor.lockState == CursorLockMode.Locked) Confirmed = true;
        }

        private void Adjust()
        {
            float delta = Input.mouseScrollDelta.y * wheelStep;
            if (!Mathf.Approximately(delta, 0f)) GameSettings.MouseSensitivity += delta;
        }

        private void SetHudVisible(bool visible)
        {
            if (hideWhileActive == null) return;
            foreach (GameObject go in hideWhileActive)
                if (go != null) go.SetActive(visible);
        }

        private void Show()
        {
            float value = GameSettings.MouseSensitivity;

            if (fill != null)
                fill.fillAmount = Mathf.InverseLerp(
                    GameSettings.MinMouseSensitivity, GameSettings.MaxMouseSensitivity, value);

            if (valueLabel != null) valueLabel.text = value.ToString("0.00");
        }
    }
}
