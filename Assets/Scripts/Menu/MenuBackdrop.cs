using UnityEngine;
using UnityEngine.UI;

namespace IterationRoom
{
    // THE TITLE SCREEN'S BACKGROUND, DRIFTING SIDEWAYS.
    //
    // A still photograph of a room behind a menu reads as a loading screen. The same picture moving
    // slowly reads as a place the game is already running in - which is the whole trick Minecraft's
    // title screen turns, and it costs one sine wave.
    //
    // IT DRIFTS, IT DOES NOT SCROLL. The image is one capture of one room, so it cannot repeat; it
    // is drawn OVERSIZE and slid within that margin, turning around before it ever shows an edge.
    // `overscan` is that margin, as a fraction of the screen, and the pan is clamped to it rather
    // than trusted to stay inside - a background that flashes a black edge once every forty seconds
    // is worse than one that never moved.
    //
    // UNSCALED TIME, because the title screen has no game time. `Time.timeScale` is whatever the
    // last scene left it at, and a menu that stops moving because the game was paused before quitting
    // to it would be a puzzle nobody should have to solve.
    [RequireComponent(typeof(RectTransform))]
    public class MenuBackdrop : MonoBehaviour
    {
        // How far past the screen the image is drawn, per side, as a fraction of screen size. The
        // pan can never exceed this, so it is also the safety margin.
        public float overscan = 0.08f;

        // Seconds for one full there-and-back. Slow on purpose: fast enough to be alive, slow enough
        // that nobody watches it. Forty seconds is about the length of a title track's first verse.
        public float period = 44f;

        // A second, much slower vertical drift at a fraction of the horizontal one. Pure sideways
        // motion reads as a slide; a little Y makes it read as a camera breathing.
        public float verticalRatio = 0.22f;
        public float verticalPeriod = 67f;

        private RectTransform rect;
        private float phase;

        private void Awake()
        {
            rect = GetComponent<RectTransform>();
            // The two periods are deliberately not multiples of each other, so the pair does not
            // visibly repeat - but they DO both start at zero, which puts the image exactly centred
            // on the first frame. A menu that opens mid-drift looks like it was left running.
            phase = 0f;
            Apply();
        }

        private void OnEnable() => Apply();

        private void Update()
        {
            phase += Time.unscaledDeltaTime;
            Apply();
        }

        private void Apply()
        {
            if (rect == null) return;

            // The rect is stretched to the canvas, so its own size is the screen; the offsets below
            // are what push it out past the edges and then slide it back.
            float w = rect.rect.width, h = rect.rect.height;
            float marginX = w * overscan, marginY = h * overscan;

            float x = Mathf.Sin(phase / Mathf.Max(1f, period) * 2f * Mathf.PI) * marginX;
            float y = Mathf.Sin(phase / Mathf.Max(1f, verticalPeriod) * 2f * Mathf.PI)
                    * marginY * verticalRatio;

            // Clamped as well as bounded by construction: `overscan` is edited in the Inspector and a
            // value raised without also growing the image is exactly how an edge gets shown.
            rect.offsetMin = new Vector2(-marginX + Mathf.Clamp(x, -marginX, marginX),
                                         -marginY + Mathf.Clamp(y, -marginY, marginY));
            rect.offsetMax = new Vector2(marginX + Mathf.Clamp(x, -marginX, marginX),
                                         marginY + Mathf.Clamp(y, -marginY, marginY));
        }
    }
}
