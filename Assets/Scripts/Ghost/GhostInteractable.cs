using UnityEngine;

namespace IterationRoom
{
    // Anything a ghost can operate on the player's behalf.
    //
    // PlayerRecorder samples PlayerSignal into one bit of RecordedFrame.signals every frame, and a
    // GhostReplayer feeds that bit back through SetGhostSignal an iteration later. The bit position
    // is the object's index in the shared interactable list, so the recorder and every replayer
    // must read the same list in the same order - SceneBuilder wires both from one array.
    //
    // The signal is deliberately a *level*, not an event. A ghost scrubs its timeline by elapsed
    // time and can advance several recorded frames in a single tick, so anything recorded as a
    // one-frame pulse would eventually be skipped. Press-type interactables stretch their pulse
    // (see Drawer.openPulseDuration) instead of recording an edge.
    public abstract class GhostInteractable : MonoBehaviour
    {
        // Sampled by PlayerRecorder each frame. Must reflect the real player only - never a ghost's
        // replayed state, or the signal feeds back into the recording and compounds every
        // iteration.
        public abstract bool PlayerSignal { get; }

        // Called when a ghost's replayed signal changes. Implementations decide what the edges
        // mean: a hold button tracks the level, a one-touch button acts on the rise and ignores
        // the fall.
        public abstract void SetGhostSignal(GhostReplayer ghost, bool active);

        // **"THE RECORDING SAID OFF" AND "THE RECORDING RAN OUT" ARE NOT THE SAME EVENT, and until
        // 2026-08-21 this class could not tell them apart.** `GhostReplayer` applied a falling edge
        // for both, which is right for a pad - a ghost that has left is not standing on anything -
        // and wrong for anything a past self is supposed to have LEFT that way. Play found it as a
        // staircase folding up mid-climb: the lever that raised it was thrown at second 12 of a
        // 40-second recording, and at second 40 the ghost retired and took the throw with it.
        //
        // So a genuine recorded transition still goes through `SetGhostSignal`, and this is the other
        // one: the ghost is done, for whatever reason. The default is the old behaviour, so nothing
        // has to opt in; a fixture whose state is meant to outlive the past self that set it
        // overrides this and does nothing.
        public virtual void ReleaseGhostSignal(GhostReplayer ghost) => SetGhostSignal(ghost, false);
    }
}
