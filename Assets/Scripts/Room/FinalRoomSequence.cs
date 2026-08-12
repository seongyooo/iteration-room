using System.Collections;
using UnityEngine;

namespace IterationRoom
{
    // Room4 - the room past the last door, and the console in it that ends the game.
    //
    // IT USED TO BE THE ONE ROOM THE LOOP COULD NOT REACH. Crossing Room3's threshold stopped the
    // clock, and everything after that was outside the iteration: a plinth rose, a plate went live,
    // and pressing it broke the cycle. That is gone. The clock now runs through Room4 like it runs
    // through every other room, and **the ending is putting all three escape objects into the
    // console** - the red cube, the blue sphere and the yellow triangle, one from each puzzle room.
    //
    // What that buys is the thing the plate could never say: the last room is not a reward for
    // arriving, it is a sixty-second errand like the rest of the building, and a run that gets there
    // with two objects gets pulled back to the bed and has to do it again. Reaching Room4 is no
    // longer the end of anything.
    //
    // There is no button any more. It was the only fixture in the game that existed purely to be
    // pressed once, and three recesses that each want a specific object say everything it said.
    //
    // THE CONSOLE IS A `RewardPlinth`, not a coroutine, and that is what makes this room resettable.
    // The old rise was a coroutine started by an event that could only happen once; an iteration can
    // now end at any point in Room4, so the plinth has to be derived state that snaps back with
    // everything else.
    public class FinalRoomSequence : MonoBehaviour
    {
        // Rises as the player comes through the last doorway. Its `Requested` is driven here.
        public RewardPlinth console;

        // The three shaped recesses in its top. One per escape object, and `FinalSlot.AllFilled`
        // refuses an empty array - a console wired to nothing does not open.
        public FinalSlot[] slots;

        // "The player has reached Room4 this iteration." Latched by the doorway and cleared by the
        // loop, so each run has to get there for itself.
        public EscapeTrigger arrival;

        // Room3's door, the one the player came through. Sealing it is the first thing the ending
        // does: the way back closes before anything else happens, which is the room saying this is
        // not a place you leave.
        public Door doorBehind;

        // The wall panels, which stop being walls and become screens. The break spreads out from the
        // console, so the origin handed over is the thing the player just finished.
        public WallPanelDisplay wallPanels;

        // The same shake the last ten seconds of every cycle has, run again over the break. It is
        // the one piece of the collapse the ending deliberately borrows: LoopManager has just
        // released it to nothing on the way in here, so the building going back to shaking is the
        // player's doing rather than the loop's.
        public CameraShaker cameraShaker;

        public NarrationDirector narration;

        // Press to scrim. A floor rather than a pause: the door takes a second to seal and the
        // panels take `glitchOnset` to fail across the building, and at the 3.4s this was first
        // built with, all of it was still arriving when the screen went black. The room has to be
        // seen broken or the last thing the player did has no visible consequence.
        public float breakDuration = 10f;

        // The console is up and taking objects. Read by ControlHintDisplay, and by FinalSlot as the
        // other half of its own liveness test - a recess sunk under the floor accepts nothing.
        public bool Active { get; private set; }

        // ALL THREE ARE IN. This is the game's only exit condition now, and the only thing that ends
        // LoopManager's while(true).
        public bool Completed { get; private set; }

        private void Update()
        {
            if (Completed) return;

            if (console != null) console.Requested = arrival != null && arrival.PlayerArrived;

            bool running = LoopManager.Instance == null || LoopManager.Instance.AcceptsInput;
            Active = running && console != null && console.Raised;

            // Tested rather than notified, so there is no ordering to get wrong between three
            // recesses each reporting themselves. AllFilled skips undeclared slots and refuses an
            // empty array, so a console with nothing wired to it can never satisfy this.
            if (!Active || !FinalSlot.AllFilled(slots)) return;

            Completed = true;
            // Prompts off on the same frame the last object lands. There is nothing left to ask for.
            Active = false;
        }

        // The loop rewinding. The objects themselves are already back on their plinths by now
        // (`ItemRegistry.ReturnAllToOrigin`); this takes the console down and forgets what was in it.
        public void ResetRoom()
        {
            Active = false;
            arrival?.Rearm();
            if (console != null) console.Requested = false;
            if (slots == null) return;
            foreach (FinalSlot slot in slots) if (slot != null) slot.Clear();
        }

        // What happens once all three are in. Driven by LoopManager, after it has stopped the clock
        // and let the collapse go - see RunEnding.
        public IEnumerator RunBreak()
        {
            // The way back, first. Slides rather than snapping - Close() is the loop rewinding
            // behind a black screen, this is a door shutting with the player watching it.
            doorBehind?.Seal();

            // The facility notices, and it says so here rather than when the player stepped through
            // the doorway: walking into a room is not what breaks a cycle, this is.
            narration?.AnnounceCycleBroken();

            wallPanels?.BeginGlitch(console != null ? console.transform.position : transform.position);

            yield return Break();

            // Left broken. EndingSequence's scrim comes up over a room that is still failing, which
            // is the opposite of the loop's power-down and deliberately so: the panels going dark is
            // the facility switching the cell off before switching it back on, and that would say
            // the cycle continued.
        }

        // The shake builds over the whole break and peaks as the scrim starts, which is exactly the
        // curve LoopManager runs over the last seconds of a cycle - linear intensity, same shaker.
        // A player who has felt the collapse dozens of times knows what this means without being
        // told, and that is the only reason to reuse it rather than invent a motion for the ending.
        private IEnumerator Break()
        {
            float t = 0f;
            while (t < breakDuration)
            {
                t += Time.unscaledDeltaTime;
                cameraShaker?.SetIntensity(breakDuration > 0f ? Mathf.Clamp01(t / breakDuration) : 1f);
                yield return null;
            }
            cameraShaker?.SetIntensity(1f);
        }
    }
}
