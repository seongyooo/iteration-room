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
        // Consumed by LoopManager on the first iteration. **NOTHING SETS IT ANY MORE**: the title
        // screen's TEST button was removed 2026-08-15 by request, and it was the only caller. The
        // machinery is kept because the argument above still holds and it costs one assignment to use
        // - set this from a script or the console before the scene loads and the jump runs as it did.
        public static bool AtCycleBoundary;

        // FILL THE CONSOLE TOO, rather than laying its objects on the floor to be filled by hand.
        //
        // `AtCycleBoundary` alone deliberately stops SHORT of finishing: it stands the player in the
        // last room with the objects in front of them, so the completion, the break and the hatch are
        // all still things the tester does and watches. That is the right shape when the boundary is
        // what is under test.
        //
        // It is the wrong shape when the CYCLE AFTER IT is what is under test, which is what this
        // adds. Seating the objects fires the boundary at once, and the tester is through the hatch
        // and into the next cycle's bed in a few seconds rather than after four E presses.
        //
        // **IT STILL DOES NOT FAKE ANYTHING.** The objects go in through `FinalSlot.AcceptFromGhost`,
        // which is the same path a past self's delivery takes, and everything after that - the break,
        // the wake of the cycle below, the gas being repointed, the hatch, the drop - is the real
        // `CrossToNextCycle`. Opening the hatch on its own would not do: the cycle underneath is
        // ASLEEP until that coroutine wakes it, so a player dropping through an early hole would land
        // in an inactive room and the shortcut would look like a bug in the thing it exists to test.
        public static bool FinishOnJump;

        // WHICH CYCLE TO WAKE IN. -1 means the ordinary route: cycle 1, from the beginning.
        //
        // A cycle is a whole game, and by the time there are several, reaching the third by playing
        // the first two is twenty minutes before the thing under test is even on screen. This is the
        // same argument the boundary jump was built on, one level up.
        //
        // It starts the cycle PROPERLY - at its own bed, iteration 1, no ghosts - rather than
        // dropping the player into the middle of one. So what it skips is the cycles before it, not
        // any part of the cycle it selects.
        public static int StartCycle = -1;

        // Both cleared together, so a test run followed by an ordinary PLAY cannot inherit either -
        // these are statics and survive until the domain reloads.
        public static void Clear()
        {
            AtCycleBoundary = false;
            FinishOnJump = false;
            StartCycle = -1;
        }
    }
}
