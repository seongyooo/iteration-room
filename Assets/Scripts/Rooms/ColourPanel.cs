using UnityEngine;

namespace IterationRoom
{
    // ONE STEP OF ROOM3-2N'S STAIRCASE. Out of the wall while its colour's lever is held, back in the
    // moment it is let go.
    //
    // **IT IS A REAL STEP, NOT A LIT PANEL.** The collider moves with it, so a panel that is out can
    // be stood on and one that is in cannot - which is the only reason the room is a climb rather than
    // a light show. Its travel is deliberately short and quick: the player is usually watching this
    // happen on a monitor in another room, and a slow extend would put a second of doubt between
    // pulling a lever and knowing it worked.
    //
    // **IT ASKS ITS COLOUR RATHER THAN BEING TOLD.** `ColourLever.ColourActive` is a static question,
    // so a panel needs no wiring at all - it knows where it is and what colour it is, and nothing has
    // to hold a list of panels that would go out of date the first time the layout is retuned. The
    // spec says the count and placement are for play to settle, so the cheapest possible thing to move
    // is the right shape here.
    public class ColourPanel : MonoBehaviour
    {
        public PanelColour colour;

        // Straight out of the wall it belongs to. Authored IN, like every other retractable thing in
        // this project, so the scene can be read without pressing Play - and because in is the state
        // the room rests in.
        public Vector3 outLocalOffset = new Vector3(0f, 0f, 0.9f);
        public float travelDuration = 0.18f;

        private Vector3 inLocalPos;
        private float outAmount;

        private void Awake()
        {
            inLocalPos = transform.localPosition;
            Apply();
        }

        // The loop's rewind, snapped and silent - the same contract `Door.Close` has. Called from the
        // cycle's reset rather than left to the lever falling back on its own, which would be a
        // staircase folding itself away behind the closed eyelids.
        public void ResetPanel()
        {
            outAmount = 0f;
            Apply();
        }

        private void Update()
        {
            // Frozen through the wake-up like every other moving thing. Without it the stairs retract
            // under a black screen at the top of every iteration.
            bool running = LoopManager.Instance == null || LoopManager.Instance.IterationRunning;
            if (!running) return;

            float target = ColourLever.ColourActive(colour) ? 1f : 0f;
            if (Mathf.Approximately(outAmount, target)) return;

            float step = travelDuration > 0f ? Time.deltaTime / travelDuration : 1f;
            outAmount = Mathf.MoveTowards(outAmount, target, step);
            Apply();
        }

        private void Apply() => transform.localPosition = inLocalPos + outLocalOffset * outAmount;
    }
}
