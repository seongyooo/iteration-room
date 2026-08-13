using System.Collections.Generic;
using UnityEngine;

namespace IterationRoom
{
    // What the player is currently carrying, which since 2026-08-13 is EXACTLY ONE OBJECT OR NONE.
    // Sits on the player, with holdAnchor parented under the camera so a held item rides the view -
    // including through the wake-up, where WakeUpSequence poses the camera directly.
    //
    // ONE OBJECT, AND TAB IS GONE. The pocket held everything you had picked up and Tab cycled which
    // of them was out; now your hands are simply full or empty. What that buys:
    //
    // - "Carried" and "in hand" stop being two facts that must be kept in agreement. Every fixture
    //   operated WITH an object used to have to gate on Holding rather than Has, and every ghost
    //   action had to re-evaluate an Equip event recorded alongside the take. Both are now true by
    //   construction: there is one object, so it is the one in your hand.
    // - The last lap changes shape, deliberately. Three escape objects can no longer be carried to
    //   Room4 in one trip, so finishing means past selves delivering what they delivered while the
    //   living player brings the last one - which is the game's own thesis stated as a rule instead
    //   of as a convenience.
    //
    // Carrying is world state, so the loop has to rewind it: ReturnAll() puts every item back where
    // SceneBuilder left it. Without that, the tool stays in your hand across the reset and the trip
    // to the drawer stops costing anything, which is most of what Room2's puzzle is made of.
    public class PlayerHand : MonoBehaviour
    {
        public Transform holdAnchor;

        // Where takes, drops and surrenders are written so a past self can repeat them. Every
        // carryable is recorded; whether a ghost may act on one is decided at REPLAY, by
        // CarryableItem.ghostCarryable - see docs/ghost-possession-design.md.
        public PlayerRecorder recorder;

        // How far in front of the player a dropped object lands, BEFORE its own size is added. It
        // must not land inside the player - a dropped object is solid, and one placed at their own
        // feet would leave them standing in it - and the clearance a metre-wide cube needs is not
        // the clearance a pin needs, so half the object's width is added to this.
        public float dropAhead = 0.55f;

        // ...and never further than there is room for. A put-down used to place the object at that
        // distance whatever was in the way, so facing a wall put it inside the wall. A cast decides
        // how far the space actually goes - the same one HeldItemClearance uses to keep a HELD
        // object out of the walls, applied to where a released one comes to rest.
        //
        // Whose hits count: everything except balloons, which the player's own controller already
        // refuses to collide with. The player and the object being dropped are filtered by
        // hierarchy - the sphere starts inside the player's capsule.
        public LayerMask dropBlockers = ~0;

        // How far ahead to put it when NOTHING is in the way. It is not a floor under the cast, and
        // that distinction is the whole of a bug play found: written as `Max(allowed, minDropAhead)`
        // it silently undid the clearance the cast had just measured, so standing at Room4's console
        // - whose face stops the player 0.38m out - put a 0.30m object down centred at 0.35m, half of
        // it inside the console. **An object buried in a structure is worse than an object underfoot**:
        // the player can step off one, and cannot dig the other out.
        public float minDropAhead = 0.35f;

        // The one real floor: never at the player's own pivot, where an object would be centred
        // inside them rather than merely touching.
        public float hardMinDrop = 0.15f;

        private readonly RaycastHit[] dropHits = new RaycastHit[8];

        // Everything picked up this iteration, including items since given up. ReturnAll works off
        // this rather than off what is in hand, so a key surrendered to a lock is still put back
        // where SceneBuilder left it - it must not survive the reset sitting in the keyhole.
        private readonly List<CarryableItem> taken = new List<CarryableItem>();

        // Bumped on every change so the HUD can skip rebuilding itself on the frames - almost all of
        // them - where nothing happened.
        public int Version { get; private set; }

        // THE OBJECT IN THE HAND, or null. This is the whole of what the player carries.
        public CarryableItem Held { get; private set; }

        // In hand right now. `Has` is gone with the pocket: it meant "carried but possibly stowed",
        // and there is no longer anywhere to stow anything. Every old call site wanted this one.
        public bool Holding(string itemId) => Held != null && Held.itemId == itemId;

        public bool HandsFull => Held != null;

