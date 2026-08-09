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
        public FloorButton floorButton;
        public GhostReplayer ghostPrefab;
        public Transform ghostParent;
        public IterationLabel iterationLabel;
        public WakeUpSequence wakeUpSequence;

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

                foreach (var ghost in ghosts)
                    ghost.ResetPlayback();

                iterationLabel?.ShowIteration(IterationNumber);

                if (wakeUpSequence != null)
                    yield return wakeUpSequence.WakeUp(playerController);

                playerRecorder?.BeginRecording();

                while (ElapsedTime < loopDuration)
                {
                    ElapsedTime += Time.deltaTime;

                    foreach (var ghost in ghosts)
                        ghost.Tick(ElapsedTime);

                    yield return null;
                }

                if (wakeUpSequence != null)
                    yield return wakeUpSequence.CloseEyes();

                List<RecordedFrame> timeline = playerRecorder != null ? playerRecorder.EndRecording() : null;

                if (timeline != null && timeline.Count > 0 && ghostPrefab != null)
                {
                    GhostReplayer ghost = Instantiate(ghostPrefab, ghostParent);
                    ghost.Init(timeline, floorButton);
                    ghosts.Add(ghost);
                }
            }
        }
    }
}
