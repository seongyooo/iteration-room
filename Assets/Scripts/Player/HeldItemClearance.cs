using UnityEngine;

namespace IterationRoom
{
    // Keeps whatever is in the player's hand out of the walls.
    //
    // THE PROBLEM. `holdAnchor` hangs off the camera and a held item sits forward and to the right of
    // it - about 0.57m out for the cube. The CharacterController stops the player's CAPSULE at its
    // own 0.3m radius, so the camera never gets closer than roughly 0.22m to a wall and the item is
    // left poking a third of a metre through it. Walk up to a wall with something in hand and rub
    // along it and you watch your own key inside the plaster.
    //
    // **THE FIX IS TO STOP THE PLAYER** (2026-08-29, by request), and it is the third answer this
    // has had.
    //
    // It SHRANK the object first, which held the apparent size still by lying about the real one.
    // Then it PULLED THE OBJECT IN along the hold line, pinning it to the wall at true size - and
    // that is where the growth came from. The object was already fixed to the wall; what nobody had
    // counted is that the PLAYER keeps walking at the wall, so the wall and the object both arrive
    // at the eye. 0.57m out becomes 0.22m at the capsule's limit, which is 2.6x on screen.
    //
    // For a wall dead ahead there is no third position for the object - sideways and down are still
    // inside the wall - so the only thing left to move is the PLAYER. That is the honest answer
    // rather than a workaround: CLAUDE.md §4 already claims a metre of glass is "genuinely in the
    // way", and until now it was in the way of nothing. Walk a mirror into a wall and the mirror
    // stops you, the way it would.
    //
    // WHAT IT COSTS, and it is the one failure this must not have. A held object can catch a door
    // jamb, so `FirstPersonController` removes only the component of motion INTO the contact and
    // leaves the player free to slide off it - the same thing the CharacterController already does
    // for the capsule. Wedged in a doorway you can plainly fit through, with nothing on screen
    // saying why, would be worse than the growth this replaces.
    //
    // WHY NOT A SECOND CAMERA. The usual answer to a clipping viewmodel is to render it with its own
    // camera on its own layer, over the top of everything. That means a second camera, a layer, and a
    // renderer feature - and this project's renderer is reconciled rather than appended to for good
    // reason (CLAUDE.md SS3). A cast is one component and touches nothing else.
    //
    // This is the FIRST cast in the project, and the two exclusions it needs are both real: the
    // player's own capsule, which the sphere starts inside, and balloons, which the controller
    // already refuses to collide with - without that, walking through Room2 would have the item
    // flinching at every balloon it passed.
    public class HeldItemClearance : MonoBehaviour
    {
        public Transform anchor;
        public Transform eye;
        public PlayerHand hand;

        // Whose hits count. Everything except balloons; SceneBuilder owns the value.
        public LayerMask blockers = ~0;

        // Anything under this transform is the player themselves and is not an obstacle. The sphere
        // starts inside the CharacterController's capsule, which would otherwise report a hit at
        // distance zero every single frame and pin the item to the player's nose.
        public Transform ignoreRoot;

        // Floor and ceiling on the probe. It is derived from the HELD object - half its own width,
        // because the cast stops the item's centre that far short of a surface and its own body
        // fills the gap - and clamped, because a metre-wide cube would otherwise cast a sphere so
        // large it is inside the wall before it starts and the item would never leave the camera.
        public float minProbe = 0.06f;
        public float maxProbe = 0.22f;

        // **HOW MUCH CONTACT COUNTS AS CONTACT**, in metres. The stop fires as soon as the object is
        // this much short of where it wants to be, which for a player walking at a wall is one frame's
        // travel - so they are held before the object has gone anywhere.
        //
        // **IT NO LONGER LIMITS HOW FAR THE OBJECT PULLS IN, and that was the bug.** It used to clamp
        // the pull-in to 8%, so past that the object stopped moving and the wall kept coming: the
        // object was left INSIDE it by however far the player got. Play reported exactly that - "벽에
        // 조금씩 파묻히는" - after the stop had already fixed the growth.
        //
        // Separating the two questions is the whole fix. **How far does the object move** is answered
        // by the cast, always, to the millimetre: it sits exactly on the surface. **When is the player
        // stopped** is answered by this. Neither has to compromise for the other, which is what a
        // single number was making them do.
        public float contactSlack = 0.01f;

