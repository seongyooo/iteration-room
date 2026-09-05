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
        // **GRAPHICS: A RESOLUTION AND A PRESET, not a render scale.** The scale was one bare
        // percentage and play said the obvious thing about it - "I cannot tell how much changes
        // depending on how I set it". A resolution is a number people have read in graphics menus
        // for thirty years, and the preset carries the per-pixel work that actually costs the frame.
        // See `VideoSettings` for what each preset is and why the default moved.
        public Button resolutionDownButton;
        public Button resolutionUpButton;
        public Text resolutionValue;
        public Button fullscreenOnButton;
        public Button fullscreenOffButton;
        public Text fullscreenOnInk;
        public Text fullscreenOffInk;
        public Button[] qualityButtons;
        public Text[] qualityInks;

        public Button englishButton;
        public Button koreanButton;
        public Text englishInk;
        public Text koreanInk;

        public Button subtitlesOnButton;
        public Button subtitlesOffButton;
        public Text subtitlesOnInk;
        public Text subtitlesOffInk;

        // **ONE TAB PER GROUP, and the page shows one at a time.** It had grown to six rows, a
        // heading and eleven binding lines in a single column, which play called cluttered and was
        // right about. The groups are the ones a player already expects to find - what it looks
        // like, what it sounds like, how it is driven, and what it says.
        public CanvasGroup[] tabPages;
        public Button[] tabButtons;
        public Text[] tabInks;
        private int tab;

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
            if (resolutionDownButton != null)
                resolutionDownButton.onClick.AddListener(() => StepResolution(-1));
            if (resolutionUpButton != null)
                resolutionUpButton.onClick.AddListener(() => StepResolution(1));
            if (fullscreenOnButton != null)
                fullscreenOnButton.onClick.AddListener(() => SetFullscreen(true));
            if (fullscreenOffButton != null)
                fullscreenOffButton.onClick.AddListener(() => SetFullscreen(false));

            if (qualityButtons != null)
                for (int i = 0; i < qualityButtons.Length; i++)
                {
                    int level = i;
                    if (qualityButtons[i] != null)
                        qualityButtons[i].onClick.AddListener(() => SetQuality((GraphicsQuality)level));
                }

            if (tabButtons != null)
                for (int i = 0; i < tabButtons.Length; i++)
                {
                    int index = i;
                    if (tabButtons[i] != null)
                        tabButtons[i].onClick.AddListener(() => ShowTab(index));
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

            ShowTab(0);
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
            if (show) { ShowTab(tab); Refresh(); }
            else if (bindings != null) bindings.Cancel();
        }

        // Every control put back in step with what is actually stored.
        public void Refresh()
        {
            if (volumeSlider != null) volumeSlider.SetValueWithoutNotify(GameSettings.MasterVolume);
            if (sensitivitySlider != null)
                sensitivitySlider.SetValueWithoutNotify(GameSettings.MouseSensitivity);
            ShowVolume();
            ShowSensitivity();
            ShowResolution();
            ShowFullscreen();
            ShowQuality();
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

        // **THE LIST IS THE MONITOR'S OWN MODES, deduplicated and sorted.** `Screen.resolutions`
        // repeats every size once per refresh rate, so the raw list offers 1920x1080 four times and
        // reads as broken. Anything under 1280 wide is dropped: this game puts monospace type and a
        // grid of thin lines on screen, and below that the HUD stops being readable, which is not a
        // trade a menu should offer.
        private static Vector2Int[] modes;

        private static Vector2Int[] Modes()
        {
            if (modes != null) return modes;

            var seen = new System.Collections.Generic.List<Vector2Int>();
            foreach (Resolution r in Screen.resolutions)
            {
                if (r.width < 1280) continue;
                var size = new Vector2Int(r.width, r.height);
                if (!seen.Contains(size)) seen.Add(size);
            }
            // A headless or unusual display can report nothing usable; the current size is always a
            // valid answer and keeps the row from being empty.
            if (seen.Count == 0) seen.Add(new Vector2Int(Screen.width, Screen.height));
            seen.Sort((a, b) => (a.x * a.y).CompareTo(b.x * b.y));
            modes = seen.ToArray();
            return modes;
        }

        private void StepResolution(int by)
        {
            Vector2Int[] list = Modes();
            int at = 0;
            for (int i = 0; i < list.Length; i++)
                if (list[i].x == Screen.width && list[i].y == Screen.height) { at = i; break; }

            int next = Mathf.Clamp(at + by, 0, list.Length - 1);
            if (next == at) return;

            VideoSettings.SetResolution(list[next].x, list[next].y, VideoSettings.Fullscreen);
            ShowResolution();
        }

        private void ShowResolution()
        {
            if (resolutionValue != null)
                resolutionValue.text = Screen.width + " x " + Screen.height;
        }

        private void SetFullscreen(bool on)
        {
            VideoSettings.SetResolution(Screen.width, Screen.height, on);
            ShowFullscreen();
        }

        private void ShowFullscreen()
        {
            bool on = VideoSettings.Fullscreen;
            Dim(fullscreenOnInk, on);
            Dim(fullscreenOffInk, !on);
        }

        // **SAVED ON CHANGE, unlike volume and sensitivity.** These are the settings a player
        // reaches for while the game is misbehaving, and the run they are meant to rescue is the one
        // most likely to end in a hard quit rather than a tidy walk back to the title screen.
        private void SetQuality(GraphicsQuality level)
        {
            VideoSettings.Quality = level;
            VideoSettings.Save();
            ShowQuality();
        }

        private void ShowQuality()
        {
            if (qualityInks == null) return;
            for (int i = 0; i < qualityInks.Length; i++)
                Dim(qualityInks[i], (int)VideoSettings.Quality == i);
        }

        // One page up, the rest down, and the tab that is showing lit.
        public void ShowTab(int index)
        {
            tab = index;
            if (tabPages != null)
                for (int i = 0; i < tabPages.Length; i++)
                {
                    if (tabPages[i] == null) continue;
                    bool on = i == index;
                    tabPages[i].alpha = on ? 1f : 0f;
                    tabPages[i].blocksRaycasts = on;
                    tabPages[i].interactable = on;
                }
            if (tabInks != null)
                for (int i = 0; i < tabInks.Length; i++) Dim(tabInks[i], i == index);
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
