using UnityEngine;
using UnityEngine.UI;

namespace IterationRoom
{
    // Top-left readout of what the player is carrying, as an icon - and the one prompt that teaches
    // putting it down again.
    //
    // ONE SLOT, since Tab went and the hand holds exactly one object. It was a row of eight, dimming
    // all but the equipped one, and every part of that readout was about a pocket that no longer
    // exists: which of several things is out, how many are left, what Tab would bring up next.
    //
    // It still earns its place with one object, for the reason it was built: carrying is state the
    // loop rewinds, and a run that resets while you believed you still had the key is unreadable
    // without a mark on screen saying you do not. It also answers the new question - hands full or
    // empty - which is now what decides what E does.
    //
    // An icon rather than a word because this is the facility's readout, not the game's subtitle -
    // the same reason the HUD is monospace and the room is built out of displays.
    public class CarriedItemsDisplay : MonoBehaviour
    {
        public PlayerHand hand;

        // Disabled rather than destroyed when empty, so a pickup costs no allocation mid-iteration.
        public Image slot;

        // The icon's own tint multiplies this. White carries "in hand" and the item's tint carries
        // which item it is - three keys share one silhouette and would otherwise be one mark.
        public Color heldColor = Color.white;

        // The put-down prompt, directly under the icon because the icon IS what it explains: it
        // appears exactly while there is something to put down. Nothing else in the game mentions
        // that E does two things, and a control the room never explains is how testers lost the
        // door button.
        public CanvasGroup dropHint;

        // Retired against drops actually PERFORMED, not against having been shown. A prompt shown
        // once has not taught anything. Retire after the first release, across items and loops.
        public int dropHintRetireAfter = 1;

        public float fadeSpeed = 6f;
        // Short of opaque, like the control discs: a label on the readout rather than the game
        // talking over it.
        public float maxAlpha = 0.85f;

        // Rebuilt only when the hand actually changes. The work is trivial, but this runs every
        // frame of a sixty-second loop and the answer is the same on almost all of them.
        private int lastVersion = -1;
        private float hintAlpha;

        // Counted here rather than on PlayerHand: it is a fact about what this prompt has taught,
        // not about what the player is carrying, and it deliberately does NOT reset at the loop
        // boundary - learning a control is not state an iteration rewinds.
        private int dropsSeen;
        private bool wasHolding;

        private void Awake()
        {
            if (dropHint != null) dropHint.alpha = 0f;
        }

        private void Update()
        {
            if (hand == null) return;

            // A drop is the hand going from full to empty. Surrendering to a socket does the same,
            // and counting that too is right rather than sloppy: both are the player demonstrating
            // they can get an object out of their hands, which is all the prompt has to teach.
            bool holding = hand.HandsFull;
            if (wasHolding && !holding) dropsSeen++;
            wasHolding = holding;

            // Outside the version gate: this fades every frame, and the item arriving is exactly the
            // frame the version check would swallow on the way back down.
            UpdateDropHint(holding);

            if (slot == null) return;
            if (hand.Version == lastVersion) return;
            lastVersion = hand.Version;

            CarryableItem item = hand.Held;
            Sprite icon = item != null ? item.icon : null;

            slot.sprite = icon;
            slot.color = item != null ? heldColor * item.iconTint : heldColor;
            slot.enabled = icon != null;
        }

        private void UpdateDropHint(bool holding)
        {
            if (dropHint == null) return;

            // Silent through the wake-up, like every other prompt: the player has no control then.
            bool running = LoopManager.Instance == null || LoopManager.Instance.AcceptsInput;
            bool wanted = running && holding && dropsSeen < dropHintRetireAfter;

            // Unscaled, so it still fades out under the pause overlay rather than freezing at
            // whatever alpha it had.
            hintAlpha = Mathf.MoveTowards(hintAlpha, wanted ? maxAlpha : 0f, fadeSpeed * Time.unscaledDeltaTime);
            dropHint.alpha = hintAlpha;
        }
    }
}
