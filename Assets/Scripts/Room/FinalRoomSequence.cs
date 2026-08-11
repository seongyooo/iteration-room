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

        // The wall panels, which stop being walls and become screens. The break spreads out from
        // the plinth, so the origin handed over is the thing the player just pressed.
        public WallPanelDisplay wallPanels;

        // The same shake the last ten seconds of every cycle has, run again over the break. It is
        // the one piece of the collapse the ending deliberately borrows: LoopManager has just
        // released it to nothing on the way in here, so the building going back to shaking is the
        // player's doing rather than the loop's.
        public CameraShaker cameraShaker;

        public NarrationDirector narration;

        // How far the plinth travels. It sits at -riseHeight while hidden, so this is also its
        // height: the whole thing is under the floor slab, which is opaque, so nothing shows.
        public float riseHeight = 1.05f;
        // A beat before it starts, so the player is through the doorway and looking at the room
        // rather than watching it move from inside the door pocket.
        public float riseDelay = 0.8f;
        public float riseDuration = 2.4f;

        // Press to scrim. A floor rather than a pause: the door takes a second to seal and the
        // panels take `glitchOnset` to fail across the building, and at the 3.4s this was first
        // built with, all of it was still arriving when the screen went black. The room has to be
        // seen broken or the last thing the player did has no visible consequence.
        public float breakDuration = 10f;

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

            wallPanels?.BeginGlitch(plinth != null ? plinth.position : transform.position);

            yield return Break();

            // Left broken. EndingSequence's scrim comes up over a room that is still failing, which
            // is the opposite of the loop's power-down and deliberately so: the panels going dark is
            // the facility switching the cell off before switching it back on, and that would say
            // the cycle continued.
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
