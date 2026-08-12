using UnityEngine;

namespace IterationRoom
{
    // One recess in the cube room's wall, carrying one symbol and wanting the one cube that matches
    // it. The input half of the puzzle; `CubeRoom` owns every question about which cube belongs where.
    // That is the same split `ChessPlacer` has from `ChessBoard`.
    //
    // E, NOT LEFT CLICK, and the difference is what the fixture IS rather than a preference. The chess
    // board is a surface AIMED at from across a room, so it wants the button that means "act on what I
    // am pointing at". A recess in a wall is a fixture you STAND at, which in this game has always
    // been E - the drawer, the lock, the plates. Left click here would mean aiming at a hole 26cm
    // across from inside arm's reach.
    //
    // Range is polled, not driven by trigger callbacks - see FloorButton for why.
    [RequireComponent(typeof(Collider))]
    public class SymbolSlot : MonoBehaviour, IInteractHintTarget
    {
        public CubeRoom room;
        public PlayerHand hand;
        public string acceptedItemId;

        // Where an accepted cube ends up, parented at local identity: this transform's frame decides
        // how it sits in the wall, so the cube arrives square to the recess whatever way it was being
        // carried.
        public Transform seat;

        // The plate the symbol is printed on. It lights while the cube that matches it is on the
        // player - which is the whole of how a player learns the pairing without being told, and the
        // reason the room needs no legend on the wall.
        public Renderer plateRenderer;
        public Color idleColor = new Color(0.20f, 0.20f, 0.23f);
        public float idleEmission = 0f;
        public Color readyColor = new Color(0.55f, 0.85f, 1f);
        public float readyEmission = 1.6f;
        public Color filledColor = new Color(0.35f, 0.62f, 0.75f);
        public float filledEmission = 0.5f;

        public AudioSource audioSource;
        public AudioClip insertClip;

        public bool Filled { get; private set; }

        private Collider trigger;
        private Collider playerCollider;
        private bool playerInRange;
        private MaterialPropertyBlock block;
        private Color appliedColor;
        private float appliedEmission = -1f;

        private bool Running => LoopManager.Instance == null || LoopManager.Instance.AcceptsInput;

        // LIT WHILE IT IS CARRIED, prompted only while it is IN HAND. The two are deliberately
        // different tests. The light is a beacon - it has to be readable from across the room, and a
        // cube stowed by Tab is still a cube the player has to deliver. The prompt is a promise that
        // the press will work, and the press needs the cube out, like every other fixture operated
        // WITH an object.
        private bool Wanted => Running && !Filled && hand != null && hand.Has(acceptedItemId);

        public bool WantsInteractHint =>
            Wanted && playerInRange && hand.Holding(acceptedItemId);

        public Transform HintAnchor => seat != null ? seat : transform;

        private void Awake()
        {
            trigger = GetComponent<Collider>();
            block = new MaterialPropertyBlock();
            ApplyPlate(idleColor, idleEmission);
        }

        private void FixedUpdate()
        {
            if (playerCollider == null)
            {
                GameObject player = GameObject.FindGameObjectWithTag("Player");
                if (player != null)
                {
                    playerCollider = player.GetComponent<Collider>();
                    if (hand == null) hand = player.GetComponent<PlayerHand>();
                }
            }

            playerInRange = playerCollider != null
                && playerCollider.enabled
                && trigger != null
                && trigger.bounds.Intersects(playerCollider.bounds);
        }

        private void Update()
        {
            if (Filled) ApplyPlate(filledColor, filledEmission);
            else if (Wanted) ApplyPlate(readyColor, readyEmission);
            else ApplyPlate(idleColor, idleEmission);

            if (!WantsInteractHint) return;
            if (!Input.GetKeyDown(KeyCode.E)) return;

            // Asked of the ROOM, not of this slot's own `Filled`. A cube can be seated by a past self
            // between one frame and the next, and the room is the one place that knows.
            if (room == null || !room.CanSeat(acceptedItemId)) return;

            // Surrendered rather than lifted straight out of the hand: PlayerHand owns `carried`,
            // `carriedItems` and which item is out, and CarryableItem knows about none of it.
            // Surrender is also what writes the event a past self replays.
            CarryableItem given = hand.Surrender(acceptedItemId);
            if (given == null) return;

            // Through the room, so the player's placement and a ghost's land in exactly one piece of
            // code. If it refuses now - it cannot, CanSeat was true a line ago - the cube would be out
            // of the hand's books with nowhere to be, so it goes on the floor rather than nowhere.
            if (!room.Seat(given)) given.DropAt(transform.position);
        }

        // Called by CubeRoom, for the player's placement and a ghost's alike.
        public void Accept(CarryableItem cube)
        {
            if (cube == null) return;
            cube.InsertInto(seat != null ? seat : transform);
            Filled = true;
            if (audioSource != null && insertClip != null) audioSource.PlayOneShot(insertClip);
        }

        // The loop rewinding. The cube itself is already back on the floor by now; this only forgets
        // that it was ever here.
        public void Clear() => Filled = false;

        // Through a property block, because the six plates share one material and each is saying
        // something different about itself.
        private void ApplyPlate(Color color, float emission)
        {
            if (plateRenderer == null) return;
            if (appliedEmission >= 0f && appliedColor == color && Mathf.Approximately(appliedEmission, emission)) return;

            appliedColor = color;
            appliedEmission = emission;

            plateRenderer.GetPropertyBlock(block);
            block.SetColor("_BaseColor", color);
            block.SetColor("_EmissionColor", color * emission);
            plateRenderer.SetPropertyBlock(block);
        }
    }
}
