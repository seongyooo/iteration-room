using UnityEngine;

namespace IterationRoom
{
    // WHAT THE FACILITY SAW, counted while the run happens. Raw counts only - what they are worth
    // is `RunEvaluation`'s job and what they look like is `EvaluationBoard`'s.
    //
    // WHY IT IS STATIC, like `RunReport`. These numbers have to outlive an iteration, a cycle
    // boundary and the destruction of every ghost in the building, and there is no object in this
    // project with that lifetime - `LoopManager` is the closest and it is per scene. A run is the
    // unit here, and `Begin` is what starts one.
    //
    // NOTHING HERE IS EVER REWOUND. That is deliberate and it is the difference between a tally and
    // world state: `ItemRegistry.ReturnAllToOrigin` puts the objects back, and the fact that the
    // player put the wrong ball in a pedestal at iteration 9 still happened. A counter that the loop
    // reset would only ever report the last iteration, which is the one iteration nobody is being
    // judged on.
    //
    // A RESTART DOES NOT CLEAR IT EITHER. `LoopManager.RestartCycleState` throws away the ghosts and
    // starts the cycle again; the attempts made before it were still attempts, and forgiving them
    // would make restarting the cheapest way to raise a score.
    public static class RunTally
    {
        // ANSWERS GIVEN AT A FIXTURE THAT CAN SAY NO - the three in the game are `FinalSlot` (every
        // console recess and every cycle-2 pedestal), `KeyLock` (the coloured doors) and
        // `BedlamPlacer` (a block offered to the cube). What they have in common is the only thing
        // that matters here: the player was free to be wrong, and the game told them which they were.
        //
        // A fixture that simply shows no prompt for the wrong object is NOT an answer and must never
        // be counted - `SymbolSlot` refuses by never offering, so trying every recess with a cube in
        // hand is searching, not guessing, and searching is not a memory failure.
        public static int AnswersRight { get; private set; }
        public static int AnswersWrong { get; private set; }

        // WHO DID THE WORK. One increment per action that changed the world: a take, a drop, a
        // surrender, a balloon popped, or a signal fixture going from off to on.
        //
        // HOOKED IN EXACTLY TWO FILES, and that is not a convenience - it is the reason the number
        // can be trusted. `PlayerRecorder` is the single point every living-player action passes
        // through on its way into a timeline (`RecordPop`, `RecordCarry`, `SampleSignals`), and
        // `GhostReplayer` is the single point every past-self action passes through on the way back
        // out. Counting anywhere else would be counting a different set on each side.
        //
        // ONLY ACTIONS THAT LANDED. A ghost re-evaluates every recorded action against the world as
        // it is now (CLAUDE.md 1.3) and a great many of them fail - the item is gone, the socket is
        // full, another past self got there first. A refused replay is a past self that walked to a
        // place and did nothing, and it did not help.
        public static int PlayerActs { get; private set; }
        public static int GhostActs { get; private set; }

        // ITERATIONS ENDED BY DYING. `LoopManager.EndCycleEarly` is the one call, and it is shared
        // with the voluntary skip - see its `reason` argument, which exists for this.
        public static int Deaths { get; private set; }

        // ITERATIONS ENDED ON PURPOSE. `EndCycleControl.UseCount` already counts these, but it is
        // zeroed per cycle and this has to survive the whole run.
        public static int Skips { get; private set; }

        // A run starts. Called by `LoopManager` rather than by a static initialiser, because the
        // Editor's play mode can be configured to skip the domain reload - and then these would
        // carry the last session's numbers into a fresh one, which is the one failure mode of
        // static state in Unity that is silent.
        public static void Begin()
        {
            AnswersRight = 0;
            AnswersWrong = 0;
            PlayerActs = 0;
            GhostActs = 0;
            Deaths = 0;
            Skips = 0;
        }

        // `right` is whether the fixture accepted. Both branches are one call so that no caller can
        // add itself to one side of the ratio and forget the other - a fixture that counted only its
        // refusals would drag every score down by existing.
        public static void Answer(bool right)
        {
            if (right) AnswersRight++;
            else AnswersWrong++;
        }

        // **TEST ONLY, AND THE NAME SAYS SO ON PURPOSE.** Called from `DebugStart.Seed` and nowhere
        // else - a run that jumps straight to the last room has no history for the board to report,
        // and this is what lets the board's layout and pacing be looked at without playing an hour
        // first. See the argument at `DebugStart.Seed`; nothing sampled is ever written to
        // `RunReport`.
        public static void SeedForTest(int rightAnswers, int wrongAnswers, int playerActs,
                                       int ghostActs, int deaths, int skips)
        {
            AnswersRight = rightAnswers;
            AnswersWrong = wrongAnswers;
            PlayerActs = playerActs;
            GhostActs = ghostActs;
            Deaths = deaths;
            Skips = skips;
        }

        public static void PlayerAct() => PlayerActs++;
        public static void GhostAct() => GhostActs++;
        public static void Death() => Deaths++;
        public static void Skip() => Skips++;
    }
}
