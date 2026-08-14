namespace IterationRoom
{
    // WHERE THE GAME SHOULD BEGIN, when it should not begin at the beginning.
    //
    // A cycle boundary is about eight minutes into a run: three puzzle rooms cleared and all three
    // escape objects delivered. Testing what happens *at* the boundary by playing up to it costs that
    // eight minutes every time a number changes, which in practice means the boundary gets tested
    // rarely and late.
    //
    // A plain static rather than anything cleverer, because the flag has to survive a scene load -
    // it is set in `MainMenu` and read in `IterationRoom` - and statics reset when play stops, which
    // is exactly the lifetime wanted. Nothing serialises it and nothing persists it.
    //
    // **This shortcut arranges the world, it does not fake the boundary.** It stands the player in the
    // last room, walks the console up, and puts the three escape objects on the floor in front of it.
    // Everything from there is what a player does - pick one up, put it in, three times - and the
    // completion, the hatch, the drop and the gas are all the real code path. That is the only reason
    // the shortcut is worth having.
    //
    // The objects were SEATED here at first, which completed the cycle on the same frame and fired
    // the boundary before the room had been looked at. On the floor instead, the insertion itself is
    // part of what gets tested.
    public static class DebugStart
    {
        // Set by the title screen's test button, consumed by LoopManager on the first iteration.
        public static bool AtCycleBoundary;
    }
}
