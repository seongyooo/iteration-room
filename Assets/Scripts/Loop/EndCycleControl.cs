using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace IterationRoom
{
    // Lets the player end the current cycle before the clock runs out. 60 seconds is a ceiling, not
    // a quota - see LoopManager.EndCycleEarly for what that costs.
    //
    // Hold to commit rather than press: this is irreversible and it truncates the recording the
    // next ghost is made from, so it should not be possible to fire it by brushing a key.
    //
    // It answers to a key as well as a click, and the key is what actually gets used:
    // FirstPersonController locks and hides the cursor, so clicking means freeing it first and then
    // watching the view spin as the mouse drags to the corner.
    public class EndCycleControl : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        public KeyCode hotkey = KeyCode.N;
        public float holdDuration = 0.6f;

        public Image fill;
        public CanvasGroup group;
        // Dimmed whenever the clock isn't running, so the control reads as unavailable during the
        // wake-up rather than looking broken.
        public float unavailableAlpha = 0.25f;

        private bool pointerHeld;
        private float held;
        // Cleared on commit and only restored on release, so holding the key down through the
        // blackout doesn't immediately end the next cycle too.
        private bool armed = true;

        // How many times the player has actually ended a cycle themselves. Read by PanelMessage,
        // which retires the wall sign teaching this control once it has been USED - the same rule
        // the Tab hint uses (PlayerHand.CycleCount) and the same one the left-click prompt settled
        // on. A sign that has been seen has taught nothing; one whose action has been performed has
        // nothing left to say.
        //
        // Deliberately NOT reset at the loop boundary: learning a control is not state an iteration
        // rewinds.
        public int UseCount { get; private set; }

        public void OnPointerDown(PointerEventData eventData) => pointerHeld = true;
        public void OnPointerUp(PointerEventData eventData) => pointerHeld = false;

        private void Update()
        {
            bool available = LoopManager.Instance != null && LoopManager.Instance.AcceptsInput;
            bool down = pointerHeld || Input.GetKey(hotkey);

            if (!down)
            {
                armed = true;
                held = 0f;
            }
            else if (armed && available)
            {
                held += Time.deltaTime;
                if (held >= holdDuration)
                {
                    LoopManager.Instance.EndCycleEarly();
                    UseCount++;
                    armed = false;
                    held = 0f;
                }
            }

            if (fill != null) fill.fillAmount = holdDuration > 0f ? Mathf.Clamp01(held / holdDuration) : 0f;
            if (group != null) group.alpha = available ? 1f : unavailableAlpha;
        }
    }
}
