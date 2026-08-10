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

        public RecordedTimeline(List<RecordedFrame> frames, List<PopEvent> pops)
        {
            this.frames = frames;
            this.pops = pops;
        }

        public int FrameCount => frames != null ? frames.Count : 0;
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
