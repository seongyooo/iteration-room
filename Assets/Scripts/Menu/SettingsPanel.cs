using UnityEngine;
using UnityEngine.UI;

namespace IterationRoom
{
    // **ONE SETTINGS PAGE, AND BOTH MENUS SHOW THE SAME ONE** (2026-09-05, by request).
    //
    // There were two. The title screen had the full page - language, subtitles, volume, sensitivity,
    // render scale and the key bindings - and the pause menu had four of those six built inline as
    // loose rows between RESUME and QUIT. Every one of the eight handlers behind them
    // (`SetLanguage`/`ShowLanguage` and its siblings) existed once in `MainMenu` and again in
    // `PauseMenu`, and adding a third control would have written each of them a third time.
    //
    // **THE ASYMMETRY WAS THE REAL COST, not the duplication.** A player mid-run could reach volume
    // and sensitivity but not their key bindings, and not the render scale - which is the one
    // setting somebody reaches for BECAUSE the game is running badly, and therefore the one they are
    // least able to go back to the title screen for. Settings you can only change before you start
    // are settings you cannot change when you find out you need them.
    //
    // So the page moved here, whole, and `SceneBuilder.BuildSettingsPage` builds it into whichever
    // canvas asked. `MainMenu` and `PauseMenu` now own one reference each and a call to `Show`.
    //
    // **IT READS `GameSettings` ON EVERY SHOW rather than caching.** Two copies of this page exist at
    // once in a running game - one in the menu scene, one on the HUD canvas - and either may have
    // been the last to write. Reading late is what keeps the second one from showing a stale value.
    public class SettingsPanel : MonoBehaviour
    {
        public CanvasGroup group;

        public Slider volumeSlider;
        public Text volumeValue;
        public Slider sensitivitySlider;
        public Text sensitivityValue;
        public Slider renderScaleSlider;
        public Text renderScaleValue;

        public Button englishButton;
        public Button koreanButton;
        public Text englishInk;
        public Text koreanInk;

        public Button subtitlesOnButton;
        public Button subtitlesOffButton;
        public Text subtitlesOnInk;
        public Text subtitlesOffInk;

        // The rebinding rows live on this page, so closing it has to call off any capture in
        // progress - otherwise the next key the player presses anywhere gets bound.
        public KeyBindingPanel bindings;

        // Whatever should happen when BACK is pressed - the title screen returns to its column, the
        // pause menu returns to its own buttons. The panel does not know which it is in.
        public Button backButton;
        [System.NonSerialized] public System.Action onBack;

        // The dimmed state a language or subtitle button wears when it is NOT the current choice.
        private const float Unchosen = 0.35f;

        private void Awake()
        {
            if (volumeSlider != null)
            {
                volumeSlider.minValue = 0f;
                volumeSlider.maxValue = 1f;
                volumeSlider.onValueChanged.AddListener(SetVolume);
            }
            if (sensitivitySlider != null)
            {
                sensitivitySlider.minValue = GameSettings.MinMouseSensitivity;
                sensitivitySlider.maxValue = GameSettings.MaxMouseSensitivity;
                sensitivitySlider.onValueChanged.AddListener(SetSensitivity);
            }
            if (renderScaleSlider != null)
            {
                renderScaleSlider.minValue = GameSettings.MinRenderScale;
                renderScaleSlider.maxValue = GameSettings.MaxRenderScale;
                renderScaleSlider.onValueChanged.AddListener(SetRenderScale);
            }

            if (englishButton != null)
                englishButton.onClick.AddListener(() => SetLanguage(GameLanguage.English));
            if (koreanButton != null)
                koreanButton.onClick.AddListener(() => SetLanguage(GameLanguage.Korean));
            if (subtitlesOnButton != null)
                subtitlesOnButton.onClick.AddListener(() => SetSubtitles(true));
            if (subtitlesOffButton != null)
                subtitlesOffButton.onClick.AddListener(() => SetSubtitles(false));

            if (backButton != null) backButton.onClick.AddListener(() => onBack?.Invoke());

            Show(false);
        }

        public bool IsOpen => group != null && group.blocksRaycasts;

        public void Show(bool show)
        {
            if (group != null)
            {
                group.alpha = show ? 1f : 0f;
                group.blocksRaycasts = show;
                group.interactable = show;
            }
            // Only on the way IN. Pulling the stored values back onto the controls as the page closes
            // would be work nobody sees, and it would fight a slider the player is still dragging.
            if (show) Refresh();
            else if (bindings != null) bindings.Cancel();
        }

        // Every control put back in step with what is actually stored.
        public void Refresh()
        {
            if (volumeSlider != null) volumeSlider.SetValueWithoutNotify(GameSettings.MasterVolume);
            if (sensitivitySlider != null)
                sensitivitySlider.SetValueWithoutNotify(GameSettings.MouseSensitivity);
            if (renderScaleSlider != null)
                renderScaleSlider.SetValueWithoutNotify(GameSettings.RenderScale);

            ShowVolume();
            ShowSensitivity();
            ShowRenderScale();
            ShowLanguage();
            ShowSubtitles();
        }

        private void SetVolume(float value)
        {
            GameSettings.MasterVolume = value;
            GameSettings.ApplyAudio();
            ShowVolume();
        }

        private void ShowVolume()
        {
            if (volumeValue != null)
                volumeValue.text = Mathf.RoundToInt(GameSettings.MasterVolume * 100f) + "%";
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

        // **SAVED ON CHANGE, unlike the two above.** Volume and sensitivity are saved when a menu
        // closes; this one is written straight through because it is the setting a player reaches
        // for while the game is misbehaving, and the run it is meant to rescue is the one most
        // likely to end in a hard quit rather than a tidy walk back to the title screen.
        private void SetRenderScale(float value)
        {
            GameSettings.RenderScale = value;
            ShowRenderScale();
            GameSettings.Save();
        }

        // Percent, because "0.75" means nothing and "75%" means three quarters of the pixels.
        private void ShowRenderScale()
        {
            if (renderScaleValue != null)
                renderScaleValue.text = Mathf.RoundToInt(GameSettings.RenderScale * 100f) + "%";
        }

        private void SetLanguage(GameLanguage value)
        {
            GameSettings.Language = value;
            ShowLanguage();
        }

        private void ShowLanguage()
        {
            bool korean = GameSettings.Language == GameLanguage.Korean;
            Dim(englishInk, !korean);
            Dim(koreanInk, korean);
        }

        private void SetSubtitles(bool on)
        {
            GameSettings.Subtitles = on;
            ShowSubtitles();
        }

        private void ShowSubtitles()
        {
            bool on = GameSettings.Subtitles;
            Dim(subtitlesOnInk, on);
            Dim(subtitlesOffInk, !on);
        }

        // The chosen one at full strength, the other faded. Alpha only - the ink colour is the
        // menu's and this must not decide it.
        private static void Dim(Text text, bool chosen)
        {
            if (text == null) return;
            Color c = text.color;
            text.color = new Color(c.r, c.g, c.b, chosen ? 1f : Unchosen);
        }
    }
}
