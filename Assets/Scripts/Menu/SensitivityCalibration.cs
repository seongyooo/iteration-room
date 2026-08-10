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
    // Here there is nothing to fake. The player is standing at the foot of the bed in the finished,
    // lit room, looking through the actual player camera with the actual look code - so there is no
    // fov to keep matched, no exposure to guess, and the thing being calibrated is the thing being
    // used. Control is left fully on, walking included: the loop teleports everyone back to the bed
    // at the top of iteration 1 anyway, Room1's door will not open while the clock is stopped, and
    // feeling the movement speed is no bad thing either.
    //
    // Every other control is already silent here, because they all gate on
    // LoopManager.AcceptsInput and the clock has not started - E opens nothing, N charges nothing,
    // the mouse swings nothing.
    public class SensitivityCalibration : MonoBehaviour
    {
        public CanvasGroup group;
        public Image fill;
        public Text valueLabel;
        public Text lockHint;

        // The rest of the HUD, switched off for the duration. See the note where SceneBuilder
        // fills it in for why it is "everything but two" rather than a list.
        public GameObject[] hideWhileActive;

        // The wheel is the ONLY way to change the value, and the reason is that the player is
        // walking around while they do it. Arrow keys and A/D were offered at first and had to go:
        // both are bound to the Horizontal axis, so every press that nudged the number also
        // strafed the player, which reads as the setting having moved the room.
        public float wheelStep = 0.2f;

        public bool Confirmed { get; private set; }

        private bool active;

        public void Begin()
        {
            active = true;
            Confirmed = false;
            if (group != null)
            {
                group.alpha = 1f;
                // Nothing here is clickable - with the pointer captured there is no cursor to click
                // with, which is the whole reason the wheel does the adjusting.
                group.blocksRaycasts = false;
            }
            SetHudVisible(false);
            Show();
        }

        public void End()
        {
            active = false;
            if (group != null) group.alpha = 0f;
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
            bool locked = Cursor.lockState == CursorLockMode.Locked;
            if (lockHint != null)
                lockHint.text = locked ? "[ENTER]  TO BEGIN" : "CLICK TO ENABLE MOUSE LOOK";

            Adjust();
            Show();

            // Gated on the capture, so nobody can confirm a number they were never able to test.
            if (locked && (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)))
                Confirmed = true;
        }

        private void Adjust()
        {
            float delta = Input.mouseScrollDelta.y * wheelStep;
            if (!Mathf.Approximately(delta, 0f))
                GameSettings.MouseSensitivity += delta;
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
