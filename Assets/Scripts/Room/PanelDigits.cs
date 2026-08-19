using UnityEngine;

namespace IterationRoom
{
    // THE NUMBER, DRAWN ONTO THE SCALE'S OWN DISPLAY PANEL.
    //
    // Not quads laid over the panel and not a canvas floating above it - the model's own display
    // renderer, showing a texture this component paints. Nothing is added to the scene: the scale has
    // exactly the parts it was exported with, and one of them now says something.
    //
    // WHY IT NEEDED THE MESH'S UVs REBUILT, which is the part worth knowing. `weigh_scale.glb` maps
    // its green display patch to a tiny window of an atlas - u 0.688..0.75, v 0.546..0.69 - and, more
    // to the point, U DOES NOT RUN ACROSS THE PANEL: the same u appears at both ends of it, because
    // the patch was UV'd as repeated strips of a flat colour rather than as a surface anything is
    // drawn on. Measured off the file rather than guessed. So no tiling or offset could put readable
    // digits on it, and `SceneBuilder` gives that submesh fresh UVs taken from its own footprint - the
    // same geometry, mapped so that a texture lands on it square.
    //
    // SEVEN-SEGMENT, PAINTED WITH `SetPixels32`. A bitmap font would be a second asset to keep and an
    // LCD is seven bars; the texture is small and is rewritten only when the DISPLAYED value changes,
    // so a scale with something standing on it costs nothing per frame.
    public class PanelDigits : MonoBehaviour
    {
        public Renderer panel;
        public int digitCount = 4;
        public int decimalsAfter = 1;

        [Header("The panel")]
        public int textureWidth = 512;
        public int textureHeight = 256;
        public Color background = new Color(0.035f, 0.055f, 0.045f);
        public Color lit = new Color(0.25f, 1f, 0.45f);
        // The unlit bars of a real display are not invisible - they are the segment sitting there dark,
        // and they are most of what makes an LCD read as one rather than as marks on a panel.
        public Color unlit = new Color(0.075f, 0.13f, 0.10f);

        private static readonly int[] Glyphs =
        {
            0b0111111, 0b0000110, 0b1011011, 0b1001111, 0b1100110,
            0b1101101, 0b1111101, 0b0000111, 0b1111111, 0b1101111,
        };

        private Texture2D canvas;
        private Color32[] pixels;
        private Material material;
        private float shown = float.NaN;
        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");

        private void Awake()
        {
            if (panel == null) { enabled = false; return; }

            canvas = new Texture2D(textureWidth, textureHeight, TextureFormat.RGBA32, false)
            {
                // POINT filtering, because a seven-segment display has hard edges and bilinear turns
                // a 6px bar into a smear at the range this is read from.
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = "PanelDigits",
            };
            pixels = new Color32[textureWidth * textureHeight];

            // ITS OWN MATERIAL INSTANCE. `panel.material` instantiates on first access, which is what
            // is wanted here - the glb's material is a shared sub-asset, and writing a texture into it
            // would put this scale's reading on every copy of the model that ever exists.
            material = panel.material;

            Blank();
        }

        private void OnDestroy()
        {
            if (canvas != null) Destroy(canvas);
            if (material != null) Destroy(material);
        }

