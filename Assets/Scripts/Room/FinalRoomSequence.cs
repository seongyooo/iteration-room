using System.Collections;
using UnityEngine;

namespace IterationRoom
{
    // Room4 - the room past the last door, and the only room in the game the loop cannot reach.
    //
    // Getting into it is what stops the loop (EscapeTrigger, on Room3's north threshold). Everything
    // after that is this: a plinth rises out of the floor, a plate on top of it goes live, and
    // pressing it shuts the door behind the player, breaks every display in the building, and hands
    // over to EndingSequence.
    //
    // WHY THE PLAYER KEEPS CONTROL HERE. The run is already over by the time this starts - the clock
    // is stopped, IterationRunning is false, and nothing can pull them back to the bed. Leaving them
    // walking is what makes the last thing in the game an ACTION rather than a cutscene: the loop
    // stopped taking them, and the last press is theirs. They keep it THROUGH the break as well, and
    // lose it only when the scrim starts - ten seconds pinned in place watching a room fail is the
    // game freezing, not the room failing.
    //
    // Everything runs on unscaled time, like the rest of the ending. PauseMenu is locked out for the
    // duration (RunOver), but a coroutine that can be frozen with no way to unfreeze it is a soft
    // lock, and this one is the end of the game.
    public class FinalRoomSequence : MonoBehaviour
    {
        // Rises out of the floor. It starts fully below it, which is why the room is empty when the
        // player walks in and why there is nothing to notice before it moves.
        public Transform plinth;
        public FinalRoomButton button;

        // Room3's door, the one the player just came through. Shutting it is the first thing the
        // press does: the way back closes before anything else happens, which is the room saying
        // this is not a place you leave.
        public Door doorBehind;

        public WallPanelDisplay wallPanels;
        // ERROR on all four of this room's walls, built exactly like Room3's message - see
        // SceneBuilder.MakeWallFace for why a world-space canvas on a dark plate reads as the wall
        // rather than as a poster.
        public CanvasGroup[] errorFaces;

        public NarrationDirector narration;

        // How far the plinth travels. It sits at -riseHeight while hidden, so this is also its
        // height: the whole thing is under the floor slab, which is opaque, so nothing shows.
        public float riseHeight = 1.05f;
        // A beat before it starts, so the player is through the doorway and looking at the room
        // rather than watching it move from inside the door pocket.
        public float riseDelay = 0.8f;
        public float riseDuration = 2.4f;

        // How long the room stays broken before the scrim starts. Long enough to read ERROR and see
        // the colour, short enough that it does not become the ending itself.
        public float breakHold = 3.4f;
        public float errorFadeIn = 0.35f;

        // True once the plinth is up. FinalRoomButton reads it, so the plate cannot be pressed
        // through the floor on the way up.
        public bool ButtonLive { get; private set; }

        // The ControlHintDisplay gate, the same job SensitivityCalibration.Active does at the other
        // end of the run: AcceptsInput is false for all of this, and without this the grey disc
        // would never appear over the one fixture the room has.
        public bool Active { get; private set; }

        private Vector3 plinthDownPosition;
        private Vector3 plinthUpPosition;

        private void Awake()
        {
            if (plinth == null) return;

            // Authored in the UP position by SceneBuilder, so the scene file shows the plinth where
            // it will actually be and the down position is derived. The reverse - authoring it sunk
            // and adding the height at runtime - leaves a scene whose one prop is invisible and
            // impossible to check without pressing Play.
            plinthUpPosition = plinth.localPosition;
            plinthDownPosition = plinthUpPosition + Vector3.down * riseHeight;
            plinth.localPosition = plinthDownPosition;
        }

        public IEnumerator Run()
        {
            Active = true;

            yield return Wait(riseDelay);
            yield return Rise();

            ButtonLive = true;
            while (button != null && !button.Pressed) yield return null;

            // The PLATE goes dead - there is no second press, and a disc lingering over it while
            // the room comes apart would be the game still asking for something. The PLAYER does
            // not: control is held all the way to the scrim, and LoopManager takes it there.
            ButtonLive = false;
            Active = false;

            // The way back, first. Slides rather than snapping - Close() is the loop rewinding
            // behind a black screen, this is a door shutting with the player watching it.
            doorBehind?.Seal();

            // The facility notices, and it says so here rather than when the player stepped through
            // the doorway: walking into a room is not what breaks a cycle, this is.
            narration?.AnnounceCycleBroken();

            wallPanels?.BeginGlitch();
            yield return FadeErrors(1f, errorFadeIn);
            yield return Wait(breakHold);

            // Left broken. EndingSequence's scrim comes up over a room that is still tearing itself
            // apart, which is the opposite of the loop's power-down and deliberately so: the panels
            // going dark is the facility switching the cell off before switching it back on, and
            // that would say the cycle continued.
        }

        private IEnumerator Rise()
        {
            if (plinth == null) yield break;

            float t = 0f;
            while (t < riseDuration)
            {
                t += Time.unscaledDeltaTime;
                // Eased at both ends, so it does not start or stop with a jolt - this is machinery
                // being raised, not a prop being switched on.
                float k = Mathf.SmoothStep(0f, 1f, riseDuration > 0f ? t / riseDuration : 1f);
                plinth.localPosition = Vector3.Lerp(plinthDownPosition, plinthUpPosition, k);
                yield return null;
            }
            plinth.localPosition = plinthUpPosition;
        }

        private IEnumerator FadeErrors(float target, float duration)
        {
            if (errorFaces == null) yield break;

            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                float a = Mathf.Lerp(0f, target, duration > 0f ? t / duration : 1f);
                foreach (CanvasGroup face in errorFaces)
                    if (face != null) face.alpha = a;
                yield return null;
            }
            foreach (CanvasGroup face in errorFaces)
                if (face != null) face.alpha = target;
        }

        private static IEnumerator Wait(float seconds)
        {
            float t = 0f;
            while (t < seconds)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }
        }
    }
}
