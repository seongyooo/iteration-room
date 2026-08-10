using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace IterationRoom
{
    // Escape freezes the room and puts RESUME / MAIN MENU / QUIT over it.
    //
    // The freeze is one line - Time.timeScale = 0 - and that works because every moving part in
    // this project already runs on scaled time: the loop's clock, the wake-up's WaitForSeconds,
    // the collapse ramp, the balloons' physics, and CameraShaker's Perlin sampling of Time.time.
    // None of them needs to know a pause exists.
    //
    // What a zero time scale does NOT stop is Update, so every script that reads a raw key would
    // still see it - E would open drawers, N would end the cycle, the mouse would swing the pin.
    // Those all gate on LoopManager.AcceptsInput instead of IterationRunning for that reason.
    public class PauseMenu : MonoBehaviour
    {
        public string menuScene = "MainMenu";
        public FirstPersonController playerController;

        public CanvasGroup group;
        public Button resumeButton;
        public Button menuButton;
        public Button quitButton;

        // The pause overlay is also the settings screen, because it is the only place the player
        // can reach with the cursor free and the room still in front of them - which is exactly
        // what tuning a look sensitivity needs. Resume, look around, pause again, adjust.
        public Slider sensitivitySlider;
        public Text sensitivityValue;

        public bool IsPaused { get; private set; }

        // What the loop wanted control to be before the pause. Restored rather than forced true:
        // pausing during the wake-up must not hand the player a camera the loop had taken away.
        private bool controlBeforePause;

        private void Awake()
        {
            if (resumeButton != null) resumeButton.onClick.AddListener(Resume);
            if (menuButton != null) menuButton.onClick.AddListener(ToMainMenu);
            if (quitButton != null) quitButton.onClick.AddListener(Quit);

            if (sensitivitySlider != null)
            {
                sensitivitySlider.minValue = GameSettings.MinMouseSensitivity;
                sensitivitySlider.maxValue = GameSettings.MaxMouseSensitivity;
                // The listener is attached AFTER the first seed below, because a Slider raises
                // onValueChanged on assignment - seeding through an attached listener would write
                // the slider's own starting value straight back over the saved one.
                SyncSensitivity();
                sensitivitySlider.onValueChanged.AddListener(SetSensitivity);
            }
            ShowSensitivity();

            Apply(false);
        }

        // Re-read on every open, not seeded once in Awake, and that distinction is the whole of a
        // bug this shipped with: Awake runs at scene load, the calibration step runs from
        // LoopManager.Start() afterwards, so a slider seeded in Awake held the load-time value and
        // never showed what the player had just set. Opening the pause menu reported the old number
        // and dragging it snapped away from the real one.
        //
        // It is the other half of the lesson the deleted LightingTuner taught. "Seed the panel from
        // the live state" is not a thing to do once - anything that can change the value behind the
        // panel's back makes a single seed stale, and here something does.
        private void SyncSensitivity()
        {
            if (sensitivitySlider != null)
                sensitivitySlider.SetValueWithoutNotify(GameSettings.MouseSensitivity);
            ShowSensitivity();
        }

        private void SetSensitivity(float value)
        {
            GameSettings.MouseSensitivity = value;
            ShowSensitivity();
        }

        private void ShowSensitivity()
        {
            if (sensitivityValue != null)
                sensitivityValue.text = GameSettings.MouseSensitivity.ToString("0.00");
        }

        // Time scale and the audio pause are global, not per-scene. Leaving either set on the way
        // out would freeze and silence whatever loads next, and this runs on a scene change as
        // well as on a quit - so it is the one place the restore cannot be missed.
        private void OnDestroy()
        {
            Time.timeScale = 1f;
            AudioListener.pause = false;
        }

        private void Update()
        {
            // Locked out once the player has escaped. The ending owns the screen from that point
            // and there is no loop left running - pausing over it and pressing Resume would hand
            // back a frozen room with nothing able to unfreeze it, which is a soft lock at the one
            // moment the game must not have one.
            bool endingRunning = LoopManager.Instance != null && LoopManager.Instance.RunOver;

            if (Input.GetKeyDown(KeyCode.Escape) && !endingRunning)
            {
                if (IsPaused) Resume();
                else Pause();
            }

            // Re-asserted every frame rather than once on open. The loop hands control back at the
            // end of every wake-up regardless of what this wants, and HandleLook uses no deltaTime
            // at all - so a single frame of control returning under a frozen game swings the view
            // with the very mouse movement the player is making to reach Resume.
            if (IsPaused && playerController != null) playerController.ControlEnabled = false;
        }

        public void Pause()
        {
            if (IsPaused) return;

            controlBeforePause = playerController == null || playerController.ControlEnabled;
            // Re-read here, every open. See SyncSensitivity.
            SyncSensitivity();
            Apply(true);
        }

        public void Resume()
        {
            if (!IsPaused) return;

            Apply(false);
            if (playerController != null) playerController.ControlEnabled = controlBeforePause;
        }

        public void ToMainMenu()
        {
            // Unfrozen before the load, not after: a scene that arrives at timeScale 0 has no way
            // to start itself moving again.
            Apply(false);
            SceneManager.LoadScene(menuScene);
        }

        public void Quit()
        {
            Apply(false);
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void Apply(bool paused)
        {
            IsPaused = paused;

            // Committed on the way out rather than on every drag frame: onValueChanged fires each
            // frame the slider is held, and on WebGL each save is a storage flush. Every route out
            // of the menu - Resume, Main Menu, Quit - passes through here, so none of them can
            // lose the setting.
            if (!paused) GameSettings.Save();

            Time.timeScale = paused ? 0f : 1f;
            // The announcer and the room tone are not on scaled time and would carry on talking
            // over a frozen room.
            AudioListener.pause = paused;

            // FirstPersonController locks and hides the cursor; the menu needs it back to be
            // clickable, and the game needs it gone again on resume.
            Cursor.lockState = paused ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = paused;

            if (group != null)
            {
                group.alpha = paused ? 1f : 0f;
                // Both matter: alpha 0 still receives clicks, so without this the invisible
                // overlay would keep swallowing everything the moment it had been opened once.
                group.blocksRaycasts = paused;
                group.interactable = paused;
            }

            LoopManager.Instance?.SetPaused(paused);
        }
    }
}
