using System.Collections.Generic;
using UnityEngine;

namespace IterationRoom
{
    // WHERE EVERYBODY WAS STANDING WHEN THEIR CYCLE ENDED.
    //
    // **THIS EXISTS FOR ONE SHOT AND IT IS THE POINT OF THE WHOLE ENDING.** The cable car climbs
    // back up through cycles 3, 2 and 1 on the way out, and an empty building would make that an
    // architectural tour. What is meant to be out there is the past selves - dozens of them, frozen
    // mid-errand in rooms the player has not seen for half an hour - because that is the only image
    // the game has that says what it was actually about. `EndingSequence` has been trying to say it
    // in one room for months: *"The ghosts are left standing. That is the last image of the run: the
    // people it took to get out."* This is that line, applied to the whole facility.
    //
    // WHY A SNAPSHOT AND NOT THE GHOSTS THEMSELVES. A cycle boundary destroys every ghost in the
    // building (`LoopManager.EndCycleState`), and it has to - their timelines are the only copy of
    // the recordings, 32 signal bits are re-dealt per cycle, and a live replayer from cycle 1 in
    // cycle 3's world would go on trying to open doors that are two storeys away. Keeping them alive
    // for the ending would mean keeping three cycles of replay running for the sake of a view. So
    // this keeps the one thing the view needs - a pose - and throws the machinery away on schedule.
    //
    // THE MOMENT CHOSEN IS THE BOUNDARY, and it is chosen rather than convenient. A ghost whose
    // timeline has run out stops where it stopped, so what this captures is every past self at the
    // instant that cycle was solved: some at the console, some still walking somewhere, some at a
    // pad they were holding for a version of the player that no longer needs them. Nobody is posed.
    //
    // Static for the same reason `RunTally` is: it has to outlive the scene objects it describes.
    public static class GhostArchive
    {
        // A past self, in world space. World rather than cycle-local because the cycles are laid out
        // in one continuous world (cycle 3's floor is 24.9m under cycle 1's) and the tour flies
        // through all of it - converting into and out of a cycle's frame would be two chances to be
        // wrong about a thing whose whole job is to be in exactly the right place.
        public readonly struct Pose
        {
            public readonly Vector3 Position;
            public readonly float Yaw;
            // Which cycle this past self belonged to, so the exterior can raise them a cycle at a
            // time as the car passes rather than all at once at the bottom.
            public readonly int Cycle;

            public Pose(Vector3 position, float yaw, int cycle)
            {
                Position = position;
                Yaw = yaw;
                Cycle = cycle;
            }
        }

        private static readonly List<Pose> poses = new List<Pose>();

        public static IReadOnlyList<Pose> Poses => poses;

        // Cleared with the run, from `LoopManager.Start`, alongside `RunTally.Begin` and for exactly
        // the same reason - static state and a domain reload that may not have happened.
        public static void Begin() => poses.Clear();

        // Called with the ghosts a cycle is about to destroy, BEFORE it destroys them. A ghost that
        // is already null is skipped rather than logged: the list `LoopManager` keeps can hold one
        // if something else tore it down, and a missing past self is a missing statue, not a fault.
        public static void Capture(IEnumerable<GhostReplayer> ghosts, int cycle)
        {
            if (ghosts == null) return;
            foreach (GhostReplayer ghost in ghosts)
            {
                if (ghost == null) continue;
                poses.Add(new Pose(ghost.transform.position, ghost.transform.eulerAngles.y, cycle));
            }
        }
    }
}
