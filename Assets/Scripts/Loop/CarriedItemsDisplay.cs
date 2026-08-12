using UnityEngine;
using UnityEngine.UI;

namespace IterationRoom
{
    // Top-left readout of what the player is carrying, as icons - and the one prompt that teaches
    // Tab.
    //
    // It exists because carrying is the one piece of state the loop rewinds that the player cannot
    // otherwise see: the pin is visible in the hand, but the key is pocketed, and without a readout
    // "do I still have the key" is only answerable by walking to the door and trying it.
    //
    // Icons rather than words because this is the facility's readout, not the game's subtitle - the
    // same reason the HUD is monospace and the room is built out of displays. They also survive
    // being glanced at, which is all this ever gets: it is read on the move, with sixty seconds
    // running.
    public class CarriedItemsDisplay : MonoBehaviour
    {
        public PlayerHand hand;

        // A fixed pool, built by SceneBuilder and never grown. An empty slot is disabled rather
        // than destroyed, so a pickup costs no allocation mid-iteration.
        public Image[] slots;

        // The item actually in the hand is drawn at full strength and the stowed ones are dimmed.
        // Tab makes this readout answer a second question - not just "what do I have" but "what am
        // I about to use" - and a lock that refuses a key you are carrying is unreadable without it.
        public Color heldColor = Color.white;
        public Color stowedColor = new Color(1f, 1f, 1f, 0.33f);

        // The Tab prompt, sitting directly under the icon row because the row IS what it explains:
        // one icon bright and the rest dimmed only means something once you know what moves the
        // brightness. Nothing else in the game mentions Tab, and a control the room never explains
        // is exactly how testers lost the door button.
        public CanvasGroup swapHint;

        // Retired against swaps actually PERFORMED, not against having been displayed. A prompt
        // shown once has not taught anything - but one that nags for the rest of a sixty-second
        // loop becomes the most repeated thing on screen. Three is "they have done it on purpose".
        public int swapHintRetireAfter = 3;

        public float fadeSpeed = 6f;
        // Short of opaque, like the control discs: a label on the readout rather than the game
        // talking over it.
        public float maxAlpha = 0.85f;

        // Rebuilt only when the hand actually changes. The work is trivial, but this runs every
        // frame of a sixty-second loop and the answer is the same on almost all of them.
        // PlayerHand bumps Version on equip as well as on pickup, so a Tab press lands here.
        private int lastVersion = -1;
        private float swapAlpha;

        private void Awake()
        {
            if (swapHint != null) swapHint.alpha = 0f;
        }

        private void Update()
        {
            if (hand == null) return;

            // Outside the version gate: this fades every frame, and the second item arriving is
            // exactly the frame the version check would swallow on the way back down.
            UpdateSwapHint();

            if (slots == null) return;
            if (hand.Version == lastVersion) return;
            lastVersion = hand.Version;

            var items = hand.CarriedItems;
            for (int i = 0; i < slots.Length; i++)
            {
                Image slot = slots[i];
                if (slot == null) continue;

                CarryableItem item = i < items.Count ? items[i] : null;
                Sprite icon = item != null ? item.icon : null;

                slot.sprite = icon;
                // MULTIPLIED, not replaced. heldColor/stowedColor carry the one thing this row exists
                // to say - which item Tab has out - and an item's own tint carries which item it is.
                // Either alone loses half the readout: three keys tinted but not dimmed cannot show
                // what is in the hand, and dimmed but untinted cannot show which key is which.
                Color state = item != null && item == hand.Held ? heldColor : stowedColor;
                slot.color = item != null ? state * item.iconTint : state;
                // Disabled rather than made transparent: an empty inventory is the resting state of
                // most of an iteration, and it should cost nothing to draw.
                slot.enabled = icon != null;
            }
        }

        // Shown the moment a second item lands in the pocket, which is the first moment Tab does
        // anything at all - with one item the cycle is only "put it away", and teaching it there
        // would teach a control the player has no reason for yet.
        private void UpdateSwapHint()
        {
            if (swapHint == null) return;

            // Silent through the wake-up, like every other prompt: the player has no control then.
            bool running = LoopManager.Instance == null || LoopManager.Instance.AcceptsInput;
            bool wanted = running
                       && hand.CarriedItems.Count >= 2
                       && hand.CycleCount < swapHintRetireAfter;

            // Unscaled, so it still fades out under the pause overlay rather than freezing at
            // whatever alpha it had.
            swapAlpha = Mathf.MoveTowards(swapAlpha, wanted ? maxAlpha : 0f, fadeSpeed * Time.unscaledDeltaTime);
            swapHint.alpha = swapAlpha;
        }
    }
}
