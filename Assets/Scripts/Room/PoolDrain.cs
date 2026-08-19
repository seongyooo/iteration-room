using UnityEngine;

namespace IterationRoom
{
    // ROOM2-6'S RULE: three valves, two turns each, and the room empties.
    //
    // WHAT IT OWNS is the chain and nothing else - the valves own their own turning, `WaterPool` owns
    // the water, `Door` owns the door. This is the one place that says what they have to do with each
    // other: **six turns opens the drain, the drain empties the pool, and the empty pool is what the
    // door hangs on.**
    //
    // A `RoomCondition`, so `Door` asks it the same question it asks the chorus and the tank, and
    // `Cycle.ResetRooms` rewinds it with every other room rule at the top of an iteration.
    //
    // **`Satisfied` IS "THE WATER IS GONE", NOT "THE VALVES ARE OPEN"**, and the difference is the
    // whole design: the door does not open when the puzzle is solved, it opens when the CONSEQUENCE
    // has finished. The drain takes its time in full view and the player watches the room they are
    // standing in change before they are let out of it.
    //
    // SIX TURNS IS MORE THAN ONE ITERATION COMFORTABLY HOLDS, which is the point and is why the valves
    // are recorded interactables: a past self that got two turns into the far valve has done two turns
    // of the work, every iteration, for free. This is the same shape as room2-2's tank - accumulated
    // HANDS rather than accumulated state - and nothing about it survives the boundary.
    public class PoolDrain : RoomCondition
    {
        public Valve[] valves;
        public WaterPool pool;

        [Header("The drain")]
        // The plate over the hole. Slid aside rather than sunk: sinking hides the one moving part
        // under the thing it is supposed to be revealing.
        public Transform cover;
        public Vector3 coverOpenOffset = new Vector3(1.05f, 0f, 0f);
        public float coverSlideDuration = 1.1f;

        [Header("Emptying")]
        // How long the room takes to empty once it starts. Long enough to be watched and short enough
        // that a player who has just finished the last valve is not standing still for the rest of
        // their iteration.
        public float drainDuration = 6f;
        // Where the water ends up. Not zero: a floor with a millimetre of water left on it still reads
        // as a wet room, and a surface that vanishes entirely reads as the water being switched off.
        public float emptyLevel = 0.03f;

        [Header("The whirlpool")]
        // The funnel over the hole, hidden until the water starts moving. Scaled with how much water
        // is left rather than switched on: a full-sized vortex over a nearly empty room is a hole in
        // the floor with a cone stuck in it.
        public Transform funnel;
        public float funnelSpinSpeed = 220f;
        // What the water does to whatever is floating on it. Handed to every float group for exactly
        // as long as the room is emptying - see FloatingBalls' vortex fields.
        public FloatingBalls[] floats;
        public float vortexPull = 1.6f;
        public float vortexSwirl = 2.4f;

        [Header("Sound")]
        public AudioSource audioSource;
        public AudioClip drainClip;
        public ParticleSystem vortex;

        // How full the room is, 1 at the start and 0 when it has emptied. Derived state, reset with
        // everything else - nothing about it survives an iteration.
        public float Fill { get; private set; } = 1f;

        private bool draining;
        private Vector3 coverHome;
        private float fullLevel;

        // THE DOOR'S QUESTION. Empty, not "emptying" - see the note at the top.
        public override bool Satisfied => Fill <= 0.001f;

        private void Awake()
        {
            if (cover != null) coverHome = cover.localPosition;
            fullLevel = pool != null ? pool.surfaceLocalY : 1.2f;
        }

        private void Update()
        {
            SpinFunnel();
            if (!draining && AllValvesOpen()) StartCoroutine(Drain());
        }

        private bool AllValvesOpen()
        {
            if (valves == null || valves.Length == 0) return false;
            foreach (Valve v in valves)
                if (v == null || !v.FullyOpen) return false;
            return true;
        }

