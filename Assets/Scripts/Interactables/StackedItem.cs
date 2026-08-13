using UnityEngine;

namespace IterationRoom
{
    // A carryable that was built standing on ANOTHER carryable, and falls when that one leaves.
    //
    // The cube room stacks two of its six. Take the bottom one and the top one has nothing under it;
    // before this it simply hung there, which reads as a bug whoever caused it - the player, or a
    // past self repeating the same pickup.
    //
    // WHY NOT A RIGIDBODY. Carryables deliberately have none: the loop has to be able to put every
    // object back exactly, and a simulated fall settles somewhere slightly different every time -
    // which is also why CarryableItem.DropAt places an item rather than letting it drop. That
    // argument still holds, so this falls along ONE axis to ONE known height. It is the same shape of
    // answer Door and RewardPlinth already give: a scripted move with a number behind it, not physics.
    //
    // Nothing here is recorded, and nothing needs to be. A fall is DERIVED - it happens because the
    // support left, and a ghost that repeats taking the support makes it happen again by itself. A
    // take is recorded by identity, never by position (CLAUDE.md SS1.4), so a cube that is somewhere
    // else this time is still the same cube to whoever comes for it.
    [RequireComponent(typeof(CarryableItem))]
    public class StackedItem : MonoBehaviour
    {
        public CarryableItem item;

        // The one directly underneath at build time. Null is legal and means "nothing holds this up",
        // which is simply a component that will drop its object once and then never do anything.
        public CarryableItem support;

        // Real gravity. There is no reason to invent a number: the fall is about a metre, which comes
        // out at just under half a second, and anything slower reads as an object being lowered.
        public float gravity = 9.81f;

        // How far the support may be from directly underneath and still count as underneath. Wide
        // enough to survive the jitter a stack is built with, narrow enough that a support dropped
        // somewhere else in the room is not still holding this up from across the floor.
        public float supportRadius = 0.45f;

        public AudioSource audioSource;
        public AudioClip landClip;

        // The height this was BUILT at, captured rather than configured - the same number
        // SceneBuilder already had to work out to place it, and reading it back is one fewer value
        // to keep in step.
        private float stackedY;
        private float speed;

        private void Awake()
        {
            if (item == null) item = GetComponent<CarryableItem>();
            stackedY = transform.position.y;
        }

        private void Update()
        {
            // In a hand, on a ghost, or seated in a recess. Someone else owns where this is, and a
            // fall competing with them would fight the hand anchor for the transform.
            if (item == null || item.IsCarried) { speed = 0f; return; }

            // ONLY EVER DOWN. A support put back underneath does not lift this off the floor again -
            // that would be an object climbing, and the only thing entitled to rebuild the tower is
            // the loop's own rewind, which sets the position outright.
            float target = Supported ? stackedY : item.floorY;
            Vector3 p = transform.position;
            if (p.y <= target + 0.001f) { speed = 0f; return; }

            speed += gravity * Time.deltaTime;
            float y = p.y - speed * Time.deltaTime;

            bool landed = y <= target;
            if (landed) y = target;
            transform.position = new Vector3(p.x, y, p.z);

            if (!landed) return;
            speed = 0f;
            if (audioSource != null && landClip != null) audioSource.PlayOneShot(landClip);
        }

        // Still under this and still free-standing. `IsCarried` covers all three ways a support can
        // stop being furniture at once - picked up, taken by a ghost, seated in its recess - and the
        // distance test covers the fourth, which is being put back down somewhere else.
        private bool Supported
        {
            get
            {
                if (support == null || support.IsCarried) return false;

                Vector3 d = support.transform.position - transform.position;
                return d.y < 0f
                    && Mathf.Abs(d.x) < supportRadius
                    && Mathf.Abs(d.z) < supportRadius;
            }
        }
    }
}
