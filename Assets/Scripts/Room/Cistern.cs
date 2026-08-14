using UnityEngine;

namespace IterationRoom
{
    // ROOM5'S RULE: fill the tank, and the tank takes more than one trip.
    //
    // **THE WAITING IS THE POINT, AND IT IS NEW.** A bucket takes eight seconds under the valve, and
    // for those eight seconds there is nothing to do. In every other room in this game a past self is
    // worth having because it was somewhere or did something; here it is worth having because it
    // SPENT TIME. A ghost standing under a valve is time already paid, and the living player spends
    // theirs elsewhere.
    //
    // That makes it the first room where the right answer is to start something and walk away, which
    // is a genuinely different way to use a past self than any of the others.
    //
    // Nothing here is stored across an iteration. The tank empties at the boundary and the ghosts
    // refill it, exactly as the tree re-falls and the pads re-count.
    public class Cistern : RoomCondition
    {
        public Valve valve;
        public int loadsNeeded = 6;

        // The visible column of water, scaled to the count. What the room is asking for has to be
        // legible from the door or the player cannot plan the trips.
        public Transform level;
        public float fullHeight = 1.5f;

        public AudioSource audioSource;
        public AudioClip pourClip;

        private int loads;
        private float shown;

        public int Loads => loads;
        public override bool Satisfied => loads >= loadsNeeded;

        // Called by the tank's trigger when a full bucket is brought to it. The bucket empties and
        // goes back to being a bucket; there is no second item and nothing to keep track of.
        public void Pour(Bucket bucket)
        {
            if (bucket == null || !bucket.Full || Satisfied) return;
            bucket.Empty();
            loads++;
            if (audioSource != null && pourClip != null) audioSource.PlayOneShot(pourClip);
        }

        private void Update()
        {
            if (level == null) return;
            // Eased toward the target rather than snapped, so a pour reads as water arriving.
            float target = Mathf.Clamp01(loads / (float)Mathf.Max(1, loadsNeeded));
            shown = Mathf.Lerp(shown, target, 1f - Mathf.Exp(-6f * Time.deltaTime));
            level.localScale = new Vector3(1f, Mathf.Max(0.0001f, shown * fullHeight), 1f);
        }

        public override void ResetCondition()
        {
            loads = 0;
            shown = 0f;
            if (level != null) level.localScale = new Vector3(1f, 0.0001f, 1f);
            valve?.ResetValve();
        }
    }

    // The tap. Fills whichever bucket is standing under it, and only while one is.
    //
    // NOT A GHOST INTERACTABLE. Nothing is operated here - a bucket is put down in the right place
    // and the valve does the rest - so there is no press to record and no signal to replay. What a
    // ghost contributes is the CARRYING, which `CarryEvent` already records, and the standing about,
    // which costs it nothing to reproduce.
    public class Valve : MonoBehaviour
    {
        public float fillSeconds = 8f;
        public float catchRadius = 0.55f;
        public Transform spout;

        public Renderer flowRenderer;

        private Bucket filling;
        private float progress;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        public void ResetValve()
        {
            filling = null;
            progress = 0f;
            Show(false);
        }

        private void Update()
        {
            bool running = LoopManager.Instance == null || LoopManager.Instance.IterationRunning;
            if (!running) { Show(false); return; }

            Bucket under = FindBucket();
            if (under != filling)
            {
                filling = under;
                progress = 0f;
            }

            if (filling == null || filling.Full) { Show(false); return; }

            Show(true);
            progress += Time.deltaTime;
            filling.SetFill(Mathf.Clamp01(progress / Mathf.Max(0.1f, fillSeconds)));
            if (progress >= fillSeconds) filling.Fill();
        }

        // Whatever bucket is sitting under the spout and not in anyone's hands. Polled rather than
        // triggered, for the reason every other fixture here polls: an object can be taken out from
        // under this in a frame the callback never arrives for.
        private Bucket FindBucket()
        {
            Vector3 at = spout != null ? spout.position : transform.position;
            Bucket best = null;
            float bestSqr = catchRadius * catchRadius;

            foreach (Bucket b in Bucket.All)
            {
                if (b == null || b.Item == null || b.Item.IsCarried) continue;
                float d = (b.transform.position - new Vector3(at.x, b.transform.position.y, at.z)).sqrMagnitude;
                if (d > bestSqr) continue;
                bestSqr = d;
                best = b;
            }
            return best;
        }

        private void Show(bool on)
        {
            if (flowRenderer == null) return;
            if (flowRenderer.enabled != on) flowRenderer.enabled = on;
        }
    }
}
