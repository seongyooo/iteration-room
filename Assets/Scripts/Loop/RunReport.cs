using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace IterationRoom
{
    // WHAT A RUN COST, formatted once and stored once.
    //
    // It exists because two screens now say the same thing and neither owns it: the ending card
    // reports the run the player has just finished, and the title screen's RECORD page reports the
    // last one they finished at all. Two copies of a padded table is two copies that drift.
    //
    // The storage half is here for the same reason `GameSettings` holds the saved cycle: a run's
    // record has to survive the process, and PlayerPrefs is the only thing in this project that does.
    public static class RunReport
    {
        // `cycle:iterations:seconds` per entry, pipe-separated. Written by `LoopManager` when a run
        // ends and read by the title screen; nothing else touches it.
        //
        // INVARIANT CULTURE, and that is not a formality. `float.ToString()` under a Korean or German
        // locale writes `602,759`, and the `:` split would then read the fraction as a field - so a
        // run recorded on one machine and read on another comes back as nonsense or as nothing.
        private const string Key = "iteration.lastRun";
        private const char EntrySeparator = '|';
        private const char FieldSeparator = ':';

        public static void Save(IReadOnlyList<CycleRecord> records)
        {
            if (records == null || records.Count == 0) return;

            var sb = new StringBuilder();
            foreach (CycleRecord r in records)
            {
                if (sb.Length > 0) sb.Append(EntrySeparator);
                sb.Append(r.Cycle).Append(FieldSeparator)
                  .Append(r.Iterations).Append(FieldSeparator)
                  .Append(r.Seconds.ToString("0.###", CultureInfo.InvariantCulture));
            }

            PlayerPrefs.SetString(Key, sb.ToString());
            // Written through immediately rather than batched into `GameSettings.Save` - this happens
            // once at the end of a run, and the whole value of it is surviving the player closing the
            // window on the ending card.
            PlayerPrefs.Save();
        }

        // Null when there is nothing stored OR when what is stored cannot be read. A record that
        // fails to parse is treated as no record at all: the title screen simply does not offer the
        // page, which is a better answer than a table with a blank row in it.
        public static CycleRecord[] Load()
        {
            string raw = PlayerPrefs.GetString(Key, string.Empty);
            if (string.IsNullOrEmpty(raw)) return null;

            string[] entries = raw.Split(EntrySeparator);
            var records = new List<CycleRecord>(entries.Length);

            foreach (string entry in entries)
            {
                string[] parts = entry.Split(FieldSeparator);
                if (parts.Length != 3) return null;
                if (!int.TryParse(parts[0], out int cycle)) return null;
                if (!int.TryParse(parts[1], out int iterations)) return null;
                if (!float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture,
                                    out float seconds)) return null;

                records.Add(new CycleRecord(cycle, iterations, seconds));
            }

            return records.Count > 0 ? records.ToArray() : null;
        }

        public static bool HasRun => Load() != null;

        // THE TABLE. Padded rather than laid out, because the typeface is monospaced and a space is
        // exactly one cell - the same fact `EndingSequence.Space` rests on.
        //
        // The total is the SUM OF THE PARTS, not a clock read at the end of the run. Those are not
        // the same number and the difference is not rounding: everything between cycles - the gas,
        // the collapse, the walk to the hatch, the wake-up - happens with the iteration clock
        // stopped, so a wall-clock total would bill the player for minutes they were not timed for.
        public static string Table(IReadOnlyList<CycleRecord> records)
        {
            if (records == null || records.Count == 0) return string.Empty;

            var lines = new StringBuilder();
            int iterations = 0;
            float seconds = 0f;

            foreach (CycleRecord record in records)
            {
                iterations += record.Iterations;
                seconds += record.Seconds;
                lines.AppendLine(Row($"CYCLE {record.Cycle}", record.Iterations, record.Seconds));
            }

            // A blank line for the rule a monospace face cannot draw.
            lines.AppendLine();
            lines.Append(Row("TOTAL", iterations, seconds));
            return lines.ToString();
        }

        // `CYCLE 1` is seven cells, so everything else is padded to it and the three columns line up
        // whatever the numbers are. Iterations right-aligned in three, which covers a run nobody will
        // ever have; the clock is right-aligned in ten, which fits `59:59.999` with a cell to spare.
        public static string Row(string label, int iterations, float seconds) =>
            $"{label,-7}  {iterations,3} ITERATIONS  {FormatTime(seconds),10}";

        // `M:SS.mmm` - the run's clock to the millisecond.
        //
        // FLOOR THE WHOLE SECONDS, ROUND THE MILLISECONDS, CLAMP THE CARRY. Three different rules for
        // three different reasons, and each of the other combinations is wrong:
        //   - flooring the seconds is the rule this had at whole seconds and keeps: 14:59.9 read as
        //     15:00 claims a minute the run did not spend;
        //   - flooring the milliseconds TOO is systematically a millisecond low, because a float never
        //     holds the value it was written as - 287.416 is stored as 287.41598, and truncating that
        //     prints `.415` for every run;
        //   - rounding them without a clamp can produce 1000, which prints as `59.1000`.
        // Clamped, the only case that is not nearest-millisecond is the top of a second, where it
        // floors - which is exactly where flooring was wanted in the first place.
        //
        // WHAT THE THIRD DECIMAL IS WORTH. It is the clock the GAME actually ran on, reported at full
        // resolution rather than a more precise measurement taken alongside: `LoopManager.ElapsedTime`
        // is a float summing `Time.deltaTime` frame by frame, so over a ten-minute run the accumulated
        // rounding is on the order of a millisecond or two. The digit is real and it is not a stopwatch.
        public static string FormatTime(float seconds)
        {
            int total = Mathf.Max(0, Mathf.FloorToInt(seconds));
            int minutes = total / 60;
            int secs = total % 60;
            int ms = Mathf.Clamp(Mathf.RoundToInt((seconds - total) * 1000f), 0, 999);
            return $"{minutes}:{secs:00}.{ms:000}";
        }
    }
}
