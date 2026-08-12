using UnityEngine;

namespace IterationRoom
{
    // What Room2West does once its board is finished: the lights come up, the board opens down the
    // middle, and a plinth carrying a red cube rises out of the gap.
    //
    // SEPARATE FROM `ChessBoard` on the boundary CLAUDE.md §2 draws. That class owns which piece
    // belongs where and whether they are all home; this one owns what the room does about it. It is
    // the same split `FinalRoomSequence` has from the button that starts it, and it is what keeps
    // "the puzzle" testable without "the theatre".
    //
    // DRIVEN BY A CLOCK, NOT A COROUTINE, which is the one structural decision here. An iteration can
    // end at any point during these three and a half seconds, and the loop's reset has to be total
    // and instant: a coroutine would have to be found and stopped, and a half-finished one would
    // leave the board open. A float set to zero cannot.
    public class ChessReward : MonoBehaviour
    {
        // THE ROOM'S OWN CEILING FIXTURES. They start at zero - this room is dark until it is
        // solved, which is the one thing about it that can be read from the doorway before you have
        // worked out what it wants. The room is still legible at zero because the building's ambient
        // fill is not part of these lights (Trilight, equator 0.644): what goes out is the bright
        // pools and the falloff, and what is left is a flat grey room.
        public Light[] lights;
        public float litIntensity = 10.5f;
        public Color litColour = new Color(0.99f, 0.99f, 1f);

        // The emissive panels those fixtures sit in. Driven TOO, or the room reads as four bright
        // ceiling tiles lighting nothing - which is not "the lights are off", it is "the lighting is
        // broken".
        //
        // Through a MaterialPropertyBlock rather than the material, because `CeilingFixture` is ONE
        // material shared by every room in the building: dimming it here would black out Room1.
        public Renderer[] fixturePanels;
        public Color litPanelEmission = new Color(3.5f, 3.5f, 3.5f);

        // The two halves of the board, which slide apart along X. Their closed positions are read at
        // Awake, so SceneBuilder authors them in place and this only ever offsets them.
        public Transform boardWest;
        public Transform boardEast;
        public float openTravel = 1.7f;

        // The slab ChessPlacer aims at. Switched off while the board is open: it spans the whole
        // closed board, so leaving it on would put an invisible sheet across the gap the plinth
        // comes up through.
        public Collider boardCollider;

        // Authored in its RAISED position and sunk here, the same way Room4's is - a scene whose one
        // prop is invisible cannot be checked without pressing Play.
        public Transform plinth;
        public float riseHeight = 1.25f;

        // The three beats overlap: the lights are already coming up as the board starts to move, and
        // the plinth starts while it is still opening. Played strictly in sequence this is six
        // seconds of a sixty-second iteration, and the player is standing still for all of it.
        public float lightSeconds = 1.6f;
        public float openDelay = 0.35f;
        public float openSeconds = 2.2f;
        public float riseDelay = 1.5f;
        public float riseSeconds = 1.8f;

        private Vector3 westClosed, eastClosed;
        private Vector3 plinthUp, plinthDown;
        private MaterialPropertyBlock block;
        private float elapsed;
        private bool running;

        private float Duration => Mathf.Max(lightSeconds, Mathf.Max(openDelay + openSeconds, riseDelay + riseSeconds));

        private void Awake()
        {
            if (boardWest != null) westClosed = boardWest.localPosition;
            if (boardEast != null) eastClosed = boardEast.localPosition;
            if (plinth != null)
            {
                plinthUp = plinth.localPosition;
                plinthDown = plinthUp + Vector3.down * riseHeight;
            }

            block = new MaterialPropertyBlock();
            ResetNow();
        }

        // Called once, by ChessBoard, the moment the last piece lands.
        public void Play()
        {
            running = true;
            elapsed = 0f;
        }

        // The loop rewinding. Everything back with no fade, because the player is asleep for it.
        public void ResetNow()
        {
            running = false;
            elapsed = 0f;
            Apply(0f);
        }

        private void Update()
        {
            if (!running) return;

            elapsed += Time.deltaTime;
            Apply(elapsed);
            if (elapsed >= Duration) running = false;
        }

        private void Apply(float t)
        {
            float lit = Blend(t, 0f, lightSeconds);
            float open = Blend(t, openDelay, openSeconds);
            float rise = Blend(t, riseDelay, riseSeconds);

            if (lights != null)
                foreach (Light light in lights)
                {
                    if (light == null) continue;
                    light.intensity = litIntensity * lit;
                    light.color = litColour;
                }

            if (fixturePanels != null && block != null)
                foreach (Renderer panel in fixturePanels)
                {
                    if (panel == null) continue;
                    panel.GetPropertyBlock(block);
                    block.SetColor("_EmissionColor", litPanelEmission * lit);
                    panel.SetPropertyBlock(block);
                }

            if (boardWest != null) boardWest.localPosition = westClosed + Vector3.left * (openTravel * open);
            if (boardEast != null) boardEast.localPosition = eastClosed + Vector3.right * (openTravel * open);
            // Off the moment the board starts moving, not when it finishes: a piece cannot be placed
            // on a board that is coming apart, and by then there is none left to place anyway.
            if (boardCollider != null) boardCollider.enabled = open <= 0f;

            if (plinth != null) plinth.localPosition = Vector3.Lerp(plinthDown, plinthUp, rise);
        }

        // Smoothstep rather than linear. These are heavy objects: a slab that starts at full speed
        // and stops dead reads as a sprite being moved, which is exactly what it is.
        private static float Blend(float t, float delay, float duration)
        {
            float k = Mathf.Clamp01((t - delay) / Mathf.Max(0.01f, duration));
            return k * k * (3f - 2f * k);
        }
    }
}
