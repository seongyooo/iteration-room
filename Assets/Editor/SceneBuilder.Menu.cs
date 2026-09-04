using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace IterationRoom.EditorTools
{
    // THE TITLE SCREEN AND EVERYTHING REACHED FROM IT: the menu scene, its rows and sliders, key
    // bindings, settings, and the attribution file read out of the models' own metadata - plus the
    // small pieces the game scene borrows (eyelids, countdown, gas, the end-cycle control).
    //
    // Split out of `SceneBuilder.cs` (2026-09-02), which had reached 25,000 lines. One class,
    // many files - see the note over the `partial` keyword there. Everything private in any part
    // is reachable from every other part, so nothing about the build changed when this moved.
    public static partial class SceneBuilder
    {

        // `ink` null keeps the pale red this was built with, which is right over the pause menu's
        // black scrim and wrong over the title screen's white wall - the same split MakeMenuButton
        // makes, for the same reason.
        // A LABEL THAT ALSO PICKS ITS FACE AND SIZE, for the page and section headings. The menu's
        // whole hierarchy is size and weight - see the title's note on why it is ExtraLight at 86 -
        // and `MakeRowLabel` hard-codes Regular at 18, which is one voice and not three.
        private static Text MakeRowLabelWeight(Transform parent, string name, string content,
                                               Vector2 anchoredPosition, Vector2 size,
                                               TextAnchor alignment, Color ink,
                                               string weight, int fontSize)
        {
            Text text = MakeRowLabelInk(parent, name, content, anchoredPosition, size, alignment, ink);
            if (text != null)
            {
                text.font = UIFont(weight);
                text.fontSize = fontSize;
            }
            return text;
        }

        // ONE SETTINGS ROW: label on the column edge, slider at the indent, number after it. Both
        // sliders on the settings page and both on the pause overlay are built through here, so the
        // four cannot drift apart - which is exactly what had happened.
        //
        // The INK is a parameter and not a constant, because the two pages that use it sit on
        // opposite grounds: the settings page is charcoal on a bright photograph, the pause overlay
        // is red on a near-black scrim. Everything else about the row is identical, which is the
        // point of having one.
        private static (Slider slider, Text value) MakeSettingsSliderRow(
            Transform column, string name, string label, string locKey, string initialValue,
            float y, Color ink)
        {
            Localize(MakeRowLabelInk(column, name + "Label", label,
                new Vector2(SettingsLabelWidth / 2f, y), new Vector2(SettingsLabelWidth, 30f),
                TextAnchor.MiddleLeft, ink), locKey);

            const float sliderWidth = 240f;
            Slider slider = MakeSlider(column, name + "Slider",
                new Vector2(SettingsControlX + sliderWidth / 2f, y), new Vector2(sliderWidth, 26f));

            // The readout starts a clear gap past the track's right end. Left-aligned in its rect, so
            // "100%" and "8%" both begin at the same x instead of the number sliding about as it
            // changes width.
            Text value = MakeRowLabelInk(column, name + "Value", initialValue,
                new Vector2(SettingsControlX + sliderWidth + 20f + 45f, y), new Vector2(90f, 30f),
                TextAnchor.MiddleLeft, ink);

            return (slider, value);
        }

        // **THE TITLE, REPEATED BEHIND ITSELF AND FADING - THE GAME'S OWN PREMISE AS A LOGOTYPE**
        // (2026-08-28, by request: the words were "심심하다", and thickness alone was not the answer).
        //
        // Every game decorates its title somehow; the question is whether the decoration is ABOUT
        // anything. This one is. The thing the player looks at for fifteen minutes is a past self
        // rendered faint and a step behind - `docs/ghosts.md`'s afterimage - and stacking the word on
        // its own trail is that image made out of type. It is not a style borrowed from a genre: no
        // other game would arrive at it, because no other game is about the thing it draws.
        //
        // **PLAIN `Text` COPIES, NOT A SHADER.** uGUI has `Shadow` and `Outline`, and both are one
        // offset apiece with no independent alpha ramp - three of them stacked is three copies of one
        // colour, which is a blur rather than a trail. Copies cost three extra draws on a static
        // screen and give every echo its own step and its own fade, which is the whole effect.
        //
        // Inserted BEFORE the title in sibling order, because that is uGUI's draw order: first child
        // is furthest back. The trail has to be behind the word or it is fog over it.
        //
        // The text is READ OFF the title rather than passed in, so the two cannot drift apart. That is
        // safe precisely because the title is facility-voice (CLAUDE.md §3) and carries no `Loc` key -
        // nothing translates it and nothing rewrites it at runtime.
        private static void MakeTitleAfterimage(Text title, int count, Vector2 step,
                                                float firstAlpha, float falloff)
        {
            if (title == null || count <= 0) return;

            RectTransform titleRect = title.GetComponent<RectTransform>();
            Transform parent = title.transform.parent;
            int frontIndex = title.transform.GetSiblingIndex();

            // The nearest echo's alpha is given rather than derived, because the FIRST step down from
            // solid is the one the eye judges the whole ramp by - derived from the falloff it was
            // either a double image or nothing at all. Scaled by the title's own alpha so the trail
            // fades with it if the screen ever dips.
            float alpha = title.color.a * firstAlpha / falloff;
            for (int i = 1; i <= count; i++)
            {
                alpha *= falloff;

                GameObject echoGO = new GameObject($"TitleEcho_{i}");
                echoGO.transform.SetParent(parent, false);

                Text echo = echoGO.AddComponent<Text>();
                echo.font = title.font;
                echo.fontSize = title.fontSize;
                echo.fontStyle = title.fontStyle;
                echo.alignment = title.alignment;
                echo.lineSpacing = title.lineSpacing;
                echo.horizontalOverflow = title.horizontalOverflow;
                echo.verticalOverflow = title.verticalOverflow;
                echo.text = title.text;
                echo.raycastTarget = false;
                echo.color = new Color(title.color.r, title.color.g, title.color.b, alpha);

                RectTransform echoRect = echo.GetComponent<RectTransform>();
                echoRect.anchorMin = titleRect.anchorMin;
                echoRect.anchorMax = titleRect.anchorMax;
                echoRect.pivot = titleRect.pivot;
                echoRect.sizeDelta = titleRect.sizeDelta;
                echoRect.anchoredPosition = titleRect.anchoredPosition + step * i;

                // Each one goes immediately in front of where the title currently is, so they end up
                // ordered furthest-first with the solid word last. Re-read every pass: inserting
                // shifts the title along by one.
                echoGO.transform.SetSiblingIndex(frontIndex);
                frontIndex = title.transform.GetSiblingIndex();
            }
        }

        private static Text MakeRowLabelInk(Transform parent, string name, string content,
                                            Vector2 anchoredPosition, Vector2 size,
                                            TextAnchor alignment, Color ink)
        {
            Text text = MakeRowLabel(parent, name, content, anchoredPosition, size, alignment);
            if (text != null) { text.color = ink; text.font = UIFont("Medium"); }
            return text;
        }

        // Same shape, one line: a menu button that has already been told what surface it is on.
        // Re-hangs a menu button on the BOTTOM-RIGHT corner. `MakeMenuButton` anchors everything to
        // the left edge at mid-height, which is right for a column and wrong for the one entry that is
        // not in it - and anchoring to the corner rather than offsetting from the centre is what keeps
        // it in the corner when the window is resized.
        // `rise` stacks a second entry above the first. There are two things in this corner now -
        // RECORD and CREDITS - and 78 is the button's own 66 plus a gap, which is deliberately TIGHTER
        // than the column's 88: these are a pair off to one side, not a continuation of the menu.
        // **~~BOTTOM RIGHT~~ BOTTOM LEFT, since the column moved** (2026-09-03). RECORD and CREDITS
        // were cornered on the right because that was the empty half of the screen; the five main
        // rows now hang there and QUIT's bottom edge is 158px off the floor, so a pair of rows in the
        // right-hand corner is a pair of rows crowding the column they were put outside to escape.
        //
        // The name is kept, because what it means has not changed: *the corner away from the menu*.
        // **AND IT NOW GOES WHERE ITS NAME SAYS** (2026-09-04). It anchored BOTTOM-LEFT, which was
        // right while the column lived on the right margin and is a collision now that the column is
        // back on the left - the first dark title screen came out with RECORD, QUIT and CREDITS
        // stacked on top of each other in the corner. The label is right-aligned with it, or a
        // left-aligned line inside a 340-wide plate hangs 340px short of the edge it is cornered to.
        private static void CornerBottomRight(RectTransform rect, float rise = 0f)
        {
            if (rect == null) return;
            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(1f, 0f);
            // The same margin off both edges the column has off its own.
            rect.anchoredPosition = new Vector2(-MenuLeftMargin, MenuLeftMargin + rise);

            // **THE WORDS AND THE RULE BOTH HAVE TO TURN ROUND, AND FORGETTING THE RULE IS WHAT PLAY
            // SAW** (2026-09-04: "the underline under RECORD does not line up"). `MakeMenuButton`
            // built this row as a LEFT-aligned one - it has no idea it is about to be cornered - so
            // its rule is anchored to the plate's left edge and grows rightward from there, which is
            // 340px away from where the words end up. Re-pointing the label without the rule leaves
            // a line under empty space.
            Text label = rect.GetComponentInChildren<Text>(true);
            if (label != null) label.alignment = TextAnchor.MiddleRight;

            RectTransform rule = null;
            foreach (RectTransform child in rect)
                if (child.name == "HoverRule") rule = child;
            if (rule != null)
            {
                rule.anchorMin = new Vector2(1f, 0.5f);
                rule.anchorMax = new Vector2(1f, 0.5f);
                rule.pivot = new Vector2(1f, 0.5f);
                // The same hover step the row moves by, on the same side it moves toward - see the
                // note where this is set for a right-aligned row in `MakeMenuButton`.
                rule.anchoredPosition = new Vector2(10f, -26f);
            }
        }

        private static Button MakeMenuButtonInk(Transform parent, string name, string label,
                                                Vector2 anchoredPosition) =>
            MakeMenuButton(parent, name, label, anchoredPosition, MenuInk, "Bold");

        private static Text MakeRowLabel(Transform parent, string name, string content,
                                         Vector2 anchoredPosition, Vector2 size, TextAnchor alignment)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);

            Text text = go.AddComponent<Text>();
            text.font = UIFont();
            text.fontSize = 18;
            text.alignment = alignment;
            text.color = new Color(1f, 0.35f, 0.35f, 0.85f);
            text.text = content;
            // Overflow, so a rect a shade too narrow cannot break the label across two lines - the
            // same reason IterationLabel sets it.
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;

            RectTransform rect = text.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = anchoredPosition;

            return text;
        }

        // A uGUI Slider assembled by hand. Three things about it are not optional, because Slider
        // drives them itself every frame and gets them from the hierarchy rather than from fields:
        // the fill must be the child of a container rect (Slider rewrites the fill's anchors within
        // its parent), the handle likewise, and both must leave their offsets at zero or the value
        // it computes lands somewhere other than where it draws.
        private static Slider MakeSlider(Transform parent, string name, Vector2 anchoredPosition, Vector2 size)
        {
            const float handleWidth = 22f;
            Sprite uiSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");

            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            RectTransform rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = anchoredPosition;

            GameObject bgGO = new GameObject("Background");
            bgGO.transform.SetParent(go.transform, false);
            Image bg = bgGO.AddComponent<Image>();
            bg.sprite = uiSprite;
            bg.type = Image.Type.Sliced;
            bg.color = new Color(0.16f, 0.04f, 0.04f, 0.9f);
            RectTransform bgRect = bg.GetComponent<RectTransform>();
            bgRect.anchorMin = new Vector2(0f, 0.3f);
            bgRect.anchorMax = new Vector2(1f, 0.7f);
            bgRect.offsetMin = Vector2.zero;
            bgRect.offsetMax = Vector2.zero;

            // Inset by half the handle at each end, so the handle's centre reaches the track's ends
            // at 0 and 1 rather than hanging off them.
            GameObject fillAreaGO = new GameObject("Fill Area");
            fillAreaGO.transform.SetParent(go.transform, false);
            RectTransform fillArea = fillAreaGO.AddComponent<RectTransform>();
            fillArea.anchorMin = new Vector2(0f, 0.3f);
            fillArea.anchorMax = new Vector2(1f, 0.7f);
            fillArea.offsetMin = new Vector2(handleWidth / 2f, 0f);
            fillArea.offsetMax = new Vector2(-handleWidth / 2f, 0f);

            GameObject fillGO = new GameObject("Fill");
            fillGO.transform.SetParent(fillAreaGO.transform, false);
            Image fill = fillGO.AddComponent<Image>();
            fill.sprite = uiSprite;
            fill.type = Image.Type.Sliced;
            fill.color = new Color(0.7f, 0.11f, 0.11f, 0.95f);
            RectTransform fillRect = fill.GetComponent<RectTransform>();
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;

            GameObject handleAreaGO = new GameObject("Handle Slide Area");
            handleAreaGO.transform.SetParent(go.transform, false);
            RectTransform handleArea = handleAreaGO.AddComponent<RectTransform>();
            handleArea.anchorMin = new Vector2(0f, 0f);
            handleArea.anchorMax = new Vector2(1f, 1f);
            handleArea.offsetMin = new Vector2(handleWidth / 2f, 0f);
            handleArea.offsetMax = new Vector2(-handleWidth / 2f, 0f);

            GameObject handleGO = new GameObject("Handle");
            handleGO.transform.SetParent(handleAreaGO.transform, false);
            Image handle = handleGO.AddComponent<Image>();
            handle.sprite = uiSprite;
            handle.type = Image.Type.Sliced;
            handle.color = Color.white;
            RectTransform handleRect = handle.GetComponent<RectTransform>();
            handleRect.anchorMin = new Vector2(0f, 0f);
            handleRect.anchorMax = new Vector2(0f, 1f);
            handleRect.pivot = new Vector2(0.5f, 0.5f);
            // Only the width is fixed; a zero height against top-and-bottom anchors is full height.
            handleRect.sizeDelta = new Vector2(handleWidth, 0f);
            handleRect.anchoredPosition = Vector2.zero;

            Slider slider = go.AddComponent<Slider>();
            slider.direction = Slider.Direction.LeftToRight;
            slider.fillRect = fillRect;
            slider.handleRect = handleRect;
            slider.targetGraphic = handle;
            slider.wholeNumbers = false;
            // The range and the starting value are PauseMenu's to set - they belong to GameSettings,
            // and seeding them here would put the same number in two places.

            // Same treatment as the buttons: a white graphic tinted by the ColorBlock, since a
            // multiply against an already-dark handle has nothing left to brighten with.
            ColorBlock colors = slider.colors;
            colors.normalColor = new Color(0.62f, 0.1f, 0.1f, 1f);
            colors.highlightedColor = new Color(0.85f, 0.2f, 0.2f, 1f);
            colors.pressedColor = new Color(1f, 0.35f, 0.35f, 1f);
            colors.selectedColor = colors.highlightedColor;
            colors.fadeDuration = 0.12f;
            slider.colors = colors;

            return slider;
        }

        // The title screen: a camera, a canvas and a still of the room. Deliberately almost
        // nothing, because the whole point of it being a separate scene is that it opens instantly
        // while the room behind it takes a real moment to come in.
        private static void BuildMainMenuScene()
        {
            Scene menu = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            GameObject camGO = new GameObject("MenuCamera");
            camGO.tag = "MainCamera";
            Camera cam = camGO.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            // Black behind everything: the background image is stretched to the screen, but a
            // window shaped nothing like 16:9 should fall away to black rather than to the URP
            // default blue.
            cam.backgroundColor = Color.black;
            camGO.AddComponent<AudioListener>();

            GameObject canvasGO = new GameObject("Canvas");
            Canvas canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            canvasGO.AddComponent<GraphicRaycaster>();

            // The captured room, and a scrim over it. The room is near-white and the HUD's red
            // would fight it head-on; the scrim also puts the picture behind the title rather than
            // beside it, which is what a background is for.
            GameObject backgroundGO = new GameObject("Background");
            backgroundGO.transform.SetParent(canvasGO.transform, false);
            Image background = backgroundGO.AddComponent<Image>();
            Sprite shot = AssetDatabase.LoadAssetAtPath<Sprite>(MenuBackgroundPath);
            background.sprite = shot;
            // A missing capture leaves a deliberate dark panel rather than uGUI's default white
            // box, so a build without a graphics device still produces a menu that looks intended.
            background.color = shot != null ? Color.white : new Color(0.06f, 0.06f, 0.07f, 1f);
            background.raycastTarget = false;
            Stretch(background.GetComponent<RectTransform>());
            // AND IT MOVES. See MenuBackdrop: a still photograph behind a menu reads as a loading
            // screen, and the same picture drifting reads as a place the game is already running in.
            // It overscans the canvas, so the `Stretch` above sets the size it slides WITHIN.
            backgroundGO.AddComponent<MenuBackdrop>();

            // **AND ONE SIDE OF IT GOES OUT.** The same shot with the left-hand fixtures doused, laid
            // over the lit one at an alpha `MenuFlicker` cuts between - see that script for why a
            // photograph cannot flicker any other way, and why the pattern is hard cuts rather than a
            // fade.
            //
            // **A CHILD OF THE BACKGROUND, WHICH IS WHAT KEEPS THE TWO REGISTERED.** `MenuBackdrop`
            // slides the picture around to stop it reading as a loading screen; a sibling would sit
            // still while the lit frame drifted under it, and the flicker would arrive as a picture
            // jumping half a metre sideways. Stretched to the parent, it inherits every pixel of that
            // motion for free.
            Sprite darkShot = AssetDatabase.LoadAssetAtPath<Sprite>(MenuBackgroundDarkPath);
            if (darkShot != null)
            {
                GameObject darkGO = new GameObject("BackgroundDark");
                darkGO.transform.SetParent(backgroundGO.transform, false);
                Image darkImage = darkGO.AddComponent<Image>();
                darkImage.sprite = darkShot;
                // Transparent at rest - the room is whole until the fault takes it.
                darkImage.color = new Color(1f, 1f, 1f, 0f);
                darkImage.raycastTarget = false;
                Stretch(darkImage.GetComponent<RectTransform>());
                darkGO.AddComponent<MenuFlicker>();
            }
            else
            {
                // Said rather than skipped silently: a menu with no flicker looks exactly like a menu
                // whose second capture failed, and the two want telling apart.
                Debug.LogWarning($"[SceneBuilder] {MenuBackgroundDarkPath} is missing, so the title "
                               + "screen will not flicker. Rebuild from the Editor with a graphics "
                               + "device.");
            }

            // THE SCRIM IS A THIRD OF WHAT IT WAS, and it is a gradient rather than a flat wash.
            //
            // At 0.5 flat it was doing two jobs badly: darkening the whole picture to make red text
            // legible anywhere on it, which threw away the one thing the shot has going for it - the
            // room is BRIGHT, and a bright room is what this game looks like. The menu is down the
            // left edge now, so only the left edge needs protecting. A horizontal ramp does that and
            // leaves the room at nearly its captured brightness on the right, where the eye goes.
            GameObject scrimGO = new GameObject("Scrim");
            scrimGO.transform.SetParent(canvasGO.transform, false);
            Image scrim = scrimGO.AddComponent<Image>();
            Sprite scrimSprite = MakeMenuScrimSprite();
            scrim.sprite = scrimSprite;
            scrim.type = Image.Type.Simple;
            // WHITE ONLY IF THE RAMP LOADED. A uGUI `Image` with a null sprite draws a solid rect at
            // its colour, so white-plus-no-sprite is a white sheet over the whole title screen -
            // which is what a failed import produced. The fallback is the flat wash this replaced,
            // lightened, so a menu that loses its gradient is still a menu.
            // The sprite already carries the colour AND the alpha ramp, so the tint stays white -
            // white multiplies to nothing. The FALLBACK is the one that has to know the ramp is dark
            // now: a flat black wash, so a menu that loses its gradient is still a legible menu
            // rather than a white sheet over a black room.
            scrim.color = scrimSprite != null ? Color.white : new Color(0f, 0f, 0f, 0.45f);
            if (scrimSprite == null)
                Debug.LogWarning("[SceneBuilder] menu scrim gradient failed to import; using a flat wash.");
            scrim.raycastTarget = false;
            Stretch(scrim.GetComponent<RectTransform>());

            GameObject menuGO = new GameObject("Menu");
            menuGO.transform.SetParent(canvasGO.transform, false);
            CanvasGroup menuGroup = menuGO.AddComponent<CanvasGroup>();
            Stretch(menuGO.AddComponent<RectTransform>());

            GameObject titleGO = new GameObject("Title");
            titleGO.transform.SetParent(menuGO.transform, false);
            Text title = titleGO.AddComponent<Text>();
            title.font = UIFont();
            // **AND BACK TO LIGHT AT 86** (2026-08-28, by request: thin suits the game).
            //
            // It went ExtraLight -> ExtraBold earlier the same day on the reasoning below, which was
            // about the game's SUBJECT - a facility, whose lettering would be painted and heavy. That
            // reasoning was sound and lost to something better: the game's LOOK is a white room, thin
            // black grooves and a lot of empty space, and a title has to belong to what is on screen
            // rather than to what the fiction is about. Light is what the room is made of.
            //
            // Kept because it is the argument that will be made again -
            // **~~EXTRABOLD AT 86, UP FROM EXTRALIGHT~~** (was: "글자가 얇아").
            //
            // The thin setting was a real argument and it was the wrong one for this game. Thin at a
            // large size is the elegant-product voice - it reads as a design tool or a streaming
            // service, and it is what every default title screen reaches for. **This building is a
            // facility, and the title is on the FACILITY'S side of the split** (CLAUDE.md §3: `ROOM 2`,
            // `ERROR`, `FIRE AXE`, and the title stay English in every language because they are the
            // place talking to itself, not the game talking to the player). Painted-on plant lettering
            // is heavy, and heavy is what makes 86 read as stencilled on a wall rather than set in a
            // deck.
            //
            // The hierarchy the old note wanted is kept and inverted: the title is now the heaviest
            // thing on the screen and the five labels stay Medium at 26, so the ratio is still weight
            // and not four sizes with nothing between them.
            title.font = UIFont("Light");
            title.fontSize = 86;
            // CENTRED, while the buttons stay down the left edge. The two were moved together and
            // that was one step too far: a left-hung title over a left-hung column leaves the whole
            // right half empty and the words stop being a title at all. Centred over an off-centre
            // menu is the arrangement that reads - the name of the game belongs to the picture, the
            // buttons belong to the edge.
            // **RIGHT, AND AT THE TOP** (2026-09-03). It was centred over a left-hung column, which
            // was the arrangement that read while the menu was on the left. With the menu on the
            // right the two belong to the same edge: the name at the top of that edge and the
            // choices below it make one narrow strip of type, which is what the reference does and
            // why its middle can be empty without looking unfinished.
            // **CENTRED AT THE TOP, WITH THE COLUMN STILL ON THE LEFT** (2026-09-04, by request,
            // measured off `docs/mainmenu_dark.png`: the title spans x 95..690 of 782, whose centre
            // is the frame's own). It has been left, centred, right, left and now centred again -
            // each move followed the picture underneath it, and this one follows the reference
            // exactly. The name belongs to the whole frame; only the choices belong to an edge.
            title.alignment = TextAnchor.MiddleCenter;
            // CHARCOAL, not red. See MenuInk: red is the alarm this game rings, and a title screen
            // that rings it before anything has happened has nothing left to ring it with.
            title.color = MenuInk;
            // Spaced out in the string, exactly as IterationLabel does it and for the same reason:
            // uGUI's Text has no tracking control at all, and in a monospace face a space is one
            // cell. The in-game label and the title then read as the same typeface doing the same
            // thing, which is the point - both are the facility talking.
            // **TWO SPACES BETWEEN LETTERS, UP FROM ONE** (2026-09-04, by request: wider, like the
            // reference). uGUI's `Text` has no tracking control at all, so spacing IS the string -
            // and in a monospace face each space is exactly one cell, which makes this arithmetic
            // rather than taste. At 86pt a cell is about 0.6 x 86 = 52px: nine letters and eight
            // single gaps is 17 cells and spanned 46% of a 1920 frame, where the reference's title
            // spans 76%. Doubling the gaps makes it 25 cells and 67% - the nearest this can get,
            // since three spaces would be 33 cells and 89%, wider than the reference and nearly
            // touching both edges.
            title.text = "I  T  E  R  A  T  I  O  N";
            // "ROOM" is a SECOND Text below it rather than a line break, because one `Text` has one
            // font size and the two words do not want the same one - `ROOM` at 86 reads as a heading
            // of equal weight, which makes the pair a list instead of a title.
            title.lineSpacing = 1f;
            title.horizontalOverflow = HorizontalWrapMode.Overflow;
            title.verticalOverflow = VerticalWrapMode.Overflow;
            title.raycastTarget = false;
            // LEFT-HUNG, not centred. A centred title over a centred column is a poster: the screen
            // reads as a picture of a menu. Pushing both to the left edge puts the ROOM in the middle
            // of the frame with the words beside it, which is the arrangement horror titles on Steam
            // use and the reason they use it - the place is the subject, not the interface.
            //
            // Anchored to the LEFT EDGE rather than to the centre with a negative offset, so a wider
            // window widens the picture instead of dragging the words toward the middle.
            RectTransform titleRect = title.GetComponent<RectTransform>();
            // Anchored to the TOP RIGHT rather than to the centre with an offset, so a wider or
            // taller window moves the picture and leaves the words where they are against their own
            // corner - the same argument the old note made for the left edge.
            titleRect.anchorMin = new Vector2(0.5f, 1f);
            titleRect.anchorMax = new Vector2(0.5f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.sizeDelta = new Vector2(1400f, 140f);
            // **DOWN FROM -70, 2026-09-04, by request.** It also lands where the reference has it:
            // the mock's title sits at 13-21% of the frame's height and -70 put ours at 9-17%, high
            // enough that it read as pinned to the top edge rather than hung in the room. -120 puts
            // its centre at about 16%, and there is nothing to collide with on the way down - the
            // column's top row is at 42%.
            titleRect.anchoredPosition = new Vector2(0f, -120f);

            // **THE ECHOES ARE BACK, AND THE THING THAT KILLED THEM IS AVOIDED RATHER THAN
            // OVERRULED** (2026-09-04, to `docs/mainmenu_dark.png`).
            //
            // They were built and cut on 2026-08-28: "at 86pt with the letters a cell apart there is
            // no offset that clears its own NEIGHBOUR without also being far enough to read as a
            // separate word". That is true, and it is a statement about a HORIZONTAL step in a
            // monospace face - a sideways offset lands the E of one copy on the R of the next, so
            // the trail reads as the word printed several times, badly registered.
            //
            // A DOWNWARD step has no such neighbour to collide with: every letter falls into empty
            // leading, and each copy stays legible as the same word receding. That is what the
            // reference does, and it is the right shape for this game anyway - the trail is a past
            // self, and the past selves in this building are behind you, not beside you.
            //
            // The step is a little under half the cap height so the copies overlap rather than
            // stack; alpha falls fast, because the third one only has to be a suggestion.
            MakeTitleAfterimage(title, 2, new Vector2(0f, -34f), 0.34f, 0.5f);

            GameObject subtitleGO = new GameObject("TitleRoom");
            subtitleGO.transform.SetParent(menuGO.transform, false);
            Text subtitle = subtitleGO.AddComponent<Text>();
            // MEDIUM against the title's Light, which is the pairing this started with and the one
            // that works: the smaller word carries the weight, and that is what lets it hold its own
            // beneath the larger one without being set any larger. The buttons stay Bold - they are
            // the thing you click, and they are not the title.
            subtitle.font = UIFont("Medium");
            // Smaller, and spaced WIDER, so the shorter word spans a similar width to the one above
            // it. Letter-spaced in the string for the reason IterationLabel is: uGUI's Text has no
            // tracking control, and in a monospace face a space is exactly one cell.
            subtitle.fontSize = 52;
            subtitle.alignment = TextAnchor.MiddleCenter;
            // **THE ONE RED THING ON THE SCREEN AT REST**, and it is one word of the game's own name.
            // Everything else - title, five labels - is charcoal, so this is the only place the eye is
            // sent, and the colour still means what it means everywhere else in the building.
            //
            // At 52 it is large enough that red on white does not vibrate the way a 26pt label would,
            // which is the other half of why the accent is HERE and not on the buttons.
            subtitle.color = MenuAccent;
            // Widened with the title above it, and by the same sum: at 52pt a cell is ~31px, so
            // three spaces is 13 cells and 21% of the frame against the reference's 22%. The two
            // words keep their relative widths, which is what makes them read as one block.
            subtitle.text = "R   O   O   M";
            subtitle.horizontalOverflow = HorizontalWrapMode.Overflow;
            subtitle.verticalOverflow = VerticalWrapMode.Overflow;
            subtitle.raycastTarget = false;
            RectTransform subtitleRect = subtitle.GetComponent<RectTransform>();
            subtitleRect.anchorMin = new Vector2(0.5f, 0.5f);
            subtitleRect.anchorMax = new Vector2(0.5f, 0.5f);
            subtitleRect.pivot = new Vector2(0.5f, 0.5f);
            subtitleRect.sizeDelta = new Vector2(1400f, 90f);
            subtitleRect.anchorMin = new Vector2(0.5f, 1f);
            subtitleRect.anchorMax = new Vector2(0.5f, 1f);
            subtitleRect.pivot = new Vector2(0.5f, 1f);
            // Directly under ITERATION and on the same centre line, so the two read as one block -
            // which is what the reference does. Far enough down to clear the title's two echoes
            // rather than landing in the middle of one.
            // Moved by exactly what the title moved by, so the gap between the two words - which is
            // what makes them one block rather than two headings - is unchanged.
            subtitleRect.anchoredPosition = new Vector2(0f, -246f);

            // CONTINUE FIRST, since 2026-08-20. It is the entry a returning player wants and the one
            // that needs no decision; PLAY under it opens the cycle picker, which is a decision. The
            // two swapped meanings on the same day - see MainMenu.Play and MainMenu.Continue.
            //
            // CONTINUE is HIDDEN by `MainMenu.Start` when nothing has been played, so a first-time
            // title screen is PLAY / SETTINGS / QUIT with a gap where this is. The gap is deliberate:
            // shuffling the column up would move PLAY under the player's cursor between sessions.
            // THREE WAYS IN, AND EACH MEANS ONE THING: resume, start from the beginning, jump to a
            // cycle. The two a player uses have no page in between; only the rare one does.
            // **CHARCOAL, AND BOLD** (2026-08-28, by request: the words were too thin). The plate
            // under each of these is transparent until the pointer is on it (see `MakeMenuButton`),
            // so what the player sees at rest is five words printed on a white wall - which is what
            // the room is. Words printed on a wall with nothing around them have only their own
            // weight to hold the screen with, which is exactly why Medium was not enough here and
            // would have been fine inside a box.
            // **AND BACK TO THE LEFT** (2026-09-04, by request, from `docs/mainmenu_dark.png`).
            // Same five rows, same order, same pitch; it has been on both margins now and each move
            // followed the picture behind it. The argument for the right was that the interface
            // should be a strip of type at the edge rather than a block standing on the room. That
            // still holds - the strip has simply changed edges, because the reference puts the name
            // and the choices together on the left and leaves the lit doorway alone in the middle,
            // which is the one thing in the frame that should not have type over it.
            Button continueButton = MakeMenuButton(menuGO.transform, "ContinueButton",
                                                   "CONTINUE", new Vector2(0f, 86f), MenuInk, "Bold");
            Localize(continueButton, "menu.continue", "  ");

            // **WHICH CYCLE CONTINUE WOULD RESUME, UNDER IT, ON HOVER** (2026-09-04, by request,
            // and in `docs/mainmenu_dark.png`). The number is the one thing this row cannot say by
            // its own name, and it is the difference between "carry on" and "carry on from where".
            //
            // On hover rather than always, so the column stays five plain words at rest - a
            // permanent second line under one row makes that row look like a different kind of
            // thing from the others. `MenuRowHover.reveal` fades it on the row's own timing.
            //
            // Authored EMPTY: the cycle comes from the save, and `MainMenu` writes it in the same
            // breath as deciding whether CONTINUE exists at all. An authored "CYCLE 1" would be a
            // number baked into the scene that is wrong for everyone who has played past it.
            // **LINED UP UNDER THE WORD, NOT UNDER THE PLATE** (2026-09-04, by request). The first
            // attempt indented it with two spaces of its OWN font, which is 18pt where the label is
            // 28 - so the two indents were different widths and the small line sat left of the word
            // it belongs to. It is placed by the same sum the hover rule uses instead: the label's
            // glyph indent (its two spaces at ITS size) plus the hover step, since this line only
            // ever appears while the row is stepped.
            //
            // `MakeMenuLine` measures from the parent's CENTRE and the plate is 340 wide, so a rect
            // of the same width offset by that sum starts exactly where the hovered glyphs do.
            // **AND BELOW THE RULE, NOT THROUGH IT** (2026-09-04, by request: they overlapped). The
            // hover rule sits at -20 and this line is 24 tall, so centring it at -26 ran the rule
            // straight through the words. -44 clears it with a gap: the rule's own 2px, then air,
            // then the line's top edge. It stays clear of the row BELOW as well - the column's pitch
            // is 97 and the next plate's top edge is at -67, where this reaches -56.
            const float continueIndent = 2f * 0.6f * 28f + 10f;
            Text continueCycle = MakeMenuLine(continueButton.transform, "CycleLine", string.Empty, 18,
                                              MenuInk, new Vector2(continueIndent, -44f),
                                              new Vector2(340f, 24f), TextAnchor.MiddleLeft);
            // Starts invisible, and `MenuRowHover` owns the alpha from here on.
            continueCycle.color = new Color(MenuInk.r, MenuInk.g, MenuInk.b, 0f);
            MenuRowHover continueHover = continueButton.GetComponent<MenuRowHover>();
            if (continueHover != null) continueHover.reveal = continueCycle;

            Button playButton = MakeMenuButton(menuGO.transform, "PlayButton", "PLAY",
                                               new Vector2(0f, -11f), MenuInk, "Bold");
            Localize(playButton, "menu.play", "  ");
            Button cycleSelectButton = MakeMenuButton(menuGO.transform, "CycleSelectButton",
                                                      "CYCLE SELECT", new Vector2(0f, -108f), MenuInk, "Bold");
            Localize(cycleSelectButton, "menu.cycleSelect", "  ");
            // THE LAST RUN'S BILL, IN THE BOTTOM-RIGHT CORNER rather than in the column.
            //
            // It is not a way into the game and it is not a way out of it, which is what every entry
            // in that column is - and a sixth row pushed QUIT down to within seventy pixels of the
            // screen edge. Cornered, it reads as what it is: a thing the facility keeps, off to one
            // side of the choices.
            Button recordButton = MakeMenuButton(menuGO.transform, "RecordButton",
                                                 "RECORD", Vector2.zero, MenuInk, "Bold");
            Localize(recordButton, "menu.record", "  ");
            // ONE ROW UP: CREDITS is the bottom of this pair (2026-08-24, by request).
            CornerBottomRight(recordButton.GetComponent<RectTransform>(), 78f);
            // ~~TEST: CYCLE BOUNDARY~~ REMOVED 2026-08-15, by request. It was a development shortcut
            // into the cycle boundary with cycle 1 already finished, sitting on the title screen
            // between CONTINUE and QUIT and labelled loudly so it could not be mistaken for content.
            //
            // What it did is not lost: CONTINUE's cycle picker reaches any cycle from its own bed,
            // which covers most of what the shortcut was for, and `DebugStart.AtCycleBoundary` and
            // `LoopManager.JumpToBoundary` are both still there for a developer who sets the flag by
            // hand. What is gone is the entry on the screen a player sees.
            //
            // QUIT moves up into the gap rather than leaving a hole in the column.
            Button settingsButton = MakeMenuButton(menuGO.transform, "SettingsButton",
                                                   "SETTINGS", new Vector2(0f, -205f), MenuInk, "Bold");
            Localize(settingsButton, "menu.settings", "  ");
            // -382 AGAIN, now that CREDITS has left the column for the bottom-right corner. It was
            // slid to -470 to make room for it; the column's pitch is 88 throughout and this closes
            // the gap rather than leaving a hole where a button used to be.
            Button quitButton = MakeMenuButton(menuGO.transform, "QuitButton", "QUIT",
                                               new Vector2(0f, -399f), MenuInk, "Bold");

            // THE CYCLE PICKER, on a page of its own over the same background. A title screen that
            // grows a row every time the game grows a cycle stops being a title screen.
            GameObject cycleGO = new GameObject("CyclePicker");
            cycleGO.transform.SetParent(canvasGO.transform, false);
            CanvasGroup cycleGroup = cycleGO.AddComponent<CanvasGroup>();
            cycleGroup.alpha = 0f;
            cycleGroup.blocksRaycasts = false;
            Stretch(cycleGO.AddComponent<RectTransform>());

            // ONE PER CYCLE AND NOTHING ELSE. The calibration room had an entry here for one build
            // and it was a mistake worth recording: it and CYCLE 1 both start cycle 1 and differ only
            // in whether the sensitivity step runs, which no label can carry. PLAY is that path now,
            // and the sensitivity is adjustable on SETTINGS either way.
            //
            // The count is read from the cycle list rather than written here, so adding a cycle adds
            // its button - the same rule LoopManager follows for deciding which cycle is the last.
            var cycleButtons = new Button[CycleCount];
            for (int i = 0; i < CycleCount; i++)
                cycleButtons[i] = MakeMenuButton(cycleGO.transform, $"CycleButton_{i + 1}",
                                                 $"CYCLE {i + 1}", new Vector2(0f, -30f - i * 88f),
                                                 MenuInk, "Bold");

            // **~~ONE "END" ENTRY PER CYCLE~~ REMOVED 2026-08-31, by request.** They started a cycle
            // with its last room already finished, so the hatch into the next one opened within
            // seconds - a development shortcut on a page that is already one.
            //
            // The array stays and stays EMPTY rather than the field being deleted: `MainMenu` wires
            // its own listeners off it, `PlayFromCycleEnd` is still the honest way to reach a cycle
            // boundary, and `DebugStart.AtCycleBoundary` is read in three places that have nothing to
            // do with this page. What is gone is the row on the title screen, which is all that was
            // asked for - and putting it back is one loop.
            var cycleEndButtons = new Button[0];

            Button cycleBack = MakeMenuButton(cycleGO.transform, "CycleBackButton",
                                              "BACK",
                                              new Vector2(0f, -30f - CycleCount * 88f),
                                              MenuInk, "Bold");
            Localize(cycleBack, "menu.back", "  ");

            // THE RECORD PAGE, over the same background as the cycle picker and laid out the same:
            // one block of monospaced text and a way back. The table itself comes from `RunReport`,
            // which is also what the ending card prints - one formatter, two screens.
            GameObject recordGO = new GameObject("Record");
            recordGO.transform.SetParent(canvasGO.transform, false);
            CanvasGroup recordGroup = recordGO.AddComponent<CanvasGroup>();
            recordGroup.alpha = 0f;
            recordGroup.blocksRaycasts = false;
            Stretch(recordGO.AddComponent<RectTransform>());

            // **THREE ROWS, EACH A PICTURE OF THE CYCLE AND WHAT IT COST** (2026-08-31, by request).
            // This was a single padded monospace table, which is still what the ENDING card uses: that
            // one reports a run and the run is the subject, so a table is right. This page reports
            // what the player has ever done, per cycle, and the thing that makes a cycle recognisable
            // is the room - not its number.
            //
            // Laid out in a column rather than a grid: three entries is a list, and a list can grow
            // to four when cycle 4 has a room worth photographing without anything being re-designed.
            const float rowH = 124f, rowGap = 8f, shotW = 214f, shotH = 120f, rowW = 620f;
            var recordRows = new GameObject[3];
            var recordRowHead = new Text[3];
            var recordRowText = new Text[3];

            for (int i = 0; i < 3; i++)
            {
                GameObject row = new GameObject($"RecordRow{i + 1}");
                row.transform.SetParent(recordGO.transform, false);
                RectTransform rowRect = row.AddComponent<RectTransform>();
                rowRect.anchorMin = rowRect.anchorMax = rowRect.pivot = new Vector2(0.5f, 0.5f);
                rowRect.sizeDelta = new Vector2(rowW, rowH);
                rowRect.anchoredPosition = new Vector2(0f, 52f - i * (rowH + rowGap));

                // **A RULE BETWEEN CYCLES** (2026-08-31, by request). Under every row but the last,
                // so it separates rather than underlines - a line under the bottom entry would read
                // as a total that is not there. Faint: it is a gap made visible, not a border.
                if (i < 2)
                {
                    GameObject sepGO = new GameObject("Separator");
                    sepGO.transform.SetParent(row.transform, false);
                    Image sep = sepGO.AddComponent<Image>();
                    sep.color = new Color(0.11f, 0.11f, 0.13f, 0.22f);
                    sep.raycastTarget = false;
                    RectTransform sepRect = sep.GetComponent<RectTransform>();
                    sepRect.anchorMin = sepRect.anchorMax = sepRect.pivot = new Vector2(0.5f, 0.5f);
                    sepRect.sizeDelta = new Vector2(rowW, 1f);
                    sepRect.anchoredPosition = new Vector2(0f, -(rowH + rowGap) / 2f);
                }

                // THE SHOT. `preserveAspect` off and the rect at the capture's own 16:9, so the room
                // is never squeezed - the frames are rendered at 640x360 by `CaptureCyclePreview`.
                GameObject shotGO = new GameObject("Preview");
                shotGO.transform.SetParent(row.transform, false);
                Image frame = shotGO.AddComponent<Image>();
                frame.sprite = CyclePreviewSprite(i + 1);
                frame.raycastTarget = false;
                // A cycle with no rendered frame keeps the row and loses the picture, rather than
                // drawing a white box where a room should be.
                frame.color = frame.sprite != null ? Color.white : new Color(1f, 1f, 1f, 0f);
                RectTransform shotRect = frame.GetComponent<RectTransform>();
                shotRect.anchorMin = shotRect.anchorMax = shotRect.pivot = new Vector2(0.5f, 0.5f);
                shotRect.sizeDelta = new Vector2(shotW, shotH);
                shotRect.anchoredPosition = new Vector2(-(rowW - shotW) / 2f, 0f);

                // AND THE NUMBERS, left-aligned beside it, in two tiers. The heading is what the eye
                // scans to find a row; the clock and the count are what it stops on. One block of
                // three equal lines made all three equally hard to find.
                // Just clear of the picture's right edge, in the row's own frame.
                float textX = -(rowW - shotW) / 2f + shotW / 2f + 26f;

                Text head = MakeMenuLine(row.transform, "Head", string.Empty, 30, MenuInk,
                    new Vector2(textX + 150f, 30f), new Vector2(300f, 40f), TextAnchor.MiddleLeft);
                recordRowHead[i] = head;

                Text text = MakeMenuLine(row.transform, "Text", string.Empty, 22,
                    new Color(0.11f, 0.11f, 0.13f, 0.72f),
                    new Vector2(textX + 150f, -22f), new Vector2(300f, 60f), TextAnchor.MiddleLeft);
                text.lineSpacing = 1.3f;
                recordRowText[i] = text;

                recordRows[i] = row;
            }

            // What the page says with nothing on it. The RECORD button is hidden until something has
            // been finished, so this is close to unreachable - and a page that CAN be empty has to
            // have an answer for it, or it is a blank screen with a BACK button.
            Text recordEmpty = Localize(MakeMenuLine(recordGO.transform, "RecordEmpty",
                "NO CYCLE COMPLETED", 24, new Color(0.11f, 0.11f, 0.13f, 0.55f), new Vector2(0f, 0f), new Vector2(600f, 60f)),
                "record.empty");

            Button recordBack = Localize(MakeMenuButtonInk(recordGO.transform, "RecordBackButton",
                                                  "BACK", new Vector2(0f, -230f)), "menu.back", "  ");

            // SETTINGS, on its own page over the same background as the cycle picker. It belongs on
            // the TITLE screen rather than only in the pause menu because the first thing this game
            // does is talk - the PA is running before the player has a control to press - and "turn it
            // down" should not require starting first. The same argument now carries the other two:
            // a player who already knows their sensitivity, or who cannot use the default movement
            // keys at all, should not have to play an iteration to say so.
            GameObject settingsGO = new GameObject("Settings");
            settingsGO.transform.SetParent(canvasGO.transform, false);
            CanvasGroup settingsGroup = settingsGO.AddComponent<CanvasGroup>();
            settingsGroup.alpha = 0f;
            settingsGroup.blocksRaycasts = false;
            Stretch(settingsGO.AddComponent<RectTransform>());

            // EVERYTHING ON ONE LEFT EDGE, the same one the title screen's buttons and this page's
            // own BACK are hung on - see MakeLeftColumn for why that is a container rather than a
            // number written into each row.
            Transform col = MakeLeftColumn(settingsGO.transform, "Column");

            // The page says its own name, in the title's face rather than the buttons'. The title
            // screen sets its hierarchy with SIZE and WEIGHT and no rules or boxes anywhere, so this
            // page does too: ExtraLight 52 against the rows' Regular 18 is what separates them.
            Localize(MakeRowLabelWeight(col, "SettingsHeading", "SETTINGS",
                new Vector2(300f, 330f), new Vector2(600f, 70f), TextAnchor.MiddleLeft, MenuInk,
                "ExtraLight", 52), "menu.settings");

            // THE FOUR ROWS, evenly spaced, label on the edge and control at one indent. Language
            // first because it is the setting that rewrites every other label on the page, including
            // BACK - a player who has landed here by accident should reach it before reading anything.
            // Subtitles second, directly under it: it is the other setting about words on screen, and
            // the two are read together.
            //
            // The step went 55 -> 50 to fit the fourth without pushing CONTROLS into BACK; the
            // bindings panel drops by `SettingsControlsDrop` to take up the rest.
            const float languageRowY = 240f;
            const float subtitleRowY = 190f;
            const float volumeRowY = 140f;
            const float sensitivityRowY = 90f;

            MakeRowLabelInk(col, "LanguageLabel", "LANGUAGE",
                new Vector2(SettingsLabelWidth / 2f, languageRowY),
                new Vector2(SettingsLabelWidth, 30f), TextAnchor.MiddleLeft, MenuInk);
            // **THE TWO BUTTONS ARE NEVER TRANSLATED.** "ENGLISH" and "한국어" each stay in their own
            // language whichever is selected, which is the one convention every language picker
            // follows and the only one that works: a player who has landed in a language they cannot
            // read has to be able to find their way out, and "영어"/"한국어" would be two words they
            // cannot tell apart. It is also why they are plain `MakeSettingsButton`s with no
            // `Localize` on them.
            Button englishButton = MakeSettingsButton(col, "LanguageEnglish", "ENGLISH",
                new Vector2(SettingsControlX + 75f, languageRowY), new Vector2(150f, 30f), out Text englishInk);
            Button koreanButton = MakeSettingsButton(col, "LanguageKorean", "한국어",
                new Vector2(SettingsControlX + 240f, languageRowY), new Vector2(150f, 30f), out Text koreanInk);
            // The Korean button has to be able to draw its own name before anything has switched, so
            // it is the one label in the project that takes the Hangul face at build time.
            Font koreanFace = KoreanUIFont();
            if (koreanFace != null) koreanInk.font = koreanFace;

            // **TWO BUTTONS, NOT A SLIDER OR A CHECKBOX.** It is the same shape as the language
            // row directly above it - a pair of states, the live one lit and the other dimmed - and
            // repeating that shape is what makes the top of this page read as one block of two
            // choices rather than as four unrelated widgets. The key that also does this (M) is
            // listed in CONTROLS below, where every other key is.
            MakeRowLabelInk(col, "SubtitleLabel", "SUBTITLES",
                new Vector2(SettingsLabelWidth / 2f, subtitleRowY),
                new Vector2(SettingsLabelWidth, 30f), TextAnchor.MiddleLeft, MenuInk);
            Button subtitlesOnButton = Localize(MakeSettingsButton(col, "SubtitlesOn", "ON",
                new Vector2(SettingsControlX + 75f, subtitleRowY), new Vector2(150f, 30f),
                out Text subtitlesOnInk), "set.on");
            Button subtitlesOffButton = Localize(MakeSettingsButton(col, "SubtitlesOff", "OFF",
                new Vector2(SettingsControlX + 240f, subtitleRowY), new Vector2(150f, 30f),
                out Text subtitlesOffInk), "set.off");

            (Slider volumeSlider, Text volumeValue) =
                MakeSettingsSliderRow(col, "Volume", "VOLUME", "set.volume", "80%", volumeRowY, MenuInk);
            (Slider sensitivitySlider, Text sensitivityValue) =
                MakeSettingsSliderRow(col, "Sensitivity", "MOUSE SENSITIVITY", "set.sensitivity",
                                      "1.10", sensitivityRowY, MenuInk);

            KeyBindingPanel bindings = BuildKeyBindings(col, settingsGroup);

            // BACK sits on the same edge as everything above it, which is the whole point of the
            // column. It anchors to the screen's left rather than to the column, because that is what
            // MakeMenuButton does and the two edges are now the same edge.
            Button settingsBack = Localize(MakeMenuButtonInk(settingsGO.transform, "SettingsBackButton",
                                                 "BACK", new Vector2(0f, -355f)), "menu.back", "  ");

            // The loading state, built over the same middle of the screen the buttons occupy so
            // one replaces the other in place instead of the eye having to travel.
            GameObject loadingGO = new GameObject("Loading");
            loadingGO.transform.SetParent(canvasGO.transform, false);
            CanvasGroup loadingGroup = loadingGO.AddComponent<CanvasGroup>();
            loadingGroup.alpha = 0f;
            loadingGroup.blocksRaycasts = false;
            Stretch(loadingGO.AddComponent<RectTransform>());

            GameObject loadingLabelGO = new GameObject("Label");
            loadingLabelGO.transform.SetParent(loadingGO.transform, false);
            Text loadingLabel = loadingLabelGO.AddComponent<Text>();
            loadingLabel.font = UIFont();
            loadingLabel.fontSize = 22;
            loadingLabel.alignment = TextAnchor.MiddleCenter;
            // Charcoal like everything else on this surface - the loading page has no backdrop of
            // its own, it is the same photograph of a white room with a bar drawn over it.
            loadingLabel.font = UIFont("Medium");
            loadingLabel.color = MenuInk;
            loadingLabel.text = "LOADING 0%";
            loadingLabel.raycastTarget = false;
            RectTransform loadingLabelRect = loadingLabel.GetComponent<RectTransform>();
            loadingLabelRect.anchorMin = new Vector2(0.5f, 0.5f);
            loadingLabelRect.anchorMax = new Vector2(0.5f, 0.5f);
            loadingLabelRect.sizeDelta = new Vector2(600f, 40f);
            loadingLabelRect.anchoredPosition = new Vector2(0f, -20f);

            GameObject barGO = new GameObject("Bar");
            barGO.transform.SetParent(loadingGO.transform, false);
            Image bar = barGO.AddComponent<Image>();
            bar.color = new Color(0f, 0f, 0f, 0.6f);
            bar.raycastTarget = false;
            RectTransform barRect = bar.GetComponent<RectTransform>();
            barRect.anchorMin = new Vector2(0.5f, 0.5f);
            barRect.anchorMax = new Vector2(0.5f, 0.5f);
            barRect.sizeDelta = new Vector2(560f, 8f);
            barRect.anchoredPosition = new Vector2(0f, -60f);

            GameObject fillGO = new GameObject("Fill");
            fillGO.transform.SetParent(barGO.transform, false);
            Image fill = fillGO.AddComponent<Image>();
            // A filled Image needs a sprite to have anything to fill; this is uGUI's own.
            fill.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillAmount = 0f;
            fill.color = Color.red;
            fill.raycastTarget = false;
            Stretch(fill.GetComponent<RectTransform>());

            // THE CREDITS, read out of the models themselves and put on a page of their own.
            //
            // What was here was a single dim line reading "FURNITURE MODELS: CREATIVE COMMONS". The
            // reasoning for it was right - credit belongs where the player already is, not somewhere
            // they have to go looking - but the line was not an attribution, and twenty-six of them
            // do not fit on it. So the strip stays as the POINTER and the page carries the content:
            // still on the title screen, still unmissable, one click away instead of nought.
            System.Collections.Generic.List<ModelCredit> modelCredits = ReadModelCredits();
            CheckModelLicences(modelCredits);
            WriteAttributionFile(modelCredits);

            (CanvasGroup creditsGroup, Button creditsBack) = BuildCreditsPage(canvasGO.transform, modelCredits);

            // **OUT OF THE COLUMN AND INTO THE CORNER** (2026-08-24, by request), directly above
            // RECORD - and the reason is the one already written beside RECORD rather than a new one.
            // Every entry in that column is a way INTO the game or a way OUT of it; a licence list is
            // neither. Two of them now sit off to one side, which is where the things the facility
            // keeps belong.
            // **IN THE COLUMN NOW, NOT IN THE CORNER** (2026-09-04, to `docs/mainmenu_dark.png`,
            // which lists it between SETTINGS and QUIT). It was cornered with RECORD because six
            // rows once pushed QUIT to within seventy pixels of the screen edge - see the pitch at
            // the call sites above, which is re-spaced to carry six rows without that. RECORD stays
            // cornered: it is a thing the facility keeps, not a way into or out of the game, which
            // is the distinction that put it there in the first place.
            Button creditsButton = MakeMenuButton(menuGO.transform, "CreditsButton", "CREDITS",
                                                  new Vector2(0f, -302f), MenuInk, "Bold");
            Localize(creditsButton, "menu.credits", "  ");

            // THE ROOM TONE, ON THE TITLE SCREEN. The same clip `RoomAmbience` runs in the game, at
            // less than half the level - see MainMenu.ambienceVolume. 2D, looping, and started at
            // zero so `MainMenu.Start` can ride it up instead of the loop's first sample clicking in.
            //
            // The menu scene already has the one AudioListener it is allowed (on its camera), so this
            // needs nothing else to be heard - and `GameSettings.ApplyAudio` in MainMenu.Awake has
            // already put the player's saved master volume on that listener by the time it fades in.
            AudioSource menuTone = MakeSource(canvasGO.transform, "Ambience", 0f, 0f, loop: true);
            menuTone.clip = LoadClip(SfxDir, "sfx_ominous_loop");
            menuTone.playOnAwake = true;

            MainMenu mainMenu = canvasGO.AddComponent<MainMenu>();
            mainMenu.menuGroup = menuGroup;
            mainMenu.loadingGroup = loadingGroup;
            mainMenu.playButton = playButton;
            mainMenu.quitButton = quitButton;
            mainMenu.continueButton = continueButton;
            mainMenu.continueCycleLabel = continueCycle;
            mainMenu.ambience = menuTone;
            mainMenu.cycleSelectButton = cycleSelectButton;
            mainMenu.recordButton = recordButton;
            mainMenu.recordBackButton = recordBack;
            mainMenu.recordGroup = recordGroup;
            mainMenu.recordRows = recordRows;
            mainMenu.recordRowHead = recordRowHead;
            mainMenu.recordRowText = recordRowText;
            mainMenu.recordEmpty = recordEmpty;
            mainMenu.cycleBackButton = cycleBack;
            mainMenu.cycleGroup = cycleGroup;
            mainMenu.cycleButtons = cycleButtons;
            mainMenu.cycleEndButtons = cycleEndButtons;
            mainMenu.creditsButton = creditsButton;
            mainMenu.creditsBackButton = creditsBack;
            mainMenu.creditsGroup = creditsGroup;
            mainMenu.settingsButton = settingsButton;
            mainMenu.settingsBackButton = settingsBack;
            mainMenu.settingsGroup = settingsGroup;
            mainMenu.sensitivitySlider = sensitivitySlider;
            mainMenu.subtitlesOnButton = subtitlesOnButton;
            mainMenu.subtitlesOffButton = subtitlesOffButton;
            mainMenu.subtitlesOnInk = subtitlesOnInk;
            mainMenu.subtitlesOffInk = subtitlesOffInk;
            mainMenu.englishButton = englishButton;
            mainMenu.koreanButton = koreanButton;
            mainMenu.englishInk = englishInk;
            mainMenu.koreanInk = koreanInk;
            mainMenu.sensitivityValue = sensitivityValue;
            mainMenu.bindings = bindings;
            mainMenu.volumeSlider = volumeSlider;
            mainMenu.volumeValue = volumeValue;
            mainMenu.loadingFill = fill;
            mainMenu.loadingLabel = loadingLabel;

            // uGUI buttons are inert without one, and an empty scene has nothing at all in it.
            GameObject eventSystem = new GameObject("EventSystem");
            eventSystem.AddComponent<UnityEngine.EventSystems.EventSystem>();
            eventSystem.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();

            EditorSceneManager.SaveScene(menu, MenuScenePath);
        }

        private static RectTransform MakePlate(Transform parent, string name, Vector2 anchoredPosition, Vector2 size)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);

            Image plate = go.AddComponent<Image>();
            // GrooveDark, the same near-black that sits at the bottom of every groove in the room -
            // so a panel of UI reads as part of the facility rather than as an overlay.
            plate.color = new Color(0.04f, 0.04f, 0.045f, 0.82f);
            plate.raycastTarget = false;

            RectTransform rect = plate.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = anchoredPosition;
            return rect;
        }

        private static void MakeKeyCap(Transform parent, string name, string label,
                                       Vector2 anchoredPosition, Vector2 size, int fontSize = 26)
        {
            Sprite rounded = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");

            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);

            Image outer = go.AddComponent<Image>();
            outer.sprite = rounded;
            outer.type = Image.Type.Sliced;
            outer.color = new Color(0.75f, 0.14f, 0.14f, 0.95f);
            outer.raycastTarget = false;
            RectTransform rect = outer.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = anchoredPosition;

            GameObject faceGO = new GameObject("Face");
            faceGO.transform.SetParent(go.transform, false);
            Image face = faceGO.AddComponent<Image>();
            face.sprite = rounded;
            face.type = Image.Type.Sliced;
            face.color = new Color(0.06f, 0.05f, 0.06f, 0.98f);
            face.raycastTarget = false;
            RectTransform faceRect = face.GetComponent<RectTransform>();
            faceRect.anchorMin = Vector2.zero;
            faceRect.anchorMax = Vector2.one;
            // Inset on all four sides, so the outer colour shows as an even rim at any cap size.
            faceRect.offsetMin = new Vector2(3f, 3f);
            faceRect.offsetMax = new Vector2(-3f, -3f);

            GameObject textGO = new GameObject("Label");
            textGO.transform.SetParent(go.transform, false);
            Text text = textGO.AddComponent<Text>();
            text.font = UIFont();
            text.fontSize = fontSize;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = new Color(1f, 0.45f, 0.45f, 1f);
            text.text = label;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            Stretch(text.GetComponent<RectTransform>());
        }

        // The word beside a key or a glyph. Takes the x its text should START at, since every
        // caption in a column has to begin at the same place whatever its length - the rect is
        // centred, so the offset below is half its width. 190 fits the longest of these
        // ("LOOK AROUND", 11 monospace cells) at either size it is used in.
        //
        // The colour is a parameter because these appear on two very different backgrounds: the
        // wall display puts a near-black plate under them and wants the HUD's light red, where
        // anything drawn straight onto the room's white panelling needs a much darker one.
        private static void MakeCaption(Transform parent, string name, string content, Vector2 leftEdge,
                                        Color? color = null, int fontSize = 22) =>
            MakeMenuLine(parent, name, content, fontSize, color ?? new Color(0.7f, 0.06f, 0.06f, 1f),
                leftEdge + new Vector2(95f, 0f), new Vector2(190f, 30f), TextAnchor.MiddleLeft);

        // WHO MADE WHAT, READ OUT OF THE FILES THEMSELVES.
        //
        // Every `.glb` here came off Sketchfab, and Sketchfab's exporter writes the title, the author,
        // the licence and the source URL into the glTF's own `asset.extras`. That is the primary
        // record, it cannot get separated from the file it describes, and reading it is what makes a
        // credits screen impossible to forget to update.
        //
        // **THE HAND-MAINTAINED VERSION WAS WRONG, WHICH IS WHY THIS IS CODE.**
        // `docs/asset-licences.md` was a table somebody typed, and checking it against the files found:
        // `chess.glb` credited to the wrong person, `rubiks_cube.glb` credited to the wrong person,
        // thirteen models missing from it entirely, and - the one that matters - `cctv_camera.glb`
        // recorded as CC-BY when the file says **CC-BY-NC**. That is the same argument `FloorButton`'s
        // audio settled: a list of every instance of a thing, maintained by hand, is a list that will
        // be wrong. So there is no list.
        private struct ModelCredit
        {
            public string file, title, author, authorUrl, licence, source;
        }

        // Only the fields wanted, so `JsonUtility` can ignore the rest of a 30MB glTF header.
        [System.Serializable] private class GltfExtras
        {
            public string title, author, license, source;
        }
        [System.Serializable] private class GltfAssetBlock { public GltfExtras extras; }
        [System.Serializable] private class GltfHeader { public GltfAssetBlock asset; }

        private static System.Collections.Generic.List<ModelCredit> ReadModelCredits()
        {
            var credits = new System.Collections.Generic.List<ModelCredit>();
            if (!Directory.Exists(ArtAssetsDir)) return credits;

            foreach (string path in Directory.GetFiles(ArtAssetsDir, "*.glb", SearchOption.AllDirectories))
            {
                // The retarget spike is scratch, not shipped. Anything under a folder starting with
                // `_` is the same kind of thing.
                if (path.Replace('\\', '/').Contains("/_")) continue;

                GltfExtras extras = ReadGltfExtras(path);
                if (extras == null || string.IsNullOrEmpty(extras.author))
                {
                    // Silence here would be the whole bug coming back. A model whose file carries no
                    // attribution needs one found by hand and written into the docs.
                    Debug.LogWarning($"[SceneBuilder] {Path.GetFileName(path)} carries no author in its "
                                   + "glTF extras - it cannot be credited automatically. Find its source "
                                   + "and record it in docs/asset-licences.md.");
                    continue;
                }

                credits.Add(new ModelCredit
                {
                    file = Path.GetFileName(path),
                    title = string.IsNullOrEmpty(extras.title) ? Path.GetFileNameWithoutExtension(path) : extras.title,
                    author = BeforeParen(extras.author),
                    authorUrl = InsideParen(extras.author),
                    licence = PrettyLicence(BeforeParen(extras.license)),
                    source = extras.source,
                });
            }

            credits.Sort((a, b) => string.Compare(a.title, b.title, System.StringComparison.OrdinalIgnoreCase));
            return credits;
        }

        // A GLB is a 12-byte header then length-prefixed chunks; the first is always the JSON.
        private static GltfExtras ReadGltfExtras(string path)
        {
            try
            {
                using (FileStream fs = File.OpenRead(path))
                using (var br = new BinaryReader(fs))
                {
                    if (br.ReadUInt32() != 0x46546C67u) return null;   // not "glTF"
                    br.ReadUInt32();                                   // version
                    br.ReadUInt32();                                   // total length
                    int chunkLength = (int)br.ReadUInt32();
                    if (br.ReadUInt32() != 0x4E4F534Au) return null;   // first chunk is not "JSON"

                    string json = System.Text.Encoding.UTF8.GetString(br.ReadBytes(chunkLength));
                    return JsonUtility.FromJson<GltfHeader>(json)?.asset?.extras;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[SceneBuilder] could not read glTF header of {Path.GetFileName(path)}: {e.Message}");
                return null;
            }
        }

        // Sketchfab writes `Name (url)` and `CC-BY-4.0 (url)`.
        private static string BeforeParen(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            int i = value.IndexOf(" (", System.StringComparison.Ordinal);
            return (i < 0 ? value : value.Substring(0, i)).Trim();
        }

        private static string InsideParen(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            int open = value.IndexOf('(');
            int close = value.LastIndexOf(')');
            return open >= 0 && close > open ? value.Substring(open + 1, close - open - 1) : "";
        }

        // `CC-BY-NC-4.0` is the machine's spelling; `CC BY-NC 4.0` is the one the licence itself uses.
        private static string PrettyLicence(string code) =>
            string.IsNullOrEmpty(code) ? "?" : code.Replace("CC-", "CC ").Replace("-4.0", " 4.0");

        // THE CREDITS PAGE. Every model, its author and its licence, on a page of its own over the
        // same background as SETTINGS and the cycle picker.
        //
        // **A PAGE RATHER THAN THE ONE-LINE STRIP IT REPLACES.** The strip said "FURNITURE MODELS:
        // CREATIVE COMMONS", which names no author, no title, no licence version and no link - it is
        // a statement that a licence exists somewhere, not an attribution. CC-BY 4.0 asks for the
        // creator, the title, the licence and a link where practicable, and twenty-six of those do
        // not fit on one line at the bottom of a title screen.
        //
        // Two columns, because one would run off the bottom at any readable size.
        private static (CanvasGroup group, Button back) BuildCreditsPage(
            Transform canvas, System.Collections.Generic.List<ModelCredit> credits)
        {
            GameObject root = new GameObject("Credits");
            root.transform.SetParent(canvas, false);
            CanvasGroup group = root.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = false;
            Stretch(root.AddComponent<RectTransform>());

            MakeMenuLine(root.transform, "Title", "CREDITS", 52, MenuInk,
                         new Vector2(0f, 430f), new Vector2(900f, 70f));

            // The heading says what the list IS, so a player reading it knows these are obligations
            // being met rather than a thank-you note.
            MakeMenuLine(root.transform, "Subtitle",
                         "3D MODELS FROM SKETCHFAB, USED UNDER CREATIVE COMMONS", 15,
                         new Color(MenuInk.r, MenuInk.g, MenuInk.b, 0.6f),
                         new Vector2(0f, 386f), new Vector2(1200f, 24f));

            const int perColumn = 13;
            const float rowPitch = 26f;
            const float top = 340f;

            for (int i = 0; i < credits.Count; i++)
            {
                ModelCredit c = credits[i];
                int column = i / perColumn;
                int row = i % perColumn;
                // Two columns either side of centre. Left-aligned within each, so the eye has one
                // edge to run down rather than a ragged centred stack.
                float x = column == 0 ? -470f : 60f;

                MakeMenuLine(root.transform, $"Credit{i}",
                             $"\"{c.title}\" — {c.author} — {c.licence}", 13, MenuInk,
                             new Vector2(x + 200f, top - row * rowPitch), new Vector2(400f, 22f),
                             TextAnchor.MiddleLeft);
            }

            // The typefaces are the other third-party thing in the build, and the OFL wants naming
            // too. Audio and code are this project's own - see docs/asset-licences.md.
            MakeMenuLine(root.transform, "Fonts",
                         "TYPE: JETBRAINS MONO (JETBRAINS) AND D2CODING (NAVER), BOTH SIL OFL 1.1", 13,
                         new Color(MenuInk.r, MenuInk.g, MenuInk.b, 0.75f),
                         new Vector2(0f, -60f), new Vector2(1400f, 22f));

            MakeMenuLine(root.transform, "Links",
                         "FULL LIST WITH LINKS: ATTRIBUTION.md, SHIPPED BESIDE THE GAME", 13,
                         new Color(MenuInk.r, MenuInk.g, MenuInk.b, 0.55f),
                         new Vector2(0f, -86f), new Vector2(1400f, 22f));

            Button back = Localize(MakeMenuButtonInk(root.transform, "CreditsBackButton",
                                                     "BACK", new Vector2(0f, -230f)), "menu.back", "  ");
            return (group, back);
        }

        // THE FULL ATTRIBUTION, WITH URLS, AS A FILE THAT TRAVELS WITH THE BUILD.
        //
        // The page names creator, title and licence, which is a reasonable manner for a screen. The
        // licence also asks for a link to the material "where practicable", and twenty-six URLs on a
        // menu is not practicable - but a text file beside the executable is. Written on every build
        // from the same source the page uses, so the two cannot disagree.
        private static void WriteAttributionFile(System.Collections.Generic.List<ModelCredit> credits)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("# Attribution");
            sb.AppendLine();
            sb.AppendLine("Iteration Room uses the following third-party assets.");
            sb.AppendLine("**Generated by `SceneBuilder` from each file's own glTF metadata — do not edit by hand.**");
            sb.AppendLine();
            sb.AppendLine("## 3D models");
            sb.AppendLine();

            foreach (ModelCredit c in credits)
            {
                sb.AppendLine($"- **\"{c.title}\"** by {c.author} — {c.licence}");
                if (!string.IsNullOrEmpty(c.source)) sb.AppendLine($"  - Source: {c.source}");
                if (!string.IsNullOrEmpty(c.authorUrl)) sb.AppendLine($"  - Author: {c.authorUrl}");
            }

            sb.AppendLine();
            sb.AppendLine("## Fonts");
            sb.AppendLine();
            sb.AppendLine("- **JetBrains Mono** by JetBrains — SIL Open Font License 1.1");
            sb.AppendLine("- **D2Coding** by NAVER Corporation — SIL Open Font License 1.1");
            sb.AppendLine();
            sb.AppendLine("## Everything else");
            sb.AppendLine();
            sb.AppendLine("Code, scenes, textures, materials, meshes, shaders and sound effects are this");
            sb.AppendLine("project's own work. See `docs/asset-licences.md` for the full record.");

            string path = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "ATTRIBUTION.md");
            File.WriteAllText(path, sb.ToString());
            Debug.Log($"[SceneBuilder] Attribution written for {credits.Count} models to {path}");
        }

        // **THE BUILD POLICES THE LICENCES NOW**, because the last check was a document and the
        // document was wrong. NC cannot be sold at all, so it fails loudly; SA is sellable but viral
        // and wants a decision rather than a surprise.
        private static void CheckModelLicences(System.Collections.Generic.List<ModelCredit> credits)
        {
            foreach (ModelCredit c in credits)
            {
                if (c.licence.Contains("NC"))
                    Debug.LogError($"[SceneBuilder] NON-COMMERCIAL ASSET: {c.file} is {c.licence} "
                                 + $"(\"{c.title}\" by {c.author}). This CANNOT ship in a paid build - "
                                 + "replace the model or keep the game free. See docs/asset-licences.md.");
                else if (c.licence.Contains("SA"))
                    Debug.LogWarning($"[SceneBuilder] SHARE-ALIKE ASSET: {c.file} is {c.licence} "
                                   + $"(\"{c.title}\" by {c.author}). Sellable, but the model and any "
                                   + "modification of it stay under the same licence - decide deliberately.");
            }
        }

        private static Text MakeMenuLine(Transform parent, string name, string content, int fontSize,
                                         Color color, Vector2 anchoredPosition, Vector2 size,
                                         TextAnchor alignment = TextAnchor.MiddleCenter)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);

            Text text = go.AddComponent<Text>();
            text.font = UIFont();
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = color;
            text.text = content;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;

            RectTransform rect = text.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = anchoredPosition;

            return text;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        // A LEFT-TO-RIGHT DARKENING RAMP, so the words have something to sit on and the room does
        // not. Generated rather than authored, like every other texture here.
        //
        // The falloff is squared and stops well short of the right edge: the aim is a pool of shade
        // under the column, not a vignette, and anything that reaches the middle starts reading as a
        // dimmed screenshot again.
        private static Sprite MakeMenuScrimSprite()
        {
            const int w = 256, h = 4;
            Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "MenuScrim" };
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;

            // **BLACK AGAIN SINCE 2026-09-04, AND IT HAS NOW BEEN BOTH TWICE.** It was a dark ramp
            // when the type was red, went white when the type became charcoal over a bright room,
            // and comes back to dark now that the room behind it is black and the type is near-white
            // (`MenuInk`). The rule underneath all three: the ramp is whatever the TYPE is not.
            //
            // Much gentler than the white one was, because it no longer has anything to fight. The
            // white version was at 0.86 to flatten a wall of black grooves on white - the worst
            // possible field to set type in. The dark room is already an even near-black field, so
            // this only has to stop the doorway's spill from reaching under the words: 0.55 at the
            // edge, gone by 55% across, which leaves the doorway itself untouched in the middle.
            for (int x = 0; x < w; x++)
            {
                float u = x / (float)(w - 1);
                // Full strength at the very edge, gone by 55% across.
                float k = 1f - Mathf.Clamp01(u / 0.55f);
                float a = k * k * 0.55f;
                Color c = new Color(0f, 0f, 0f, a);
                for (int y = 0; y < h; y++) tex.SetPixel(x, y, c);
            }
            tex.Apply();

            string path = TexturesDir + "/MenuScrim.png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            // THE TYPE HAS TO BE SET BEFORE THE ASSET IS ASKED FOR AS A SPRITE. A freshly written PNG
            // imports as a plain Texture2D by default, and `LoadAssetAtPath<Sprite>` on one of those
            // returns null - which is how the scrim ended up as a white sheet.
            if (AssetImporter.GetAtPath(path) is TextureImporter imp)
            {
                imp.textureType = TextureImporterType.Sprite;
                imp.spriteImportMode = SpriteImportMode.Single;
                imp.alphaIsTransparency = true;
                imp.mipmapEnabled = false;
                imp.wrapMode = TextureWrapMode.Clamp;
                imp.SaveAndReimport();
            }
            AssetDatabase.Refresh();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        // `ink` null keeps the red-on-dark this was built with, which is what the PAUSE menu wants -
        // it sits over a 0.72 black scrim, where charcoal would be invisible. The title screen passes
        // `MenuInk`, because it sits over a bright white room.
        //
        // THE WHITE SLAB IS GONE, and it was the single loudest thing on the title screen: five filled
        // rectangles stacked down the edge of a photograph of a white room. The plate is still THERE -
        // a `Button` needs a graphic to receive a click - it is simply transparent until the pointer
        // is on it, which is the whole of the difference between a menu that looks built and one that
        // looks laid out.
        // THE CONTROLS LIST on the settings page: every verb `InputBindings` knows about, as a label
        // and a button showing the key it is on.
        //
        // TWO COLUMNS, because eleven rows in one would run past the BACK button and off a 1080-tall
        // reference canvas. The split is computed from the count rather than written down, so adding a
        // twelfth verb to `InputBindings.All` re-balances the page instead of overflowing it.
        //
        // The key buttons are authored showing the DEFAULT binding, not the current one. This runs in
        // the Editor, where `InputBindings.Get` would read whatever the developer's own PlayerPrefs
        // happen to hold and bake it into the shipped scene; `KeyBindingPanel.Awake` refreshes every
        // row from the real bindings on the first frame anyway.
        private static KeyBindingPanel BuildKeyBindings(Transform page, CanvasGroup group)
        {
            GameObject root = new GameObject("KeyBindings");
            root.transform.SetParent(page, false);
            RectTransform rootRect = root.AddComponent<RectTransform>();
            // Zero-sized and on its parent's own origin, so the column edge stays the column edge one
            // level down and every x below is still "how far in from the edge".
            rootRect.anchorMin = rootRect.anchorMax = new Vector2(0.5f, 0.5f);
            rootRect.pivot = new Vector2(0.5f, 0.5f);
            rootRect.sizeDelta = Vector2.zero;
            // **DROPPED, TO PAY FOR THE FOURTH SETTINGS ROW.** Everything below is written relative
            // to this origin, so moving the panel moves the heading, the hint and all twelve rows
            // together and none of the numbers below had to change.
            rootRect.anchoredPosition = new Vector2(0f, -SettingsControlsDrop);

            // A section heading one step below the page's own: Medium 26 against ExtraLight 52. The
            // title screen sets its hierarchy with size and weight and draws no rules or boxes
            // anywhere, so this page does the same.
            Localize(MakeRowLabelWeight(root.transform, "ControlsHeading", "CONTROLS",
                new Vector2(200f, 55f), new Vector2(400f, 32f), TextAnchor.MiddleLeft, MenuInk,
                "Medium", 26), "set.controls");
            // Paler than the labels: it is an instruction about the list rather than part of it, and
            // it is the one line on the page that changes while the player is using it.
            Text hint = MakeRowLabelInk(root.transform, "ControlsHint", "CLICK A KEY TO CHANGE IT",
                new Vector2(310f, 22f), new Vector2(620f, 24f), TextAnchor.MiddleLeft,
                new Color(MenuInk.r, MenuInk.g, MenuInk.b, 0.55f));

            GameAction[] actions = InputBindings.All;
            var rows = new KeyBindingPanel.Row[actions.Length];

            const float firstRowY = -25f, rowStep = 40f, secondColumnX = 580f;
            int perColumn = (actions.Length + 1) / 2;

            for (int i = 0; i < actions.Length; i++)
            {
                float originX = i / perColumn == 0 ? 0f : secondColumnX;
                float y = firstRowY - (i % perColumn) * rowStep;

                // 250 wide against the longest label ("STRAFE RIGHT", 12 characters, about 130px at
                // this size) - the 0.6 x fontSize x length check CLAUDE.md asks for, with room spare.
                Localize(MakeRowLabelInk(root.transform, "Label_" + actions[i], InputBindings.Label(actions[i]),
                    new Vector2(originX + 125f, y), new Vector2(250f, 26f), TextAnchor.MiddleLeft, MenuInk),
                    "act." + actions[i]);

                Button keyButton = MakeSettingsButton(root.transform, "Key_" + actions[i],
                    InputBindings.KeyLabel(InputBindings.DefaultFor(actions[i])),
                    new Vector2(originX + 370f, y), new Vector2(190f, 30f), out Text keyLabel);

                rows[i] = new KeyBindingPanel.Row
                {
                    action = actions[i],
                    button = keyButton,
                    keyLabel = keyLabel,
                };
            }

            // Left edge on the column like every label above it, so its centre - which is what a
            // centre-pivot rect is positioned by - sits half its width in.
            Button reset = Localize(MakeSettingsButton(root.transform, "ResetBindings", "RESET TO DEFAULTS",
                new Vector2(140f, -280f), new Vector2(280f, 34f), out _), "set.resetBindings");

            KeyBindingPanel panel = root.AddComponent<KeyBindingPanel>();
            panel.rows = rows;
            panel.resetButton = reset;
            panel.hint = hint;
            // So the panel can tell whether the page it is on is the one being looked at - an alpha of
            // zero does not stop Update, and a row left listening behind a closed page would eat the
            // title screen's next keypress.
            panel.group = group;
            return panel;
        }

        // A COMPACT, CENTRED BUTTON for the settings page, which `MakeMenuButton` cannot be: that one
        // is 340x66 and anchors itself to the left margin so the title column lines up, which is right
        // for five choices and wrong for eleven rows in a grid.
        //
        // **VISIBLE AT REST, unlike the title column's buttons.** Those draw nothing until the pointer
        // is over them, on the argument that the mark should be earned rather than five of them sitting
        // on screen. A key binding is a VALUE being displayed as well as a control, so it has to have a
        // plate around it whether or not anything is hovering it.
        // How far the CONTROLS block drops to make room for the SUBTITLES row above it. The four
        // settings rows end at y=90 and the bindings heading was at y=55; 40 puts it back to the
        // same gap it had when there were three.
        private const float SettingsControlsDrop = 40f;

        private static Button MakeSettingsButton(Transform parent, string name, string label,
                                                 Vector2 anchoredPosition, Vector2 size, out Text text)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);

            RectTransform rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = anchoredPosition;

            // White, with the whole look coming from the ColorBlock - a Button tints its target
            // graphic by MULTIPLYING, so a plate that is already dark has nothing left to shade with.
            Image background = go.AddComponent<Image>();
            background.color = Color.white;

            GameObject textGO = new GameObject("Label");
            textGO.transform.SetParent(go.transform, false);
            text = textGO.AddComponent<Text>();
            text.font = UIFont("Medium");
            text.fontSize = 18;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = MenuInk;
            text.text = label;
            text.raycastTarget = false;
            // Overflow rather than wrap: "MIDDLE MOUSE" is the longest thing this can be asked to hold
            // and it fits, but a rebind can put any key label in here and a silent rewrap to two lines
            // is the failure CLAUDE.md warns about twice.
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            Stretch(text.GetComponent<RectTransform>());

            Button button = go.AddComponent<Button>();
            button.targetGraphic = background;

            ColorBlock colors = button.colors;
            colors.normalColor = new Color(0.11f, 0.11f, 0.13f, 0.07f);
            colors.highlightedColor = new Color(0.11f, 0.11f, 0.13f, 0.16f);
            colors.pressedColor = new Color(0.80f, 0.10f, 0.10f, 0.22f);
            // Back to rest once the click is over. Left on `highlightedColor`, the last row clicked
            // stays lit for the rest of the page's life and reads as still waiting for a key.
            colors.selectedColor = colors.normalColor;
            colors.disabledColor = new Color(0f, 0f, 0f, 0f);
            colors.fadeDuration = 0.12f;
            button.colors = colors;

            return button;
        }

        // A CONTAINER HUNG ON THE SAME LEFT EDGE THE MENU BUTTONS USE, so a page can mix rows with
        // buttons and have the two line up.
        //
        // **THIS EXISTS BECAUSE THE CANVAS SCALER MATCHES 0.5.** `MakeMenuButton` anchors to the
        // screen's LEFT edge at `MenuLeftMargin`; `MakeRowLabel`, `MakeSlider` and everything else
        // that takes an `anchoredPosition` anchors to the CENTRE. At exactly 16:9 those two can be
        // made to agree by writing `132 - 960` into every row, and at any other aspect ratio they
        // silently drift apart - the canvas's logical width changes with the aspect, so the centre
        // moves relative to the left edge. The settings page was built that way first and it is why
        // it read as two designs stacked: a block of rows floating in the middle with a BACK button
        // three hundred pixels away at the margin.
        //
        // A zero-sized rect pinned to the left edge fixes it for good: its own centre IS that point,
        // so children written at `x = 0` sit exactly on the button column's edge at every aspect,
        // and every x below reads as "how far in from the edge" rather than as a screen coordinate.
        // How far in from the right edge the title screen's column and its title both sit. One
        // number, so the two cannot drift apart - which is the whole reason `MenuLeftMargin` exists
        // for the other pages.
        private const float MenuRightMargin = 130f;

        // **A ROW BLOCK CENTRED ON THE SCREEN**, for the pause overlay's settings. Every settings
        // row is authored as "label at the column edge, control at a fixed indent" - offsets from an
        // origin on the LEFT of the block - so centring the block is a matter of putting that origin
        // half a block's width left of the middle, and nothing about a row has to change.
        //
        // `width` is the block's own width, which the caller knows and this cannot: it is the last
        // control's x plus half its own width, and that is a fact about the rows being built.
        private static Transform MakeCentreColumn(Transform parent, string name, float width)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            RectTransform rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = Vector2.zero;
            rect.anchoredPosition = new Vector2(-width / 2f, 0f);
            return go.transform;
        }

        // The width of that block on the pause overlay: the far button sits at
        // `SettingsControlX + 240` and is 150 wide, so its outer edge is 75 past its centre.
        private const float PauseSettingsWidth = SettingsControlX + 240f + 75f;

        private static Transform MakeLeftColumn(Transform parent, string name)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            RectTransform rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 0.5f);
            rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = Vector2.zero;
            // The +6 is MakeMenuButton's own nudge, repeated so the two edges are the same edge.
            rect.anchoredPosition = new Vector2(MenuLeftMargin + 6f, 0f);
            return go.transform;
        }

        // THE SHAPE OF ONE SETTINGS ROW, so the four of them cannot drift apart. A label on the
        // column edge and its control at a fixed indent - the numbers are here once rather than in
        // eight call sites.
        private const float SettingsLabelWidth = 300f;
        private const float SettingsControlX = 340f;

        // TAG A LABEL WITH WHICH STRING IT IS. The English stays authored in the `Text` itself, so a
        // scene built with this stripped is an English scene and nothing here can make a label blank -
        // `LocalizedText` only overwrites what is already correct.
        //
        // Deliberately a call AFTER the text is made rather than a parameter threaded through
        // `MakeRowLabel`, `MakeMenuLine`, `MakeMenuButton` and the rest: five builders would each need
        // an extra argument that almost every caller passes as null, and the ones that are NOT
        // localised (the facility's own signage - see `Loc`) would look like oversights instead of
        // decisions.
        private static Text Localize(Text text, string key)
        {
            if (text == null) return null;
            LocalizedText loc = text.gameObject.AddComponent<LocalizedText>();
            loc.target = text;
            loc.key = key;
            return text;
        }

        // A button's label lives on a child, and `MakeMenuButton` indents it with two spaces so the
        // words form one column down the left edge. That padding is layout, not content, so it stays
        // out of the string table and is re-applied here.
        private static Button Localize(Button button, string key, string prefix = "")
        {
            if (button == null) return null;
            Text text = button.GetComponentInChildren<Text>(true);
            if (text == null) return button;
            LocalizedText loc = text.gameObject.AddComponent<LocalizedText>();
            loc.target = text;
            loc.key = key;
            loc.prefix = prefix;
            return button;
        }

        // **`rightAligned` REBUILDS THE ROW AGAINST THE OTHER EDGE** (2026-09-03, by request, from a
        // reference screenshot in `docs/`). The title screen's column now hangs off the RIGHT margin
        // with the words set flush to it, which is the arrangement the reference uses and the reason
        // it works: the picture - the room, the drawing, the empty middle - gets the whole frame, and
        // the interface is a narrow strip of type at the edge of it rather than a block sitting on
        // top of the image.
        //
        // It is a flag rather than a second builder because everything else about a row is unchanged,
        // and two builders is how the pause menu's rows and the title screen's stop looking alike.
        private static Button MakeMenuButton(Transform parent, string name, string label,
                                             Vector2 anchoredPosition, Color? ink = null,
                                             string weight = null, bool rightAligned = false)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);

            // LEFT EDGE, like the title - see the note there. `anchoredPosition.x` arrives as 0 from
            // every call site and is ADDED to the margin, so the column lines up with the title
            // without every call having to know where the edge is.
            RectTransform rect = go.AddComponent<RectTransform>();
            float edge = rightAligned ? 1f : 0f;
            rect.anchorMin = new Vector2(edge, 0.5f);
            rect.anchorMax = new Vector2(edge, 0.5f);
            rect.pivot = new Vector2(edge, 0.5f);
            rect.sizeDelta = new Vector2(rightAligned ? 460f : 340f, 60f);
            rect.anchoredPosition = rightAligned
                ? new Vector2(-MenuRightMargin + anchoredPosition.x, anchoredPosition.y)
                : new Vector2(MenuLeftMargin + anchoredPosition.x + 6f, anchoredPosition.y);

            // White, with the dark look coming entirely from the ColorBlock below: a Button tints
            // its target graphic by multiplying, so a background that is already near-black has
            // nothing left to brighten with on hover.
            Image background = go.AddComponent<Image>();
            background.color = Color.white;

            GameObject textGO = new GameObject("Label");
            textGO.transform.SetParent(go.transform, false);
            Text text = textGO.AddComponent<Text>();
            // The resting face is LIGHT on a right-aligned row: `MenuRowHover` swaps it for the
            // weight asked for here when the pointer arrives, so `weight` is the LIVE face rather
            // than the only one. On a left-aligned row nothing swaps and it is simply the face.
            Font liveFace = weight != null ? UIFont(weight) : UIFont();
            Font restFace = rightAligned ? UIFont("Light") : liveFace;
            text.font = restFace;
            // Bigger on the right-hand column: it is the only thing on that half of the screen and
            // has nothing to share the eye with, where a left-hung row sits under a title.
            text.fontSize = rightAligned ? 34 : 28;
            // Left inside the plate too, so the words form one column down the edge rather than a
            // ragged one centred inside boxes of a single width.
            text.alignment = rightAligned ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft;
            text.color = ink ?? Color.red;
            text.text = "  " + label;
            text.raycastTarget = false;
            Stretch(text.GetComponent<RectTransform>());

            // **THE ROW'S OWN HOVER**, on top of the plate the `ColorBlock` below still fades in.
            // See `MenuRowHover` for what it does and why the step is as small as it is; everything
            // here is the pieces it moves.
            //
            // The caret sits in the two spaces the label is already indented by, so it arrives in
            // space that was always reserved for it rather than pushing the column about. The rule
            // is drawn under the words at the position they step TO, because a rule that started
            // under the resting text and then had the text slide off it would read as a mistake.
            // **~~A CARET IN THE MARGIN~~ GONE, 2026-09-03.** It was built that morning, from a
            // sketch that drew one, and the reference that arrived the same afternoon does not have
            // one: the selected row there is carried entirely by ink, weight and a rule. A caret is
            // a second marker doing the job those three already do, and on a right-aligned column it
            // has to sit on the far side of the words from the edge they are hung on, which is the
            // one place nothing else in this design is.
            //
            // The movement the caret was drawn to explain is kept - see `MenuRowHover.shift`.
            GameObject ruleGO = new GameObject("HoverRule");
            ruleGO.transform.SetParent(go.transform, false);
            Image rule = ruleGO.AddComponent<Image>();
            rule.color = new Color(text.color.r, text.color.g, text.color.b, 0f);
            rule.raycastTarget = false;
            RectTransform ruleRect = rule.GetComponent<RectTransform>();
            ruleRect.anchorMin = new Vector2(edge, 0.5f);
            ruleRect.anchorMax = new Vector2(edge, 0.5f);
            // Grows from the edge the words are hung on, so it opens under them rather than toward
            // them - a rule that grew the other way would arrive from the empty half of the screen.
            ruleRect.pivot = new Vector2(edge, 0.5f);
            ruleRect.sizeDelta = new Vector2(0f, 2f);
            // **THE RULE IS PLACED FROM THE SAME TWO NUMBERS THE LABEL IS, RATHER THAN BY EYE**
            // (2026-09-04, by request: "the underline does not line up"). It was 26 and 0, which
            // were close for one font size and wrong for every other, and wrong by the hover step on
            // both sides.
            //
            // Where the glyphs actually start is the two-space indent `text` is written with -
            // `0.6 * fontSize` per character is the same width estimate CLAUDE.md 3 asks every fixed
            // label to be checked against, and it is what sizes this rule's LENGTH too. Where they
            // end up is that plus `MenuRowHover.shift`, because the rule is only ever drawn while
            // the row is stepped. Both branches now say so in the same terms.
            const float hoverStep = 10f;
            float glyphIndent = 2f * 0.6f * text.fontSize;
            ruleRect.anchoredPosition = rightAligned
                ? new Vector2(hoverStep, -26f)
                : new Vector2(glyphIndent + hoverStep, -20f);

            MenuRowHover hover = go.AddComponent<MenuRowHover>();
            hover.label = text.GetComponent<RectTransform>();
            hover.underline = rule;
            hover.underlineRect = ruleRect;
            hover.ink = text;
            hover.restFont = restFace;
            hover.liveFont = liveFace;
            // **TAKEN OFF THE LABEL, NOT OFF THE `ink` ARGUMENT, AND THAT WAS A REAL BUG.**
            //
            // It read `ink.Value` and left the component's own defaults standing when the argument
            // was null - and the pause overlay's rows pass null, because their words are RED on a
            // near-black scrim rather than charcoal on a photograph. The defaults are charcoal, so
            // `MenuRowHover` painted every pause button dark grey on black the moment it woke up.
            // Play found it as "the buttons have no text" (2026-09-03), which is exactly what a
            // 0.11 grey at 45% on a 0.55 black scrim looks like.
            //
            // The label already carries the right colour whichever branch set it, so asking IT
            // cannot have this failure: there is no case where the row is drawn in a colour this
            // does not know about.
            Color rowInk = text.color;
            // Pale at rest and full strength live. On a left-aligned row the two are the same,
            // because those pages carry their state with the plate behind the words.
            hover.restColor = rightAligned
                ? new Color(rowInk.r, rowInk.g, rowInk.b, rowInk.a * 0.42f) : rowInk;
            hover.liveColor = rowInk;
            // Right-aligned rows step OUTWARD, toward the edge they hang on. Same distance, and the
            // same argument: small enough that the column still reads as a column.
            hover.shift = hoverStep;
            // Sized off the label, so CYCLE SELECT gets a longer rule than PLAY and the mark reads
            // as belonging to the word rather than as a fixed tick beside it. 0.6 x fontSize x
            // length is the width estimate CLAUDE.md 3 already asks every fixed label to be checked
            // against - the same sum, used here to fit something to the text instead of to fail it.
            hover.underlineWidth = 0.6f * text.fontSize * label.Length;

            Button button = go.AddComponent<Button>();
            button.targetGraphic = background;

            ColorBlock colors = button.colors;
            if (ink.HasValue)
            {
                // **NO PLATE AT ALL NOW, NOT EVEN ON HOVER** (2026-09-04, by request). It was a 9%
                // charcoal wash that read as a faint grey box behind the words - which was a mark
                // worth having when the row's only other signal was a ten-pixel step. The row now
                // changes INK, WEIGHT and grows a RULE under itself (`MenuRowHover`), so the box was
                // a fourth signal saying what three already say, and the one of the four that draws
                // a rectangle on a photograph. The button keeps its `Image` because a `Button` needs
                // a graphic to receive a click; it is simply never visible.
                colors.normalColor = new Color(0f, 0f, 0f, 0f);
                colors.highlightedColor = new Color(0f, 0f, 0f, 0f);
                // Red only on the press, which is the one moment it means something here.
                colors.pressedColor = new Color(0.80f, 0.10f, 0.10f, 0.20f);
                colors.selectedColor = colors.highlightedColor;
                colors.disabledColor = new Color(0f, 0f, 0f, 0f);
            }
            else
            {
                colors.normalColor = new Color(0f, 0f, 0f, 0.55f);
                colors.highlightedColor = new Color(0.34f, 0.04f, 0.04f, 0.8f);
                colors.pressedColor = new Color(0.6f, 0.08f, 0.08f, 0.9f);
                colors.selectedColor = colors.highlightedColor;
                colors.disabledColor = new Color(0f, 0f, 0f, 0.3f);
            }
            colors.fadeDuration = 0.12f;
            button.colors = colors;

            return button;
        }

        // Two black panels that meet in the middle. They're driven through their anchors at
        // runtime, so the sizes set here don't matter - only that they exist and are full-width.
        private static WakeUpSequence BuildEyelids(Transform canvasParent)
        {
            GameObject root = new GameObject("Eyelids");
            root.transform.SetParent(canvasParent, false);
            RectTransform rootRect = root.AddComponent<RectTransform>();
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;

            RectTransform top = MakeEyelid(root.transform, "EyelidTop");
            RectTransform bottom = MakeEyelid(root.transform, "EyelidBottom");

            WakeUpSequence wakeUp = root.AddComponent<WakeUpSequence>();
            wakeUp.topLid = top;
            wakeUp.bottomLid = bottom;
            return wakeUp;
        }

        private static RectTransform MakeEyelid(Transform parent, string name)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);

            Image image = go.AddComponent<Image>();
            image.color = Color.black;
            image.raycastTarget = false;

            RectTransform rect = go.GetComponent<RectTransform>();
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return rect;
        }

        // Returns the object it built, purely so `CaptureRig` can be handed the HUD by reference
        // rather than finding it by name at runtime. Same for the two builders below it.
        private static GameObject BuildCountdownTimer(Transform canvasParent)
        {
            GameObject go = new GameObject("CountdownTimer");
            go.transform.SetParent(canvasParent, false);

            RectTransform rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.sizeDelta = new Vector2(160f, 50f);
            rect.anchoredPosition = new Vector2(-20f, -20f);

            Text text = go.AddComponent<Text>();
            text.font = UIFont();
            text.fontSize = 32;
            text.alignment = TextAnchor.UpperRight;
            text.color = Color.red;

            CountdownTimer timer = go.AddComponent<CountdownTimer>();
            timer.label = text;
            return go;
        }

        // The end-cycle control, immediately left of the countdown. Built last so it draws over the
        // eyelids rather than under them.
        // THE GAS THAT ENDS A CYCLE. A full-screen wash and a valve, and nothing that warns.
        //
        // A screen wash rather than a particle system, and that is a considered choice rather than a
        // shortcut: gas the player is inside is not gas they can look at. Volumetric fog in the room
        // would be something happening over there, and the beat is that it is happening to them.
        // Sitting on the HUD canvas also means it is drawn OVER the eyelids' parent, so the wash and
        // the blink stack correctly.
        //
        // 2D audio for the same reason. The player has no idea where the vents are and is not meant
        // to - a positioned hiss invites them to turn and look for it.
        private static void BuildSleepingGas(Transform canvasParent)
        {
            GameObject go = new GameObject("SleepingGas");
            go.transform.SetParent(canvasParent, false);

            Image haze = go.AddComponent<Image>();
            // Authored fully transparent. SleepingGas.Administer writes the alpha, and Clear puts it
            // back - a haze left up would open the next cycle behind a white sheet.
            haze.color = new Color(0.92f, 0.94f, 0.96f, 0f);
            // The wash must never eat a click. Nothing under it is interactive at a boundary, but a
            // full-screen Image defaults to raycast target and this is exactly the kind of invisible
            // blocker that is impossible to find later.
            haze.raycastTarget = false;

            RectTransform rect = haze.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            SleepingGas gas = go.AddComponent<SleepingGas>();
            gas.haze = haze;
            gas.audioSource = MakeSource(go.transform, "GasAudio", spatialBlend: 0f, volume: 0.7f);
            gas.hissClip = LoadClip(SfxDir, "sfx_gas_hiss");
        }

        private static GameObject BuildEndCycleControl(Transform canvasParent)
        {
            GameObject go = new GameObject("EndCycleControl");
            go.transform.SetParent(canvasParent, false);

            RectTransform rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            // Wide enough for the label to fit on one line, which it did not: "HOLD [N] - END
            // CYCLE" is 20 monospace cells, i.e. ~192px at fontSize 16, inside a box that was 168
            // wide - so uGUI wrapped it onto two cramped lines and it read as decoration next to
            // the countdown rather than as a control. That is very likely why testers asked for a
            // skip button that has existed since the first build.
            rect.sizeDelta = new Vector2(224f, 40f);
            // The countdown is 160 wide, inset 20 from the corner, so it ends 180 in.
            rect.anchoredPosition = new Vector2(-192f, -20f);

            // **STARTS INVISIBLE.** `EndCycleControl` shows it only while the key is down, and a
            // CanvasGroup is born at alpha 1 - so without this the box is on screen for the one
            // frame before the first Update, and is on screen in the editor's own scene view too.
            CanvasGroup group = go.AddComponent<CanvasGroup>();
            group.alpha = 0f;

            Image background = go.AddComponent<Image>();
            background.color = new Color(0f, 0f, 0f, 0.55f);

            // The hold gauge, drawn between the background and the label so it fills behind the
            // text rather than over it.
            GameObject fillGO = new GameObject("Fill");
            fillGO.transform.SetParent(go.transform, false);
            Image fill = fillGO.AddComponent<Image>();
            // A filled Image needs a sprite to have anything to fill; the built-in UI sprite is the
            // one uGUI itself uses for buttons.
            fill.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillAmount = 0f;
            fill.color = new Color(0.75f, 0.1f, 0.1f, 0.85f);
            fill.raycastTarget = false;
            RectTransform fillRect = fill.GetComponent<RectTransform>();
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;

            GameObject labelGO = new GameObject("Label");
            labelGO.transform.SetParent(go.transform, false);
            Text label = labelGO.AddComponent<Text>();
            label.font = UIFont();
            label.fontSize = 16;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = Color.red;
            // "HOLD" states the interaction, and the key is advertised because it is the one that
            // works with the cursor locked.
            label.text = "HOLD [N] — END CYCLE";
            Localize(label, "hud.endCycle");
            // Belt and braces with the wider rect above: a label that silently rewraps is how this
            // became unreadable in the first place, and at some resolutions the scaler will shave
            // a pixel off.
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.raycastTarget = false;
            RectTransform labelRect = label.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            EndCycleControl control = go.AddComponent<EndCycleControl>();
            control.fill = fill;
            control.group = group;
            return go;
        }
    }
}
