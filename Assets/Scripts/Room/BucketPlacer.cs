using UnityEngine;

namespace IterationRoom
{
    // LEFT CLICK PUTS THE BUCKET WHERE IT BELONGS: on a stand under a tap, or over the tank.
    //
    // On the player, beside `ChessPlacer` and `BalloonTool`, and it cannot clash with either - all
    // three are silent unless the hand holds the one thing they act on, and `PlayerHand` holds exactly
    // one thing at a time (CLAUDE.md §4).
    //
    // WHY LEFT CLICK AND NOT E. E already means take and put down, and a third meaning on the same key
    // would need a prompt that says which one is about to happen. Left click is the button this game
    // already uses for "do the thing this object is FOR" - swing the pin, place the chess piece - and
    // putting a bucket under a tap is the same kind of act.
    //
    // NEAREST IN VIEW rather than a raycast, matching every other fixture here: the volume answers on
    // proximity and `PlayerLookup.InView` decides whether it is on screen, so the press and the prompt
    // can never disagree.
    public class BucketPlacer : MonoBehaviour
    {
        public PlayerHand hand;
        public string bucketItemId = "Bucket";
        public float reach = 3.2f;

        // THIS CYCLE'S STANDS AND TANKS, handed over by `CycleBinding` when the cycle wakes.
        //
        // They used to be found with `FindObjectsByType` inside `FindTargets` - twice, every frame a
        // bucket was in the hand. That scans every loaded scene, and this project keeps every cycle
        // loaded at once (only the roots are deactivated), so the cost grows with the game rather
        // than with the room. Handed over instead, for the same reason and by the same route as
        // `ChessPlacer.board`: this control is on the PLAYER, in the core scene, and a serialized
        // reference from there into a cycle scene is dropped on save.
        //
        // Left alone when a cycle has none, so a cycle without buckets does not clear the one that
        // has them.
        public BucketStand[] stands;
        public WaterTank[] tanks;

        private BucketStand targetStand;
        private WaterTank targetTank;

        // Read by ControlHintDisplay, which shares the swing disc: at most one of the pin, a chess
        // piece and a bucket is ever in hand, so at most one of the three ever wants it.
        public bool WantsPlaceHint => Held != null && (targetStand != null || targetTank != null);
        // The aiming point, not the object's own transform - see BucketStand.Aim. The disc has to
        // hang on the same point the targeting used, or the prompt is over one place and the click
        // answers another.
        public Transform PlaceAnchor =>
            targetTank != null ? targetTank.Aim : (targetStand != null ? targetStand.Aim : null);

        private Bucket Held
        {
            get
            {
                if (hand == null || !hand.Holding(bucketItemId)) return null;
                CarryableItem item = hand.Held;
                return item != null ? item.GetComponent<Bucket>() : null;
            }
        }

        private void Update()
        {
            if (LoopManager.Instance != null && !LoopManager.Instance.AcceptsInput)
            {
                targetStand = null; targetTank = null;
                return;
            }

            Bucket bucket = Held;
            FindTargets(bucket);
            if (bucket == null) return;

            if (!GameInput.UsePressed) return;

            // ALREADY GOING OVER. A pour takes a second and a half now, and a second click inside it
            // would either start another one or hand a tipped bucket to a stand mid-stream.
            if (bucket.IsPouring) return;

            // POURING BEATS PLACING when both are in reach, because a bucket with water in it is on
            // its way TO the tank - a player standing between the two and clicking meant the errand,
            // not the detour.
            if (targetTank != null && bucket.Level > 0.01f)
            {
                // Recorded only if it actually starts, and recorded on the TANK rather than on the
                // bucket: which tank was poured into is the identity a past self has to reproduce.
                if (bucket.BeginPour(targetTank) && targetTank.pourPoint != null)
                    targetTank.pourPoint.RegisterPlayerPour();
                return;
            }

            if (targetStand != null)
            {
                // NAMED, so a past self puts its bucket on the stand it actually used rather than on
                // whichever one `ItemRegistry` happens to hand back first. See
                // PlayerHand.Surrender(itemId, targetName).
                CarryableItem item = hand.Surrender(bucketItemId, targetStand.name);
                if (item != null) targetStand.Place(item.GetComponent<Bucket>());
            }
        }

        private void FindTargets(Bucket bucket)
        {
            targetStand = null;
            targetTank = null;
            // Nothing to aim at while it is going over: the prompt would be pointing at a click that
            // is refused, and the disc is supposed to mean the opposite of that.
            if (bucket == null || bucket.IsPouring) return;

            // FROM THE EYE, like every other "which one of these did you mean" in this game
            // (ItemRegistry.AimedTakeable). Measured from the body it disagreed with the prompt,
            // which is placed against the camera.
            Camera cam = PlayerLookup.Eye;
            Vector3 eye = cam != null ? cam.transform.position : transform.position;
            float bestStand = reach * reach, bestTank = reach * reach;

            if (stands != null)
                foreach (BucketStand s in stands)
                {
                    if (s == null || !s.IsFree) continue;
                    Transform aim = s.Aim;
                    float d = (aim.position - eye).sqrMagnitude;
                    if (d >= bestStand || !PlayerLookup.InView(aim)) continue;
                    bestStand = d; targetStand = s;
                }

            if (bucket.Level <= 0.01f || tanks == null) return;
            foreach (WaterTank t in tanks)
            {
                if (t == null) continue;
                Transform aim = t.Aim;
                float d = (aim.position - eye).sqrMagnitude;
                if (d >= bestTank || !PlayerLookup.InView(aim)) continue;
                bestTank = d; targetTank = t;
            }
        }
    }
}