        public void Show(float value)
        {
            if (pixels == null) return;

            float unit = Mathf.Pow(10f, decimalsAfter);
            float quantised = Mathf.Round(value * unit) / unit;
            if (quantised == shown) return;
            shown = quantised;

            Fill(background);

            int scaled = Mathf.RoundToInt(Mathf.Abs(quantised) * unit);
            int cap = (int)Mathf.Pow(10f, digitCount);
            // OVERFLOW SHOWS ALL EIGHTS - what an instrument does when it is out of range, and honest
            // where a wrapped number would be a lie about the weight.
            bool over = scaled >= cap;

            // The layout, derived rather than written: digits across the width with a margin, each
            // twice as tall as it is wide, which is the proportion a real display uses.
            int margin = textureWidth / 24;
            int pitch = (textureWidth - margin * 2) / digitCount;
            int digitW = Mathf.RoundToInt(pitch * 0.70f);
            int digitH = Mathf.Min(textureHeight - margin * 2, digitW * 2);
            int bar = Mathf.Max(2, Mathf.RoundToInt(digitW * 0.18f));
            int baseY = (textureHeight - digitH) / 2;

            for (int d = 0; d < digitCount; d++)
            {
                // Digit 0 is the RIGHTMOST, so the number is right-aligned however many figures it
                // has - which is what every real readout does.
                int left = textureWidth - margin - pitch * (d + 1) + (pitch - digitW) / 2;
                int value10 = over ? 8 : (scaled / (int)Mathf.Pow(10f, d)) % 10;

                // LEADING ZEROS ARE BLANK, except across the decimal point: "0.4" keeps its zero,
                // where "04.0" is not a number anybody writes. A blank digit still draws its UNLIT
                // segments, because that is what an unlit digit looks like.
                bool blank = !over && d > decimalsAfter && scaled < (int)Mathf.Pow(10f, d);
                int mask = blank ? 0 : Glyphs[value10];

                DrawDigit(left, baseY, digitW, digitH, bar, mask);

                if (!over && d == decimalsAfter)
                    Rect(left + digitW + bar / 2, baseY, bar, bar, lit);
            }

            canvas.SetPixels32(pixels);
            canvas.Apply(false);
            if (material.HasProperty(BaseMapId)) material.SetTexture(BaseMapId, canvas);
            if (material.HasProperty(MainTexId)) material.SetTexture(MainTexId, canvas);
        }

        // Everything dark. An unattended scale shows an unlit panel rather than a zero - a 0.0 sitting
        // there says the instrument is reading nothing, not that nothing is on it.
        public void Blank()
        {
            if (pixels == null) return;
            shown = float.NaN;

            Fill(background);

            int margin = textureWidth / 24;
            int pitch = (textureWidth - margin * 2) / digitCount;
            int digitW = Mathf.RoundToInt(pitch * 0.70f);
            int digitH = Mathf.Min(textureHeight - margin * 2, digitW * 2);
            int bar = Mathf.Max(2, Mathf.RoundToInt(digitW * 0.18f));
            int baseY = (textureHeight - digitH) / 2;

            for (int d = 0; d < digitCount; d++)
            {
                int left = textureWidth - margin - pitch * (d + 1) + (pitch - digitW) / 2;
                DrawDigit(left, baseY, digitW, digitH, bar, 0);
            }

            canvas.SetPixels32(pixels);
            canvas.Apply(false);
            if (material != null)
            {
                if (material.HasProperty(BaseMapId)) material.SetTexture(BaseMapId, canvas);
                if (material.HasProperty(MainTexId)) material.SetTexture(MainTexId, canvas);
            }
        }

        // The colour of the lit segments, so the panel can go amber when the target is hit.
        public void SetInk(Color colour)
        {
            lit = colour;
            shown = float.NaN;     // force the next Show to repaint
        }

        //      0
        //     ---
        //  5 |   | 1
        //     ---   <- 6
        //  4 |   | 2
        //     ---
        //      3
        private void DrawDigit(int x, int y, int w, int h, int bar, int mask)
        {
            int half = (h - bar) / 2;
            int inner = w - bar;

            Seg(0, mask, x + bar / 2, y + h - bar, inner, bar);
            Seg(1, mask, x + w - bar, y + half + bar / 2, bar, half - bar / 2);
            Seg(2, mask, x + w - bar, y + bar / 2, bar, half - bar / 2);
            Seg(3, mask, x + bar / 2, y, inner, bar);
            Seg(4, mask, x, y + bar / 2, bar, half - bar / 2);
            Seg(5, mask, x, y + half + bar / 2, bar, half - bar / 2);
            Seg(6, mask, x + bar / 2, y + half, inner, bar);
        }

        private void Seg(int index, int mask, int x, int y, int w, int h) =>
            Rect(x, y, w, h, (mask & (1 << index)) != 0 ? lit : unlit);

        private void Rect(int x, int y, int w, int h, Color colour)
        {
            Color32 c = colour;
            int x1 = Mathf.Min(textureWidth, x + w), y1 = Mathf.Min(textureHeight, y + h);
            for (int py = Mathf.Max(0, y); py < y1; py++)
            {
                int row = py * textureWidth;
                for (int px = Mathf.Max(0, x); px < x1; px++) pixels[row + px] = c;
            }
        }

        private void Fill(Color colour)
        {
            Color32 c = colour;
            for (int i = 0; i < pixels.Length; i++) pixels[i] = c;
        }
    }
}
