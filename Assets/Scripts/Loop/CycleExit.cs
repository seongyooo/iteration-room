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
        // **THE ONE IN ROOM3-2N'S FLOOR NEVER OPENS, AND THE LIGHTMAP HAS TO KNOW.**
        //
        // `SceneBuilder.MovesDuringPlay` returns true for every `CycleExit`, which holds a lid that
        // slides aside out of the GI bake - right for a hatch, wrong for the room-sized cover over
        // room4-1's ceiling, which is cut and stays shut for good (`opensWayOutOnBreak` is false,
        // and CLAUDE.md says not to "fix" that). Held out of the bake it had no baked light while
        // the floor around it did, so it sat in the middle of the room as a hard-edged white
        // rectangle - which is what play reported, twice.
        //
        // Set by the build and read by the build. Nothing at runtime looks at it; `Open` is
        // idempotent and this does not gate it.
        public bool neverOpens;

        // The slabs filling the opening, authored CLOSED. They slide aside by `openLocalOffset`, the
        // same way every door in the game moves - a wall panel sliding into the wall beside it.
        //
        // THERE ARE TWO, because the opening goes through two slabs with a service void between them:
        // the floor of the room above and the ceiling of the room below. Plugging only the top one
        // left a square recess in the lower room's ceiling with the shaft visible up inside it - the
        // hole was covered from one side and open from the other.
        public Transform[] covers;

        // ONE OFFSET PER COVER, and they are not the same. Both retract INTO THE SERVICE VOID rather
        // than sideways within their own slab - the floor lid drops as it slides, the ceiling lid
        // rises.
        //
        // Sliding sideways alone put each lid inside solid slab with a face coplanar to the one on
        // show, which z-fights across the whole square. Recessing the lid to avoid that left a
        // 10mm-deep hatch outline in the ceiling of the room below, lit differently from everything
        // round it. Moving them out of the slab entirely is what lets them be flush when closed and
        // gone when open, which is also what a real hatch does.
        public Vector3[] openOffsets;
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

        private Vector3[] closedLocalPos;
        private float openAmount;
        private float rate;
        private bool wantOpen;
        private bool sealed_;

        // The player has dropped through and is not coming back up this way. Latched, never
        // recomputed: once they are down, a stray upward test result must not un-say it.
        public bool PlayerThrough { get; private set; }

        private void Awake()
        {
            if (covers == null) return;
            closedLocalPos = new Vector3[covers.Length];
            for (int i = 0; i < covers.Length; i++)
                if (covers[i] != null) closedLocalPos[i] = covers[i].localPosition;
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
            if (covers != null && closedLocalPos != null)
            {
                float step = Time.unscaledDeltaTime / Mathf.Max(0.01f, rate);
                openAmount = Mathf.MoveTowards(openAmount, wantOpen ? 1f : 0f, step);
                // Eased, so the wall neither snaps aside nor stops dead. Same curve the plinth uses.
                float eased = Mathf.SmoothStep(0f, 1f, openAmount);
                for (int i = 0; i < covers.Length; i++)
                {
                    if (covers[i] == null) continue;
                    Vector3 offset = openOffsets != null && i < openOffsets.Length
                        ? openOffsets[i] : Vector3.zero;
                    covers[i].localPosition = closedLocalPos[i] + offset * eased;
                }
            }

            // Only ever asked while the opening is actually open, so nothing can be "through" a hole
            // that was never made.
            if (PlayerThrough || player == null || openAmount < 0.5f) return;
            if (player.position.y < transform.position.y - throughDrop) PlayerThrough = true;
        }
    }
}
