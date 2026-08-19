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

        public void BeginRecording()
        {
            WarnIfTooManySignals();
            frames.Clear();
            pops.Clear();
            carries.Clear();
            sampleTimer = 0f;
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
        }

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
            return signals;
        }
    }
}
