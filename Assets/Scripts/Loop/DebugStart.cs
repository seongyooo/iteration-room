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
    // **This shortcut arranges the world, it does not fake the boundary.** It walks the console up,
    // seats the three objects through the same `FinalSlot` path a ghost's delivery uses, and then
    // lets `LoopManager` notice on its own that the cycle is complete. Everything after that is the
    // real code path, which is the only reason the shortcut is worth having.
    public static class DebugStart
    {
        // Set by the title screen's test button, consumed by LoopManager on the first iteration.
        public static bool AtCycleBoundary;
    }
}
