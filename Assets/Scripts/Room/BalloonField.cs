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

        private void Awake()
        {
            Instance = this;
            ComputeSpawnPoints();
        }

        // CLEARED ON THE WAY OUT, the pattern ChessBoard already uses and the one this static was
        // missing. Assigned unguarded, a second field silently steals the first - and a cycle root
        // going to sleep must not leave the pointer aimed at a field nobody can reach, because
        // `GhostReplayer.Tick` asks this static every frame for every ghost.
        private void OnDisable() { if (Instance == this) Instance = null; }

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

            Collider playerCollider = PlayerLookup.Collider;
            if (playerCollider != null) TriggerIfInside(playerCollider.transform.position);
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

        // HOW LOUD A PAST SELF'S POP IS, and how often one is allowed to be heard at all.
        //
        // Every pop a ghost ever recorded fires again every iteration - that is the design, and it is
        // what makes the field shrink without the player doing it twice. What nobody costed is what it
        // SOUNDS like by iteration ten: three pin-holding ghosts with a dozen pops each is a burst
        // every second or two, for the whole minute, while the player is somewhere else entirely
        // doing something unrelated. Play reported it as "balloons keep popping and I am not popping
        // anything", which is exactly right and exactly what the code was told to do.
        //
        // The BURST is untouched - the balloon still goes, the key still drops, nothing about the
        // puzzle changes. Only the announcement is turned down: a replayed pop is background, and no
        // more than one is heard per interval, so a flurry reads as a flurry instead of a machine gun.
        // Set to 0 to make past selves' pops silent entirely.
        public float ghostPopVolume = 0.3f;
        public float ghostPopInterval = 0.45f;

        private float nextGhostPopSound;

        // A GHOST'S pop, by identity. The living player's goes through Pop below at full volume.
        public void PopById(int id)
        {
            if (balloons == null || id < 0 || id >= balloons.Length) return;

            float volume = 0f;
            if (ghostPopVolume > 0f && Time.time >= nextGhostPopSound)
            {
                volume = ghostPopVolume;
                nextGhostPopSound = Time.time + ghostPopInterval;
            }

            Pop(balloons[id], volume);
        }

        public void Pop(Balloon balloon, float volumeScale = 1f)
        {
            if (balloon == null || balloon.IsPopped) return;

            Vector3 at = balloon.transform.position;
            balloon.Pop(volumeScale);

            // Whatever key this balloon held drops to the floor under the burst. Revealed by whoever
            // popped it, ghost or player - a ghost finding one for you is the entire point of spending
            // an iteration searching, and with three keys and three doors it is the point three times
            // over: a past self can be delivering one while the player is still looking for the next.
            //
            // The balloon names its own key, so this needs no idea how many there are.
            if (balloon.heldKey != null)
            {
                balloon.heldKey.RevealAt(new Vector3(at.x, balloon.heldKey.RestingY, at.z));

                // LYING FLAT, not standing on its blade. RevealAt only ever moves an item - rotation
                // is whatever it already had, which for a key is KeyRestRotation, the bow-up pose
                // built for somewhere findable rather than for a floor. A key stood on end where it
                // fell reads as placed on purpose; one lying on its side reads as dropped, which is
                // what a burst balloon actually did to it.
                //
                // Rolled 90 off the LOCK's own axis (identity, shaft along +Z, KeyLock's insert
                // frame) rather than off KeyRestRotation, because identity is the frame the blade's
                // broad face is already known to be vertical in - "the keyhole's slot is vertical" -
                // and rolling that face down onto the floor is exactly the quarter turn a key takes
                // between held-for-a-lock and dropped-on-a-floor. Yawed by the balloon's own id
                // rather than randomly, so which way a given key is facing stays the same every
                // iteration a ghost bursts the same balloon - cosmetic, not gameplay, but nothing
                // else in this room roots its visuals in Time.time either.
                float yaw = (balloon.id * 47) % 360;
                balloon.heldKey.LieDown(yaw);
            }
        }
    }
}
