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

        // THIS CYCLE FROM ITERATION 1, with every past self gone. Here rather than on a control in
        // the world because it is a thing done to the GAME rather than in it - see
        // `LoopManager.RequestCycleRestart` for what makes it necessary at all.
        public Button restartButton;
        public Button menuButton;
        public Button quitButton;

        // **THE SAME SETTINGS PAGE THE TITLE SCREEN SHOWS.** It used to be four of its six rows,
        // built inline here, with a private copy of every handler behind them - see `SettingsPanel`
        // for what that cost and why the render scale in particular could not stay behind.
        public Button settingsButton;
        public SettingsPanel settings;





        public bool IsPaused { get; private set; }

        // Which of the two faces of this overlay is up. The page and the buttons share one scrim, so
        // one has to stand down for the other.
        private void ShowSettings(bool show)
        {
            if (settings != null) settings.Show(show);
            SetButtons(!show);
        }

        private void SetButtons(bool on)
        {
            if (resumeButton != null) resumeButton.gameObject.SetActive(on);
            if (restartButton != null) restartButton.gameObject.SetActive(on);
            if (menuButton != null) menuButton.gameObject.SetActive(on);
            if (quitButton != null) quitButton.gameObject.SetActive(on);
            if (settingsButton != null) settingsButton.gameObject.SetActive(on);
        }

        // What the loop wanted control to be before the pause. Restored rather than forced true:
        // pausing during the wake-up must not hand the player a camera the loop had taken away.
        private bool controlBeforePause;

        private void Awake()
        {
            if (resumeButton != null) resumeButton.onClick.AddListener(Resume);
            if (restartButton != null) restartButton.onClick.AddListener(RestartCycle);
            if (menuButton != null) menuButton.onClick.AddListener(ToMainMenu);
            if (quitButton != null) quitButton.onClick.AddListener(Quit);

            // **THE PAGE IS SHARED; WHAT BACK DOES IS NOT.** Here it returns to the four buttons
            // rather than to a title-screen column - see `SettingsPanel.onBack`.
            if (settingsButton != null) settingsButton.onClick.AddListener(() => ShowSettings(true));
            if (settings != null) settings.onBack = () =>
            {
                GameSettings.Save();
                ShowSettings(false);
            };

            // The saved volume, pushed at the engine here as well as on the title screen: a run
            // started straight from the Editor never passes through the menu, and `AudioListener`'s
            // volume is a global nothing else initialises. The slider for it lives on the title
            // screen (MainMenu.settingsGroup); this only honours what it set.
            GameSettings.ApplyAudio();





            Apply(false);
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
            // **~~LOCKED OUT ONCE THE PLAYER HAS ESCAPED~~ OPEN AGAIN, 2026-09-01, by request.**
            //
            // It was shut on a real argument: the ending ran on unscaled time, so pausing over it
            // would freeze nothing, and Resume would hand back a room with no loop able to unfreeze
            // it. That was true when the ending was a scrim and a card - thirty seconds of watching
            // with no input.
            //
            // The ending is a place now. The player climbs down a ladder, reads a wall, walks out
            // through a breach and rides a cable car, and a game that will not take Escape for the
            // last five minutes of itself is a game that cannot be put down. So the two halves of
            // the old objection are both answered rather than ignored:
            //   - `EndingClock` is what those sequences time themselves on now, and it stops dead
            //     while this menu is up - so a pause really is a pause;
            //   - `controlBeforePause` was always captured and restored, so Resume hands back
            //     whatever control state the ending had at the moment it was interrupted.
            if (GameInput.PausePressed)
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
            // **THE OVERLAY ALWAYS OPENS ON THE BUTTONS, never on the settings page it was left
            // showing.** ESC is "let me out of this"; landing back on a page of sliders is not that.
            // Closing it is also what re-reads the stored values next time it is opened - see
            // `SettingsPanel.Show`, which does that on the way in rather than caching.
            ShowSettings(false);
            Apply(true);
        }

        public void Resume()
        {
            if (!IsPaused) return;

            Apply(false);
            if (playerController != null) playerController.ControlEnabled = controlBeforePause;
        }

        // UNPAUSED FIRST, THEN ASKED. The loop has to be running to hear this: the iteration
        // coroutine spins on `yield return null` while frozen, and everything the restart then drives
        // - the blink, the reset, the wake-up - is on scaled time and would stand still at zero.
        public void RestartCycle()
        {
            LoopManager loop = LoopManager.Instance;
            if (loop == null) return;

            Resume();
            // Refused by the loop when there is no iteration to interrupt, which is the honest place
            // for that test - this menu does not know what state the run is in.
            loop.RequestCycleRestart();
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
