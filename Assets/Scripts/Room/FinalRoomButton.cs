using UnityEngine;

namespace IterationRoom
{
    // The plate on top of the plinth in the final room. The last input in the game: pressing it
    // shuts the door back to Room3, breaks the displays, and ends the run.
    //
    // It is deliberately the SAME fixture as the one that started the run - same plate, same disc,
    // same E - because the run is a loop and this is the other end of it. See PressPlate for why
    // both sit outside LoopManager.AcceptsInput.
    //
    // The press only latches a flag; FinalRoomSequence is what reads it and runs the ending. That
    // split is the point: a button owns being pressed, not what a press means.
    public class FinalRoomButton : PressPlate
    {
        public FinalRoomSequence sequence;

        public bool Pressed { get; private set; }

        // Dead until the plinth has finished rising, and dead again the instant it is pressed -
        // there is no second press, and a disc lingering over it while the room comes apart would
        // be the game still asking for something.
        protected override bool IsLive => !Pressed && sequence != null && sequence.ButtonLive;

        protected override void OnPressed() => Pressed = true;
    }
}
