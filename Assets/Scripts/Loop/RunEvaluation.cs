using System.Collections.Generic;
using UnityEngine;

namespace IterationRoom
{
    // THE STANDARD THE SUBJECT IS HELD TO. Every tuned number the evaluation uses, in one struct, so
    // that `SceneBuilder` owns them the way it owns every other constant in the project (CLAUDE.md
    // 2) - `RunEvaluation` below decides what the numbers MEAN and nothing else.
    //
    // **EVERY VALUE HERE IS A GUESS AND IS MEANT TO BE RETUNED.** The only clear this game has ever
    // had was played by the person who designed it, knowing every count going in, so the baselines
    // are one sample of a population of one. They are set DELIBERATELY LENIENT: the evaluation does
    // not gate anything - `SUBJECT MAY PROCEED` is printed whatever the verdict - so the cost of
    // being generous is nothing and the cost of being harsh is a first-time player being told they
    // were bad at a game they have just spent an hour finishing.
    [System.Serializable]
    public struct EvaluationStandard
    {
        // What a cycle is expected to cost, in iterations, index 0 being cycle 1. Read off the
        // 2026-08-20 clear (9 and 22) with cycle 3 estimated against cycle 2's shape, then loosened.
        // A run that matches these scores about 80; beating them is what the top of the range is for.
        public int[] baselineIterations;

        // What a well-spent iteration costs in seconds. 29.0 is the measured figure for a practised
        // player across three clears, and it is well under the 60-second budget because the loop is
        // meant to be ended the moment its errand lands (`EndCycleControl`). An iteration that runs
        // to the buzzer is one that spent its last seconds walking.
        public float targetIterationSeconds;
        // The clock itself. An iteration cannot cost more than this, so it is the top of the scale.
        public float budgetSeconds;

        // The bands the final score is read against. Note that NONE of them is a pass mark - see
        // `VerdictFor`.
        public int exemplaryScore;
        public int qualifiedScore;
        public int marginalScore;

        public static EvaluationStandard Default => new EvaluationStandard
        {
            baselineIterations = new[] { 12, 26, 26 },
            targetIterationSeconds = 29f,
            budgetSeconds = 60f,
            exemplaryScore = 88,
            qualifiedScore = 70,
            marginalScore = 50,
        };
    }

