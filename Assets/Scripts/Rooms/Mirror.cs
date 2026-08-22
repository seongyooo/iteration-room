using UnityEngine;

namespace IterationRoom
{
    // A MIRROR. The only thing in cycle 3 that bends the beam, and the only thing in this project
    // whose useful state is WHERE A PERSON IS STANDING AND WHICH WAY THEY ARE FACING.
    //
    // **THAT IS THE WHOLE DESIGN, and it is why this replaced a lever.** A lever is a switch, and a
    // switch remembers a decision - so a past self who threw it wrongly goes on throwing it wrongly
    // for the rest of the cycle. A mirror remembers nothing. A past self holding one is a past self
    // standing somewhere facing somewhere, which is exactly what `RecordedFrame` already stores, and
    // which cannot be a mistake that accumulates: at worst it is a mirror pointing somewhere useless,
    // and you can walk up and take it out of their hands.
    //
    // It also makes a ghost into MATTER rather than labour. Everywhere else in the building a past
    // self runs an errand - carries a key, holds a pad, pops a balloon. Here the solution is made of
    // people: five mirrors is five past selves standing in a line through the building, each holding
    // one, passing the light along.
    //
    // **ONE MIRROR IS ONE PAST SELF**, and that follows from two rules that were already here: a
    // dropped carryable falls to the floor (`FallingItem`), and the player carries exactly one object
    // (CLAUDE.md §4). So a mirror only bends the beam while somebody is holding it, and nobody can
    // hold two.
    public class Mirror : MonoBehaviour
    {
        // THE REFLECTING FACE, and everything visible hangs off it. Its +Z is the normal.
        //
        // It is a child rather than this object because the two need different rotations: this root
        // is parented to a hand and inherits the holder's PITCH, and the mirror must not. See
        // `LevelTheFace`.
        public Transform face;

        // The disc's radius, used for the ray test. Measured off the model at build time rather than
        // guessed, so a differently scaled mirror cannot silently reflect from thin air.
        public float radius = 0.28f;

        // **SINGLE SIDED, by request, and it is a rule rather than a saving.** The back is opaque: a
        // beam that arrives at it stops there. So a mirror turned the wrong way does not merely fail
        // to help, it blocks - which is the one way a mirror can be in the way, and it is instantly
        // legible because you can see the beam stop.
        //
        // It also means the mirror in your own hands shows you nothing, because its face points away
        // from you. **You only ever see yourself in somebody else's mirror**, which is the better
        // image and happens to be the cheaper one to render.
        public Renderer backRenderer;

        private static readonly System.Collections.Generic.List<Mirror> all
            = new System.Collections.Generic.List<Mirror>();

        public static System.Collections.Generic.List<Mirror> All => all;

        private void OnEnable() => all.Add(this);
        private void OnDisable() => all.Remove(this);

        public Vector3 Centre => face != null ? face.position : transform.position;
        public Vector3 Normal => face != null ? face.forward : transform.forward;

        // **WHILE SOMEBODY IS HOLDING IT, THE GLASS STAYS AT BEAM HEIGHT.**
        //
        // The hold anchor is parented under the camera (`PlayerHand`), so a held item rides the view -
        // look at your feet and it goes with them. For every other object in the building that is
        // exactly right. For this one it means the beam sails over the mirror the moment you glance
        // down, which is not a puzzle, it is a fight with the mouse.
        //
        // So the face keeps the holder's x and z and takes its height from here. It costs nothing
        // visually - there are no arms to look detached from - and it makes the horizontal beam an
        // enforced rule rather than a hope: every mirror in play is on the beam's plane, so the puzzle
        // is the floor plan and nothing else, which is what horizontal was chosen for.
        //
        // Set by `SceneBuilder` from the same constant the emitter is placed at, so the two cannot
        // drift apart.
        public bool holdAtBeamHeight = true;
        public float beamHeight = 1.2f;

        // How far in front of the holder the glass sits. Both hands, chest height, out where it can
        // be seen past - not tucked into a fist.
        public float carryReach = 0.55f;

        // **THE REACH VOLUME FOLLOWS THE GLASS.** `CarryableItem` finds its own trigger with
        // `GetComponent<Collider>()`, so it is on the root - which is a wrist bone while a ghost holds
        // this, a metre from the mirror anybody is actually looking at. Moved every frame to wherever
        // the glass is, so "near enough to take it" means the same thing as "that is the mirror".
        public BoxCollider reach;

        [System.NonSerialized] public CarryableItem carryable;

        private Vector3 lastLevel = Vector3.forward;

