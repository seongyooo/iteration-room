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

        // HOW HIGH A BODY'S EYE IS, for `PutDownPoint`'s visibility walk-back. The player's real
        // camera is used when there is one; this is what a GHOST is given, because a past self has
        // no camera and the walk-back is part of the act being reproduced. One number, so the two
        // bodies cannot answer it differently - `SceneBuilder` writes the controller's own standing
        // eye height into it.
        public float eyeHeight = 1.6f;

        private readonly RaycastHit[] dropHits = new RaycastHit[8];

        // Everything picked up this iteration, including items since given up. ReturnAll works off
        // this rather than off what is in hand, so a key surrendered to a lock is still put back
        // where SceneBuilder left it - it must not survive the reset sitting in the keyhole.
        private readonly List<CarryableItem> taken = new List<CarryableItem>();

        // Bumped on every change so the HUD can skip rebuilding itself on the frames - almost all of
        // them - where nothing happened.
        public int Version { get; private set; }

        // THE OBJECT IN THE HAND, or null. This is the whole of what the player carries.
        // **ONE WRITE POINT, because four call sites is four chances to forget.** `Held` changes in
        // Take, Drop, Surrender and ReturnAll; anything that has to follow what is in the hand hangs
        // off the setter rather than off those four.
        private CarryableItem held;

        public CarryableItem Held
        {
            get => held;
            private set
            {
                if (held == value) return;
                held = value;
                ApplySteadyHand();
            }
        }

        // How much look sensitivity is left while a mirror is in the hand. Halved cancels the law of
        // reflection exactly - see `FirstPersonController.steadyHandScale`, which is where the
        // argument lives. 1 would restore the old behaviour for anyone who wants to feel why not.
        public float mirrorLookScale = 0.5f;

        private FirstPersonController controllerForLook;

        private void ApplySteadyHand()
        {
            if (controllerForLook == null) controllerForLook = GetComponent<FirstPersonController>();
            if (controllerForLook == null) return;

            bool mirror = held != null && held.GetComponent<Mirror>() != null;
            controllerForLook.steadyHandScale = mirror ? Mathf.Max(0.05f, mirrorLookScale) : 1f;
        }

        // In hand right now. `Has` is gone with the pocket: it meant "carried but possibly stowed",
        // and there is no longer anywhere to stow anything. Every old call site wanted this one.
        public bool Holding(string itemId) => Held != null && Held.itemId == itemId;

        public bool HandsFull => Held != null;

        // ONE PRESS DOES ONE THING, and this is the half of that rule ItemRegistry.AimedTakeable
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

        // A SWING OF WHATEVER IS IN THE HAND, asked for by the thing being swung AT.
        //
        // It lives here rather than on `TreeTrunk` because the hand is what owns where a held object
        // sits (`CarryableItem.AttachTo` poses it against `handLocalPosition`), and an animation that
        // fought the hand for that transform would jitter. The tree says "a swing happened"; how a
        // held thing moves is the hand's business, and any future tool-shaped verb gets it free.
        //
        // Nothing about this is recorded. A swing is already an EVENT on the interactable's own bit
        // (CLAUDE.md §1.5) and a ghost reproduces the CHOP, not the animation - so this is dressing
        // on the living player's view and must never become a second source of truth.
        // A SIDE SWING, not an overhead one. An axe against a standing trunk is swung ACROSS - the
        // blade comes in horizontally at the notch - and the first version chopped downward like a
        // maul splitting a round on a block, which is a different job and read as one.
        //
        // `swingOut` is how far the hand travels across the body, `swingRoll` the wrist turn that
        // brings the blade round to lead, and `swingYaw` the shoulder. Lift is small and negative on
        // the strike: the arc dips as it comes through rather than rising.
        // DRAWN RIGHT BACK, THEN ALL THE WAY ACROSS. The first version was too small to read as a
        // swing at all - the axe twitched. What makes a swing legible is the WIND-UP: the arm goes
        // somewhere the resting pose never is, holds for an instant, and then travels a long way.
        //
        // The numbers are large on purpose. `swingOut` takes the axe most of a metre out to the
        // right and behind, and the strike carries it nearly twice that back across the body, so the
        // blade crosses the whole view rather than nodding in the middle of it.
        // ONE CLICK IS ONE BLOW: the axe goes back and UP over the shoulder, then comes DOWN and
        // ACROSS into the trunk. It is a diagonal, not a horizontal sweep - a flat swing at a
        // standing tree reads as swatting, because nothing about it is falling.
        //
        // `swingLift` is what carries that: strongly positive on the wind-up, and driven well BELOW
        // the resting pose on the strike, so the blade ends lower than it started.
        // BIGGER AND SLOWER, third attempt. The two before it were too small and too quick to read
        // as anything: at 0.5s the whole move is over before the eye finds it, and at 0.6m of travel
        // the axe never leaves the corner of the screen it rests in.
        //
        // What sells a swing is DISTANCE, and most of that distance is sideways. The axe is drawn a
        // metre and a quarter out to the right and up over the shoulder, hangs there for an instant,
        // then crosses more than two metres down and left through the trunk. A slower chop is fine -
        // `chopsToFell` pays for it - and a legible one is not optional.
        // SIDEWAYS, not downward. The diagonal chop was the previous answer to "it should come down
        // into the trunk" and it is not what a tree gets: you swing LEVEL at a standing trunk,
        // because the cut is a horizontal wedge and the blade has to arrive along it.
        //
        // So the lift is small - just enough that the draw goes somewhere the rest pose is not - and
        // the whole move is the two metres of lateral travel. It still winds up and still lands.
        public float swingDuration = 0.78f;
        public float swingOut = 1.32f;
        public float swingBack = 0.58f;
        public float swingLift = 0.20f;
        public float swingRoll = 104f;
        public float swingYaw = 104f;

        private float swingStarted = -1f;

        public void Swing()
        {
            if (Held == null) return;
            swingStarted = Time.time;
        }

        // WRITTEN ABSOLUTELY, NEVER ACCUMULATED, and that distinction is the whole of getting this
        // right. `CarryableItem.AttachTo` sets the held pose ONCE at pickup - nothing re-poses it per
        // frame - so an offset added to `localPosition` compounds every frame and the axe leaves the
        // screen inside a second. The rest pose is re-derived from the item's own `handLocalPosition`
        // each frame and the swing is written on top of it, which also means letting go mid-swing
        // needs no cleanup: the next AttachTo writes the rest pose anyway.
        private void ApplySwing()
        {
            if (swingStarted < 0f) return;

            CarryableItem held = Held;
            if (held == null) { swingStarted = -1f; return; }

            float t = (Time.time - swingStarted) / Mathf.Max(0.01f, swingDuration);
            Transform tr = held.transform;

            if (t >= 1f)
            {
                swingStarted = -1f;
                tr.localPosition = held.handLocalPosition;
                tr.localRotation = Quaternion.Euler(held.handLocalEuler);
                return;
            }

            // Out to the right and back, then across and through. The return is slower than the
            // strike, which is what makes it read as a blow landing rather than as a wobble.
            // THE WIND-UP TAKES HALF THE MOVE. Drawing back slowly and striking fast is the whole
            // difference between an axe swing and a wave; an even split reads as neither.
            // Slightly more of the move goes into the draw now that there is more of it to watch.
            const float draw = 0.58f;
            float wind = t < draw
                ? Mathf.SmoothStep(0f, 1f, t / draw)                       // out, back and up
                : 1f - Mathf.SmoothStep(0f, 1f, (t - draw) / (1f - draw)); // through and recover
            // The strike is squared so it accelerates into the contact rather than easing into it.
            float k = t < draw ? 0f : Mathf.Clamp01((t - draw) / (1f - draw));
            float strike = Mathf.Sin(k * Mathf.PI) * (0.35f + 0.65f * k);

            tr.localPosition = held.handLocalPosition
                + new Vector3(swingOut * wind - swingOut * 1.85f * strike,
                              // Barely any vertical travel: the blade rises a little on the draw and
                              // comes back through level, which is what a horizontal cut looks like.
                              swingLift * wind - swingLift * 0.9f * strike,
                              -swingBack * wind + swingBack * 1.7f * strike);
            // A little pitch on the draw only, so the head cocks back rather than dropping.
            tr.localRotation = Quaternion.Euler(-26f * wind + 10f * strike,
                                                -swingYaw * wind + swingYaw * 1.6f * strike,
                                                swingRoll * wind - swingRoll * 1.8f * strike)
                             * Quaternion.Euler(held.handLocalEuler);
        }
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
            // The swing first and unconditionally: it is dressing on the held object's pose and has
            // to keep running through a frame where the press below is about to put the object down,
            // and through the frames where there is no press at all.
            ApplySwing();

            if (LoopManager.Instance != null && !LoopManager.Instance.AcceptsInput) return;
            if (Held == null || InteractedThisFrame) return;
            if (!GameInput.InteractPressed) return;

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

            Camera cam = PlayerLookup.Eye;
            Vector3 rest = PutDownPoint(dropped, transform,
                                        cam != null ? cam.transform.position
                                                    : transform.position + Vector3.up * eyeHeight);

            dropped.DropAt(from);
            FallingItem fall = dropped.GetComponent<FallingItem>();
            if (fall != null) fall.Release(rest, pose);
            else dropped.DropAt(rest);

            Version++;
            return dropped;
        }

        // **WHERE A PUT-DOWN LANDS, AND IT IS ONE ANSWER FOR TWO BODIES.**
        //
        // `GhostReplayer.PutDown` calls this with the ghost's own transform, so a past self performs
        // the act the player performed rather than an approximation of it that happens to agree on
        // flat open floor. CLAUDE.md §4: *compare the acts, not the outcomes*.
        //
        // It was written as an approximation on purpose, and the reasoning was wrong in an
        // instructive way. The ghost skipped both the clearance cast and the visibility walk-back
        // because "it is in the same place facing the same way as the player was, so that question
        // was answered when the recording was made". That argument is about the RESULT - it holds
        // exactly while the two calculations would agree, which is to say while nothing is in the
        // way. Play found where they do not: put the ladder down **on the bed** and the player's
        // walk-back shortens the throw, because a bed hides the spot beyond it from a standing eye,
        // while the ghost threw the full distance and landed it somewhere else entirely. Two bodies
        // doing the same thing in the same spot and getting different answers.
        //
        // The cost of asking properly is one sphere cast and a few short rays per drop event, which
        // is nothing, and what it buys is that this method IS the definition of the act.
        public Vector3 PutDownPoint(CarryableItem item, Transform body, Vector3 eye)
        {
            if (item == null || body == null) return Vector3.zero;

            // Room for it, and then somewhere it can be seen. Two separate questions and the second
            // is the one play asked: an object put down correctly against a structure is still an
            // object that has apparently disappeared.
            float ahead = VisibleAhead(item, body, eye, RoomAhead(item, body));
            return body.position + body.forward * ahead;
        }

        // How far ahead there is actually room to put this down. The object's own half-width is part
        // of the distance it wants - a metre-wide cube has to clear the player, where a pin does not
        // - and a cast then takes back however much of that a wall is standing in.
        //
        // Cast at waist height rather than along the floor, so it meets what the object would meet
        // on its way out rather than the floor it is going to land on. The sphere is the object's
        // own half-width, so what it reports is "would this fit here", not "is there a wall
        // somewhere over there".
        //
        // HOW WIDE THE OBJECT IS, MEASURED - not `handLocalScale.x`, which is a SCALE and only
        // happens to be a width for something built from a unit mesh. The bucket is the first
        // carryable where the two differ: it is a 0.35m pail held at a root scale of 2, so this read
        // it as two metres across, cast a one-metre sphere from inside the player, hit the floor at
        // distance zero and put every bucket down at `hardMinDrop` - on the player's own feet.
        private float RoomAhead(CarryableItem item, Transform body)
        {
            float half = item.WorldHalfWidth();
            float want = dropAhead + half;

            Vector3 origin = body.position + Vector3.up;
            int count = Physics.SphereCastNonAlloc(origin, Mathf.Max(0.05f, half), body.forward,
                                                   dropHits, want, dropBlockers,
                                                   QueryTriggerInteraction.Ignore);

            bool blocked = false;
            float allowed = want;
            for (int i = 0; i < count; i++)
            {
                Transform t = dropHits[i].collider != null ? dropHits[i].collider.transform : null;
                if (t == null) continue;
                // The body, whose capsule the sphere starts inside, and the object itself - whose
                // colliders are off while it is held, so this is belt and braces. A ghost has no
                // colliders at all (CLAUDE.md §1.7), so the first test simply finds nothing for it.
                if (t.IsChildOf(body)) continue;
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
        private float VisibleAhead(CarryableItem item, Transform body, Vector3 from, float allowed)
        {
            for (float d = allowed; d > hardMinDrop; d -= 0.12f)
            {
                // `floorY` HERE, NOT `RestingY`, and the difference matters once there is a second
                // storey. This builds a point relative to the PLAYER, who is standing on the same
                // floor they are dropping onto - so what is wanted is the object's height above that
                // floor, which is exactly what `floorY` is. `RestingY` is a world height and would
                // add the storey in twice.
                Vector3 spot = body.position + body.forward * d + Vector3.up * item.floorY;
                if (!Occluded(from, spot, item, body)) return d;
            }

            return hardMinDrop;
        }

        private bool Occluded(Vector3 from, Vector3 to, CarryableItem item, Transform body)
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
                if (t.IsChildOf(body)) continue;
                if (t.IsChildOf(item.transform)) continue;
                return true;
            }

            return false;
        }

        // Hands an item over for good - the key going into Room2's lock. Returns the item so the
        // caller can place it; nobody else knows where it should go. It stays in `taken`, because
        // the loop still has to put it back at the top of the next iteration.
        public CarryableItem Surrender(string itemId) => Surrender(itemId, null);

        // WHICH socket, when the id has more than one. Everything up to the buckets had exactly one
        // place a given id could go, so a surrender needed only to name the item; two stands will
        // take a bucket, and "put it on that one" is a fact a past self has to reproduce or it
        // reproduces nothing. Recorded through `CarryEvent.instanceName`, which already carries this
        // kind of identity for a Take - see ItemRegistry.FindSocket.
        public CarryableItem Surrender(string itemId, string targetName)
        {
            if (Held == null || Held.itemId != itemId) return null;

            MarkInteract();

            CarryableItem given = Held;
            Held = null;

            // Recorded AFTER the item is confirmed gone from the hand, so a surrender is only ever
            // written for one that was genuinely held. RecordedTimeline's own note on re-evaluation
            // explains why both ends are conditional.
            recorder?.RecordCarry(itemId, CarryKind.Surrender, targetName);

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
