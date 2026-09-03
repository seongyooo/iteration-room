using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace IterationRoom
{
    // ONE CYCLE'S BILL: which cycle it was, how many iterations it took, and how long they lasted.
    //
    // Taken at the moment the cycle breaks, because that is the only moment the numbers exist -
    // `LoopManager.EndCycleState` zeroes the iteration count and the clock behind the shut eyelids
    // so the next cycle starts from nothing, and a run with three cycles in it would otherwise be
    // able to report only the last one.
    //
    // A readonly struct rather than a class: it is three numbers that never change once written, and
    // the list of them is the whole of a run's history.
    public readonly struct CycleRecord
    {
        // 1-based, and the same number the HUD shows - see LoopManager.CycleNumber.
        public readonly int Cycle;
        public readonly int Iterations;
        public readonly float Seconds;

        public CycleRecord(int cycle, int iterations, float seconds)
        {
            Cycle = cycle;
            Iterations = iterations;
            Seconds = seconds;
        }
    }

    // Drives the 60-second loop: shows the "Iteration N" label, resets the player to bed on timeout,
    // and turns the just-finished recording into a new accumulating ghost.
    //
    // AND THE CYCLES THOSE ITERATIONS ARE GROUPED INTO. Filling a cycle's console does not end the
    // game any more - it ends that cycle, and the player drops into the next one's bed room. Nothing
    // inside a cycle changes because of this: one bed, sixty seconds, ghosts accumulating forever.
    // See `docs/cycle-design.md`.
    public class LoopManager : MonoBehaviour
    {
        public static LoopManager Instance { get; private set; }

        public float loopDuration = 60f;

        // EVERY CYCLE, IN ORDER. One entry means the game behaves exactly as it did before cycles
        // existed, which is the property this array was chosen for.
        //
        // **"The last cycle" is derived from this array and must never be hardcoded.** The last one is
        // the one with no successor; that is what routes into `EndingSequence`. More cycles are
        // intended, so a literal cycle number anywhere in this file is a bug waiting for cycle 3.
        // NOT SERIALIZED FROM THE SCENE ANY MORE, and it cannot be: each cycle lives in a scene of its
        // own, and Unity drops a serialized reference that points into another one - without an error.
        // Filled at the top of `RunLoop` from `sceneLoader`, which is also the only place that can wait
        // for the scenes to arrive.
        [System.NonSerialized] public Cycle[] cycles;

        // How long the player has in room1-1 after the fall, before the card. Enough to stand up,
        // turn round and understand where they are; not enough to go and try the door.
        public float afterTheFall = 5f;

        // Brings the cycle scenes in, and re-establishes everything the split would otherwise have
        // broken. Both run once, before the first iteration - see the top of `RunLoop`.
        public CycleSceneLoader sceneLoader;
        public CycleBinding binding;

        public PlayerRecorder playerRecorder;
        public FirstPersonController playerController;
        public PlayerHand playerHand;
        public GhostReplayer ghostPrefab;
        public Transform ghostParent;
        public IterationLabel iterationLabel;
        public WakeUpSequence wakeUpSequence;

        // THE LOADING SCREEN, over the shut eyelids. See SceneBuilder for what it is; this holds it
        // only long enough to answer "is the game ready", which is a question only this coroutine
        // can answer.
        public CanvasGroup loadingBackdrop;
        public NarrationDirector narration;
        public RoomAmbience ambience;
        public CameraShaker cameraShaker;
        public EndingSequence endingSequence;

        // Reset at a cycle boundary, so a new cycle's wall signs can teach the control again. See
        // EndCycleControl.ResetUseCount for why that is not a contradiction of its no-reset rule.
        public EndCycleControl endCycleControl;

        // Puts the player out at a boundary. Fires with no warning at all - see SleepingGas.
        public SleepingGas sleepingGas;

        // HOW LONG THE PLAYER GETS IN THE NEW ROOM BEFORE THE GAS. Measured from the moment they
        // land, not from the moment they drop: the fall is the last thing they chose to do and it
        // should be allowed to finish.
        //
        // Five seconds is enough to stand up, turn round and see that there is a bed - which is the
        // whole point of the beat, because the bed is what tells them what is about to happen. Much
        // less and the room is gassed before it has been read; much more and the player starts
        // looking for something to do, and there is nothing.
        public float settleBeforeGas = 5f;

        // How long before the reset the room starts coming apart: the wall displays blow out and
        // the view begins to judder, both building to the moment the cycle takes you.
        public float collapseLeadTime = 10f;

        // "10 seconds remaining." is cued earlier than ten seconds on purpose. The line runs about
        // two seconds, and from nine down the digit countdown replaces whatever the announcer is
        // saying every whole second - cued at exactly T-10 it gets clipped after one word.
        public int tenSecondCueAt = 12;

        public float ElapsedTime { get; private set; }
        public int IterationNumber { get; private set; }

        // WHAT EACH CYCLE COST, one entry per cycle the player has broken, in the order they broke
        // them. Written once per cycle and never rewritten - a run's history rather than its state.
        //
        // It exists because the ending had one number for a game that now has several cycles in it:
        // "TOTAL TIME 10:04" after a run that spent 4:47 in cycle 1 and 10:04 in cycle 2 is not a
        // total of anything, it is the last cycle's clock wearing the word TOTAL. See EndingSequence.
        private readonly System.Collections.Generic.List<CycleRecord> cycleRecords =
            new System.Collections.Generic.List<CycleRecord>();

        public System.Collections.Generic.IReadOnlyList<CycleRecord> CycleRecords => cycleRecords;

        // THE PLAYER ASKING FOR THIS CYCLE BACK FROM NOTHING. Set by the pause menu, read once by the
        // iteration loop, and cleared the moment it is acted on.
        //
        // WHY IT EXISTS. A cycle is its ghosts, and a ghost cannot be undone - "you can only add,
        // never take away" is the whole premise of the loop, and it is also the one way this game can
        // be made unwinnable: a past self that takes an object and fumbles it holds that object for
        // the rest of the run, and a wrong delivery a ghost repeats every sixty seconds is a room
        // that will not open again. There is no move inside the fiction that repairs that. This is
        // the outside-the-fiction answer, and it lives in the pause menu with QUIT rather than on a
        // control in the world, so it reads as a thing you do to the GAME rather than in it.
        private bool restartRequested;

        // 1-based, and it does NOT reset. The iteration count starts again at every bed; this is what
        // says which bed. Read by the HUD, which puts it on its own line above the iteration.
        public int CycleNumber { get; private set; }
        private int cycleIndex;

        // The cycle currently being played, and whether anything follows it.
        public Cycle Current =>
            cycles != null && cycleIndex >= 0 && cycleIndex < cycles.Length ? cycles[cycleIndex] : null;
        // A NEXT CYCLE HAS TO EXIST *AND* BE PLAYABLE. The second half is what ends the game at
        // cycle 3 while leaving cycle 4 standing in the world for the ending to fly past - see
        // `Cycle.playable`. Without it, "is there another scene loaded" and "is there another act"
        // are the same question, and they stopped being the same question the moment the building
        // had to contain a room the game does not enter.
        private bool HasNextCycle =>
            cycles != null && cycleIndex + 1 < cycles.Length
            && cycles[cycleIndex + 1] != null && cycles[cycleIndex + 1].playable;

        // The clock's own running total, across every iteration this CYCLE has spent - what
        // EndingSequence reports alongside the iteration count. `totalElapsedTime` accumulates each
        // iteration's ElapsedTime the moment that iteration ends (see the top of RunLoop's inner
        // while); the CURRENT iteration's own ElapsedTime is still live and not yet folded in, which
        // is exactly right at the one moment this is read - the ending, while the escaping iteration
        // is still the current one and has not gone through that accumulation step itself.
        public float TotalElapsedTime => totalElapsedTime + ElapsedTime;
        private float totalElapsedTime;

        // True only while the clock is actually running - not during the wake-up, not during the
        // eyelid close, not during a cycle boundary. The end-cycle control reads this so it can't be
        // charged up out of turn.
        public bool IterationRunning { get; private set; }

        // Set by PauseMenu. The loop itself needs no knowledge of it - Time.timeScale freezes the
        // whole coroutine - but the scripts that read raw Input in Update do, because Update keeps
        // running at a zero time scale and E, N and the mouse would all still land.
        public bool IsPaused { get; private set; }

        // "The game is accepting player input right now." Every Update that reads a key should
        // gate on this rather than on IterationRunning alone; the two only differ while paused.
        public bool AcceptsInput => IterationRunning && !IsPaused && !RunOver && !CycleBreaking;

        // The player got out of the LAST cycle and the ending is playing. Read by PauseMenu, which
        // must not let Escape open an overlay over the last thing in the game - Resume would then hand
        // back a frozen room with no loop left running to unfreeze it.
        //
        // **There is no way back from this, by design, and that is why a cycle boundary must not use
        // it.** Once true, AcceptsInput is false forever and every interactable in the game is dead.
        public bool RunOver { get; private set; }

        // A CYCLE ENDED AND ANOTHER FOLLOWS. Distinct from RunOver in the one way that matters: it is
        // cleared again when the next cycle starts.
        //
        // The player keeps control - walking out through what they built is the whole of the beat -
        // but no fixture answers a key, because there is nothing left to operate and the way out is
        // the only thing that can happen next. That matches the rule that the last stretch before an
        // escape asks for no new operations.
        public bool CycleBreaking { get; private set; }

        public void SetPaused(bool paused) => IsPaused = paused;

        private bool endRequested;

        // The player choosing to cut this iteration short. 60 seconds is a ceiling, not a quota: once
        // you have done what you came to do, the rest is dead time, and spending it is the one
        // thing the loop never let you decide.
        //
        // It sets a flag rather than shoving ElapsedTime to loopDuration, because ElapsedTime is
        // the timestamp written into every recorded frame - forcing it would corrupt the tail of
        // the timeline the ghost is about to replay. The iteration still ends by the normal path.
        //
        // The cost is real and lands next loop: the recording stops here too, so the ghost this
        // makes only covers the seconds you actually spent. Deciding when to quit *is* deciding how
        // long your past self keeps standing on the pad. See GhostReplayer.Tick, which releases
        // everything once a timeline runs out - without that, quitting early would be free.
        // **WHY THIS TAKES A REASON.** One call ends an iteration early and two very different
        // things use it: `EndCycleControl` when the player decides the errand has landed, and
        // `CrushingBarrier`/`KillVolume` when the player dies. They are indistinguishable from in
        // here - both stop the clock and keep the recording - and the evaluation has to tell them
        // apart, because one of them is a skill and the other is a fact. Defaulted to `Skipped` so
        // that the meaning of an un-updated caller is the harmless one.
        public enum EndReason { Skipped, Killed }

        public void EndCycleEarly(EndReason reason = EndReason.Skipped)
        {
            if (!IterationRunning) return;
            endRequested = true;
            if (reason == EndReason.Killed) RunTally.Death();
            else RunTally.Skip();
        }

        private readonly List<GhostReplayer> ghosts = new List<GhostReplayer>();

        private void Awake()
        {
            Instance = this;
        }

        private void Start()
        {
            // A RUN STARTS HERE, and the tally starts with it. Explicitly rather than in a static
            // initialiser: the Editor can be set to skip the domain reload between play sessions,
            // and static counters that survived that would quietly judge this run on the last one's
            // mistakes. See `RunTally.Begin`.
            RunTally.Begin();
            GhostArchive.Begin();

            // A TEST RUN THAT WANTS A REPORT BEHIND IT. Off unless an editor menu armed it - see
            // `DebugStart.Seed`, which carries the whole argument for why the one screen in the game
            // that reports on the run is also the one screen that cannot be tested without inventing
            // one. The records below are what the tally is describing, so the two agree.
            if (DebugStart.SampleReport)
            {
                DebugStart.Seed();
                cycleRecords.Add(new CycleRecord(1, 9, 260.640f));
                cycleRecords.Add(new CycleRecord(2, 22, 602.759f));
            }

            StartCoroutine(RunLoop());
        }

        // The whole loop lives in one coroutine rather than Update, because an iteration is not
        // just a timer any more - it opens and closes with the wake-up sequence, and the clock and
        // the ghosts have to stay frozen while that plays out. Recording deliberately starts only
        // once the player has control, so every ghost's timeline covers the same window.
        private IEnumerator RunLoop()
        {
            // THE CYCLES ARRIVE FIRST, AND NOTHING BELOW IS SAFE UNTIL THEY HAVE.
            //
            // Each cycle is its own scene now (see CycleSceneLoader for why), so `cycles` cannot be a
            // serialized array any more - Unity drops a reference that crosses a scene, silently. The
            // loader brings the scenes in and hands the Cycle components back; `CycleBinding` then
            // re-establishes every OTHER reference the split would have broken. Both happen here,
            // before the first line of the game, because this coroutine is the only place that can
            // WAIT for them - an Awake cannot.
            // EYES SHUT BEFORE ANYTHING ELSE. See WakeUpSequence.ShutInstantly: everything below this
            // takes at least a frame and some of it takes many, and until the awake pass runs the
            // camera is pointed at whatever the scenes were saved with from wherever the player
            // prefab was built. Shutting first makes the loading window black rather than a glimpse
            // of a room nobody is in.
            wakeUpSequence?.ShutInstantly();
            if (loadingBackdrop != null) loadingBackdrop.alpha = 1f;

            if (sceneLoader != null)
            {
                yield return sceneLoader.LoadAll();
                cycles = sceneLoader.Cycles;
            }
            binding?.BindAll(cycles);

            // Before anything: look around the room and set the mouse sensitivity. Outside both
            // whiles, so it happens exactly once in the game, and before IterationNumber has been
            // incremented - the clock is stopped, IterationRunning is false, and every interactable is
            // therefore already inert. The player has full control meanwhile, which is the point;
            // iteration 1 teleports them back to the bed regardless.
            // STARTING PARTWAY ALONG. Everything before the chosen cycle is skipped outright: its
            // rooms go to sleep and the chosen one wakes, exactly as a boundary would have left them.
            //
            // `CycleNumber` is pre-set rather than counted up to, because the loop increments it on
            // entry - so cycle 3 starts from 2 and the HUD reads what it would have read.
            if (DebugStart.StartCycle > 1 && cycles != null)
            {
                cycleIndex = Mathf.Clamp(DebugStart.StartCycle - 1, 0, cycles.Length - 1);
                CycleNumber = cycleIndex;
            }

            // EXACTLY ONE CYCLE IS AWAKE, AND IT IS DECIDED HERE - not by what each scene happened to
            // be saved with. Every cycle root now ships asleep (see SceneBuilder.SleepCycle), so this
            // is the only thing that ever turns one on at startup, and there is no window in which a
            // cycle nobody is in has been loaded and is still rendering.
            if (cycles != null)
                for (int i = 0; i < cycles.Length; i++) cycles[i]?.SetAwake(i == cycleIndex);

            // THE WORLD IS READY. The backdrop goes and the eyelids are what is left underneath -
            // shut, and opened by iteration 1's own wake-up - which is the very next thing now that
            // the calibration step is gone. Faded rather than cut, so the hand-off from picture to
            // black is not a blink.
            if (loadingBackdrop != null)
            {
                for (float e = 0f; e < 0.45f; e += Time.unscaledDeltaTime)
                {
                    loadingBackdrop.alpha = 1f - Mathf.Clamp01(e / 0.45f);
                    yield return null;
                }
                loadingBackdrop.alpha = 0f;
                loadingBackdrop.gameObject.SetActive(false);
            }

            // **THE SENSITIVITY STEP IS GONE, WITH THE ROOM IT LIVED IN** (2026-08-31, by
            // request). It stood between PLAY and the first iteration: a shell of its own to walk
            // into, a number to set with the scroll wheel, and an E press on the far wall to leave -
            // a minute of standing still before the clock had ever run. The number is still
            // adjustable, on the title screen's SETTINGS page, where it always was.
            //
            // What the room TAUGHT was worth keeping and has moved into the game: the control
            // pictograms are on room1-1's south wall for iteration 1 (see `ControlsWall`), which is
            // the wall the wake-up leaves the player looking at.
            //
            // The step was already skipped on a touch device and on both debug shortcuts, so nothing
            // downstream ever assumed it had run.


            // THE OUTER LOOP IS CYCLES. Exited only by the last one, which has no successor to cross
            // to and goes to the ending instead.
            while (true)
            {
                CycleNumber++;

                // WHERE THE RUN GOT TO, written the moment a cycle begins rather than when it ends -
                // a player who closes the window halfway through cycle 2 has reached cycle 2, and
                // CONTINUE should say so. See GameSettings.SavedCycle for why it is a cycle and not
                // a moment.
                GameSettings.SavedCycle = CycleNumber;

                // The recorder samples THIS cycle's interactables. Assigned here rather than wired
                // once, because an entry's index is its bit in RecordedFrame.signals and each cycle
                // numbers its own from zero - the boundary teardown is what makes that safe.
                if (playerRecorder != null && Current != null)
                    playerRecorder.interactables = Current.ghostInteractables;

                // AND THE WAKE-UP'S PANELS, or a new cycle wakes in an unlit room. The panels boot in
                // WakeUpSequence, which held ONE reference wired at build time - so cycle 2's walls
                // were never powered up and stayed at their off colour, which is nearly black.
                if (wakeUpSequence != null && Current != null)
                    wakeUpSequence.wallPanels = Current.wallPanels;

                // The inner loop is iterations, and it is the loop this game is about.
                while (true)
                {
                    IterationNumber++;
                    // Folded in BEFORE the reset, so the iteration just finished counts toward the
                    // total exactly once. On the very first pass ElapsedTime is still its default
                    // zero, so this is a harmless no-op rather than a special case to guard.
                    totalElapsedTime += ElapsedTime;
                    ElapsedTime = 0f;

                    Transform bed = Current != null ? Current.bedSpawnPoint : null;
                    if (playerController != null && bed != null)
                        playerController.Teleport(bed.position, bed.rotation);

                    // The doors are world state, so the loop has to rewind them too - and only after
                    // the teleport, so a player standing in a doorway is already back at the bed
                    // rather than inside the slab when it snaps shut.
                    Current?.CloseDoors();

                    // Ghosts let go first, then the player, then the rooms hide what they own. All
                    // three orderings matter and for the same reason: an item has to be back at its
                    // parked position BEFORE the thing that hides it runs, or it ends up visible on
                    // the floor of a room whose balloons have not been popped yet.
                    //
                    // The ghost pass is separate from ResetPlayback below rather than folded into it,
                    // because ResetPlayback runs after the room resets - by then the key is already
                    // hidden, and handing it back at that point would un-hide it.
                    foreach (var ghost in ghosts) ghost.ReleaseCarried();

                    playerHand?.ReturnAll();

                    // The catch-all, and it is not redundant with the two lines above. Those clear the
                    // holders' own bookkeeping; this puts every object back. A key a GHOST left in the
                    // lock belongs to neither list - it is not in the player's `taken` and the ghost
                    // let go of it the moment the socket accepted it - so before this it stayed in the
                    // keyhole with IsCarried true and no ghost could ever pick it up again.
                    ItemRegistry.ReturnAllToOrigin();

                    // Drawers, the balloon field, the console and the two placement rooms, in the one
                    // order that works. AFTER the sweep, always: the sweep is what puts objects back,
                    // and this is what forgets who was home. See Cycle.ResetRooms.
                    Current?.ResetRooms();

                    // Hidden for the whole wake-up: resetting parks them all on the bed spawn, which
                    // is exactly where the player is about to open their eyes.
                    foreach (var ghost in ghosts)
                    {
                        ghost.ResetPlayback();
                        ghost.SetVisible(false);
                    }

                    // The announcer confirms the reset under the closed eyelids. Iteration 1 opens a
                    // cycle rather than resetting it, so it gets no line. (The pull and the shutdown
                    // that precede this are fired at the *end* of the previous iteration, below, which
                    // is why they need no such guard - there is always an iteration before them.)
                    if (IterationNumber > 1) narration?.AnnounceNewCycle();

                    iterationLabel?.Show(CycleNumber, IterationNumber);

                    if (wakeUpSequence != null)
                        yield return wakeUpSequence.WakeUp(playerController);

                    // Announced as the clock actually starts, which is also the moment control
                    // returns.
                    narration?.AnnounceIteration(IterationNumber);

                    // Back in view exactly as the clock starts, which is also the frame they start
                    // moving - so they appear already walking away rather than blinking into being.
                    foreach (var ghost in ghosts)
                        ghost.SetVisible(true);

                    playerRecorder?.BeginRecording();

                    int lastCueSecond = int.MaxValue;
                    // Armed here, not at the top of the iteration: an input that lands during the
                    // wake-up would otherwise end the cycle the instant the clock started.
                    endRequested = false;
                    IterationRunning = true;

                    // AFTER the clock is live, because everything it drives gates on AcceptsInput -
                    // a console asked to rise while the iteration is not running does nothing.
                    //
                    // EVERY ITERATION until the boundary is crossed, not just the first. The three
                    // objects are swept home and hidden when a clock runs out, so a shortcut that
                    // fired once would leave a tester who fumbled the sixty seconds stranded at the
                    // bed with nothing to carry and no way back to the state they asked for.
                    if (DebugStart.AtCycleBoundary) yield return JumpToBoundary();

                    while (ElapsedTime < loopDuration && !endRequested && !CycleComplete
                        && !restartRequested)
                    {
                        ElapsedTime += Time.deltaTime;

                        foreach (var ghost in ghosts)
                            ghost.Tick(ElapsedTime);

                        // Ceil, so a cue lands as the clock crosses its whole second rather than a
                        // frame after it. Guarding on a falling second also means one cue per second
                        // no matter the frame rate.
                        int remaining = Mathf.CeilToInt(loopDuration - ElapsedTime);
                        if (remaining < lastCueSecond)
                        {
                            lastCueSecond = remaining;
                            if (remaining == tenSecondCueAt) narration?.AnnounceTenSeconds();
                            else if (remaining >= 1 && remaining <= 9) narration?.AnnounceCountdown(remaining);
                        }

                        // Squared, so the collapse is barely there at first and then runs away with
                        // itself - a linear ramp reads as a slider being dragged.
                        float toEnd = loopDuration - ElapsedTime;
                        float collapse = collapseLeadTime > 0f
                            ? 1f - Mathf.Clamp01(toEnd / collapseLeadTime)
                            : 0f;
                        Panels?.SetFlare(collapse * collapse);
                        cameraShaker?.SetIntensity(collapse);

                        yield return null;
                    }

                    IterationRunning = false;

                    // THE ONLY WAY OUT OF THE INNER WHILE, and it has to be taken HERE - before any
                    // of what follows. Everything below this point is an iteration closing and
                    // re-opening: the collapse held at full, the pull-in, the blink, the panels going
                    // out, the recording becoming another ghost. None of it should happen to a player
                    // who just filled the console, and both the ending and the boundary are largely
                    // defined by their absence.
                    if (CycleComplete) break;

                    // Held at full through the blink shut - the room is still coming apart while the
                    // lids fall, which is what makes the reset feel like it happens *to* the player.
                    Panels?.SetFlare(1f);
                    cameraShaker?.SetIntensity(1f);

                    // The announcer acknowledges a voluntary end on the spot, because cutting the
                    // clock short skips the countdown - otherwise ending early is silent, and the one
                    // decision the player gets to make would land with no feedback at all.
                    if (endRequested) narration?.AnnounceCycleTerminated();

                    // Control and recording both stop before the eyelids start closing. Left running,
                    // the player spent the ~1.6s blackout walking blind, and because ElapsedTime is
                    // frozen at loopDuration by then, every one of those frames recorded at the same
                    // timestamp - the ghost skipped the lot in a single jump on its final tick.
                    if (playerController != null) playerController.ControlEnabled = false;

                    RecordedTimeline timeline = playerRecorder != null ? playerRecorder.EndRecording() : null;

                    // Fired here rather than at the top of the next iteration: this is the moment the
                    // loop takes you, and it has to be heard while the lids are still falling and the
                    // room is still flaring. Held until after the blackout it becomes an explanation
                    // of something that already happened.
                    ambience?.PlayPullIn();

                    if (wakeUpSequence != null)
                        yield return wakeUpSequence.CloseEyes();

                    // Cleared only once the screen is fully black. Stopping the shake while the player
                    // can still see would put a visible full stop on it.
                    cameraShaker?.SetIntensity(0f);
                    Panels?.SetFlare(0f);

                    // And now the room goes out, with nothing to look at while it does.
                    ambience?.PlayPowerDown();

                    // A RESTART LANDS HERE, BEHIND THE SHUT EYELIDS, and that placement is the whole
                    // of why it needs no presentation of its own: everything above has already
                    // happened - the flare, the pull-in, the blink, the panels going out - so the
                    // teardown is invisible and the wake-up at the top of the next pass is the one the
                    // player has seen a hundred times. What makes it a restart rather than an
                    // iteration is only that this run does NOT become a ghost and the ones before it
                    // are destroyed.
                    if (restartRequested)
                    {
                        restartRequested = false;
                        RestartCycleState();
                    }
                    else if (timeline != null && timeline.FrameCount > 0 && ghostPrefab != null)
                    {
                        GhostReplayer ghost = Instantiate(ghostPrefab, ghostParent);
                        ghost.Init(timeline, Current != null ? Current.ghostInteractables : null);
                        ghosts.Add(ghost);
                    }
                }

                // WHAT IT COST, banked before either branch below can touch the counters.
                //
                // The reading is taken HERE rather than at `FinalRoomSequence.BreakOpen` - which is
                // the moment the ERROR actually goes up - because the two are numerically identical
                // and this one cannot go wrong: the clock stopped when the inner loop broke on
                // `CycleComplete`, nothing between here and the break moves it, and there is exactly
                // ONE path through this line where there are two through the break. `EndCycleState`
                // zeroes all three counters a few seconds later, so anything read after it is gone.
                CycleRecord finished = new CycleRecord(CycleNumber, IterationNumber, TotalElapsedTime);
                cycleRecords.Add(finished);
                // **BANKED HERE, NOT AT THE ENDING CARD** (2026-08-31, by request). The run's own
                // list is what the card reports and it is still built above; this is the persistent
                // one, and it is written per cycle so that finishing a cycle and then stopping is a
                // result rather than nothing. See `RunReport.Record` for why it keeps the better.
                // **NOT ON A SAMPLED RUN.** `RunReport` keeps the BEST attempt at each cycle and the
                // title screen reads it, so letting a test jump write into it would put a cycle the
                // tester never played into the player's permanent record - and `RunReport.Better`
                // would then defend that invented number against every real run afterwards.
                if (!DebugStart.SampleReport) RunReport.Record(finished);

                // The cycle is finished. Either the game is over, or there is another bed.
                if (!HasNextCycle)
                {
                    yield return RunEnding();
                    yield break;
                }

                yield return CrossToNextCycle();
            }
        }

        // ARRANGES THE WORLD AT A CYCLE BOUNDARY, without faking any of it.
        //
        // Every step here is something the player would otherwise have done: stand in the last room,
        // let the console come up, put the three objects in. It then STOPS - `CycleComplete` is
        // noticed by the ordinary inner loop and everything after that is the real path. A shortcut
        // that jumped straight to `CrossToNextCycle` would test the shortcut rather than the game.
        //
        // See DebugStart. Editor-only in practice: nothing sets the flag except the title screen's
        // test button.
        private IEnumerator JumpToBoundary()
        {
            FinalRoomSequence room = Current != null ? Current.finalRoom : null;
            if (room == null || room.console == null) yield break;

            Vector3 console = room.console.transform.position;

            // **THE FLOOR IS THE ROOM'S, NOT ZERO.** This stood the player at y = 0.05 and got away
            // with it for as long as there was one console: cycle 1's room4 has its floor on y = 0.
            // Room2-0's is ELEVEN METRES DOWN, so the jump spawned the tester in the air above the
            // building and they fell out of the room they were meant to be standing in. The sequence
            // object is a child of its room at local zero, so its own Y is that room's floor.
            //
            // **AND THE WAY IN IS NOT ALWAYS -Z.** Cycle 1's room4 is entered from -Z and cycle 2's
            // room2-0 from +Z, so a hard-coded `console.z - 2.6` put the tester standing inside
            // room2-0's pedestal row. `arrival` is the doorway the room is entered by, so a point
            // between it and the console is "just inside, facing in" for any room that has one.
            float floorY = room.transform.position.y + 0.05f;
            Vector3 doorway = room.arrival != null
                ? room.arrival.transform.position
                : console + Vector3.back * 4f;

            Vector3 stand = Vector3.Lerp(console, doorway, 0.42f);
            stand.y = floorY;

            Vector3 facing = console - stand;
            facing.y = 0f;
            Quaternion look = facing.sqrMagnitude > 0.001f
                ? Quaternion.LookRotation(facing.normalized, Vector3.up)
                : Quaternion.identity;

            if (playerController != null) playerController.Teleport(stand, look);

            // The console rises on arrival, and arrival is a doorway the player never came through.
            room.arrival?.ForceArrived();

            // A sunk console accepts nothing - FinalSlot gates on the room being Active, which gates
            // on the plinth being all the way up. So this waits rather than assuming.
            while (!room.console.Raised) yield return null;

            // THE THREE OBJECTS GO ON THE FLOOR, NOT INTO THE SLOTS. They were seated here at first,
            // which completed the cycle on the same frame - so the boundary fired before the room had
            // been looked at, and the one thing the shortcut exists to watch was already over.
            //
            // On the floor, the tester does what a player does: picks each one up and puts it in. That
            // exercises the whole chain - the reach, the prompt, `FinalSlot.Accept`, the completion
            // test, the hatch - instead of only what comes after it.
            //
            // ONE SHOT PER ITERATION. These live on plinths in three other rooms; letting the clock run
            // out sweeps them home and hides them again. Press the button again rather than waiting.
            RewardPlinth[] plinths = UnityEngine.Object.FindObjectsByType<RewardPlinth>();

            // Spread across however many recesses this console has - three for cycle 1, four for
            // cycle 2. Written as a count rather than a table, or the fourth object lands on the
            // third: the table this replaced was three wide and clamped.
            int declared = 0;
            foreach (FinalSlot s in room.slots) if (s != null && s.Declared) declared++;
            int placed = 0;

            foreach (FinalSlot slot in room.slots)
            {
                if (slot == null || !slot.Declared) continue;

                // `FindAny`, NOT `FindFreeForGhost`. The availability lookups all require `visible`,
                // and these three are sitting HIDDEN on sunk plinths in three other rooms - which is
                // exactly the state this jump exists to skip past. Asking "can it be taken right now"
                // gets a truthful no, and the room came up empty.
                CarryableItem item = ItemRegistry.FindAny(slot.AcceptedItemId);
                if (item == null)
                {
                    Debug.LogWarning($"[LoopManager] Test jump found no item for '{slot.AcceptedItemId}'.");
                    continue;
                }

                // ITS PLINTH HAS TO LET GO OF IT FIRST. A `RewardPlinth` that is not raised calls
                // Hide() on what it carries EVERY frame, so revealing one of these anywhere else is
                // undone before it can be seen. Cutting the reference is the whole of what is needed:
                // the plinth keeps working, it simply no longer owns this object.
                foreach (RewardPlinth plinth in plinths)
                    if (plinth != null && plinth.key == item) plinth.key = null;

                // STRAIGHT INTO THE RECESS when the shortcut is being used to reach the cycle BELOW
                // - see DebugStart.FinishOnJump. Through the same call a ghost's delivery uses, so
                // the completion test, the break and the hatch are all the real path.
                if (DebugStart.FinishOnJump)
                {
                    item.RevealAt(new Vector3(console.x, floorY + item.floorY, stand.z));
                    if (!slot.AcceptFromGhost(item))
                        Debug.LogWarning($"[LoopManager] Test jump could not seat "
                                       + $"'{slot.AcceptedItemId}' - the recess refused it.");
                    placed++;
                    continue;
                }

                float spread = 0.7f;
                float x = console.x + (placed - (declared - 1) * 0.5f) * spread;
                // Between the player and the console, on THIS ROOM'S floor - not `item.RestingY`,
                // which is `floorBaseY + floorY` and so is the height of the storey the object was
                // BUILT on. Cycle 2's balls are built one storey above room2-0, so resting height put
                // them 3.7m in the air - and `FallingItem` leaves them there, because a revealed
                // object is not a dropped one (`Released` stays false, by design).
                item.RevealAt(new Vector3(x, floorY + item.floorY, stand.z));
                placed++;
            }
        }

        // This cycle's console is full. The only way out of a cycle, and it used to be the only way
        // out of the game - reaching Room4 once ended a run, then filling its console did.
        private bool CycleComplete => Current != null && Current.Complete;

        // This cycle's wall panels. Per cycle, because the ERROR spreading from a console means *this
        // bed's cycle is over*, and a panel in a cycle the player has not reached has no business
        // failing.
        private WallPanelDisplay Panels => Current != null ? Current.wallPanels : null;

        // Shared by the ending and the boundary: the collapse lets go rather than peaking. Escaping
        // inside collapseLeadTime means the room was already coming apart, so this is visible and it
        // is the point - the thing that takes the player every sixty seconds tries, and stops.
        private IEnumerator ReleaseCollapse()
        {
            const float releaseDuration = 1.1f;
            float t = 0f;
            float startFlare = Panels != null ? Panels.Flare : 0f;
            while (t < releaseDuration)
            {
                t += Time.deltaTime;
                float k = 1f - Mathf.Clamp01(t / releaseDuration);
                Panels?.SetFlare(startFlare * k);
                cameraShaker?.SetIntensity(k * k);
                yield return null;
            }
            Panels?.SetFlare(0f);
            cameraShaker?.SetIntensity(0f);
        }

        // A CYCLE ENDED AND ANOTHER FOLLOWS. Everything the ending does up to the point where the two
        // part company: the facility breaks, but nothing is paced, nothing is taken away, and the room
        // stays the player's until they choose to leave it.
        private IEnumerator CrossToNextCycle()
        {
            CycleBreaking = true;

            // The shortcut has done its job. Cleared here rather than after its first firing, so it
            // survives a mistimed iteration - and cleared here rather than never, so the cycle on the
            // other side is played as it actually is.
            DebugStart.AtCycleBoundary = false;

            // CONTROL IS NOT TAKEN. Walking out through what you built is the whole of this beat, and
            // there is no timer on it - the way out is the only thing that can happen next, so nothing
            // has to push the player toward it.

            // Stopped and discarded. The iteration that got out does not become a ghost: this cycle's
            // ghosts are all about to be destroyed anyway, and the next cycle starts from nothing.
            playerRecorder?.EndRecording();

            yield return ReleaseCollapse();

            // And the room tone goes with it. It has been under every second of every iteration, so
            // its absence is the quietest and clearest signal that this one is not turning over.
            ambience?.FadeOutTone(3.5f);

            // The way back seals, the facility says what has happened, and every panel in this cycle
            // fails. No ten-second break: see FinalRoomSequence.BreakOpen.
            Current?.finalRoom?.BreakOpen();

            // The way on opens, and the player takes it in their own time. Both halves are the way
            // out's own business - it knows when it has been used.
            // THE NEXT CYCLE WAKES BEFORE THE HATCH DOES. It has to be there to be looked down at -
            // seeing the bed you are about to wake in is the whole of why the opening is in the floor
            // rather than in a wall.
            if (HasNextCycle) cycles[cycleIndex + 1]?.SetAwake(true);

            // AND THE GAS FOLLOWS THE PLAYER DOWN. It pours out of the room they are about to land in,
            // so the emitters have to be that cycle's - they were wired once at build time to cycle
            // 2's, which is right exactly once and gasses the wrong storey from cycle 3 on. Repointed
            // before the hatch opens, alongside the wake, because both are about the room below.
            if (HasNextCycle) binding?.PointGasAt(cycles[cycleIndex + 1]);

            CycleExit exit = Current != null && Current.finalRoom != null ? Current.finalRoom.wayOut : null;
            if (exit != null)
            {
                exit.Open();
                while (!exit.PlayerThrough) yield return null;
                exit.Seal();

                // DOWN, AND STANDING. Waiting on the landing rather than on the drop is what stops
                // the gas arriving while the player is still in the air, which would read as being
                // shot down rather than as the room deciding something.
                while (playerController != null && !playerController.IsGrounded) yield return null;
                yield return new WaitForSeconds(settleBeforeGas);
            }

            // AND THEN, WITH NO WARNING. Not announced, not telegraphed: the player is not in a bed
            // and has not asked for anything.
            //
            // CONTROL IS NOT TAKEN HERE. It goes in Collapse, below, so the first seconds of the gas
            // are spent with the player's legs still working - long enough to look up and find the
            // slots it is pouring out of. Taking control the moment it starts would make it a
            // cutscene beginning rather than something happening to them.
            // THE ROOM FIRST. This returns when the vapour has crossed the room, not when the player
            // is down - they watch it arrive with their legs still working, which is what makes it
            // something that happened rather than a cut.
            if (sleepingGas != null) yield return sleepingGas.Fill();

            ambience?.PlayPullIn();

            // AND THEN THEY BREATHE IT. The wash deepens WHILE the body falls rather than before it:
            // the two are one event, and making them take turns is what made the first version read
            // as a screen effect followed by an animation.
            float goingUnder = wakeUpSequence != null ? wakeUpSequence.collapseDuration : 2.3f;
            sleepingGas?.Overcome(goingUnder * 1.15f);

            // Down first, then the eyes. Not the loop's blink: that one is instant and involuntary,
            // and this has to read as losing rather than as being switched off.
            if (wakeUpSequence != null)
                yield return wakeUpSequence.Collapse(playerController);
            else if (playerController != null)
                playerController.ControlEnabled = false;

            cameraShaker?.SetIntensity(0f);
            Panels?.SetFlare(0f);
            ambience?.PlayPowerDown();

            // EVERYTHING BEHIND THE SHUT LIDS FROM HERE.
            EndCycleState();

            cycleIndex++;
            CycleBreaking = false;
        }

        // Asked for from the pause menu. Refused rather than queued when there is no iteration to
        // interrupt: during the ending the loop is gone, and across a boundary the cycle being
        // restarted is ambiguous - the one being left or the one being entered.
        public void RequestCycleRestart()
        {
            if (RunOver || CycleBreaking || !IterationRunning) return;
            restartRequested = true;
        }

        // THIS CYCLE FROM NOTHING: every ghost gone, every object home, every room shut, the count
        // and the clock back to zero.
        //
        // It is `EndCycleState` minus the two steps that are about LEAVING - the gas is not cleared
        // because none was fired, and the cycle is not put to sleep because it is the cycle being
        // started. `cycleRecords` is deliberately untouched: a restart is not a cycle broken, and a
        // run that restarted cycle 2 four times should report the attempt that finished it.
        private void RestartCycleState()
        {
            // GHOSTS FIRST, in the same slot and for the same reason `EndCycleState` puts them there:
            // OnDestroy releases what a ghost was carrying and that ends in ReturnToOrigin, so a
            // ghost destroyed AFTER the sweep would put an object back on screen behind it.
            foreach (var ghost in ghosts)
            {
                if (ghost == null) continue;
                ghost.ReleaseCarried();
                Destroy(ghost.gameObject);
            }
            ghosts.Clear();

            playerHand?.ReturnAll();
            ItemRegistry.ReturnAllToOrigin();

            Current?.CloseDoors();
            Current?.ResetRooms();
            // A cycle can be restarted after its console has been filled - the break has not been
            // reached yet, but `Completed` latches - so this has to be forgotten with the rest.
            Current?.finalRoom?.ForgetCompletion();

            IterationNumber = 0;
            totalElapsedTime = 0f;
            ElapsedTime = 0f;
            // Given back with everything else: a restart is a fresh attempt at this cycle, and the
            // early-end allowance is part of what an attempt has.
            endCycleControl?.ResetUseCount();
        }

        // The boundary itself. Nothing here is visible, and the order is the whole of it.
        private void EndCycleState()
        {
            // GHOSTS GO FIRST, in the same slot the per-iteration ReleaseCarried occupies and for the
            // same reason. OnDestroy releases what a ghost was carrying, and releasing runs
            // ReturnToOrigin, which ends in SetVisible(true) - so a ghost destroyed AFTER the room
            // resets would put a key back on screen outside the balloon that is supposed to be hiding
            // it. Destroyed before anything else, that cannot happen.
            //
            // Not inside the foreach: Destroy defers OnDestroy to the end of the frame, so the release
            // is asked for explicitly here rather than waited on.
            //
            // AND WHERE THEY WERE STANDING IS KEPT, first of all - before the release, which moves
            // nobody, and certainly before the destroy. `GhostArchive` is what lets the ending fly
            // back through cycles 1 and 2 and find them still in them; see its header for why a pose
            // is kept rather than the ghost.
            GhostArchive.Capture(ghosts, CycleNumber);

            foreach (var ghost in ghosts)
            {
                if (ghost == null) continue;
                ghost.ReleaseCarried();
                Destroy(ghost.gameObject);
            }
            ghosts.Clear();

            // The timelines went with them. They live nowhere else - there is no list of recordings
            // anywhere in the project, only the ghost objects holding their own - so clearing the
            // list above is the whole of forgetting this cycle.

            playerHand?.ReturnAll();
            ItemRegistry.ReturnAllToOrigin();

            // The cycle that just ended goes back to how it was found, so nothing is left standing
            // open behind the player: the console down, the recesses empty, and completion itself
            // forgotten - within a cycle that flag is final, across a boundary it cannot be.
            Current?.CloseDoors();
            Current?.ResetRooms();
            Current?.finalRoom?.ForgetCompletion();

            // The wash goes, behind the shut lids. Left up, the next cycle would open on a white
            // sheet - and the eyelids being closed is exactly why this is invisible.
            sleepingGas?.Clear();

            // AND THE CYCLE JUST LEFT GOES TO SLEEP, behind the shut eyelids and after everything
            // above has finished with it - the ghost teardown, the item sweep and the room resets all
            // need it awake. Its carryables unregister on the way out, which is what keeps the next
            // cycle's sweep to the next cycle's objects.
            Current?.SetAwake(false);

            // A new bed is a new count of everything.
            IterationNumber = 0;
            totalElapsedTime = 0f;
            ElapsedTime = 0f;
            endCycleControl?.ResetUseCount();
        }

        // **CYCLE 1, ITERATION 1, FOR FIVE SECONDS - AND IT IS THE REAL THING.**
        //
        // The first attempt at this lived in `EndingDeparture` and stood the player on the bed with
        // the wake-up played over them. It looked right and was hollow: `AcceptsInput` was false so
        // nothing could be picked up, no announcement fired, and `WakeUpSequence.wallPanels` still
        // pointed at cycle 3, so room1-1 woke to a wall of ERROR that nothing was going to clear.
        //
        // Every one of those is a thing the ordinary iteration opening does, which is why this is
        // here and not there: `LoopManager` owns the iteration, the clock and the reset ordering
        // (CLAUDE.md 2). What follows is that opening, in its order, with the two differences that
        // make it an ending - the ghosts are destroyed rather than replayed, and the clock is never
        // started, because the player is not being given another minute.
        private IEnumerator ReturnToTheFirstRoom()
        {
            Cycle first = cycles != null && cycles.Length > 0 ? cycles[0] : null;
            if (first == null) yield break;

            // The world first: walls back, ERROR cleared, every other cycle asleep, the outside off.
            FacilityExterior outside = Current != null && Current.departure != null
                ? Current.departure.exterior : null;
            outside?.ReturnToTheFirstRoom(first);

            // **THE GHOSTS ARE DESTROYED, NOT RESET.** Every other iteration keeps them; this one
            // cannot. They are recordings of a run that is over, and a room1-1 with thirty past
            // selves walking through it would answer the question the ending is asking.
            foreach (GhostReplayer ghost in ghosts)
                if (ghost != null) { ghost.ReleaseCarried(); Destroy(ghost.gameObject); }
            ghosts.Clear();

            // `Current` is derived from `cycleIndex`, so this is how you become cycle 1 again.
            cycleIndex = 0;
            CycleNumber = 1;
            IterationNumber = 1;

            // The same two handovers the cycle loop does, and for the same reasons - see there.
            if (playerRecorder != null) playerRecorder.interactables = first.ghostInteractables;
            if (wakeUpSequence != null) wakeUpSequence.wallPanels = first.wallPanels;

            first.ResetRooms();
            ItemRegistry.ReturnAllToOrigin();

            Transform bed = first.bedSpawnPoint;
            if (playerController != null && bed != null)
                playerController.Teleport(bed.position, bed.rotation);
            first.CloseDoors();

            // Set before the lids open, so the first thing seen is the room with the number already
            // on it rather than a number arriving to explain the room.
            iterationLabel?.Show(1, 1);

            if (wakeUpSequence != null) yield return wakeUpSequence.WakeUp(playerController);
            if (playerController != null) playerController.ControlEnabled = true;

            // "Iteration one, sixty seconds remaining." The line the run opened with.
            narration?.AnnounceIteration(1);

            // **AND THIS IS WHAT MAKES IT REAL.** `AcceptsInput` is an AND over this, the pause and
            // `RunOver`; with it true every fixture and every carryable in room1-1 answers E again.
            // The clock is deliberately not started - `ElapsedTime` stays where it is and no
            // iteration coroutine is running, so the sixty seconds the PA just promised never count
            // down. That is the joke and it is meant to be noticed.
            IterationRunning = true;
            yield return new WaitForSeconds(afterTheFall);
            IterationRunning = false;
        }

        // The last cycle. Note what this does NOT do, which is most of its design - see
        // EndingSequence for the reasoning behind each omission.
        private IEnumerator RunEnding()
        {
            // **`RunOver` IS SET AT THE END OF THIS NOW, NOT THE TOP** (2026-09-03). The ending has a
            // playable beat in it - five seconds in room1-1 after the fall - and `AcceptsInput` reads
            // this, so setting it here made that beat a diorama: the player stood in the right room
            // and could not pick anything up.
            //
            // Moving it is safe rather than clever. `IterationRunning` is false from the moment the
            // inner loop breaks on `CycleComplete`, and `AcceptsInput` is an AND - so nothing is
            // interactive during the break, the walk to the car or the ride whether this is set or
            // not. The only thing it was doing in those minutes was hiding the fact that the last
            // beat needed it false.

            // CONTROL IS NOT TAKEN HERE. The player has just put the last object in and the room is
            // about to come apart around them; standing in it and watching that happen is the whole
            // of the ending, and it is theirs to stand in. It is taken at the scrim, below.

            // Stopped and discarded. The run that got out does not become a ghost - there is no
            // next iteration for it to haunt, and building one would be the loop's habit outliving
            // the loop.
            playerRecorder?.EndRecording();

            yield return ReleaseCollapse();

            // And the room tone goes with it. It has been under every second of every iteration,
            // so its absence is the quietest and clearest signal that this one is not turning over.
            ambience?.FadeOutTone(3.5f);

            // The break: the way back seals, the facility says what has happened, and every panel in
            // the building fails. Started here rather than by the room itself, because the collapse
            // above has to have let go first - the loop trying to take the player and stopping is
            // what the last object landing means.
            //
            // The PACED version, unlike a cycle boundary's: the ten seconds are what the scrim comes
            // up over, and only the last cycle has a scrim.
            if (Current != null && Current.finalRoom != null)
            {
                yield return Current.finalRoom.RunBreak();
            }
            else
            {
                // No final room wired - the pre-Room4 ending, kept so this does not depend on a
                // scene object existing. Chimed, because it is the most important thing the
                // facility ever says.
                narration?.AnnounceCycleBroken();
            }

            // **AND THEN THE PLAYER LEAVES THE BUILDING.** Everything between the break and the card
            // - the evaluation on room3-2N's wall, the wall opening, the cable car, the ride up
            // through cycles 3, 2 and 1 - is `EndingDeparture`, which owns the ORDER of it and
            // nothing else. Null on any cycle that is not the last, and null-safe here so that a
            // build without the departure wired still ends the way it always did: break, scrim, card.
            //
            // IT RUNS BEFORE CONTROL IS TAKEN, and it has to. The player walks down a ladder, reads
            // a board and steps into a vehicle in the middle of it; a sequence that ran after the
            // line below would be a cutscene of somebody else doing that.
            EndingDeparture departure = Current != null ? Current.departure : null;
            if (departure != null) yield return departure.Play(cycles);

            // Held all the way through the break and taken here, at the scrim. Ten seconds pinned in
            // place watching a room fail would be the game freezing rather than the room failing.
            if (playerController != null) playerController.ControlEnabled = false;

            // A RUN HAS BEEN FINISHED. Banked after the departure rather than before it, so that the
            // subject number the board printed is the one for THIS run - see `RunReport.SubjectNumber`,
            // which counts the run being evaluated as the one in progress.
            if (!DebugStart.SampleReport) RunReport.RecordClear();

            // **NOTHING IS WRITTEN HERE ANY MORE.** Every cycle banked itself as it finished (see
            // the `RunReport.Record` call in the cycle loop), so by the time the ending card is up
            // the stored record is already complete - and writing the run again here would overwrite
            // per-cycle bests with whatever this particular run happened to do.

            // **AND THE LOOP CLOSES.** The fall put the player back in the first room, and this is
            // that room being a real iteration rather than a picture of one - see the method.
            yield return ReturnToTheFirstRoom();

            RunOver = true;
            if (playerController != null) playerController.ControlEnabled = false;

            if (endingSequence != null)
                yield return endingSequence.Play(cycleRecords);
        }
    }
}
