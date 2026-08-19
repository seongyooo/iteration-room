using UnityEngine;

namespace IterationRoom
{
    // A ROOM WITH STANDING WATER IN IT - room2-5's landing room, the thing at the bottom of the slide.
    //
    // WHAT IT IS NOT. It is not a volume that holds anything, not a puzzle, not a socket and not a
    // hazard: the player rides the chute in, drops through the surface and wades. Nothing here is
    // recorded, nothing is reset and nothing is carried, so a past self swims the same way the living
    // player does - by walking, with the water drawn around them (CLAUDE.md 1.5: this is neither an
    // instant nor a level, because nothing about it is a state the loop has to rewind).
    //
    // WHY IT IS NOT SOLID. Water the player can walk through needs no collider, and giving it one
    // would put a surface across the room that the slide's ride path raycasts at build time - the
    // chute's measured landing would come back on the waterline instead of the floor and the rider
    // would be set down a metre and a bit in the air. See SceneBuilder.SampleRidePath.
    //
    // WHAT IT OWNS: where the surface is, how far it reaches, and the splash when somebody is in it.
    // The surface mesh's own look is the shader's (IterationRoom/WaterSurface) and the balls floating
    // on it are `FloatingBalls`' - one component, one job.
    //
    // POLLED, NOT TRIGGERED, for the reason `PuddleSplash` and `FloorButton` are: the slide moves the
    // player with `Teleport`, which disables the controller's collider for the write, and a trigger
    // that is switched off across the frame the player crosses the surface is a trigger that never
    // fires the one event this component exists for.
    public class WaterPool : MonoBehaviour
    {
        [Header("The body of water, in this transform's local space")]
        // WHERE THE SURFACE SITS WHEN THE ROOM IS FULL. Authored by `SceneBuilder`, and from then on
        // the level is `SetLevel`'s business - `PoolDrain` lowers it as the room empties. Everything
        // else here reads `Level`, never this.
        public float surfaceLocalY = 1.2f;
        // The mesh, the ripples and the floats all follow the level rather than being told separately:
        // one number moved in one place, or a drained room keeps its ripples at head height.
        public Transform surface;
        public FloatingBalls[] floats;
        // Below this there is not enough water to wade in - a film on the floor, not a pool. It is
        // what turns the slowdown and the wet footsteps off when the room empties, and it is why
        // `emptyLevel` on the drain does not have to be exactly zero.
        public float wadeDepth = 0.25f;
        // Half-extents in X and Z. A box rather than the renderer's bounds: the sheet is pushed into
        // the walls so no gap shows at the edge, and "inside the water" should stop at the wall.
        public Vector2 halfExtents = new Vector2(4.3f, 5.2f);
        // How far below the surface the room's floor is. Only used to reject a player who is a storey
        // away with the same X and Z - the same trap `PuddleSplash` guards against.
        public float floorDrop = 2f;

        [Header("Going into it and through it")]
        public ParticleSystem splash;
        public WaterRipples ripples;
        public AudioSource audioSource;
        // THE SOUND OF WATER BEING PASSED THROUGH, not of water being hit. It was the three
        // `sfx_water_splash_*` clips - the ones the tap's puddle uses, which are water landing on a
        // hard floor from above - and in play that is what they sounded like: a splash effect laid
        // over the room rather than the player going into it. Generated instead (`MakeWadeClip`), with
        // no transient and the bubbles arriving after the push.
        public AudioClip[] splashClips;
        // Coming in off the slide is one event and wading is another, and they are not the same size.
        public int entryParticles = 120;
        public int strideParticles = 26;
        public float entryVolume = 1f;
        // Roughly a stride in water, which is slower than one on a floor. Particles only: the SOUND of
        // wading is fired from the player's own step phase (`FirstPersonController.wadeClips`), because
        // that is the one clock that knows when a foot lands. This one only has to look right.
        public float strideInterval = 0.55f;
        // Below this, the player is standing still and standing still in water is silent.
        public float strideSpeed = 0.35f;

        [Header("Walking in it")]
        // WHAT THE WATER DOES TO A WALK. 0.45 of the dry speed - waist-deep water roughly halves a
        // walking pace in life, and at much less than this a room nine metres across becomes a wait.
        // Applied as `FirstPersonController.SpeedScale`, which multiplies the top speed and leaves the
        // acceleration ramps alone: water makes you slow, not sluggish to start.
        public float wadeSpeedScale = 0.45f;

        private bool wasInside;
        private float nextSplash;
        private Vector3 lastPosition;
        private bool hasLast;
        // Whether the slowdown and the wet footsteps are OURS to take back. Set when this component
        // writes them and cleared when it restores them, so it can never restore a value it did not
        // set - see the note on `SpeedScale`.
        private FirstPersonController held;

        // Where the surface is NOW. Starts at the authored height and falls as the room drains.
        public float Level { get; private set; }

        private void Awake() => SetLevel(surfaceLocalY);