        // Never closer than this fraction of the item's normal distance. The camera's near plane is
        // 0.05m and the item has to stay outside it whatever it is pressed against.
        public float minFraction = 0.22f;

        private readonly RaycastHit[] hits = new RaycastHit[8];

        // Resolved through `PlayerLookup` rather than wired, for the reason every player-side
        // reference here is: this component lives on the hand anchor and a serialised reference
        // across a scene boundary comes back null.
        private FirstPersonController controller;

        // LateUpdate: FirstPersonController writes the camera's rotation in Update, and a cast fired
        // along last frame's forward would lag the view by a frame at exactly the moment - turning to
        // face a wall - when it is most visible.
        private void LateUpdate()
        {
            if (anchor == null || eye == null || hand == null) return;

            if (controller == null) controller = PlayerLookup.Controller;

            CarryableItem held = hand.Held;
            if (held == null)
            {
                anchor.localPosition = Vector3.zero;
                // NOTHING IN THE HAND IS NOTHING IN THE WAY, and it has to be SAID rather than simply
                // not written: a player who put an object down while it was against a wall would
                // otherwise carry its obstruction for the rest of the run.
                Release();
                return;
            }

            // The item's own resting offset, so this works for whatever is in the hand rather than
            // for one hard-coded pose.
            Vector3 rest = held.handLocalPosition;
            float reach = rest.magnitude;
            if (reach <= 0.0001f) { anchor.localPosition = Vector3.zero; Release(); return; }

            Vector3 dir = eye.rotation * (rest / reach);
            float allowed = Sweep(dir, reach, HeldProbeRadius(held), out Vector3 normal);

            // The anchor sits at the camera and the item's offset lives inside it, so displacing the
            // anchor by (f - 1) x rest lands the item at f x rest: same direction, same size, nearer.
            //
            // **ALL THE WAY TO THE SURFACE, ALWAYS.** The sphere cast stops with its centre exactly
            // where the object touches, so placing the object there is contact and not penetration.
            // `minFraction` is the only floor left, and it is about the camera's near plane rather
            // than about walls.
            //
            // The apparent growth this used to cause is handled by the stop below rather than by
            // refusing to move: a player who cannot walk any further at the wall cannot bring the
            // wall - or the object on it - any closer to their eye.
            float f = Mathf.Clamp(allowed / reach, minFraction, 1f);
            anchor.localPosition = rest * (f - 1f);

            // AND THE STOP. The inward normal of whatever the object is pressed against, handed over
            // as a LEVEL - written every frame and cleared every frame - so there is no state a
            // missed frame could leave set.
            //
            // **FLATTENED, and it has to be.** A cast at a floor or a ceiling comes back with a
            // vertical normal, and refusing motion along that would stop a player walking on the
            // floor they are standing on. An object can be in your way; it cannot hold you up.
            // **TURNING IS NOT WALKING, and this is the one case the stop cannot cover.** Rotating to
            // face a wall you are already standing at sweeps the object into it without moving the
            // player an inch, and there is no motion to refuse. The object pulls in for that, which is
            // the growth this feature set out to remove - bounded now by how close the player managed
            // to get, which is a step rather than a stride.
            if (allowed >= reach - contactSlack) { Release(); return; }

            Vector3 flat = normal;
            flat.y = 0f;
            if (controller == null) return;

            controller.heldObstruction = flat.sqrMagnitude > 0.0001f ? flat.normalized : Vector3.zero;

            // **THE VIEW IS NOT FROZEN HERE ANY MORE.** Contact is not by itself a reason to refuse
            // a turn - only a turn that makes the contact worse is, and that is a question about a
            // direction rather than about a state. `FirstPersonController` asks `WouldWorsen` per
            // axis as the player turns; this just registers so it has something to ask.
            controller.heldClearance = this;
        }

