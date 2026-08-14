using UnityEngine;

namespace IterationRoom
{
    // THE WAY OUT OF A CYCLE: one wall grid cell that opens behind the console once it is full, and
    // seals again once the player has dropped through it.
    //
    // WHY A GRID CELL AND NOT A DOOR. Every doorway in the building is 1.3 x 2.5 and deliberately
    // off-grid, so it reads as a door fitted into a wall. This lands exactly on the panel grid, which
    // reads as the wall itself coming apart - and that is what the moment is. See
    // `docs/cycle-design.md` §6.
    //
    // EXPLICIT STATE, unlike `RewardPlinth`, which is the other mover in this room. A plinth is
    // derived - it rises with its condition and sinks when the condition lapses - and that is right
    // for something an iteration rewinds. This does not rewind: it opens once, is used once, and
    // seals. Deriving it would need a "has the player gone through" input anyway, so the latch is the
    // honest shape.
    //
    // ON `Time.unscaledDeltaTime`, like `Door.Seal` and for the same reason: this moves while no
    // iteration is running, and the boundary must not be at the mercy of a time scale the pause menu
    // owns.
    public class CycleExit : MonoBehaviour
    {
        // The slab filling the opening, authored CLOSED. Slides aside by `openLocalOffset`, the same
        // way every door in the game moves - a wall panel sliding into the wall beside it.
        public Transform cover;
        public Vector3 openLocalOffset = new Vector3(0f, 0f, 1.75f);
        public float openDuration = 2.2f;
        public float sealDuration = 1.1f;

        // How far below this object's own height counts as "gone". The drop is real - there is no kill
        // plane and no fall damage anywhere in the project - so having fallen past the floor of the
        // room above is the plainest possible statement of having left it.
        public float throughDrop = 2.5f;

        public Transform player;

        public AudioSource audioSource;
        public AudioClip openClip;
        public AudioClip sealClip;

        private Vector3 closedLocalPos;
        private float openAmount;
        private float rate;
        private bool wantOpen;
        private bool sealed_;

        // The player has dropped through and is not coming back up this way. Latched, never
        // recomputed: once they are down, a stray upward test result must not un-say it.
        public bool PlayerThrough { get; private set; }

        private void Awake()
        {
            if (cover != null) closedLocalPos = cover.localPosition;
        }

        public void Open()
        {
            if (sealed_) return;
            wantOpen = true;
            rate = openDuration;
            if (audioSource != null && openClip != null) audioSource.PlayOneShot(openClip);
        }

        public void Seal()
        {
            wantOpen = false;
            sealed_ = true;
            rate = sealDuration;
            if (audioSource != null && sealClip != null) audioSource.PlayOneShot(sealClip);
        }

        private void Update()
        {
            if (cover != null)
            {
                float step = Time.unscaledDeltaTime / Mathf.Max(0.01f, rate);
                openAmount = Mathf.MoveTowards(openAmount, wantOpen ? 1f : 0f, step);
                // Eased, so the wall neither snaps aside nor stops dead. Same curve the plinth uses.
                cover.localPosition = closedLocalPos + openLocalOffset * Mathf.SmoothStep(0f, 1f, openAmount);
            }

            // Only ever asked while the opening is actually open, so nothing can be "through" a hole
            // that was never made.
            if (PlayerThrough || player == null || openAmount < 0.5f) return;
            if (player.position.y < transform.position.y - throughDrop) PlayerThrough = true;
        }
    }
}