        // THE ONE WRITER OF THE WATER'S HEIGHT. The mesh, the ripple plane and every float's resting
        // depth come from here, so there is no way for the room to be half drained.
        public void SetLevel(float level)
        {
            Level = level;

            if (surface != null)
            {
                Vector3 at = surface.localPosition;
                surface.localPosition = new Vector3(at.x, level, at.z);
            }

            if (ripples != null) ripples.SetHeight(level);

            // The floats ride DOWN with it - a duck left at head height over an empty room is the
            // clearest possible statement that the water was never real. Offsets rather than new
            // homes, so each one keeps the spot it was placed at.
            if (floats != null)
                foreach (FloatingBalls f in floats) f?.SetWaterOffset(level - surfaceLocalY);
        }

        public bool Contains(Vector3 worldPoint)
        {
            // NOT WHILE IT IS DRAINING AWAY. Below the wading depth this is a wet floor: no slowdown,
            // no wet footsteps, no splash for walking across it.
            if (Level < wadeDepth) return false;

            Vector3 local = transform.InverseTransformPoint(worldPoint);
            return Mathf.Abs(local.x) <= halfExtents.x
                && Mathf.Abs(local.z) <= halfExtents.y
                && local.y <= Level
                && local.y >= Level - floorDrop;
        }

        private void Update()
        {
            Collider player = PlayerLookup.Collider;
            if (player == null || !player.enabled) { wasInside = false; hasLast = false; return; }

            // The pivot is at the player's feet, so this is "are the legs in the water" - which is
            // what wading is, and it is true the frame the chute puts them down through the surface.
            Vector3 at = player.transform.position;
            bool inside = Contains(at);

            float speed = 0f;
            if (hasLast && Time.deltaTime > 0f)
            {
                Vector3 step = at - lastPosition;
                step.y = 0f;
                speed = step.magnitude / Time.deltaTime;
            }
            lastPosition = at;
            hasLast = true;

            if (inside && !wasInside)
            {
                // ARRIVING. The whole ride ends here, so it is the loudest thing this makes - and the
                // splash is put where the player crossed the surface rather than at their feet, which
                // by now are on the floor of the pool.
                Splash(at, entryParticles, entryVolume, 0.88f);
                nextSplash = Time.time + strideInterval;
            }
            else if (inside && speed > strideSpeed && Time.time >= nextSplash)
            {
                // Particles and a ring - the sound of this step is the player's own, in time with
                // their gait (`FirstPersonController.wadeClips`).
                Splash(at, strideParticles, 0f, 1f);
                nextSplash = Time.time + strideInterval;
            }

            if (inside != wasInside) SetWading(inside);
            wasInside = inside;
        }

        // THE SLOWDOWN AND THE WET FOOTSTEPS, taken on entering and given back on leaving.
        //
        // Written on the TRANSITION rather than every frame on purpose. A component that reasserts a
        // value every frame owns it forever and cannot be layered with anything - and this one has to
        // hand it back cleanly at a loop boundary, where the player is teleported out of the water
        // between one frame and the next.
        private void SetWading(bool wading)
        {
            if (wading)
            {
                FirstPersonController controller = PlayerLookup.Controller;
                if (controller == null) return;
                held = controller;
                held.Wading = true;
                held.SpeedScale = wadeSpeedScale;
            }
            else if (held != null)
            {
                held.Wading = false;
                held.SpeedScale = 1f;
                held = null;
            }
        }

        private void Splash(Vector3 worldPoint, int particles, float volume, float pitchCentre)
        {
            // At the WATERLINE, not at the point given: a splash is something the surface does, and one
            // emitted at a pair of feet on the floor of a 1.2m pool is underwater where nothing sees it.
            Vector3 local = transform.InverseTransformPoint(worldPoint);
            Vector3 waterline = transform.TransformPoint(new Vector3(local.x, Level, local.z));

            if (splash != null)
            {
                splash.transform.position = waterline;
                splash.Emit(particles);
            }

            // AND THE RING IT LEAVES. Started from the same point and at the same moments as the
            // spray, so what spreads is what the player just did rather than a second effect on its
            // own clock.
            if (ripples != null) ripples.Ring(waterline);

            if (volume <= 0f) return;
            if (audioSource == null || splashClips == null || splashClips.Length == 0) return;
            AudioClip clip = splashClips[Random.Range(0, splashClips.Length)];
            if (clip == null) return;

            audioSource.transform.position = waterline;
            // Scattered a little, for the reason the three clips exist at all: two identical splashes
            // in a row read as a sample rather than as water.
            audioSource.pitch = pitchCentre * Random.Range(0.94f, 1.07f);
            audioSource.PlayOneShot(clip, volume);
        }

        // THE ONE PATH THAT IS NOT A PLAYER WALKING OUT. A cycle is unloaded with the player still in
        // the pool and nothing else would ever give the speed back - they would walk the rest of the
        // game at wading pace, in a room that no longer exists. Restoring here rather than trusting
        // `Update` to notice is CLAUDE.md §6's "trace every path by which the state can escape".
        private void OnDisable()
        {
            SetWading(false);
            wasInside = false;
            hasLast = false;
        }

        // The extent of the water, which is otherwise two numbers on a component and invisible in the
        // scene - the same reason `SlideRide` draws its path.
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.5f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(new Vector3(0f, Application.isPlaying ? Level : surfaceLocalY, 0f),
                                new Vector3(halfExtents.x * 2f, 0.02f, halfExtents.y * 2f));
        }
    }
}
