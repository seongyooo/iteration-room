using UnityEngine;
using UnityEngine.UI;

namespace IterationRoom
{
    // The room teaches its two controls once each and then never mentions them again.
    //
    // A grey disc with a key glyph in it floats over whatever the player has walked up to, and a
    // second one sits on the pin the moment it is in hand. Each retires permanently the first time
    // the player performs the action it describes - not the first time it is displayed, so walking
    // past the nightstand without opening it does not burn the lesson.
    //
    // Retiring on the action rather than on a timer matters more here than it would in most games:
    // the loop's whole texture is repetition, and an instruction that replays every sixty seconds
    // would become the most repeated thing in the prototype. The flags are plain fields rather than
    // PlayerPrefs, so they last for a session of play mode and reset when you press Play again -
    // which is what you want while the thing is still being tested.
    public class ControlHintDisplay : MonoBehaviour
    {
        public Camera playerCamera;
        public PlayerHand hand;

        // Everything E does something to. Typed as MonoBehaviour rather than IInteractHintTarget
        // because Unity does not serialize interface fields - the cast back happens once in Awake.
        // SceneBuilder fills this the same way it fills LoopManager.ghostInteractables.
        public MonoBehaviour[] interactTargets;

        // The item whose arrival in the hand introduces the mouse button.
        public string swingItemId = "Tool";

        // The rect the screen positions are resolved against: a full-screen child of the canvas,
        // which is also both hints' parent, so a local point in it is an anchoredPosition.
        public RectTransform area;

        public CanvasGroup interactGroup;
        public RectTransform interactRect;
        public CanvasGroup swingGroup;
        public RectTransform swingRect;

        public float fadeSpeed = 6f;

        private IInteractHintTarget[] targets;
        private bool interactRetired;
        private bool swingRetired;
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
            bool running = LoopManager.Instance == null || LoopManager.Instance.IterationRunning;

            Transform interactAnchor = !interactRetired && running ? NearestWantingHint() : null;
            Show(interactGroup, interactRect, interactAnchor, ref interactAlpha);
            // Retired only while it is actually up, so a press made somewhere else in the room -
            // long before the player has ever seen this - does not silently spend the lesson.
            if (interactAnchor != null && Input.GetKeyDown(KeyCode.E)) interactRetired = true;

            Transform swingAnchor = !swingRetired && running ? SwingAnchor() : null;
            Show(swingGroup, swingRect, swingAnchor, ref swingAlpha);
            if (swingAnchor != null && Input.GetMouseButtonDown(0)) swingRetired = true;
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

        // Hung on the tool itself rather than on a balloon: this control is about the thing in your
        // hand, and the pin rides the view, so the prompt sits on it wherever the player looks.
        private Transform SwingAnchor()
        {
            if (hand == null) return null;

            CarryableItem held = hand.Held;
            return held != null && held.itemId == swingItemId ? held.transform : null;
        }

        private void Show(CanvasGroup group, RectTransform rect, Transform anchor, ref float alpha)
        {
            if (group == null || rect == null) return;

            bool visible = anchor != null && Place(rect, anchor);
            alpha = Mathf.MoveTowards(alpha, visible ? 1f : 0f, fadeSpeed * Time.deltaTime);
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
