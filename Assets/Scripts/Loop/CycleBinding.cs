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
        public SleepingGas sleepingGas;
        public ControlHintDisplay hints;

        // Wall signs live in a room and are cued by the PA, which does not. Same for the end-cycle
        // control they retire against, which is on the HUD canvas.
        public EndCycleControl endCycleControl;
        // Putting a chess piece back is a control ON THE PLAYER pointed at a board in a room, so it
        // crosses in the other direction from most of these.
        public ChessPlacer placer;
        // Room2's wordless sign retires on the first POP rather than on the first visit, so it points
        // at the swing tool - which is on the player.
        public BalloonTool swingTool;

        // The E fixtures that live OUTSIDE any cycle - the calibration room's start button, which runs
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
                if (wayOuts != null && i < wayOuts.Length && cycles[i] != null && cycles[i].finalRoom != null)
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
        }

        // THE WAY OUT OF EACH CYCLE. `CycleExit` sits on the join between two storeys rather than
        // inside either one, so it is found across the whole scene rather than under a cycle root.
        // Its `player` is the one field on it that points at the core.
        private void BindExits()
        {
            foreach (CycleExit exit in FindObjectsByType<CycleExit>(FindObjectsInactive.Include, FindObjectsSortMode.None))
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
            if (sleepingGas == null || cycle == null || cycle.worldRoot == null) return;

            ParticleSystem[] emitters = cycle.worldRoot.GetComponentsInChildren<ParticleSystem>(true);
            if (emitters.Length > 0) sleepingGas.emitters = emitters;
        }

    }
}
