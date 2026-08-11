using UnityEngine;
using UnityEngine.UI;

namespace IterationRoom
{
    // Top-left readout of what the player is carrying, as icons.
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

        // Rebuilt only when the hand actually changes. The work is trivial, but this runs every
        // frame of a sixty-second loop and the answer is the same on almost all of them.
        // PlayerHand bumps Version on equip as well as on pickup, so a Tab press lands here.
        private int lastVersion = -1;

        private void Update()
        {
            if (hand == null || slots == null) return;
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
                slot.color = item != null && item == hand.Held ? heldColor : stowedColor;
                // Disabled rather than made transparent: an empty inventory is the resting state of
                // most of an iteration, and it should cost nothing to draw.
                slot.enabled = icon != null;
            }
        }
    }
}
