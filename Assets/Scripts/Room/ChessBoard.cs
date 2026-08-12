using System.Collections.Generic;
using UnityEngine;

namespace IterationRoom
{
    // Room2West's puzzle: a chess set with pieces missing off it, scattered across the room floor,
    // and a board whose empty squares each want ONE of them back.
    //
    // WHY THIS IS AN ACCUMULATION PUZZLE AND NOT A KNOWLEDGE ONE. `docs/puzzle-design.md` filed chess
    // under knowledge - "once you know the layout no iteration reduces the work" - and that is true of
    // a puzzle whose difficulty is REMEMBERING where a piece goes. This one TELLS you where the piece
    // in your hand goes (its home square lights up) and charges you the WALK. More errands than fit in
    // sixty seconds, so the work is divided across iterations, and every delivery a past self made
    // replays. That is the shape the loop is built for.
    //
    // THE HOME SQUARE IS WHERE THE PIECE STARTED. SceneBuilder measures each piece's pose off the
    // model's own opening position and only then scatters it, so the answer is the board as chess sets
    // in the world are already arranged - nothing invented and nothing to look up.
    //
    // GHOSTS TIDY IT FOR FREE, and that is why the seating goes through IItemSocket rather than
    // through a method of this class. A ghost's recorded hand-over carries an itemId and nothing else;
    // ItemRegistry turns that id into a socket; this class registers ONE SOCKET PER PIECE, each of
    // which is that piece's own square. So a past self replaying "surrender Piece_12" puts Piece_12 on
    // its square, and GhostReplayer learns nothing about chess.
    public class ChessBoard : MonoBehaviour
    {
        // One board. The static is for symmetry with BalloonField and for anything spawned at runtime
        // that cannot be wired to a scene object; everything SceneBuilder can hand a reference to is
        // handed one instead.
        public static ChessBoard Instance { get; private set; }

        // The playing surface's own collider, which is what ChessPlacer aims at. It lives on the model
        // node inside the imported set - scaled 0.3039, rotated -90 about X - which is why the grid
        // below is stated in WORLD units and measured, rather than read off that transform.
        public Collider boardCollider;

        public int squaresPerSide = 8;

        // The grid, in world units, MEASURED off the opening position rather than divided out of the
        // board's bounding box. The model has a frame around its playing area - 5.40m of slab against
        // 4.19m from the a-file to the h-file - so bounds/8 gives cells 0.675m apart where the real
        // ones are 0.598m apart, and by the far file that error is more than a whole square.
        public float squarePitch = 0.598f;
        // World XZ of the board's centre - the corner where the four middle squares meet - and the
        // height its surface stands at.
        public Vector3 gridCentre;
        public float surfaceY;

        // Parallel arrays filled by SceneBuilder, one entry per piece:
        //   pieces[i]      the piece
        //   homeAnchors[i] an empty object holding that piece's ORIGINAL pose inside the set
        //   startsHome[i]  true if it was left on the board rather than scattered
        //
        // THE ANCHOR IS THE OPENING POSE, and that is the whole trick to seating a piece correctly.
        // CarryableItem.InsertInto parents an item to a socket at local identity, so the socket's own
        // frame decides how the piece sits - and an anchor built as a sibling of the piece, carrying
        // the local position and rotation the piece had before it was scattered, means "identity"
        // reproduces the model's own arrangement exactly. Nothing here has to know that the set is
        // scaled, Z-up, or point-mirrored for the dark pieces.
        public CarryableItem[] pieces;
        public Transform[] homeAnchors;
        public bool[] startsHome;

        // The slab that lights up over the held piece's home square. ONE, moved, rather than one per
        // square: only ever one is wanted at a time, and 64 renderers to show one of them is 63
        // renderers of waste.
        public Transform marker;
        // Clear of the surface, or it z-fights the square it is marking.
        public float markerLift = 0.004f;

        // THE PAYOFF, which this class does not perform. It owns only the fact that the board is
        // finished; what the room does about it - lights, the board opening, the plinth - is
        // `ChessReward`, on the boundary CLAUDE.md §2 draws between a puzzle's rule and a room's
        // theatre. Null is a working board with no reward, which is what a test scene wants.
        public ChessReward reward;

        // itemId -> its square, -> its anchor, -> whether it starts on the board.
        private readonly Dictionary<string, int> homeById = new Dictionary<string, int>();
        private readonly Dictionary<string, Transform> anchorById = new Dictionary<string, Transform>();
        private readonly List<IItemSocket> sockets = new List<IItemSocket>();
        private readonly List<CarryableItem> startSeated = new List<CarryableItem>();
        // Which ids are currently home. Ids rather than objects, so the same set answers both "is this
        // piece home" and "how many are home" without walking anything.
        private readonly HashSet<string> seated = new HashSet<string>();

        public int PieceCount => pieces != null ? pieces.Length : 0;
        public int SeatedCount => seated.Count;
        public bool IsSolved => PieceCount > 0 && seated.Count >= PieceCount;

        private void Awake()
        {
            Instance = this;
            BuildIndex();
            MarkSquare(-1);
        }

        // The pieces that were never scattered are put out of play HERE and not in Awake, because
        // seating re-parents them and CarryableItem captures its origin in ITS Awake. Script execution
        // order between two components is arbitrary; Start after Awake is not.
        private void Start() => SeatStartingPieces();

        private void OnEnable()
        {
            foreach (IItemSocket socket in sockets) ItemRegistry.RegisterSocket(socket);
        }

