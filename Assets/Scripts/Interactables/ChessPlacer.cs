using UnityEngine;

namespace IterationRoom
{
    // Putting a held chess piece back on the board, with the LEFT MOUSE BUTTON. Lives on the player,
    // beside BalloonTool, and is the same shape of thing: a verb that only exists while the right
    // object is in the hand.
    //
    // The two cannot collide even though they share the button. BalloonTool returns immediately unless
    // the PIN is held, this returns immediately unless a PIECE is held, and PlayerHand has exactly one
    // item out at a time - so at any moment at most one of them is listening.
    //
    // WHY LEFT CLICK RATHER THAN E. E is "take the thing in front of me" everywhere in this game, and
    // it is already how the piece got into the hand. Putting it down is the opposite motion and wants
    // its own button, or a player standing over a board with a piece would have one key meaning both
    // "pick that up" and "put this down" depending on state they cannot see.
    public class ChessPlacer : MonoBehaviour
    {
        public Camera playerCamera;
        public PlayerHand hand;

        // The board. Everything about which piece goes where is ITS question - this component owns
        // only the aiming and the button.
        public ChessBoard board;

        // How far the player can place from. Generous: the board is 5.4m across and its far side is
        // out of arm's reach from any edge, so a placement range tuned to a hand would make half the
        // squares unusable without walking round.
        public float reach = 6f;

        // How many pieces this player has actually put down, ever - not reset at the loop boundary,
        // because learning a control is not state the iteration rewinds. The left-click prompt retires
        // against this. Same rule as CarriedItemsDisplay's put-down prompt, for the same reason.
        public int PlacementCount { get; private set; }

        // ONCE. Not "three times" like the Tab prompt, and not "until it is used" like the swing:
        // this one has the lit square standing next to it doing the pointing, so the disc only has to
        // say which BUTTON, and nobody forgets a button they have just pressed. The lit square itself
        // stays for good - which square is a new answer every time.
        public int placeHintRetireAfter = 1;

        // And only from close to the square it is pointing at. Hung on the marker the moment a piece
        // entered the hand, the disc would appear the instant the player picked something up on the
        // far side of the room - describing a click that cannot reach - and it would be a HUD element
        // about a control rather than a label on the thing the control acts on. Four metres is
        // "standing at the board", against the six the placement itself reaches.
        public float hintRange = 4f;

        // The square the held piece belongs on, or -1 for nothing held that goes anywhere. Recomputed
        // every frame rather than cached on pickup: the piece can leave the hand by routes this
        // component never sees (Tab, a ghost, the loop resetting).
        private int MarkedSquare
        {
            get
            {
                if (hand == null || board == null || hand.Held == null) return -1;
                return board.HomeSquareOf(hand.Held.itemId);
            }
        }

        // THE PROMPT POINTS AT THE DESTINATION, not at the hand. ControlHintDisplay hangs its mouse
        // disc on this, exactly as it hangs the swing prompt on the balloon a click would burst -
        // so the one prompt says both "left click" and "over there", which is the whole answer to a
        // control the room otherwise never explains.
        public bool WantsPlaceHint =>
            board != null && board.Marker != null && MarkedSquare >= 0
            && PlacementCount < placeHintRetireAfter
            && WithinHintRange
            && (LoopManager.Instance == null || LoopManager.Instance.AcceptsInput);

        // Measured from the CAMERA, like every other range in this game that decides whether a prompt
        // is about something the player is standing at - and measured to the marker rather than to the
        // board, because the marker is what the disc is drawn on.
        private bool WithinHintRange
        {
            get
            {
                if (playerCamera == null || board.Marker == null) return false;
                Vector3 d = board.Marker.position - playerCamera.transform.position;
                return d.sqrMagnitude <= hintRange * hintRange;
            }
        }

        public Transform PlaceAnchor => board != null ? board.Marker : null;

        private void Update()
        {
            if (hand == null || playerCamera == null || board == null) return;

            bool running = LoopManager.Instance == null || LoopManager.Instance.AcceptsInput;

            // The lit square follows the hand and not the aim, deliberately. It is not a cursor - it
            // is the answer to "where does this one go", which is the question a player crossing the
            // room with a piece is actually holding.
            int home = running ? MarkedSquare : -1;
            board.MarkSquare(home);

            if (!running || home < 0 || board.boardCollider == null) return;
            if (!Input.GetMouseButtonDown(0)) return;

            CarryableItem held = hand.Held;

            // Raycast against the BOARD alone. A hit test against everything would let a piece be
            // placed on whatever happened to be in front of the crosshair, and the failure would be
            // silent - the piece would leave the hand and appear somewhere the player was not aiming.
            Ray ray = new Ray(playerCamera.transform.position, playerCamera.transform.forward);
            if (!board.boardCollider.Raycast(ray, out RaycastHit hit, reach)) return;

            // ONLY ITS OWN SQUARE, and only from a click that landed in it. The alternative - snap to
            // whatever square was clicked - makes a puzzle out of remembering an opening position, and
            // the piece would sit somewhere plausible and wrong with nothing to say so. Refusing
            // instead means the lit square is not decoration: it is the one place the click works.
            if (board.SquareAt(hit.point) != home) return;
            if (!board.CanSeat(held.itemId)) return;

            // Surrendered rather than dropped straight out of the hand, because the HAND has
            // bookkeeping of its own - `carried`, `carriedItems`, and which item is out - that
            // CarryableItem knows nothing about. Surrender also writes the event into the recording,
            // which is the whole of how a past self repeats this: the id resolves to this piece's own
            // square through ItemRegistry, and the ghost puts it there without knowing what a board is.
            //
            // Asked AFTER CanSeat, never before. A recorded surrender that could not be completed is a
            // ghost doing something the player did not.
            CarryableItem placed = hand.Surrender(held.itemId);
            if (placed == null) return;

            // Cannot fail - CanSeat was true a line ago and nothing between can have changed it - but
            // the item is out of the hand's books by now, so a hole here would lose it until the loop
            // swept it back. Put it down where it was aimed instead.
            if (!board.Seat(placed)) placed.DropAt(hit.point);
            else PlacementCount++;
        }
    }
}
