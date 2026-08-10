using System.Collections.Generic;
using UnityEngine;

namespace IterationRoom
{
    // What the player is currently carrying. Sits on the player, with holdAnchor parented under the
    // camera so a held item rides the view - including through the wake-up, where WakeUpSequence
    // poses the camera directly.
    //
    // Carrying is world state, so the loop has to rewind it: ReturnAll() puts every item back where
    // SceneBuilder left it. Without that, the tool stays in your hand across the reset and the
    // trip to the drawer stops costing anything, which is most of what Room2's puzzle is made of.
    public class PlayerHand : MonoBehaviour
    {
        public Transform holdAnchor;

        // Ids rather than references, so callers ask "is the key carried" without needing to hold a
        // pointer to the key object. KeyLock and the balloon tool both read this.
        private readonly HashSet<string> carried = new HashSet<string>();
        private readonly List<CarryableItem> taken = new List<CarryableItem>();
        private readonly List<string> carriedNames = new List<string>();

        // In pickup order, so the readout reads as a log of what you have done this iteration
        // rather than as a sorted list. Bumped on every change so the HUD can skip rebuilding its
        // string on the frames - almost all of them - where nothing happened.
        public IReadOnlyList<string> CarriedNames => carriedNames;
        public int Version { get; private set; }

        // The one item actually shown in the hand. Small items (the key) are pocketed instead, so
        // picking one up does not knock the tool out of view.
        public CarryableItem Held { get; private set; }

        public bool Has(string itemId) => carried.Contains(itemId);

        public void Take(CarryableItem item)
        {
            if (item == null || carried.Contains(item.itemId)) return;

            carried.Add(item.itemId);
            taken.Add(item);
            carriedNames.Add(string.IsNullOrEmpty(item.displayName) ? item.itemId : item.displayName);
            Version++;

            if (item.showInHand && holdAnchor != null)
            {
                Held = item;
                item.AttachTo(holdAnchor);
            }
            else
            {
                item.Pocket();
            }
        }

        public void ReturnAll()
        {
            foreach (CarryableItem item in taken)
                if (item != null) item.ReturnToOrigin();

            taken.Clear();
            carried.Clear();
            carriedNames.Clear();
            Held = null;
            Version++;
        }
    }
}
