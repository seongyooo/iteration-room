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

        // THE COMPLETED-ERRAND RULE. A ghost only repeats a pickup whose recording also put the item
        // somewhere; errands this run started and abandoned are not replayed at all.
        //
        // Without it one common case fails silently and unexplainably. Say iteration 3 fetched the
        // key at t=20 and unlocked the door at t=40, and iteration 5 fetched it at t=18 and got
        // distracted. There is ONE key, so on replay the second ghost wins it by two seconds, the
        // first ghost's whole chain dies, and a door that has been opening for five iterations stops
        // - with nothing on screen to say why. Every iteration spent fetching the key mints another
        // competitor for it, so this is not a rare collision.
        //
        // Decidable from the timeline alone, which is what keeps it deterministic.
        public bool Delivers(string itemId)
        {
            if (carries == null) return false;
            foreach (CarryEvent e in carries)
                if (e.kind == CarryKind.Surrender && e.itemId == itemId) return true;
            return false;
        }
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

        public CarryEvent(float time, string itemId, CarryKind kind)
        {
            this.time = time;
            this.itemId = itemId;
            this.kind = kind;
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
