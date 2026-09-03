using System.Collections.Generic;
using UnityEngine;

namespace IterationRoom
{
    // RE-ESTABLISHES EVERY REFERENCE THAT CROSSES A CYCLE, at startup, by lookup rather than by
    // serialization.
    //
    // WHY THIS EXISTS, when `SceneBuilder` already wires all of it correctly. It is the groundwork for
    // the per-cycle scene split (`docs/cycle-design.md` §7b): **Unity silently drops a serialized
    // reference that points into another scene.** Not an error, not a warning - the field is simply
    // null at runtime, and the symptom is "the console will not take the object" or "the gas never
    // fires", found by playing rather than by building. A split done by moving objects into scenes and
    // hoping is a split that fails that way, once per reference, in whatever order they are met.
    //
    // So the order is reversed: every one of them is rebound HERE first, while everything is still in
    // one scene and the game demonstrably works. `SceneBuilder`'s own wiring stays exactly as it is and
    // this writes the same values over the top - which is the point, because it means this file can be
    // verified against a game that already runs. Once the scenes are actually split, the serialized
    // halves come back null and nothing changes, because nothing was reading them by then.
    //
    // It runs in `Awake`. `LoopManager.Start` is what reads `cycles`, and Unity runs every `Awake`
    // before any `Start`, so the binding is always in place before the loop asks for it.
    public class CycleBinding : MonoBehaviour
    {
        public LoopManager loop;
        public PlayerHand hand;
        public CameraShaker cameraShaker;
        public NarrationDirector narration;
        public Transform player;

        // Handed to `EndingDeparture` for the last beat, where the player wakes in room1-1. Both
        // live in the core scene and the departure lives in cycle 3's, so they cross a scene
        // boundary and have to be bound here like everything else in this file.
        public WakeUpSequence wakeUp;
        public IterationLabel iterationLabel;
        public SleepingGas sleepingGas;
        public ControlHintDisplay hints;

        // Wall signs live in a room and are cued by the PA, which does not. Same for the end-cycle
        // control they retire against, which is on the HUD canvas.
        public EndCycleControl endCycleControl;
        // Putting a chess piece back is a control ON THE PLAYER pointed at a board in a room, so it
        // crosses in the other direction from most of these.
        public ChessPlacer placer;
        // And the bucket's, which crosses the same way for the same reason - see BucketPlacer.stands.
        public BucketPlacer bucketPlacer;
        // And room3-2N's cube, the same direction again.
        public BedlamPlacer bedlamPlacer;
        // And the ladder's, the same direction again.
        public LadderPlacer ladderPlacer;
        // Room2's wordless sign retires on the first POP rather than on the first visit, so it points
        // at the swing tool - which is on the player.
        public BalloonTool swingTool;

        // Where a past self is parented. Core-scene, and the ending's statues need it - see the
        // departure block in `Bind`.
        public Transform ghostParent;

        // The E fixtures that live OUTSIDE any cycle. **EMPTY SINCE 2026-08-31**, when the calibration
        // room took the only one with it; kept because it is the right shape for the next fixture that
        // needs it. It held the start button, which ran
        // before the first iteration. Cycle fixtures are gathered per cycle below and appended to it.
        public MonoBehaviour[] coreHintTargets;

        // THE WAY OUT OF EACH CYCLE, one per entry in the cycle list and in the same order. These sit
        // on the JOIN between two storeys rather than inside either, so they stay in the core scene
        // while the console that opens them moves out - which makes `finalRoom.wayOut` a reference
        // across the split like any other. A null entry is a cycle with no successor, and that is what
        // makes it the last one; see LoopManager.
        public CycleExit[] wayOuts;

        // CALLED BY `LoopManager`, not run from `Awake`. The cycles live in scenes of their own now and
        // arrive a frame or more after this object does, so there is nothing to bind at `Awake` time -
        // and binding "when everything is present" is a thing only the loop knows, because it is the
        // loop that waits for the loader. See `LoopManager.Start`.
        public void BindAll(Cycle[] cycles)
        {
            if (loop == null) loop = GetComponent<LoopManager>();
            if (cycles == null) return;

            for (int i = 0; i < cycles.Length; i++)
            {
                Bind(cycles[i]);

                // The console's way out, matched by position. Left alone when nothing is supplied, so
                // the last cycle keeps the null that is what MAKES it the last cycle.
                //
                // A NULL ENTRY LEAVES WHATEVER IS ALREADY THERE, which is what the line above has
                // always claimed and did not do. It mattered the moment cycle 2 grew a way out of its
                // own: that hatch lives INSIDE room2-0 rather than on a seam between two storeys, so
                // its `finalRoom.wayOut` is an ordinary reference within one scene - and a `wayOuts`
                // array that arrives here with a null in that slot (because the entry it names is in
                // another scene and Unity dropped it) would overwrite a correct reference with
                // nothing. Assigning only what is actually supplied cannot do that.
                if (wayOuts != null && i < wayOuts.Length && wayOuts[i] != null
                    && cycles[i] != null && cycles[i].finalRoom != null)
                    cycles[i].finalRoom.wayOut = wayOuts[i];
            }

            BindExits();
            RebindHints(cycles);
        }

