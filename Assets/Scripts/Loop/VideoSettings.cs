using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace IterationRoom
{
    public enum GraphicsQuality { Low = 0, Medium = 1, High = 2 }

    // **THE GRAPHICS SETTINGS, AND WHY THE DEFAULT IS NOT WHERE IT WAS** (2026-09-05, by request).
    //
    // The game shipped at `defaultIsNativeResolution` fullscreen with the Ultra quality level and 4x
    // MSAA, and play found the built player heavier than the Editor - which reads backwards until you
    // notice they are not drawing the same picture. Windowed at 1280x720 the same build was smooth.
    // The cost is pixels.
    //
    // **BUT THE DEFAULT DOES NOT BELONG ON RESOLUTION.** A game that launches below the desktop
    // resolution reads as broken, and a player who does not know to open the settings simply decides
    // it looks bad. Most of them are at 1920x1080 anyway, so native IS 1080p for the majority.
    //
    // The expensive thing is per-pixel work. Measured: an essentially EMPTY dark room cost 6.6ms of
    // GPU time in a windowed Game view. None of that is geometry - it is MSAA, SSAO, soft shadows
    // and the HDR resolve, and every one of them scales with the pixel count. So the default comes
    // down on the per-pixel work and leaves the resolution alone:
    //
    //   LOW      no MSAA, no SSAO, hard shadows, 85% render scale
    //   MEDIUM   2x MSAA, SSAO, soft shadows, full scale        <- the default
    //   HIGH     4x MSAA, SSAO, high soft shadows, full scale   <- what it used to always be
    //
    // 4x to 2x is the single biggest saving available. This building is thin black grid lines on
    // white, which is the worst case for aliasing, so MSAA cannot simply go - but 2x holds those
    // edges nearly as well as 4x for half the cost, and 4x is still there for anyone who wants it.
    //
    // **WHAT IS NOT KNOWN: whether MEDIUM holds 60fps on an ordinary laptop.** It cannot be known
    // from one machine. What IS known is that this machine struggled at what MEDIUM replaces, which
    // is reason enough to move the default and no reason at all to believe a particular number.
    // Named Video rather than Graphics because `UnityEngine.Rendering.GraphicsSettings` exists and any
    // file with that using would find two of them.
    public static class VideoSettings
    {
        public const GraphicsQuality DefaultQuality = GraphicsQuality.Medium;

        private const string QualityKey = "iteration.quality";
        private const string WidthKey = "iteration.screenWidth";
        private const string HeightKey = "iteration.screenHeight";
        private const string FullscreenKey = "iteration.fullscreen";

        private static int quality = -1;

        public static GraphicsQuality Quality
        {
            get
            {
                if (quality < 0)
                    quality = Mathf.Clamp(PlayerPrefs.GetInt(QualityKey, (int)DefaultQuality),
                                          0, (int)GraphicsQuality.High);
                return (GraphicsQuality)quality;
            }
            set
            {
                quality = Mathf.Clamp((int)value, 0, (int)GraphicsQuality.High);
                Apply();
            }
        }

        // **THE PIPELINE ASSET IS WHERE ALL OF THIS LIVES, and in a player that is a copy in
        // memory** - the change dies with the process, which is what makes it safe to drive from a
        // menu. In the EDITOR it dirties `IterationURP.asset`, so a value fiddled with while testing
        // can end up committed. Check that file before committing if it ever shows as modified.
        public static void Apply()
        {
            var urp = UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline
                      as UniversalRenderPipelineAsset;
            if (urp == null) return;

            switch (Quality)
            {
                case GraphicsQuality.Low:
                    urp.msaaSampleCount = 1;          // 1 means off, not 1x
                    urp.renderScale = 0.85f;
                    break;
                case GraphicsQuality.High:
                    urp.msaaSampleCount = 4;
                    urp.renderScale = 1f;
                    break;
                default:
                    urp.msaaSampleCount = 2;
                    urp.renderScale = 1f;
                    break;
            }

            // SSAO and the shadow quality live on the renderer's feature list and its shadow block
            // rather than here, and neither is reachable through a public setter. They follow the
            // quality LEVEL instead - see `ApplyQualityLevel`.
            ApplyQualityLevel();
        }

        // Unity's own quality levels carry the shadow and anti-aliasing tiers that URP does not
        // expose. The names in QualitySettings are Very Low..Ultra; these three map onto the ones
        // this project actually has content for.
        private static void ApplyQualityLevel()
        {
            string[] names = QualitySettings.names;
            string want = Quality == GraphicsQuality.Low ? "Medium"
                        : Quality == GraphicsQuality.High ? "Ultra" : "Very High";
            for (int i = 0; i < names.Length; i++)
            {
                if (names[i] != want) continue;
                // false: do not wait for the next frame. The menu is showing the result immediately.
                QualitySettings.SetQualityLevel(i, false);
                return;
            }
        }

        // **RESOLUTION IS STORED ONLY WHEN THE PLAYER HAS CHOSEN ONE.** Zero means "whatever the
        // desktop is", which is what `defaultIsNativeResolution` already does and what a first run
        // should keep doing - storing a number on the first launch would freeze one machine's
        // desktop into the save and follow the player to a different monitor.
        public static void ApplyResolution()
        {
            int w = PlayerPrefs.GetInt(WidthKey, 0);
            int h = PlayerPrefs.GetInt(HeightKey, 0);
            if (w <= 0 || h <= 0) return;

            bool full = PlayerPrefs.GetInt(FullscreenKey, 1) != 0;
            SetResolution(w, h, full);
        }

        // What the player last CHOSE, which is not what `Screen` reports: `SetResolution` applies
        // at the end of the frame, and in the Editor it does nothing at all. A menu that reads the
        // screen back cannot show the choice that was just made.
        public static Vector2Int ChosenResolution
        {
            get
            {
                int w = PlayerPrefs.GetInt(WidthKey, 0);
                int h = PlayerPrefs.GetInt(HeightKey, 0);
                return w > 0 && h > 0 ? new Vector2Int(w, h)
                                      : new Vector2Int(Screen.width, Screen.height);
            }
        }

        public static bool Fullscreen
        {
            get => PlayerPrefs.GetInt(FullscreenKey, 1) != 0;
            set => SetResolution(Screen.width, Screen.height, value);
        }

        public static void SetResolution(int width, int height, bool fullscreen)
        {
            PlayerPrefs.SetInt(WidthKey, width);
            PlayerPrefs.SetInt(HeightKey, height);
            PlayerPrefs.SetInt(FullscreenKey, fullscreen ? 1 : 0);
            PlayerPrefs.Save();

            // ExclusiveFullScreen changes the display mode and can black the screen for a moment;
            // borderless is what a windowed game on a modern desktop wants and what alt-tabbing out
            // of a paused game needs to stay quick.
            Screen.SetResolution(width, height,
                fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed);
        }

        public static void Save()
        {
            if (quality >= 0) PlayerPrefs.SetInt(QualityKey, quality);
            PlayerPrefs.Save();
        }
    }
}
