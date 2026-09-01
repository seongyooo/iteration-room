using UnityEngine;

namespace IterationRoom
{
    // THE CLOCK EVERY ENDING SEQUENCE RUNS ON, and the one thing that can stop it.
    //
    // **WHY NOT `Time.deltaTime`.** Everything after the last console fills - the break, the board,
    // the breach, the ride - happens with the loop over and `RunOver` set. Those sequences were
    // written on `Time.unscaledDeltaTime` for a stated reason: *"a coroutine that can be frozen with
    // no way to unfreeze it is a soft lock, and this one ends the game"*. `Time.timeScale` is the
    // pause menu's, and at the time the ending was written the pause menu was locked out of it, so
    // scaled time was a freeze with no thaw.
    //
    // **WHY NOT `Time.unscaledDeltaTime` EITHER, ANY MORE.** The ending stopped being a cutscene.
    // The player walks down a ladder, reads a wall, and steps into a vehicle - so Escape has to bring
    // the pause menu up (2026-09-01, by request), and a pause that leaves the cable car climbing the
    // outside of the building behind the menu is not a pause.
    //
    // So: unscaled, EXCEPT that an explicit pause stops it. That keeps the original guarantee - no
    // stray `timeScale` can strand the end of the game - while making the one deliberate freeze work.
    // `PauseMenu.Resume` is the only thing that starts it again, and it is always reachable, because
    // it is the thing that stopped it.
    public static class EndingClock
    {
        // Asked of `LoopManager` rather than of `PauseMenu`, which has no singleton: the loop is
        // told about every pause (`PauseMenu.Apply` ends in `LoopManager.SetPaused`) and is already
        // the thing the rest of the game asks, through `AcceptsInput`.
        public static bool Paused =>
            LoopManager.Instance != null && LoopManager.Instance.IsPaused;

        public static float Delta => Paused ? 0f : Time.unscaledDeltaTime;
    }
}
