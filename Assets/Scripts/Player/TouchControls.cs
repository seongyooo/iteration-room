using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace IterationRoom
{
    // THE SCREEN, READ AS A CONTROLLER: a thumb stick on the left, a look area on the right, and a
    // handful of round buttons. Answers `GameInput`, which is the only thing that talks to it.
    //
    // **RAW TOUCHES, NOT uGUI BUTTONS.** A Button's onClick arrives from the EventSystem part-way
    // through the frame, so a press would be seen by whichever fixtures happened to update after it
    // and missed by the rest - and every one of this game's eight E fixtures polls for itself.
    // Hit-testing circles here and sampling once per frame (see GameInput.Pump) is what makes the
    // answer the same for all of them. The visuals are therefore non-interactive images sitting at
    // the same coordinates; they are drawn FROM these numbers rather than the numbers being read
    // off them.
    //
    // EVERY POSITION IS NORMALISED - centres as a fraction of the screen, radii as a fraction of its
    // HEIGHT so a button stays circular and a thumb-sized target stays thumb-sized on any aspect.
    // The numbers themselves live in `SceneBuilder` like every other tuned value (CLAUDE.md §2).
    public class TouchControls : MonoBehaviour
    {
        // One on-screen button: where it is, how big, and what it is doing right now.
        [System.Serializable]
        public class TouchButton
        {
            public string label;
            // Normalised screen position, (0,0) bottom-left.
            public Vector2 center;
            // Fraction of SCREEN HEIGHT, so it is a circle rather than an ellipse.
            public float radius = 0.07f;
            public RectTransform visual;

            [System.NonSerialized] public int finger = -1;
            [System.NonSerialized] public int pressedFrame = -1;

            public bool PressedThisFrame => pressedFrame == Time.frameCount;
            public bool Held => finger != -1;
        }

        // ON A PHONE, or forced on for testing. `Application.isMobilePlatform` is true in a mobile
        // browser as well as in a native build, which is the point - the WebGL build is the first
        // delivery and it has to answer the same way.
        public bool forceOn;

        public CanvasGroup group;
        public RectTransform stickBase;
        public RectTransform stickKnob;

        // Where a thumb may plant the stick. It appears WHERE THE FINGER LANDS inside this box
        // rather than at a fixed spot - a fixed stick has to be found by looking down, and this game
        // is asking the player to watch a clock.
        public Rect stickZone = new Rect(0f, 0f, 0.45f, 0.7f);
        // How far from that origin counts as full deflection, as a fraction of screen height.
        public float stickRadius = 0.11f;
        // Below this the stick reads as nothing at all, so a resting thumb does not creep.
        public float stickDeadZone = 0.14f;

        // PUSH PAST THIS AND IT IS A RUN. Two numbers rather than one because a single threshold
        // sitting exactly under the thumb flickers between walk and sprint - the same hysteresis
        // `GhostReplayer` puts on its walk animation, for the same reason.
        public float sprintEnter = 0.86f;
        public float sprintExit = 0.74f;

        // Degrees of turn for a drag across the full height of the screen, before the player's own
        // sensitivity setting multiplies it. Expressed this way so it means the same thing on any
        // display.
        public float lookTurnPerHeight = 170f;

        public TouchButton interact = new TouchButton { label = "E" };
        public TouchButton use = new TouchButton { label = "USE" };
        public TouchButton jump = new TouchButton { label = "JUMP" };
        public TouchButton pause = new TouchButton { label = "II" };
        // **HELD, NOT PRESSED** - `EndCycleControl` charges a gauge while it is down, and this is the
        // one touch control that is read through `Held` rather than `PressedThisFrame`.
        //
        // It is here at all because the desktop path does NOT carry over. `EndCycleControl` has its
        // own pointer handlers, so a thumb on that box already worked - but the box is invisible
        // until the control is down, which on a keyboard is fine (press N and it appears) and on a
        // phone means hunting for something that cannot be seen. There is no key to press.
        public TouchButton endIteration = new TouchButton { label = "END" };
        // NO "END ITERATION" BUTTON, and that is not an omission. `EndCycleControl` is already a
        // uGUI element implementing IPointerDown/Up, so a thumb held on it works through the
        // EventSystem exactly as a mouse does - it needed nothing. What it DID need is the guard in
        // `OverInteractiveUI` below, or the same touch would have turned the camera as well.

        public Vector2 Move { get; private set; }
        public Vector2 Look { get; private set; }
        public bool Sprint { get; private set; }

        public bool JumpPressed => jump.PressedThisFrame;
        public bool InteractPressed => interact.PressedThisFrame;
        public bool UsePressed => use.PressedThisFrame;
        public bool PausePressed => pause.PressedThisFrame;
        public bool EndIterationHeld => endIteration.Held;

        private static readonly List<RaycastResult> uiProbe = new List<RaycastResult>();

        private TouchButton[] buttons;
        private int stickFinger = -1;
        private Vector2 stickOrigin;
        private int lookFinger = -1;
        private Vector2 lookLast;
        private float alpha;

        // THIS IS A TOUCH DEVICE - a question about the HARDWARE, with nothing about the loop in it.
        //
        // Split from `Active` below because the two are wanted at different moments. `Active` is
        // "the controls are live right now", which is false whenever the game is not taking input;
        // this one has to be answerable BEFORE the loop has started at all, because the sensitivity
        // step runs there and has to be skipped on a phone (LoopManager.Start). Asking `Active` at
        // that point is circular: it says no precisely because the thing it gates has not begun.
        public bool Present => isActiveAndEnabled && (forceOn || Application.isMobilePlatform);

        // UP UNLESS THE GAME IS PAUSED, and the gate is PAUSED rather than `AcceptsInput` on
        // purpose. `AcceptsInput` is also false while the run is ending and while a cycle boundary
        // is breaking - and the player keeps control through both of those (see FinalRoomSequence:
        // "the player keeps control until the scrim"). Gated on it, a phone lost its stick and its
        // look for the whole of the ending, which is a walk the player is supposed to take.
        //
        // Nothing is let through early by loosening it: every fixture in the game already tests
        // `AcceptsInput` for itself before acting (CLAUDE.md §1.8), and movement answers to
        // `FirstPersonController.ControlEnabled`. What this decides is only whether the SCREEN is
        // being read - and while the pause menu is up it must not be, because that menu is ordinary
        // uGUI and wants the EventSystem's own touch handling.
        public bool Active =>
            Present && (LoopManager.Instance == null || !LoopManager.Instance.IsPaused);

        private void Awake()
        {
            buttons = new[] { interact, use, jump, pause, endIteration };
        }

        private void OnEnable() => GameInput.Register(this);

        private void OnDisable()
        {
            GameInput.Unregister(this);
            Clear();
        }

        // A backstop for a frame on which nothing reads `GameInput`: an edge must not survive into
        // the next frame just because nobody was listening. Whichever runs first does the work.
        private void Update()
        {
            GameInput.Pump();
            Draw();
        }

        // ONE READING PER FRAME, taken by whoever asks first. See GameInput.Pump.
        public void Sample()
        {
            if (!Active) { Clear(); return; }

            // Cleared every frame rather than decayed: a look delta is what happened SINCE the last
            // reading, so a finger that stopped moving is contributing nothing.
            Look = Vector2.zero;

            foreach (Finger f in Fingers())
            {
                if (f.began) Begin(f);
                else if (f.ended) End(f);
                else Continue(f);
            }

            if (stickFinger == -1) { Move = Vector2.zero; Sprint = false; }
        }

        private void Begin(Finger f)
        {
            // A CONTROL THE EVENTSYSTEM OWNS TAKES IT INSTEAD, and this game has one that matters:
            // `EndCycleControl` is held down to end an iteration early, which a practised run does
            // ten times. Without this the same thumb would drive the look as well, so ending an
            // iteration would spin the room.
            if (OverInteractiveUI(f.position)) return;

            // BUTTONS FIRST, so a button sitting inside the look area is a button. They do not
            // overlap each other, and the first one hit takes the finger.
            foreach (TouchButton b in buttons)
            {
                if (b.finger != -1 || !Inside(b, f.position)) continue;
                b.finger = f.id;
                b.pressedFrame = Time.frameCount;
                return;
            }

            Vector2 n = Normalised(f.position);

            // THE STICK APPEARS UNDER THE THUMB, wherever in its box the thumb landed.
            if (stickFinger == -1 && stickZone.Contains(n))
            {
                stickFinger = f.id;
                stickOrigin = f.position;
                Move = Vector2.zero;
                return;
            }

            // Everything else is looking. Deliberately the fallback rather than a box of its own:
            // any part of the screen not already spoken for turns the view, which is one less thing
            // for the player to find.
            if (lookFinger == -1)
            {
                lookFinger = f.id;
                lookLast = f.position;
            }
        }

        private void Continue(Finger f)
        {
            if (f.id == stickFinger)
            {
                float reach = Mathf.Max(1f, stickRadius * Screen.height);
                Vector2 pull = (f.position - stickOrigin) / reach;
                float mag = pull.magnitude;

                if (mag < stickDeadZone) { Move = Vector2.zero; Sprint = false; return; }

                float scaled = Mathf.Clamp01(mag);
                Move = (pull / mag) * scaled;
                // Hysteresis: a firm push to start running, a real let-up to stop.
                Sprint = scaled >= (Sprint ? sprintExit : sprintEnter);
                return;
            }

            if (f.id == lookFinger)
            {
                Vector2 delta = f.position - lookLast;
                lookLast = f.position;
                // Pixels into the same units `GetAxis("Mouse X")` hands over: degrees before the
                // player's own sensitivity multiplies them.
                Look = delta / Mathf.Max(1f, Screen.height) * lookTurnPerHeight;
            }
        }

        private void End(Finger f)
        {
            if (f.id == stickFinger) { stickFinger = -1; Move = Vector2.zero; Sprint = false; }
            if (f.id == lookFinger) lookFinger = -1;
            foreach (TouchButton b in buttons)
                if (b.finger == f.id) b.finger = -1;
        }

        private void Clear()
        {
            Move = Vector2.zero;
            Look = Vector2.zero;
            Sprint = false;
            stickFinger = -1;
            lookFinger = -1;
            if (buttons == null) return;
            foreach (TouchButton b in buttons) b.finger = -1;
        }

        // IS SOMETHING THAT WANTS PRESSES UNDER THIS FINGER? Not merely "is there any UI here" -
        // the HUD is full of images that are raycast targets and none of them wants a touch, so
        // treating those as blockers would leave most of the screen unable to turn the view. Only a
        // handler that actually takes a pointer counts.
        private bool OverInteractiveUI(Vector2 pixel)
        {
            EventSystem events = EventSystem.current;
            if (events == null) return false;

            uiProbe.Clear();
            events.RaycastAll(new PointerEventData(events) { position = pixel }, uiProbe);

            foreach (RaycastResult hit in uiProbe)
            {
                if (hit.gameObject == null) continue;
                if (hit.gameObject.GetComponentInParent<IPointerDownHandler>() != null) return true;
                if (hit.gameObject.GetComponentInParent<Selectable>() != null) return true;
            }
            return false;
        }

        private bool Inside(TouchButton b, Vector2 pixel)
        {
            Vector2 centre = new Vector2(b.center.x * Screen.width, b.center.y * Screen.height);
            float r = b.radius * Screen.height;
            return (pixel - centre).sqrMagnitude <= r * r;
        }

        // THE VISUALS ARE PLACED FROM THE SAME NUMBERS THE HIT TEST USES, every frame, rather than
        // being positioned once by the builder. Two copies of a button's position is two things that
        // can disagree, and the one thing this game refuses to ship is a mark in one place and the
        // press it describes in another (see ControlHintDisplay, ItemRegistry.AimedTakeable).
        //
        // Cheap enough to do unconditionally: four rects, and it also means a rotated phone or a
        // resized browser window needs no notification to follow.
        private void LayOut()
        {
            RectTransform area = transform as RectTransform;
            if (area == null || buttons == null) return;

            Vector2 canvas = area.rect.size;
            if (canvas.x < 1f || canvas.y < 1f) return;

            foreach (TouchButton b in buttons)
            {
                if (b.visual == null) continue;
                float d = b.radius * 2f * canvas.y;
                b.visual.sizeDelta = new Vector2(d, d);
                b.visual.anchoredPosition =
                    new Vector2((b.center.x - 0.5f) * canvas.x, (b.center.y - 0.5f) * canvas.y);
                // Pressed reads as pressed. The only feedback a finger gets on a screen it is
                // covering with itself.
                b.visual.localScale = Vector3.one * (b.Held ? 0.88f : 1f);
            }

            if (stickBase != null)
            {
                float d = stickRadius * 2f * canvas.y;
                stickBase.sizeDelta = new Vector2(d, d);
            }
            if (stickKnob != null)
            {
                float d = stickRadius * 0.82f * canvas.y;
                stickKnob.sizeDelta = new Vector2(d, d);
            }
        }

        private Vector2 Normalised(Vector2 pixel) =>
            new Vector2(pixel.x / Mathf.Max(1f, Screen.width), pixel.y / Mathf.Max(1f, Screen.height));

        // The stick follows the thumb, and everything fades with the controls. Its own job rather
        // than the sampler's: this is what the player SEES, not what the game reads.
        private void Draw()
        {
            bool on = Active;
            alpha = Mathf.MoveTowards(alpha, on ? 1f : 0f, Time.unscaledDeltaTime * 6f);
            if (group != null) group.alpha = alpha;

            LayOut();

            if (stickBase == null || stickKnob == null) return;

            bool held = stickFinger != -1;
            if (stickBase.gameObject.activeSelf != (on && held))
                stickBase.gameObject.SetActive(on && held);
            if (stickKnob.gameObject.activeSelf != (on && held))
                stickKnob.gameObject.SetActive(on && held);
            if (!held) return;

            // Screen pixels into canvas space. The canvas is ScreenSpaceOverlay, so its rect maps
            // one-to-one onto the screen once the scaler's own factor is undone.
            float scale = stickBase.parent != null ? stickBase.parent.lossyScale.x : 1f;
            if (Mathf.Abs(scale) < 0.0001f) scale = 1f;

            Vector2 half = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            stickBase.anchoredPosition = (stickOrigin - half) / scale;
            stickKnob.anchoredPosition =
                (stickOrigin + Move * (stickRadius * Screen.height) - half) / scale;
        }

        // Unity's touches, with the mouse standing in for one when the controls are forced on at a
        // desk. Testing a thumb layout should not need a phone in hand for every change.
        private struct Finger
        {
            public int id;
            public Vector2 position;
            public bool began;
            public bool ended;
        }

        private IEnumerable<Finger> Fingers()
        {
            if (Input.touchCount > 0)
            {
                foreach (Touch t in Input.touches)
                    yield return new Finger
                    {
                        id = t.fingerId,
                        position = t.position,
                        began = t.phase == TouchPhase.Began,
                        ended = t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled,
                    };
                yield break;
            }

            if (!forceOn || !Input.mousePresent) yield break;
            if (!Input.GetMouseButton(0) && !Input.GetMouseButtonUp(0)) yield break;

            yield return new Finger
            {
                id = -99,
                position = Input.mousePosition,
                began = Input.GetMouseButtonDown(0),
                ended = Input.GetMouseButtonUp(0),
            };
        }
    }
}
