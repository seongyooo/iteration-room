using UnityEngine;
using UnityEngine.UI;

namespace IterationRoom
{
    // ONE SIDE OF THE TITLE SCREEN'S ROOM GOING OUT AND COMING BACK.
    //
    // **THE BACKGROUND IS A PHOTOGRAPH, WHICH IS WHY THIS WORKS THE WAY IT DOES.** The menu shows a
    // PNG that `SceneBuilder.CaptureMenuBackground` rendered at build time - there is no room behind
    // it, no lights and nothing to switch. So the flicker cannot be a light going off; it has to be a
    // SECOND photograph of the same room with half its fixtures dark, laid over the first and faded
    // in. Two stills and an alpha is the whole mechanism.
    //
    // The alternative - putting a live 3D room in the menu scene - would cost a scene load, a camera
    // and a set of lights on the first screen the player ever sees, to reproduce something two images
    // already say.
    //
    // **IT IS NOT A SINE WAVE.** A light that fades smoothly in and out reads as a mood; a failing
    // one stutters, holds, and catches. So this runs long quiet stretches broken by short bursts of
    // hard cuts, which is what a tube on its way out actually does - and it is the one thing on this
    // screen that says the building is not well.
    [RequireComponent(typeof(Image))]
    public class MenuFlicker : MonoBehaviour
    {
        // The dark frame. Sits over the lit one at whatever alpha this drives, so 0 is a healthy room
        // and 1 is one side of it out.
        private Image dark;

        // **ONE FRAME PER ROOM DOWN THE CORRIDOR, AND THE FAULT WALKS ALONG THEM** (2026-09-04, by
        // request). Index 0 is the room the camera stands in; each one after it is a room further
        // away, seen through the doorways.
        //
        // Left empty this behaves exactly as it always did - `dark` alone, one room, on and off -
        // which is what a menu built before these frames existed gets. With frames it steps: a fit
        // takes one room out, the next fit takes the next room, and at the end of the corridor the
        // direction reverses. What that reads as is a failure moving through the building rather
        // than a bulb going in one room, which is the whole reason the shot looks down six of them.
        public Image[] roomFrames;

        private int room;
        private int step = 1;

        // How long the room stays whole between fits. Long, and deliberately so: a title screen that
        // flickers constantly is a broken television, where one that is steady for eight seconds and
        // then stumbles is a building with a fault in it.
        public Vector2 calmSeconds = new Vector2(4.5f, 11f);
        // How many hard cuts a fit contains, and how long each one lasts.
        public Vector2Int burstCuts = new Vector2Int(2, 6);
        public Vector2 cutSeconds = new Vector2(0.035f, 0.13f);
        // The pause at the end of a fit, where it stays out a beat longer before catching again. This
        // is the part that reads as a light STRUGGLING rather than as a shutter.
        // **LONGER THAN A CUT, AND THAT IS THE POINT** (widened 2026-08-28, by request: the dark
        // should last longer). A fit that ends the instant it starts reads as a glitch in the image;
        // one that goes out and STAYS out for a beat reads as a room with a fault in it, and that
        // beat is the only part of this the player consciously notices.
        public Vector2 holdSeconds = new Vector2(0.7f, 2.6f);

        private float until;
        private int cutsLeft;
        private bool isOut;   // NOT `out` - that is a C# keyword.

        private void Awake()
        {
            dark = GetComponent<Image>();
            SetOut(false);
            // Whatever a previous session or a rebuild left showing - every frame starts clear, so
            // the first thing on screen is the corridor whole.
            if (roomFrames != null)
                foreach (Image frame in roomFrames) Show(frame, false);
            // Starts calm, so the first thing the player sees is the room intact.
            until = Time.unscaledTime + Random.Range(calmSeconds.x, calmSeconds.y);
        }

        // **UNSCALED, because the menu is not the game.** `Time.timeScale` is zero whenever the pause
        // menu is up and this same screen is reachable from there; on a scaled clock the flicker would
        // freeze exactly when the player is looking at it longest.
        private void Update()
        {
            if (dark == null || Time.unscaledTime < until) return;

            if (cutsLeft > 0)
            {
                cutsLeft--;
                SetOut(!isOut);
                // The last cut of a fit is the long one - out, and hanging there before it catches.
                until = Time.unscaledTime + (cutsLeft == 0 && isOut
                    ? Random.Range(holdSeconds.x, holdSeconds.y)
                    : Random.Range(cutSeconds.x, cutSeconds.y));
                return;
            }

            if (isOut)
            {
                // A fit always ends with the room whole again.
                SetOut(false);
                Advance();
                until = Time.unscaledTime + Random.Range(calmSeconds.x, calmSeconds.y);
                return;
            }

            cutsLeft = Random.Range(burstCuts.x, burstCuts.y + 1);
        }

        // **ONE ROOM DARK AT A TIME.** The whole set is cleared and the current one shown, rather
        // than the previous one being turned off by index - a frame left up because an index moved
        // while a fit was mid-cut is exactly the kind of state that only shows on the fifth loop.
        private void SetOut(bool value)
        {
            isOut = value;

            if (roomFrames != null && roomFrames.Length > 0)
            {
                for (int i = 0; i < roomFrames.Length; i++)
                    Show(roomFrames[i], value && i == room);
                return;
            }

            Color c = dark.color;
            c.a = value ? 1f : 0f;
            dark.color = c;
        }

        // Along the corridor and back. Reversing at the ends rather than wrapping, because a fault
        // that jumps from the far room to the near one in one step reads as a cut rather than as
        // something moving.
        private void Advance()
        {
            int count = roomFrames != null ? roomFrames.Length : 0;
            if (count < 2) return;

            room += step;
            if (room >= count) { room = count - 2; step = -1; }
            else if (room < 0) { room = 1; step = 1; }
        }

        private static void Show(Image frame, bool visible)
        {
            if (frame == null) return;
            Color c = frame.color;
            c.a = visible ? 1f : 0f;
            frame.color = c;
        }
    }
}
