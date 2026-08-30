using UnityEngine;

namespace IterationRoom
{
    // The last doorway. Standing in it with the door open says THE PLAYER HAS REACHED ROOM4 - and
    // that is all it says now.
    //
    // IT USED TO STOP THE LOOP, and that was the whole exit condition of the game. It is not any
    // more: the clock keeps running through Room4, and what ends a run is putting all three escape
    // objects into the console in there. Reaching the last room is no longer an achievement, it is a
    // journey with sixty seconds on it like every other.
    //
    // What this arms is the console, which rises as the player comes through. A LATCH rather than a
    // live test, because the player walks through this volume in half a second and then stands in
    // the room beyond it - a console that only stayed up while somebody was in the doorway would
    // sink the moment they went to use it. `Rearm` is called by the loop at the top of an iteration,
    // which is what makes reaching Room4 something each run has to do for itself.
    //
    // The volume sits ON the threshold rather than past it, which is now simply where a doorway is.
    //
    // Two gates, and both still matter:
    //   - the door has to be OPEN, so walking up to a shut one arms nothing. The pads are what open
    //     it, which keeps this reading as "you got through" rather than "you arrived".
    //   - the X extent has to be the doorway's, not the room's. Without it a player idling in a far
    //     corner of Room3 is at the same Z as the opening, and the console would come up from across
    //     the room the moment the second pad went down.
    public class EscapeTrigger : MonoBehaviour
    {
        // BalloonField's pattern, for the same reason: a ghost has no collider to trip this with,
        // so it has to be handed the position itself - see TryArm and GhostReplayer.Tick.
        public static EscapeTrigger Instance { get; private set; }

        public Door door;

        // Half-extents about this object's position, in world axes. Y is deliberately not tested:
        // the room has a ceiling and a floor, so anything at this X and Z is in the doorway whatever
        // its height, and a jump must not be a way to miss it.
        public float halfWidth = 0.7f;
        public float halfDepth = 0.5f;

        // **AND Y, WHEN THERE IS A ROOM UNDERNEATH.** The note above is right for a building laid
        // out in one plane and became wrong the moment cycle 3 stacked room3-0 on top of room3-2N:
        // untested, this volume also covers the deck two storeys below it, so standing at the
        // ladder's foot armed the console upstairs. Zero keeps the old behaviour, which is what
        // every doorway in cycles 1 and 2 wants.
        public float halfHeight = 0f;

        // **WHETHER ARRIVING HAS TO BE THROUGH A DOOR.** It always did, because until room3-0 every
        // final room had one — and this fixture is placed IN the doorway, so "the door is open" is
        // just a cheap way of saying "this is a threshold somebody could be standing in".
        //
        // Room3-0 has no doorway at all: the only way in is the hole in its own floor, which you
        // climb into. `door` is therefore null there, and the test at the top of `TryArm` returned
        // on the first line every time — so `PlayerArrived` never became true, `FinalRoomSequence`
        // was never `Active`, and **the console refused the cube for ever**, with no prompt to say
        // why. Reported from play as exactly that.
        public bool requiresOpenDoor = true;

        public bool PlayerArrived { get; private set; }

        public void Rearm() => PlayerArrived = false;

        // Says it happened without anyone walking through the doorway. Only the editor test jump
        // uses this - it drops the player straight into the last room, which means none of the
        // conditions TryArm checks were ever met on the way.
        public void ForceArrived() => PlayerArrived = true;

        // CLEARED ON THE WAY OUT, the pattern ChessBoard already uses. A second instance silently
        // steals the static - and there is one of these per cycle, so the cycle being ENTERED takes
        // it at the boundary, which is right. What was missing is the other half: a cycle going to
        // sleep must not leave a pointer to its own trigger behind if it happened to be the holder.
        private void Awake() => Instance = this;
        private void OnDisable() { if (Instance == this) Instance = null; }

        // Polled, like every other volume in this project - see FloorButton for why trigger
        // callbacks are not trustworthy across the loop's teleport. This one would survive them,
        // but two conventions for the same job is worse than one.
        private void FixedUpdate()
        {
            Collider playerCollider = PlayerLookup.Collider;
            if (playerCollider == null) return;
            TryArm(playerCollider.transform.position);
        }

        // Shared by the living player's own poll above and a replaying ghost's, in
        // GhostReplayer.Tick. WITHOUT THIS SECOND CALLER, Room4's console could only ever arm on
        // the CURRENT iteration's own living player crossing this threshold - so a past self that
        // genuinely delivered an escape object here, in its own original run, could never
        // successfully replay that delivery unless the living player independently reached Room4
        // again in the SAME later iteration. Every other socket a ghost delivers into (a key's
        // lock, a chess square, a cube's recess) re-evaluates a condition ghosts themselves can
        // satisfy; this one used to re-evaluate a condition only the player could, which is exactly
        // the "weaker fact that correlates with it" CLAUDE.md's replay invariant warns against - the
        // fact that actually enabled the ORIGINAL delivery was "something crossed this doorway with
        // the door open", not "the living player, specifically, did".
        public void TryArm(Vector3 position)
        {
            if (PlayerArrived) return;
            if (requiresOpenDoor && (door == null || !door.IsOpen)) return;
            // Not during the wake-up or the blackout: the loop closes the doors after teleporting
            // the player, so there is a window where a door is briefly still open with the player
            // already back at the bed. Nowhere near this volume, but the guard costs nothing.
            if (LoopManager.Instance != null && !LoopManager.Instance.IterationRunning) return;

            Vector3 offset = position - transform.position;
            if (Mathf.Abs(offset.x) > halfWidth) return;
            if (Mathf.Abs(offset.z) > halfDepth) return;
            if (halfHeight > 0f && Mathf.Abs(offset.y) > halfHeight) return;

            PlayerArrived = true;
        }
    }
}
