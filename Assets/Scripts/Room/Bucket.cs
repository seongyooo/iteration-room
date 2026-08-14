using System.Collections.Generic;
using UnityEngine;

namespace IterationRoom
{
    // A bucket, which is a `CarryableItem` with one extra bit: whether it has water in it.
    //
    // NOT A SECOND ITEM. A full bucket and an empty one are the same object with a flag, which is
    // what keeps CLAUDE.md §1.2 intact - swapping a "BucketEmpty" for a "BucketFull" would be one
    // object becoming another, and the loop's sweep would have two things to put back where there
    // had been one.
    //
    // The fill does NOT survive an iteration. `ResetFill` is called from the room's own reset, so
    // every iteration starts with empty buckets and the ghosts refill them - the same bargain the
    // tree makes about its own damage.
    [RequireComponent(typeof(CarryableItem))]
    public class Bucket : MonoBehaviour
    {
        // Every bucket in the scene, so the valve can find whichever is under it without a physics
        // query. Registered on enable and dropped on disable, exactly as `ItemRegistry` does it -
        // which also means a bucket in a sleeping cycle is not in the list.
        public static readonly List<Bucket> All = new List<Bucket>();

        public CarryableItem Item { get; private set; }

        // The water inside, scaled up as it fills. A bucket that only changed colour when full would
        // give the player no reason to believe waiting was doing anything.
        public Transform water;
        public float fullHeight = 0.16f;

        public bool Full { get; private set; }

        private float shown;

        private void Awake() => Item = GetComponent<CarryableItem>();
        private void OnEnable() { All.Add(this); Apply(); }
        private void OnDisable() => All.Remove(this);

        public void SetFill(float t)
        {
            if (Full) return;
            shown = Mathf.Clamp01(t);
            Apply();
        }

        public void Fill()
        {
            Full = true;
            shown = 1f;
            Apply();
        }

        public void Empty()
        {
            Full = false;
            shown = 0f;
            Apply();
        }

        public void ResetFill() => Empty();

        private void Apply()
        {
            if (water == null) return;
            water.localScale = new Vector3(1f, Mathf.Max(0.0001f, shown * fullHeight), 1f);
            // Hidden outright at zero rather than left as a sliver, which reads as a dirty bucket.
            Renderer r = water.GetComponent<Renderer>();
            if (r != null && r.enabled != shown > 0.02f) r.enabled = shown > 0.02f;
        }
    }
}