        // Half the object's own width, within reason - see minProbe/maxProbe.
        private float HeldProbeRadius(CarryableItem held) =>
            Mathf.Clamp(held.handLocalScale.x * 0.5f, minProbe, maxProbe);

        // HOW FAR THE OBJECT CAN GO IN ONE DIRECTION before it meets something, and what it met.
        // Pulled out of `LateUpdate` so the LOOK can ask it about a direction the player has not
        // turned to yet - see `WouldWorsen`.
        private float Sweep(Vector3 dir, float reach, float probeRadius, out Vector3 normal)
        {
            normal = Vector3.zero;
            float allowed = reach;

            int count = Physics.SphereCastNonAlloc(eye.position, probeRadius, dir, hits, reach,
                                                   blockers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Transform t = hits[i].collider != null ? hits[i].collider.transform : null;
                if (t == null) continue;
                // The player, and the item itself. The item's own colliders are switched off while it
                // is held (CarryableItem.AttachTo), so the second test is belt and braces rather than
                // load-bearing - but a future carryable that keeps one would otherwise stop dead the
                // instant it was picked up, and that failure would be baffling.
                if (ignoreRoot != null && t.IsChildOf(ignoreRoot)) continue;
                if (t.IsChildOf(anchor)) continue;

                if (hits[i].distance >= allowed) continue;
                allowed = hits[i].distance;
                normal = hits[i].normal;
            }

            return allowed;
        }

        // **WOULD TURNING THIS WAY PUSH THE OBJECT FURTHER IN?** Asked by `FirstPersonController`
        // before it applies a look delta, once per axis.
        //
        // **THIS IS THE WHOLE FIX FOR "I CANNOT TURN BACK"** (2026-08-29). Freezing the view outright
        // on contact stopped the growth and took the player's only sense with it - including the turn
        // AWAY from the wall, which makes things better and was being refused along with everything
        // else. A lock you cannot turn out of is a trap whether or not you can walk out of it.
        //
        // **IT IS A CAST, NOT A RULE ABOUT NORMALS.** Comparing the turn against the contact normal
        // would be cheaper and wrong at exactly the moment it matters: near a corner the surface the
        // object is touching says nothing about what it will touch a degree later. Casting down the
        // direction the player is actually asking for answers the real question - the same idea as
        // treating the player and what they carry as one body, done with the one shape a
        // `CharacterController` can afford, since it cannot have a compound one and growing its
        // capsule would seal every doorway in the building.
        //
        // Per axis, because a diagonal mouse movement can be helpful in yaw and harmful in pitch, and
        // refusing both would be the same over-refusal one step smaller.
        public bool WouldWorsen(float yawDelta, float pitchDelta)
        {
            if (anchor == null || eye == null || hand == null) return false;

            CarryableItem held = hand.Held;
            if (held == null || held.posesItself) return false;

            Vector3 rest = held.handLocalPosition;
            float reach = rest.magnitude;
            if (reach <= 0.0001f) return false;

            float probeRadius = HeldProbeRadius(held);
            Vector3 dir = eye.rotation * (rest / reach);

            float now = Sweep(dir, reach, probeRadius, out _);
            // Not in contact at all: every direction is free, and asking a second cast would be waste.
            if (now >= reach - contactSlack) return false;

            Vector3 turned = Quaternion.AngleAxis(yawDelta, Vector3.up)
                           * Quaternion.AngleAxis(-pitchDelta, eye.right) * dir;
            float then = Sweep(turned, reach, probeRadius, out _);

            // Strictly worse, with a hair of tolerance so a direction that is the same within
            // floating point does not read as deeper and lock the axis.
            return then < now - 0.0005f;
        }

        private void Release()
        {
            if (controller == null) return;
            controller.heldObstruction = Vector3.zero;
            // **THE REGISTRATION STAYS.** `WouldWorsen` asks the hand what is held every time it is
            // called and answers false for an empty one, so a stale reference costs nothing - where
            // clearing it here would leave the look unguarded for the frame after any release.
        }
    }
}
