using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace IterationRoom
{
    // Drives the 60-second loop: shows the "Iteration N" label, resets the
    // player to bed on timeout, and turns the just-finished recording into a
    // new accumulating ghost.
    public class LoopManager : MonoBehaviour
    {
        public static LoopManager Instance { get; private set; }

        public float loopDuration = 60f;
        public Transform bedSpawnPoint;
        public PlayerRecorder playerRecorder;
        public FirstPersonController playerController;
        // Everything a ghost can operate. Order is the wire format: an entry's index here is its
        // bit in RecordedFrame.signals, so this must be the same array PlayerRecorder samples.
        // Reordering it invalidates every timeline recorded so far.
        public GhostInteractable[] ghostInteractables;
        // Every door in the run. World state the loop has to rewind, same as the first one always
        // was - Room2's key door joins Room1's button door here.
        public Door[] doors;
        // The nightstand drawer, and whatever the player is carrying. Both are world state too:
        // leave the tool in the player's hand across a reset and the trip to the drawer stops
        // costing anything, which is most of what Room2's puzzle is made of.
        public Drawer[] drawers;
        public PlayerHand playerHand;
        public BalloonField balloonField;
        public ChessBoard chessBoard;
        public CubeRoom cubeRoom;
        public GhostReplayer ghostPrefab;
        public Transform ghostParent;
        public IterationLabel iterationLabel;
        public WakeUpSequence wakeUpSequence;
        public NarrationDirector narration;
        public RoomAmbience ambience;
        public WallPanelDisplay wallPanels;
        public CameraShaker cameraShaker;

        // Room4, which the loop now runs through like every other room. A run that reaches it with
        // two of the three objects gets pulled back to the bed and does it again.
        //
        // The doorway trigger is NOT held here any more. It used to end the run, which was the loop's
        // business; it arms the last room's console now, which is that room's - so it is wired to
        // FinalRoomSequence and this class never asks about it.
        public FinalRoomSequence finalRoom;
        public EndingSequence endingSequence;

        // The sensitivity step, run once before the first iteration. See SensitivityCalibration.
        public SensitivityCalibration calibration;

        // How long before the reset the room starts coming apart: the wall displays blow out and
        // the view begins to judder, both building to the moment the cycle takes you.
        public float collapseLeadTime = 10f;

        // "10 seconds remaining." is cued earlier than ten seconds on purpose. The line runs about
        // two seconds, and from nine down the digit countdown replaces whatever the announcer is
        // saying every whole second - cued at exactly T-10 it gets clipped after one word.
        public int tenSecondCueAt = 12;

        public float ElapsedTime { get; private set; }
        public int IterationNumber { get; private set; }

        // The clock's own running total, across every iteration this run has spent - what
        // EndingSequence reports alongside the iteration count. `totalElapsedTime` accumulates each
        // iteration's ElapsedTime the moment that iteration ends (see the top of RunLoop's while);
        // the CURRENT iteration's own ElapsedTime is still live and not yet folded in, which is
        // exactly right at the one moment this is read - RunEnding, while the escaping iteration is
        // still the current one and has not gone through that accumulation step itself.
        public float TotalElapsedTime => totalElapsedTime + ElapsedTime;
        private float totalElapsedTime;

        // True only while the clock is actually running - not during the wake-up, not during the
        // eyelid close. The end-cycle control reads this so it can't be charged up out of turn.
        public bool IterationRunning { get; private set; }

        // Set by PauseMenu. The loop itself needs no knowledge of it - Time.timeScale freezes the
        // whole coroutine - but the scripts that read raw Input in Update do, because Update keeps
        // running at a zero time scale and E, N and the mouse would all still land.
        public bool IsPaused { get; private set; }

        // "The game is accepting player input right now." Every Update that reads a key should
        // gate on this rather than on IterationRunning alone; the two only differ while paused.
        public bool AcceptsInput => IterationRunning && !IsPaused && !RunOver;

        // The player got out and the ending is playing. Read by PauseMenu, which must not let
        // Escape open an overlay over the last thing in the game - Resume would then hand back a
        // frozen room with no loop left running to unfreeze it.
        public bool RunOver { get; private set; }

        public void SetPaused(bool paused) => IsPaused = paused;

        private bool endRequested;

        // The player choosing to cut this cycle short. 60 seconds is a ceiling, not a quota: once
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
        public void EndCycleEarly()
        {
            if (!IterationRunning) return;
            endRequested = true;
        }

        private readonly List<GhostReplayer> ghosts = new List<GhostReplayer>();

        private void Awake()
        {
            Instance = this;
        }

        private void Start()
        {
            StartCoroutine(RunLoop());
        }

        // The whole loop lives in one coroutine rather than Update, because an iteration is not
        // just a timer any more - it opens and closes with the wake-up sequence, and the clock and
        // the ghosts have to stay frozen while that plays out. Recording deliberately starts only
        // once the player has control, so every ghost's timeline covers the same window.
        private IEnumerator RunLoop()
        {
            // Before anything: look around the room and set the mouse sensitivity. Outside the
            // while, so it happens exactly once, and before IterationNumber has been incremented -
            // the clock is stopped, IterationRunning is false, and every interactable is therefore
            // already inert. The player has full control meanwhile, which is the point; iteration 1
            // teleports them back to the bed regardless.
            if (calibration != null)
            {
                calibration.Begin();
                while (!calibration.Confirmed) yield return null;
                calibration.End();
            }

            while (true)
            {
                IterationNumber++;
                // Folded in BEFORE the reset, so the iteration just finished counts toward the
                // total exactly once. On the very first pass ElapsedTime is still its default zero,
                // so this is a harmless no-op rather than a special case to guard.
                totalElapsedTime += ElapsedTime;
                ElapsedTime = 0f;

                if (playerController != null && bedSpawnPoint != null)
                    playerController.Teleport(bedSpawnPoint.position, bedSpawnPoint.rotation);

                // The doors are world state, so the loop has to rewind them too - and only after
                // the teleport, so a player standing in a doorway is already back at the bed rather
                // than inside the slab when it snaps shut.
                if (doors != null)
                    foreach (Door d in doors) d?.Close();

                // Ghosts let go first, then the player, then the field hides what it owns. All three
                // orderings matter and for the same reason: an item has to be back at its parked
                // position BEFORE the thing that hides it runs, or it ends up visible on the floor
                // of a room whose balloons have not been popped yet.
                //
                // The ghost pass is separate from ResetPlayback below rather than folded into it,
                // because ResetPlayback runs after ResetField - by then the key is already hidden,
                // and handing it back at that point would un-hide it.
                foreach (var ghost in ghosts) ghost.ReleaseCarried();

                playerHand?.ReturnAll();

                // The catch-all, and it is not redundant with the two lines above. Those clear the
                // holders' own bookkeeping; this puts every object back. A key a GHOST left in the
                // lock belongs to neither list - it is not in the player's `taken` and the ghost let
                // go of it the moment the socket accepted it - so before this it stayed in the
                // keyhole with IsCarried true and no ghost could ever pick it up again.
                ItemRegistry.ReturnAllToOrigin();

                if (drawers != null)
                    foreach (Drawer dr in drawers) dr?.Close();

                balloonField?.ResetField();

                // Room4 rewinds like anywhere else now: the console goes down, the recesses forget
                // what was in them, and reaching the last doorway has to be done again. Before the
                // clock ran through that room there was nothing here to reset, because getting there
                // ended the run.
                finalRoom?.ResetRoom();

                // AFTER the sweep, like the balloon field and for the same reason. The sweep is what
                // puts the pieces back - scattered ones on the floor, the rest on their squares - and
                // this is what forgets who was home and puts the untouched ones back out of play. Run
                // the other way round, every piece the sweep returned would still be marked seated and
                // the room would open on a puzzle it thought was already solved.
                chessBoard?.ResetBoard();
                // Same position in the order and for the same reason: the sweep puts the cubes back
                // on the floor, and this forgets who was home and takes the reward's request back.
                cubeRoom?.ResetRoom();

                // Hidden for the whole wake-up: resetting parks them all on the bed spawn, which is
                // exactly where the player is about to open their eyes.
                foreach (var ghost in ghosts)
                {
                    ghost.ResetPlayback();
                    ghost.SetVisible(false);
                }

                // The announcer confirms the reset under the closed eyelids. Iteration 1 opens the
                // run rather than resetting it, so it gets no line. (The pull and the shutdown that
                // precede this are fired at the *end* of the previous iteration, below, which is
                // why they need no such guard - there is always an iteration before them.)
                if (IterationNumber > 1) narration?.AnnounceNewCycle();

                iterationLabel?.ShowIteration(IterationNumber);

                if (wakeUpSequence != null)
                    yield return wakeUpSequence.WakeUp(playerController);

                // Announced as the clock actually starts, which is also the moment control returns.
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

                while (ElapsedTime < loopDuration && !endRequested && !RunComplete)
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
                    wallPanels?.SetFlare(collapse * collapse);
                    cameraShaker?.SetIntensity(collapse);

                    yield return null;
                }

                IterationRunning = false;

                // The only way out of the while(true), and it has to be taken HERE - before any of
                // what follows. Everything below this point is the cycle closing and re-opening:
                // the collapse held at full, the pull-in, the blink, the panels going out, the
                // recording becoming another ghost. None of it should happen to a player who just
                // got out, and the ending is largely defined by their absence.
                if (RunComplete)
                {
                    yield return RunEnding();
                    yield break;
                }

                // Held at full through the blink shut - the room is still coming apart while the
                // lids fall, which is what makes the reset feel like it happens *to* the player.
                wallPanels?.SetFlare(1f);
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
                // room is still flaring. Held until after the blackout it becomes an explanation of
                // something that already happened.
                ambience?.PlayPullIn();

                if (wakeUpSequence != null)
                    yield return wakeUpSequence.CloseEyes();

                // Cleared only once the screen is fully black. Stopping the shake while the player
                // can still see would put a visible full stop on it.
                cameraShaker?.SetIntensity(0f);
                wallPanels?.SetFlare(0f);

                // And now the room goes out, with nothing to look at while it does.
                ambience?.PlayPowerDown();

                if (timeline != null && timeline.FrameCount > 0 && ghostPrefab != null)
                {
                    GhostReplayer ghost = Instantiate(ghostPrefab, ghostParent);
                    ghost.Init(timeline, ghostInteractables);
                    ghosts.Add(ghost);
                }
            }
        }

        // THE ONLY EXIT CONDITION IN THE GAME, and it used to be a doorway. Reaching Room4 ended a
        // run; now it starts an errand there, and what ends the run is the console being filled.
        private bool RunComplete => finalRoom != null && finalRoom.Completed;

        // The run is over. Note what this does NOT do, which is most of its design - see
        // EndingSequence for the reasoning behind each omission.
        private IEnumerator RunEnding()
        {
            RunOver = true;

            // CONTROL IS NOT TAKEN HERE. The player has just put the last object in and the room is
            // about to come apart around them; standing in it and watching that happen is the whole
            // of the ending, and it is theirs to stand in. It is taken at the scrim, below.

            // Stopped and discarded. The run that got out does not become a ghost - there is no
            // next iteration for it to haunt, and building one would be the loop's habit outliving
            // the loop.
            playerRecorder?.EndRecording();

            // The collapse lets go rather than peaking. Escaping inside collapseLeadTime means the
            // room was already coming apart, so this is visible and it is the point: the thing that
            // takes the player every sixty seconds tries, and stops.
            const float releaseDuration = 1.1f;
            float t = 0f;
            float startFlare = wallPanels != null ? wallPanels.Flare : 0f;
            while (t < releaseDuration)
            {
                t += Time.deltaTime;
                float k = 1f - Mathf.Clamp01(t / releaseDuration);
                wallPanels?.SetFlare(startFlare * k);
                cameraShaker?.SetIntensity(k * k);
                yield return null;
            }
            wallPanels?.SetFlare(0f);
            cameraShaker?.SetIntensity(0f);

            // And the room tone goes with it. It has been under every second of every iteration,
            // so its absence is the quietest and clearest signal that this one is not turning over.
            ambience?.FadeOutTone(3.5f);

            // The break: the way back seals, the facility says what has happened, and every panel in
            // the building fails. Started here rather than by the room itself, because the collapse
            // above has to have let go first - the loop trying to take the player and stopping is
            // what the last object landing means.
            if (finalRoom != null)
            {
                yield return finalRoom.RunBreak();
            }
            else
            {
                // No final room wired - the pre-Room4 ending, kept so this does not depend on a
                // scene object existing. Chimed, because it is the most important thing the
                // facility ever says.
                narration?.AnnounceCycleBroken();
            }

            // Held all the way through the break and taken here, at the scrim. Ten seconds pinned in
            // place watching a room fail would be the game freezing rather than the room failing.
            if (playerController != null) playerController.ControlEnabled = false;

            if (endingSequence != null)
                yield return endingSequence.Play(IterationNumber, TotalElapsedTime);
        }
    }
}
