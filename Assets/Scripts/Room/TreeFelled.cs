using UnityEngine;

namespace IterationRoom
{
    // The tree hall's rule: the tree is down, so there is a way across the pit.
    //
    // A SEPARATE COMPONENT FROM `TreeTrunk` because the two base classes are both abstract
    // MonoBehaviours and nothing can be a `GhostInteractable` and a `RoomCondition` at once. The
    // split is not merely tolerated - it is the §2 boundary drawn where it belongs: `TreeTrunk` owns
    // one tree's own state, and this owns what the room does about it.
    //
    // WHY THE DOOR HANGS ON THIS AT ALL, when the pit already stops the player physically: so that
    // the door opening is the FEEDBACK for the tree landing. A door that was open all along says
    // nothing when the bridge arrives, and this room's whole payoff is the moment several past
    // selves finish something together.
    public class TreeFelled : RoomCondition
    {
        public TreeTrunk trunk;

        public override bool Satisfied => trunk != null && trunk.HasFallen;

        // Standing the tree back up is part of the same rewind as shutting the door, so it runs
        // here rather than from a second reset hook nobody would remember to call - see
        // Cycle.ResetRooms for the ordering, and CLAUDE.md §1.9 for why it has to follow the sweep.
        public override void ResetCondition()
        {
            if (trunk != null) trunk.ResetTree();
        }
    }
}