        // Everything inside one cycle that points OUT of it. Gathered with `true` for inactive,
        // because every cycle but the first starts asleep and `GetComponentsInChildren` skips a
        // disabled subtree by default - which would quietly bind cycle 1 and no other.
        private void Bind(Cycle cycle)
        {
            if (cycle == null || cycle.worldRoot == null) return;

            foreach (FinalSlot slot in cycle.worldRoot.GetComponentsInChildren<FinalSlot>(true))
                slot.hand = hand;

            foreach (SymbolSlot slot in cycle.worldRoot.GetComponentsInChildren<SymbolSlot>(true))
                slot.hand = hand;

            foreach (FinalRoomSequence final in cycle.worldRoot.GetComponentsInChildren<FinalRoomSequence>(true))
            {
                final.cameraShaker = cameraShaker;
                final.narration = narration;

                // AND THE RECESSES POINT BACK AT IT. Strictly an intra-scene reference, so
                // `SceneBuilder` wiring it is enough and this writes the same value over the top -
                // which is this class's whole job (see the header). It is here because room2-0
                // shipped with all four null and the symptom was a console that silently refused
                // every press: `FinalSlot.Live` is false without it, so nothing prompts and nothing
                // acts. A room that cannot be finished is not a thing to leave to one call site.
                if (final.slots == null) continue;
                foreach (FinalSlot slot in final.slots)
                    if (slot != null) slot.sequence = final;
            }

            // The wall signs. Both of their references point out of the room they hang in - the PA
            // that reads them aloud, and the control they retire against once it has been used.
            foreach (PanelMessage message in cycle.worldRoot.GetComponentsInChildren<PanelMessage>(true))
            {
                message.narration = narration;
                if (message.retireOnEndCycle != null || endCycleControl != null)
                    message.retireOnEndCycle = endCycleControl;
                // Only the balloon sign has this, and it must not be handed one where it had none -
                // a sign that retires on a pop it was never about would go up and never come down.
                if (message.retireOnPop != null && swingTool != null) message.retireOnPop = swingTool;
            }

            // THE OTHER DIRECTION: a control on the player, pointed at a board in this cycle. Only
            // assigned when this cycle actually has one, or cycle 2 would clear cycle 1's.
            if (placer != null && cycle.chessBoard != null) placer.board = cycle.chessBoard;

            // The bucket's targets, the same direction and the same guard. `Cycle` already gathers the
            // stands for its own reset, so this reuses that array rather than walking the cycle again;
            // the tanks are found here, because one room having one of something is not yet a reason
            // for every cycle to hold a field for it (the same line TreeTrunk is on, below).
            if (bucketPlacer != null && cycle.bucketStands != null && cycle.bucketStands.Length > 0)
            {
                bucketPlacer.stands = cycle.bucketStands;
                bucketPlacer.tanks = cycle.worldRoot.GetComponentsInChildren<WaterTank>(true);
            }

            // HOW CYCLE 3 BREAKS, and it crosses the boundary in the ordinary direction: the
            // sequence lives in the cycle and reaches OUT for the shaker it drives and the PA it
            // speaks through. The same two `FinalRoomSequence` already needed, for the same reason.
            //
            // `wallPanels` is handed over from the cycle rather than left serialised, because the
            // display is assembled per cycle and there is exactly one right answer per cycle - the
            // same value `Cycle.wallPanels` holds.
            foreach (FacilityFailure failure in cycle.worldRoot.GetComponentsInChildren<FacilityFailure>(true))
            {
                failure.cameraShaker = cameraShaker;
                failure.narration = narration;
                if (cycle.wallPanels != null) failure.wallPanels = cycle.wallPanels;
            }

            // Room3-2N's cube, the same direction and the same guard: assigned only when this cycle
            // actually has one, or an earlier cycle would clear cycle 3's. Found by type rather than
            // carried on `Cycle`, because one room having one of something is not yet a reason for
            // every cycle to hold a field for it - the same line the tree is on, below.
            BedlamCube bedlam = cycle.worldRoot.GetComponentInChildren<BedlamCube>(true);
            if (bedlamPlacer != null && bedlam != null) bedlamPlacer.cube = bedlam;

            // The ladder's mount, and the PLAYER it moves. That second one is the other direction -
            // a fixture in the cycle reaching out for the controller it turns into a climbing one -
            // and it is why a ladder in a scene of its own does nothing until this runs.
            LadderMount mount = cycle.worldRoot.GetComponentInChildren<LadderMount>(true);
            if (mount != null)
            {
                mount.player = player != null ? player.GetComponent<FirstPersonController>() : null;
                if (ladderPlacer != null) ladderPlacer.mount = mount;
            }

            // THE ENDING'S DEPARTURE, and it crosses in BOTH directions at once - which is why it is
            // bound here rather than trusted to `SceneBuilder`, which wires all of it correctly and
            // then watches Unity throw half of it away on save.
            //
            // Out of the cycle: the shaker the breach rattles, the player the board waits for, the
            // player the car carries and the controller it stops. Every one of those lives in the
            // core scene. Into the cycle: nothing - `EndingDeparture` is reached through
            // `Cycle.departure`, which is a reference within one scene and needs no help.
            //
            // Read `cross-scene-report.txt` after touching any of this (CLAUDE.md 3). Cycle 3 is the
            // only cycle that has a departure, so everything below is guarded on finding one rather
            // than on which cycle this is.
            EndingDeparture departure = cycle.worldRoot.GetComponentInChildren<EndingDeparture>(true);
            if (departure != null)
            {
                cycle.departure = departure;
                departure.cameraShaker = cameraShaker;
                // The last beat of the ending wakes the player in room1-1 - see
                // `EndingDeparture.ReturnToTheStart`. All three live in the core scene, so like
                // every other reference here they are handed over rather than found.
                departure.wakeUp = wakeUp;
                departure.controller = player != null ? player.GetComponent<FirstPersonController>() : null;
                departure.iterationLabel = iterationLabel;
                // Which cycle this is, so the exterior can leave the player's own rooms alone, and
                // the player, whose height is what says they have finished the climb down.
                departure.occupied = cycle;
                departure.player = player;
                departure.narration = narration;
                // The break. It no longer holds anything back (see `FacilityFailure.held`, reverted
                // 2026-09-01) - the reference is kept because the hold is one bool away if it is ever
                // wanted, and re-finding it across a scene boundary is the expensive half.
                departure.failure = cycle.worldRoot.GetComponentInChildren<FacilityFailure>(true);

                if (departure.board != null)
                {
                    departure.board.player = player;
                    departure.board.narration = narration;
                }
                if (departure.car != null)
                {
                    departure.car.player = player;
                    departure.car.controller =
                        player != null ? player.GetComponent<FirstPersonController>() : null;
                }
                // The ghost prefab is a PREFAB ASSET rather than a scene object, so it survives the
                // split on its own and is left alone here. Its PARENT does not - `Ghosts` is an
                // object in the core scene - so the statues are re-pointed at it.
                if (departure.exterior != null) departure.exterior.ghostParent = ghostParent;
            }

            // The tree's left-click disc, the same direction and the same guard: assigned only when
            // this cycle actually has a tree, or cycle 1 would clear cycle 2's. Found by type rather
            // than carried on `Cycle`, because one room having one of something is not yet a reason
            // for every cycle to hold a field for it.
            TreeTrunk tree = cycle.worldRoot.GetComponentInChildren<TreeTrunk>(true);
            if (hints != null && tree != null) hints.treeTrunk = tree;
        }

