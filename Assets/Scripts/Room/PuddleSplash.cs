using UnityEngine;

namespace IterationRoom
{
    // SPLASHES WHEN SOMETHING WALKS THROUGH THE WATER, on the puddle rather than on the walker.
    //
    // On the puddle because the puddle is the thing that knows how big it is: `SpreadingPuddle` grows
    // it for as long as the tap runs, so "am I standing in it" is a question only this object can
    // answer, and answering it anywhere else would mean a second copy of the radius.
    //
    // It fires on ENTERING the water and then on a stride's rhythm while still in it - not per
    // footstep, because nothing here is told when a foot lands. A step rate is a fair approximation
    // and it is what stops a player standing still in a puddle from splashing forever.
    [RequireComponent(typeof(SpreadingPuddle))]
    public class PuddleSplash : MonoBehaviour
    {
        public AudioSource audioSource;
        public AudioClip[] splashClips;

        // Roughly a walking stride. Slower than the footstep rate on purpose: every step in water does
        // not make a full splash, and firing on each one turns a puddle into a drum.
        public float strideInterval = 0.45f;

        private bool wasInside;
        private float nextSplash;
        private SpreadingPuddle puddle;
        private Renderer rend;

        private void Update()
        {
            if (puddle == null) puddle = GetComponent<SpreadingPuddle>();
            if (rend == null) rend = GetComponent<Renderer>();

            // NO WATER, NO SPLASH. The puddle keeps its last radius while it dries and switches its
            // renderer off at the end, so the radius alone would go on splashing over a dry floor.
            if (rend != null && !rend.enabled) { wasInside = false; return; }

            // Asked of the puddle rather than measured off the transform, so the two cannot disagree
            // about how far the water reaches.
            float radius = puddle != null ? puddle.Radius : transform.localScale.x * 0.5f;

            Collider player = PlayerLookup.Collider;
            bool inside = false;
            if (player != null && player.enabled && radius > 0.01f)
            {
                Vector3 offset = player.transform.position - transform.position;
                // Horizontal only: the puddle is a film on the floor and the player's pivot is on it,
                // but a storey below has the same X and Z and must not splash.
                inside = Mathf.Abs(offset.y) < 1.5f
                      && new Vector2(offset.x, offset.z).sqrMagnitude < radius * radius;
            }

            if (inside && (!wasInside || Time.time >= nextSplash))
            {
                Splash();
                nextSplash = Time.time + strideInterval;
            }
            wasInside = inside;
        }

        private void Splash()
        {
            if (audioSource == null || splashClips == null || splashClips.Length == 0) return;
            AudioClip clip = splashClips[Random.Range(0, splashClips.Length)];
            if (clip == null) return;
            // Pitch scattered a little, for the reason the three clips exist at all: two identical
            // splashes in a row read as a sample rather than as water.
            audioSource.pitch = Random.Range(0.92f, 1.09f);
            audioSource.PlayOneShot(clip);
        }

        // The tap being shut takes the puddle with it, and a splash must not survive that - `OnDisable`
        // rather than a flag, so the state cannot be left set by whatever turned the flow off.
        private void OnDisable()
        {
            wasInside = false;
            nextSplash = 0f;
        }
    }
}
