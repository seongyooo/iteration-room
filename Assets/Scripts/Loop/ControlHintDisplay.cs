using UnityEngine;
using UnityEngine.UI;

namespace IterationRoom
{
    // A grey disc with a key glyph in it floats over whatever interactable the player has walked up
    // to, and a second one carrying a mouse button floats over the balloon a swing would burst.
    //
    // These used to retire permanently the first time the player performed the action each one
    // described - the reasoning being that the loop's whole texture is repetition, so an
    // instruction replaying every sixty seconds would become the most repeated thing in the
    // prototype. Play-testing overruled it, and decisively: testers never found the door button at
    // all, and the ones who did could not find it again an iteration later. A prompt that has been
    // shown once has not been taught, and a control the player cannot find is worse than a control
    // they are reminded of.
    //
    // What survives of the old reasoning is the restraint: the prompt still appears only when E
    // would actually do something here (WantsInteractHint, not mere proximity), only over the
    // nearest such thing, and at maxAlpha rather than full - so it reads as a label on the fixture
    // rather than as the game talking.
    //
    // The SWING prompt is the one exception, and retires for good after the first pop. That is not a
    // return of what play-testing overruled: the finding was that showing a prompt ONCE does not
    // teach, and this one stays up until the player has performed the action, not until they have
    // seen it. Nobody forgets which button they just clicked.
    public class ControlHintDisplay : MonoBehaviour
    {
        public Camera playerCamera;

        // Everything E does something to. Typed as MonoBehaviour rather than IInteractHintTarget
        // because Unity does not serialize interface fields - the cast back happens once in Awake.
        // SceneBuilder fills this the same way it fills LoopManager.ghostInteractables.
        public MonoBehaviour[] interactTargets;

        // The calibration step runs before the loop does, so AcceptsInput is false for all of it -
        // but the start button on that room's wall is an IInteractHintTarget and wants this prompt.
        // Letting it through here rather than giving that step a prompt of its own means the player
        // meets the game's own disc, in the game's own position, before the game.
        public SensitivityCalibration calibration;

        // And the same again at the other end of the run: the final room's plate is live while
        // AcceptsInput is false, because the loop has already stopped. Without this the one
        // fixture the last room has would have no prompt over it.
        public FinalRoomSequence finalRoom;

        // The swing itself. Asked rather than re-derived: it owns both halves of this prompt's rule -
        // whether the click is live, and which balloon it would burst.
        public BalloonTool swingTool;

        // The rect the screen positions are resolved against: a full-screen child of the canvas,
        // which is also both hints' parent, so a local point in it is an anchoredPosition.
        public RectTransform area;

        public CanvasGroup interactGroup;
        public RectTransform interactRect;
        public CanvasGroup swingGroup;
        public RectTransform swingRect;

        public float fadeSpeed = 6f;
        // Held short of opaque now that these are permanent rather than one-time. At full alpha a
        // prompt that is always there reads as part of the HUD; at 0.75 it reads as something
        // stencilled on the fixture.
        public float maxAlpha = 0.75f;

        private IInteractHintTarget[] targets;
        private float interactAlpha;
        private float swingAlpha;

        private void Awake()
        {
            int count = interactTargets != null ? interactTargets.Length : 0;
            targets = new IInteractHintTarget[count];
            for (int i = 0; i < count; i++) targets[i] = interactTargets[i] as IInteractHintTarget;

            if (interactGroup != null) interactGroup.alpha = 0f;
            if (swingGroup != null) swingGroup.alpha = 0f;
        }

        private void Update()
        {
            // Silent through the wake-up, like everything else the facility does: the player has no
            // control then, so a prompt would be describing a button that does nothing.
            bool running = LoopManager.Instance == null || LoopManager.Instance.AcceptsInput
                        || (calibration != null && calibration.Active)
                        || (finalRoom != null && finalRoom.Active);

            Show(interactGroup, interactRect, running ? NearestWantingHint() : null, ref interactAlpha);
            Show(swingGroup, swingRect, running ? SwingAnchor() : null, ref swingAlpha);
        }

        // Nearest rather than first, so standing between the drawer and the pin inside it prompts
        // over the one being looked at rather than over whichever was built first.
        private Transform NearestWantingHint()
        {
            if (targets == null || playerCamera == null) return null;

            Transform best = null;
            float bestSqr = float.MaxValue;
            Vector3 eye = playerCamera.transform.position;

            foreach (IInteractHintTarget target in targets)
            {
                if (target == null || !target.WantsInteractHint) continue;

                Transform anchor = target.HintAnchor;
                if (anchor == null) continue;

                float sqr = (anchor.position - eye).sqrMagnitude;
                if (sqr >= bestSqr) continue;
                bestSqr = sqr;
                best = anchor;
            }

            return best;
        }

        // On the BALLOON, not on the pin. Hung on the held tool this rode the view, so it was up the
        // whole time the pin was in hand and sat on the one thing the player was not being asked to
        // click - which made it a HUD element describing a control rather than a label on what the
        // control acts on. Anchored to the balloon a swing would actually burst, it appears only when
        // clicking would do something and points at what that something is.
        private Transform SwingAnchor()
        {
            if (swingTool == null || !swingTool.WantsSwingHint) return null;

            Balloon target = swingTool.FindTarget();
            return target != null ? target.transform : null;
        }

        private void Show(CanvasGroup group, RectTransform rect, Transform anchor, ref float alpha)
        {
            if (group == null || rect == null) return;

            bool visible = anchor != null && Place(rect, anchor);
            // Unscaled, so the prompt still fades away when the pause menu freezes the game. On
            // scaled time it would sit there at whatever alpha it had, frozen under the overlay.
            alpha = Mathf.MoveTowards(alpha, visible ? maxAlpha : 0f, fadeSpeed * Time.unscaledDeltaTime);
            group.alpha = alpha;
        }

        // False when the anchor is behind the camera: WorldToScreenPoint happily returns a mirrored
        // on-screen position for anything behind the eye, so without the z test a prompt for
        // something at your back appears in front of you.
        private bool Place(RectTransform rect, Transform anchor)
        {
            if (playerCamera == null || area == null) return false;

            Vector3 screenPoint = playerCamera.WorldToScreenPoint(anchor.position);
            if (screenPoint.z <= 0f) return false;

            // Null camera: the canvas is ScreenSpaceOverlay, where screen points are already in the
            // canvas's own space and passing a camera would skew the result.
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(area, screenPoint, null, out Vector2 local))
                return false;

            rect.anchoredPosition = local;
            return true;
        }
    }
}
