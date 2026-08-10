using UnityEngine;

namespace IterationRoom
{
    // Room2's puzzle: a roomful of pink balloons, one of which has the key in it.
    //
    // The balloons are pooled and reused rather than instantiated per iteration, because their ids
    // have to survive the reset - a ghost's recording refers to balloon 17, and balloon 17 must
    // still mean the same thing sixty seconds later. Spawn points come from a fixed seed for the
    // same reason: the field the player faces is the same field every time, so what they learn
    // about it carries across iterations even though physics scatters the balloons differently.
    public class BalloonField : MonoBehaviour
    {
        public static BalloonField Instance { get; private set; }

        public Balloon[] balloons;
        public CarryableItem key;

        public int seed = 20260810;
        public float roomCenterZ = 10.85f;
        // Kept inside the walls by a balloon's own radius plus a margin, so none spawns clipped
        // into the panelling.
        public float halfWidth = 3.8f;
        public float halfDepth = 4.5f;
        public float lowestSpawnY = 3.1f;
        public float highestSpawnY = 5.1f;

        // Where the key ends up once its balloon bursts: on the floor under the burst, rather than
        // hanging in the air where the balloon was.
        public float keyFloorY = 0.06f;

        private Vector3[] spawnPoints;
        private Quaternion[] spawnRotations;
        private bool dropped;
        private Transform player;

        private void Awake()
        {
            Instance = this;
            ComputeSpawnPoints();
        }

        // Computed once, from a fixed seed, and never recomputed. Reseeding per iteration would
        // make the room a different room each time and there would be nothing to learn.
        //
        // Public so SceneBuilder can park the pool at build time. Left to Awake alone, the saved
        // scene stores all seventy balloons stacked on the world origin - which is in Room1, beside
        // the bed - and anyone opening the scene sees a pink heap that only play mode clears.
        public void ComputeSpawnPoints()
        {
            int count = balloons != null ? balloons.Length : 0;
            spawnPoints = new Vector3[count];
            spawnRotations = new Quaternion[count];

            System.Random rng = new System.Random(seed);
            for (int i = 0; i < count; i++)
            {
                spawnPoints[i] = new Vector3(
                    Mathf.Lerp(-halfWidth, halfWidth, (float)rng.NextDouble()),
                    Mathf.Lerp(lowestSpawnY, highestSpawnY, (float)rng.NextDouble()),
                    roomCenterZ + Mathf.Lerp(-halfDepth, halfDepth, (float)rng.NextDouble()));
                spawnRotations[i] = Quaternion.Euler(
                    (float)rng.NextDouble() * 360f,
                    (float)rng.NextDouble() * 360f,
                    (float)rng.NextDouble() * 360f);
            }
        }

        // Called by the loop at the top of every iteration, behind the closed eyelids.
        public void ResetField()
        {
            dropped = false;

            if (balloons != null)
                for (int i = 0; i < balloons.Length; i++)
                    if (balloons[i] != null) balloons[i].Respawn(spawnPoints[i], spawnRotations[i]);

            if (key != null) key.Hide();
        }

        private void Update()
        {
            if (dropped) return;

            if (player == null)
            {
                GameObject go = GameObject.FindGameObjectWithTag("Player");
                if (go != null) player = go.transform;
            }

            if (player != null) TriggerIfInside(player.position);
        }

        // The drop is triggered by somebody walking in - the player, or a ghost replaying the walk
        // in. Ghosts count deliberately: they are deterministic, so once one of them has been
        // through that door the balloons come down at the same moment every iteration, which is
        // what keeps every later recording aligned with the field it was made against.
        public void TriggerIfInside(Vector3 position)
        {
            if (dropped) return;
            if (Mathf.Abs(position.z - roomCenterZ) > halfDepth + 0.6f) return;

            dropped = true;
            if (balloons == null) return;
            foreach (Balloon b in balloons)
                if (b != null) b.Release();
        }

        public void PopById(int id)
        {
            if (balloons == null || id < 0 || id >= balloons.Length) return;
            Pop(balloons[id]);
        }

        public void Pop(Balloon balloon)
        {
            if (balloon == null || balloon.IsPopped) return;

            Vector3 at = balloon.transform.position;
            balloon.Pop();

            // The key drops to the floor under the burst. Revealed by whoever popped it, ghost or
            // player - a ghost finding it for you is the entire point of spending an iteration
            // searching.
            if (balloon.holdsKey && key != null) key.RevealAt(new Vector3(at.x, keyFloorY, at.z));
        }
    }
}
