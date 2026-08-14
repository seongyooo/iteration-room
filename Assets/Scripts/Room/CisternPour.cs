using UnityEngine;

namespace IterationRoom
{
    // The volume over the tank that takes a full bucket off whoever brings it.
    //
    // NO E PRESS, which is the one place this room deliberately breaks the game's habit. Everything
    // else is operated with a key because a press is a decision, and there is no decision here: a
    // player walking up to a tank holding a full bucket wants exactly one thing, and asking them to
    // confirm it six times a room is asking them to confirm nothing.
    //
    // **POLLED, NOT TRIGGERED, and that is not a style choice.** A carryable's reach trigger is
    // DISABLED while the player holds it (`CarryableItem.AttachTo`) and left ENABLED while a ghost
    // does (`AttachToGhost`, so the player can take it back). A trigger-driven tank would therefore
    // see every bucket a ghost brought and none of the ones the player carried in themselves - which
    // is exactly backwards from which of the two is easier to notice.
    //
    // So it asks the two questions directly, every frame: is the player standing here with a full
    // bucket, and is there a full bucket sitting or being carried through here by anyone else.
    public class CisternPour : MonoBehaviour
    {
        public Cistern cistern;
        public float radius = 1.3f;

        private void Update()
        {
            if (cistern == null) return;
            if (LoopManager.Instance != null && !LoopManager.Instance.IterationRunning) return;

            // What the player is carrying. `PlayerHand.Held` is the whole answer - one object or
            // none, since Tab went.
            PlayerHand hand = PlayerLookup.Hand;
            Collider player = PlayerLookup.Collider;
            if (hand != null && hand.Held != null && player != null && player.enabled
                && Inside(player.bounds.center))
            {
                Bucket carried = hand.Held.GetComponent<Bucket>();
                if (carried != null && carried.Full) { cistern.Pour(carried); return; }
            }

            // And everything else - loose on the floor, or in a past self's hands. A ghost has no
            // collider by design (§1.7), so its position is the only thing there is to ask about.
            foreach (Bucket bucket in Bucket.All)
            {
                if (bucket == null || !bucket.Full) continue;
                if (!Inside(bucket.transform.position)) continue;
                cistern.Pour(bucket);
                return;
            }
        }

        private bool Inside(Vector3 world)
        {
            Vector3 d = world - transform.position;
            d.y = 0f;
            return d.sqrMagnitude < radius * radius;
        }
    }
}
