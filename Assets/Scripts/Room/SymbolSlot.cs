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
        // SEVERAL renderers, because the recess is a real hollow now rather than a flat panel: four
        // bars make the rim standing proud of the wall, and they have to say the same thing at the
        // same time or the hole lights up in pieces. One painter each - LitRendererPainter caches
        // what it last wrote, so a single one shared across four renderers would paint the first and
        // skip the rest.
        public Renderer[] plateRenderers;
        public Color idleColor = new Color(0.20f, 0.20f, 0.23f);
        public float idleEmission = 0f;
        public Color filledColor = new Color(0.35f, 0.62f, 0.75f);
        public float filledEmission = 0.5f;

        public AudioSource audioSource;
        public AudioClip insertClip;

        // How the cube arrives. `insertOffer` is where it starts in the seat's own frame - local +Z
        // is the recess's outward normal, so this is straight out of the mouth - and the tilt is what
        // it straightens up from on the way in. Values live in SceneBuilder like everything else;
        // these are only what a slot built without them would do.
        public Vector3 insertOffer = new Vector3(0f, 0f, 0.55f);
        public Vector3 insertTilt = new Vector3(-7f, 13f, 4f);
        public float insertDuration = 0.45f;

        // Derived from the room rather than tracked here, so there is exactly one record of "this
        // cube's seated" - CubeRoom.seated - instead of two that discipline alone keeps in step.
        public bool Filled => room != null && room.IsSeated(acceptedItemId);

        private Collider trigger;
        private bool playerInRange;
        private LitRendererPainter[] painters;

        private bool Running => LoopManager.Instance == null || LoopManager.Instance.AcceptsInput;

        // Still asked for the PROMPT's sake even though the plate no longer lights for it - the
        // press needs the cube equipped, not merely carried, like every other fixture operated WITH
        // an object.
        // `Has` is gone with Tab: one object at a time means carried and in-hand are the same fact,
        // so the two tests this used to draw a careful line between have collapsed into one.
        public bool WantsInteractHint =>
            Running && !Filled && playerInRange
            && hand != null && hand.Holding(acceptedItemId)
            && PlayerLookup.InView(HintAnchor);

        public Transform HintAnchor => seat != null ? seat : transform;

        private void Awake()
        {
            trigger = GetComponent<Collider>();

            painters = new LitRendererPainter[plateRenderers != null ? plateRenderers.Length : 0];
            for (int i = 0; i < painters.Length; i++) painters[i] = new LitRendererPainter();

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
            if (!GameInput.InteractPressed) return;
            // Checked as well as claimed - see PlayerLookup.InteractTaken. E means one thing at a
            // time, and standing at the right recess with the right cube is what decides which.
            if (PlayerLookup.InteractTaken) return;

            hand.MarkInteract();

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

            Transform socket = seat != null ? seat : transform;
            // SEATED FIRST, then animated. InsertInto is what actually takes custody - after this
            // line the cube is in state 4 of CLAUDE.md SS1.2 whatever happens next - and the slide
            // only decides where it is drawn on the way. An animation that owned the hand-over could
            // lose the object if it were interrupted; this one cannot.
            cube.InsertInto(socket);
            StartCoroutine(SocketInsert.Slide(cube, socket, insertOffer, insertTilt, insertDuration,
                                              audioSource, insertClip));
        }

        // Through a property block, because the six recesses share one material and each is saying
        // something different about itself.
        private void ApplyPlate(Color color, float emission)
        {
            if (plateRenderers == null || painters == null) return;

            int count = Mathf.Min(plateRenderers.Length, painters.Length);
            for (int i = 0; i < count; i++) painters[i].Paint(plateRenderers[i], color, color * emission);
        }
    }
}
