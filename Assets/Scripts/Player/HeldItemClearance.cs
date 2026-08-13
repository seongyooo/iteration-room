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
    // THE FIX IS TO PULL IT IN along the line it already sits on, so it stops at the surface instead
    // of passing through it. The anchor sits AT the camera and the item's offset is expressed inside
    // it, so moving the anchor to `rest * (f - 1)` puts the item at `rest * f` - same direction, same
    // true size, nearer.
    //
    // IT USED TO SHRINK IT INSTEAD (scaling the anchor moves and shrinks by one factor, so apparent
    // size never changed) and that is gone with the shrink-to-fit hold: objects are held at their
    // real size now, and a clearance that quietly resized them would be the same lie by another
    // route. The cost is honest and visible - a big object pressed against a wall comes toward the
    // camera and takes more of the screen, because that is what a big object against a wall does.
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

        // Never closer than this fraction of the item's normal distance. The camera's near plane is
        // 0.05m and the item has to stay outside it whatever it is pressed against.
        public float minFraction = 0.22f;

        private readonly RaycastHit[] hits = new RaycastHit[8];

        // LateUpdate: FirstPersonController writes the camera's rotation in Update, and a cast fired
        // along last frame's forward would lag the view by a frame at exactly the moment - turning to
        // face a wall - when it is most visible.
        private void LateUpdate()
        {
            if (anchor == null || eye == null || hand == null) return;

            CarryableItem held = hand.Held;
            if (held == null) { anchor.localPosition = Vector3.zero; return; }

            // The item's own resting offset, so this works for whatever is in the hand rather than
            // for one hard-coded pose.
            Vector3 rest = held.handLocalPosition;
            float reach = rest.magnitude;
            if (reach <= 0.0001f) { anchor.localPosition = Vector3.zero; return; }

            Vector3 origin = eye.position;
            Vector3 dir = eye.rotation * (rest / reach);

            // Half the object's own width, within reason - see minProbe/maxProbe.
            float probeRadius = Mathf.Clamp(held.handLocalScale.x * 0.5f, minProbe, maxProbe);

            float allowed = reach;
            int count = Physics.SphereCastNonAlloc(origin, probeRadius, dir, hits, reach,
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

                if (hits[i].distance < allowed) allowed = hits[i].distance;
            }

            // The anchor sits at the camera and the item's offset lives inside it, so displacing the
            // anchor by (f - 1) x rest lands the item at f x rest: same direction, same size, nearer.
            float f = Mathf.Clamp(allowed / reach, minFraction, 1f);
            anchor.localPosition = rest * (f - 1f);
        }
    }
}