        // ONE PRESS DOES ONE THING, and this is the half of that rule ItemRegistry.NearestTakeable
        // could not enforce on its own.
        //
        // Every carryable polls E for itself and defers to the nearest candidate, which reads as
        // watertight and is not: the candidates are recomputed by each item as its own Update runs,
        // and taking one REMOVES it from the running (IsCarried kills IsAvailable, which kills
        // WantsInteractHint). So if the nearest item's Update happened to run FIRST, it took itself,
        // and the next overlapping item - now the nearest of what was left - took itself too, on the
        // same press. Script execution order is arbitrary, so the same press did nothing wrong half
        // the time and emptied a whole pile the other half.
        //
        // It answers a second question now that E also PUTS DOWN: every fixture that consumes a
        // press marks it here, and the drop below runs in LateUpdate and stands down if anything
        // did. That ordering is what makes "E means insert when you are at a recess, and put down
        // when you are not" true without anyone having to know about anyone else.
        public bool InteractedThisFrame => interactFrame == Time.frameCount;
        public void MarkInteract() => interactFrame = Time.frameCount;
        private int interactFrame = -1;

        public void Take(CarryableItem item)
        {
            // HANDS FULL IS A REFUSAL, not a swap. E has exactly one meaning at a time - take when
            // empty-handed, put down when not - and a press that silently exchanged one object for
            // another would be a third meaning with no prompt to announce it.
            if (item == null || Held != null) return;

            MarkInteract();

            // Taking it off a past self. The ghost has to be told, or it would keep the item in its
            // own held list and try to surrender it later - and there is exactly one of each object,
            // so two holders is the one state this design does not allow.
            item.HeldByGhost?.ReleaseItem(item, toWorld: false);

            // Added once per iteration even if this is the second time it has passed through the
            // player's hands, because `taken` is what ReturnAll walks - a duplicate would just
            // return the same object twice.
            if (!taken.Contains(item)) taken.Add(item);
            recorder?.RecordCarry(item.itemId, CarryKind.Take, item.name);

            Held = item;
            if (holdAnchor != null) item.AttachTo(holdAnchor);
            Version++;
        }

        // E with your hands full and nothing else claiming the press. Runs in LateUpdate so every
        // fixture and every takeable has already had its Update: a press that went into a recess,
        // a lock, a drawer or a plate has marked itself by now, and this stands down.
        private void LateUpdate()
        {
            if (LoopManager.Instance != null && !LoopManager.Instance.AcceptsInput) return;
            if (Held == null || InteractedThisFrame) return;
            if (!Input.GetKeyDown(KeyCode.E)) return;

            Drop();
        }

        // Put down, still in play, where the player is standing - the fifth of the five states in
        // CLAUDE.md SS1.2, and the one nothing could reach until now. It is NOT a surrender: nothing
        // keeps the object, and E over it picks it straight back up.
        //
        // IT LEAVES THE HAND. The object is released exactly where it was being held - same place,
        // same pose, no jump - and FallingItem carries it down and forward to a spot in front of the
        // player. Releasing it at the destination instead and letting it fall straight down was the
        // first version, and it read as an object appearing out of the air already falling, because
        // that is what it was.
        //
        // AHEAD OF THE PLAYER, not at them: a dropped object is solid (CarryableItem.blocker) and one
        // placed at their own feet would leave them standing inside it. Its own half-width is part of
        // that distance, so a metre-wide cube clears the player and a pin is not thrown across the
        // room to achieve the same thing.
        public CarryableItem Drop()
        {
            if (Held == null) return null;

            MarkInteract();

            CarryableItem dropped = Held;
            Held = null;

            // Recorded AFTER the hand has let go, so a drop is only ever written for something that
            // was genuinely held - the same ordering Surrender uses, for the same reason.
            recorder?.RecordCarry(dropped.itemId, CarryKind.Drop);

            // Captured BEFORE DropAt, which restores the object's resting rotation: this is the pose
            // the player can see in their hand, and the fall turns it from here to there.
            Vector3 from = dropped.transform.position;
            Quaternion pose = dropped.transform.rotation;

            // Room for it, and then somewhere it can be seen. Two separate questions and the second
            // is the one play asked: an object put down correctly against a structure is still an
            // object that has apparently disappeared.
            float ahead = VisibleAhead(dropped, RoomAhead(dropped));
            Vector3 rest = transform.position + transform.forward * ahead;

            dropped.DropAt(from);
            FallingItem fall = dropped.GetComponent<FallingItem>();
            if (fall != null) fall.Release(rest, pose);
            else dropped.DropAt(rest);

            Version++;
            return dropped;
        }

