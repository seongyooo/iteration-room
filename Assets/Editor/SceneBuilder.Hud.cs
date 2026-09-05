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
    // WHAT IS DRAWN OVER THE ROOM RATHER THAN IN IT: fonts and colours, the carried-item readout,
    // touch controls, control hints, the PA subtitle, the capture rig, the pause menu, the ending
    // screen, the controls wall, and the final room's console.
    //
    // Split out of `SceneBuilder.cs` (2026-09-02), which had reached 25,000 lines. One class,
    // many files - see the note over the `partial` keyword there. Everything private in any part
    // is reachable from every other part, so nothing about the build changed when this moved.
    public static partial class SceneBuilder
    {

        // WEIGHTS, and the whole family is on disk - sixteen faces, of which this project used one.
        // A hierarchy built from SIZE alone is what four sizes with no ratio between them looks like;
        // one built from weight needs only two sizes. ExtraLight at 86 is a different instrument from
        // Regular at 86, and it is the one a title wants.
        // THE HANGUL FACE, for the one label that must be Korean before any language is chosen.
        // Everything else swaps at runtime through `LocalizedText`, which resolves the same asset -
        // this exists so the two cannot point at different files.
        private static Font KoreanUIFont() =>
            AssetDatabase.LoadAssetAtPath<Font>("Assets/Resources/Fonts/D2Coding.ttf");

        private static Font UIFont(string weight)
        {
            Font font = AssetDatabase.LoadAssetAtPath<Font>(
                $"{FontsDir}/JetBrains_Mono/static/JetBrainsMono-{weight}.ttf");
            return font != null ? font : UIFont();
        }

        private static Font UIFont()
        {
            // The STATIC Regular, not the variable font that ships alongside it: uGUI's legacy
            // Font has no axis control, so a variable face is a coin toss on which weight renders.
            Font font = AssetDatabase.LoadAssetAtPath<Font>($"{FontsDir}/JetBrains_Mono/static/JetBrainsMono-Regular.ttf");
            // Falls back rather than throwing: a missing font should leave the UI ugly and legible,
            // not leave the scene unbuildable.
            return font != null ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        // Top-left readout of what is in the player's pockets. Carrying is state the loop rewinds
        // and the key is invisible once pocketed, so without this "do I still have the key" is only
        // answerable by walking to the door and trying it.
        private static GameObject BuildCarriedItems(Transform canvas, PlayerHand hand)
        {
            // ONE SLOT. It was a row of eight, sized for a pocket that could hold every symbol cube
            // at once; the hand holds exactly one object now and the other seven were a readout of
            // something that cannot happen.
            const float slotSize = 58f;

            GameObject go = new GameObject("CarriedItems");
            go.transform.SetParent(canvas, false);

            RectTransform rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(slotSize, slotSize);
            rect.anchoredPosition = new Vector2(36f, -30f);

            GameObject slotGO = new GameObject("Slot");
            slotGO.transform.SetParent(go.transform, false);

            Image image = slotGO.AddComponent<Image>();
            // Red like the rest of the HUD: the walls are near-white, so a white glyph disappears
            // into them.
            image.color = Color.red;
            image.raycastTarget = false;
            image.preserveAspect = true;
            image.enabled = false;

            RectTransform slotRect = image.GetComponent<RectTransform>();
            slotRect.anchorMin = new Vector2(0f, 1f);
            slotRect.anchorMax = new Vector2(0f, 1f);
            slotRect.pivot = new Vector2(0f, 1f);
            slotRect.sizeDelta = new Vector2(slotSize, slotSize);
            slotRect.anchoredPosition = Vector2.zero;

            // The put-down prompt, under the icon rather than out in the middle of the screen: it
            // explains this readout, so it belongs on it. A child of the row, so moving the row
            // moves the label with it.
            GameObject hintGO = new GameObject("DropHint");
            hintGO.transform.SetParent(go.transform, false);

            CanvasGroup hintGroup = hintGO.AddComponent<CanvasGroup>();
            hintGroup.alpha = 0f;
            hintGroup.blocksRaycasts = false;
            hintGroup.interactable = false;

            Text hint = hintGO.AddComponent<Text>();
            hint.font = UIFont();
            hint.fontSize = 16;
            hint.alignment = TextAnchor.UpperLeft;
            hint.color = Color.red;
            // Bracketed key then the verb, the same shape as the end-cycle control's label.
            hint.text = "[E] — PUT DOWN";
            Localize(hint, "hud.putDown");
            // 0.6 * 16 * 16 is about 154px against a 300px rect, but overflow is set anyway - a
            // silently rewrapping HUD label has bitten this project twice.
            hint.horizontalOverflow = HorizontalWrapMode.Overflow;
            hint.verticalOverflow = VerticalWrapMode.Overflow;
            hint.raycastTarget = false;

            RectTransform hintRect = hint.GetComponent<RectTransform>();
            hintRect.anchorMin = new Vector2(0f, 1f);
            hintRect.anchorMax = new Vector2(0f, 1f);
            hintRect.pivot = new Vector2(0f, 1f);
            hintRect.sizeDelta = new Vector2(300f, 22f);
            // anchoredPosition, never localPosition: the latter is stale on a RectTransform, and
            // it must be written AFTER AddComponent<Text> replaced the Transform.
            hintRect.anchoredPosition = new Vector2(0f, -(slotSize + 10f));

            CarriedItemsDisplay display = go.AddComponent<CarriedItemsDisplay>();
            display.hand = hand;
            display.slot = image;
            display.dropHint = hintGroup;
            return go;
        }

        // The two control prompts. A grey disc over whatever the player has walked up to, with an
        // E on it, and a second disc carrying a mouse glyph that appears on the pin the moment it
        // is in hand. Each is shown once and then retired for good - see ControlHintDisplay.
        //
        // Built last of everything on the canvas so it draws over the eyelids and the HUD, and
        // parented to a full-screen rect so a screen point converts straight to an anchoredPosition.
        // THE SCREEN AS A CONTROLLER, for the WebGL build opened on a phone. See TouchControls for
        // why this is raw touch handling rather than uGUI buttons, and why the visuals here are
        // non-interactive images: every one of them is placed AT RUNTIME from the same numbers the
        // hit test uses, so a mark and the press it describes can never end up in two places.
        //
        // **RAYCAST TARGETS OFF ON EVERY PIECE.** TouchControls asks the EventSystem whether
        // something that wants presses is under a finger, so that `EndCycleControl` keeps its hold -
        // and its own artwork answering that question would block the look drag across a third of
        // the screen.
        //
        // Built into the same canvas as the HUD, and it costs a desktop player nothing: `Active` is
        // false off a touch device, so the group sits at zero alpha and samples nothing.
        private static TouchControls BuildTouchControls(Transform canvas)
        {
            GameObject root = new GameObject("TouchControls");
            root.transform.SetParent(canvas, false);
            Stretch(root.AddComponent<RectTransform>());

            CanvasGroup group = root.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            // Never takes a press itself - this whole layer is read by polling, not by the
            // EventSystem, and blocking raycasts here would swallow the pause menu underneath it.
            group.blocksRaycasts = false;
            group.interactable = false;

            Sprite disc = HintDiscSprite();

            TouchControls touch = root.AddComponent<TouchControls>();
            touch.group = group;

            // THE STICK, drawn only while a thumb is down - see TouchControls.Draw. It appears where
            // the finger lands, so there is nothing to place here beyond its look.
            touch.stickBase = MakeTouchDisc(root.transform, "StickBase", disc,
                                            new Color(1f, 1f, 1f, 0.13f));
            touch.stickKnob = MakeTouchDisc(root.transform, "StickKnob", disc,
                                            new Color(1f, 1f, 1f, 0.30f));
            touch.stickBase.gameObject.SetActive(false);
            touch.stickKnob.gameObject.SetActive(false);

            // WHERE THE THUMBS GO. The left box is the stick's, the rest of the screen turns the
            // view, and the buttons sit up the right-hand edge where a right thumb reaches without
            // covering the middle of the room. E is the biggest and the lowest because it is the
            // verb this game is almost entirely made of.
            touch.stickZone = new Rect(0f, 0f, 0.45f, 0.72f);
            touch.stickRadius = 0.115f;

            // E AND JUMP SWAPPED 2026-08-15, after play on a phone: E was in the lowest position
            // where the thumb rests, jump above it. That is backwards for how often each is used -
            // jump is pressed a handful of times in a run and E is pressed constantly, but the
            // resting spot is also where the thumb sits BETWEEN presses, so it was catching the
            // wrong one. E takes the upper spot, which the thumb travels to deliberately.
            //
            // Sizes did NOT swap. E stays the biggest target because it is still the verb this game
            // is almost entirely made of.
            SetTouchButton(touch.interact, root.transform, disc, "E",
                           new Vector2(0.885f, 0.420f), 0.098f);
            // A MOUSE RATHER THAN THE WORD "USE", and the icon already exists: this is the same
            // left-button glyph `ControlHintDisplay` puts on whatever a click would act on. The word
            // was a placeholder that named the input; the glyph names the same thing the rest of the
            // game already uses for it, so a player who has seen the prompt disc knows this button.
            SetTouchButton(touch.use, root.transform, disc, "USE",
                           new Vector2(0.700f, 0.135f), 0.072f, MouseLeftIcon());
            SetTouchButton(touch.jump, root.transform, disc, "JUMP",
                           new Vector2(0.885f, 0.175f), 0.072f);
            // **IMMEDIATELY LEFT OF E** (2026-09-04, by request: that space is empty).
            //
            // The x is worked out rather than eyeballed, because a RADIUS IS A FRACTION OF SCREEN
            // HEIGHT while a centre is a fraction of each axis - so a button's width in x is
            // `radius / aspect` and the gap between two of them SHRINKS as the screen gets squarer.
            // At 16:9 E spans x 0.830..0.940; at 4:3 it spans 0.812..0.959. Sitting this at 0.755
            // with a radius of 0.062 leaves 0.040 of clear width at 16:9 and still 0.010 at 4:3,
            // where 0.775 - which looks right on a phone - would have overlapped E on a tablet.
            // `TouchControls` hit-tests buttons in order and gives the finger to the first one hit,
            // so two that overlap would make which verb fires depend on the array order.
            //
            // Smaller than E, and it is a hold with a charging gauge: E is pressed constantly and
            // this throws the iteration away, so it must not be reachable by the same brush.
            SetTouchButton(touch.endIteration, root.transform, disc, "END",
                           new Vector2(0.755f, 0.420f), 0.062f);
            // Top corner, small, and away from everything else: it is pressed between runs rather
            // than during one.
            SetTouchButton(touch.pause, root.transform, disc, "II",
                           new Vector2(0.955f, 0.930f), 0.042f);

            return touch;
        }

        // THE TOUCH BUTTONS ARE ONE TRANSPARENT GREY.
        //
        // They shipped white at 17% alpha, which is the HUD's own language for a prompt and
        // invisible against the one thing this building is made of. The first fix gave each button
        // its own hue - red, amber, blue - and play called it: three saturated circles are the
        // loudest thing on a screen showing a white room, and they read as a different game's UI
        // bolted on.
        //
        // So the answer to "cannot be seen" turned out to be VALUE, not colour. A dark grey at 62%
        // composites to about 0.45 over a white wall, which is a clear silhouette and carries the
        // white label at readable contrast - and over an unlit wall (room2-1 starts dark) it goes
        // darker still while the label stays white. One colour, both ends.
        private static readonly Color TouchButtonColour = new Color(0.12f, 0.12f, 0.14f, 0.62f);
        private static readonly Color TouchRimColour = new Color(0.04f, 0.04f, 0.06f, 0.55f);

        // One round control: the disc, its label, and the numbers TouchControls hit-tests against.
        // `icon` replaces the text label when the button has a glyph of its own - see the mouse on
        // USE. Everything else carries its word, because there is no drawing of "jump" that is
        // clearer than the word.
        private static void SetTouchButton(TouchControls.TouchButton button, Transform parent,
                                           Sprite disc, string label, Vector2 center, float radius,
                                           Sprite icon = null)
        {
            button.label = label;
            button.center = center;
            button.radius = radius;

            // A CONTAINER rather than the disc itself, because a button is three pieces now: the
            // rim, the fill and the label. `TouchControls.LayOut` sizes this one rect and the rest
            // stretch to it, so there is still exactly one thing that knows how big a button is.
            GameObject go = new GameObject("Touch_" + label);
            go.transform.SetParent(parent, false);
            RectTransform rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            button.visual = rect;

            // Built in draw order: the rim first, so it sits behind. It is anchored slightly OUTSIDE
            // its parent rather than given a pixel offset, so the ring keeps its proportion at any
            // button size and on any screen.
            MakeTouchDisc(rect, "Rim", disc, TouchRimColour, -0.07f);
            MakeTouchDisc(rect, "Fill", disc, TouchButtonColour, 0f);

            if (icon != null)
            {
                GameObject iconGO = new GameObject("Icon");
                iconGO.transform.SetParent(rect, false);
                Image glyph = iconGO.AddComponent<Image>();
                glyph.sprite = icon;
                glyph.color = Color.white;
                glyph.raycastTarget = false;
                // Inset inside the disc rather than filling it, so the button still reads as a
                // button with something on it. Anchors, so the inset is proportional at any size.
                RectTransform ir = iconGO.GetComponent<RectTransform>();
                ir.anchorMin = new Vector2(0.24f, 0.24f);
                ir.anchorMax = new Vector2(0.76f, 0.76f);
                ir.offsetMin = Vector2.zero;
                ir.offsetMax = Vector2.zero;
                return;
            }

            GameObject textGO = new GameObject("Label");
            textGO.transform.SetParent(rect, false);
            Text text = textGO.AddComponent<Text>();
            text.font = UIFont();
            // Against the disc's own size rather than a point size, so it stays centred and legible
            // whatever the screen does - the disc is sized from screen height at runtime.
            text.fontSize = 30;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 8;
            text.resizeTextMaxSize = 44;
            text.alignment = TextAnchor.MiddleCenter;
            // Fully opaque, unlike the HUD's own labels: this one is read at a glance with a thumb
            // beside it, over a fill that is itself over whatever the room happens to be.
            text.color = Color.white;
            text.text = label;
            text.raycastTarget = false;
            Stretch(text.GetComponent<RectTransform>());
        }

        // `overhang` is how far outside the parent this reaches, as a fraction of the parent - 0
        // fills it exactly, -0.07 makes a ring 7% proud all round. Zero for a free-standing disc
        // (the stick), which is positioned by TouchControls rather than stretched to anything.
        private static RectTransform MakeTouchDisc(Transform parent, string name, Sprite disc,
                                                   Color colour, float overhang = float.NaN)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);

            Image image = go.AddComponent<Image>();
            image.sprite = disc;
            image.color = colour;
            // See BuildTouchControls: this layer must be invisible to the EventSystem.
            image.raycastTarget = false;

            RectTransform rect = go.GetComponent<RectTransform>();

            if (float.IsNaN(overhang))
            {
                // FREE-STANDING: centre-anchored, because TouchControls positions and sizes it
                // directly as an offset from the middle of the canvas.
                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);
            }
            else
            {
                // STRETCHED TO ITS PARENT, and beyond it by `overhang`. Anchors rather than offsets
                // so the proportion survives a button of any size.
                rect.anchorMin = new Vector2(overhang, overhang);
                rect.anchorMax = new Vector2(1f - overhang, 1f - overhang);
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
            }

            rect.pivot = new Vector2(0.5f, 0.5f);
            return rect;
        }

        private static ControlHintDisplay BuildControlHints(Transform canvas, Camera playerCamera, BalloonTool swingTool,
                                              MonoBehaviour[] interactTargets)
        {
            Sprite disc = HintDiscSprite();

            GameObject root = new GameObject("ControlHints");
            root.transform.SetParent(canvas, false);
            RectTransform area = root.AddComponent<RectTransform>();
            area.anchorMin = Vector2.zero;
            area.anchorMax = Vector2.one;
            area.offsetMin = Vector2.zero;
            area.offsetMax = Vector2.zero;

            (CanvasGroup interactGroup, RectTransform interactRect) =
                MakeHintBadge(area, "InteractHint", disc);
            // The key itself, in the HUD's monospace face - the same one the iteration number and
            // the clock use, because this is the facility labelling its own equipment.
            GameObject glyphGO = new GameObject("Glyph");
            glyphGO.transform.SetParent(interactRect, false);
            Text glyph = glyphGO.AddComponent<Text>();
            glyph.font = UIFont();
            glyph.fontSize = 44;
            glyph.alignment = TextAnchor.MiddleCenter;
            glyph.color = Color.white;
            glyph.raycastTarget = false;
            glyph.text = "E";
            RectTransform glyphRect = glyph.GetComponent<RectTransform>();
            glyphRect.anchorMin = Vector2.zero;
            glyphRect.anchorMax = Vector2.one;
            glyphRect.offsetMin = Vector2.zero;
            glyphRect.offsetMax = Vector2.zero;

            (CanvasGroup swingGroup, RectTransform swingRect) = MakeHintBadge(area, "SwingHint", disc);
            GameObject mouseGO = new GameObject("Glyph");
            mouseGO.transform.SetParent(swingRect, false);
            Image mouse = mouseGO.AddComponent<Image>();
            mouse.sprite = MouseLeftIcon();
            mouse.color = Color.white;
            mouse.raycastTarget = false;
            mouse.preserveAspect = true;
            RectTransform mouseRect = mouse.GetComponent<RectTransform>();
            mouseRect.anchorMin = new Vector2(0.5f, 0.5f);
            mouseRect.anchorMax = new Vector2(0.5f, 0.5f);
            mouseRect.sizeDelta = new Vector2(48f, 48f);
            mouseRect.anchoredPosition = Vector2.zero;

            ControlHintDisplay display = root.AddComponent<ControlHintDisplay>();
            display.playerCamera = playerCamera;
            display.swingTool = swingTool;
            display.interactTargets = interactTargets;
            display.area = area;
            display.interactGroup = interactGroup;
            display.interactRect = interactRect;
            display.swingGroup = swingGroup;
            display.swingRect = swingRect;
            return display;
        }

        private static (CanvasGroup, RectTransform) MakeHintBadge(Transform parent, string name, Sprite disc)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);

            RectTransform rect = go.AddComponent<RectTransform>();
            // Centred anchors: ControlHintDisplay writes a screen position straight into
            // anchoredPosition, which only lines up if the anchor is the middle of the parent.
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(78f, 78f);

            CanvasGroup group = go.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            // Never eats input: the end-cycle control is a real uGUI button, and a prompt that
            // swallowed clicks would be a bug that only shows up when both are on screen.
            group.blocksRaycasts = false;
            group.interactable = false;

            Image background = go.AddComponent<Image>();
            background.sprite = disc;
            // Grey and translucent - present enough to read against a white wall, quiet enough not
            // to become the thing you look at.
            background.color = new Color(0.25f, 0.25f, 0.27f, 0.78f);
            background.raycastTarget = false;

            return (group, rect);
        }

        // THE PA'S WORDS, ALONG THE BOTTOM OF THE SCREEN.
        //
        // Bottom centre because everything else on this HUD is at the TOP - the clock and the end-cycle
        // prompt top right, the carried item top left, the iteration card dead centre. The bottom
        // eighth of the screen is the only strip a caption can own without ever colliding with one of
        // them, and it is where a subtitle belongs anyway.
        //
        // **WHITE ON A DARK PLATE, NOT RED.** The iteration card is red because it is the facility
        // announcing itself onto a near-white wall; this is a subtitle, and a subtitle that competes
        // with the thing it is subtitling is a bad one. The plate is what makes it legible over a lit
        // floor - white text alone disappears into this building, which is the same reason the card
        // above is not white either.
        private static PaSubtitle BuildPaSubtitle(Transform canvas)
        {
            GameObject root = new GameObject("PaSubtitle");
            root.transform.SetParent(canvas, false);
            CanvasGroup group = root.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            // It is never clicked and never blocks the crosshair.
            group.blocksRaycasts = false;
            group.interactable = false;

            // ADDED, not fetched. `CanvasGroup` is not a `Graphic`, so unlike `Image` and `Text` it
            // does NOT replace the plain `Transform` on its way in - the object still has one, and
            // asking it for a `RectTransform` throws. `IterationLabelGroup` above does the same thing
            // for the same reason.
            RectTransform rect = root.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.sizeDelta = new Vector2(1180f, 110f);
            rect.anchoredPosition = new Vector2(0f, 90f);

            // **~~THE PLATE~~ THERE IS NO PLATE ANY MORE** (2026-09-03, by request: *"can the
            // subtitles be prettier, without hurting the game?"*).
            //
            // It was a 1180x110 rectangle at 62% black, fixed size, and it was the least integrated
            // thing on the screen: this game is a WHITE building, and a hard black bar across the
            // bottom of it is a piece of media-player furniture sitting in the shot. Worse, being
            // fixed size it drew the same full-width bar for "Iteration nine." as for the longest
            // line in the game.
            //
            // **THE QUESTION IT ANSWERED WAS REAL, THOUGH: WHITE TEXT ON A WHITE WALL IS INVISIBLE.**
            // What a plate actually buys is contrast, and a plate is only one way to buy it - the
            // wrong way here, because it is contrast for a RECTANGLE when what needs it is the
            // GLYPHS. So the contrast moved onto the letters themselves, which is what film
            // subtitling does and for exactly this reason: the same treatment has to survive a white
            // room, a dark shaft and an open sky, and only a per-glyph one does.
            //
            // `Outline` emits four offset copies of the mesh (+-x, +-y) and `Shadow` one; together
            // they wrap each letter in a dark rim and drop it a little off the wall behind it. Both
            // honour `useGraphicAlpha`, so they fade with the `CanvasGroup` rather than hanging
            // around after the text has gone.
            //
            // Cost: it quadruples this one label's vertex count. It is one line of text on a HUD
            // that draws nothing else - see `ShadowBudget` for where this project actually spends.

            GameObject textGO = new GameObject("Line");
            textGO.transform.SetParent(root.transform, false);
            Text text = textGO.AddComponent<Text>();
            // **THE KOREAN FACE, ALWAYS.** This is the one label in the game that is guaranteed to
            // carry Hangul, and the default UI font draws it as empty boxes. `KoreanUIFont` falls back
            // to the ordinary face when it is not there, so an English build is unchanged.
            Font korean = KoreanUIFont();
            text.font = korean != null ? korean : UIFont();
            // 28 -> 30. It grew when the plate went: without a box around it the line has to hold
            // the eye on its own, and it is no longer competing with a rectangle for attention.
            text.fontSize = 30;
            text.alignment = TextAnchor.MiddleCenter;
            // **NOT PURE WHITE.** A 255 line against a room whose walls measure about 235 is a
            // brightness the eye reads as a light source rather than as type; slightly off white
            // sits ON the picture instead of in front of it, and the rim below is what makes it
            // legible rather than the raw value.
            text.color = new Color(0.96f, 0.96f, 0.97f, 1f);
            // WRAPS, unlike every other label in this HUD. The longest line - "all cycles have been
            // destroyed, you will pay the price for destroying them" - is genuinely two lines wide at
            // this size, and a caption is the one place where a second line is correct rather than a
            // bug. Vertical overflow so a third line is never clipped away silently.
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            RectTransform textRect = text.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(24f, 8f);
            textRect.offsetMax = new Vector2(-24f, -8f);

            // THE RIM, AND THE DROP. Order matters only in that both are applied to the same mesh;
            // the rim is tight and nearly opaque so it reads as an edge rather than as a glow, and
            // the drop is soft and offset downward so the line sits ON the room rather than in it.
            //
            // 1.6 rather than a round 2: at 30pt a two-pixel rim starts to close the counters of
            // 'e' and 'a' and the Hangul syllables suffer worse, because their strokes are packed
            // into the same em. Checked against the longest line the PA has.
            Outline rim = textGO.AddComponent<Outline>();
            rim.effectColor = new Color(0.04f, 0.04f, 0.06f, 0.92f);
            rim.effectDistance = new Vector2(1.6f, 1.6f);
            rim.useGraphicAlpha = true;

            Shadow drop = textGO.AddComponent<Shadow>();
            drop.effectColor = new Color(0f, 0f, 0f, 0.45f);
            drop.effectDistance = new Vector2(0f, -3f);
            drop.useGraphicAlpha = true;

            PaSubtitle subtitle = root.AddComponent<PaSubtitle>();
            subtitle.group = group;
            subtitle.label = text;
            return subtitle;
        }

        private static (IterationLabel label, WakeUpSequence wakeUp, Transform canvas, CanvasGroup loading, CaptureRig capture) BuildUI(PlayerHand hand)
        {
            GameObject canvasGO = new GameObject("Canvas");
            Canvas canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Scale with the screen rather than the default constant pixel size, which pins the
            // text to a fixed point size and leaves it tiny at 4K and oversized in a small window.
            CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            // Split the difference between matching width and height, so neither a wide nor a tall
            // window blows the UI up.
            scaler.matchWidthOrHeight = 0.5f;

            canvasGO.AddComponent<GraphicRaycaster>();

            // Built first so it sits at the back of the canvas: the iteration label and the timer
            // then draw ON TOP of the closed eyelids instead of being blacked out by them.
            WakeUpSequence wakeUp = BuildEyelids(canvasGO.transform);

            // THE LOADING SCREEN: the title screen's own photograph, drifting, instead of black.
            //
            // The cycle scenes load asynchronously and nothing can be shown until they arrive, so the
            // window was filled by shutting the eyelids - correct, and a black rectangle. Filling it
            // with the picture the menu already uses costs one Image and makes the wait look like part
            // of the game rather than like a hitch.
            //
            // BUILT LAST so it draws over the eyelids, which are shut underneath it the whole time.
            // Faster than the menu's drift by design: this is on screen for a few seconds, not for as
            // long as somebody leaves the title up, so the movement has to be visible immediately.
            GameObject loadGO = new GameObject("LoadingBackdrop");
            loadGO.transform.SetParent(canvasGO.transform, false);
            CanvasGroup loadGroup = loadGO.AddComponent<CanvasGroup>();
            loadGroup.blocksRaycasts = false;
            loadGroup.interactable = false;
            Stretch(loadGO.AddComponent<RectTransform>());

            GameObject loadImageGO = new GameObject("Image");
            loadImageGO.transform.SetParent(loadGO.transform, false);
            Image loadImage = loadImageGO.AddComponent<Image>();
            Sprite loadShot = AssetDatabase.LoadAssetAtPath<Sprite>(MenuBackgroundPath);
            loadImage.sprite = loadShot;
            // The same guard the menu's scrim needed: an Image with no sprite is a solid rectangle,
            // and a white one here would be a white flash on every start.
            loadImage.color = loadShot != null ? Color.white : new Color(0.05f, 0.05f, 0.06f, 1f);
            loadImage.raycastTarget = false;
            Stretch(loadImage.GetComponent<RectTransform>());
            MenuBackdrop loadDrift = loadImageGO.AddComponent<MenuBackdrop>();
            loadDrift.period = 13f;
            loadDrift.verticalPeriod = 19f;
            loadDrift.overscan = 0.10f;

            GameObject groupGO = new GameObject("IterationLabelGroup");
            groupGO.transform.SetParent(canvasGO.transform, false);
            CanvasGroup group = groupGO.AddComponent<CanvasGroup>();
            group.alpha = 0f;

            RectTransform rect = groupGO.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            // Dead centre. It used to sit 150px above, which read as a subtitle floating over the
            // room; the label is the iteration announcing itself, so it belongs on the middle of
            // the screen with nothing else on it.
            rect.sizeDelta = new Vector2(1200f, 140f);
            rect.anchoredPosition = Vector2.zero;

            GameObject textGO = new GameObject("Label");
            textGO.transform.SetParent(groupGO.transform, false);
            Text text = textGO.AddComponent<Text>();
            text.font = UIFont();
            // Bigger than it was, because the letters are now spaced out (see IterationLabel) and
            // tracked-out text at 42 reads as small print rather than as a title card.
            text.fontSize = 54;
            text.alignment = TextAnchor.MiddleCenter;
            // Never wrap. The spaced-out string is full of spaces, so a rect a shade too narrow
            // would break the label across two lines mid-word - "ITERATIO / N 12".
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            // Red, not white - the room walls are near-white, so white text is invisible.
            text.color = Color.red;
            RectTransform textRect = text.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            // CYCLE N, above the iteration and inside the SAME CanvasGroup so the two fade as one
            // card rather than as two announcements.
            //
            // Smaller than the iteration deliberately: the cycle is the frame and the iteration is
            // the subject. Anchored to the group's top edge and pushed clear of it, so the stack
            // stays centred on the screen as a whole.
            GameObject cycleGO = new GameObject("CycleLabel");
            cycleGO.transform.SetParent(groupGO.transform, false);
            Text cycleText = cycleGO.AddComponent<Text>();
            cycleText.font = UIFont();
            cycleText.fontSize = 34;
            cycleText.alignment = TextAnchor.MiddleCenter;
            cycleText.horizontalOverflow = HorizontalWrapMode.Overflow;
            cycleText.verticalOverflow = VerticalWrapMode.Overflow;
            cycleText.color = Color.red;
            RectTransform cycleRect = cycleText.GetComponent<RectTransform>();
            cycleRect.anchorMin = new Vector2(0f, 1f);
            cycleRect.anchorMax = new Vector2(1f, 1f);
            cycleRect.pivot = new Vector2(0.5f, 0.5f);
            cycleRect.sizeDelta = new Vector2(0f, 60f);
            cycleRect.anchoredPosition = new Vector2(0f, 42f);
            // Off until there has been more than one bed - IterationLabel.Show enables it. Cycle 1
            // says nothing, the way iteration 1 gets no reset announcement.
            cycleText.enabled = false;

            IterationLabel label = groupGO.AddComponent<IterationLabel>();
            label.canvasGroup = group;
            label.label = text;
            label.cycleLabel = cycleText;

            BuildSleepingGas(canvasGO.transform);
            // Wired to the PA in `BuildAudio`, which runs later - `narration` does not exist yet.
            BuildPaSubtitle(canvasGO.transform);
            GameObject timerGO = BuildCountdownTimer(canvasGO.transform);
            GameObject endCycleGO = BuildEndCycleControl(canvasGO.transform);
            GameObject carriedGO = BuildCarriedItems(canvasGO.transform, hand);

            // THE CAPTURE RIG, holding the three readouts above by reference. The prompts, the touch
            // layer and the two cameras are added by `WireCaptureRig` in `Build`, because none of
            // them exists yet at this point.
            CaptureRig capture = BuildCaptureRig(timerGO, endCycleGO, carriedGO, groupGO);

            // uGUI buttons do nothing without one of these in the scene, and NewScene's default
            // objects are only a camera and a light.
            GameObject eventSystem = new GameObject("EventSystem");
            eventSystem.AddComponent<UnityEngine.EventSystems.EventSystem>();
            eventSystem.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();

            return (label, wakeUp, canvasGO.transform, loadGroup, capture);
        }

        // THE RIG THAT MAKES A RECORDING A TRAILER: HUD off, camera off the player's head. See
        // `CaptureRig` for why it is not gated on `AcceptsInput` and why its two keys are not in
        // `InputBindings`.
        //
        // ITS OWN ROOT, NOT A CHILD OF THE CANVAS. It hides canvas children by deactivating them, and
        // an object that can deactivate its own parent's siblings is confusing enough without also
        // sitting among them. It is also not per-cycle: the HUD, the player and this all live in
        // `IterationRoom`, so the rig survives a cycle swap with its references intact.
        //
        // The tuned numbers live here rather than on the component, per CLAUDE.md SS2 - a capture rig
        // is still a mechanism, and how fast its camera flies is still a value.
        private static CaptureRig BuildCaptureRig(GameObject timer, GameObject endCycle,
                                                  GameObject carried, GameObject card)
        {
            GameObject go = new GameObject("CaptureRig");
            CaptureRig rig = go.AddComponent<CaptureRig>();

            // Everything that is a READOUT. The eyelids, the gas and the pause menu are deliberately
            // absent: the first two are the game rather than an overlay on it, and a hidden pause
            // menu is a game that looks broken when somebody presses Escape mid-take.
            rig.hud = new[] { timer, endCycle, carried };
            // The ITERATION / CYCLE card, on its own step - it is the one overlay that is also the
            // best single frame in the game.
            rig.cards = new[] { card };

            // A THIRD OF WALKING PACE. `walkSpeed` is 2.5 and a camera moving at it reads as a person
            // rather than as a camera; the scroll wheel takes it up when a shot needs to cover ground.
            rig.flySpeed = 1.6f;
            rig.boostMultiplier = 4f;
            rig.minSpeed = 0.15f;
            rig.maxSpeed = 12f;
            // Well under gameplay sensitivity, which is tuned for finding things in a hurry.
            rig.lookSensitivity = 0.6f;

            // A QUARTER OF THE RETREAT SPEED GOES UP. Enough to read as "backed off and slightly
            // high" per the shotlist without the shot visibly climbing.
            rig.dollyRiseRatio = 0.25f;
            // Same constant governs the ramp up on press and the ease down on release - see
            // `CaptureRig.HandleDolly`.
            rig.dollyEaseTime = 1.1f;

            return rig;
        }

        // The half of the wiring that cannot happen inside `BuildUI`: the prompts and the touch layer
        // are built after it returns, and the player camera after that again.
        private static void WireCaptureRig(CaptureRig rig, GameObject hints, GameObject touch,
                                           Camera playerCamera)
        {
            if (rig == null) return;

            var hidden = new System.Collections.Generic.List<GameObject>(rig.hud) { hints, touch };
            rig.hud = hidden.ToArray();

            rig.playerCamera = playerCamera;
            rig.captureCamera = BuildCaptureCamera(rig.transform, playerCamera);
        }

        // THE SECOND CAMERA, disabled until F10. Not parented to the player - the whole point is that
        // it leaves - and not carrying an `AudioListener`, because the scene already has exactly one
        // and a second is a warning on every frame plus a mix nobody chose.
        private static Camera BuildCaptureCamera(Transform parent, Camera playerCamera)
        {
            GameObject go = new GameObject("CaptureCamera");
            go.transform.SetParent(parent, false);

            Camera cam = go.AddComponent<Camera>();
            cam.enabled = false;
            // Optics are copied off the player camera at detach time (see `CaptureRig.SetDetached`),
            // so what is set here is only what a fresh Camera would otherwise get wrong.
            cam.nearClipPlane = playerCamera.nearClipPlane;
            cam.farClipPlane = playerCamera.farClipPlane;
            cam.fieldOfView = playerCamera.fieldOfView;

            // **THE OPPOSITE BODY TO THE PLAYER'S OWN CAMERA.** `BuildPlayer` masks out the world body
            // and keeps the headless one, because it is standing inside the other one's skull. A
            // camera that has flown away wants the reverse: the whole person, head included, exactly
            // as a mirror or a past self sees them. Without this the trailer's wide shots have a
            // decapitated player in them.
            cam.cullingMask &= ~(1 << EnsureLayer(PlayerBodyViewLayer));
            cam.cullingMask &= ~(1 << EnsureLayer(PlayerBodyShadowLayer));

            // The same pipeline treatment the player camera gets, or the footage stops looking like
            // the game: post-processing off would drop the volume stack the whole building is graded
            // through, and no AA on a room made of straight black panel joins crawls badly in motion.
            UniversalAdditionalCameraData data = go.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;

            return cam;
        }

        // Escape's overlay. Built after everything else on the canvas so it draws over the HUD,
        // the control prompts and the eyelids - a pause menu behind a closed eyelid would be a
        // strange thing to discover.
        private static void BuildPauseMenu(Transform canvas, FirstPersonController playerController)
        {
            GameObject root = new GameObject("PauseMenu");
            root.transform.SetParent(canvas, false);
            Stretch(root.AddComponent<RectTransform>());

            CanvasGroup group = root.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = false;
            group.interactable = false;

            // Heavier than the title screen's scrim: this one has to read as the game having
            // stopped, where the menu's only has to hold text off a picture.
            GameObject scrimGO = new GameObject("Scrim");
            scrimGO.transform.SetParent(root.transform, false);
            Image scrim = scrimGO.AddComponent<Image>();
            scrim.color = new Color(0f, 0f, 0f, 0.72f);
            // Left as a raycast target on purpose: it is what stops a click reaching the end-cycle
            // control sitting underneath it.
            scrim.raycastTarget = true;
            Stretch(scrim.GetComponent<RectTransform>());

            GameObject titleGO = new GameObject("Title");
            titleGO.transform.SetParent(root.transform, false);
            Text title = titleGO.AddComponent<Text>();
            title.font = UIFont();
            title.fontSize = 52;
            title.alignment = TextAnchor.MiddleCenter;
            title.color = Color.red;
            // Spaced out in the string, as everything else in this typeface is.
            title.text = "P A U S E D";
            Localize(title, "pause.title");
            title.horizontalOverflow = HorizontalWrapMode.Overflow;
            title.verticalOverflow = VerticalWrapMode.Overflow;
            title.raycastTarget = false;
            RectTransform titleRect = title.GetComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0.5f, 0.5f);
            titleRect.anchorMax = new Vector2(0.5f, 0.5f);
            titleRect.sizeDelta = new Vector2(1000f, 100f);
            titleRect.anchoredPosition = new Vector2(0f, 170f);

            // FOUR ROWS NOW, at the same 80 pitch the three had. RESTART CYCLE sits SECOND rather
            // than last: it is an action on the run like RESUME, where MAIN MENU and QUIT are ways
            // out of the game - and putting a destructive one at the bottom of a column is how it
            // gets clicked by somebody reaching for QUIT.
            Button resume = Localize(MakeMenuButton(root.transform, "ResumeButton", "RESUME", new Vector2(0f, 80f)), "pause.resume", "  ");
            Button restart = Localize(MakeMenuButton(root.transform, "RestartButton", "RESTART CYCLE", new Vector2(0f, 0f)), "pause.restart", "  ");
            Button toMenu = Localize(MakeMenuButton(root.transform, "MenuButton", "MAIN MENU", new Vector2(0f, -80f)), "pause.mainMenu", "  ");


            // THE SETTINGS, below the buttons rather than above them: these are settings, not
            // actions, and the things a paused player most often wants stay at the top.
            //
            // **VOLUME IS HERE NOW as well as on the title screen** (2026-08-21, by request), and the
            // pause overlay is the place it was most missing: "too loud" is a thought a player has
            // while the PA is talking over them, and until now the only fix was quitting to the menu.
            // Both write the same `GameSettings.MasterVolume`, so neither can disagree with the other.
            //
            // ON THE SAME LEFT COLUMN AS THE BUTTONS. They were centre-anchored while the buttons hang
            // off the screen's left margin, so the two only lined up at 16:9 and read as two designs
            // at any other aspect - see MakeLeftColumn.
            // **CENTRED, AND BELOW THE BUTTONS** (2026-09-03, by request). It was on the same left
            // column the buttons hang on, which put the language row directly under QUIT and
            // overlapping it - QUIT is 60 tall centred at -160, so its bottom edge is -190, and that
            // is exactly where the first settings row was.
            // **THE WHOLE SETTINGS PAGE, NOT FOUR ROWS OF IT** (2026-09-05, by request).
            //
            // This used to build language, subtitles, sensitivity and volume inline, as loose rows
            // under the four buttons - which left the OTHER two settings, the key bindings and the
            // render scale, reachable only from the title screen. The render scale is the worst one
            // to strand there: it is the setting a player reaches for BECAUSE the run is stuttering,
            // which is exactly when quitting to the menu is the least acceptable answer.
            //
            // So the pause menu gets a SETTINGS button and the same page the title screen shows.
            // `SettingsPanel` owns every control and every handler; the eight that `PauseMenu` used
            // to carry its own copy of are gone with the rows.
            Button settingsButton = Localize(MakeMenuButton(root.transform, "SettingsButton",
                "SETTINGS", new Vector2(0f, -160f)), "menu.settings", "  ");
            Button quitTail = Localize(MakeMenuButton(root.transform, "QuitButton", "QUIT",
                new Vector2(0f, -240f)), "menu.quit", "  ");

            SettingsPanel pauseSettings = BuildSettingsPage(root.transform);

            GameObject hintGO = new GameObject("Hint");
            hintGO.transform.SetParent(root.transform, false);
            Text hint = hintGO.AddComponent<Text>();
            hint.font = UIFont();
            hint.fontSize = 18;
            hint.alignment = TextAnchor.MiddleCenter;
            hint.color = new Color(1f, 0.35f, 0.35f, 0.7f);
            hint.text = "[ESC] TO RESUME";
            hint.raycastTarget = false;
            RectTransform hintRect = hint.GetComponent<RectTransform>();
            hintRect.anchorMin = new Vector2(0.5f, 0.5f);
            hintRect.anchorMax = new Vector2(0.5f, 0.5f);
            hintRect.sizeDelta = new Vector2(600f, 36f);
            // Below the four settings rows, which now reach -400.
            hintRect.anchoredPosition = new Vector2(0f, -460f);

            PauseMenu pause = root.AddComponent<PauseMenu>();
            pause.playerController = playerController;
            pause.group = group;
            pause.resumeButton = resume;
            pause.restartButton = restart;
            pause.menuButton = toMenu;
            pause.quitButton = quitTail;
            pause.settingsButton = settingsButton;
            pause.settings = pauseSettings;
        }

        // **A SETTINGS BUTTON BORROWED ONTO THE PAUSE OVERLAY, REPAINTED FOR IT.**
        //
        // `MakeSettingsButton` is built for the settings PAGE, which is charcoal type on a bright
        // photograph of the room: a white plate under near-black words. The pause overlay is the
        // opposite surface - red type on a near-black scrim - so the same button arrives as a white
        // slab with charcoal on it, which is both unreadable against everything around it and the
        // brightest thing on a screen that is meant to be dark. Play reported it as the language and
        // subtitle rows not matching the buttons (2026-09-03).
        //
        // Repainted rather than parameterised: the shape, the size and the layout are all correct
        // and it is only the palette that belongs to the other page. A second builder would be two
        // places for one button to be wrong in.
        private static void OnTheScrim(Button button, Text ink, Color scrimInk)
        {
            if (button == null) return;
            if (ink != null) ink.color = scrimInk;

            if (button.targetGraphic is Image plate) plate.color = Color.white;

            ColorBlock colors = button.colors;
            // Nothing at rest, so the row reads as words on the scrim like everything else on this
            // overlay; a faint red wash on hover and a stronger one on the press.
            colors.normalColor = new Color(0f, 0f, 0f, 0.35f);
            colors.highlightedColor = new Color(0.55f, 0.10f, 0.10f, 0.55f);
            colors.pressedColor = new Color(0.75f, 0.12f, 0.12f, 0.75f);
            colors.selectedColor = colors.highlightedColor;
            colors.disabledColor = new Color(0f, 0f, 0f, 0.2f);
            button.colors = colors;
        }

        // The ending: a full-screen black scrim and a card over it. Two separate CanvasGroups
        // rather than one, because the whole shape of the ending is that the room goes first and
        // the card arrives afterwards, with a beat of nothing in between.
        //
        // Deliberately NOT the wake-up's eyelids, even though they are black panels over the same
        // canvas and would have been free. The lids blink, and the blink is the loop taking you -
        // it is the visual signature of the exact thing that has just failed. Reusing it here would
        // say the cycle turned over.
        private static EndingSequence BuildEndingScreen(Transform canvas)
        {
            GameObject root = new GameObject("EndingScreen");
            root.transform.SetParent(canvas, false);
            Stretch(root.AddComponent<RectTransform>());

            GameObject scrimGO = new GameObject("Scrim");
            scrimGO.transform.SetParent(root.transform, false);
            CanvasGroup scrimGroup = scrimGO.AddComponent<CanvasGroup>();
            scrimGroup.alpha = 0f;
            // Never a raycast target, unlike the pause scrim. There is nothing underneath left to
            // click by the time this is up, and blocking would only matter if something could still
            // be interacted with - which would be a bug, not a thing to defend against here.
            scrimGroup.blocksRaycasts = false;
            Stretch(scrimGO.AddComponent<RectTransform>());

            Image scrim = scrimGO.AddComponent<Image>();
            // Fully opaque, unlike the pause overlay's 0.72: this is not a layer over the room, it
            // is the room being gone.
            scrim.color = Color.black;
            scrim.raycastTarget = false;

            GameObject cardGO = new GameObject("Card");
            cardGO.transform.SetParent(root.transform, false);
            CanvasGroup cardGroup = cardGO.AddComponent<CanvasGroup>();
            cardGroup.alpha = 0f;
            cardGroup.blocksRaycasts = false;
            Stretch(cardGO.AddComponent<RectTransform>());

            GameObject headlineGO = new GameObject("Headline");
            headlineGO.transform.SetParent(cardGO.transform, false);
            Text headline = headlineGO.AddComponent<Text>();
            headline.font = UIFont();
            headline.fontSize = 52;
            headline.alignment = TextAnchor.MiddleCenter;
            headline.color = Color.red;
            // Filled in by EndingSequence, which spaces it out in the string. Seeded here only so
            // the object is not blank in the saved scene.
            headline.text = "C Y C L E   B R O K E N";
            Localize(headline, "end.title");
            headline.horizontalOverflow = HorizontalWrapMode.Overflow;
            headline.verticalOverflow = VerticalWrapMode.Overflow;
            headline.raycastTarget = false;
            RectTransform headlineRect = headline.GetComponent<RectTransform>();
            headlineRect.anchorMin = new Vector2(0.5f, 0.5f);
            headlineRect.anchorMax = new Vector2(0.5f, 0.5f);
            headlineRect.sizeDelta = new Vector2(1400f, 100f);
            headlineRect.anchoredPosition = new Vector2(0f, 30f);

            GameObject detailGO = new GameObject("Detail");
            detailGO.transform.SetParent(cardGO.transform, false);
            Text detail = detailGO.AddComponent<Text>();
            detail.font = UIFont();
            detail.fontSize = 22;
            detail.alignment = TextAnchor.MiddleCenter;
            // Dimmer than the headline, and not spaced out: this is the facility's record of the
            // run rather than its verdict on it, and it is the one number the player earned.
            detail.color = new Color(1f, 0.35f, 0.35f, 0.75f);
            detail.text = "ESCAPED ON ITERATION 1";
            detail.horizontalOverflow = HorizontalWrapMode.Overflow;
            detail.verticalOverflow = VerticalWrapMode.Overflow;
            detail.raycastTarget = false;
            RectTransform detailRect = detail.GetComponent<RectTransform>();
            detailRect.anchorMin = new Vector2(0.5f, 0.5f);
            detailRect.anchorMax = new Vector2(0.5f, 0.5f);
            detailRect.sizeDelta = new Vector2(1000f, 40f);
            detailRect.anchoredPosition = new Vector2(0f, -40f);

            // The run's total clock time, under the iteration count - the facility's record again,
            // not its verdict, so it gets the same treatment `detail` does rather than the
            // headline's. Smaller and dimmer still: this is the second line of a record, not a
            // second thing being announced.
            GameObject timeDetailGO = new GameObject("TimeDetail");
            timeDetailGO.transform.SetParent(cardGO.transform, false);
            Text timeDetail = timeDetailGO.AddComponent<Text>();
            timeDetail.font = UIFont();
            timeDetail.fontSize = 18;
            timeDetail.alignment = TextAnchor.MiddleCenter;
            timeDetail.color = new Color(1f, 0.35f, 0.35f, 0.6f);
            timeDetail.text = "TOTAL TIME 0:00";
            timeDetail.horizontalOverflow = HorizontalWrapMode.Overflow;
            timeDetail.verticalOverflow = VerticalWrapMode.Overflow;
            timeDetail.raycastTarget = false;
            RectTransform timeDetailRect = timeDetail.GetComponent<RectTransform>();
            timeDetailRect.anchorMin = new Vector2(0.5f, 0.5f);
            timeDetailRect.anchorMax = new Vector2(0.5f, 0.5f);
            timeDetailRect.sizeDelta = new Vector2(1000f, 34f);
            timeDetailRect.anchoredPosition = new Vector2(0f, -72f);

            // THE PER-CYCLE BREAKDOWN, in the same place `timeDetail` sits and taller. The two are
            // never on screen together - a one-cycle run gets the single TOTAL TIME line this card
            // has always had, and a run through several gets the table instead - so they can share
            // the space rather than one of them leaving a gap in the layout of the other.
            //
            // ANCHORED AT ITS TOP, which is what lets it grow downward as cycles are added without
            // anything above it moving: the rect is centred at -166 and 200 tall, so its first line
            // starts at -66 whether there are two cycles in it or five.
            GameObject breakdownGO = new GameObject("Breakdown");
            breakdownGO.transform.SetParent(cardGO.transform, false);
            Text breakdown = breakdownGO.AddComponent<Text>();
            breakdown.font = UIFont();
            breakdown.fontSize = 18;
            breakdown.alignment = TextAnchor.UpperCenter;
            // The same dim red as `timeDetail`, because it IS `timeDetail` when there is more than
            // one cycle to report - a record, not a verdict.
            breakdown.color = new Color(1f, 0.35f, 0.35f, 0.6f);
            // Padded columns need a monospace cell, which UIFont() is - see EndingSequence.Row.
            breakdown.lineSpacing = 1.25f;
            breakdown.text = string.Empty;
            breakdown.horizontalOverflow = HorizontalWrapMode.Overflow;
            breakdown.verticalOverflow = VerticalWrapMode.Overflow;
            breakdown.raycastTarget = false;
            RectTransform breakdownRect = breakdown.GetComponent<RectTransform>();
            breakdownRect.anchorMin = new Vector2(0.5f, 0.5f);
            breakdownRect.anchorMax = new Vector2(0.5f, 0.5f);
            breakdownRect.sizeDelta = new Vector2(1000f, 200f);
            breakdownRect.anchoredPosition = new Vector2(0f, -166f);

            // "CLICK TO CONTINUE", on a group of its own so it can arrive after the card rather than
            // with it - the numbers get their moment before anything asks the player to move on.
            GameObject promptGO = new GameObject("Prompt");
            promptGO.transform.SetParent(root.transform, false);
            CanvasGroup promptGroup = promptGO.AddComponent<CanvasGroup>();
            promptGroup.alpha = 0f;
            promptGroup.blocksRaycasts = false;
            Stretch(promptGO.AddComponent<RectTransform>());

            GameObject promptTextGO = new GameObject("PromptText");
            promptTextGO.transform.SetParent(promptGO.transform, false);
            Text prompt = promptTextGO.AddComponent<Text>();
            prompt.font = UIFont();
            prompt.fontSize = 18;
            prompt.alignment = TextAnchor.MiddleCenter;
            // Dimmer than the record above it: this is an instruction, not part of what the run said.
            prompt.color = new Color(1f, 0.35f, 0.35f, 0.55f);
            prompt.text = "CLICK TO CONTINUE";
            Localize(prompt, "end.clickContinue");
            prompt.horizontalOverflow = HorizontalWrapMode.Overflow;
            prompt.verticalOverflow = VerticalWrapMode.Overflow;
            prompt.raycastTarget = false;
            RectTransform promptRect = prompt.GetComponent<RectTransform>();
            promptRect.anchorMin = new Vector2(0.5f, 0.5f);
            promptRect.anchorMax = new Vector2(0.5f, 0.5f);
            promptRect.sizeDelta = new Vector2(800f, 34f);
            // Below the breakdown, which is 200 tall centred at -166 - so this clears its bottom edge.
            promptRect.anchoredPosition = new Vector2(0f, -300f);

            EndingSequence ending = root.AddComponent<EndingSequence>();
            ending.prompt = prompt;
            ending.promptGroup = promptGroup;
            ending.scrimGroup = scrimGroup;
            ending.cardGroup = cardGroup;
            ending.headline = headline;
            ending.detail = detail;
            ending.timeDetail = timeDetail;
            ending.breakdown = breakdown;
            ending.menuScene = "MainMenu";

            return ending;
        }

        // The room telling the player about the end-cycle control, on all four walls at once. Hung
        // in Room1 now and shown from iteration 2 - see the call site for why it moved out of Room3.
        //
        // World-space canvases hung just proud of the panelling rather than the panels themselves
        // being lit to spell it. That was the first idea and it does not survive contact with the
        // grid: a wall is 5 or 6 cells across by 4 tall, so panel-as-pixel gives a 6x4 display and
        // nothing legible can be written in it. What makes this still read as the wall rather than
        // as a poster is the dark plate behind the text - a section of white panelling switching to
        // near-black with red type on it is exactly what a display doing something looks like, and
        // it is the same red-on-near-black the HUD already uses.
        // **EVERY CONTROL, ON THE WALL THE PLAYER WAKES UP FACING, IN ITERATION 1 ONLY.**
        //
        // What the calibration room used to say, said in the room the game actually starts in. That
        // room is gone (2026-08-31, by request): it taught the controls, set the sensitivity, and
        // charged a minute of standing still before the clock had ever run. The teaching was worth
        // keeping; the room was not.
        //
        // THE SOUTH WALL, because that is the one the wake-up leaves the player looking at - room1-1
        // is entered at yaw 180, down the room and away from the bed (see `BuildWallMessage`, which
        // relies on the same fact). It shares that wall with the N sign and cannot collide with it:
        // that one is `showFromIteration = 2` and this is `lastIteration = 1`, so the wall is never
        // carrying both.
        //
        // ONE FACE, WHERE THE CALIBRATION ROOM USED THREE. Sprint and crouch were on its two side
        // walls, on the argument that they are about HOW you cross a room and belong on the walls
        // you cross between. That argument needed a room built to make it; here it would just be two
        // controls the player has to turn round to find, so they join the column.
        //
        // FIGURES INSTEAD OF WORDS, which is the rule the whole building follows - the facility
        // labels itself in pictograms and the one screen that has to be understood before anybody
        // has played is the worst possible place to require reading.
        // **EVERY CONTROL, ON THE WALL THE PLAYER WAKES UP FACING, IN ITERATION 1 ONLY.**
        //
        // What the calibration room used to say, said in the room the game actually starts in. That
        // room is gone (2026-08-31, by request): it taught the controls, set the sensitivity, and
        // charged a minute of standing still before the clock had ever run. The teaching was worth
        // keeping; the room was not.
        //
        // THE SOUTH WALL, because that is the one the wake-up leaves the player looking at - room1-1
        // is entered at yaw 180, down the room and away from the bed (`BuildWallMessage` relies on
        // the same fact). It shares that wall with the N sign and cannot collide with it: that one is
        // `showFromIteration = 2` and this is `lastIteration = 1`, so the wall never carries both.
        //
        // **ONE PICTOGRAM PER WALL PANEL, AND THAT IS THE WHOLE LAYOUT RULE** (2026-08-31, by
        // request: "it is ugly where it crosses the black grid"). The first version was a single
        // 6.4m canvas laid over the wall, which put glyphs across the grooves at whatever height the
        // arithmetic happened to land on - a sign printed on top of the wall rather than on it.
        //
        // The wall is already a grid: five columns of `GridCellWidth` by four rows of
        // `GridCellHeight`, with `GridLineThickness` of dark groove between them, and every cell
        // centre is at `(i + 0.5) * cell`. So each control gets a cell of its own and a canvas
        // inset from the groove on all four sides, which is why nothing overlaps anything - it
        // cannot, by construction, rather than by a number somebody checked.
        //
        // FIVE ON TOP AND TWO BELOW, both centred, because five columns is ODD: four items cannot be
        // centred on cell centres and five can. That constraint decided the grouping and the grouping
        // turns out to be the right one anyway - **how you get about** on the upper row, **what you
        // do when you get there** on the lower.
        //
        // FIGURES INSTEAD OF WORDS, which is the rule the whole building follows: the facility labels
        // itself in pictograms, and the one screen that has to be understood before anybody has
        // played is the worst possible place to require reading.
        // **EVERY CONTROL, ON THE WALL THE PLAYER WAKES UP FACING, IN ITERATION 1 ONLY.**
        //
        // What the calibration room used to say, said in the room the game actually starts in. That
        // room is gone (2026-08-31, by request): it taught the controls, set the sensitivity, and
        // charged a minute of standing still before the clock had ever run.
        //
        // THE SOUTH WALL, because that is the one the wake-up leaves the player looking at - room1-1
        // is entered at yaw 180, down the room and away from the bed (`BuildWallMessage` relies on
        // the same fact). It shares that wall with the N sign and cannot collide with it: that one is
        // `showFromIteration = 2` and this is `lastIteration = 1`.
        //
        // **ONE PANEL PER IDEA, NOT ONE PER KEY** (2026-08-31, by request). Seven cells spread across
        // two rows became four: the two things you do with your legs share a cell, the two things you
        // do with your posture share the next, E has its own, and the mouse sits under the middle of
        // them. Grouping by what a control is FOR reads faster than a grid of every key the game
        // uses, and it leaves the wall mostly white - which is what makes four marks findable.
        //
        // **THE LEFT-CLICK CELL IS GONE**, also by request. It was added in this same session on the
        // argument that the button is never taught anywhere else; that is still true, and the HUD's
        // own disc is where it gets taught.
        //
        // Cells are addressed the way the request stated them - **row from the TOP, column from the
        // LEFT, both counting from one** - and turned into this file's own bottom-left indices in
        // `ControlPanel`. Two conventions for one grid is worth it here: the wall is described from
        // where a person stands looking at it.
        private static ControlsWall BuildControlsWall(Transform room, float roomCenterZ)
        {
            GameObject root = new GameObject("ControlsWall");
            root.transform.SetParent(room, false);
            root.transform.localPosition = new Vector3(0f, 0f, roomCenterZ);

            float wallZ = -RoomDepth / 2f;                 // inner face of the south wall

            // Charcoal on white panelling, and the same ink the Room2 pictogram uses so the game's
            // wordless displays match. Well clear of `GrooveDark` (0.04) so a glyph cannot be
            // mistaken for a seam.
            Color ink = new Color(0.20f, 0.20f, 0.23f, 0.78f);

            var faces = new System.Collections.Generic.List<CanvasGroup>();

            // E on the left, then posture, then the legs (2026-08-31, by request: the two ends
            // swapped). The mouse sits under the middle of the three either way.
            CanvasGroup take  = ControlPanel(root.transform, "Take",  row: 2, column: 2, wallZ, faces);
            CanvasGroup pose  = ControlPanel(root.transform, "Pose",  row: 2, column: 3, wallZ, faces);
            CanvasGroup legs  = ControlPanel(root.transform, "Move",  row: 2, column: 4, wallZ, faces);
            CanvasGroup look  = ControlPanel(root.transform, "Look",  row: 3, column: 3, wallZ, faces);

            // W/A/S/D is the one group that is four caps in two courses, so it gets its own
            // arithmetic; everything else is one key and one figure, which is what `ControlRow`
            // draws. Both halves are measured as a pair and then centred, so a 130-wide SHIFT and a
            // 72-wide E sit equally well in identical cells.
            const float key = 54f, gap = 6f, step = key + gap;
            const float blockW = 3f * key + 2f * gap;      // 174
            const float figure = 72f, capGap = 20f;
            float pairW = blockW + capGap + figure;
            float keysX = -pairW / 2f + blockW / 2f;
            float figX = pairW / 2f - figure / 2f;

            // The two courses inside the legs panel: W/A/S/D above, SPACE below it.
            const float upper = 58f, lower = -80f;

            MakeKeyCap(legs.transform, "KeyW", "W", new Vector2(keysX, upper + step / 2f), new Vector2(key, key), 24);
            MakeKeyCap(legs.transform, "KeyA", "A", new Vector2(keysX - step, upper - step / 2f), new Vector2(key, key), 24);
            MakeKeyCap(legs.transform, "KeyS", "S", new Vector2(keysX, upper - step / 2f), new Vector2(key, key), 24);
            MakeKeyCap(legs.transform, "KeyD", "D", new Vector2(keysX + step, upper - step / 2f), new Vector2(key, key), 24);
            MakeWallIcon(legs.transform, "MoveFigure", FigureWalkIcon(), new Vector2(figX, upper), figure, ink);

            MakeKeyCap(legs.transform, "KeySpace", "SPACE", new Vector2(keysX, lower), new Vector2(blockW, 42f), 20);
            MakeWallIcon(legs.transform, "JumpFigure", FigureJumpIcon(), new Vector2(figX, lower), figure, ink);

            ControlPair(pose.transform, "SHIFT", FigureRunIcon(), "CTRL", FigureCrouchIcon(), ink);

            ControlRow(take.transform, "E", 72f, FigurePressIcon(), ink);
            MouseRow(look.transform, MouseIcon(), FigureLookIcon(), ink);

            ControlsWall wall = root.AddComponent<ControlsWall>();
            wall.faces = faces.ToArray();
            wall.roomCenterZ = room.position.z + roomCenterZ;
            wall.halfDepth = RoomDepth / 2f;
            wall.lastIteration = 1;

            Debug.Log($"[SceneBuilder] Controls wall on room1-1's south wall: {faces.Count} panels "
                    + "(move, posture, E, look), iteration 1 only. Cells are "
                    + $"{GridCellWidth:0.##} x {GridCellHeight:0.##} with {GridLineThickness:0.###} "
                    + "of groove; nothing is drawn within it.");
            return wall;
        }

        // TWO KEYS AND WHAT THEY DO, one above the other in a single cell. The pair shares the
        // panel's own centring so both rows line up on the same two columns - a key column and a
        // figure column - which is what makes them read as one idea rather than two cells crammed
        // together.
        private static void ControlPair(Transform panel, string topLabel, Sprite topFigure,
                                        string bottomLabel, Sprite bottomFigure, Color ink)
        {
            const float capW = 130f, capH = 42f, figure = 72f, capGap = 20f;
            const float course = 62f;
            float total = capW + capGap + figure;
            float keyX = -total / 2f + capW / 2f, figX = total / 2f - figure / 2f;

            MakeKeyCap(panel, "KeyTop", topLabel, new Vector2(keyX, course), new Vector2(capW, capH), 20);
            MakeWallIcon(panel, "FigureTop", topFigure, new Vector2(figX, course), figure, ink);
            MakeKeyCap(panel, "KeyBottom", bottomLabel, new Vector2(keyX, -course), new Vector2(capW, capH), 20);
            MakeWallIcon(panel, "FigureBottom", bottomFigure, new Vector2(figX, -course), figure, ink);
        }

        // ONE WALL PANEL, AS A CANVAS INSET FROM ITS OWN GROOVES.
        //
        // `column` and `row` index the wall grid from the bottom left, and the cell centre is
        // `(i + 0.5) * cell` - the same arithmetic the calibration room's start button used to sit
        // its plate on a cell. The canvas is the visible FACE (cell minus the groove) shrunk by a
        // further margin, so a glyph that fills its canvas still stops short of the dark line.
        private static CanvasGroup ControlPanel(Transform parent, string name, int row, int column,
                                                float wallZ,
                                                System.Collections.Generic.List<CanvasGroup> into)
        {
            // **STATED FROM THE TOP LEFT, BUILT FROM THE BOTTOM LEFT.** A wall is described the way
            // somebody standing in front of it counts - first row down, first column from the left,
            // both from one - and this file's grid arithmetic counts rows up from the floor. The
            // conversion happens here, once, rather than at four call sites.
            int cell = GridRows - row;          // 1st row from the top is the top row
            column -= 1;
            // How far inside the panel face the drawing stops. The groove is 50mm; this is another
            // 60mm of white on top of it, which is what keeps a glyph from looking crowded into its
            // own cell rather than placed in it.
            const float margin = 0.06f;
            const float scale = 0.004f;

            float x = (column + 0.5f) * GridCellWidth - RoomWidth / 2f;
            float y = (cell + 0.5f) * GridCellHeight;
            float w = GridCellWidth - GridLineThickness - 2f * margin;
            float h = GridCellHeight - GridLineThickness - 2f * margin;

            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);

            Canvas canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;

            RectTransform rect = go.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(w / scale, h / scale);
            rect.localScale = Vector3.one * scale;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            // anchoredPosition3D and AFTER the Canvas exists - see `MakeWallFace` for both traps.
            rect.anchoredPosition3D = new Vector3(x, y, wallZ + 0.05f);
            // Forward points INTO the wall: a world-space canvas is legible when its forward matches
            // the direction the viewer is LOOKING, and a player who has just woken looks south at it.
            rect.localRotation = Quaternion.Euler(0f, 180f, 0f);

            CanvasGroup group = go.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = false;
            group.interactable = false;
            into.Add(group);
            return group;
        }

        // A KEY AND WHAT IT DOES, side by side and centred in their panel. The pair is measured as a
        // whole and then centred, rather than each half being placed at a number - which is what
        // keeps a 148-wide SHIFT and a 72-wide E looking equally well seated in identical cells.
        private static void ControlRow(Transform panel, string label, float capW, Sprite figure, Color ink)
        {
            const float figureSize = 84f, capGap = 22f;
            float capH = capW > 100f ? 48f : 62f;
            float total = capW + capGap + figureSize;

            MakeKeyCap(panel, "Key", label, new Vector2(-total / 2f + capW / 2f, 0f),
                       new Vector2(capW, capH), capW > 100f ? 22 : 28);
            MakeWallIcon(panel, "Figure", figure,
                         new Vector2(total / 2f - figureSize / 2f, 0f), figureSize, ink);
        }

        // The same row with a mouse glyph where the keycap goes.
        private static void MouseRow(Transform panel, Sprite glyph, Sprite figure, Color ink)
        {
            const float glyphSize = 84f, figureSize = 84f, capGap = 22f;
            float total = glyphSize + capGap + figureSize;

            MakeWallIcon(panel, "Glyph", glyph, new Vector2(-total / 2f + glyphSize / 2f, 0f), glyphSize, ink);
            MakeWallIcon(panel, "Figure", figure, new Vector2(total / 2f - figureSize / 2f, 0f), figureSize, ink);
        }

        // **THE NOTICE ON THE FLOOR, WHICH IS THE ONLY PLACE THE GAME EVER STATES ITS OWN PREMISE.**
        //
        // A clipboard lying in front of the controls wall (2026-08-31, by request). Pick it up with E
        // and the page is in your hand at reading distance; put it down and it stays where it fell.
        //
        // **ITS TEXT IS A WORLD-SPACE CANVAS, NOT A TEXTURE**, which is the choice every sign in this
        // building makes. Printing words into a `Texture2D` needs a font rasteriser at build time; a
        // `Text` on a canvas is what `MakeMenuLine` already does, it stays crisp at any distance, and
        // it goes through `Localize` for free.
        //
        // The page is its own 24-vertex flat mesh with its own material in the model, which is what
        // made this cheap - the canvas sits a millimetre above that page and is exactly its size.
        private static CarryableItem BuildIntakeNotice(Transform room, Vector3 localPosition, float yaw)
        {
            // **0.44, UP FROM 0.32** (2026-08-31, by request). A real clipboard is 0.32m and that is
            // what "held at true size" would give, but this is the one object in the game that has to
            // be READ rather than recognised, and a page carries a fixed number of characters however
            // close it is held. Oversizing is what every prop in this building already does - a 220mm
            // ball-pit ball, a 160mm Rubik's cube - and here it buys legibility rather than presence.
            // **0.88, DOUBLED AGAIN** (2026-08-31, by request). A real clipboard is 0.32m; this is
            // nearly three of them, and the reason is the same one that took it to 0.44 - it is the
            // only object in the game that has to be READ. Everything on the page is sized as a
            // FRACTION of it below, so the type grows with the board and the layout is unchanged;
            // what the size actually buys is that the thing is findable lying on a white floor.
            const float boardLength = 0.88f;

            GameObject root = new GameObject("IntakeNotice");
            root.transform.SetParent(room, false);
            root.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);

            (GameObject model, Bounds box) = PlaceModelLocal($"{PlayDir}/clipboard.glb", root.transform,
                "Visual", Vector3.zero, Quaternion.identity, boardLength);
            if (model == null) return null;

            // **LAID FLAT, AND THE ROTATION IS MEASURED RATHER THAN WRITTEN.** `PlaceModelLocal`
            // forces the outer root to the rotation it is given, which does NOT reach the Sketchfab
            // export's own `Sketchfab_model` node - that one carries the -90 X that turns a Z-up
            // export into Unity's Y-up, and it survives. Identity on the root therefore left this
            // board standing on its edge on the floor, with its 0.44m long axis along Y.
            //
            // The same trap `BuildNightstandCube` recorded ("identity here stood the cube on a
            // corner"). A board is the one shape where measuring is unambiguous: it is flat, so its
            // shortest axis is its thickness, and lying down means that axis is the vertical one.
            if (box.size.y > Mathf.Min(box.size.x, box.size.z) * 1.5f)
            {
                model.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f) * model.transform.localRotation;
                box = MeasuredBounds(root.gameObject);
            }
            if (box.size.y > Mathf.Min(box.size.x, box.size.z))
                Debug.LogError($"[SceneBuilder] the intake notice is not lying flat: {box.size}.");

            // CENTRED ON THE ROOT, because `floorY` below is half the object's height - which is only
            // true if the root sits at the middle of it.
            Vector3 delta = box.center - root.transform.position;
            model.transform.position -= delta;
            box = MeasuredBounds(root.gameObject);

            // **AND THEN LIFTED OUT OF THE FLOOR.** Centring the model on the root put the root at
            // the board's MIDDLE, so a root at y=0 buries the lower half - play reported exactly
            // that. `floorY` is the resting height `FallingItem` uses when something is DROPPED; the
            // pose it is built in has to be set here as well, and the two are the same number.
            root.transform.localPosition = localPosition + Vector3.up * (box.size.y / 2f);

            // **WHICH END THE CLIP IS AT, MEASURED.** The page has to read with its head toward the
            // clip - a clipboard held the other way up is the fault play reported as "the writing is
            // upside down". Which way that is in the root's own frame is a fact about this export
            // and about the flat-lay rotation above, so it is asked rather than assumed: the clip is
            // the metal part, and the answer is the sign of its offset from the board's centre.
            float clipSide = 1f;
            foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true))
            {
                Material m = r.sharedMaterial;
                if (m == null || !m.name.ToLowerInvariant().Contains("metal")) continue;
                clipSide = Mathf.Sign(root.transform.InverseTransformPoint(r.bounds.center).z);
                break;
            }

            GameObject faceGO = new GameObject("Page");
            faceGO.transform.SetParent(root.transform, false);

            Canvas canvas = faceGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;

            // The page mesh's own footprint, as a fraction of the board's longest side.
            float pageW = boardLength * (79.2f / 126.5f);
            float pageH = boardLength * (110.3f / 126.5f);
            const float scale = 0.0004f;
            float unitsW = pageW / scale, unitsH = pageH / scale;

            RectTransform rect = faceGO.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(unitsW, unitsH);
            rect.localScale = Vector3.one * scale;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            // 1.5mm proud of the board. `Euler(90,...)` sends the canvas's own forward to -Y, i.e.
            // downward, which is right and reads backwards: a world-space canvas is legible when its
            // forward matches the direction the VIEWER looks, and somebody reading a clipboard looks
            // down at it. The yaw is the measurement above - 180 flips the page end for end so its
            // head is at the clip.
            rect.anchoredPosition3D = new Vector3(0f, box.size.y / 2f + 0.0015f, 0f);
            rect.localRotation = Quaternion.Euler(90f, clipSide > 0f ? 0f : 180f, 0f);

            // The ink. Not black: a printed form on white paper under a white ceiling reads as grey,
            // and pure black on this page looks like a decal rather than toner.
            Color ink = new Color(0.16f, 0.16f, 0.18f, 1f);
            Color faint = new Color(0.42f, 0.42f, 0.46f, 1f);

            // **EVERY LINE IS CHECKED AGAINST THE PAGE IT IS ON.** CLAUDE.md §3: a fixed-width label
            // needs `0.6 x fontSize x length` and silent rewrapping has bitten this project three
            // times now - the first version of this page overflowed on three of its four lines,
            // because the rects were 760 units wide inside a 501-unit canvas. `PageLine` asserts it,
            // so the next edit to this text fails the build rather than the eye.
            float wide = unitsW * 0.90f;
            pageHalfHeight = unitsH / 2f;

            // **EVERY SIZE AND POSITION IS A FRACTION OF THE PAGE**, so the board can be resized in
            // one place and the layout comes with it. Written as absolute units first and re-derived
            // here when the board doubled - which is the moment the absolutes stopped being numbers
            // anybody could reason about.
            float u = unitsH;

            // **EVERYTHING SLID DOWN THE PAGE TO CLEAR THE CLIP.** The clamp is a real part of the
            // model - it spans the top of the board, from -67 to -41 in the mesh's own units, which
            // is the top fifth of the page - and the title was printed straight under it. On paper
            // the top of a form is where a heading goes; on a clipboard it is where the clip is.
            //
            // `clipDrop` is that fifth, taken off every line's y. Nothing else moves, so the layout
            // is the one that was tuned - it has simply stopped starting above the clamp.
            // **THE CLIP TAKES THE TOP AND THE PAGE HAS A BOTTOM EDGE, so the block is SCALED into
            // what is left rather than just pushed down.** Shifting alone was the first attempt and
            // it traded one overflow for another - the heading cleared the clamp and the last two
            // lines ran off the bottom of the paper, which play reported.
            //
            // Measured off the model: the clamp covers the page from -55.16 to -41.25 in its own
            // units, which is the top 12.6%. The band left is therefore +0.374 to -0.5 of the page,
            // and the authored block spans +0.437 to -0.414 counting each line's own height. 0.92
            // and 0.055 put it inside with room at both ends - checked by `PageLine`, which now
            // fails the build if a line leaves the paper.
            // 0.92, and it went to 0.85 for a day while the closing block was four lines. It is two
            // again - see below - so the block fits the band it was tuned for. `PageLine` fails the
            // build if that is ever wrong, which is what it is for.
            const float layoutScale = 0.92f;
            const float clipDrop = 0.055f;

            PageLine(faceGO.transform, "Header", "ITERATION PROGRAM", Mathf.RoundToInt(u * 0.0542f), ink, u * (0.396f * layoutScale - clipDrop), wide, "note.header");
            PageLine(faceGO.transform, "Sub", "SUBJECT INTAKE", Mathf.RoundToInt(u * 0.0355f), faint, u * (0.328f * layoutScale - clipDrop), wide, "note.sub");

            GameObject ruleGO = new GameObject("Rule");
            ruleGO.transform.SetParent(faceGO.transform, false);
            Image rule = ruleGO.AddComponent<Image>();
            rule.color = faint;
            rule.raycastTarget = false;
            RectTransform ruleRect = rule.GetComponent<RectTransform>();
            ruleRect.anchorMin = ruleRect.anchorMax = ruleRect.pivot = new Vector2(0.5f, 0.5f);
            ruleRect.sizeDelta = new Vector2(wide, u * 0.0031f);
            ruleRect.anchoredPosition = new Vector2(0f, u * (0.282f * layoutScale - clipDrop));

            // LABEL AND VALUE ON SEPARATE LINES, which is what let the type grow. Run together they
            // were 33 characters and had to be small enough to fit a page held at arm's length.
            PageLine(faceGO.transform, "ObjectiveLabel", "OBJECTIVE", Mathf.RoundToInt(u * 0.0313f), faint, u * (0.167f * layoutScale - clipDrop), wide, "note.objectiveLabel");
            PageLine(faceGO.transform, "Objective", "LEAVE THE ROOM", Mathf.RoundToInt(u * 0.048f), ink, u * (0.109f * layoutScale - clipDrop), wide, "note.objective");
            PageLine(faceGO.transform, "CycleLabel", "CYCLE LENGTH", Mathf.RoundToInt(u * 0.0313f), faint, u * (0.010f * layoutScale - clipDrop), wide, "note.cycleLabel");
            PageLine(faceGO.transform, "Cycle", "60 SECONDS", Mathf.RoundToInt(u * 0.048f), ink, u * (-0.047f * layoutScale - clipDrop), wide, "note.cycle");

            PageLine(faceGO.transform, "Repeat1", "The cycle repeats until", Mathf.RoundToInt(u * 0.0334f), ink, u * (-0.182f * layoutScale - clipDrop), wide, "note.repeat1");
            PageLine(faceGO.transform, "Repeat2", "the objective is met.", Mathf.RoundToInt(u * 0.0334f), ink, u * (-0.227f * layoutScale - clipDrop), wide, "note.repeat2");

            // **THE LINE THE WHOLE PAGE IS FOR**, and it is the only thing in the game that says the
            // loop is happening TO the player rather than merely happening.
            //
            // **IT USED TO SAY THE OPPOSITE HALF** - *"You will not remember reading this."* - and that
            // line is gone (2026-09-03, by request). Two sentences were tried together for a day and
            // one of them had to go: the page has room for one closing thought, and the two were not
            // equals. What the player forgets is a rule about the fiction. What ACCUMULATES is the
            // rule the whole game is built on - the past selves are the accumulation, every room is
            // solved by it, and a player who has not yet seen a ghost has been told the mechanic in
            // one sentence without being told a single control.
            //
            // Losing the forgetting costs nothing it was carrying alone: the notice already exists in
            // iteration 1 and never again, which SHOWS the same thing rather than claiming it.
            //
            // Grey rather than ink: this is the form's small print, not its instruction. The
            // instruction is LEAVE THE ROOM, in black, four lines up.
            PageLine(faceGO.transform, "Accumulate1", "Everything you do", Mathf.RoundToInt(u * 0.0334f), faint, u * (-0.344f * layoutScale - clipDrop), wide, "note.accumulate1");
            PageLine(faceGO.transform, "Accumulate2", "will accumulate.", Mathf.RoundToInt(u * 0.0334f), faint, u * (-0.389f * layoutScale - clipDrop), wide, "note.accumulate2");

            // --- the carryable ------------------------------------------------------------------
            // **BUILT IN THE ROOT'S OWN FRAME, NOT FROM A TRANSFORMED WORLD BOX.** `box` is a world
            // AABB and this root is YAWED, and `InverseTransformVector` of an EXTENT is not how an
            // extent rotates - it mixes the axes and comes out arbitrary. Measured on the built
            // scene it gave (0.82, 0.77, 0.19): a slab 19cm deep that the player could stand beside
            // without ever being inside, which is why E did nothing. Play reported exactly that.
            //
            // The board's own dimensions are known here, so the volume is stated rather than
            // derived: the board plus an arm's reach all round, and low, because there is nothing
            // else on this floor to contest the press with.
            BoxCollider trigger = root.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.center = new Vector3(0f, 0f, 0f);
            trigger.size = new Vector3(boardLength + 1.0f, 1.4f, boardLength + 1.0f);

            // **AT THE ROOT, IN LOCAL COORDINATES - AND THAT IS THE WHOLE OF WHY E DID NOTHING.**
            //
            // This was `anchor.transform.position = box.center`, a WORLD position taken from a bounds
            // measured BEFORE the root was moved to its place in the room. The anchor therefore sat
            // where the board had been while it was still being assembled at the room's origin -
            // 3.6m from the clipboard, floating in open air. Measured on the built scene: local
            // (1.204, -0.027, 3.437).
            //
            // Every half of the press is judged against that one point. `WantsInteractHint` ends in
            // `PlayerLookup.InView(HintAnchor)` and `PressGoesTo` in `IsAimedAt(HintAnchor)`, so E
            // was being asked about a spot nowhere near the thing the player was looking at.
            //
            // **`CheckHintAnchors` could not catch this**, and that is worth knowing about the check:
            // it looks for an anchor buried INSIDE geometry its fixture does not own. An anchor
            // floating in the middle of an empty room is not inside anything, so it passed - twice.
            //
            // The root is already the board's centre by this line (the model was centred on it and
            // then the whole root was placed), so local zero is exactly right and cannot go stale.
            GameObject anchor = new GameObject("HintAnchor");
            anchor.transform.SetParent(root.transform, false);
            anchor.transform.localPosition = Vector3.zero;

            CarryableItem item = root.AddComponent<CarryableItem>();
            item.itemId = IntakeNoticeItemId;
            item.displayName = "NOTICE";
            item.icon = PageIcon();
            item.hintAnchor = anchor.transform;
            // It lies where it was put down rather than snapping back to the pose it was built in -
            // the same flag the ladder needed. See `CarryableItem.keepsDropYaw`.
            item.keepsDropYaw = true;
            item.floorY = box.size.y / 2f;
            // **HELD WHERE THE WHOLE PAGE IS ON SCREEN**, which is the constraint (2026-08-31, by
            // request: "I cannot see all of it"). The page is 0.77m tall now, so at the 0.42m it was
            // held at it ran off the top and bottom of the view. 0.95m subtends about 44 degrees of a
            // 60-degree lens - the whole board, with margin - and because the TYPE scaled with the
            // board, moving it that much further away leaves the words exactly the size they were.
            //
            // Nearly centred, too. A document offset to the corner is a document you read by turning
            // your head; this is meant to be read where it is.
            item.handLocalPosition = new Vector3(0.10f, -0.14f, 0.93f);
            // SQUARER TO THE EYE than a carried object. -58 was a clipboard held at the hip; this is
            // one held up to be read, which is most of the way to facing you.
            // **AND THE RIGHT WAY UP, WHICH TOOK THREE WRONG ANSWERS AND THEN ARITHMETIC.**
            //
            // `Quaternion.Euler(x, y, z)` applies **Z, then X, then Y** - so the composition is
            // `Ry * Rx * Rz`, with **Y outermost** (in the parent's frame) and **Z innermost** (in the
            // object's own). I had it exactly backwards for two attempts and guessed an axis each
            // time; the guesses are what play kept reporting:
            //
            //   (-76, 4, 0)    face toward you, writing upside down
            //   (-76, 184, 0)  Y is outermost, so 180 there turns the whole board over - its BACK
            //   (-76, 4, 180)  Z is innermost, about the board's own LONG axis - also its back
            //
            // Solved rather than guessed in the end. Two facts settle it: the face normal is root
            // +Y (the page canvas sits on that side), and the page's HEAD is root -Z - which is not
            // a choice, it is what `clipSide` measured off the model and what the build logs as
            // "clip toward -Z". Feeding those through `Ry*Rx*Rz` reproduces all three reports above
            // and leaves exactly one pose that shows the face AND stands the page up.
            //
            // The turn that was actually wanted - 180 about the board's own face normal - is not a
            // single Euler term at all: `Ry(4)*Rx(-76)*Ry(180)` reduces to `Ry(184)*Rx(76)`, which
            // in Unity's own order is (76, 184, 0).
            item.handLocalEuler = new Vector3(76f, 184f, 0f);
            item.handLocalScale = root.transform.lossyScale;
            item.audioSource = MakeSource(root.transform, "PickupAudio", 1f, 0.9f);
            item.pickupClip = LoadClip(SfxDir, "sfx_item_pickup");

            Debug.Log($"[SceneBuilder] Intake notice: board {boardLength:0.##}m long, thickness "
                    + $"{box.size.y:0.###}m, standing at {root.transform.position}. Page "
                    + $"{pageW:0.###} x {pageH:0.###}m = {unitsW:0} x {unitsH:0} units, clip toward "
                    + $"{(clipSide > 0f ? "+Z" : "-Z")}.");
            return item;
        }

        // How far it is from the middle of the page to its edge, in canvas units. Set by
        // `BuildIntakeNotice` before it writes any line; a field because `PageLine` already takes
        // seven arguments and this is the same for every one of them.
        private static float pageHalfHeight;

        // ONE LINE ON THE PAGE, WIDTH-CHECKED. `MakeMenuLine` takes a rect and uGUI silently rewraps
        // or clips anything that does not fit it, which is invisible until somebody looks at the
        // object - and this page is 0.28m of paper, so "somebody looks at it" is the whole point of
        // the prop. The estimate is CLAUDE.md's: 0.6 x fontSize x length for this font.
        private static void PageLine(Transform page, string name, string content, int fontSize,
                                     Color color, float y, float width, string key)
        {
            // **AND THAT IT IS STILL ON THE PAPER.** A line can fit its rect and still hang off the
            // page - which is what happened when the block was pushed down to clear the clip. The
            // rect is `fontSize * 1.5` tall and centred on `y`, so its edges are what get compared.
            float half = pageHalfHeight;
            if (half > 0f && (Mathf.Abs(y) + fontSize * 0.75f) > half)
                Debug.LogError($"[SceneBuilder] the notice's '{name}' line is centred at {y:0} with "
                             + $"{fontSize * 0.75f:0} of half-height, which puts it off a page that "
                             + $"is {half:0} units to the edge. Move it or shrink the block.");

            float need = 0.6f * fontSize * content.Length;
            if (need > width)
                Debug.LogError($"[SceneBuilder] the notice's '{name}' line needs about {need:0} units "
                             + $"at size {fontSize} and has {width:0}. It will rewrap or clip on the "
                             + "page. Shorten it, drop the size, or split the line.");

            Localize(MakeMenuLine(page, name, content, fontSize, color,
                                  new Vector2(0f, y), new Vector2(width, fontSize * 1.5f)), key);
        }

        // A SHEET WITH WRITING ON IT, for the HUD's carried slot. Lines rather than glyphs - at 24
        // pixels a paragraph is texture, and texture is what says "document".
        private static Sprite PageIcon()
        {
            var icon = new IconCanvas(128);
            icon.Bar(new Vector2(0.5f, 0.5f), new Vector2(0.30f, 0.40f));
            icon.Bar(new Vector2(0.5f, 0.5f), new Vector2(0.255f, 0.355f), 0f, -1f);
            for (int i = 0; i < 5; i++)
                icon.Bar(new Vector2(0.5f, 0.76f - i * 0.115f), new Vector2(0.175f, 0.022f));
            return SaveSprite(icon, "icon_page");
        }

        private static PanelMessage BuildWallMessage(Transform parent, float roomCenterZ)
        {
            GameObject root = new GameObject("WallMessage");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = new Vector3(0f, 0f, roomCenterZ);

            // Proud of the panel faces, which sit on the room bound itself. Small enough to read as
            // printed on the wall, large enough that no camera angle z-fights with it.
            const float standoff = 0.05f;
            float halfDepth = RoomDepth / 2f;
            // Set so the plate's BOTTOM edge clears the door lamp, not merely the doorway. The
            // plate is 1.98m tall, so at 3.95 it spans 2.96..4.94: above the lamp at 2.725..2.835
            // by 0.125m and under the 5.408 ceiling by 0.47. At the 3.4 it was first built at it
            // covered the lamp on the north wall completely - and every room in the chain has that
            // lamp, so the number carried across the move to Room1 unchanged.
            //
            // All four faces share the height even though only the north one has a lamp under it:
            // two walls are in view at once from most of this room, and a sign that changes height
            // between them reads as a mistake.
            const float y = 3.95f;

            // All four walls. In Room1 the player wakes facing 180 degrees - down the room at the
            // SOUTH wall, away from the bed - and then turns for the door in the north one, so the
            // two walls that matter here are opposite each other. Covering all four costs nothing
            // extra and removes the question. It retires against the action (see PanelMessage), so
            // the sign is not paid for once per iteration for the rest of the run.
            //
            // Each canvas's forward (+Z) points INTO its wall, i.e. away from the room. That reads
            // backwards and it is the opposite of what was built first, which came out mirrored.
            //
            // The rule: a world-space canvas is legible when its forward matches the direction the
            // viewer is LOOKING, not when it points at the viewer. Unity's own default scene is the
            // proof - camera at z = -10 looking toward +Z, canvas at the origin unrotated, text the
            // right way round. So a wall message must face the same way as the eyes reading it, and
            // a player at the middle of the room looks outwards at every one of these.
            CanvasGroup[] faces = MakeFourWallFaces(root.transform, y, standoff, FillTerminationMessage);

            PanelMessage message = root.AddComponent<PanelMessage>();
            message.faces = faces;
            message.roomCenterZ = roomCenterZ;
            message.halfDepth = halfDepth;
            return message;
        }

        // Room4 - the room past the last door, and everything in it.
        //
        // The shell is a plain white cell like the other three (BuildShell), and that is the point
        // rather than a saving: the room the player finally gets out into looks exactly like the one
        // they have been trying to get out of. What makes it the ending is that NOTHING ELSE IS IN
        // IT. One object, and it is not there when they walk in - it comes up out of the floor, the
        // only thing in the game that does, so there is nothing to look for and nowhere else to go.
        //
        // The plinth is authored in its RAISED position and sunk at runtime by FinalRoomSequence.
        // Authoring it underground instead would leave a scene whose one prop is invisible and
        // impossible to check without pressing Play.
        private static FinalRoomSequence BuildFinalRoom(Transform parent, float roomCenterZ,
                                                        Material propMat, Door doorBehind,
                                                        WallPanelDisplay wallPanels,
                                                        CameraShaker cameraShaker, PlayerHand hand,
                                                        string cubeId, string sphereId, string prismId)
        {
            GameObject root = new GameObject("FinalRoom");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = new Vector3(0f, 0f, roomCenterZ);

            // Waist height, so the plate on top is looked DOWN at from a 1.6m eye rather than
            // squared up to like the calibration wall's. The two plates are the same fixture at
            // opposite ends of the run, and the difference in how you stand over them is the only
            // thing separating "begin" from "end".
            const float plinthHeight = 1.05f;
            // WIDER THAN IT IS DEEP now, where it used to be a 1.15 square. Three recesses in a row
            // need a run of top surface, and a console you stand at the front of - sockets across the
            // back, the press at the front - is a clearer object than a square block with four things
            // crowded onto it. The depth is unchanged, so the walk round it is what it always was.
            const float plinthWidth = 1.70f;
            const float plinthDepth = 1.15f;
            const float plateProud = 0.02f;

            // The three recesses, in a row across the top. 0.46 apart spans 1.38 of the 1.55 of
            // usable plate. They used to sit in the back half with a press at the front; the press is
            // gone, so the row is centred and the console is the three things it asks for.
            const float slotPitch = 0.46f;
            const float slotSize = 0.26f;
            const float slotRowZ = 0f;

            Material plateMat = MakeColorMaterial("FinalPlate", new Color(0.05f, 0.05f, 0.055f));

            GameObject plinth = new GameObject("Plinth");
            plinth.transform.SetParent(root.transform, false);
            plinth.transform.localPosition = Vector3.zero;

            // Keeps its collider - it is a solid object in the middle of the room, and the player
            // has to walk round it to the plate. Rising through someone standing exactly on the
            // centre would shove them aside on the next frame, which is ugly but not reachable: the
            // rise starts as they clear the doorway, six metres away.
            Prim(PrimitiveType.Cube, "Body", plinth.transform,
                new Vector3(0f, plinthHeight / 2f, 0f),
                new Vector3(plinthWidth, plinthHeight, plinthDepth), propMat);

            // The dark inset the button sits in, so the top of the plinth reads as a switched-off
            // display among white surfaces - the same near-black as every groove in the building.
            Prim(PrimitiveType.Cube, "TopPlate", plinth.transform,
                new Vector3(0f, plinthHeight + plateProud / 2f, 0f),
                new Vector3(plinthWidth * 0.91f, plateProud, plinthDepth * 0.78f), plateMat,
                removeCollider: true);

            // THE THREE RECESSES, one per escape object. Built before the button so the row is the
            // first thing in the hierarchy as it is the first thing on the console.
            //
            // Shapes, not colours, are what say which is which - the same principle the cube room's
            // spec insists on - so each is cut to the silhouette of the thing it takes: a square hole,
            // a round hole, a triangular hole. The colour is carried by the rim on top of that,
            // because the three objects are distinct in both and there is no reason to spend only one.
            var slots = new FinalSlot[3];
            slots[0] = BuildFinalSlot(plinth.transform, "Slot_Cube", SlotShape.Square,
                new Vector3(-slotPitch, plinthHeight + plateProud, slotRowZ), slotSize,
                new Color(0.85f, 0.10f, 0.10f));
            slots[1] = BuildFinalSlot(plinth.transform, "Slot_Sphere", SlotShape.Round,
                new Vector3(0f, plinthHeight + plateProud, slotRowZ), slotSize,
                new Color(0.16f, 0.40f, 0.95f));
            slots[2] = BuildFinalSlot(plinth.transform, "Slot_Prism", SlotShape.Triangle,
                new Vector3(slotPitch, plinthHeight + plateProud, slotRowZ), slotSize,
                new Color(0.95f, 0.78f, 0.12f));

            // Which object each recess takes, handed in rather than written here, because two of the
            // three do not exist yet and an empty id is the honest way to say that: an undeclared slot
            // accepts nothing, prompts for nothing, and is skipped by FinalSlot.AllFilled.
            slots[0].acceptedItemId = cubeId;
            slots[1].acceptedItemId = sphereId;
            slots[2].acceptedItemId = prismId;

            // NO BUTTON. There was one here - a plate that ended the game on a single press - and
            // three recesses that each want a named object say everything it said, in a room the
            // player now has to reach with all three inside a running sixty seconds.

            // NO ERROR SIGN ON THE WALLS. It was four wall-sized canvases like Room3's message, and
            // that was the wrong instrument: a sign is something the room PUTS UP, and a facility
            // that can still put a sign up has not failed. The word lives on the panels themselves
            // now - every one of them a screen showing a test card with ERROR on it - so the room
            // does not report the fault, it IS the fault. See WallPanelDisplay.
            // THE CONSOLE IS A RewardPlinth, the same fixture the other three rooms pay out on. It
            // rises when the player reaches Room4 and sinks when the loop takes them back - which is
            // only a question now that the clock runs through this room. The old rise was a coroutine
            // fired by an event that could happen once; an iteration can end in here.
            RewardPlinth console = plinth.AddComponent<RewardPlinth>();
            console.plinth = plinth.transform;
            // Clears the floor by a hair, so nothing shows through the slab before it is meant to.
            console.riseHeight = plinthHeight + plateProud + 0.06f;
            console.riseSeconds = 2.4f;

            FinalRoomSequence sequence = root.AddComponent<FinalRoomSequence>();
            sequence.console = console;
            sequence.slots = slots;
            foreach (FinalSlot slot in slots) { slot.sequence = sequence; slot.hand = hand; }
            sequence.doorBehind = doorBehind;
            sequence.wallPanels = wallPanels;
            sequence.cameraShaker = cameraShaker;
            // TEN SECONDS from the press to the scrim starting, and that is a floor rather than a
            // pause: the door takes 1s to seal and the panels take glitchOnset 6.5s to fail across
            // the whole building. At the 3.4s this was first built at, all of that was still
            // arriving when the screen went black. The room has to be SEEN broken, or the last
            // thing the player did has no visible consequence.
            sequence.breakDuration = 10f;

            return sequence;
        }
    }
}
