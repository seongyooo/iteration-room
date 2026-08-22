using UnityEngine;

namespace IterationRoom
{
    // ONE CYCLE'S WORLD: the bed its iterations start from, the rooms they run through, and the
    // console that ends it. `LoopManager` holds an array of these and walks it - see `docs/cycle-design.md`.
    //
    // WHY THIS EXISTS. Every field below used to sit directly on `LoopManager`, one per room type,
    // wired once. That was exactly right while there was one bed and one corridor, and it stops being
    // right the moment a second cycle exists: `balloonField` cannot be *the* balloon field when two
    // of them are in the scene. The alternative was to swap `LoopManager`'s whole block of fields at
    // the boundary, which is the same coupling with a mutation step added.
    //
    // A cycle is NOT a chapter. Inside one, nothing about the game changes: one bed, sixty seconds,
    // ghosts accumulating forever. What a cycle bounds is the accumulation, not the rules.
    public class Cycle : MonoBehaviour
    {
        // EVERYTHING THIS CYCLE IS, as one transform to switch off.
        //
        // Only one cycle is awake at a time. Culling keeps an unseen cycle off the screen for free,
        // but it does not stop `Update` - and this scene runs about two hundred polling components,
        // most of them carryables and balloons, which tick whether or not anybody is in their cycle.
        //
        // Deactivating also unregisters the carryables under it, through `CarryableItem.OnDisable`.
        // That is the behaviour wanted rather than a side effect: `ItemRegistry.ReturnAllToOrigin`
        // should not be sweeping a cycle nobody can reach.
        public Transform worldRoot;

        public void SetAwake(bool awake)
        {
            if (worldRoot != null) worldRoot.gameObject.SetActive(awake);
        }

        // Where every iteration of this cycle begins. `WakeUpSequence` needs no counterpart - it
        // poses the eye wherever the player already is, so it works at any bed unchanged.
        public Transform bedSpawnPoint;

        // World state the loop rewinds. Doors close right after the teleport, drawers after the item
        // sweep; the two are separate calls below because that ordering is load-bearing.
        public Door[] doors;
        // Reset alongside the doors, and for the same reason at the same moment: it is a slab that
        // holds a position, and one left up is world state the loop forgot to rewind. Snapping it is
        // also what stops the reset being HEARD - it tracks its pads, so left alone it would slam
        // shut on its own a fraction after the ghosts are released, behind the closed eyelids.
        public CrushingBarrier[] barriers;
        public Drawer[] drawers;
        // Taps left running are world state exactly like an open drawer, and a good deal more visible:
        // a tap not shut here floods the next iteration on top of the last one's puddle.
        public WaterTap[] taps;
        // Buckets forget what they were holding, and stands forget what was standing on them. Both are
        // world state the item sweep does not cover: the sweep moves the OBJECT back and says nothing
        // about how full it was or what still believes it is occupied.
        public Bucket[] buckets;
        public BucketStand[] bucketStands;

        // This cycle's puzzle rooms. All optional: a cycle that has no balloon field simply leaves it
        // null, which is how a cycle can be built before its puzzles are designed.
        public BalloonField balloonField;
        public ChessBoard chessBoard;
        public CubeRoom cubeRoom;
        // Every room rule in this cycle. An array rather than one field per type, so a new puzzle
        // is an entry here instead of a line in this file and another in `ResetRooms`.
        public RoomCondition[] conditions;

        // The room this cycle ENDS in - `room<cycle>-0`, the hinge. Filling its console is the only
        // way out of a cycle, and `Completed` is what `LoopManager` watches for.
        public FinalRoomSequence finalRoom;

