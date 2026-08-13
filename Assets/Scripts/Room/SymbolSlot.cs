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

        // The plate the symbol is printed on.
        //
        // ~~It lights while the cube that matches it is on the player~~ REMOVED 2026-08-13, by
        // request: that was the whole of how a player learned the pairing without being told, and
        // removing it means the room no longer volunteers the answer - the symbol on the cube and
        // the symbol on the plate are the only pairing left, same as the room's own "no legend on
        // the wall" rule already asked of everything else in it.
        public Renderer plateRenderer;
        public Color idleColor = new Color(0.20f, 0.20f, 0.23f);
        public float idleEmission = 0f;
        public Color filledColor = new Color(0.35f, 0.62f, 0.75f);
        public float filledEmission = 0.5f;

        public AudioSource audioSource;
        public AudioClip insertClip;

        // Derived from the room rather than tracked here, so there is exactly one record of "this
        // cube's seated" - CubeRoom.seated - instead of two that discipline alone keeps in step.
        public bool Filled => room != null && room.IsSeated(acceptedItemId);

        private Collider trigger;
        private bool playerInRange;
        private readonly LitRendererPainter painter = new LitRendererPainter();

        private bool Running => LoopManager.Instance == null || LoopManager.Instance.AcceptsInput;

        // Still asked for the PROMPT's sake even though the plate no longer lights for it - the
        // press needs the cube equipped, not merely carried, like every other fixture operated WITH
        // an object.
        private bool Wanted => Running && !Filled && hand != null && hand.Has(acceptedItemId);

        public bool WantsInteractHint =>
            Wanted && playerInRange && hand.Holding(acceptedItemId)
            && PlayerLookup.InView(HintAnchor);

        public Transform HintAnchor => seat != null ? seat : transform;

        private void Awake()
        {
            trigger = GetComponent<Collider>();
            ApplyPlate(idleColor, idleEmission);
        }

        private void FixedUpdate()
        {
            Collider playerCollider = PlayerLookup.Collider;
            if (hand == null) hand = PlayerLookup.Hand;

            playerInRange = playerCollider != null
                && playerCollider.enabled
                && trigger != null
                && trigger.bounds.Intersects(playerCollider.bounds);
        }

        private void Update()
        {
            ApplyPlate(Filled ? filledColor : idleColor, Filled ? filledEmission : idleEmission);

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

        // Called by CubeRoom, for the player's placement and a ghost's alike. CubeRoom.Seat adds the
        // itemId to `seated` in the same call, so Filled (derived from it) is already true by the
        // time anything asks.
        public void Accept(CarryableItem cube)
        {
            if (cube == null) return;
            cube.InsertInto(seat != null ? seat : transform);
            if (audioSource != null && insertClip != null) audioSource.PlayOneShot(insertClip);
        }

        // Through a property block, because the six plates share one material and each is saying
        // something different about itself.
        private void ApplyPlate(Color color, float emission) => painter.Paint(plateRenderer, color, color * emission);
    }
}
