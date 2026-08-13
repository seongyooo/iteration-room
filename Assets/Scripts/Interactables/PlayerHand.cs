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

        // Where takes, surrenders and equips are written so a past self can repeat them. Every
        // carryable is recorded; whether a ghost may act on one is decided at REPLAY, by
        // CarryableItem.ghostCarryable - see docs/ghost-possession-design.md.
        public PlayerRecorder recorder;

        // Ids rather than references, so callers ask "is the key carried" without needing to hold a
        // pointer to the key object. KeyLock and the balloon tool both read this.
        private readonly HashSet<string> carried = new HashSet<string>();
        // Everything picked up this iteration, including items since given up. ReturnAll works off
        // this rather than off `carriedItems`, so a key surrendered to a lock is still put back
        // where SceneBuilder left it - it must not survive the reset sitting in the keyhole.
        private readonly List<CarryableItem> taken = new List<CarryableItem>();
        private readonly List<CarryableItem> carriedItems = new List<CarryableItem>();

        // In pickup order, so the readout reads as a log of what you have done this iteration
        // rather than as a sorted list. Bumped on every change so the HUD can skip rebuilding
        // itself on the frames - almost all of them - where nothing happened.
        public IReadOnlyList<CarryableItem> CarriedItems => carriedItems;
        public int Version { get; private set; }

        // THE ONE ITEM IN THE HAND. Everything else carried is stowed and invisible, and TAB cycles
        // which one is out - including an empty-handed slot at the end of the cycle.
        //
        // This replaces a fixed `showInHand` flag per item, where the pin was always in view and the
        // key was always pocketed. The flag existed for one reason: picking the key up must not
        // knock the tool out of the hand you needed to get it with. Tab answers that directly, and
        // makes the key a thing you handle rather than a fact about your inventory - you take it
        // out, and then you put it in the lock.
        public CarryableItem Held { get; private set; }

        // Carried at all, in hand or stowed. Deliberately still the loose test: the HUD readout and
        // the loop's rewind both care about what you have, not about what is out.
        public bool Has(string itemId) => carried.Contains(itemId);

        // In hand RIGHT NOW. This is what a fixture that must be OPERATED with the item asks for -
        // KeyLock and BalloonTool - because the condition that enables the action is the object
        // being in your hand, not somewhere on your person.
        public bool Holding(string itemId) => Held != null && Held.itemId == itemId;

        public void Take(CarryableItem item)
        {
            if (item == null || carried.Contains(item.itemId)) return;

            // Taking it off a past self. The ghost has to be told, or it would keep the item in its
            // own held list and try to surrender it later - and there is exactly one of each object,
            // so two holders is the one state this design does not allow.
            item.HeldByGhost?.ReleaseItem(item, toWorld: false);

            carried.Add(item.itemId);
            // Added once per iteration even if this is the second time it has passed through the
            // player's hands, because `taken` is what ReturnAll walks - a duplicate would just
            // return the same object twice.
            if (!taken.Contains(item)) taken.Add(item);
            carriedItems.Add(item);
            recorder?.RecordCarry(item.itemId, CarryKind.Take, item.name);
            Version++;

            // Straight into the hand. You just picked it up, so having to press Tab to see what you
            // are holding would be a step the room never explains - and it keeps the pin's old
            // behaviour exactly, which is the one the play-test was run against.
            Equip(item);
        }

        // Tab. Cycles through what you are carrying and then through EMPTY HANDS, which is the slot
        // that makes it a cycle rather than a toggle - and is the only way to put the pin away
        // without dropping it.
        private void Update()
        {
            if (LoopManager.Instance != null && !LoopManager.Instance.AcceptsInput) return;
            if (Input.GetKeyDown(KeyCode.Tab)) CycleHeld();
        }

        // How many times the player has actually swapped, ever - not reset at the loop boundary,
        // because learning a control is not state the iteration rewinds. The swap prompt retires
        // against this rather than against having been shown: "shown once" did not teach the door
        // button either. See CarriedItemsDisplay.
        public int CycleCount { get; private set; }

        public void CycleHeld()
        {
            if (carriedItems.Count == 0) return;

            CycleCount++;

            int current = Held == null ? carriedItems.Count : carriedItems.IndexOf(Held);
            // count is the empty-hands slot: index count-1 -> count (empty) -> wraps to 0.
            int next = current + 1 > carriedItems.Count ? 0 : current + 1;
            Equip(next < carriedItems.Count ? carriedItems[next] : null);
        }

        public void Equip(CarryableItem item)
        {
            if (Held == item) return;
            // Stowed, not dropped: it stays carried and stays in `taken`, it is simply not out.
            if (Held != null) Held.Pocket();

            Held = item;
            if (item != null && holdAnchor != null) item.AttachTo(holdAnchor);
            else if (item != null) item.Pocket();

            // Recorded like a take, so a past self swaps to the key at the same moment you did -
            // and so a ghost's unlock can be re-evaluated against the same condition the player's
            // was: the key in the hand, not merely on the person.
            recorder?.RecordCarry(item != null ? item.itemId : string.Empty, CarryKind.Equip);
            Version++;
        }

        // Hands an item over for good - the key going into Room2's lock. It stops counting as
        // carried (so Has() is false and the HUD drops its icon) but stays in `taken`, because the
        // loop still has to put it back at the top of the next iteration.
        //
        // Returns the item so the caller can place it; nobody else knows where it should go.
        public CarryableItem Surrender(string itemId)
        {
            if (!carried.Remove(itemId)) return null;

            CarryableItem given = null;
            int slot = -1;
            for (int i = carriedItems.Count - 1; i >= 0; i--)
            {
                if (carriedItems[i] == null || carriedItems[i].itemId != itemId) continue;
                given = carriedItems[i];
                slot = i;
                carriedItems.RemoveAt(i);
                break;
            }

            // Out of the hand, so the hand is not left holding a ghost of it. The null check is not
            // decoration: `carried` and `carriedItems` can disagree in principle, and without it an
            // id that was in the set but had no object would compare null==null, read as "the held
            // item just left", and auto-advance a hand that was empty the whole time.
            bool wasHeld = given != null && Held == given;
            if (wasHeld) Held = null;

            // Recorded AFTER the item is confirmed gone from the hand, so a surrender is only ever
            // written for one that was genuinely held. RecordedTimeline.Delivers reads these to
            // decide whether a ghost repeats the pickup at all.
            if (given != null) recorder?.RecordCarry(itemId, CarryKind.Surrender);

            // AND THEN THE HAND ADVANCES TO WHATEVER IS LEFT, rather than to empty.
            //
            // This reverses an earlier call, which was that having the pin appear unasked is the
            // game deciding what the player meant. What that missed is how the surrender usually
            // arrives: placing is a repeated errand - twelve chess pieces, six cubes, three escape
            // objects - and emptying the hand after each one means a Tab press between every
            // placement just to get back to work. Empty hands are still reachable, because they are
            // still a slot in the cycle; they are simply no longer where you are put by default.
            //
            // The SAME INDEX, so the order matches Tab's. The item that was after this one has
            // shifted down into its slot, and past the end wraps to the front.
            //
            // Through Equip, which is what makes it safe: equips are RECORDED, so a past self
            // advances at the same moment the player did and TrySurrender keeps re-evaluating
            // against the item genuinely in a ghost's hand. Setting Held directly here would put
            // every recording made after this out of step with the run that made it.
            if (wasHeld && carriedItems.Count > 0)
                Equip(carriedItems[slot >= carriedItems.Count || slot < 0 ? 0 : slot]);

            Version++;
            return given;
        }

        public void ReturnAll()
        {
            foreach (CarryableItem item in taken)
                if (item != null) item.ReturnToOrigin();

            taken.Clear();
            carried.Clear();
            carriedItems.Clear();
            Held = null;
            Version++;
            // Note: the objects themselves are put back by ItemRegistry.ReturnAllToOrigin, which the
            // loop calls straight after this. This only clears what the HAND believed it had -
            // `taken` misses anything a ghost fetched, which is exactly the bug that sweep fixes.
        }
    }
}
