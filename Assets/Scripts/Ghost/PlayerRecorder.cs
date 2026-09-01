using System.Collections.Generic;
using UnityEngine;

namespace IterationRoom
{
    // Samples the player's transform and interaction signals at a fixed interval while a loop is
    // running, so the result can be handed to a GhostReplayer for exact playback next iteration.
    public class PlayerRecorder : MonoBehaviour
    {
        // Order matters: an interactable's index here is its bit in RecordedFrame.signals, and
        // GhostReplayer decodes with the same list. SceneBuilder assigns both from one array.
        public GhostInteractable[] interactables;
        public float sampleInterval = 0.02f;

        private readonly List<RecordedFrame> frames = new List<RecordedFrame>();
        private readonly List<PopEvent> pops = new List<PopEvent>();
        private readonly List<CarryEvent> carries = new List<CarryEvent>();
        private float sampleTimer;
        private bool recording;
        // The previous sample's mask, for the rising-edge count in `SampleSignals`. Cleared with the
        // recording rather than kept across iterations: a pad the player was standing on when the
        // clock ran out is a pad they will be standing on again next iteration only if they walk
        // back to it, and that walk is a new action.
        private uint lastSignals;

        public void BeginRecording()
        {
            WarnIfTooManySignals();
            frames.Clear();
            pops.Clear();
            carries.Clear();
            sampleTimer = 0f;
            lastSignals = 0u;
            recording = true;
        }

        public RecordedTimeline EndRecording()
        {
            recording = false;
            return new RecordedTimeline(new List<RecordedFrame>(frames), new List<PopEvent>(pops),
                new List<CarryEvent>(carries));
        }

        // Called by BalloonTool the moment the player bursts one. Stamped with the loop clock
        // rather than Time.time, so it lands on the same timeline the frames are sampled onto.
        public void RecordPop(int balloonId)
        {
            if (!recording || LoopManager.Instance == null) return;
            pops.Add(new PopEvent(LoopManager.Instance.ElapsedTime, balloonId));
            RunTally.PlayerAct();
        }

        // Called by PlayerHand on every take, surrender and equip of an item flagged ghostCarryable.
        // Stamped off the loop clock for the same reason pops are.
        //
        // `instanceName` is only ever meaningful for a Take - see CarryEvent - so callers recording
        // an Equip or a Surrender simply omit it.
        public void RecordCarry(string itemId, CarryKind kind, string instanceName = null)
        {
            if (!recording || LoopManager.Instance == null || string.IsNullOrEmpty(itemId)) return;
            carries.Add(new CarryEvent(LoopManager.Instance.ElapsedTime, itemId, kind, instanceName));
            RunTally.PlayerAct();
        }

        // WHY THE RUN TALLY IS COUNTED HERE AND NOWHERE ELSE ON THE PLAYER'S SIDE. This class is the
        // single point every living-player action passes through - a pop, a carry, and the signal
        // sample below are the complete set of things a timeline records, which is to say the
        // complete set of things a past self will ever be seen to do. `GhostReplayer` is the mirror
        // of it, and counting the two sides anywhere else would be counting two different sets and
        // calling the ratio between them a measurement. See `RunTally.PlayerActs`.

        private void Update()
        {
            if (!recording || LoopManager.Instance == null) return;

            sampleTimer -= Time.deltaTime;
            if (sampleTimer > 0f) return;
            sampleTimer = sampleInterval;

            frames.Add(new RecordedFrame(
                LoopManager.Instance.ElapsedTime,
                transform.position,
                transform.eulerAngles.y,
                SampleSignals()));
        }

        // THE MASK IS 32 BITS WIDE AND OVERFLOWING IT IS SILENT, which is the whole reason this
        // exists. `SampleSignals` below clamps and drops, and a dropped interactable looks exactly
        // like a fixture a ghost has decided not to touch. `SceneBuilder.CheckGhostSignals` catches
        // it at build time; this catches the case where the array was swapped at a cycle boundary
        // rather than authored - once per recording, not once per frame, and once per session after
        // that, because an error repeated sixty times a second is an error nobody reads.
        private static bool warnedOverflow;

        private void WarnIfTooManySignals()
        {
            if (warnedOverflow || interactables == null || interactables.Length <= 32) return;
            warnedOverflow = true;
            Debug.LogError($"[PlayerRecorder] {interactables.Length} ghost interactables, but "
                + "RecordedFrame.signals is a uint - everything from index 32 on is dropped and no "
                + "ghost will ever operate it. See CLAUDE.md 1.6.");
        }

        private uint SampleSignals()
        {
            if (interactables == null) return 0u;

            uint signals = 0u;
            // 32 is the width of the mask. Past that an interactable would silently alias onto
            // another one's bit, so it is dropped instead.
            int count = Mathf.Min(interactables.Length, 32);
            for (int i = 0; i < count; i++)
            {
                if (interactables[i] != null && interactables[i].PlayerSignal)
                    signals |= 1u << i;
            }

            // RISING EDGES ONLY, which is what makes this comparable with a ghost's side of the same
            // count (`GhostReplayer.ApplySignals`). A pad HELD for ten seconds is one action, not six
            // hundred: the level is state, and the moment it went up is the thing the player did.
            // Counted off the sampled mask rather than off the fixtures, so a bit dropped by the
            // 32-bit clamp is not counted here either - the tally reports what was recorded, and an
            // action that never reached a timeline is one no past self will ever perform.
            uint risen = signals & ~lastSignals;
            while (risen != 0u)
            {
                RunTally.PlayerAct();
                risen &= risen - 1u;
            }
            lastSignals = signals;

            return signals;
        }
    }
}