    // FOUR NUMBERS AND A WORD, computed once when the last cycle breaks.
    //
    // WHAT MAKES THIS WORTH BUILDING: the axes are real. Every one of them is a ratio of things the
    // run actually did, taken off `RunTally` and the cycle records, and a player who plays
    // differently gets a different number. That matters more here than in most places, because the
    // board is the last screen in the game and the whole of its effect rests on the player believing
    // it was watching - a scoreboard of decorative numbers is one the second playthrough catches out.
    //
    // AN UNMEASURED AXIS PRINTS `INSUFFICIENT DATA` RATHER THAN A ZERO, and the difference is not
    // cosmetic: a zero is a judgement and this has none to make. `Unmeasured` is the sentinel.
    public readonly struct RunEvaluation
    {
        public const int Unmeasured = -1;

        // How often the subject knew the answer before trying it. Accuracy at the fixtures that can
        // refuse - see `RunTally.Answer`.
        public readonly int Memory;
        // Whether the subject held up as the facility stopped explaining itself. The trend across
        // cycles, not the absolute figure - a player who was slow throughout and slow at the end
        // adapted fine; one who was quick and then fell apart did not.
        public readonly int Adaptation;
        // How much of the work was done by past selves. See the note on the field in `RunTally`, and
        // note that it is HIGH FOR EVERYONE who reaches this board - the game cannot be finished any
        // other way. room2-7's latch alone charges five past-self deliveries for every trip to
        // room2-0, and room3-1's door is a pad somebody else has to be standing on. That is not a
        // flaw in the measurement. It is the measurement telling the truth about the game.
        public readonly int Cooperation;
        // Work per iteration: the clock spent against what an iteration is worth, and the count
        // spent against what the cycle is worth.
        public readonly int Efficiency;

        public readonly int Final;
        // Reported, not scored. Dying is not penalised anywhere in this game - the timeline up to
        // the moment of death becomes a ghost like any other (CLAUDE.md 4) - so a death is a fact
        // about the run rather than a mark against it, and the board states it as one.
        public readonly int Failures;
        // ONE. Always one, and it is a measurement rather than a gag: this counts the distinct
        // people whose actions the facility recorded, and every past self in the building is the
        // subject. It is printed under COOPERATION so the two sit together and the reader does the
        // arithmetic themselves.
        public readonly int SubjectsInvolved;

        public readonly string Verdict;

        private RunEvaluation(int memory, int adaptation, int cooperation, int efficiency,
                              int final, int failures, string verdict)
        {
            Memory = memory;
            Adaptation = adaptation;
            Cooperation = cooperation;
            Efficiency = efficiency;
            Final = final;
            Failures = failures;
            SubjectsInvolved = 1;
            Verdict = verdict;
        }

        public static RunEvaluation Evaluate(IReadOnlyList<CycleRecord> records,
                                             EvaluationStandard standard)
        {
            int memory = ScoreMemory();
            int cooperation = ScoreCooperation();
            int adaptation = ScoreAdaptation(records, standard);
            int efficiency = ScoreEfficiency(records, standard);

            // The mean of whatever could be measured. An axis that came back `Unmeasured` is left
            // out of both the sum and the divisor rather than counted as zero - see the note on the
            // sentinel above.
            int total = 0, counted = 0;
            foreach (int score in new[] { memory, adaptation, cooperation, efficiency })
            {
                if (score == Unmeasured) continue;
                total += score;
                counted++;
            }
            int final = counted > 0 ? Mathf.RoundToInt((float)total / counted) : Unmeasured;

            return new RunEvaluation(memory, adaptation, cooperation, efficiency, final,
                                     RunTally.Deaths, VerdictFor(final, standard));
        }

        // ACCURACY, PLAINLY: of every answer the subject gave to a fixture that could say no, how
        // many were right. Nothing is weighted - a wrong billiard ball and a wrong Bedlam block are
        // the same mistake, which is not remembering something the building had already shown you.
        private static int ScoreMemory()
        {
            int attempts = RunTally.AnswersRight + RunTally.AnswersWrong;
            if (attempts <= 0) return Unmeasured;
            return Mathf.RoundToInt(100f * RunTally.AnswersRight / attempts);
        }

        // The past selves' share of every action that changed the world.
        //
        // THE SHAPE OF THIS NUMBER IS WORTH KNOWING BEFORE READING IT. Ghost work grows with the
        // SQUARE of the iteration count - every iteration replays every previous one - while the
        // living player's grows linearly, so a long run scores higher here than a short one for
        // reasons that have nothing to do with skill. That is not being corrected for. The axis is
        // not "did you cooperate well", it is "how much of this was done by people who are you", and
        // on that question the arithmetic is exactly right.
        private static int ScoreCooperation()
        {
            int total = RunTally.PlayerActs + RunTally.GhostActs;
            if (total <= 0) return Unmeasured;
            return Mathf.RoundToInt(100f * RunTally.GhostActs / total);
        }

        // DID THE SUBJECT HOLD UP. Each cycle's cost against what that cycle is expected to cost
        // gives a performance figure that is comparable ACROSS cycles - which is the whole trick,
        // since cycle 2 is twice cycle 1 and comparing the raw counts would call every player who
        // ever lived worse at the end than at the beginning.
        //
        // Needs two finished cycles to have a trend at all. A run with fewer is not scored zero
        // here, it is not scored - the only way to see that in normal play is a debug jump.
        private static int ScoreAdaptation(IReadOnlyList<CycleRecord> records,
                                           EvaluationStandard standard)
        {
            if (records == null || records.Count < 2) return Unmeasured;

            float first = Performance(records[0], standard);
            float last = Performance(records[records.Count - 1], standard);
            if (first <= 0f) return Unmeasured;

            // Holding steady is 80, and that is the point of the curve: the later cycles are longer
            // and less explained, so matching your own early form in them is a good result rather
            // than a neutral one. Doubling it tops out at 100; falling to half lands at 70, which is
            // a mark and not a punishment.
            float ratio = last / first;
            return Mathf.RoundToInt(Mathf.Clamp(60f + 20f * ratio, 0f, 100f));
        }

        // How well one cycle went against its baseline. Above 1 is better than expected.
        private static float Performance(CycleRecord record, EvaluationStandard standard)
        {
            int baseline = BaselineFor(record.Cycle, standard);
            if (record.Iterations <= 0) return 0f;
            return (float)baseline / record.Iterations;
        }

        private static int BaselineFor(int cycle, EvaluationStandard standard)
        {
            int[] baselines = standard.baselineIterations;
            if (baselines == null || baselines.Length == 0) return 20;
            int index = Mathf.Clamp(cycle - 1, 0, baselines.Length - 1);
            return Mathf.Max(1, baselines[index]);
        }

        // TWO HALVES, EQUALLY WEIGHTED, because there are two ways to waste this game and they are
        // independent: spending iterations you did not need, and spending seconds inside the ones
        // you did. A player can be bad at exactly one of them.
        private static int ScoreEfficiency(IReadOnlyList<CycleRecord> records,
                                           EvaluationStandard standard)
        {
            if (records == null || records.Count == 0) return Unmeasured;

            int iterations = 0, baseline = 0;
            float seconds = 0f;
            foreach (CycleRecord record in records)
            {
                iterations += record.Iterations;
                seconds += record.Seconds;
                baseline += BaselineFor(record.Cycle, standard);
            }
            if (iterations <= 0) return Unmeasured;

            // THE COUNT. Matching the baseline is full marks rather than the middle of the scale -
            // the baselines are already a practised player's numbers, and a scale that demanded
            // beating them would score the person who set them at less than 100.
            float economy = Mathf.Clamp01((float)baseline / iterations);

            // THE CLOCK, measured as how much of the 60 seconds an average iteration did NOT need.
            // An iteration that runs to the buzzer scores nothing here and one cut at the target
            // scores full, because ending the iteration the moment its errand lands is the single
            // biggest thing separating a practised run from a first one - three clears took the
            // clock from 8:29 to 4:47 while taking only four iterations off.
            float average = seconds / iterations;
            float span = Mathf.Max(1f, standard.budgetSeconds - standard.targetIterationSeconds);
            float pace = Mathf.Clamp01((standard.budgetSeconds - average) / span);

            return Mathf.RoundToInt(100f * (0.5f * economy + 0.5f * pace));
        }

        // **NONE OF THESE IS A PASS MARK, AND THE LAST LINE OF THE BOARD SAYS SO WHATEVER THIS
        // RETURNS.** The evaluation gates nothing: every subject who reaches this room has already
        // done the only thing the facility measures for, which is getting out, and the grade is what
        // it thought of the manner. A `DEFICIENT` subject is told to proceed in exactly the same
        // words as an `EXEMPLARY` one - which is a colder thing for the building to say than a
        // refusal would have been.
        private static string VerdictFor(int final, EvaluationStandard standard)
        {
            if (final == Unmeasured) return "UNCLASSIFIED";
            if (final >= standard.exemplaryScore) return "EXEMPLARY";
            if (final >= standard.qualifiedScore) return "QUALIFIED";
            if (final >= standard.marginalScore) return "MARGINAL";
            return "DEFICIENT";
        }
    }
}
