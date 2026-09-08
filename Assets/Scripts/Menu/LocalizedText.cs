using UnityEngine;
using UnityEngine.UI;

namespace IterationRoom
{
    // A PIECE OF UI TEXT THAT KNOWS WHICH STRING IT IS, rather than which words.
    //
    // `SceneBuilder` still authors the English into the `Text` itself, so the scene reads correctly
    // with this component stripped and a build with `Loc` deleted is an English build. This only
    // overwrites it - on enable, and again whenever the language changes under it.
    //
    // **IT ALSO OWNS THE FONT, and that is not a nicety.** The UI is set in JetBrains Mono, which has
    // no Hangul at all: switching the language without switching the face renders every Korean string
    // as a row of empty boxes. So the font is part of what "this text is in Korean" means, and the two
    // cannot be allowed to move separately.
    public class LocalizedText : MonoBehaviour
    {
        public Text target;
        public string key;

        // Layout, not content: `MakeMenuButton` indents its label so the words form one column down
        // the left edge. Kept out of the string table so a translator cannot lose it.
        public string prefix = "";

        // The face this text was authored in, restored when the language goes back to English. Captured
        // rather than re-derived, because the menu uses three weights of JetBrains Mono and this
        // component has no way to know which one it was handed.
        private Font authoredFont;
        private FontStyle authoredStyle;
        private bool captured;

        private void OnEnable()
        {
            if (target == null) target = GetComponent<Text>();
            if (!captured && target != null)
            {
                authoredFont = target.font;
                authoredStyle = target.fontStyle;
                captured = true;
            }

            Loc.Changed += Refresh;
            Refresh();
        }

        private void OnDisable() => Loc.Changed -= Refresh;

        private void Refresh()
        {
            if (target == null) return;

            target.text = prefix + Loc.Get(key);

            Font korean = LocFont.Korean();
            // Only touched when there is something to swap to: if no Hangul-capable face could be
            // found, the English face stays and the text is boxes - which is ugly and diagnosable,
            // where a null font is an invisible label.
            if (Loc.Current == GameLanguage.Korean && korean != null) target.font = korean;
            else if (authoredFont != null) target.font = authoredFont;
            target.fontStyle = Loc.Current == GameLanguage.Korean ? FontStyle.Bold : authoredStyle;
        }
    }

    // WHERE A HANGUL-CAPABLE FACE COMES FROM, resolved once and shared.
    //
    // **D2CODING, AND IT IS A MONOSPACE ON PURPOSE.** The obvious pick for a Korean UI font is Noto
    // Sans KR or Pretendard, and both would be wrong here: this project's whole UI is set in JetBrains
    // Mono, and it does not merely LOOK monospaced - it is measured that way. `SceneBuilder` sizes
    // fixed-width labels by `0.6 x fontSize x length` (CLAUDE.md §3, a check that has caught two bugs)
    // and at least one wall display is laid out in counted cells. A proportional Korean face beside
    // that is both a different rhythm on screen and a different answer to how wide a string is.
    // D2Coding is a Hangul CODING font - fixed width, and built to sit next to Latin monospace.
    //
    // 4.2MB against JetBrains Mono's 115KB, which is the price of 11,172 precomposed syllables rather
    // than an alphabet. SIL OFL 1.1, NAVER Corporation - commercial bundling is explicitly allowed as
    // long as the licence travels with it, which is why `D2Coding-LICENSE.txt` sits beside the TTF
    // inside `Resources` and therefore ships inside the build. See `docs/asset-licences.md`.
    //
    // The OS fallback below is kept for the case where the asset is missing - a stripped build, or a
    // clone that has not pulled it. `Malgun Gothic` has been on every Windows since 7. It is a
    // proportional face, so it is a legibility net rather than the intended look, and it does not
    // exist on WebGL at all.
    public static class LocFont
    {
        private static Font korean;
        private static bool resolved;

        public static Font Korean()
        {
            if (resolved) return korean;
            resolved = true;

            korean = Resources.Load<Font>("Fonts/D2Coding");
            if (korean != null) return korean;

            Debug.LogWarning("[Loc] Resources/Fonts/D2Coding missing - falling back to an OS font. "
                           + "Korean will not be monospaced, and on WebGL it will not render at all.");
            // The size is only the face's default; a dynamic font renders at whatever `Text.fontSize`
            // asks for, so one instance serves every label on screen.
            korean = Font.CreateDynamicFontFromOSFont(
                new[] { "Malgun Gothic", "Noto Sans KR", "Apple SD Gothic Neo", "NanumGothic",
                        "Gulim", "Dotum", "Arial Unicode MS" }, 32);
            return korean;
        }
    }
}
