using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace IterationRoom
{
    // The title screen. Its whole job is to hold the player outside the loop until they choose to
    // enter it, and to cover the load while the room comes in.
    //
    // The background is a still of the room from the foot of the bed - the exact frame the game
    // opens on - captured by SceneBuilder at build time. It is a separate lightweight scene rather
    // than a state of the room, which is what makes the loading bar real: IterationRoom is a
    // 306,000-triangle bed, 182 wall panels, seventy balloons and a pile of shaders, and it takes
    // a genuine moment to come in.
    public class MainMenu : MonoBehaviour
    {
        public string gameScene = "IterationRoom";

        public CanvasGroup menuGroup;
        public CanvasGroup loadingGroup;
        public Button playButton;
        public Button quitButton;

        // Straight to the cycle boundary, with cycle 1 already finished. A development shortcut and
        // labelled as one - see DebugStart for why eight minutes of play per test is the thing it
        // exists to avoid.
        public Button testBoundaryButton;
        public Image loadingFill;
        public Text loadingLabel;

        // A floor on how long the bar is up. It does NOT inflate the reported progress - see
        // LoadGame, which shows the *lesser* of real progress and elapsed fraction - it only stops
        // a fast machine from flashing the whole load past in two frames, which reads as a glitch
        // rather than as a load.
        public float minimumLoadingTime = 0.9f;
        public float fadeSpeed = 3f;

        private bool starting;

        private void Awake()
        {
            // Wired here rather than as persistent listeners on the buttons: a listener added from
            // an editor script has to be serialized through UnityEventTools, and this is one line.
            if (playButton != null) playButton.onClick.AddListener(Play);
            if (quitButton != null) quitButton.onClick.AddListener(Quit);
            if (testBoundaryButton != null) testBoundaryButton.onClick.AddListener(PlayFromBoundary);
        }

        private void Start()
        {
            // The game locks and hides the cursor and Unity does not put it back across a scene
            // load, so returning to this scene without this leaves a menu you cannot click.
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            if (menuGroup != null) menuGroup.alpha = 1f;
            if (loadingGroup != null)
            {
                loadingGroup.alpha = 0f;
                loadingGroup.blocksRaycasts = false;
            }

            SetProgress(0f);
        }

        private void Update()
        {
            if (starting) return;
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) Play();
            // Return starts the game; the shortcut is deliberately click-only, so it cannot be
            // reached by the key somebody presses to start playing.
        }

        public void Play()
        {
            if (starting) return;
            // Cleared on the normal route as well as set on the other one: the flag is static, so a
            // test run followed by PLAY without leaving the editor would otherwise start the real
            // game at the boundary.
            DebugStart.AtCycleBoundary = false;
            starting = true;
            StartCoroutine(LoadGame());
        }

        public void PlayFromBoundary()
        {
            if (starting) return;
            DebugStart.AtCycleBoundary = true;
            starting = true;
            StartCoroutine(LoadGame());
        }

        public void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private IEnumerator LoadGame()
        {
            if (menuGroup != null) menuGroup.blocksRaycasts = false;

            // The title and the buttons go, the background stays: the last thing on screen before
            // the room loads is the room, which is also the first thing after. What greets the
            // player on the other side is the calibration step, which lives in the room scene -
            // see SensitivityCalibration for why it is not here.
            yield return Fade(menuGroup, 0f);

            if (loadingGroup != null) loadingGroup.alpha = 1f;

            AsyncOperation op = SceneManager.LoadSceneAsync(gameScene);
            // Withheld so the bar owns the moment of arrival rather than the scene dropping in
            // under a half-drawn one.
            op.allowSceneActivation = false;

            float started = Time.unscaledTime;
            while (op.progress < 0.9f || Time.unscaledTime - started < minimumLoadingTime)
            {
                // Unity reports 0..0.9 while activation is withheld and never reaches 1, so it is
                // rescaled here; a bar that stops dead at 90% reads as a failed load.
                float loaded = Mathf.Clamp01(op.progress / 0.9f);
                float waited = minimumLoadingTime > 0f
                    ? Mathf.Clamp01((Time.unscaledTime - started) / minimumLoadingTime)
                    : 1f;

                // The LESSER of the two, so the bar never claims more than has actually happened.
                SetProgress(Mathf.Min(loaded, waited));
                yield return null;
            }

            SetProgress(1f);
            // One frame at 100% before the switch, or the bar's last state is never drawn.
            yield return null;

            op.allowSceneActivation = true;
        }

        private void SetProgress(float t)
        {
            if (loadingFill != null) loadingFill.fillAmount = t;
            if (loadingLabel != null) loadingLabel.text = $"LOADING {Mathf.RoundToInt(t * 100f)}%";
        }

        private IEnumerator Fade(CanvasGroup group, float target)
        {
            if (group == null) yield break;

            while (!Mathf.Approximately(group.alpha, target))
            {
                // Unscaled: nothing here touches Time.timeScale today, but a menu that stops
                // working because something else paused the game is a poor way to discover that.
                group.alpha = Mathf.MoveTowards(group.alpha, target, fadeSpeed * Time.unscaledDeltaTime);
                yield return null;
            }
        }
    }
}
