using UnityEngine;

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

        // WITH A REPORT'S WORTH OF NUMBERS BEHIND IT. Off by default, and the only thing in this
        // project that invents data - see `Seed` below for the argument.
        public static bool SampleReport;

        // Both cleared together, so a test run followed by an ordinary PLAY cannot inherit either -
        // these are statics and survive until the domain reloads.
        public static void Clear()
        {
            AtCycleBoundary = false;
            FinishOnJump = false;
            SampleReport = false;
            StartCycle = -1;
        }

        // ======================================================== ARMING THE NEXT PLAY
        //
        // **THE STATICS ABOVE CANNOT BE SET FROM AN EDITOR MENU, AND THAT IS THE WHOLE PROBLEM THIS
        // SOLVES.** Entering play mode reloads the domain, which resets every static in the project -
        // so a menu item that assigned `StartCycle` and then asked the user to press Play would be
        // assigning a field that is about to be thrown away. It is why the shortcut used to need a
        // button on the title screen: `MainMenu` runs in the same domain as the game it loads.
        //
        // PlayerPrefs survives the reload, so the menu writes there and this reads it back
        // `BeforeSceneLoad` - before any `Awake`, and well before `LoopManager.Start`, which is the
        // only thing that reads these.
        //
        // **ONE SHOT.** The key is deleted as it is read, so the Play after a test Play is an
        // ordinary one. A flag that stayed armed until somebody remembered to clear it is a flag that
        // silently starts the game in the wrong place a week later.
        private const string ArmKey = "iteration.testStart";

        // `cycle:boundary:finish:sample`, all ints. Deliberately not a JSON blob: it is written and
        // read in this file and nowhere else, and a format nobody has to parse by hand is a format
        // that cannot be typed wrong in the console when the menu is not to hand.
        public static void Arm(int cycle, bool atBoundary, bool finish, bool sample)
        {
            PlayerPrefs.SetString(ArmKey,
                $"{cycle}:{(atBoundary ? 1 : 0)}:{(finish ? 1 : 0)}:{(sample ? 1 : 0)}");
            PlayerPrefs.Save();
        }

        public static void Disarm()
        {
            PlayerPrefs.DeleteKey(ArmKey);
            PlayerPrefs.Save();
        }

        public static bool IsArmed => PlayerPrefs.HasKey(ArmKey);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ApplyArmed()
        {
            if (!PlayerPrefs.HasKey(ArmKey)) return;

            string raw = PlayerPrefs.GetString(ArmKey, string.Empty);
            // Read once and gone, whatever it said. A malformed value must not arm the next session
            // either - it is cleared before it is parsed for exactly that reason.
            Disarm();

            // **NOT IN A SHIPPED BUILD.** The key is only ever written by an editor menu, so this can
            // only fire where somebody put it - but a debug entry point that is reachable at all in a
            // release player is one that can be reached by accident, and the cost of the guard is a
            // branch.
            if (!Application.isEditor && !Debug.isDebugBuild) return;

            string[] parts = raw.Split(':');
            if (parts.Length != 4 || !int.TryParse(parts[0], out int cycle)) return;

            Clear();
            StartCycle = Mathf.Max(1, cycle);
            AtCycleBoundary = parts[1] == "1";
            FinishOnJump = parts[2] == "1";
            SampleReport = parts[3] == "1";

            Debug.Log($"[DebugStart] Armed run: cycle {StartCycle}"
                    + (AtCycleBoundary ? ", at its last room" : ", from its bed")
                    + (FinishOnJump ? ", console filled" : string.Empty)
                    + (SampleReport ? ", with a sample report" : string.Empty)
                    + ". This is one shot - the next Play is an ordinary one.");
        }

        // **THE ONE PLACE THIS PROJECT INVENTS DATA, AND IT IS FENCED IN HERE FOR THAT REASON.**
        //
        // `JumpToBoundary`'s whole worth is that it arranges the world without faking the boundary -
        // every object goes in through the path a player's or a past self's would. That principle is
        // not being broken; it is being stepped around for one screen, and only that screen.
        //
        // WHY IT IS NEEDED. A run that jumps straight to cycle 3's last room has played no cycle 1
        // and no cycle 2, so `LoopManager.CycleRecords` holds one entry and `RunTally` holds almost
        // nothing. The evaluation board is then honest and nearly empty: two axes come back
        // `INSUFFICIENT DATA`, the table is three lines, and the thing that most needs looking at -
        // whether twenty rows of it are readable from where the player lands, and whether the pacing
        // of the print is right - cannot be looked at.
        //
        // These numbers are a PLAUSIBLE run, not a good one: 31 iterations across three cycles is the
        // 2026-08-20 clear, and the answer counts are what a player who guessed wrong four times
        // would have. Nothing derived from them is ever recorded - see the note in `LoopManager`,
        // which does not call `RunReport.Record` on a sampled run.
        public static void Seed()
        {
            RunTally.SeedForTest(rightAnswers: 38, wrongAnswers: 4,
                                 playerActs: 210, ghostActs: 2960,
                                 deaths: 3, skips: 22);
        }
    }
}
