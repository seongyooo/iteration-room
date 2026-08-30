using UnityEngine;

namespace IterationRoom
{
    // **A CARRIED OBJECT THAT STAYS LEVEL, at a fixed height in front of the body.**
    //
    // The ordinary hold parents an item to `holdAnchor`, which hangs off the CAMERA - so the item
    // inherits the player's pitch and swings through the world as they look about. For a key that is
    // right and unremarkable. For a ladder eight metres long it is the fault it was reported as:
    // look down and the far end drives into the floor, look up and it goes into the ceiling, and in
    // both cases what is left on screen is the inside of a slab, which reads as the ladder vanishing.
    //
    // **NOT A NEW IDEA - `Mirror` has done exactly this since the beam existed**, for a reason one
    // step along from this one: a pane that inherited the holder's pitch would send the beam wherever
    // the mouse was pointing. Same fix, same shape, and this is the general version of it so the next
    // long object does not need a third copy. See `CarryableItem.posesItself`, which is what tells
    // `HeldItemClearance` not to drag an object that is placing itself.
    //
    // WHAT STAYS TRUE: everything about custody. This only writes where the object is DRAWN, every
    // frame, after whoever moved it. The hand still owns whether it is held, the recording still
    // records the take, and a ghost carrying one gets the same pose off its own body.
    [RequireComponent(typeof(CarryableItem))]
    public class LevelCarry : MonoBehaviour
    {
        public CarryableItem item;

        // Above the holder's FEET, not their eye. The reference is the body for the reason `Mirror`
        // uses it: a ghost's eye is wherever its animation put it this frame, and the same object in
        // a past self's hands has to sit where it sat in the player's.
        public float carryHeight = 1.1f;
        // How far in front of the holder, along their own facing.
        public float carryAhead = 0.55f;
        // And how far to the side, so the object is beside the player rather than through them.
        public float carrySide = 0.35f;

        // Turned off the level facing, so the object is not exactly along the line of sight - dead
        // along it, a long thin thing is a single line vanishing at the crosshair.
        public float yaw = 4f;

        // **HOW THICK IT IS TAKEN TO BE when it is felt against a wall** - see `GiveWay`. A line with
        // a little width, not a sphere the size of the object.
        public float probeRadius = 0.16f;
        // Whose hits count. Everything by default; triggers never.
        public LayerMask blockers = ~0;

        // **HOW FAR IT MAY SLIDE THROUGH THE HOLDER'S HANDS, and this is what decides whether the
        // object gives way or the PLAYER does.**
        //
        // Unclamped, `GiveWay` always resolves the penetration - which is an object that never buries
        // and also never stops anybody, because `HeldItemClearance.LongContact` casts the same span
        // and finds it clear a frame later. That is one of the two behaviours asked for and not the
        // other.
        //
        // Clamped, both hold and neither has to be special-cased. The object slides through the
        // holder's grip as they approach a wall, so it never goes through it; once it can give no
        // more, an end IS in the wall, `LongContact` sees it, and the player is refused - which
        // arrives about where the holder's own capsule would have stopped them anyway.
        public float maxSlide = 2.6f;

        // **WHAT THE OBJECT COULD NOT GET OUT OF, as an inward normal along its own axis** - zero
        // when it resolved, which is almost always. `HeldItemClearance` reads it rather than casting
        // the span a second time: two casts of one line is two answers that can disagree, and the
        // one that decides where the object is DRAWN has to be the one that decides whether the
        // player may move.
        public Vector3 BlockedNormal { get; private set; }

        private Vector3 lastLevel = Vector3.forward;

