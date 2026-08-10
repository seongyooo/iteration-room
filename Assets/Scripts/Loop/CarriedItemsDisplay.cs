using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace IterationRoom
{
    // Top-left readout of what the player is carrying.
    //
    // It exists because carrying is the one piece of state the loop rewinds that the player cannot
    // otherwise see: the tool is visible in the hand, but the key is pocketed, and without a
    // readout "do I still have the key" is only answerable by walking to the door and trying it.
    public class CarriedItemsDisplay : MonoBehaviour
    {
        public PlayerHand hand;
        public Text label;
        public string header = "CARRYING";

        // Rebuilt only when the hand actually changes. The string work is trivial, but this runs
        // every frame of a sixty-second loop and the answer is the same on almost all of them.
        private int lastVersion = -1;
        private readonly StringBuilder builder = new StringBuilder();

        private void Update()
        {
            if (hand == null || label == null) return;
            if (hand.Version == lastVersion) return;
            lastVersion = hand.Version;

            var names = hand.CarriedNames;
            if (names.Count == 0)
            {
                // Empty rather than "CARRYING: nothing" - an empty inventory is the resting state
                // of most of an iteration, and a permanent label for it is just clutter.
                label.text = string.Empty;
                return;
            }

            builder.Length = 0;
            builder.Append(header);
            for (int i = 0; i < names.Count; i++)
                builder.Append('\n').Append("  ").Append(names[i]);

            label.text = builder.ToString();
        }
    }
}
