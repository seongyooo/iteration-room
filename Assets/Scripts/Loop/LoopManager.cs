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
        public Door door;
        public GhostReplayer ghostPrefab;
        public Transform ghostParent;
        public IterationLabel iterationLabel;
        public WakeUpSequence wakeUpSequence;
        public NarrationDirector narration;
        public RoomAmbience ambience;
        public WallPanelDisplay wallPanels;
        public CameraShaker cameraShaker;

        // How long before the reset the room starts coming apart: the wall displays blow out and
        // the view begins to judder, both building to the moment the cycle takes you.
        public float collapseLeadTime = 10f;

        // "10 seconds remaining." is cued earlier than ten seconds on purpose. The line runs about
        // two seconds, and from nine down the digit countdown replaces whatever the announcer is
        // saying every whole second - cued at exactly T-10 it gets clipped after one word.
        public int tenSecondCueAt = 12;

        public float ElapsedTime { get; private set; }
        public int IterationNumber { get; private set; }

        // True only while the clock is actually running - not during the wake-up, not during the
        // eyelid close. The end-cycle control reads this so it can't be charged up out of turn.
        public bool IterationRunning { get; private set; }

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
            while (true)
            {
                IterationNumber++;
                ElapsedTime = 0f;

                if (playerController != null && bedSpawnPoint != null)
                    playerController.Teleport(bedSpawnPoint.position, bedSpawnPoint.rotation);

                // The door is world state, so the loop has to rewind it too - and only after the
                // teleport, so a player standing in the doorway is already back at the bed rather
                // than inside the slab when it snaps shut.
                door?.Close();

                // Hidden for the whole wake-up: resetting parks them all on the bed spawn, which is
                // exactly where the player is about to open their eyes.
                foreach (var ghost in ghosts)
                {
                    ghost.ResetPlayback();
                    ghost.SetVisible(false);
                }

                // The machines spin the room back up, then the announcer confirms it - both under
                // the closed eyelids. Iteration 1 opens the run rather than resetting it, so it
                // gets neither.
                if (IterationNumber > 1)
                {
                    ambience?.PlayResetSting();
                    narration?.AnnounceNewCycle();
                }

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

                while (ElapsedTime < loopDuration && !endRequested)
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

                List<RecordedFrame> timeline = playerRecorder != null ? playerRecorder.EndRecording() : null;

                if (wakeUpSequence != null)
                    yield return wakeUpSequence.CloseEyes();

                // Cleared only once the screen is fully black. Stopping the shake while the player
                // can still see would put a visible full stop on it.
                cameraShaker?.SetIntensity(0f);
                wallPanels?.SetFlare(0f);

                if (timeline != null && timeline.Count > 0 && ghostPrefab != null)
                {
                    GhostReplayer ghost = Instantiate(ghostPrefab, ghostParent);
                    ghost.Init(timeline, ghostInteractables);
                    ghosts.Add(ghost);
                }
            }
        }
    }
}
