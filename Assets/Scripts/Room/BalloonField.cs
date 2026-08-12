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

        public int seed = 20260810;
        public float roomCenterZ = 10.85f;
        // Kept inside the walls by a balloon's own radius plus a margin, so none spawns clipped
        // into the panelling.
        public float halfWidth = 3.8f;
        public float halfDepth = 4.5f;
        public float lowestSpawnY = 3.1f;
        public float highestSpawnY = 5.1f;

        // Where the key ends up once its balloon bursts is CarryableItem.floorY now - the same
        // number a ghost dropping something lands at, and one owner for it rather than two
        // constants that have to agree.

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

            if (balloons == null) return;

            for (int i = 0; i < balloons.Length; i++)
            {
                if (balloons[i] == null) continue;
                balloons[i].Respawn(spawnPoints[i], spawnRotations[i]);
                // Every key goes back inside its balloon, and each balloon knows which key that is - so
                // this needs no list of its own and cannot fall out of step with how many there are.
                //
                // AFTER ItemRegistry.ReturnAllToOrigin, which the loop calls first (see the reset order
                // in CLAUDE.md): that puts each key back at its origin on the floor, and this is what
                // takes it out of play again. Hiding before the sweep would leave a hidden key sitting
                // wherever it was dropped.
                if (balloons[i].heldKey != null) balloons[i].heldKey.Hide();
            }
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

            // Whatever key this balloon held drops to the floor under the burst. Revealed by whoever
            // popped it, ghost or player - a ghost finding one for you is the entire point of spending
            // an iteration searching, and with three keys and three doors it is the point three times
            // over: a past self can be delivering one while the player is still looking for the next.
            //
            // The balloon names its own key, so this needs no idea how many there are.
            if (balloon.heldKey != null)
                balloon.heldKey.RevealAt(new Vector3(at.x, balloon.heldKey.floorY, at.z));
        }
    }
}