        // How far ahead there is actually room to put this down. The object's own half-width is part
        // of the distance it wants - a metre-wide cube has to clear the player, where a pin does not
        // - and a cast then takes back however much of that a wall is standing in.
        //
        // Cast at waist height rather than along the floor, so it meets what the object would meet
        // on its way out rather than the floor it is going to land on. The sphere is the object's
        // own half-width, so what it reports is "would this fit here", not "is there a wall
        // somewhere over there".
        private float RoomAhead(CarryableItem item)
        {
            float half = Mathf.Abs(item.handLocalScale.x) * 0.5f;
            float want = dropAhead + half;

            Vector3 origin = transform.position + Vector3.up;
            int count = Physics.SphereCastNonAlloc(origin, Mathf.Max(0.05f, half), transform.forward,
                                                   dropHits, want, dropBlockers,
                                                   QueryTriggerInteraction.Ignore);

            bool blocked = false;
            float allowed = want;
            for (int i = 0; i < count; i++)
            {
                Transform t = dropHits[i].collider != null ? dropHits[i].collider.transform : null;
                if (t == null) continue;
                // The player, whose capsule the sphere starts inside, and the object itself - whose
                // colliders are off while it is held, so this is belt and braces.
                if (t.IsChildOf(transform)) continue;
                if (t.IsChildOf(item.transform)) continue;

                if (dropHits[i].distance >= allowed) continue;
                allowed = dropHits[i].distance;
                blocked = true;
            }

            // `minDropAhead` applies only when the way is CLEAR. Once something has been hit, the
            // cast's answer is the whole answer - raising it back up to a comfortable distance is
            // exactly how the object ended up inside Room4's console.
            if (blocked) return Mathf.Max(allowed, hardMinDrop);
            return Mathf.Max(want, minDropAhead);
        }

        // ...AND SOMEWHERE THE PLAYER CAN SEE IT. Clearing the geometry is not the same as being
        // visible: a console the player is standing at hides its own foot from a 1.6m eye, so an
        // object correctly placed against it is still an object that has apparently vanished.
        //
        // Walks the drop point back toward the player until the line from the eye to it is clear.
        // Closer is always more visible here, because the thing doing the hiding is in front - and
        // the worst case, at the player's own feet, is a place they only have to look down at.
        private float VisibleAhead(CarryableItem item, float allowed)
        {
            Camera eye = PlayerLookup.Eye;
            if (eye == null) return allowed;

            Vector3 from = eye.transform.position;
            for (float d = allowed; d > hardMinDrop; d -= 0.12f)
            {
                Vector3 spot = transform.position + transform.forward * d + Vector3.up * item.floorY;
                if (!Occluded(from, spot, item)) return d;
            }

            return hardMinDrop;
        }

        private bool Occluded(Vector3 from, Vector3 to, CarryableItem item)
        {
            Vector3 delta = to - from;
            float dist = delta.magnitude;
            if (dist < 0.01f) return false;

            int count = Physics.RaycastNonAlloc(from, delta / dist, dropHits, dist, dropBlockers,
                                                QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Transform t = dropHits[i].collider != null ? dropHits[i].collider.transform : null;
                if (t == null) continue;
                if (t.IsChildOf(transform)) continue;
                if (t.IsChildOf(item.transform)) continue;
                return true;
            }

            return false;
        }

        // Hands an item over for good - the key going into Room2's lock. Returns the item so the
        // caller can place it; nobody else knows where it should go. It stays in `taken`, because
        // the loop still has to put it back at the top of the next iteration.
        public CarryableItem Surrender(string itemId)
        {
            if (Held == null || Held.itemId != itemId) return null;

            MarkInteract();

            CarryableItem given = Held;
            Held = null;

            // Recorded AFTER the item is confirmed gone from the hand, so a surrender is only ever
            // written for one that was genuinely held. RecordedTimeline's own note on re-evaluation
            // explains why both ends are conditional.
            recorder?.RecordCarry(itemId, CarryKind.Surrender);

            Version++;
            return given;
        }

        public void ReturnAll()
        {
            foreach (CarryableItem item in taken)
                if (item != null) item.ReturnToOrigin();

            taken.Clear();
            Held = null;
            Version++;
            // Note: the objects themselves are put back by ItemRegistry.ReturnAllToOrigin, which the
            // loop calls straight after this. This only clears what the HAND believed it had -
            // `taken` misses anything a ghost fetched, which is exactly the bug that sweep fixes.
        }
    }
}