        private System.Collections.IEnumerator Drain()
        {
            draining = true;

            // THE COVER FIRST, AND THE WATER ONLY AFTER IT. The order is the causation: a room that
            // starts emptying while the drain is still shut is a room where the valves did it, and
            // then the hole in the floor is decoration.
            if (cover != null)
            {
                Vector3 from = coverHome, to = coverHome + coverOpenOffset;
                for (float e = 0f; e < coverSlideDuration; e += Time.deltaTime)
                {
                    float t = Mathf.Clamp01(e / coverSlideDuration);
                    cover.localPosition = Vector3.Lerp(from, to, t * t * (3f - 2f * t));
                    yield return null;
                }
                cover.localPosition = to;
            }

            if (vortex != null) vortex.Play();
            // THE WHOLE POOL STARTS MOVING, and this is the moment it does. Everything on the surface
            // is handed to the hole at once, so the drain reads as pulling rather than as the water
            // merely getting shorter.
            SetVortex(true);
            if (audioSource != null && drainClip != null)
            {
                audioSource.clip = drainClip;
                audioSource.loop = true;
                audioSource.Play();
            }

            for (float e = 0f; e < drainDuration; e += Time.deltaTime)
            {
                // ACCELERATING. A tank drains fastest when it is fullest - the head above the hole is
                // what pushes it - so the level falls off a curve rather than a ramp. Squared is not
                // Torricelli's law and is near enough at this size to read correctly.
                float t = Mathf.Clamp01(e / drainDuration);
                SetFill(1f - t * t);
                yield return null;
            }

            SetFill(0f);

            SetVortex(false);
            if (vortex != null) vortex.Stop();
            if (audioSource != null && audioSource.loop)
            {
                audioSource.Stop();
                audioSource.loop = false;
            }
        }

        private void SetFill(float fill)
        {
            Fill = Mathf.Clamp01(fill);
            if (pool != null) pool.SetLevel(Mathf.Lerp(emptyLevel, fullLevel, Fill));

            if (funnel == null) return;
            // THE FUNNEL RIDES THE SURFACE AND SHRINKS WITH IT. Its mouth is as wide as the water is
            // deep, which is the shape a real one takes: a shallow dish over a full pool pulling into
            // a narrow throat as the last of it goes.
            bool showing = Fill > 0.02f && draining;
            if (funnel.gameObject.activeSelf != showing) funnel.gameObject.SetActive(showing);
            if (!showing) return;

            float level = pool != null ? pool.Level : 0f;
            funnel.localPosition = new Vector3(funnel.localPosition.x, level, funnel.localPosition.z);
            float span = Mathf.Lerp(0.55f, 1.35f, Fill);
            funnel.localScale = new Vector3(span, Mathf.Max(0.12f, level * 0.9f), span);
        }

        // Told to every float group at once, or a duck would keep drifting to its own spot while the
        // water under it is spinning down a hole.
        private void SetVortex(bool on)
        {
            if (floats == null) return;
            foreach (FloatingBalls f in floats)
            {
                if (f == null) continue;
                f.vortexCentre = on ? transform : null;
                f.vortexPull = on ? vortexPull : 0f;
                f.vortexSwirl = on ? vortexSwirl : 0f;
            }
        }

        private void SpinFunnel()
        {
            if (funnel != null && funnel.gameObject.activeSelf)
                funnel.Rotate(0f, funnelSpinSpeed * Time.deltaTime, 0f, Space.Self);
        }

        // THE LOOP REWINDING. Everything this room did goes back at once and in silence: the valves to
        // shut, the cover home, the water to full. `Cycle.ResetRooms` calls this after the item sweep
        // with every other room rule.
        //
        // The coroutine is stopped first, or a drain caught halfway through the boundary goes on
        // writing a falling level into a pool that has just been refilled.
        public override void ResetCondition()
        {
            StopAllCoroutines();
            draining = false;

            if (valves != null)
                foreach (Valve v in valves) v?.ResetValve();

            if (cover != null) cover.localPosition = coverHome;
            SetVortex(false);
            if (funnel != null) funnel.gameObject.SetActive(false);
            if (vortex != null) vortex.Stop();
            if (audioSource != null)
            {
                audioSource.Stop();
                audioSource.loop = false;
            }

            SetFill(1f);
        }
    }
}