        // **THE FACE IS KEPT LEVEL EVERY FRAME, and that is what makes the beam horizontal.**
        //
        // The beam is horizontal by decision (2026-08-21), and the reason is in the recording format:
        // `RecordedFrame` stores `position`, `yaw` and `signals` - there is no pitch anywhere in a
        // timeline. A mirror that could tilt would replay at the wrong angle in a ghost's hands, and
        // adding pitch is a real change to four files. Level, a ghost's recorded yaw is exactly
        // enough and the replay is exact for free.
        //
        // Kept level VISUALLY as well as optically, or the two would disagree: the disc you see would
        // tip with your head while the light did not. So the whole model hangs off `face`, and the
        // mirror stands upright in your hands however you are looking. You turn it by turning your
        // body, which is also how you would hold a mirror.
        private void LateUpdate() => Sync();

        // **THE GLASS IS A PURE FUNCTION OF THE HOLDER'S POSITION AND YAW, and that is not a
        // simplification - it is the only thing a timeline can reproduce.** A recorded frame holds
        // `position`, `yaw` and `signals`. Anything the optics depend on that is not one of those
        // three cannot come back the same way twice.
        //
        // **It was reading its own transform, and play found what that meant.** A carried item is
        // parented to the holder's hand: for a ghost that is `MiddleHand.R` **on the animated
        // skeleton**, so the mirror's facing was whatever the idle animation was doing with a wrist -
        // twisted, and moving. A past self set up carefully in the right spot bent the light
        // somewhere else, and somewhere else again a second later. For the player it was subtler and
        // still wrong: the hand anchor rides the camera, so the glass tipped with the head.
        //
        // Off the holder's BODY, both are exact and identical. The player's root carries the yaw
        // `HandleLook` writes and no pitch at all; a ghost's carries the yaw off its own recording.
        public void Sync()
        {
            if (face == null) return;
            if (carryable == null) carryable = GetComponent<CarryableItem>();

            Transform holder = Holder();
            Vector3 source = holder != null ? holder.forward : transform.forward;

            Vector3 flat = source;
            flat.y = 0f;
            // A loose mirror standing on the floor is upright, so this only bites for a held one when
            // the holder is somehow vertical. Kept because the fallback costs nothing and a beam that
            // vanishes is very hard to explain.
            if (flat.sqrMagnitude < 1e-4f) flat = lastLevel;

            flat.Normalize();
            lastLevel = flat;
            face.rotation = Quaternion.LookRotation(flat, Vector3.up);

            if (holder != null && holdAtBeamHeight)
            {
                Vector3 at = holder.position;
                face.position = new Vector3(at.x, beamHeight, at.z) + flat * carryReach;
            }
            else face.position = transform.position;

            if (reach != null) reach.center = transform.InverseTransformPoint(face.position);
        }

        // WHOSE BODY IS AIMING IT. Null when it is standing on the floor, which is when the object's
        // own transform is the honest answer.
        private Transform Holder()
        {
            if (carryable == null || !carryable.IsCarried) return null;
            if (carryable.HeldByGhost != null) return carryable.HeldByGhost.transform;

            Collider player = PlayerLookup.Collider;
            return player != null ? player.transform : null;
        }

        // WHERE A RAY MEETS THIS DISC, if it does.
        //
        // **Analytic, not a raycast, and that is not an optimisation - it is the only thing that
        // works.** Ghosts have no colliders at all (CLAUDE.md §1.7) and a carried item's collider is
        // switched off while it is held, so the mirrors that matter most - the ones in people's hands
        // - are invisible to physics. A plane and a radius are visible to arithmetic.
        public bool Raycast(Vector3 origin, Vector3 dir, float maxDistance, out float distance,
                            out bool frontFace)
        {
            distance = 0f;
            frontFace = false;

            Vector3 n = Normal;
            float denom = Vector3.Dot(dir, n);
            // Edge on. A disc with no thickness is not hit by a ray running along it, and letting the
            // division through would return an infinity.
            if (Mathf.Abs(denom) < 1e-5f) return false;

            float t = Vector3.Dot(Centre - origin, n) / denom;
            // The 1mm is what stops a reflected ray immediately re-hitting the mirror it just left.
            if (t <= 0.001f || t >= maxDistance) return false;

            Vector3 point = origin + dir * t;
            if ((point - Centre).sqrMagnitude > radius * radius) return false;

            distance = t;
            // Arriving against the normal means arriving at the silvered side.
            frontFace = denom < 0f;
            return true;
        }
    }
}
