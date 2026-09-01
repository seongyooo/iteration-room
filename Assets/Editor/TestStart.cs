using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace IterationRoom.EditorTools
{
    // WAYS INTO THE GAME THAT ARE NOT THE BEGINNING, on a menu instead of on the title screen.
    //
    // **WHY A MENU AND NOT A BUTTON.** The title screen used to carry these - a TEST row, then an END
    // row per cycle - and both were removed by request, the second of them on the same day this was
    // written. The objection was never that the shortcut is unwanted; it is that a page a player
    // looks at should not be where a developer's shortcuts live. A menu item is invisible to the
    // player, costs no layout, and is faster to reach than clicking through a title screen.
    //
    // **THE HARD PART IS THAT STATICS DO NOT SURVIVE ENTERING PLAY MODE.** `DebugStart`'s fields are
    // reset by the domain reload, so a menu item cannot simply assign them - see `DebugStart.Arm`,
    // which writes to PlayerPrefs instead and is read back `BeforeSceneLoad`. Everything here is a
    // wrapper round that one call.
    //
    // Each entry ARMS THE NEXT PLAY and opens the game scene; it deliberately does NOT enter play
    // mode itself. Pressing Play is the user's, and a menu item that starts running the game is one
    // that cannot be undone by not pressing anything.
    public static class TestStart
    {
        private const string GameScene = "Assets/Scenes/IterationRoom.unity";

        // Cycle 3 is the last cycle (`Cycle.playable` is false on cycle 4), so filling its console
        // does not cross a boundary - it runs `LoopManager.RunEnding`, which is the break, the
        // evaluation board, the breach, the cable car and the card. This is the entry point for all
        // of that.
        private const int LastCycle = 3;

        [MenuItem("Iteration Room/Test/Cycle 3 ending - console filled, sample report", priority = 20)]
        public static void CycleThreeEndingWithReport()
        {
            Arm(LastCycle, atBoundary: true, finish: true, sample: true,
                "cycle 3's last room with the console already filled, and a full report behind the "
              + "evaluation board");
        }

        // THE SAME JUMP WITH NOTHING INVENTED. What the board prints here is what this run actually
        // did, which on a jumped run is almost nothing: one cycle record, a handful of answers, and
        // two axes coming back `INSUFFICIENT DATA`. That is the honest state and it is worth being
        // able to see - it is exactly what a player who somehow reached cycle 3 without playing 1 and
        // 2 would get, and it is the case the `Unmeasured` path exists for.
        [MenuItem("Iteration Room/Test/Cycle 3 ending - console filled, real numbers", priority = 21)]
        public static void CycleThreeEnding()
        {
            Arm(LastCycle, atBoundary: true, finish: true, sample: false,
                "cycle 3's last room with the console already filled, reporting only what this run "
              + "actually did");
        }

        // STOPS SHORT OF FINISHING. The tester stands in room3-0 with the escape object on the floor
        // in front of the console and puts it in themselves, so the completion test and the break are
        // both things they do and watch rather than things that have already happened. The right
        // shape when the CONSOLE is what is under test rather than the ending after it.
        [MenuItem("Iteration Room/Test/Cycle 3 - last room, fill it yourself", priority = 22)]
        public static void CycleThreeLastRoom()
        {
            Arm(LastCycle, atBoundary: true, finish: false, sample: false,
                "cycle 3's last room with the escape object on the floor in front of the console");
        }

        [MenuItem("Iteration Room/Test/Cycle 3 - from its own bed", priority = 23)]
        public static void CycleThreeFromBed()
        {
            Arm(LastCycle, atBoundary: false, finish: false, sample: false,
                "cycle 3 from its own bed, iteration 1, no ghosts");
        }

        [MenuItem("Iteration Room/Test/Normal start (disarm)", priority = 40)]
        public static void Disarm()
        {
            DebugStart.Disarm();
            Debug.Log("[TestStart] Disarmed. The next Play starts the game normally.");
        }

        // Ticked in the menu while a test start is armed. It is one shot and it is easy to forget
        // having set it, so the menu says whether it is on.
        [MenuItem("Iteration Room/Test/Normal start (disarm)", validate = true)]
        private static bool DisarmValidate()
        {
            Menu.SetChecked("Iteration Room/Test/Normal start (disarm)", !DebugStart.IsArmed);
            return true;
        }

        private static void Arm(int cycle, bool atBoundary, bool finish, bool sample, string what)
        {
            DebugStart.Arm(cycle, atBoundary, finish, sample);

            // Opened rather than merely named, because the alternative is a log line asking the user
            // to go and open a scene - and Play runs whatever scene is open, so arming without
            // opening is the one combination that silently does the wrong thing. The scenes are build
            // output and git-ignored (CLAUDE.md 1.1), so there is nothing here to lose; Unity still
            // prompts if the open scene is dirty.
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                EditorSceneManager.OpenScene(GameScene, OpenSceneMode.Single);

            Debug.Log($"[TestStart] Armed: {what}.\n"
                    + "Press Play. This is ONE SHOT - the Play after it starts the game normally.");
        }
    }
}