        // LateUpdate, and after everything that could have moved it: the hand writes the hold pose in
        // its own pass and this replaces it. Two writes rather than one is the price of a pose the
        // hand's anchor cannot express.
        private void LateUpdate()
        {
            if (item == null) item = GetComponent<CarryableItem>();
            // **NOT WHILE IT IS SEATED.** `IsCarried` is true for an object in a SOCKET as well as one
            // in a hand, and a ladder standing in its mount that still posed itself off the player
            // walked out of the room with them. See `CarryableItem.Seated`.
            if (item == null || !item.IsCarried || item.Seated) { BlockedNormal = Vector3.zero; return; }

            Transform holder = Holder();
            if (holder == null) return;

            Vector3 flat = holder.forward;
            flat.y = 0f;
            // A holder somehow vertical has no facing to take. Keeping the last one costs nothing and
            // an object that flips to an arbitrary heading for a frame is very visible.
            if (flat.sqrMagnitude < 1e-4f) flat = lastLevel;
            flat.Normalize();
            lastLevel = flat;

            Quaternion level = Quaternion.LookRotation(flat, Vector3.up);
            Vector3 side = level * Vector3.right;

            transform.rotation = level * Quaternion.Euler(0f, yaw, 0f);
            transform.position = new Vector3(holder.position.x, holder.position.y + carryHeight,
                                             holder.position.z)
                               + flat * carryAhead + side * carrySide;

            GiveWay();
        }

        // **IT SLIDES BACK ALONG ITS OWN LENGTH RATHER THAN GOING THROUGH THE WALL.**
        //
        // Level carrying fixed the floor and the ceiling - the object cannot be tipped into either
        // any more - and left the walls, which is what was reported next. A six-metre ladder pointed
        // at a wall two metres away has four metres of itself inside it, and no amount of choosing
        // WHERE to hold it helps: the object is longer than the room is wide.
        //
        // So it gives way. The far end stops at the surface and the rest slides back past the
        // player, which is both the honest motion (that is what happens when you walk a ladder into
        // a wall) and free to look at: what goes behind the holder is behind the camera.
        //
        // **THE SAME IDEA `HeldItemClearance` ALREADY HAS, on the axis that matters here.** That one
        // pulls a small object in along the line from the eye to the hand, because for a key that is
        // the only direction there is. For something long the direction that gives way is its own
        // length, and pulling it toward the eye would just put six metres of ladder in the face.
        //
        // The player is still STOPPED - `HeldItemClearance.LongContact` does that, off the same span
        // - so this is not a way of walking a ladder into a wall. It is what the object does while
        // the player is being refused.
        private void GiveWay()
        {
            BlockedNormal = Vector3.zero;
            if (item.heldSpan == Vector3.zero) return;

            Vector3 span = transform.TransformVector(item.heldSpan);
            float length = span.magnitude;
            if (length < 0.05f) return;

            // **MEASURED OUTWARD FROM THE MIDDLE, NEVER FROM THE ENDS, and that is the whole of
            // this method being correct.**
            //
            // The obvious way to ask "is this end buried" is to stand at that end and cast. It was
            // written that way twice and it was wrong both times, for one reason: **a wall in this
            // building is 0.125m thick** (`SceneBuilder.WallDepth`). A six-metre ladder does not stop
            // inside a wall, it goes straight THROUGH it - so by the time the rear end is buried
            // enough to matter it is out the far side standing in the next room, and a cast from
            // there measures a completely different piece of air. It reported the wall's own back
            // face as an obstruction 0.1m in FRONT of that end, which reads as "the leading end is
            // buried six metres", so the object was shoved further backwards and the player was
            // refused FORWARD - the one direction that would have freed them. Play found it exactly
            // so: back into a wall and W stops working until you turn or drop it.
            //
            // The middle cannot have that problem. It is where the holder's hands are, so it is in
            // open air by construction, and it is on the near side of every surface the object is
            // pressed against. Two casts out of it answer the only questions there are - how much
            // room is there in front of the grip, and how much behind - and everything else falls
            // out of those.
            //
            // Its failure mode is the right one too. Should the middle somehow end up inside
            // something, both casts come back clear, nothing slides and nothing is blocked: the
            // object passes through a wall, which is a graphical fault. The end-based version's
            // failure mode was to refuse the player a direction, which is a trap.
            Vector3 dir = span / length;
            Vector3 mid = transform.position;
            float half = length * 0.5f;

            // Only ever asked out to where the answer could still change the result: past `maxSlide`
            // nothing more can be given, so a longer cast would cost time to learn nothing.
            float reach = half + maxSlide;
            float runFront = Clear(mid, dir, reach);
            float runBack = Clear(mid, -dir, reach);

            // THE WINDOW THE SLIDE HAS TO LAND IN. Sliding forward by s puts the leading end at
            // `half + s` from the grip and the trailing end at `half - s`, so s may be at most
            // `runFront - half` and must be at least `half - runBack`. Stated that way the three
            // cases the old code enumerated collapse into one clamp: zero if zero fits, and
            // otherwise the nearer edge of the window.
            float most = runFront - half;
            float least = half - runBack;

            // **AN EMPTY WINDOW IS A SPACE SHORTER THAN THE OBJECT** - a ladder turned across a
            // corridor - and no slide fixes it. Sit in the middle of the overlap so it sticks out
            // evenly at both ends rather than being shunted from one wall into the other every frame.
            float slide = least <= most ? Mathf.Clamp(0f, least, most)
                                        : (runFront - runBack) * 0.5f;

            slide = Mathf.Clamp(slide, -maxSlide, maxSlide);
            transform.position += dir * slide;

            // WHAT IS STILL PAST A SURFACE once it has given everything it can, and therefore which
            // way the player must not push.
            float leftFront = Mathf.Max(0f, half + slide - runFront);
            float leftBack = Mathf.Max(0f, half - slide - runBack);

            // **BOTH ENDS OVER IS NOT A REASON TO REFUSE ANYBODY.** Where the free run is shorter
            // than the object, walking either way along the axis leaves it exactly as buried - so a
            // refusal buys nothing and can only take away the player's way out. Blocked in ONE
            // direction at most, always, which is what makes this incapable of trapping anyone: the
            // opposite direction is open by construction.
            BlockedNormal = leftFront > 0.01f && leftBack > 0.01f ? Vector3.zero
                          : leftFront > 0.01f ? -dir
                          : leftBack > 0.01f ? dir
                          : Vector3.zero;
        }