        // THE WAY OUT OF EACH CYCLE. `CycleExit` sits on the join between two storeys rather than
        // inside either one, so it is found across the whole scene rather than under a cycle root.
        // Its `player` is the one field on it that points at the core.
        private void BindExits()
        {
            foreach (CycleExit exit in FindObjectsByType<CycleExit>(FindObjectsInactive.Include))
                exit.player = player;
        }

        // THE PROMPT LIST, which is core -> cycle and the one that has already gone wrong once: cycle
        // 2's light switches shipped missing from it, which is a fixture with no mark on it at all.
        // Rebuilt from the cycles themselves so a fixture joins by existing rather than by being
        // remembered at its call site.
        private void RebindHints(Cycle[] cycles)
        {
            if (hints == null) return;

            var targets = new List<MonoBehaviour>();
            if (coreHintTargets != null) targets.AddRange(coreHintTargets);

            foreach (Cycle cycle in cycles)
            {
                if (cycle == null || cycle.worldRoot == null) continue;
                foreach (MonoBehaviour mb in cycle.worldRoot.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (mb is IInteractHintTarget) targets.Add(mb);
                }
            }

            // Through SetTargets, not the field: the display casts the array to the interface once in
            // its own Awake, and writing the field alone would leave it caching whatever SceneBuilder
            // serialized. See ControlHintDisplay.SetTargets.
            hints.SetTargets(targets.ToArray());
        }

        // THE GAS BELONGS TO THE CYCLE IT PUTS YOU OUT IN, and until this ran it did not: `SceneBuilder`
        // hands `SleepingGas` cycle 2's emitters once, at build time, so the boundary out of a LATER
        // cycle would gas the player with room2-1's emitters one storey up. Harmless with two cycles
        // because cycle 2's boundary has nowhere to go yet, and a real fault the moment cycle 3 exists.
        //
        // Pointed at the cycle being ENTERED, and repointed at each boundary - see
        // `LoopManager.EndCycleState`.
        public void PointGasAt(Cycle cycle)
        {
            if (sleepingGas == null || cycle == null) return;

            // THE CYCLE'S OWN LIST, not every particle system under it. Gathering by type swept up the
            // tap sprays with the wall emitters - see Cycle.gasEmitters. A cycle that names none keeps
            // whatever was already wired rather than being handed the wrong thing.
            if (cycle.gasEmitters != null && cycle.gasEmitters.Length > 0)
                sleepingGas.emitters = cycle.gasEmitters;
            else
                Debug.LogWarning($"[CycleBinding] '{cycle.name}' declares no gas emitters - the boundary "
                    + "into it will fire whatever was last wired.");
        }

    }
}
