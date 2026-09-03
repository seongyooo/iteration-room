using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace IterationRoom
{
    // THE BOARD ON ROOM3-2N'S WALL. What the facility thought of the subject, printed a line at a
    // time while they stand in front of it.
    //
    // **IT IS A SCOREBOARD IN THE ROOM, NOT A SCREEN OVER THE GAME.** That distinction is the whole
    // brief and it decides every choice below. A HUD panel would be the game talking to the player;
    // this is the building talking to itself where the player happens to be standing, which is what
    // makes being graded by it feel like being graded rather than being scored. So:
    //   - it is world space, on the wall, at a size that has to be walked up to and looked at;
    //   - it is in ENGLISH and carries no `Loc` key, like every other piece of facility signage in
    //     the game (CLAUDE.md 3) - the menus and the ending card are the game addressing the player
    //     and get translated; `ERROR` and `FIRE AXE` and this do not;
    //   - the player can look away from it, walk off, or not read it. Nothing waits on their eyes.
    //
    // **IT WAKES WHEN THE PLAYER ARRIVES, NOT WHEN THE CYCLE BREAKS.** The break happens in room3-0,
    // a storey up; getting down here is a ladder climb that takes as long as it takes. A board that
    // started printing at the break would be most of the way through its readout before anyone was
    // in the room to read it, so the trigger is proximity - see `Update`. `FacilityFailure` powers
    // the thing ON at the break, which is what makes a dark wall panel become a lit one in the
    // corner of the eye on the way down.
    //
    // ON UNSCALED TIME, like every sequence that outlives the loop. By the time this runs the clock
    // has stopped and `RunOver` is set; a pause must not be able to freeze a half-printed verdict.
    public class EvaluationBoard : MonoBehaviour
    {
        // **THE REPORT IS ON EVERY WALL OF THE ROOM, not on a panel in it** (2026-09-01, by
        // request). One array per line of the readout, one entry per wall, all written together -
        // so whichever way the player is facing when they land, the facility is already talking to
        // them, and turning round does not mean missing it.
        //
        // It replaces a 9m monitor hung on the north wall. That was a screen in a room; this is the
        // room itself being the screen, which is what every other wall in this building already does
        // when the facility has something to say (`WallPanelDisplay`). The ERROR wave is suppressed
        // in here for the same reason - two things shouting on one surface is neither of them.
        //
        // Split into three arrays rather than one because the verdict is set at a different size and
        // the footer arrives after a beat of nothing; both are layout facts one string cannot carry.
        public Text[] bodies;
        // **UNUSED SINCE THE AXES WENT.** Kept as a field rather than deleted because the four
        // scores still exist and still measure (see `Print`); this is where they would come back.
        public Text[] verdicts;
        public Text[] footers;

        // The panel's own light and its emissive face, brought up when the board wakes. A monitor
        // that was already glowing before it had anything to say is a prop; one that comes on is an
        // event.
        public Renderer face;
        public Light glow;
        public Color offColour = new Color(0.04f, 0.045f, 0.05f);
        // **A DISPLAY IS A DARK PANEL WITH BRIGHT TYPE ON IT, NOT A LAMP.**
        //
        // This was a near-white blue at 0.62/0.86/0.95 with the emission driven to match, which lit
        // the whole face - and a face that bright is a face nothing can be read against. Play called
        // it exactly that: "the panel is too bright and the writing is hard to see". The panel is now
        // barely above off and the CONTRAST comes from the text, which is where the contrast on a
        // real monitor comes from.
        public Color onColour = new Color(0.055f, 0.075f, 0.09f);
        // What the glow at the panel is worth once it is on. A tenth of what it was: it is a wash on
        // the wall either side, not a light source for the room.
        public float glowIntensity = 0.35f;
        public float wakeSeconds = 0.9f;

        public AudioSource audioSource;
        // One per line printed. Short and dry - this is a machine writing, not a typewriter.
        public AudioClip lineClip;
        public AudioClip verdictClip;

        // The PA, which reads out each cycle's row as it appears. Bound at runtime like everything
        // else that crosses into the core scene - see `CycleBinding`.
        public NarrationDirector narration;

        // How close the player has to be for the readout to start, measured HORIZONTALLY. Generous
        // on purpose - it should have begun by the time they are looking at it, not be a wire they
        // trip - and 26m covers the far corner of a room that is 17.5 by 21 (22.7m away), so landing anywhere in it starts the readout.
        public float wakeRange = 26f;
        // How far below this panel's centre the floor of its room is. Authored by `SceneBuilder`
        // from the height it mounted the board at, so the two cannot disagree; a metre is taken off
        // it so that standing on the floor counts rather than only crouching on it.
        public float floorDrop = 3.6f;
        public Transform player;

        // Pacing. A line every `linePause`, with `sectionPause` at a blank - so the board reads in
        // paragraphs rather than as a scroll.
        public float linePause = 0.32f;
        // The extra beat after a CYCLE row, on top of `linePause`. Those rows are the only ones the
        // PA reads aloud, and they need long enough not to be cut off by the next one - four or five
        // seconds of assembled clips. See `NarrationDirector.AnnounceCycleResult`.
        public float cycleLinePause = 4.2f;
        public float sectionPause = 0.7f;
        // How long a score takes to roll from nothing to its value. The roll is what makes the
        // number read as measured rather than looked up.
        public float rollSeconds = 0.45f;
        // Before the bar, and before the verdict. The two silences the board has, and they are the
        // only reason the last word lands.
        public float beforeBar = 1.1f;
        public float beforeVerdict = 1.6f;
        public float barSeconds = 2.2f;

        // THE WIDTH OF THE TABLE, in characters. Every row is padded to it - see `Row` - so the
        // dotted leaders line up down the panel whatever the labels are. `SceneBuilder` sizes the
        // rect from this and the font size; the two must agree or the longest row silently wraps,
        // which is the failure CLAUDE.md 3 calls out for fixed-width labels.
        public int columns = 32;
        // Twenty cells, which is a bar the eye can count without counting.
        public int barCells = 20;

        // Set once the last line is up. `FinalRoomSequence` waits on this before the wall opens -
        // the cable car arriving over the top of the readout would be the ending interrupting its
        // own ending.
        public bool Finished { get; private set; }

        private readonly List<string> printed = new List<string>();
        private bool started;

        // The standard, authored by `SceneBuilder` like every other tuned value in the project.
        public EvaluationStandard standard = EvaluationStandard.Default;

        private void Awake()
        {
            Fill(bodies, string.Empty);
            Fill(verdicts, string.Empty);
            Fill(footers, string.Empty);
            SetLit(0f);
        }

        private static void Fill(Text[] texts, string content)
        {
            if (texts == null) return;
            foreach (Text text in texts)
                if (text != null) text.text = content;
        }

        // Called by `FacilityFailure` at the break, a storey above. Only powers the panel - it comes
        // up to a dark standby, with nothing on it. The readout waits for somebody to be standing in
        // front of it, which is a storey down and a climb away.
        public void PowerOn()
        {
            Powered = true;
            StartCoroutine(Wake());
        }

        // Both read by `EndingDeparture`, which has to tell "not started yet" from "never will".
        // A board that is not powered is one `FacilityFailure` was never given a reference to, and
        // waiting on it is waiting on nothing - see the note there, which this pair exists for.
        public bool Powered { get; private set; }
        public bool Printing => started && !Finished;

        // **WITHOUT `Powered` THE BOARD GRADES THE PLAYER IN THE MIDDLE OF THE PUZZLE.** Room3-2N
        // is not a room the player visits at the end - it is where the Bedlam cube is, so they are in
        // it, under this panel, for most of cycle 3. Proximity alone would start the readout the
        // first time they walked in with a block in their hands.

        private IEnumerator Wake()
        {
            float t = 0f;
            while (t < wakeSeconds)
            {
                t += EndingClock.Delta;
                SetLit(Mathf.Clamp01(t / wakeSeconds));
                yield return null;
            }
            SetLit(1f);
        }

        private void SetLit(float amount)
        {
            if (face != null && face.material != null)
            {
                Color c = Color.Lerp(offColour, onColour, amount);
                face.material.color = c;
                if (face.material.HasProperty("_EmissionColor"))
                    face.material.SetColor("_EmissionColor", c * amount);
            }
            if (glow != null) glow.intensity = amount * glowIntensity;
        }

        // **STARTED BY `EndingDeparture`, NOT BY WALKING NEAR IT** (2026-09-01).
        //
        // It used to trigger itself on proximity, which is one more thing that can fire at the wrong
        // moment - and it did: the readout is only correct once the player has climbed down into
        // room3-2N, and every other beat of the ending has to wait for that same fact. Two places
        // deciding when the player has arrived is two places to disagree.
        //
        // `EndingDeparture` owns the order of the last five minutes and nothing else; this is one of
        // the things it orders. Refuses twice over rather than trusting the caller: the panel has to
        // have been powered by the break, and it prints once.
        public void Begin()
        {
            if (started || !Powered) return;
            started = true;
            StartCoroutine(Print());
        }

        // **THE READOUT, AND IT IS NOW ONLY WHAT THE RUN COST** (2026-09-01, by request).
        //
        // It used to print four scored axes - MEMORY, ADAPTATION, COOPERATION, EFFICIENCY - a bar
        // and a verdict. Every one of them was a real ratio of things the run did, and that turned
        // out not to be the problem. **The problem is that nobody can read them.** A player who has
        // just climbed out of a hole is shown a word like ADAPTATION and a number, with nothing
        // anywhere in the preceding hour that says what the game measured or what a good number
        // would be. It reads as a grade you cannot check, which is worse than no grade.
        //
        // What is left is what the player counted themselves: how many iterations each cycle took,
        // how long, and the totals. Those are the two numbers the whole game is about, they are on
        // the HUD the entire time, and the player already knows whether their own is good.
        //
        // `RunTally` and `RunEvaluation` are NOT deleted. They are hooked at one point per side in
        // two files (see CLAUDE.md 4) and they still measure correctly; what changed is that nothing
        // displays them. If a grade is ever wanted again - on the title screen, or here with the
        // axes explained somewhere the player can reach - the measurement is already running.
        private IEnumerator Print()
        {
            IReadOnlyList<CycleRecord> records = LoopManager.Instance != null
                ? LoopManager.Instance.CycleRecords : null;

            yield return Line("EXPERIMENT COMPLETE");
            yield return Line($"SUBJECT {RunReport.SubjectNumber:000}");
            yield return Blank();

            int iterations = 0;
            float seconds = 0f;
            if (records != null)
                foreach (CycleRecord record in records)
                {
                    iterations += record.Iterations;
                    seconds += record.Seconds;
                    // **THE VOICE AND THE WALL TOGETHER.** Spoken as the row is printed rather than
                    // after the table is complete: the announcement is what the wall is saying, not a
                    // summary of it, and a PA reading out a table the player finished reading a
                    // minute ago is a PA talking to itself.
                    //
                    // The line takes about four seconds to say and the row takes `linePause` to
                    // print, so the wall runs ahead - which is the right way round. The reader
                    // catches up; the reverse would mean waiting for the voice with nothing on screen.
                    narration?.AnnounceCycleResult(record.Cycle, record.Iterations, record.Seconds);
                    yield return Line(Row($"CYCLE {record.Cycle:00}",
                                          $"{record.Iterations,3}   {RunReport.FormatClock(record.Seconds)}"));
                    // **WAIT FOR THE VOICE, DO NOT TIME IT.** `cycleLinePause` was a guess at how long
                    // five assembled clips take, and a guess that is a shade short cuts the line off
                    // mid-word - which is exactly what play heard. The row is already on the wall by
                    // now, so what this waits for is the sentence about it finishing.
                    yield return WaitForVoice();
                }

            yield return Blank();
            narration?.AnnounceTotalResult(iterations, seconds);
            // **THE SAME ORDER THE CYCLE ROWS USE** (2026-09-03). This waited for the whole
            // announcement and printed afterwards, so the wall sat blank for the four seconds the PA
            // spent reading the total out - the one figure the player most wants in front of them
            // while it is being said. Printed with the voice now, like every row above it.
            yield return Line(Row("TOTAL", $"{iterations,3}   {RunReport.FormatClock(seconds)}"));
            yield return WaitForVoice();
            // The column heading arrives AFTER the rows it describes, which is the wrong way round
            // on paper and the right way round here: the rows are printed one at a time and the
            // reader has already worked out what the two numbers are by the time this confirms it.
            yield return Line(Row(string.Empty, "ITER  TIME"));

            yield return Wait(beforeVerdict);
            // **NOTHING IS PRINTED HERE ANY MORE** (2026-09-03, by request). The verdict line went
            // first - there is nothing being judged - and the dismissal has now followed it. The
            // argument for keeping `SUBJECT MAY PROCEED` was that something has to tell the player
            // the facility is finished with them; what actually does that is the wall coming apart
            // and a cable car arriving, which is the same sentence said by the building instead of
            // to the player.
            //
            // The footer objects and their `Fill` are kept rather than deleted: they are already
            // cleared to empty in `Reset`, and a board that has somewhere to print a closing line
            // costs nothing until something wants one.
            if (audioSource != null && verdictClip != null) audioSource.PlayOneShot(verdictClip);

            Finished = true;
        }

        // `LABEL ......... VALUE`, padded to `columns`. The dots are a leader rather than a rule
        // because a monospace face has no other way to tie a label to a number across a gap - the
        // same reason `RunReport.Row` pads instead of laying out.
        private string Row(string label, string value)
        {
            int dots = Mathf.Max(1, columns - label.Length - value.Length - 2);
            return label + " " + new string('.', dots) + " " + value;
        }

        private IEnumerator Line(string text)
        {
            printed.Add(text);
            Redraw();
            if (audioSource != null && lineClip != null) audioSource.PlayOneShot(lineClip);
            yield return Wait(linePause);
        }

        private IEnumerator Blank()
        {
            printed.Add(string.Empty);
            Redraw();
            yield return Wait(sectionPause);
        }

        private void Redraw()
        {
            if (bodies == null) return;
            var sb = new StringBuilder();
            foreach (string line in printed) sb.AppendLine(line);
            Fill(bodies, sb.ToString());
        }

        // Until the PA has stopped talking, with a ceiling on it so a missing clip cannot stall the
        // end of the game. The cap is long enough that only a fault reaches it.
        private IEnumerator WaitForVoice()
        {
            yield return Wait(0.2f);

            float t = 0f;
            while (narration != null && narration.Speaking && t < 12f)
            {
                t += EndingClock.Delta;
                yield return null;
            }
            yield return Wait(0.35f);
        }

        private static IEnumerator Wait(float seconds)
        {
            float t = 0f;
            while (t < seconds)
            {
                t += EndingClock.Delta;
                yield return null;
            }
        }
    }
}
