using UnityEngine;

namespace IterationRoom
{
    // WHAT THE BEAM HAS TO ARRIVE AT. A plate in room3-2N that is lit while the light is on it.
    //
    // **IT DOES NOTHING YET, ON PURPOSE.** Whether the beam runs a machine or opens a way out of the
    // cycle is undecided, and building the answer before the question is what put a CCTV system in
    // this cycle before there was a puzzle for it to watch. `Lit` is the whole interface: when there
    // is something for the beam to do, that thing asks this.
    //
    // Nothing here is recorded or reset. The beam is derived from where the mirrors are, and the
    // mirrors are `CarryableItem`s that the loop already sweeps home - so at the top of an iteration
    // this goes dark on its own, without the loop having to know it exists.
    public class LaserReceiver : MonoBehaviour
    {
        public Renderer lamp;
        public Color offColour = new Color(0.22f, 0.22f, 0.24f);
        public Color onColour = new Color(1f, 0.35f, 0.28f);

        // How near the beam's landing point has to be to count. **Generous on purpose**: the mirror
        // is aimed by turning your body, and the law of reflection doubles every wobble - turn the
        // glass one degree and the far end of the beam moves two. At twenty metres that is 70cm of
        // travel for one degree of hand, so a target measured in centimetres would be a target nobody
        // could hold. See `FirstPersonController` for the other half of that, which is halving look
        // sensitivity while a mirror is in the hand.
        public float radius = 0.7f;

        public AudioSource audioSource;
        public AudioClip onClip;

        public bool Lit { get; private set; }

        private static readonly System.Collections.Generic.List<LaserReceiver> all
            = new System.Collections.Generic.List<LaserReceiver>();

        public static System.Collections.Generic.List<LaserReceiver> All => all;

        private void OnEnable() => all.Add(this);
        private void OnDisable() => all.Remove(this);

        private int illuminatedFrame = -10;

        public bool Covers(Vector3 point) => (point - transform.position).sqrMagnitude < radius * radius;

        // Called by the beam, every frame it lands here. A stamp rather than a bool, so the plate
        // goes out by itself the frame after the beam moves off it and nothing has to tell it.
        public void Illuminate() => illuminatedFrame = Time.frameCount;

        private void Update()
        {
            bool lit = illuminatedFrame >= Time.frameCount - 1;
            if (lit == Lit) return;

            Lit = lit;
            if (lamp != null) lamp.material.color = lit ? onColour : offColour;

            // Silent while the clock is stopped, for the reason every other fixture in this building
            // is: the loop puts the mirrors back during the blackout, and a plate going out behind
            // the closed eyelids is the machinery showing through.
            if (!lit || audioSource == null || onClip == null) return;
            if (LoopManager.Instance != null && !LoopManager.Instance.IterationRunning) return;
            audioSource.PlayOneShot(onClip);
        }
    }
}