        private void OnDisable()
        {
            foreach (IItemSocket socket in sockets) ItemRegistry.UnregisterSocket(socket);
            if (Instance == this) Instance = null;
        }

        private void BuildIndex()
        {
            homeById.Clear();
            anchorById.Clear();
            sockets.Clear();
            startSeated.Clear();

            int count = Mathf.Min(PieceCount, homeAnchors != null ? homeAnchors.Length : 0);
            for (int i = 0; i < count; i++)
            {
                CarryableItem piece = pieces[i];
                Transform anchor = homeAnchors[i];
                if (piece == null || anchor == null || string.IsNullOrEmpty(piece.itemId)) continue;
                if (homeById.ContainsKey(piece.itemId)) continue;

                // Derived from the anchor rather than passed in alongside it, so there is exactly one
                // statement of where a piece belongs and the square index cannot drift out of step
                // with the pose.
                homeById[piece.itemId] = SquareAt(anchor.position);
                anchorById[piece.itemId] = anchor;
                sockets.Add(new DelegateItemSocket(piece.itemId, Seat));

                if (startsHome != null && i < startsHome.Length && startsHome[i]) startSeated.Add(piece);
            }
        }

        // -1 for an id that is not a piece of this set, which is the answer everything else branches
        // on - it is how ChessPlacer knows the thing in the hand is a pin and not a rook.
        public int HomeSquareOf(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return -1;
            return homeById.TryGetValue(itemId, out int square) ? square : -1;
        }

        // The square a world point falls in, or -1 for a point off the playing area. Off the area is a
        // real answer and not a clamp: the board's frame is part of the model, and a click on the
        // frame is a click that missed.
        public int SquareAt(Vector3 worldPoint)
        {
            int n = Mathf.Max(1, squaresPerSide);
            float half = n * 0.5f * squarePitch;

            float dx = worldPoint.x - gridCentre.x;
            float dz = worldPoint.z - gridCentre.z;
            if (Mathf.Abs(dx) > half || Mathf.Abs(dz) > half) return -1;

            int col = Mathf.Clamp(Mathf.FloorToInt(dx / squarePitch + n * 0.5f), 0, n - 1);
            int row = Mathf.Clamp(Mathf.FloorToInt(dz / squarePitch + n * 0.5f), 0, n - 1);
            return row * n + col;
        }

        public Vector3 CentreOf(int square)
        {
            int n = Mathf.Max(1, squaresPerSide);
            int col = square % n;
            int row = square / n;
            return new Vector3(
                gridCentre.x + (col + 0.5f - n * 0.5f) * squarePitch,
                surfaceY,
                gridCentre.z + (row + 0.5f - n * 0.5f) * squarePitch);
        }

        public bool IsSeated(string itemId) => seated.Contains(itemId);

        // Asked BEFORE the item leaves anyone's hands, so a hand-over is only ever begun when it can
        // be finished. PlayerHand.Surrender writes a Surrender event into the recording, and a
        // recorded surrender that did not happen is a past self doing something the player did not.
        public bool CanSeat(string itemId)
        {
            return anchorById.ContainsKey(itemId) && !seated.Contains(itemId);
        }

        // The piece goes home. Deliberately InsertInto and not DropAt: a seated piece is OUT OF PLAY
        // exactly like the key in Room2's lock - not takeable, not free for a ghost, and rewound only
        // by the loop. Without that a past self could lift a piece back off the board and the room
        // would go backwards while the player watched.
        public bool Seat(CarryableItem item)
        {
            if (item == null || !CanSeat(item.itemId)) return false;

            item.InsertInto(anchorById[item.itemId]);
            seated.Add(item.itemId);
            // Fired from here rather than polled in an Update, because this is the only place the
            // answer can change to true - and it fires exactly once, since a seated piece cannot be
            // seated again.
            if (IsSolved) reward?.Play();
            return true;
        }

        // Which square the marker sits on, or -1 to put it away. Driven by ChessPlacer rather than
        // read from the hand here, because deciding when a prompt is wanted is that component's job
        // and this one owns only where the square IS.
        public void MarkSquare(int square)
        {
            if (marker == null) return;

            bool show = square >= 0 && square < squaresPerSide * squaresPerSide;
            if (show) marker.position = CentreOf(square) + Vector3.up * markerLift;
            if (marker.gameObject.activeSelf != show) marker.gameObject.SetActive(show);
        }

        public Transform Marker => marker;

        // The top of an iteration. The pieces themselves are already back at their origins by the time
        // this runs (ItemRegistry.ReturnAllToOrigin) - scattered ones on the floor, the rest on their
        // squares - so this only has to forget who was home and put the untouched ones back out of
        // play.
        //
        // The reward is closed FIRST, and that ordering is load-bearing: the home anchors are
        // children of the two board halves, so seating a piece before the halves are back would
        // stand it out over the floor where the open board left its square.
        public void ResetBoard()
        {
            reward?.ResetNow();
            seated.Clear();
            MarkSquare(-1);
            SeatStartingPieces();
        }

        // The pieces SceneBuilder left on the board. They are part of the puzzle's count - the lights
        // come up when ALL 32 are home - but no part of its work, and seating them is what makes them
        // scenery rather than 20 more things to pick up by accident.
        private void SeatStartingPieces()
        {
            for (int i = 0; i < startSeated.Count; i++) Seat(startSeated[i]);
        }
    }
}
