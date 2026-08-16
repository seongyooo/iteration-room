using UnityEngine;

namespace IterationRoom
{
    // ONE TAP'S WATER: the fall, where it lands, and the spill it leaves.
    //
    // WHY THIS EXISTS RATHER THAN `SetActive` ON A ROOT. The tap used to switch the whole flow off in
    // one call, which made the water a decal the press turned on and off. Water does not work like
    // that, and the two halves genuinely have different lifetimes: **the stream stops the instant the
    // valve shuts, and the puddle does not.** One object cannot express that, so this splits them -
    // the fall and its impact go immediately, the spill is handed over to `SpreadingPuddle` to dry in
    // its own time.
    public class WaterFlow : MonoBehaviour
    {
        // Everything that only exists while water is actually leaving the spout - the stream, the
        // layer inside it, and the crown where it lands.
        public GameObject[] falling;
        public SpreadingPuddle puddle;

        // The running-water loop, STARTED AND STOPPED BY HAND rather than left to `playOnAwake`.
        // `Awake` runs once, and this object is switched off within it - so whether the loop is playing
        // after the tap is later turned on depends on subtleties of when Unity re-honours that flag,
        // which is not something a sound this important should rest on. Told explicitly, it cannot be
        // wrong.
        public AudioSource runSource;

        // WHERE THE WATER STOPS, when something is standing under it. The stream is authored falling
        // all the way to the floor; a bucket underneath catches it at its rim, and from that moment
        // there is no water past that point - so the fall is shortened to end at the rim and the spill
        // and the spray, which are what happens when water reaches a FLOOR, are switched off.
        //
        // Without this the tap fills the bucket and floods the floor at the same time, which reads as
        // the bucket not working.
        public Transform stream;
        public GameObject[] floorOnly;

        // Set at build time from where the stream was authored, so the catch can be undone exactly.
        public float spoutLocalY;
        public float floorLocalY;

        private float catchY;
        private bool caught;

        // Called by BucketStand when a bucket arrives or leaves. `rimWorldY` is the height the water
        // now lands at.
        public void SetCatch(bool on, float rimWorldY)
        {
            caught = on;
            catchY = transform.InverseTransformPoint(new Vector3(0f, rimWorldY, 0f)).y;

            // THE SPILL FOLLOWS THE CATCH, and saying so here is the whole of it. The feed used to be
            // set only by `SetRunning`, so a bucket slid under a tap that was ALREADY running left
            // the puddle spreading with nothing reaching the floor - and lifting that bucket left the
            // floor being flooded by a puddle that had started drying. The catch changes whether
            // water reaches the floor exactly as much as the valve does, so it has to answer for it.
            puddle?.SetFed(Running && !caught);
            ApplyStream();
        }

        private void ApplyStream()
        {
            if (stream == null) return;

            float landing = caught ? Mathf.Clamp(catchY, floorLocalY, spoutLocalY - 0.05f) : floorLocalY;
            float drop = Mathf.Max(0.01f, spoutLocalY - landing);

            Vector3 s = stream.localScale;
            stream.localScale = new Vector3(s.x, drop, s.z);
            Vector3 p = stream.localPosition;
            stream.localPosition = new Vector3(p.x, landing + drop / 2f, p.z);

            // The floor's share of the effect: the spill that spreads and the spray that bounces. Both
            // are about water meeting a floor and neither is true while a bucket is in the way.
            if (floorOnly != null)
                foreach (GameObject g in floorOnly)
                    if (g != null) g.SetActive(Running && !caught);
        }

        public bool Running { get; private set; }

        private void Awake()
        {
            // Authored on so the pieces can be seen and placed in the Editor; off before the first
            // frame, because no tap in this game starts running.
            ResetInstant();
        }

        public void SetRunning(bool running)
        {
            Running = running;
            if (falling != null)
                foreach (GameObject go in falling) if (go != null) go.SetActive(running);

            // The spill only spreads while water is actually reaching the floor - so not while a
            // bucket is catching it, however long the tap runs.
            puddle?.SetFed(running && !caught);
            ApplyStream();

            if (runSource != null)
            {
                if (running) { if (!runSource.isPlaying) runSource.Play(); }
                else runSource.Stop();
            }
        }

        // THE LOOP REWINDING. Distinct from `SetRunning(false)` in the one way that matters: it does
        // not dry, it deletes. This runs behind the closed eyelids at the top of an iteration, and a
        // puddle taking six seconds to evaporate would still be shrinking when the player wakes up.
        public void ResetInstant()
        {
            Running = false;
            caught = false;
            if (falling != null)
                foreach (GameObject go in falling) if (go != null) go.SetActive(false);

            puddle?.ResetInstant();
            ApplyStream();
            if (runSource != null) runSource.Stop();
        }
    }
}
