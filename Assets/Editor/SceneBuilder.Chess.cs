using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace IterationRoom.EditorTools
{
    // ROOM2WEST: the board, the thirty-two pieces, and which twelve are scattered where.
    // `MakeChessPiece` is where the mirrored import is undone - half of `chess.glb` carries a local
    // scale of (-1,-1,-1), and pushing that onto a wrapper INSIDE the object is what keeps
    // colliders, hand poses and authored rotations honest (CLAUDE.md 3).
    //
    // Split out of `SceneBuilder.cs` (2026-09-02), which had reached 25,000 lines. One class,
    // many files - see the note over the `partial` keyword there. Everything private in any part
    // is reachable from every other part, so nothing about the build changed when this moved.
    public static partial class SceneBuilder
    {

        // The chess set in the red door's room, centred on its floor.
        //
        // The model is a SketchUp export by way of Collada, which is why it arrives at an odd import
        // scale with a 270 X rotation on its root and 218 separate renderers inside it. That count is
        // the thing to watch: it is the largest single object in the game by an order of magnitude, and
        // it sits in a room nothing can reach yet - so if the frame rate moves, this is the first
        // suspect.
        private static (CarryableItem[] pieces, ChessBoard board) BuildChessSet(
            Transform parent, Vector3 roomCentre, Vector3 doorwayInside, Material propMat,
            Light[] roomLights, Renderer[] roomPanels)
        {
            // addBoxCollider FALSE, unlike the bed and the nightstand. One box over the whole set would
            // be a 3.6m solid block, and a player who cannot walk up to the board cannot get their
            // capsule inside a piece's trigger - the pieces in the middle would be unreachable by E. The
            // board gets its own collider below and the pieces get triggers.
            (GameObject set, Bounds bounds) = PlaceModel($"{FurnitureDir}/chess.glb", parent, "ChessSet",
                new Vector3(roomCentre.x, 0f, roomCentre.z), 0f, ChessScale,
                addBoxCollider: false, rotation: Quaternion.Euler(-90f, 0f, 0f));

            // Logged like the key model's, and for the same reason: every number above is a measurement
            // off this file, and a re-export that changes its units should show up in the build log
            // rather than as a chessboard the size of a room.
            // UNPACKED, and it is the only model in the game that is. Unity lets components be ADDED
            // to a prefab instance but not the hierarchy RESTRUCTURED, and opening the board means
            // reparenting 77 of its children into two movers. The .glb stays the source of truth for
            // the geometry either way - the scene is build output, so nothing is lost by flattening it.
            PrefabUtility.UnpackPrefabInstance(set, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

            Debug.Log($"[SceneBuilder] Chess set size={bounds.size} (expects ~5.4 across)");

            // FINDING THE PIECES IN A MACHINE-GENERATED HIERARCHY. This is a SketchUp export by way of
            // Collada, so every node is called instance_N and nothing is named after what it is. The
            // pieces are found by SHAPE instead, which is the only thing in here that means anything:
            //
            // The group holding them is the node that spans the whole set on ALL THREE AXES and has the
            // most children. `SketchUp` and the scene group span it too but have one or two children;
            // the board spans it on X and Z with 77 tiles and is 0.06 TALL against the set's 0.79.
            //
            // Height is the whole discriminator and leaving it out is not hypothetical: the first version
            // tested X and Z only, the board won on child count, and the build reported 70 pieces from
            // its tiles instead of 32 from the group above it.
            Transform group = null;
            foreach (Transform t in set.GetComponentsInChildren<Transform>())
            {
                Renderer[] rs = t.GetComponentsInChildren<Renderer>();
                if (rs.Length == 0) continue;
                Bounds b = rs[0].bounds;
                for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
                if (b.size.x < bounds.size.x * 0.95f
                 || b.size.z < bounds.size.z * 0.95f
                 || b.size.y < bounds.size.y * 0.95f) continue;
                if (group == null || t.childCount > group.childCount) group = t;
            }

            if (group == null)
            {
                Debug.LogError("[SceneBuilder] chess.glb: no node spans the set - pieces not built. "
                             + "A re-export has changed its hierarchy.");
                return (new CarryableItem[0], null);
            }

            // Among that group's children, the BOARD is the one with a footprint - 3.60 wide and 0.06 tall
            // where a piece is 0.30 wide and up to 0.72. Half the set's width separates the two by a mile,
            // so the test needs no tuning.
            //
            // TWO PASSES, because the board is the LAST of the 33 children and every piece needs to know
            // how high its top is. One pass built all 32 pieces with a floor height of zero, so a dropped
            // piece would have rested on the floor through the board it came off.
            var pieceNodes = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<Transform, Bounds>>();
            float boardTop = 0f;
            Collider boardCollider = null;
            Bounds boardBounds = new Bounds(roomCentre, Vector3.zero);

            foreach (Transform child in group)
            {
                Renderer[] rs = child.GetComponentsInChildren<Renderer>();
                if (rs.Length == 0) continue;
                Bounds b = rs[0].bounds;
                for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);

                if (b.size.x > bounds.size.x * 0.5f)
                {
                    // The board. Solid, so pieces and players rest ON it rather than in it - and thin
                    // enough that the CharacterController's step offset walks straight over the edge.
                    child.gameObject.name = "Board";
                    BoxCollider slab = child.gameObject.AddComponent<BoxCollider>();
                    slab.center = child.InverseTransformPoint(b.center);
                    // ABS, because half this set is MIRRORED. The dark pieces carry a local scale of
                    // -1, and InverseTransformVector divides by the scale - so a size taken through it
                    // comes back negative and the collider is inside out. It bit the piece triggers
                    // rather than this one, but the correction belongs on both.
                    slab.size = Abs(child.InverseTransformVector(b.size));
                    boardTop = b.max.y;
                    boardBounds = b;
                    boardCollider = slab;
                    continue;
                }

                pieceNodes.Add(new System.Collections.Generic.KeyValuePair<Transform, Bounds>(child, b));
            }

            // THE GRID, MEASURED OFF THE OPENING POSITION rather than divided out of the board's
            // bounding box. The slab is 5.40m across but its playing area is 4.19m from the first file
            // to the eighth - the rest is the frame - so size/8 gives 0.675m cells where the real ones
            // are 0.598m, and by the far file that error is more than a whole square. It shows up in
            // play as "the piece does not land in the middle of a square".
            //
            // Measuring from the extremes is safe because an opening position fills the outer files
            // AND the outer ranks on both sides: the lowest and highest coordinate on each axis are the
            // first and eighth line of that axis, seven pitches apart. The two axes are averaged
            // because they are two measurements of one number.
            float minX = float.MaxValue, maxX = float.MinValue;
            float minZ = float.MaxValue, maxZ = float.MinValue;
            foreach (var node in pieceNodes)
            {
                Vector3 p = node.Key.position;
                minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x);
                minZ = Mathf.Min(minZ, p.z); maxZ = Mathf.Max(maxZ, p.z);
            }

            float pitch = ((maxX - minX) + (maxZ - minZ)) / (2f * (SquaresPerSide - 1));
            Vector3 gridCentre = new Vector3((minX + maxX) * 0.5f, boardTop, (minZ + maxZ) * 0.5f);

            // The board is NOT centred on where the set was placed - the model has the slab off-centre
            // inside its own bounds - so everything below works off the measured centre and not off
            // roomCentre.
            GameObject boardGO = new GameObject("ChessBoard");
            boardGO.transform.SetParent(parent, false);

            ChessBoard board = boardGO.AddComponent<ChessBoard>();
            board.boardCollider = boardCollider;
            board.squaresPerSide = SquaresPerSide;
            board.squarePitch = pitch;
            board.gridCentre = gridCentre;
            board.surfaceY = boardTop;

            // THE LIT SQUARE. A slab a shade inside a square, so the square's own edges still read
            // under it, and emissive so it is the brightest thing in a room whose lights are OUT
            // until the puzzle is finished - it has to be findable from the far side of a dark room,
            // which is where a player carrying a piece usually is.
            GameObject marker = Prim(PrimitiveType.Cube, "SquareMarker", boardGO.transform,
                gridCentre, new Vector3(pitch * 0.86f, 0.006f, pitch * 0.86f),
                MakeEmissiveMaterial("ChessSquareMarker", new Color(0.30f, 0.72f, 1f), 2.6f),
                removeCollider: true);
            marker.SetActive(false);
            board.marker = marker.transform;

            Sprite pieceIcon = ChessPieceIcon();
            var pieces = new System.Collections.Generic.List<CarryableItem>();
            var anchors = new System.Collections.Generic.List<Transform>();
            var startsHome = new System.Collections.Generic.List<bool>();

            // WHICH PIECES GO ON THE FLOOR, by a shuffle rather than by taking the first twelve. The
            // node order is the exporter's, which groups a colour and a rank together - the first
            // twelve would be one side's back row, and the room would read as one player's pieces
            // knocked over rather than as a set that has been disturbed.
            int[] order = new int[pieceNodes.Count];
            for (int i = 0; i < order.Length; i++) order[i] = i;
            System.Random rng = new System.Random(ChessScatterSeed);
            for (int i = order.Length - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (order[i], order[j]) = (order[j], order[i]);
            }

            var scattered = new System.Collections.Generic.HashSet<int>();
            for (int i = 0; i < Mathf.Min(ScatteredPieceCount, order.Length); i++) scattered.Add(order[i]);

            float boardHalfSpan = Mathf.Max(boardBounds.size.x, boardBounds.size.z) * 0.5f;
            var spots = new System.Collections.Generic.List<Vector3>();

            for (int i = 0; i < pieceNodes.Count; i++)
            {
                Transform piece = pieceNodes[i].Key;

                // CAPTURED BEFORE ANYTHING MOVES. The anchor is the opening pose and the tilt is the
                // way this piece stands; both are only true of the set as the model left it.
                Vector3 homeLocalPosition = piece.localPosition;
                Quaternion homeLocalRotation = piece.localRotation;
                float tiltX = piece.eulerAngles.x;

                pieces.Add(MakeChessPiece(piece, pieceNodes[i].Value, pieceIcon, tiltX));

                // The socket, as a SIBLING of the piece carrying the pose the piece had. See
                // ChessBoard.homeAnchors: an item is parented to its socket at local identity, so an
                // anchor built this way reproduces the model's own arrangement exactly - without
                // ChessBoard knowing that the set is scaled 0.3039, modelled Z-up, or point-mirrored
                // for the dark half.
                //
                // localScale ONE, not the piece's. CarryableItem.InsertInto restores the item's own
                // local scale on top of this, and a dark piece carries -1 there; copying it here would
                // multiply the two and un-mirror the piece.
                GameObject anchor = new GameObject("Home_" + piece.gameObject.name);
                anchor.transform.SetParent(piece.parent, false);
                anchor.transform.localPosition = homeLocalPosition;
                anchor.transform.localRotation = homeLocalRotation;
                anchor.transform.localScale = Vector3.one;
                anchors.Add(anchor.transform);

                startsHome.Add(!scattered.Contains(i));
                if (!scattered.Contains(i)) continue;

                Vector3 spot = ScatterSpot(rng, roomCentre, gridCentre, boardHalfSpan, doorwayInside, spots);
                spots.Add(spot);
                // The pivot of every piece sits at its base - on the board they stand at exactly the
                // slab's top - so y ZERO is a piece standing on the floor, not one sunk into it.
                piece.position = spot;
                // Turned where it fell. Without this the twelve stand in a shared direction and read
                // as a second, sparser grid rather than as pieces that have been knocked about.
                piece.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f) * piece.rotation;
            }

            board.pieces = pieces.ToArray();
            board.homeAnchors = anchors.ToArray();
            board.startsHome = startsHome.ToArray();

            // THE BOARD COMES APART. Split before the anchors are re-hung, so each anchor can be
            // handed to the half its square is on - a seated piece is parented to its anchor, so
            // that is the whole of how thirty-two pieces ride the halves out without anything
            // tracking them.
            (Transform halfWest, Transform halfEast) = SplitBoard(parent, boardCollider.transform, gridCentre.x);
            foreach (Transform a in anchors)
                a.SetParent(a.position.x < gridCentre.x ? halfWest : halfEast, true);

            // TAKEABLE NOW, where it was scenery. The final room's first recess names it, so the
            // thing this room pays out has somewhere to go for the first time.
            (Transform plinth, _, CarryableItem redCube) = BuildKeyPlinth(parent, "ChessPlinth",
                gridCentre, propMat, SlotShape.Square, new Color(0.85f, 0.10f, 0.10f), "ChessRewardKey",
                CubeKeyItemId, "CUBE", SquareIcon());

            ChessReward reward = boardGO.AddComponent<ChessReward>();
            reward.lights = roomLights;
            reward.fixturePanels = roomPanels;
            // AUTHORED, NOT LEFT TO THE COMPONENT'S DEFAULT. This is what the room's panels come back
            // UP to when the board is finished, so it has to be the same number the rest of the
            // building's fixtures are built at or this one room ends up brighter than every other.
            // See `CeilingPanelEmission`.
            reward.litPanelEmission =
                new Color(CeilingPanelEmission, CeilingPanelEmission, CeilingPanelEmission);
            reward.boardWest = halfWest;
            reward.boardEast = halfEast;
            reward.boardCollider = boardCollider;
            reward.plinth = plinth;
            reward.key = redCube;
            // 1.7 each way opens a 3.4m gap in a 5.4m board - wide enough that the plinth comes up
            // through clear floor rather than between two ledges - and leaves the far half 1.6m short
            // of the wall, which is where the room stops being able to give any more.
            reward.openTravel = 1.7f;
            board.reward = reward;
            // NO ResetNow() HERE, ON PURPOSE. AddComponent already ran Awake, before `plinth`/`key`
            // above were assigned - the same order RewardPlinth's own rooms are already built in,
            // and the reason "a scene whose one prop is invisible cannot be checked without pressing
            // Play" is written on that class rather than solved for it. Calling ResetNow (or
            // anything else that reads plinthUp/plinthDown) here would run it against the zeroed
            // values Awake's skipped capture left behind and BAKE a wrong position into the saved
            // scene - which is exactly what happened the first time this line existed: the plinth
            // landed at world (0,0,0), Room1, and stayed there even once Play mode's own fresh Awake
            // ran, because by then (0,0,0) was the authored position it captured FROM. Play mode's
            // own Awake, running against the fully-serialized scene, is what correctly captures the
            // closed/up state - same as it already does for Room2East and Room3.

            // EVERY PIECE ON A SQUARE OF ITS OWN, checked rather than assumed. Two pieces resolving to
            // one square is exactly what a mis-measured pitch looks like, and the symptom in play would
            // be a piece that cannot be put down with no indication why. Worth an error at build time.
            var occupied = new System.Collections.Generic.HashSet<int>();
            int offBoard = 0, clashes = 0;
            foreach (Transform a in anchors)
            {
                int square = board.SquareAt(a.position);
                if (square < 0) offBoard++;
                else if (!occupied.Add(square)) clashes++;
            }

            if (offBoard > 0 || clashes > 0)
                Debug.LogError($"[SceneBuilder] Chess grid mis-measured: {offBoard} piece(s) off the board, "
                             + $"{clashes} sharing a square. pitch={pitch:F4} centre={gridCentre}");

            // The red cube rides out with the pieces so it lands in the prompt list with them. It is
            // not a piece and the board knows nothing about it; this is the one list both belong to.
            pieces.Add(redCube);

            Debug.Log($"[SceneBuilder] Chess: {pieces.Count - 1} pieces (expects 32), {spots.Count} scattered, "
                    + $"square pitch {pitch:F3}, board top {boardTop:F3}");
            return (pieces.ToArray(), board);
        }

        // Cuts the imported board down its middle so the two halves can slide apart, and hands back
        // the movers they hang off.
        //
        // THE BOARD IS NOT ONE MESH, which is the only reason this is possible at all: the export is
        // 72 separate tiles, four frame rails and a base plate - 77 children. Sorting them by which
        // side of the centre line they sit on is a real split of the real board rather than a trick
        // played with two copies.
        //
        // Three of those children run the WHOLE width - the base plate and the two cross rails - so
        // no side owns them. Each is duplicated and each copy squashed to half width about its own
        // outer edge, which is exact for an axis-aligned slab and costs nothing on this model:
        // every material on the board is a FLAT COLOUR, so there is no texture to stretch.
        //
        // THE MOVERS ARE CHILDREN OF THE ROOM, not of the set. The set is scaled 0.3039 and rotated
        // -90 about X, so a metre of travel written inside it is neither a metre nor along X.
        //
        // No collider on either half. The slab is 0.1m - under the CharacterController's step
        // offset, so it was never something to walk into - and a moving solid that slides into a
        // standing player is a way to get wedged for no gain. The board's own collider stays for
        // aiming and `ChessReward` switches it off as the halves part.
        private static (Transform west, Transform east) SplitBoard(Transform room, Transform boardNode, float centreX)
        {
            GameObject west = new GameObject("BoardHalf_West");
            GameObject east = new GameObject("BoardHalf_East");
            west.transform.SetParent(room, false);
            east.transform.SetParent(room, false);

            // Copied out first: reparenting while iterating a Transform's children skips every other
            // entry, because the collection being walked is the thing being emptied.
            var children = new System.Collections.Generic.List<Transform>();
            foreach (Transform child in boardNode) children.Add(child);

            int split = 0;
            foreach (Transform child in children)
            {
                Renderer[] rs = child.GetComponentsInChildren<Renderer>();
                if (rs.Length == 0) continue;

                Bounds b = rs[0].bounds;
                for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);

                const float slack = 0.01f;
                if (b.max.x <= centreX + slack) { child.SetParent(west.transform, true); continue; }
                if (b.min.x >= centreX - slack) { child.SetParent(east.transform, true); continue; }

                // A straddler. The squash is done by a WRAPPER at world identity rather than by
                // scaling the child, so it stays a pure world-X scale whatever rotation the child
                // carries - a non-uniform scale applied through a rotated basis shears.
                GameObject twin = Object.Instantiate(child.gameObject, child.parent);
                twin.name = child.gameObject.name + "_E";
                child.gameObject.name = child.gameObject.name + "_W";

                float span = b.max.x - b.min.x;
                ClipHalf(child, west.transform, b.min.x, (centreX - b.min.x) / span);
                ClipHalf(twin.transform, east.transform, b.max.x, (b.max.x - centreX) / span);
                split++;
            }

            Debug.Log($"[SceneBuilder] Chess board split: {west.transform.childCount} west / "
                    + $"{east.transform.childCount} east, {split} part(s) cut in half");
            return (west.transform, east.transform);
        }

        // One straddling part reduced to the half on its pivot's side. The wrapper sits AT the edge
        // that must not move, so scaling about it walks the far edge in to the centre line.
        private static void ClipHalf(Transform child, Transform half, float pivotX, float scaleX)
        {
            GameObject clip = new GameObject(child.gameObject.name + "_Clip");
            clip.transform.SetParent(half, false);
            // After parenting, and while the scale is still one: this is the pivot the squash turns
            // about, so it has to be a world position rather than an offset in a scaled frame.
            clip.transform.position = new Vector3(pivotX, 0f, 0f);
            child.SetParent(clip.transform, true);
            clip.transform.localScale = new Vector3(scaleX, 1f, 1f);
        }

        // The reward, and the only thing in Room2West that is not chess: a plinth rises out of the
        // gap the board leaves, with a red cube on it.
        //
        // Authored RAISED and sunk at runtime by `ChessReward`, exactly like Room4's plinth - a scene
        // whose one prop is invisible cannot be checked without pressing Play. It stands at the
        // board's own centre, so while the board is shut it is under the slab as well as under the
        // floor and there is nothing to notice before it moves.
        // A plinth with one of the escape objects on it. Shared by every room that pays one out, so
        // the three rewards are the same fixture in three colours and three shapes - a player who has
        // seen one knows what the next one is the moment it comes up.
        //
        // `itemId` EMPTY means the object is scenery: it is built and it is lit, but nothing can pick
        // it up. That is the state Room2West's red cube is in - there is nowhere yet for it to go, and
        // a carryable with no destination sits in the HUD row claiming to matter.
        //
        // Returns the plinth, the seat the object rests on, and the object itself when it is takeable.
        private static (Transform plinth, Transform seat, CarryableItem item) BuildKeyPlinth(
            Transform parent, string name, Vector3 centre, Material propMat,
            SlotShape shape, Color accent, string materialName,
            string itemId = "", string displayName = "", Sprite icon = null)
        {
            const float bodyHeight = 0.85f;
            const float bodyWidth = 0.62f;
            const float plateProud = 0.035f;
            const float keySize = 0.30f;

            GameObject plinth = new GameObject(name);
            plinth.transform.SetParent(parent, false);
            plinth.transform.localPosition = new Vector3(centre.x, 0f, centre.z);

            Prim(PrimitiveType.Cube, "Body", plinth.transform,
                new Vector3(0f, bodyHeight / 2f, 0f),
                new Vector3(bodyWidth, bodyHeight, bodyWidth), propMat);

            // The dark inset the object stands on, borrowed from Room4's plinth for the same reason:
            // a pale block on a pale block is one block, and the seat is what says something is
            // MEANT to be there.
            Prim(PrimitiveType.Cube, "TopPlate", plinth.transform,
                new Vector3(0f, bodyHeight + plateProud / 2f, 0f),
                new Vector3(bodyWidth * 0.82f, plateProud, bodyWidth * 0.82f),
                MakeColorMaterial("ChessPlinthPlate", new Color(0.14f, 0.14f, 0.16f)),
                removeCollider: true);

            GameObject seat = new GameObject("Seat");
            seat.transform.SetParent(plinth.transform, false);
            seat.transform.localPosition = new Vector3(0f, bodyHeight + plateProud + keySize / 2f, 0f);

            // POLISHED METAL WITH A GLOW STILL IN IT, on a chamfered version of its own silhouette.
            //
            // These were flat emissive at 2.4 on raw Unity primitives, and the two faults compounded:
            // a matte surface has no highlight to catch, and a primitive has no geometry at its edges
            // for a highlight to run along even if it did. The result was three coloured blocks - the
            // least interesting objects in the game, which is the wrong thing for the three the whole
            // run is spent collecting.
            //
            // The emission is kept, at a fifth of what it was, because the ORIGINAL reason for it
            // stands: each of these arrives in the same second as the biggest lighting change its
            // room has, and Room2West is genuinely dark until that moment. It is also the floor that
            // makes the metal safe here - see MakePolishedMetalMaterial on why a pure metal in this
            // project renders black.
            GameObject key = ShapePrim(shape, "RewardKey", plinth.transform,
                seat.transform.localPosition, Vector3.one * keySize,
                MakePolishedMetalMaterial(materialName, accent, 0.40f), bevelled: true);

            // SCENERY GETS A SOLID BOX, a takeable gets a trigger, and it is never both. CarryableItem
            // takes whatever `GetComponent<Collider>()` hands back as its reach - so an object left
            // with the shape's own 0.30 collider AND a trigger is one you have to stand inside to
            // pick up, because the solid one was added first and wins the lookup.
            if (string.IsNullOrEmpty(itemId))
            {
                key.AddComponent<BoxCollider>();
                return (plinth.transform, seat.transform, null);
            }

            // Local units on a transform scaled to 0.30, so this is 1.02m across and 0.90 tall in the
            // world: an arm's length rather than the object's own size, so it is taken from standing
            // at the plinth instead of from inside it.
            //
            // WIDE ENOUGH TO CLEAR THE PEDESTAL FROM EVERY SIDE, not just the width Body itself needs.
            // Body is a solid 0.62m box (0.31 half-extent) and the player's CharacterController has a
            // 0.08 skin width, so walking straight into ANY face - not a diagonal - stops the player's
            // capsule 0.31 + 0.08 = 0.39m out. A trigger sized to only just clear Body's own half-extent
            // (0.33m, one number over) was closer than that stop distance, so a square approach could
            // never overlap it - only a corner-on approach, where the AABB test's two axes both shrink
            // at once, ever reached it. Measured by walking a CharacterController at this pedestal from
            // all four cardinal directions and a diagonal: the cardinals never overlapped, the diagonal
            // always did - which is exactly "picks up from one angle, not from the side." 0.51m half
            // extent clears the 0.39m stop distance with room to spare.
            BoxCollider trigger = key.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = new Vector3(3.4f, 3.0f, 3.4f);

            CarryableItem item = key.AddComponent<CarryableItem>();
            item.itemId = itemId;
            // **PUT DOWN, IT LEAVES THE PLINTH'S SUBTREE - AND NOTHING ELSE WOULD MAKE IT.**
            //
            // `CarryableItem.DropAt` reparents to `dropParent ?? originParent`, and this object is
            // AUTHORED as a child of the plinth, so with no drop home its origin IS the plinth. Set
            // the triangle down anywhere in the building and it came back under that transform -
            // where `RewardPlinth.OwnsKey` reads as true again, and the plinth sinking on the next
            // pad release calls `Hide()` on it.
            //
            // Play reported it as the yellow triangle disappearing after the structure closed. It is
            // the SECOND time this parentage has bitten: `RewardPlinth` was already rewritten once,
            // in 2026-08-29, to stop it hiding and teleporting an object it no longer held. That fix
            // made ownership a parentage test, which is right - and left the one path that puts the
            // object back under that parent without anybody carrying it.
            //
            // Set HERE rather than at the three call sites, because all three rewards come through
            // this method and a fourth would arrive with the same fault.
            item.dropParent = parent;
            item.displayName = displayName;
            item.icon = icon;
            item.iconTint = accent;
            // Its own half-height: the mesh is centred on its pivot, so this is what puts it ON a
            // floor rather than half through one. Only a ghost's timeline ending under it drops one.
            item.floorY = keySize / 2f;
            item.handLocalPosition = HandPoseFor(keySize);
            // Tipped TOWARD the eye, not away. A prism seen square-on is a rectangle; -25 about X
            // brings its top face into view, which is the face that says which shape this is. The
            // yaw follows, so it is a three-quarter view like the chess pieces'.
            item.handLocalEuler = new Vector3(-25f, 20f, 0f);
            // ITS OWN SIZE. The anchor is unscaled, so writing the object's world scale here is what
            // "held at true size" means - it used to be shrunk to 0.22 to fit the view, and the view
            // is what gives way now instead of the object.
            item.handLocalScale = key.transform.lossyScale;
            return (plinth.transform, seat.transform, item);
        }

        // A spot on the floor for a piece to be found on: inside the room, off the board, out of the
        // doorway, and not on top of another piece.
        //
        // Rejection sampling rather than a laid-out pattern, because the point is that the room looks
        // disturbed. The bail-out is not decoration: the free floor here is a RING around a 5.4m board
        // in an 8.75 x 10.5 room, which is roomy for a dozen pieces and would not be for forty, and a
        // build that hangs is worse than a floor that is a little crowded.
        //
        // The four margins are PARAMETERS, not constants, since 2026-08-13: the cube room's pieces are
        // nine times the chess set's, and "clear of a 0.24m piece" and "clear of a 2.16m cube" are not
        // the same number. Every default reproduces the chess room's original behaviour untouched.
        private static Vector3 ScatterSpot(System.Random rng, Vector3 roomCentre, Vector3 gridCentre,
                                           float boardHalfSpan, Vector3 doorwayInside,
                                           System.Collections.Generic.List<Vector3> taken,
                                           float wallMargin = 0.8f, float boardMargin = 0.35f,
                                           float doorClear = 1.6f, float apart = 0.62f)
        {
            // wallMargin: nothing jammed into a corner where it cannot be seen.
            // boardMargin: clear of the slab, so nothing looks like it is on it.
            // doorClear: the way in stays the way in.
            // apart: two pieces never share one E press.

            // Room2West is a room on the chain now, same orientation as every other: WIDTH along X,
            // DEPTH along Z.
            float halfX = RoomWidth / 2f - wallMargin;
            float halfZ = RoomDepth / 2f - wallMargin;
            float keepOut = boardHalfSpan + boardMargin;

            Vector3 fallback = roomCentre;
            for (int attempt = 0; attempt < 400; attempt++)
            {
                Vector3 p = new Vector3(
                    roomCentre.x + ((float)rng.NextDouble() * 2f - 1f) * halfX,
                    0f,
                    roomCentre.z + ((float)rng.NextDouble() * 2f - 1f) * halfZ);
                fallback = p;

                if (Mathf.Abs(p.x - gridCentre.x) < keepOut && Mathf.Abs(p.z - gridCentre.z) < keepOut) continue;

                Vector2 fromDoor = new Vector2(p.x - doorwayInside.x, p.z - doorwayInside.z);
                if (fromDoor.magnitude < doorClear) continue;

                bool crowded = false;
                foreach (Vector3 other in taken)
                    if ((other - p).sqrMagnitude < apart * apart) { crowded = true; break; }
                if (crowded) continue;

                return p;
            }

            return fallback;
        }

        // Componentwise absolute value. Needed because InverseTransformVector divides by the scale and
        // half this chess set is mirrored - see the note where the board's collider is built.
        private static Vector3 Abs(Vector3 v) =>
            new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

        // One piece, turned into something E can pick up. Everything here is the pin and the key over
        // again - trigger, CarryableItem, hand pose - and nothing about it is chess-specific except how
        // big it is and what it is called.
        //
        // ITS OWN ITEM ID, from its own node name. The alternative was one shared id, which would have
        // capped the player at one piece at a time for free (PlayerHand.carried is a set of ids) and let
        // ItemRegistry's pool hand a ghost any spare piece. That is right for pins, which are
        // interchangeable, and wrong here: a knight is not a bishop, and a puzzle about putting the right
        // piece on the right square needs the recording to name a piece and mean it.
        //
        // The node names are instance_N and say nothing about type, so the ids do not pretend to either.
        // Classifying them - which is a knight, which is a pawn - is the puzzle's job and is in TODO.md.
        //
        // `tiltX` is the piece's own measured world pitch, and it is the whole of what makes a dark
        // piece sit in the hand the same way up as a light one. See the note on handLocalEuler below.
        private static CarryableItem MakeChessPiece(Transform piece, Bounds bounds, Sprite icon, float tiltX)
        {
            piece.gameObject.name = "Piece_" + piece.name.Replace("instance_", string.Empty);

            // A MIRRORED PIECE'S MINUS ONE, MOVED OFF THE PIECE ITSELF. Half this set has a local
            // scale of (-1,-1,-1), and Unity warns "BoxCollider does not support negative scale" on
            // every load for each one. The warning is benign - it uses the absolute value, which for
            // a centred symmetric trigger is exactly right - but a permanent warning in the console
            // is a permanent warning, and the next person to read it will not know that.
            //
            // Pushing the -1 onto a wrapper INSIDE the piece leaves the composite untouched, because
            // the wrapper sits after the piece's own rotation and scale either way:
            //   before  P * R * S(-1) * child
            //   after   P * R * S(+1) * S(-1) * child
            // What changes is that the piece's own transform is now positively scaled - and it is
            // that transform the trigger, `handLocalScale` and the home anchor are all read off. The
            // rotation is untouched, so the measured tilt below still means what it meant.
            if (piece.localScale.x < 0f || piece.localScale.y < 0f || piece.localScale.z < 0f)
            {
                Vector3 mirrored = piece.localScale;

                GameObject wrapper = new GameObject("Mirror");
                wrapper.transform.SetParent(piece, false);

                var meshes = new System.Collections.Generic.List<Transform>();
                foreach (Transform c in piece) if (c != wrapper.transform) meshes.Add(c);
                // worldPositionStays FALSE: the wrapper is still identity at this point, so keeping
                // the local pose is what keeps the child exactly where it was in the piece's frame.
                foreach (Transform c in meshes) c.SetParent(wrapper.transform, false);

                wrapper.transform.localScale = mirrored;
                piece.localScale = Vector3.one;
            }

            // Sized to about one square (0.45m) rather than to the piece, so the reach is even across the
            // board instead of a king being easier to grab than a pawn. Triggers overlapping is fine and
            // expected: ControlHintDisplay prompts over the NEAREST one that wants it.
            BoxCollider trigger = piece.gameObject.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.center = piece.InverseTransformPoint(bounds.center);
            // ABS: the dark pieces have a local scale of -1, so this division comes back negative and
            // the trigger is built inside out.
            trigger.size = Abs(piece.InverseTransformVector(new Vector3(0.45f, Mathf.Max(0.45f, bounds.size.y), 0.45f)));

            CarryableItem item = piece.gameObject.AddComponent<CarryableItem>();
            item.itemId = piece.gameObject.name;
            item.displayName = "PIECE";
            item.icon = icon;
            // ZERO, i.e. the floor. floorY is where a piece rests when it is DROPPED, and the only
            // thing that drops one is a ghost's timeline running out under it - on the floor, mid-room.
            // Putting a piece back on the board is not a drop: it goes into its square's socket, which
            // carries its own height. This read boardTop while the pieces all started on the board, and
            // a piece let go of by a past self floated a centimetre over the floor.
            item.floorY = 0f;
            // Further out and lower than the key's hold. A king is 0.72m tall at this scale, and at the
            // key's 0.46m from the eye it would fill the view - which is the cost of the board being
            // twice the size it was. If it still reads as too big in hand, this is the number.
            // STOOD UP IN THE HAND, and the tilt is the whole of it. The set is modelled Z-up, so
            // inside a piece's own local frame the model's "up" is +Z. Attached to the hand anchor at
            // an identity-ish rotation that +Z lines up with the camera's FORWARD - the piece lies down
            // and comes at the eye end-on, which is what "too big and I cannot tell which piece it is"
            // was. A pitch of 270 turns local +Z back onto the parent's up.
            //
            // WHY IT IS MEASURED AND NOT WRITTEN AS -90. Half of this set is MIRRORED: the dark pieces
            // carry a local scale of (-1,-1,-1), and Unity decomposes that basis as a pitch of +90 with
            // a negative scale where the light ones are 270 with a positive one. Both stand up on the
            // board - the two cancel - but handLocalScale copies the sign of the scale, so handing both
            // colours the same -90 stood the light pieces up and the dark ones on their heads. Play
            // found it as "the black ones are held upside down". Taking the pitch off each piece's own
            // world rotation makes the pair cancel in the hand exactly as they do on the board.
            //
            // The yaw is applied AFTER the pitch (Unity's order is Z, X, then Y), so it spins the piece
            // about its own vertical rather than tipping it: a three-quarter view, where face-on a
            // bishop and a pawn are the same silhouette.
            // Sized from the piece's own board footprint, so a king is held further out than a pawn.
            // Its own measured height, so a king is held further out than a pawn. `bounds` is the
            // world-space extent the trigger above is already sized from.
            item.handLocalPosition = HandPoseFor(bounds.size.y);
            item.handLocalEuler = new Vector3(tiltX, HeldPieceYaw, 0f);
            // The same correction on a ghost's wrist, with no yaw: that anchor is tuned for the key and
            // hands a piece over lying along the forearm otherwise. A past self carrying a rook has to
            // read as carrying a rook - who is holding what is half of what makes ghosts legible.
            item.ghostLocalEuler = new Vector3(tiltX, 0f, 0f);
            // ITS BOARD SIZE, exactly. Not optional even for true-size holding: the hand anchor is
            // unscaled and the set is not, so this is the correction that stops a piece being handed
            // over at 1/0.3039 of its own size - a 3.5m king. What is gone is the extra 0.28 that
            // used to shrink it below board size on top of that.
            // The SIGN matters as much as the size - it is what keeps a mirrored piece mirrored.
            item.handLocalScale = piece.lossyScale;
            return item;
        }

        // A pawn in silhouette, standing in for every piece in the readout. One shape for all 32 rather
        // than six drawn shapes: the row is telling the player they are carrying A PIECE, and until the
        // puzzle needs types told apart, six silhouettes would be five more things to learn than the
        // readout is currently saying.
        private static Sprite ChessPieceIcon()
        {
            var icon = new IconCanvas(128);
            icon.Disc(new Vector2(0.5f, 0.735f), 0.135f);                        // head
            icon.Bar(new Vector2(0.5f, 0.575f), new Vector2(0.155f, 0.038f));    // collar
            icon.Shape(p =>
            {
                // Body: a waist that flares to the base, so it reads as turned wood rather than a peg.
                if (p.y < 0.20f || p.y > 0.56f) return false;
                float k = (p.y - 0.20f) / 0.36f;
                float half = Mathf.Lerp(0.185f, 0.072f, Mathf.Sqrt(k));
                return Mathf.Abs(p.x - 0.5f) <= half;
            });
            icon.Bar(new Vector2(0.5f, 0.175f), new Vector2(0.225f, 0.045f));    // base
            return SaveSprite(icon, "icon_chess_piece");
        }
    }
}
