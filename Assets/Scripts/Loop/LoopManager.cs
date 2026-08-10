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

        // "10 seconds remaining." is cued earlier than ten seconds on purpose. The line runs about
        // two seconds, and from nine down the digit countdown replaces whatever the announcer is
        // saying every whole second - cued at exactly T-10 it gets clipped after one word.
        public int tenSecondCueAt = 12;

        public float ElapsedTime { get; private set; }
        public int IterationNumber { get; private set; }

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

                foreach (var ghost in ghosts)
                    ghost.ResetPlayback();

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

                playerRecorder?.BeginRecording();

                int lastCueSecond = int.MaxValue;

                while (ElapsedTime < loopDuration)
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

                    yield return null;
                }

                if (wakeUpSequence != null)
                    yield return wakeUpSequence.CloseEyes();

                List<RecordedFrame> timeline = playerRecorder != null ? playerRecorder.EndRecording() : null;

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