        // WHAT PUTS THE PLAYER OUT WHEN THEY ARRIVE IN THIS CYCLE. Named here rather than found by
        // type at runtime, and that distinction is the fix to a real fault: `CycleBinding.PointGasAt`
        // used to hand `SleepingGas` every ParticleSystem under the cycle root, which is the four wall
        // emitters AND every other particle system the cycle happens to own - room2-2's four tap
        // sprays among them. `Fill()` plays what it is given, so the boundary started water spraying
        // in a room nobody was in, and `Clear()` then stopped and cleared systems that belong to the
        // taps. `SceneBuilder` already knows exactly which four these are; this is where it says so.
        public ParticleSystem[] gasEmitters;

        // Everything a ghost can operate IN THIS CYCLE. An entry's index is its bit in
        // `RecordedFrame.signals`, so this array is a wire format: append, never reorder.
        //
        // **The 32-bit cap is per cycle rather than global**, and that is a consequence of the
        // boundary rather than a concession. Every ghost is destroyed when a cycle ends, so no
        // surviving timeline refers to these bits and the next cycle may number its own from zero.
        // What must never happen is mutating an array while ghosts born against it are still alive -
        // a bit would change meaning under them, and `GhostReplayer`'s edge tracking would leave the
        // old one set forever. `LoopManager` swaps the REFERENCE at a boundary, after the teardown.
        public GhostInteractable[] ghostInteractables;

        // This cycle's wall panels. Per cycle, because the `ERROR` test card spreading from the
        // console means *this bed's cycle is over* - a panel in a cycle the player has not reached
        // has no business failing, and the gather that builds these is by name, so it would
        // otherwise sweep up every floor at once.
        public WallPanelDisplay wallPanels;

        // Shuts every door. Called immediately after the teleport, never before it: a player standing
        // in a doorway has to be back at the bed already, or the slab closes through them.
        public void CloseDoors()
        {
            if (barriers != null)
                foreach (CrushingBarrier b in barriers) b?.ResetBarrier();

            // ~~THE COLOURED LEVERS AND PANELS~~ GONE 2026-08-21, replaced by the beam. Nothing
            // about that needs rewinding here: the mirrors are `CarryableItem`s, so
            // `ItemRegistry.ReturnAllToOrigin` already puts them back, and the beam is recomputed
            // from where they are every frame rather than being state anybody has to remember.

            if (doors == null) return;
            foreach (Door d in doors) d?.Close();
        }

        // The room-specific half of an iteration reset. Runs AFTER `ItemRegistry.ReturnAllToOrigin`,
        // and the order inside is the one CLAUDE.md §1.9 fixes:
        //
        // The sweep is what puts objects back; these are what forget who was home. Run the other way
        // round, every piece the sweep returned would still be marked seated and the room would open
        // on a puzzle it thought was already solved. Same argument for the balloon field, which hides
        // what it owns - a key handed back after that point would be visible outside its balloon.
        public void ResetRooms()
        {
            if (drawers != null)
                foreach (Drawer dr in drawers) dr?.Close();

            if (taps != null)
                foreach (WaterTap tap in taps) tap?.ShutOff();

            // AFTER the item sweep, like every other room reset: the sweep is what puts each bucket
            // back at its origin, and these are what forget that it was full and that a stand was
            // holding it. Run the other way round and a bucket would be returned and then immediately
            // re-seated by a stand that still thought it had one.
            if (buckets != null)
                foreach (Bucket b in buckets) b?.EmptyInstant();
            if (bucketStands != null)
                foreach (BucketStand s in bucketStands) s?.ResetStand();

            balloonField?.ResetField();
            finalRoom?.ResetRoom();
            chessBoard?.ResetBoard();
            cubeRoom?.ResetRoom();
            // Room rules are world state exactly like a door's position: left standing, a latched
            // chorus would still be latched and every pad would start the next iteration wherever the
            // last one finished, with the ghosts replaying on top of that.
            if (conditions == null) return;
            foreach (RoomCondition c in conditions) c?.ResetCondition();
        }

        // All three objects are in this cycle's console. The one way out of a cycle.
        public bool Complete => finalRoom != null && finalRoom.Completed;
    }
}