        // How far a point can travel before it meets something, along one direction. Degenerate hits
        // are skipped for the reason `HeldItemClearance.Sweep` skips them: a cast that begins inside
        // a collider has not travelled, so it has no distance to report and no surface to report it
        // against. Casting from the GRIP should put that out of reach, and it is kept because "no
        // reading" is the answer that fails safe here - see `GiveWay`.
        private float Clear(Vector3 from, Vector3 dir, float distance)
        {
            int count = Physics.SphereCastNonAlloc(from, probeRadius, dir, hits, distance,
                                                   blockers, QueryTriggerInteraction.Ignore);

            float allowed = distance;
            for (int i = 0; i < count; i++)
            {
                Transform t = hits[i].collider != null ? hits[i].collider.transform : null;
                if (t == null) continue;
                // The holder, whose capsule a cast from the middle of the object starts inside, and
                // the object itself - whose colliders are off while it is held, so the second is belt
                // and braces.
                if (t.root.CompareTag("Player")) continue;
                if (t.IsChildOf(transform)) continue;
                if (hits[i].distance <= 0.0001f) continue;
                if (hits[i].distance < allowed) allowed = hits[i].distance;
            }

            return allowed;
        }

        private readonly RaycastHit[] hits = new RaycastHit[8];

        // WHOSE BODY IS CARRYING IT. Null when nobody is, which is when the object's own transform is
        // already the honest answer and this must not touch it.
        private Transform Holder()
        {
            if (item == null || !item.IsCarried) return null;
            if (item.HeldByGhost != null) return item.HeldByGhost.transform;

            Collider player = PlayerLookup.Collider;
            return player != null ? player.transform : null;
        }
    }
}
