using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace IterationRoom
{
    // ROOM3-2N'S GROUND FLOOR: thirteen polycube blocks on the floor, one of them standing on a
    // plinth, and exactly one way for the other twelve to join it - a 4x4x4 cube.
    //
    // This is the same SHAPE of puzzle as Room2West's chess board and Room2East's recesses: a
    // number of objects larger than one pair of hands can deliver inside sixty seconds, each with
    // one place it belongs, seated through `IItemSocket` so a past self's delivery replays without
    // the ghost learning anything about the puzzle. What is different is that the destinations are
    // not fixtures in the room - they are the other blocks. The cube is its own board, and it does
    // not exist until the player has built it.
    //
    // **THE PACKING IS SOLVED OFFLINE, NOT HERE.** The Bedlam Cube has 19,186 solutions and finding
    // one is an exact cover; `Tools/split_bedlam_cube.py` does it once, bakes each piece's solved
    // pose into the model's own node translations, and prints the cell map `SceneBuilder` hands to
    // `solvedCells` below. Nothing at runtime searches for anything.
    //
    // WHY THE RULE IS "TOUCHING", AND WHY THAT IS SAFE WITH GHOSTS. A block may be clicked home the
    // moment its own place in the solution shares a face with something already there - order does
    // not matter beyond that. A past self's delivery is refused if that is not true when it
    // replays, and the obvious worry is that a ghost could be refused forever. It cannot, and the
    // reason is worth writing down: every ghost replays at its OWN recorded timestamp, and every
    // iteration seats a superset of what the one before it seated at the same instant. So a
    // delivery the player made at t=12 of some iteration is made at t=12 of every iteration after
    // it, against a cube that is at least as complete. The rule is monotone in time, which is
    // exactly what makes it replayable.
    public class BedlamCube : RoomCondition
    {
        // Indexed by PIECE NUMBER, which is the number in the model's node names and the number in
        // `solvedCells`. Three parallel arrays rather than a struct because SceneBuilder fills them
        // and Unity serialises arrays of references, not arrays of structs holding references.
        public CarryableItem[] pieces;
        // Each piece's place in the finished cube, as a child of this transform at the solved
        // offset with identity rotation. An item is parented to its socket at local identity
        // (`CarryableItem.InsertInto`), so an anchor built from the model's own node translation
        // reproduces the solved cube exactly - the same trick `ChessBoard.homeAnchors` uses to put
        // a rook back without knowing what a rook is.
        public Transform[] homeAnchors;

        // Cell (x * 16 + y * 4 + z) -> the piece filling it. 64 entries, every one of them a piece
        // index. This is the ONLY thing here that says which blocks touch which, and it is checked
        // against the anchors at build time - see SceneBuilder.
        public int[] solvedCells;
        public int cellsPerSide = 4;

        // The one that starts on the plinth. Without a seed there is nothing to click on and no
        // fixed place for the cube to be: the first block would decide where the finished cube
        // stood, which is a cube that could end up inside a wall.
        public int seedPiece;

        // WHERE THE CLICK IS AIMED AND WHERE THE DISC HANGS - the middle of the finished cube, not
        // whichever block happens to be nearest. One anchor for both, on the rule the whole E
        // family already follows: the prompt goes on the thing the click acts on, so the two can
        // never point at different places.
        public Transform aim;

        // How the block arrives: out of the cube's middle along its own radius, straightened up on
        // the way in. Radial rather than from above, because a block belonging to the underside of
        // the cube would otherwise be lowered through everything already built.
        public float insertOffer = 0.32f;
        public Vector3 insertTilt = new Vector3(-8f, 11f, 6f);
        public float insertDuration = 0.30f;

        public AudioSource audioSource;
        public AudioClip insertClip;

        // **THE FINISHED CUBE IS THE ESCAPE OBJECT, AND IT SHRINKS UNTIL IT FITS IN A HAND**
        // (2026-08-30, by request). No plinth, no separate reward: the thing the player built is the
        // thing they carry to room3-0.
        //
        // TWO OBJECTS, ONE ILLUSION, and the seam is what the shrink is for. `assembled` is the
        // 1.6m cube the thirteen blocks are seated into - it cannot be a `CarryableItem` itself,
        // because `InsertInto` restores an item's ORIGIN scale when it goes into a socket, and the
        // origin of that object is a cube taller than the console it would be put in. `held` is the
        // same model assembled and built small, hidden until now. The shrink runs on the first and
        // hands over to the second at the size they match, so what the player watches is one object
        // getting smaller.
        public Transform assembled;
        public CarryableItem held;
        // What `assembled` shrinks TO, as a fraction of its own size - the point at which `held`
        // takes over. Written by SceneBuilder from the two objects' measured sizes rather than
        // guessed, so the swap is invisible however either is resized later.
        public float heldScale = 0.25f;
        public float shrinkSeconds = 1.6f;

        // The plinth's plate, flashed on the answer to a click. A refused block has to say so:
        // without it the only feedback for "not yet" is nothing happening, which reads as a broken
        // control rather than as an answer. Same mechanism as `KeyLock`, for the same reason.
        public Renderer statusRenderer;
        public Color idleColor = new Color(0.14f, 0.14f, 0.16f);
        public Color acceptedColor = new Color(0.30f, 0.72f, 1f);
        public Color deniedColor = new Color(0.75f, 0.12f, 0.12f);
        public float flashSeconds = 0.3f;

        private readonly Dictionary<string, int> indexById = new Dictionary<string, int>();
        private readonly List<IItemSocket> sockets = new List<IItemSocket>();
        private readonly HashSet<string> seated = new HashSet<string>();
        // Piece -> the pieces it shares a face with in the solution. Derived from `solvedCells` in
        // Awake rather than authored, so there is one statement of the packing and not two.
        private List<int>[] neighbours;

        public int PieceCount => pieces != null ? pieces.Length : 0;
        public int SeatedCount => seated.Count;
        public bool IsSolved => PieceCount > 0 && seated.Count >= PieceCount;

        // What `Door` and anything else asks. The cube being finished is the room's rule; what the
        // room DOES about it is somebody else's (CLAUDE.md §2), and today nothing does - cycle 3
        // has no exit condition yet.
        public override bool Satisfied => IsSolved;

        private void Awake()
        {
            BuildIndex();
            BuildAdjacency();
        }

        // The seed is put out of play HERE and not in Awake, for the reason ChessBoard's starting
        // pieces are: seating re-parents an item, and `CarryableItem` captures its origin in ITS
        // own Awake. Execution order between two components is arbitrary; Start after Awake is not.
        private void Start()
        {
            // OUT OF PLAY UNTIL THE SHRINK, and hidden HERE rather than deactivated at build time -
            // `Hide` needs `CarryableItem.Awake` to have captured its origin first, and an inactive
            // object never gets there. The same reason a key inside a balloon is hidden this way.
            held?.Hide();
            SeatSeed();
            ShowStatus(idleColor);
        }

        private void OnEnable()
        {
            foreach (IItemSocket socket in sockets) ItemRegistry.RegisterSocket(socket);
        }

        private void OnDisable()
        {
            foreach (IItemSocket socket in sockets) ItemRegistry.UnregisterSocket(socket);
        }

        private void BuildIndex()
        {
            indexById.Clear();
            sockets.Clear();

            int count = Mathf.Min(PieceCount, homeAnchors != null ? homeAnchors.Length : 0);
            for (int i = 0; i < count; i++)
            {
                CarryableItem piece = pieces[i];
                if (piece == null || homeAnchors[i] == null || string.IsNullOrEmpty(piece.itemId)) continue;
                if (indexById.ContainsKey(piece.itemId)) continue;

                indexById[piece.itemId] = i;
                // One socket per block, so a ghost's recorded surrender of "Bedlam_07" resolves
                // through ItemRegistry to this cube and lands in block 7's own place. The ghost
                // learns nothing; `AcceptFromGhost` runs the same CanSeat every press runs.
                sockets.Add(new DelegateItemSocket(piece.itemId, Seat));
            }
        }

        // Which blocks share a face, read off the cell map. Two cells one step apart on any axis
        // holding two different pieces means those pieces touch - that is the whole definition, and
        // taking it from the map is what stops it drifting away from the packing it describes.
        private void BuildAdjacency()
        {
            neighbours = new List<int>[PieceCount];
            for (int i = 0; i < neighbours.Length; i++) neighbours[i] = new List<int>();

            int n = Mathf.Max(1, cellsPerSide);
            if (solvedCells == null || solvedCells.Length != n * n * n)
            {
                Debug.LogError($"[BedlamCube] '{name}' has {(solvedCells == null ? 0 : solvedCells.Length)} "
                             + $"cells where {n * n * n} are wanted - no block will ever be able to join.");
                return;
            }

            for (int x = 0; x < n; x++)
                for (int y = 0; y < n; y++)
                    for (int z = 0; z < n; z++)
                    {
                        int here = solvedCells[(x * n + y) * n + z];
                        // Forward neighbours only; the pair is recorded both ways below, so
                        // walking all six would record every pair twice.
                        Join(here, At(x + 1, y, z, n));
                        Join(here, At(x, y + 1, z, n));
                        Join(here, At(x, y, z + 1, n));
                    }
        }

        private int At(int x, int y, int z, int n) =>
            x < n && y < n && z < n ? solvedCells[(x * n + y) * n + z] : -1;

        private void Join(int a, int b)
        {
            if (a < 0 || b < 0 || a == b) return;
            if (a >= neighbours.Length || b >= neighbours.Length) return;
            if (!neighbours[a].Contains(b)) neighbours[a].Add(b);
            if (!neighbours[b].Contains(a)) neighbours[b].Add(a);
        }

        public bool IsSeated(string itemId) => seated.Contains(itemId);

        // -1 for anything that is not a block of this cube, which is how `BedlamPlacer` tells a
        // block from a mirror or a pin without knowing what either is.
        public int IndexOf(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return -1;
            return indexById.TryGetValue(itemId, out int index) ? index : -1;
        }

        // Asked BEFORE the block leaves anybody's hands, so a hand-over is only ever begun when it
        // can be finished. `PlayerHand.Surrender` writes the event into the recording, and a
        // recorded surrender that did not happen is a past self doing something the player did not.
        public bool CanSeat(string itemId)
        {
            int index = IndexOf(itemId);
            if (index < 0 || seated.Contains(itemId)) return false;
            // Nothing built yet: only the seed can start it, and it is seated by Start rather than
            // by a click. A cube growing from an arbitrary first block would be a cube in an
            // arbitrary place.
            if (seated.Count == 0) return index == seedPiece;

            foreach (int other in neighbours[index])
                if (pieces[other] != null && seated.Contains(pieces[other].itemId)) return true;

            return false;
        }

        public bool Seat(CarryableItem item)
        {
            if (item == null || !CanSeat(item.itemId)) return false;

            int index = indexById[item.itemId];
            Transform anchor = homeAnchors[index];

            // Parented and out of play FIRST - the block is in state 4 of CLAUDE.md §1.2 from this
            // line on, and only where it is DRAWN is still moving. That ordering is what makes the
            // slide safe to interrupt: an iteration ending mid-move takes the block back and the
            // coroutine simply stops (see SocketInsert).
            item.InsertInto(anchor);
            seated.Add(item.itemId);

            if (isActiveAndEnabled)
                StartCoroutine(SocketInsert.Slide(item, anchor, OfferedAt(anchor), insertTilt,
                                                  insertDuration, audioSource, insertClip));

            ShowStatus(acceptedColor);

            // Fired from here rather than polled, because this is the only place the answer can
            // change to true, and it fires exactly once - a seated block cannot be seated again.
            if (IsSolved && isActiveAndEnabled) StartCoroutine(Shrink());
            return true;
        }

        // Straight out of the middle of the cube, in the anchor's own frame. The anchors are
        // children of this transform at identity rotation, so an anchor's local position IS its
        // direction from the centre and the offer is the reverse of the way the block goes in.
        private Vector3 OfferedAt(Transform anchor)
        {
            Vector3 outward = anchor.localPosition;
            // A block whose place straddles the middle has no radius worth speaking of. There is
            // none in a 4x4x4 - every solved offset is at least half a cell out - but a smaller
            // cube would have one, and "no direction" must not come out as a zero-length offer that
            // makes the slide a no-op.
            return outward.sqrMagnitude < 1e-4f
                ? Vector3.up * insertOffer
                : outward.normalized * insertOffer;
        }

        // A CLICK THAT IS REFUSED HAS TO SAY SO. Called by `BedlamPlacer` when it aimed at the cube
        // with a block in hand and `CanSeat` said no - which is the only refusal a player can
        // provoke, and the one that would otherwise read as a broken button.
        public void ShowRefused() => ShowStatus(deniedColor);

        private void ShowStatus(Color color)
        {
            if (statusRenderer == null) return;
            statusRenderer.material.color = color;
            CancelInvoke(nameof(RestStatus));
            if (color != idleColor) Invoke(nameof(RestStatus), flashSeconds);
        }

        private void RestStatus()
        {
            if (statusRenderer != null) statusRenderer.material.color = idleColor;
        }

        // The top of an iteration, AFTER `ItemRegistry.ReturnAllToOrigin` - see CLAUDE.md §1.9. The
        // blocks are already back on the floor by the time this runs; all this has to do is forget
        // who was home and stand the seed back up.
        //
        // The shrink goes back with it, and that is the one piece of state here the item sweep
        // cannot reach: it moves the OBJECTS back and says nothing about one of them being 25% of
        // its size and the other one being out.
        public override void ResetCondition()
        {
            StopAllCoroutines();
            CancelInvoke(nameof(RestStatus));
            seated.Clear();
            if (assembled != null) assembled.localScale = Vector3.one;
            held?.Hide();
            SeatSeed();
            ShowStatus(idleColor);
        }

        // THE CUBE COMES DOWN TO A SIZE A HAND CAN CLOSE ROUND, and then it is a thing you can pick
        // up. Eased so it lets go slowly and then goes quickly - the same curve everything else in
        // this cycle moves on.
        //
        // **THE SWAP IS AT THE END AND AT THE MATCHING SIZE**, which is the whole of why it is not
        // seen. `held` is built to be exactly `heldScale` of the assembly, so the frame the big one
        // is hidden on is a frame where the small one is already the same size in the same place.
        private IEnumerator Shrink()
        {
            if (assembled == null) yield break;

            Vector3 from = Vector3.one;
            Vector3 to = Vector3.one * heldScale;

            for (float t = 0f; t < shrinkSeconds; t += Time.deltaTime)
            {
                float u = Mathf.Clamp01(t / shrinkSeconds);
                assembled.localScale = Vector3.Lerp(from, to, u * u);
                yield return null;
            }

            assembled.localScale = to;
            if (held == null) yield break;

            // Where the assembly stands, not where the small one was built: the two are siblings and
            // the shrink happens about the assembly's own pivot, so this is the one point they have
            // to agree on.
            held.RevealAt(assembled.position);
            // AND THE BIG ONE GOES, on the same frame. Scaled to nothing rather than deactivated,
            // because the thirteen blocks are its children and `ItemRegistry.ReturnAllToOrigin` has
            // to be able to reach them at the top of the next iteration - a deactivated subtree
            // unregisters everything under it.
            assembled.localScale = Vector3.zero;
        }

        private void SeatSeed()
        {
            if (pieces == null || seedPiece < 0 || seedPiece >= pieces.Length) return;
            Seat(pieces[seedPiece]);
            // Seating flashed the accept colour; the seed is not an achievement.
            CancelInvoke(nameof(RestStatus));
        }
    }
}
