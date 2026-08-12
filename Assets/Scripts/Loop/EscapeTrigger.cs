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
        public Door door;

        // Half-extents about this object's position, in world axes. Y is deliberately not tested:
        // the room has a ceiling and a floor, so anything at this X and Z is in the doorway whatever
        // its height, and a jump must not be a way to miss it.
        public float halfWidth = 0.7f;
        public float halfDepth = 0.5f;

        public bool PlayerArrived { get; private set; }

        public void Rearm() => PlayerArrived = false;

        // Polled, like every other volume in this project - see FloorButton for why trigger
        // callbacks are not trustworthy across the loop's teleport. This one would survive them,
        // but two conventions for the same job is worse than one.
        private void FixedUpdate()
        {
            if (PlayerArrived) return;
            if (door == null || !door.IsOpen) return;
            // Not during the wake-up or the blackout: the loop closes the doors after teleporting
            // the player, so there is a window where a door is briefly still open with the player
            // already back at the bed. Nowhere near this volume, but the guard costs nothing.
            if (LoopManager.Instance != null && !LoopManager.Instance.IterationRunning) return;

            Collider playerCollider = PlayerLookup.Collider;
            if (playerCollider == null) return;

            Vector3 offset = playerCollider.transform.position - transform.position;
            if (Mathf.Abs(offset.x) > halfWidth) return;
            if (Mathf.Abs(offset.z) > halfDepth) return;

            PlayerArrived = true;
        }
    }
}
