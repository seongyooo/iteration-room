using UnityEngine;

namespace IterationRoom
{
    // The last doorway. Standing in it with the door open ends the run - this is the only exit
    // condition in the game, and the only thing that ever breaks LoopManager's while(true).
    //
    // The volume sits ON the threshold rather than past it, and that is not a shortcut. Behind that
    // doorway is the FarCap, and the pocket in front of it is 0.1m deep against a controller of
    // radius 0.3 - so there is physically no "through" to stand in and nothing to detect there. The
    // ending takes control on the frame this fires, so the player never reaches the wall they were
    // walking into and never learns it was there.
    //
    // Two gates, and both matter:
    //   - the door has to be OPEN, so walking up to a shut one ends nothing. The pads are what open
    //     it, which makes this read as "you got out" rather than "you arrived".
    //   - the X extent has to be the doorway's, not the room's. Without it a player idling in a far
    //     corner of Room3 is at the same Z as the opening, and the run would end from across the
    //     room the moment the fourth pad went down.
    public class EscapeTrigger : MonoBehaviour
    {
        public Door door;

        // Half-extents about this object's position, in world axes. Y is deliberately not tested:
        // the room has a ceiling and a floor, so anything at this X and Z is in the doorway
        // whatever its height, and a jump must not be a way to miss the end of the game.
        public float halfWidth = 0.7f;
        public float halfDepth = 0.5f;

        public bool PlayerEscaped { get; private set; }

        private Transform player;

        // Polled, like every other volume in this project - see FloorButton for why trigger
        // callbacks are not trustworthy across the loop's teleport. This one would survive them,
        // but two conventions for the same job is worse than one.
        private void FixedUpdate()
        {
            if (PlayerEscaped) return;
            if (door == null || !door.IsOpen) return;
            // Not during the wake-up or the blackout: the loop closes the doors after teleporting
            // the player, so there is a window where a door is briefly still open with the player
            // already back at the bed. Nowhere near this volume, but the guard costs nothing and
            // the failure it prevents would be a run ending itself for no visible reason.
            if (LoopManager.Instance != null && !LoopManager.Instance.IterationRunning) return;

            if (player == null)
            {
                GameObject go = GameObject.FindGameObjectWithTag("Player");
                if (go == null) return;
                player = go.transform;
            }

            Vector3 offset = player.position - transform.position;
            if (Mathf.Abs(offset.x) > halfWidth) return;
            if (Mathf.Abs(offset.z) > halfDepth) return;

            PlayerEscaped = true;
        }
    }
}
