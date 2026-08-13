using System.Collections.Generic;

namespace IterationRoom
{
    // One iteration's worth of recording: the sampled transform/signal frames, plus the balloons
    // that were popped and when.
    //
    // Pops are a separate list rather than more bits in RecordedFrame.signals for two reasons. The
    // mask is 32 wide and there are far more balloons than that, and - more fundamentally - a
    // signal is a *level* (see GhostInteractable), whereas popping is an instant with an identity
    // attached. An event list carries the identity, and a replayer drains every event whose time
    // has passed, so an event survives a ghost skipping several frames in one tick without needing
    // the pulse-stretching trick Drawer uses.
    public class RecordedTimeline
    {
        public readonly List<RecordedFrame> frames;
        public readonly List<PopEvent> pops;
        // Carries ride in a third list for exactly the reasons pops do: an itemId is an identity,
        // and taking is an instant rather than a level.
        public readonly List<CarryEvent> carries;

        public RecordedTimeline(List<RecordedFrame> frames, List<PopEvent> pops, List<CarryEvent> carries)
        {
            this.frames = frames;
            this.pops = pops;
            this.carries = carries;
        }

        public int FrameCount => frames != null ? frames.Count : 0;
    }

    // Equip is the Tab press: which of the carried items is actually in the hand. An Equip with an
    // empty itemId is empty hands, the slot at the end of Tab's cycle. Recorded because a ghost
    // carrying two things would otherwise put both in one fist, and because a ghost's unlock has to
    // be re-evaluated against the same condition the player's was - key IN HAND, not key on person.
    public enum CarryKind { Take, Surrender, Equip }

    // The player picking an item up, or giving it to something that keeps it. Both ends are
    // RE-EVALUATED at replay rather than simply applied - see GhostReplayer.DrainCarries. A take
    // only lands if the item is genuinely free, and a surrender only lands if this ghost is
    // genuinely holding it. That is the difference between this and the KeyRevealed bug, which
    // re-evaluated a weaker fact than the one that enabled the action.
    public struct CarryEvent
    {
        public float time;
        public string itemId;
        public CarryKind kind;

        // WHICH physical object, for a Take - empty for Equip/Surrender, which never need it: a
        // ghost holds at most one instance per id, so itemId alone already picks the right one out
        // of `held`. A Take is different the moment an id is a SUPPLY (the three pins): itemId alone
        // only ever says "give me a free one", which loses which one a take off a GHOST actually
        // meant. `instanceName` is the object's own GameObject name (unique within a supply, fixed
        // at build time, e.g. "BalloonTool_1") - the same identity `docs/gotchas.md`-style balloon
        // ids and chess piece ids already lean on, just borrowed rather than a new number scheme.
        public string instanceName;

        public CarryEvent(float time, string itemId, CarryKind kind, string instanceName = null)
        {
            this.time = time;
            this.itemId = itemId;
            this.kind = kind;
            this.instanceName = instanceName;
        }
    }

    // Identity, not position. The ghost re-pops the same balloon it popped, wherever physics has
    // put that balloon this time round - which is what lets the balloons stay physical without the
    // puzzle's progress becoming a matter of luck.
    public struct PopEvent
    {
        public float time;
        public int balloonId;

        public PopEvent(float time, int balloonId)
        {
            this.time = time;
            this.balloonId = balloonId;
        }
    }
}
