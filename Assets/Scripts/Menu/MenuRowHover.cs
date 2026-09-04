using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace IterationRoom
{
    // WHAT A MENU ROW DOES WHEN THE POINTER IS ON IT.
    //
    // The rows already had a hover: `Button`'s own `ColorBlock` fades a faint plate in behind the
    // one under the pointer. That is a rectangle appearing, and a rectangle is the one thing this
    // menu has otherwise managed to avoid - the title screen sets its whole hierarchy with size and
    // weight and draws no rules or boxes anywhere.
    //
    // So the mark moved onto the ROW. Asked for (2026-09-03) as *"the selected menu moves very
    // slightly to the right"*, and then given the rest of its language by a reference screenshot the
    // same afternoon: the words step, they darken, they gain weight, and a rule is drawn under them.
    //
    // **IT IS TEN PIXELS AND IT IS MEANT TO BE.** The step has to be small enough that the column
    // still reads as a column - a row that jumps a centimetre is a row that has left the list. What
    // actually carries the state is the INK and the WEIGHT; the movement is what makes the change
    // feel like a response to the pointer rather than a light coming on.
    //
    // **EVERYTHING IS ANIMATED, NOTHING IS SNAPPED.** A menu of five rows where the pointer crosses
    // three of them on the way down would otherwise flash three of them on and off; an eased fade
    // over `seconds` turns that into a soft trail, which is the same reason the HUD's own prompts
    // fade rather than blink. The FONT is the one thing that cannot be eased - see `Apply`.
    //
    // **AND IT RUNS ON UNSCALED TIME.** This is on the pause menu as well as the title screen, and
    // the pause menu is up precisely when `Time.timeScale` is zero. A hover that only moved while
    // the game was running would be dead in the one place a player is definitely using a mouse.
    public class MenuRowHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        // The words. Moved rather than re-anchored: its rect is stretched to the row, so writing
        // `anchoredPosition` slides both its edges together and the alignment inside it is unchanged.
        public RectTransform label;

        // The rule under the words. May be null - the row still steps - because this is decoration
        // and a missing piece must not take the movement with it.
        public Graphic underline;

        // **THE INK, AND THE WEIGHT.** The two things the reference this was rebuilt from carries the
        // state with (2026-09-03): everything unselected is a pale grey in a light face, and the one
        // under the pointer is near-black and bold. It is a stronger signal than any amount of
        // movement, and it is what lets the movement stay as small as it does.
        //
        // The font swap reflows the label by a few pixels, which is why the rule is sized off the
        // LIVE face rather than the resting one - it is only ever drawn while the live one is up.
        public Text ink;
        public Color restColor = new Color(0.11f, 0.11f, 0.13f, 0.45f);
        public Color liveColor = new Color(0.11f, 0.11f, 0.13f, 1f);
        public Font restFont;
        public Font liveFont;

        // How far the words step. See the note above: small on purpose.
        public float shift = 10f;

        // How long the whole gesture takes. Short enough to feel like a response to the pointer
        // rather than an animation being played at it.
        public float seconds = 0.12f;

        // The rule grows out from the edge the row is hung on rather than fading in place - a line
        // that appears at full width reads as a box edge, one that draws itself reads as an
        // underline.
        public RectTransform underlineRect;
        public float underlineWidth = 120f;

        // **A LINE THAT IS ONLY THERE ON HOVER.** Optional, like the underline, and for the same
        // reason: a row without one still does everything else. CONTINUE uses it for which cycle
        // would be resumed - a fact worth having and not worth spending a permanent line of the
        // title screen on, since it is only ever the answer to "what does this button do".
        public Graphic reveal;

        // Held well under full strength: it is a footnote to the row, not a second row. Kept as a
        // field rather than a constant so a future row can be louder without editing this.
        public float revealAlpha = 0.55f;

        private Vector2 restingLabel;
        private bool over;
        private float t;                 // 0 at rest, 1 fully hovered

        private void Awake()
        {
            if (label != null) restingLabel = label.anchoredPosition;
            // Applied at once so a row built hovered-looking in the scene starts correct. `Apply` is
            // the only thing that writes any of this, which is what stops a rest state existing in
            // two places.
            t = 0f;
            Apply();
        }

        // **AND RESET ON DISABLE, NOT JUST ON EXIT.** A pointer that is over a row when the menu
        // closes never gets its exit event - the object is switched off first - so the row would be
        // stepped, dark and underlined the next time the page opened, with the pointer elsewhere.
        private void OnDisable()
        {
            over = false;
            t = 0f;
            Apply();
        }

        public void OnPointerEnter(PointerEventData eventData) => over = true;
        public void OnPointerExit(PointerEventData eventData) => over = false;

        private void Update()
        {
            float target = over ? 1f : 0f;
            if (Mathf.Approximately(t, target)) return;

            float step = seconds > 0f ? Time.unscaledDeltaTime / seconds : 1f;
            t = Mathf.MoveTowards(t, target, step);
            Apply();
        }

        private void Apply()
        {
            // Smoothstepped rather than linear: the row should leave and arrive slowly and cross the
            // middle quickly, which is what makes ten pixels read as a movement at all.
            float k = Mathf.SmoothStep(0f, 1f, t);

            if (label != null)
                label.anchoredPosition = restingLabel + new Vector2(shift * k, 0f);

            if (ink != null)
            {
                ink.color = Color.Lerp(restColor, liveColor, k);
                // Swapped at the halfway point rather than lerped, because a font is not a value
                // that has a middle. Half way through a 0.12s move is early enough that the weight
                // has arrived by the time the row has stopped moving.
                Font want = k >= 0.5f ? liveFont : restFont;
                if (want != null && ink.font != want) ink.font = want;
            }

            if (underline != null)
            {
                Color c = underline.color;
                // Held under full strength: it is a rule under a word, not a second word.
                underline.color = new Color(c.r, c.g, c.b, k * 0.55f);
            }

            if (underlineRect != null)
                underlineRect.sizeDelta = new Vector2(underlineWidth * k, underlineRect.sizeDelta.y);

            // A second line that only exists while the pointer is here - CONTINUE's cycle number is
            // the one that wanted it. It rides the SAME `k` as everything else rather than having a
            // timing of its own, so the row arrives as one gesture; and it is faded rather than
            // switched off, because a line that pops in reads as a tooltip and this is meant to read
            // as part of the row.
            if (reveal != null)
            {
                Color c = reveal.color;
                reveal.color = new Color(c.r, c.g, c.b, k * revealAlpha);
            }
        }
    }
}
