using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace IterationRoom.EditorTools
{
    // THE LAST FIVE MINUTES, BUILT. The evaluation board on room3-2N's wall, the breach in the wall
    // beside it, the cable car that comes through it, and the outside of a building that until this
    // moment has never been seen from outside.
    //
    // Split out of `SceneBuilder.cs` because that file is 24,000 lines and none of this is touched by
    // anything in it. One class, two files; see the note on the `partial` keyword over there.
    //
    // **WHAT IS DERIVED HERE AND WHY IT MATTERS MORE THAN USUAL.** The ride's path is computed from
    // where the cycles actually are, not written down. Cycle 2 wanders twenty metres west and two
    // rooms south of where it starts and cycle 3 is placed by measurement off room2-0's centre - so
    // a hand-written flight path would be three numbers that have to be re-found every time a room
    // moves, and would be wrong silently, at the one moment in the game that cannot be replayed to
    // check. Everything below is measured off the world it flies through.
    public static partial class SceneBuilder
    {
        private const string CableCarModel = "Assets/ArtAssets/Facility/cable_car.glb";

        // The cabin's length. The real SC8 is a little over 4m nose to tail and this is a real SC8 -
        // the model carries its own proportions, so only one dimension has to be stated.
        // **ITS HEIGHT, NOT ITS LENGTH.** A gondola's longest side is its vertical - cabin plus
        // hanger arm - so this is the dimension `PlaceModelLocal` is given, and `StandUpright` is
        // what makes sure that dimension ends up pointing at the sky.
        private const float CableCarHeight = 4.2f;

        // The slab the player stands on inside the cabin. Thin: it sits on top of the model's own
        // floor, and a thick one would stand the player above it.
        private const float CabinFloorThickness = 0.12f;

        // HOW MUCH EMPTY SPACE THERE IS ROUND THE BUILDING before the shaft's own walls. It is the
        // number that decides whether the installation reads as vast or as a crate, and it is the
        // one value here most likely to want changing after somebody has ridden it.
        private const float ShaftInnerMargin = 30f;
        // Extra room either side of the cable, so the car is never scraping the shaft wall.
        private const float ShaftClearance = 14f;
        // How wide the well in the shaft's roof is - the way out, and the only place the sky shows.
        private const float ShaftWellSpan = 20f;
        // How far a cell block has to stay clear of the real building.
        //
        // **5m -> 2m (2026-09-01, by request: the racks should crowd the building, not stand off it).**
        // Two metres is the width of the service void between two of the real cycles, so the fake
        // cells sit against the played ones the way the played ones sit against each other. It only
        // has to be enough that nothing lands inside a room the player walked through, and the test
        // below is against the real bounds, so it is enough.
        // A hand's breadth. It used to hold the whole building ENVELOPE clear at 5m; it is now the
        // gap between a fake cell and a real room's outer face, and 0.4m is two wall build-ups - the
        // same distance two real rooms sit apart. See where it is used.
        private const float CellKeepOut = 0.4f;
        // A ceiling on the racks - a SAFETY VALVE now, not a shaping tool.
        //
        // **240 WAS SHAPING THE RACKS AND NOBODY MEANT IT TO.** The loop below runs x outer, z, then
        // y inner, so a cap does not thin the field out evenly - it fills whole columns from one
        // corner and then stops dead. At 240 that was twenty-two columns of a hundred and twenty-six:
        // three quarters of the shaft had no cells in it at all, and the ones it did have were all on
        // one side. What the racks look like was being decided by an arithmetic accident.
        //
        // The region and the pitch shape them now (see `BuildCellGrid`), and this is only here to
        // stop a future change to either from minting geometry without limit. Hitting it is an ERROR,
        // not a quiet truncation - a silently clipped field is exactly what this replaces.
        //
        // 3000 cubes on one shared material is instanced and costs almost nothing next to the
        // 766k-triangle cable car in the same frame.
        // 8000: comfortably past what the region actually produces, which is the point - the shape
        // has to come from the geometry and not from here. 3000 was hit on the first build after the
        // keep-out stopped excluding the whole envelope, and the assert below said so.
        private const int MaxCells = 8000;

        // **HOW FAR BEYOND THE WALL THE CAR WAITS, AND IT IS THE CABIN'S OWN HALF-WIDTH.** That
        // puts its near face on the wall plane: the breach opens straight into the cabin, its floor
        // continuous with the room's, and there is nothing between the two to stand on or fall from.
        //
        // It was 3.2m, which needed a walkway to cross - see the note where that walkway used to be.
        // Overlapping the opening by 20cm rather than merely touching it: the wall has its own
        // thickness, and a cabin that stops exactly at the wall plane leaves that thickness as a
        // slot in the floor at the one step the player has to take. Nothing to intersect - the
        // covers have slid away by the time the car is here.
        private const float CarStandoffFromWall = CarHalfWidth - 0.2f;

        // THE CABIN, MEASURED OFF THE MODEL AT ITS IMPORT SIZE (2.39 x 4.2 x 2.11 stood upright).
        //
        // Written here rather than measured at the point of use because the ride's PATH is built
        // before the car is - the mouth of it is where the car will rest, and that has to be known
        // first. `BuildCableCar` checks these against the model it actually loaded and fails the
        // build if they have drifted, which is what stops a written constant going quietly stale.
        private const float CarHalfWidth = 1.20f;
        private const float CarHalfDepth = 1.06f;

        // THE EXTERIOR LIGHTING'S THREE NUMBERS. Big and soft: these are lighting a building 164m
        // deep from outside, and the failure mode being fixed is one where there was no light at all.
        private const float ExteriorLightStandoff = 26f;
        private const float ExteriorLightPitch = 22f;
        private const float ExteriorLightRange = 130f;
        private const float ExteriorLightIntensity = 2.2f;

        // **HOW FAR UP ITS WALL THE REPORT IS PRINTED, TO ITS CENTRE - RAISED FROM 4.6 (2026-09-01,
        // by request: the bottom was cut off).**
        //
        // Two things were wrong and only one of them was the height. The body text overran its rect
        // and ran off the bottom of the face (see the note where the rect is sized); and the face
        // itself was centred low enough that its lower third was down among the player's feet. It is
        // 11.2m tall now and centred at 6.8, so it spans roughly 1.2m to 12.4m of a 16.2m room -
        // clear of the floor, and the whole of it above head height.
        // **CENTRED ON THE WALL** (2026-09-01, by request). Room3-2N is three storeys tall, so its
        // walls' middle is half of that - derived rather than typed, because the room's height is
        // `3 * RoomHeight` and a number written here would not follow it if that changed.
        //
        // At 11.2m tall the face then spans roughly 2.5m to 13.7m of a 16.2m wall, which is margin
        // top and bottom and the type sitting where the eye rests.
        // **LOWER THAN THE WALL'S MIDDLE** (2026-09-01, by request: still too high). The middle of a
        // three-storey wall is 8.1m up, and a report printed there is read by craning. The player
        // arrives on the floor of this room and stays on it, so the type belongs at the height they
        // are standing at - a storey up, not three.
        private const float BoardHeight = RoomHeight;
        // Clear of the panelling by more than the chamfer is deep, so the type never z-fights the
        // wall it is printed on.
        private const float WallStandoff = 0.12f;
        // 12m of a 17.5m wall, and 12 of the 21m ones. Big enough to read on landing, with margin
        // either side so it reads as printed ON the wall rather than as filling it.
        private const float BoardWorldWidth = 12f;
        private const float BoardAuthoredWidth = 1500f;
        // 1400px at the 12m/1500px this is authored to is 11.2m of wall.
        private const float BoardAuthoredHeight = 1400f;
        // 38pt over 21 lines is ~840px of body text - see where the rect is sized. It is 0.30m of
        // glyph on the wall, which is large type read from across a room rather than small type
        // read from in front of it.
        private const int BoardBodyPt = 38;
        // Near-black on a white wall - the building's own palette, and the same contrast every other
        // piece of signage in it uses.
        private static readonly Color BoardInk = new Color(0.07f, 0.07f, 0.09f, 1f);
        private static readonly Color BoardFadedInk = new Color(0.30f, 0.31f, 0.34f, 1f);

        // THE SHAFT'S STRUCTURE, and every one of these is deliberately larger than a room the
        // player has walked through. A gantry is 3m deep against a 5.4m storey; the building the
        // whole game happens inside is one cell in a rack of two hundred.
        private const float GantryPitch = 14f;
        private const float GantryDepth = 3f;
        private const float GantryStandoff = 16f;
        private const float StripDepth = 0.5f;
        private const float StackWidth = 4.5f;

        // How far below the platform the car starts its arrival. Far enough to be out of sight below
        // the breach when the wall opens, so it is seen to come up rather than seen to be waiting.
        private const float CarArrivalDrop = 24f;

        // How deep each face of the cabin's cage is. Generous - see the note where they are built.
        private const float CabinCageThickness = 0.5f;

        // How far the cabin's floor slab reaches back past its own doorway, toward the room. It is
        // collision only - nothing is drawn out there - so it costs nothing and removes the seam the
        // player has to cross. See where it is used.
        // The invisible floor across the breach - see `BuildBreachBlockers`. Far enough out to meet
        // the cabin's own slab with room to spare.
        private const float ApronReach = 4.5f;
        private const float ApronThickness = 0.4f;

        private const float CabinThresholdReach = 1.2f;
        // And how far below the room's floor that slab's top sits, so it can never be a step UP.
        private const float CabinThresholdDrop = 0.03f;

        // How tall the cabin's collision cage is. Head height plus a little - the roof is there to
        // stop a jump putting the player on top of a moving car, not to be noticed.
        private const float CabinWallHeight = 2.3f;

        // The haul rope. 90mm reads as a rope at ten metres and does not vanish at fifty.
        private const float CableThickness = 0.09f;

        // HOW FAR OUT FROM THE BUILDING THE CABLE RUNS, measured from the east face of the widest
        // thing in it. Far enough that a whole room fits in view, close enough that a past self
        // standing in one is a person rather than a speck.
        private const float ShaftStandoff = 11.5f;

        // How far above cycle 1's floor the ride ends. Cycle 1 is the top storey, so this is the
        // only daylight in the game.
        private const float SurfaceRise = 34f;

        // THE BREACH: three cells wide, three rows tall, in room3-2N's east wall. Three cells is
        // 5.25m and the cabin is 4.2 - a car that exactly filled its hole would be a car threading a
        // needle, and the wall is being torn open rather than fitted with a door.
        // **FOUR, NOT THREE** (2026-09-01, by request). Three cells centred on the wall put the
        // opening's edges at +-0.875m, and this wall's own panel boundaries are at 0, +-1.75, +-3.5 -
        // so the hole cut across the middle of a cell at each end and the covers' grid ran half a
        // cell out of step with the wall they sit in. An EVEN count lands the edges on +-3.5, which
        // is a boundary, and the two grids line up.
        //
        // It costs a wider hole: 7m against 5.25. `BuildBreachBlockers` fills whatever the cabin does
        // not, so the extra is closed off the same way the old extra was.
        private const int BreachCells = 4;
        private const int BreachRows = 3;

        // Where the hole sits along that wall: on the room's own centreline. The wall is 21m of
        // unbroken panelling with nothing else in it, so there is nothing to avoid and the middle is
        // where a player standing in the room is already looking.
        internal static Rect BreachCutout()
        {
            float half = BreachCells * GridCellWidth / 2f;
            return Rect.MinMaxRect(-half, 0f, half, BreachRows * GridCellHeight);
        }

        // Everything the ending needs, built and wired. Called from `Build` once cycle 3 exists.
        //
        // `cycleRoots` is every cycle's world root IN PLAY ORDER, cycle 4 included - the unfinished
        // cell is as much a part of the view as the three that were played.
        internal static EndingDeparture BuildEndingDeparture(
            Transform roomNorth, Transform cycleThreeRoot, Transform[] cycleRoots,
            Transform player, FirstPersonController controller, CameraShaker shaker,
            GhostReplayer ghostPrefab, Transform ghostParent,
            Material floorMat, Material panelMat, Material grooveMat)
        {
            if (roomNorth == null) return null;

            GameObject root = new GameObject("EndingDeparture");
            root.transform.SetParent(cycleThreeRoot, false);

            EvaluationBoard board = BuildEvaluationBoard(roomNorth, player);
            CycleExit breach = BuildBreach(roomNorth, panelMat, grooveMat);

            // **THE BREAK IS WHAT POWERS THE BOARD, AND THIS IS THE LINE THAT TELLS IT SO.**
            //
            // It was missing for one build and the failure is worth recording, because neither
            // symptom pointed at it. `FacilityFailure.board` was left null, so `PowerOn` was never
            // called, so `EvaluationBoard.powered` stayed false and the readout never started - a
            // blank panel. And `EndingDeparture` waits for the board to finish before opening the
            // wall, so the SECOND symptom was "the cable car never comes", four minutes later, in a
            // different room, from the same missing assignment.
            //
            // Found by hand rather than passed in, for the same reason room3-0's lights are: the
            // failure is built inside `AssembleCycleThree` and the board is built here, and threading
            // one through the other would be a parameter added to four signatures to carry a value
            // that can simply be looked up. The error below is what stops it going quiet again.
            FacilityFailure failure = cycleThreeRoot.GetComponentInChildren<FacilityFailure>(true);
            if (failure == null)
                Debug.LogError("[SceneBuilder] Cycle 3 has no FacilityFailure - the evaluation board "
                             + "will never power on and the ending will stall waiting for it.");
            else
                failure.board = board;

            // THE SHAFT AXIS. One X for the whole climb, taken off the furthest-east thing in the
            // building so the cable clears all of it - see `ShaftStandoff`.
            float shaftX = EastmostFace(cycleRoots) + ShaftStandoff;

            Vector3[] path = BuildCableCarPath(shaftX, roomNorth, cycleRoots);

            CableCarRide car = BuildCableCar(root.transform, path, player, controller);

            FacilityExterior exterior = root.AddComponent<FacilityExterior>();
            exterior.ghostPrefab = ghostPrefab;
            exterior.ghostParent = ghostParent;
            exterior.shaftLights = BuildShaftLights(root.transform, path, BuildingBounds(cycleRoots));
            exterior.sun = BuildExteriorSun(root.transform);
            exterior.endingSkybox = EndingSkybox();
            // The cable, as data rather than as a list of renderers - which wall each room loses is
            // worked out against it at runtime. See `FacilityExterior.cablePath`.
            exterior.cablePath = path;
            exterior.cellBlocks = BuildExterior(root.transform, cycleRoots,
                                               BuildingBounds(cycleRoots), path, shaftX);

            // THE ROPE, and the walkway out to it. Both are built with the car rather than with the
            // exterior because both are part of GETTING IN: the rope says the car is hung rather than
            // flying, and the gangway is the floor between the breach and the door. Neither is
            // hidden - the gangway is behind the wall covers until they slide, and the rope is
            // outside a wall nobody can see through until then.
            BuildCable(root.transform, path, car.cabinHeight * 0.98f);
            // The opening is wider than the car - see `BuildBreachBlockers`.
            BuildBreachBlockers(roomNorth, BreachCutout());

            EndingDeparture departure = root.AddComponent<EndingDeparture>();
            // WHEN THE PLAYER COUNTS AS HAVING COME DOWN. Room3-2N's floor plus a body's height: the
            // room below is a storey and a half down from room3-0, so this line is unambiguous, and
            // it is taken off the room rather than typed as a world Y that a moved room would break.
            departure.descentY = roomNorth.position.y + 2.2f;
            departure.shaftLid = BuildShaftLid(cycleThreeRoot);
            departure.board = board;
            departure.car = car;
            departure.exterior = exterior;
            departure.breach = breach;
            departure.cameraShaker = shaker;

            Debug.Log($"[SceneBuilder] Ending departure: shaft nominally x={shaftX:0.##}, "
                    + $"{path.Length} waypoints, {PathLength(path):0.#}m of ride at "
                    + $"{car.speed}m/s = {PathLength(path) / car.speed:0}s. The cutaway is counted at "
                    + "runtime - look for [FacilityExterior] in the player log.");

            return departure;
        }

        // **THE ROOM IS THE SCREEN.** The evaluation is printed on all four walls of room3-2N at
        // once (2026-09-01, by request), replacing a 9m monitor hung on one of them.
        //
        // WHY IT IS BETTER, and it is not only that it was asked for. The player arrives here by
        // dropping through a hole in the ceiling in the dark; which way they are facing when they
        // land is not something this sequence gets to choose. A monitor on the north wall is a
        // monitor half of them have their back to. Four walls means the facility is already talking
        // to them whichever way they turn, and turning round is not how you find out you were being
        // graded.
        //
        // It also puts the report where this building always puts what it has to say. Every wall in
        // here is a display already (`WallPanelDisplay`) - the ERROR wave uses these very surfaces -
        // so a report on the walls is the room speaking in its own voice rather than a screen being
        // wheeled in. The wave is held out of this room for exactly that reason; see
        // `AssembleCycleThree`.
        private static EvaluationBoard BuildEvaluationBoard(Transform roomNorth, Transform player)
        {
            const float bigWidth = 2f * RoomWidth;
            const float bigDepth = 2f * RoomDepth;

            GameObject go = new GameObject("EvaluationBoard");
            go.transform.SetParent(roomNorth, false);
            EvaluationBoard board = go.AddComponent<EvaluationBoard>();

            var bodies = new List<Text>();
            var verdicts = new List<Text>();
            var footers = new List<Text>();

            // The four faces, each turned so its canvas's +Z points INTO its own wall - the
            // convention every sign in this game follows and the one that has now been got wrong
            // twice (`docs/gotchas.md`). North is identity, south 180, west -90, east +90.
            Face(go.transform, "North", new Vector3(0f, BoardHeight, bigDepth / 2f - WallStandoff),
                 Quaternion.identity, bodies, verdicts, footers);
            Face(go.transform, "South", new Vector3(0f, BoardHeight, -bigDepth / 2f + WallStandoff),
                 Quaternion.Euler(0f, 180f, 0f), bodies, verdicts, footers);
            Face(go.transform, "West", new Vector3(-bigWidth / 2f + WallStandoff, BoardHeight, 0f),
                 Quaternion.Euler(0f, -90f, 0f), bodies, verdicts, footers);
            Face(go.transform, "East", new Vector3(bigWidth / 2f - WallStandoff, BoardHeight, 0f),
                 Quaternion.Euler(0f, 90f, 0f), bodies, verdicts, footers);

            board.bodies = bodies.ToArray();
            board.verdicts = verdicts.ToArray();
            board.footers = footers.ToArray();

            // NO PANEL BEHIND THE TYPE, and no light of its own. The type is printed straight onto
            // the wall the way the ERROR wave is - which is what makes it the building talking
            // rather than a monitor. `EvaluationBoard.SetLit` has nothing to drive and does nothing,
            // which is why `face` and `glow` are left null rather than being given something to hold.
            board.player = player;
            board.floorDrop = BoardHeight - 1f;
            board.standard = EvaluationStandard.Default;
            board.audioSource = MakeSource(go.transform, "BoardAudio", spatialBlend: 1f, volume: 0.55f);
            board.lineClip = LoadClip(SfxDir, "sfx_floor_button_press");
            board.verdictClip = LoadClip(SfxDir, "sfx_chime");

            // THE COLUMN WIDTH IS CHECKED, not hoped for. CLAUDE.md 3: a fixed-width label is about
            // `0.6 x fontSize x length` wide, and silent rewrapping has bitten this project twice.
            float widest = 0.6f * BoardBodyPt * board.columns;
            if (widest > BoardAuthoredWidth)
                Debug.LogError($"[SceneBuilder] Evaluation board: {board.columns} columns at "
                             + $"{BoardBodyPt}pt is ~{widest:0}px against a {BoardAuthoredWidth}px "
                             + "rect - the table will wrap. Drop the font size or the column count.");

            Debug.Log($"[SceneBuilder] Evaluation on all four walls of room3-2N: {bodies.Count} "
                    + $"face(s) at {BoardWorldWidth}m wide, {BoardBodyPt}pt over {board.columns} "
                    + "columns.");
            return board;
        }

        // One wall's worth of the report.
        private static void Face(Transform parent, string name, Vector3 at, Quaternion facing,
                                 List<Text> bodies, List<Text> verdicts, List<Text> footers)
        {
            Text body = null, verdict = null, footer = null;

            CanvasGroup group = MakeWallFace(parent, name, at, facing,
                f =>
                {
                    // **OPAQUE AND NEARLY WHITE, ON A WALL THAT IS ALREADY WHITE.** Reversed from
                    // the first version, which set pale blue type on a lit pale panel and was
                    // reported as unreadable. There is no panel now: the type is dark-on-white, the
                    // way every other piece of signage in this building is.
                    // **THE BODY RECT HAS TO HOLD THE WHOLE READOUT, AND IT DID NOT.**
                    //
                    // The report is twenty-one lines. At the old 58pt with 1.05 spacing that is
                    // ~1280px of text in a 760px rect - and `verticalOverflow` is `Overflow`, so it
                    // did not clip, it OVERRAN: the text kept drawing downward, through the verdict,
                    // through the footer and off the bottom of the canvas. Play saw the bottom of it
                    // cut off, which is what that is.
                    //
                    // Sized from the line count now rather than guessed: 21 lines at 38pt and 1.05
                    // spacing is ~840px, and the rect is 900. The three blocks below are laid out so
                    // none of them can reach the next, and the whole face is 1400px tall against the
                    // 1050 it was - which is also what raises it clear of the floor.
                    // **CENTRED ON THE FACE, WHICH IS CENTRED ON THE WALL** (2026-09-01, by
                    // request). The face was already at the wall's middle and the TEXT was not: an
                    // `UpperLeft` block anchored at +200 in a 1400-tall canvas starts near the top
                    // and runs down, so the printed report sat from about 6m to 13m up a 16m wall
                    // while the thing containing it was centred at 8.
                    //
                    // The report is twenty-one lines at 38pt, so it is about 840 units tall; anchored
                    // at -30 its own middle lands on the face's middle, which is the wall's.
                    body = MakeBoardLine(f, "Body", BoardBodyPt, BoardInk,
                                         new Vector2(0f, -30f), new Vector2(BoardAuthoredWidth, 900f),
                                         TextAnchor.UpperLeft);
                    verdict = MakeBoardLine(f, "Verdict", 130, BoardInk,
                                            new Vector2(0f, -350f), new Vector2(BoardAuthoredWidth, 190f),
                                            TextAnchor.MiddleCenter);
                    footer = MakeBoardLine(f, "Footer", 46, BoardFadedInk,
                                           new Vector2(0f, -560f), new Vector2(BoardAuthoredWidth, 100f),
                                           TextAnchor.MiddleCenter);
                },
                worldWidth: BoardWorldWidth, withPlate: false, authoredHeight: BoardAuthoredHeight);

            // Authored transparent like every wall face, and this one never fades in: its content
            // arriving IS its appearance, a line at a time.
            if (group != null) group.alpha = 1f;

            bodies.Add(body);
            verdicts.Add(verdict);
            footers.Add(footer);
        }

        // `MakeWallLine` with an alignment and a handle back. The board needs both: its table is left
        // aligned and its verdict is centred, and it has to hold the `Text` to write to it.
        private static Text MakeBoardLine(Transform parent, string name, int fontSize, Color colour,
                                          Vector2 at, Vector2 size, TextAnchor alignment)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);

            Text text = go.AddComponent<Text>();
            text.font = UIFont();
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = colour;
            text.text = string.Empty;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            // 1.05 rather than 1.0: at 52pt in a monospace face the rows touch, and a table of
            // twenty lines needs air between them more than it needs to be compact.
            text.lineSpacing = 1.05f;

            RectTransform rect = text.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = at;
            return text;
        }

        // THE WALL COMING APART. Three cover slabs filling the cutout, sliding aside and out of the
        // wall - the same two-part motion `CycleExit`'s lids make, and the same component driving it.
        //
        // **WHY `CycleExit` AND NOT A NEW COMPONENT.** Its header already makes this exact argument
        // for the floor hatches: the opening lands on the panel grid so it reads as the wall itself
        // coming apart rather than as a door, it opens once, is used once, and explicit state is the
        // honest shape for something an iteration does not rewind. Every word of that is true here.
        // What it is NOT given is `player`: the through-test is about falling down a hatch, and a
        // hole in a wall has nothing to fall through.
        private static CycleExit BuildBreach(Transform roomNorth, Material panelMat,
                                             Material grooveMat)
        {
            const float bigWidth = 2f * RoomWidth;

            Rect hole = BreachCutout();

            GameObject go = new GameObject("Breach");
            go.transform.SetParent(roomNorth, false);
            go.transform.localPosition = new Vector3(bigWidth / 2f, 0f, 0f);

            var covers = new List<Transform>();
            var offsets = new List<Vector3>();

            // ONE COVER PER CELL, AND EACH IS A REAL PANEL WALL RATHER THAN A SCALED CUBE.
            //
            // The first version made them `PrimitiveType.Cube` slabs, which is the thing CLAUDE.md 3
            // forbids in as many words: a wall panel in this building is a generated mesh at true
            // size with a 6mm chamfer baked into its rim, and a scaled box has no chamfer and no
            // grooves. It would have been three smooth patches in a grid wall for the whole of cycle
            // 3 - visible from the first minute in the room, long before anything opened.
            //
            // Built by the same `BuildPanelWall` the wall itself is, one cell wide and the full
            // height of the hole, so each cover IS wall until it moves. Its own overrun (`WallDepth`
            // at each end, the trap the same section warns about) is buried inside the panelling
            // either side of the hole, which is exactly where an overrun is supposed to end up.
            for (int i = 0; i < BreachCells; i++)
            {
                float z = hole.xMin + GridCellWidth * (i + 0.5f);

                GameObject pivot = new GameObject($"Cover_{i}");
                pivot.transform.SetParent(go.transform, false);
                pivot.transform.localPosition = new Vector3(0f, 0f, z);

                BuildPanelWall(pivot.transform, "Panels", Vector3.zero,
                    Vector3.forward, Vector3.left, GridCellWidth, grooveMat, panelMat,
                    Rect.zero, hole.height);

                covers.Add(pivot.transform);
                // Outward well past its own thickness and along the wall by more than its own width,
                // so no slab can be seen edge-on through the opening once it is clear. Alternating
                // fore and aft, and each one a little further and a little lower than the last: the
                // wall is being pushed through from the far side, not opened.
                float along = (i % 2 == 0 ? 1f : -1f) * (GridCellWidth * (BreachCells + 1f));
                offsets.Add(new Vector3(2.4f + 0.6f * i, -0.35f * i, along));
            }

            CycleExit exit = go.AddComponent<CycleExit>();
            exit.covers = covers.ToArray();
            exit.openOffsets = offsets.ToArray();
            exit.openDuration = 3.0f;
            exit.audioSource = MakeSource(go.transform, "BreachAudio", spatialBlend: 1f, volume: 1f);
            exit.openClip = LoadClip(SfxDir, "sfx_door_open");
            exit.sealClip = LoadClip(SfxDir, "sfx_power_down");
            return exit;
        }

        // THE CAR. Imported, doors split onto pivots of their own, and a seat put where a person
        // stands in it.
        private static CableCarRide BuildCableCar(Transform parent, Vector3[] path,
                                                  Transform player, FirstPersonController controller)
        {
            GameObject carRoot = new GameObject("CableCar");
            carRoot.transform.SetParent(parent, false);
            carRoot.transform.position = path.Length > 0 ? path[0] : parent.position;

            // **STOOD UP BY MEASUREMENT, NOT BY A WRITTEN ROTATION.** The model arrives lying on its
            // back - its own vertical is Z, which is what a glTF exported out of a Z-up tool does -
            // and the first build authored `Quaternion.identity` and got a cable car lying in the
            // corridor. This is the trap the ladder fell into twice (`docs/gotchas.md`: "Measure the
            // rotation off each object", "check all THREE axes"), so it is not written here either:
            // the longest side is found and turned to point at the sky.
            //
            // A gondola is the one shape where that is unambiguous. It is much taller than it is wide
            // once the hanger arm is counted, so "longest axis" and "up" are the same axis and the
            // measurement cannot pick the wrong one for this model.
            (GameObject model, Bounds raw) = PlaceModelLocal(CableCarModel, carRoot.transform,
                "Cabin", Vector3.zero, Quaternion.identity, CableCarHeight);

            CableCarRide car = carRoot.AddComponent<CableCarRide>();
            car.car = carRoot.transform;
            car.path = path;
            car.player = player;
            car.controller = controller;

            if (model == null) return car;

            model.transform.localRotation = StandUpright(raw.size) * model.transform.localRotation;
            Bounds box = MeasuredBounds(model);

            Debug.Log($"[SceneBuilder] Cable car as imported: {raw.size.x:0.##} x {raw.size.y:0.##} x "
                    + $"{raw.size.z:0.##}m; stood upright {box.size.x:0.##} x {box.size.y:0.##} x "
                    + $"{box.size.z:0.##}m (expects the tallest number in the middle).");

            car.doorLeft = SplitDoor(model.transform, "Left", "SC8_Door-L-");
            car.doorRight = SplitDoor(model.transform, "Right", "SC8_Door-R-");

            // AND TURNED SO THE DOORWAY FACES THE ROOM. See `CabinDoorYaw`, which is authored
            // rather than derived and says at length why.
            model.transform.localRotation = Quaternion.Euler(0f, CabinDoorYaw, 0f) * model.transform.localRotation;
            box = MeasuredBounds(model);

            // The door leaves' offsets, logged so the constant above can be re-checked against the
            // model rather than argued about. Both leaves on the same axis with opposite signs is a
            // two-leaf doorway; a large shared offset on one axis would be its outward normal.
            if (car.doorLeft != null && car.doorRight != null)
                Debug.Log("[SceneBuilder] Cable car doors, offset from the cabin centre: L="
                        + (MeasuredBounds(car.doorLeft.gameObject).center - box.center).ToString("0.00")
                        + " R="
                        + (MeasuredBounds(car.doorRight.gameObject).center - box.center).ToString("0.00")
                        // Two identical offsets means one sliding doorway rather than two opposed
                        // doors, and a horizontal component this small means the model cannot say
                        // which way it faces at all - which is why `CabinDoorYaw` is authored.
                        + $"; cabin yawed {CabinDoorYaw} deg.");

            // **THE CABIN IS MOVED ONTO THE PIVOT, AND THIS IS THE LINE THE WHOLE VEHICLE RESTS ON.**
            //
            // `PlaceModelLocal` centres a model's BOUNDS on the point it is given - which puts the
            // bounds where they were asked for and leaves the transform ORIGIN wherever the artist
            // left it, somewhere else entirely. Every rotation after that (`StandUpright`, the door
            // yaw) turns the model about that origin, so the bounds swing away from the point they
            // were centred on. The build measured the result at **2.05m**: the cabin was two metres
            // from the pivot everything else was authored against.
            //
            // What that produced is both of the faults play reported, from one cause: the collision
            // cage stood two metres from the cabin, so there was an invisible wall across the open
            // doorway AND an open hole where the cabin floor appeared to be. It is also why the car
            // did not line up with the breach it had docked at - the PATH positions the pivot.
            //
            // Recentred here rather than compensated for downstream, which is the same repair
            // `BuildLadder` makes and for the same reason: one correction at the source beats a
            // correction in every consumer, and the consumers here are the cage, the seat, the
            // boarding volume, the rope and the docking point.
            //
            // X and Z onto the pivot; Y so the cabin's FLOOR is at it, because a vehicle the player
            // stands in wants its floor where the seat is going.
            Vector3 offset = box.center - carRoot.transform.position;
            model.transform.position -= new Vector3(offset.x, box.min.y - carRoot.transform.position.y,
                                                    offset.z);
            box = MeasuredBounds(model);

            // THE GLASS. It is a separate mesh in the model and it is the most load-bearing material
            // in this scene: the player looks through it for the whole ride. Both halves of the
            // transparency rule, because alpha alone does nothing in URP.
            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>())
            {
                if (renderer == null || renderer.sharedMaterial == null) continue;
                if (!renderer.sharedMaterial.name.Contains("glass")) continue;
                // **TAKEN OUT ENTIRELY** (2026-09-01, by request: it is not clear enough). It had
                // already gone 0.22 -> 0.05 alpha and even that is a film over the one view the whole
                // ending exists to deliver.
                //
                // Nothing is lost by removing it: the cabin still reads as glazed because the
                // mullions, the door leaves and the frame are all opaque geometry, and a window is
                // read from its frame rather than from its pane. `MakeGlassMaterial` is no longer
                // called for the car, so `CableCarGlass.mat` is dead - see the licence note if it
                // ever comes back.
                renderer.enabled = false;
            }

            // **A FLOOR TO STAND ON, AND WITHOUT IT THE CAR CANNOT BE BOARDED AT ALL.**
            //
            // A `.glb` imports with no colliders - Unity's model importer leaves "Generate Colliders"
            // off - so every surface of this thing was scenery. Walking at it put the player straight
            // through the cabin and out of the building: there was nothing to stand on and nothing to
            // stop them. `CableCarRide.Inside` was never the problem; nobody could get into the
            // volume to be found in it.
            //
            // One box at the cabin's own footprint, not a mesh collider on 766,000 triangles.
            // **A FLOOR AND FOUR WALLS**, because the player walks around in here now rather than
            // being pinned to a seat (`CableCarRide.CarryPlayer`). A floor alone is a moving platform
            // with a sixty-metre drop off every edge of it.
            //
            // Invisible: the model already draws the cabin. These are the collision the model has
            // none of.
            GameObject cage = new GameObject("CabinCage");
            cage.transform.SetParent(carRoot.transform, false);

            // **AND IT IS BUILT AT THE PIVOT, WHICH IS NOW WHERE THE CABIN IS.** See the recentring
            // above - the two were 2.05m apart until it was added, and this assert is what stops
            // them drifting apart again silently.
            Vector3 cabin = carRoot.transform.InverseTransformPoint(box.center);
            if (new Vector2(cabin.x, cabin.z).magnitude > 0.05f)
                Debug.LogError($"[SceneBuilder] The cable car's cabin sits ({cabin.x:0.00}, "
                             + $"{cabin.z:0.00}) off its own pivot after recentring. The collision "
                             + "cage, the seat and the docking point are all authored at the pivot, "
                             + "so the car will have an invisible wall across its doorway and a hole "
                             + "in its floor. Check the recentring above.");

            float halfX = box.size.x * 0.41f;
            float halfZ = box.size.z * 0.41f;
            float wall = CabinWallHeight;
            float t = CabinCageThickness;

            // **THICK, AND THE THICKNESS IS THE POINT.** These were 0.12 and the player kept ending
            // up under the car. A `CharacterController` resolves against a collider by sweeping, and
            // the cabin is accelerating upward past a body that gravity is pulling down every frame
            // - a thin floor is a floor a fast enough relative motion steps straight over. Nothing
            // here is seen, so there is no cost to making it four times deeper than the gap it has
            // to close.
            //
            // The faces overlap at the corners rather than meeting, for the same reason every wall
            // in this building overruns its own length: two colliders that merely touch leave a seam
            // exactly where a sweep will find it.
            // **THE FLOOR REACHES BACK INTO THE ROOM AND SITS A HAIR BELOW IT.**
            //
            // Play could not walk into the car - only jump in - which is a step, and a step means the
            // two floors were not meeting the way the arithmetic says they do. Rather than keep
            // deriving where the millimetres went, the threshold is made impossible to catch on:
            // the slab runs `CabinThresholdReach` further toward the room than the cabin needs, so it
            // is unambiguously under the player before they leave the room floor, and its top sits
            // `CabinThresholdDrop` BELOW that floor so there is nothing to step up onto.
            //
            // A 3cm drop is well inside the controller's step offset in the other direction, so
            // walking out again is unaffected.
            Box(cage.transform, "Floor",
                new Vector3(-CabinThresholdReach / 2f, -t / 2f - CabinThresholdDrop, 0f),
                new Vector3(halfX * 2f + t + CabinThresholdReach, t, halfZ * 2f + t));
            Box(cage.transform, "Roof", new Vector3(0f, wall + t / 2f, 0f),
                new Vector3(halfX * 2f + t, t, halfZ * 2f + t));
            Box(cage.transform, "Wall_ZMin", new Vector3(0f, wall / 2f, -halfZ),
                new Vector3(halfX * 2f + t, wall + t, t));
            Box(cage.transform, "Wall_ZMax", new Vector3(0f, wall / 2f, halfZ),
                new Vector3(halfX * 2f + t, wall + t, t));

            // **BOTH X FACES OPEN TO BOARD AND SHUT TO TRAVEL.** Which of them the model's doorway
            // is actually on cannot be established from the model - see `CableCarRide.doorwayWalls`,
            // which carries the measurement and the argument. Opening both sidesteps the question;
            // shutting both the instant somebody is aboard is what makes that safe.
            car.doorwayWalls = new Collider[]
            {
                Box(cage.transform, "Wall_XMin", new Vector3(-halfX, wall / 2f, 0f),
                    new Vector3(t, wall + t, halfZ * 2f + t)),
                Box(cage.transform, "Wall_XMax_Door", new Vector3(halfX, wall / 2f, 0f),
                    new Vector3(t, wall + t, halfZ * 2f + t)),
            };

            // WHERE THE PLAYER STANDS. On that floor, in the middle.
            GameObject seat = new GameObject("Seat");
            seat.transform.SetParent(carRoot.transform, false);
            seat.transform.localPosition = new Vector3(0f, CabinFloorThickness, 0f);
            car.seat = seat.transform;
            // **THE CAGE'S INNER FACES, NOT A BOX ROUND THE CABIN.** It was 0.55 of the cabin's
            // full size, which is WIDER than the cabin - fine while the car waited three metres off
            // the wall, and the cause of a car that could not be boarded once it docked against one.
            // See CableCarRide.boardingHalfExtents, which carries the whole account.
            //
            // Half the cage's span less half its thickness is the inside face of it, and a further
            // 3cm keeps the test off the collider itself.
            car.boardingHalfExtents = new Vector3(
                halfX - CabinCageThickness / 2f - 0.03f,
                1.9f,
                halfZ - CabinCageThickness / 2f - 0.03f);

            car.audioSource = MakeSource(carRoot.transform, "CarAudio", spatialBlend: 1f, volume: 0.9f);
            // **THE CAR HAS NO VOICE OF ITS OWN YET, AND IT IS LEFT SILENT RATHER THAN BORROWED.**
            // There is no vehicle sound anywhere in this project - the closest clips are a door, a
            // loop's pull-in whoosh and an alarm - and a cable car dressed in a door sound is worse
            // than one that hums. The two that ARE honest are used: the doors get `sfx_door_open`,
            // which is what they are, and the departure gets `sfx_pull_in`, the one sound in the
            // library that means "you are being moved". See `TODO.md` for the two that are missing.
            car.doorClip = LoadClip(SfxDir, "sfx_door_open");
            car.departClip = LoadClip(SfxDir, "sfx_pull_in");

            // **THE TWO CONSTANTS THE PATH WAS BUILT FROM, CHECKED AGAINST THE MODEL THAT ACTUALLY
            // LOADED.** `CarHalfWidth` decides where the car docks and `CarHalfDepth` decides how
            // much of the breach has to be blocked off beside it, and both are needed before this
            // function runs. A model that is replaced or re-exported at a different size would leave
            // the car floating a foot off the wall with a gap nobody would think to look for.
            float measuredHalfWidth = box.size.x * 0.5f;
            float measuredHalfDepth = box.size.z * 0.5f;
            if (Mathf.Abs(measuredHalfWidth - CarHalfWidth) > 0.15f
             || Mathf.Abs(measuredHalfDepth - CarHalfDepth) > 0.15f)
                Debug.LogError($"[SceneBuilder] The cable car measures {measuredHalfWidth:0.00} x "
                             + $"{measuredHalfDepth:0.00} (half-extents) against the "
                             + $"{CarHalfWidth} x {CarHalfDepth} the breach and the docking point "
                             + "were built from. Update those two constants.");

            // **AND THE TWO FLOORS ARE CHECKED AGAINST EACH OTHER AT BUILD TIME.** The threshold
            // above makes a small mismatch harmless; this is what would catch a large one, which
            // would otherwise present as "the car cannot be boarded" with nothing to point at.
            float roomFloorY = path.Length > 0 ? path[0].y : carRoot.transform.position.y;
            if (Mathf.Abs(carRoot.transform.position.y - roomFloorY) > 0.05f)
                Debug.LogError($"[SceneBuilder] The cable car rests at y={carRoot.transform.position.y:0.00} "
                             + $"against a room floor at {roomFloorY:0.00}. Its floor and the room's "
                             + "have to be level or the doorway is a step.");

            car.cabinHeight = box.size.y;
            // IT RISES INTO THE PLATFORM along the rope, from below the room it is arriving at. See
            // the note on the field: the first version brought it in from -X, which is through the
            // wall it had just come through and out of the room it was arriving at.
            car.arriveFrom = new Vector3(0f, -CarArrivalDrop, 0f);
            return car;
        }

        // One face of the cabin's collision cage. A collider with no renderer - the model is what is
        // seen and this is what is solid.
        private static BoxCollider Box(Transform parent, string name, Vector3 at, Vector3 size)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = at;
            BoxCollider box = go.AddComponent<BoxCollider>();
            box.size = size;
            return box;
        }

        // The rotation that puts a model's longest side along +Y. Identity if it already is.
        private static Quaternion StandUpright(Vector3 size)
        {
            if (size.y >= size.x && size.y >= size.z) return Quaternion.identity;
            // Longest along Z: a quarter turn back about X brings +Z to +Y.
            if (size.z >= size.x) return Quaternion.Euler(-90f, 0f, 0f);
            // Longest along X: a quarter turn about Z brings +X to +Y.
            return Quaternion.Euler(0f, 0f, 90f);
        }

        // **WHICH WAY THE CABIN FACES IS AUTHORED, AND THAT IS A RETREAT FROM MEASURING IT.**
        //
        // It was derived: take the two door leaves, average their offset from the cabin centre, and
        // call that the doorway's outward normal. The derivation is sound and the model does not
        // support it. Measured off the glb, both leaves sit at x = +0.11 in a hull 1.74 wide and
        // differ only in y (+-0.08) - they are two leaves of one doorway sliding apart along y, and
        // they are modelled nearly closed and near the cabin's own centre. The signal left in that
        // average is 0.11 out of a half-width of 0.87, which is noise wearing the shape of an answer.
        // It came out backwards, which is what play reported.
        //
        // So this is a number somebody set by looking at it. That is worth stating plainly rather
        // than dressing up: CLAUDE.md's rule is to measure a rotation off the object instead of
        // writing it, and the rule assumes the measurement exists. Here it does not, and a constant
        // that is honest about being a constant is better than a derivation that is wrong.
        //
        // **ZERO IS THE FLIP OF WHAT WAS THERE.** The derived version turned the cabin 180 degrees;
        // play saw that and called the doors reversed, so this is the other answer. It is the whole
        // of the change - and note that it moves only what the player SEES, because the collision no
        // longer depends on it at all: both sides of the cage open to board (see
        // `CableCarRide.doorwayWalls`), so a wrong facing here is a cosmetic complaint rather than a
        // car that cannot be entered.
        //
        // **TO CHANGE IT**: add or subtract 180 and rebuild. Nothing else reads it.
        private const float CabinDoorYaw = 0f;

        // **~~THE GANGWAY~~ GONE, AND THE CAR DOCKS INSTEAD** (2026-09-01, by request).
        //
        // There was a walkway: the car waited 3.2m beyond the wall, and a slab bridged the gap so
        // the player could walk out to it. It was the wrong answer to the right question. A platform
        // outside a hole in a wall is a place to stand, and a place to stand at the top of a
        // sixteen-metre drop is a place to fall off - which is what play kept doing, and it is why
        // boarding never felt like boarding.
        //
        // The car comes to the wall instead. Its near face lands on the wall plane and its floor is
        // the room's floor, so the breach opens onto a cabin rather than onto a ledge: the player
        // walks out of room3-2N and is inside the vehicle in the same step. There is no platform to
        // fall off because there is no platform, and nothing to aim at because the doorway is the
        // hole they are already walking through.
        //
        // What fills the rest of the opening - the hole is three cells wide and the cabin is two
        // metres - is `BuildBreachBlockers`, below.

        // **THE LID THAT SEALS ROOM3-0'S FLOOR ONCE THE PLAYER IS BELOW IT.**
        //
        // Parked in the service void under that floor, where nothing can see it, and raised into the
        // hole by `EndingDeparture.SealTheShaft`. A slab rather than a panel wall: this is a floor
        // being filled in from underneath, so what the player sees from room3-2N is its underside -
        // which is a ceiling, and takes the ceiling's material.
        private static Transform BuildShaftLid(Transform cycleThreeRoot)
        {
            Transform roomZero = null;
            foreach (Transform t in cycleThreeRoot.GetComponentsInChildren<Transform>(true))
                if (t.name == "Room3_0") { roomZero = t; break; }

            if (roomZero == null)
            {
                Debug.LogWarning("[SceneBuilder] No Room3_0 to seal - its floor will stand open "
                               + "behind the player for the rest of the game.");
                return null;
            }

            Rect hole = LadderShaftHole(LadderShaftXZ.x - RoomZeroX, LadderShaftXZ.y - RoomZeroZ);

            // Exactly the hole, less a hair at each edge so its sides never share a plane with the
            // shaft's - CLAUDE.md 3: nothing may be exactly the size of the hole it sits in.
            GameObject lid = Prim(PrimitiveType.Cube, "ShaftLid", roomZero,
                new Vector3(hole.center.x, -ShaftLidPark, hole.center.y),
                new Vector3(hole.width - 0.04f, WallThickness, hole.height - 0.04f),
                CeilingMaterial(), removeCollider: true);

            // **NOT DRAWN UNTIL IT MOVES** (2026-09-01, by request: it is visible where it waits).
            // The service void it parks in is not as closed as it looked from the numbers - from
            // room3-2N you can see up into it through the shaft. `EndingDeparture.SealTheShaft`
            // switches the renderer on as it starts to rise, which is the frame it enters the hole.
            Renderer lidRenderer = lid.GetComponent<Renderer>();
            if (lidRenderer != null) lidRenderer.enabled = false;

            Debug.Log($"[SceneBuilder] Room3-0's shaft lid: {hole.width:0.##} x {hole.height:0.##}m "
                    + $"parked {ShaftLidPark:0.##}m under its floor, rising into the hole once the "
                    + "player is down in room3-2N.");
            return lid.transform;
        }

        // How far below room3-0's floor the lid waits. Deep enough to be inside the service void and
        // out of sight from either room; the rise in `EndingDeparture` covers it.
        private const float ShaftLidPark = 1.9f;

        // **THE ONLY WAY THROUGH THE BREACH IS INTO THE CAR.**
        //
        // The opening is 5.25m across and the cabin covers about 2.1 of it, so without these there
        // is a metre and a half of open wall either side of the vehicle with a drop behind it. They
        // are colliders and nothing else: the player sees the shaft through the gaps, which is worth
        // having, and cannot step into it.
        private static void BuildBreachBlockers(Transform roomNorth, Rect hole)
        {
            const float bigWidth = 2f * RoomWidth;

            // **AND A FLOOR ACROSS THE WHOLE OPENING.**
            //
            // Play could still only jump into the car after the cabin's own slab was extended back
            // toward the room, which says the gap is not at the cabin's edge - it is in the opening
            // itself, where the room's floor slab ends and there is nothing until the car. One
            // continuous surface at the room's own floor height removes the question rather than
            // answering it again.
            //
            // **IT IS COLLISION ONLY AND IT IS NOT THE OLD WALKWAY.** That was a visible white slab
            // hanging outside a hole in a wall, which is a place to stand and therefore a place to
            // fall off; this cannot be seen and cannot be reached except through the doorway, because
            // the blockers below close everything either side of the car.
            GameObject apron = new GameObject("BreachApron");
            apron.transform.SetParent(roomNorth, false);
            apron.transform.localPosition = new Vector3(
                bigWidth / 2f + ApronReach / 2f, -ApronThickness / 2f, 0f);
            apron.AddComponent<BoxCollider>().size =
                new Vector3(ApronReach, ApronThickness, hole.width);

            GameObject go = new GameObject("BreachBlockers");
            go.transform.SetParent(roomNorth, false);
            go.transform.localPosition = new Vector3(bigWidth / 2f, 0f, 0f);

            // **OVERLAPPING THE CABIN BY 15cm RATHER THAN MEETING IT.** Two colliders that merely
            // abut leave a seam, and a seam in the floor of a hole with a sixteen-metre drop behind
            // it is a way to fall through the wall the car came in by - which play found. The
            // overlap costs nothing: the doorway is on X and these narrow the opening only in Z,
            // leaving 1.8m of it against a cabin the player walks through 1.2m of.
            float inner = CarHalfDepth - 0.15f;
            float gap = hole.xMax - inner;
            if (gap <= 0.05f) return;

            foreach (int side in new[] { -1, 1 })
            {
                GameObject block = new GameObject($"Blocker_{(side < 0 ? "South" : "North")}");
                block.transform.SetParent(go.transform, false);
                block.transform.localPosition =
                    new Vector3(0f, hole.height / 2f, side * (inner + gap / 2f));

                BoxCollider box = block.AddComponent<BoxCollider>();
                // As deep as the wall it replaces, so a player pressed against it is stopped where
                // the wall used to be rather than a stride short of it.
                box.size = new Vector3(WallDepth, hole.height, gap);
            }
        }

        // **THE ROPE THE CAR HANGS ON**, drawn along the whole path.
        //
        // It was not there for one build and the car read as flying: a gondola with nothing above it
        // is a box moving through the air on its own. The rope is what makes the climb legible as a
        // haul rather than as a camera move, and it is the only thing in the shot that shows where
        // the ride is going before it gets there.
        //
        // One thin cylinder per leg of the path. Cheap, and it follows the weave for nothing, because
        // the weave IS the path.
        private static void BuildCable(Transform parent, Vector3[] path, float above)
        {
            if (path == null || path.Length < 2) return;

            Material cableMat = MakeColorMaterial("CableCarRope", new Color(0.16f, 0.17f, 0.19f));
            GameObject root = new GameObject("Cable");
            root.transform.SetParent(parent, false);

            for (int i = 1; i < path.Length; i++)
            {
                Vector3 a = path[i - 1] + Vector3.up * above;
                Vector3 b = path[i] + Vector3.up * above;
                float length = Vector3.Distance(a, b);
                if (length < 0.01f) continue;

                GameObject leg = Prim(PrimitiveType.Cylinder, $"Cable_{i}", root.transform,
                    Vector3.zero, Vector3.one, cableMat, removeCollider: true);
                leg.transform.position = (a + b) * 0.5f;
                // A Unity cylinder is two units tall on its own Y, so half the length is the scale
                // and the rotation is whatever takes +Y onto the leg.
                leg.transform.rotation = Quaternion.FromToRotation(Vector3.up, (b - a).normalized);
                leg.transform.localScale = new Vector3(CableThickness, length * 0.5f, CableThickness);
            }
        }

        // The door bodies and their glass are separate nodes in the glb, so each side is gathered
        // under one pivot and the pivot is what slides. Returns null if the model does not carry the
        // names - a car whose doors do not move is a car with a permanently open doorway, which is
        // survivable, and a null reference at runtime is not.
        private static Transform SplitDoor(Transform model, string side, string prefix)
        {
            var parts = new List<Transform>();
            foreach (Transform child in model.GetComponentsInChildren<Transform>())
                if (child.name.StartsWith(prefix)) parts.Add(child);

            if (parts.Count == 0)
            {
                Debug.LogWarning($"[SceneBuilder] Cable car has no '{prefix}*' nodes - that door will "
                               + "not move. Check the model's node names against `SplitDoor`.");
                return null;
            }

            GameObject pivot = new GameObject($"Door_{side}");
            pivot.transform.SetParent(model, false);
            // At the model's own origin, so the slide offset is in the cabin's frame and the two
            // doors are mirror images of one number.
            pivot.transform.localPosition = Vector3.zero;
            foreach (Transform part in parts)
                if (part != null && part.parent != pivot.transform) part.SetParent(pivot.transform, true);

            return pivot.transform;
        }

        // THE FLIGHT PATH, measured off the building rather than written down.
        //
        // It leaves the breach going east, then climbs past every cycle in reverse play order - 3,
        // then 2, then 1 - to daylight. **That direction is the whole point of the shot**: the player
        // spends the game descending, one hatch at a time, and leaves by going back up through all
        // of it in one continuous move.
        //
        // **THE CABLE WEAVES, AND IT HAS TO.** The first version ran the whole climb at one X, east
        // of everything - which is what a cable car does and which put the car 47m from cycle 1 and
        // 67m from the west end of cycle 2, where a past self is three pixels tall and the rooms they
        // are standing in are a smudge. The building is not a tower: cycle 2 wanders twenty metres
        // WEST of where cycle 1 sits, so a straight line cannot be close to all of it.
        //
        // So each waypoint sits off ITS OWN cycle's east face, and the run between two of them is a
        // diagonal. The car is on a haul rope in a shaft rather than strung between two pylons, and a
        // haul rope can turn - which is a cheap piece of fiction to buy the difference between
        // watching a building go by and being able to see who is in it.
        private static Vector3[] BuildCableCarPath(float shaftX, Transform roomNorth,
                                                   Transform[] cycleRoots)
        {
            const float bigWidth = 2f * RoomWidth;

            // The mouth of the breach, in the world. The car rests here with its doors open.
            Vector3 mouth = roomNorth.TransformPoint(
                new Vector3(bigWidth / 2f + CarStandoffFromWall, 0f, 0f));

            var path = new List<Vector3> { mouth };

            // Out to the cable, level. A car that started climbing the moment it left the wall would
            // pull away from the room it is leaving before the player has seen it.
            path.Add(new Vector3(shaftX, mouth.y + 1.2f, mouth.z));

            // ONE WAYPOINT PER CYCLE, at that cycle's own floor height, its own Z, and a standoff off
            // its OWN east wall. Reverse play order, so the climb passes the rooms in the order they
            // were solved, backwards.
            float lastX = shaftX;
            if (cycleRoots != null)
                for (int i = cycleRoots.Length - 1; i >= 0; i--)
                {
                    Transform cycle = cycleRoots[i];
                    // Cycle 4 is UNDER cycle 3 and the ride starts above it - the unfinished cell is
                    // something the player looks down at as they pull away, not a stop on the route.
                    if (cycle == null || cycle.position.y < mouth.y) continue;

                    lastX = EastmostFace(new[] { cycle }) + ShaftStandoff;
                    path.Add(new Vector3(lastX, cycle.position.y + RoomHeight * 0.6f, cycle.position.z));
                }

            // And out. Above the top storey, past the last of the structure, into whatever is up
            // there - which is the one thing in this game nobody has seen.
            Vector3 last = path[path.Count - 1];
            path.Add(new Vector3(lastX, last.y + SurfaceRise, last.z - 18f));

            return path.ToArray();
        }

        // The east face of the furthest-east thing in the building, so the cable clears all of it.
        private static float EastmostFace(Transform[] cycleRoots)
        {
            float east = 0f;
            bool any = false;
            if (cycleRoots != null)
                foreach (Transform cycle in cycleRoots)
                {
                    if (cycle == null) continue;
                    foreach (Renderer renderer in cycle.GetComponentsInChildren<Renderer>(true))
                    {
                        if (renderer == null) continue;
                        float x = renderer.bounds.max.x;
                        if (!any || x > east) { east = x; any = true; }
                    }
                }
            return any ? east : 0f;
        }

        // **THE ONLY LIGHT THE OUTSIDE OF THIS BUILDING HAS EVER HAD.**
        //
        // Worth saying at length, because the absence of it shipped once and did not look like a
        // lighting fault. Every light in the game is a spot set into a ceiling pointing down inside a
        // sealed room. Nothing has ever lit an exterior surface, because until this sequence there
        // was no vantage point outside a room - so the first ride was past a facility rendering
        // exactly as it should have: black, lit by the trilight ambient's ground band and nothing
        // else. `FacilityExterior` raises the ambient as well; these are what give it shape.
        //
        // ONE PER WAYPOINT PLUS A LADDER OF THEM UP THE SHAFT, so the building is lit along its whole
        // height rather than only where the car happens to be. All authored disabled and switched on
        // together at the reveal - see `FacilityExterior.LightTheOutside` for why they are no longer
        // staggered.
        private static Light[] BuildShaftLights(Transform parent, Vector3[] path, Bounds building)
        {
            GameObject root = new GameObject("ShaftLights");
            root.transform.SetParent(parent, false);

            var lights = new List<Light>();

            // The ladder: a column of big soft sources east of the building, from its floor to well
            // above its roof. Range is what matters far more than count here - the building is 164m
            // deep, so a light that does not reach is a light that is not there.
            float x = building.max.x + ExteriorLightStandoff;
            for (float y = building.min.y - 6f; y < building.max.y + 40f; y += ExteriorLightPitch)
                for (int i = 0; i < 3; i++)
                {
                    float z = Mathf.Lerp(building.min.z, building.max.z, i / 2f);
                    lights.Add(MakeExteriorLight(root.transform, $"East_{lights.Count}",
                                                 new Vector3(x, y, z)));
                }

            // And one riding above each waypoint, which is what lights the cabin's own surroundings
            // as it passes. Offset behind and above the car so the vehicle is never its own shadow.
            if (path != null)
                for (int i = 0; i < path.Length; i++)
                    lights.Add(MakeExteriorLight(root.transform, $"Path_{i}",
                                                 path[i] + new Vector3(7f, 9f, 0f)));

            Debug.Log($"[SceneBuilder] Exterior lighting: {lights.Count} point fixture(s) plus one "
                    + "directional, off at build and raised together at the reveal.");
            return lights.ToArray();
        }

        // **THE ONE LIGHT THAT MAKES THE BUILDING VISIBLE AT ALL.** See `FacilityExterior.sun` for
        // why a directional and not more points: the facility is lightmapped and its exterior faces
        // baked black, realtime light is the only thing that adds on top of that, and URP gives each
        // renderer only a few ADDITIONAL lights while every renderer gets the directional.
        //
        // Aimed down and across so the building has a lit face and a shaded one - straight down
        // would light the roofs of a thing being looked at from the side.
        private static Light BuildExteriorSun(Transform parent)
        {
            GameObject go = new GameObject("ExteriorSun");
            go.transform.SetParent(parent, false);
            go.transform.rotation = Quaternion.Euler(38f, -128f, 0f);

            Light sun = go.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.98f, 0.94f);
            // 1.35 -> 0.85 with the ambient (see `FacilityExterior.endingAmbient`). The two were
            // set together when nothing lit the outside at all, and together they were overexposing
            // every surface that faces the light while leaving the ones that face away black. Less of
            // both, and the ratio between them is what makes the building readable rather than either
            // one being large.
            sun.intensity = 0.85f;
            // **NO SHADOWS, AND IT IS A TRADE RATHER THAN AN OVERSIGHT.** Shadows would make the
            // cutaway read far better - a room with one wall off would have a lit floor and a dark
            // corner instead of a flat wash. They would also be cast by every renderer in four awake
            // cycles plus a 766k-triangle vehicle, in the heaviest frame this game has. Measure the
            // frame first; `TODO.md`.
            sun.shadows = LightShadows.None;
            sun.lightmapBakeType = LightmapBakeType.Realtime;
            sun.enabled = false;
            return sun;
        }

        private static Light MakeExteriorLight(Transform parent, string name, Vector3 at)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = at;

            Light light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = ExteriorLightRange;
            light.intensity = ExteriorLightIntensity;
            light.color = new Color(0.92f, 0.94f, 1f);
            // Never shadow-casting. Forty of these in the scene where every cycle is awake at once is
            // already the heaviest frame in the game; forty shadow maps would be the end of it.
            light.shadows = LightShadows.None;
            // Realtime, and off at build. This is scenery lighting for one minute at the end of the
            // game - baking it would put a lit exterior into the lightmap of a building nobody can
            // see the outside of for the other fifty-nine.
            light.lightmapBakeType = LightmapBakeType.Realtime;
            light.enabled = false;
            return light;
        }

        // **THE SKY THE OPEN TOP OF THE SHAFT SHOWS.**
        //
        // Without one it is Unity's default procedural sky, which play named exactly: "the editor's
        // default screen". It is a blue gradient with a sun in it at the end of a game that has never
        // been outdoors, in a shot whose whole subject is a windowless facility.
        //
        // A near-white field instead: no sun disk, almost no atmosphere, tinted to the same white the
        // building is. The reading is meant to be "there is nothing up there yet", not "it is a nice
        // day" - the surface is what the NEXT cycle would be about, and this ending only gets as far
        // as leaving.
        private static Material EndingSkybox()
        {
            string path = $"{MaterialsDir}/EndingSky.mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Skybox/Procedural"));
                AssetDatabase.CreateAsset(mat, path);
            }

            mat.SetFloat("_SunSize", 0f);
            // 0 is "None" on this shader's sun-disk enum. A sun in this sky would be a light source
            // the building is demonstrably not lit by.
            mat.SetFloat("_SunDisk", 0f);
            mat.SetColor("_SkyTint", new Color(0.86f, 0.88f, 0.92f));
            mat.SetColor("_GroundColor", new Color(0.80f, 0.82f, 0.86f));
            // Thin, so the gradient between the two is almost nothing and the field reads flat.
            mat.SetFloat("_AtmosphereThickness", 0.35f);
            mat.SetFloat("_Exposure", 1.15f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // **THE REST OF THE INSTALLATION, AND THE BOX IT ALL STANDS IN.**
        //
        // Two jobs, done together because they are the same measurement:
        //
        // 1. **NOTHING MAY BE SEEN OUTSIDE THE MAP.** Once the cutaway takes the ceilings and one
        //    wall off every room, the player is looking at the building from a place that has no
        //    world in it - and past the building there was, literally, nothing. A shaft is what turns
        //    "the level ends here" into "the level is inside something".
        //
        // 2. **THE ROOMS SHOULD READ AS REPEATED.** One cell block is a building; forty in a regular
        //    grid, most of them dark, is an INSTALLATION - and the player has just spent an hour
        //    inside one of the cells. Nothing says so out loud; the player rides past the evidence.
        //
        // **AND THE FIRST VERSION PUT HALF OF THEM INSIDE THE BUILDING.** The ranks were offsets from
        // the cable rather than from the architecture - `shaftX + {-46, -30, +30, +46}` at a shaft of
        // 46.8 - so two of the four columns landed at x = 0.8 and x = 16.8, which is cycle 1 and
        // cycle 3. They were built through the rooms the ride exists to show. Everything here is now
        // measured off `BuildingBounds`, and the cells are held OUT of that box by name.
        private static GameObject[] BuildExterior(Transform parent, Transform[] cycleRoots,
                                                  Bounds building, Vector3[] path, float cableX)
        {
            GameObject root = new GameObject("Exterior");
            root.transform.SetParent(parent, false);
            root.SetActive(false);

            // **WHITE, NOT BLACK** (2026-09-01, by request, and it is the same fault as the black
            // building). These were near-black on the reasoning that a shaft is a dark place - which
            // is a statement about a shaft that is LIT, and nothing out here was. A dark material
            // under no light is not moody, it is invisible, and the whole ride was happening inside
            // an unlit charcoal box.
            //
            // The building is white; so is what it stands in. What separates the two is that the
            // racks are a shade darker and further away, which is enough.
            Material shellMat = MakeColorMaterial("ShaftShell", new Color(0.78f, 0.79f, 0.82f));
            // 0.62 -> 0.80. The racks read grey beside a white building, and the difference was albedo:
            // a real room's panels are 0.85 and these were two thirds of that. They are the same
            // cells; what separates them from the played ones is distance and the fact that they are
            // shut, not that they are made of something darker.
            Material cellMat = MakeColorMaterial("CellBlockShell", new Color(0.80f, 0.81f, 0.84f));

            // THE SHAFT. A box round everything, faces turned inward - built as six slabs rather than
            // an inverted cube because a cube with a flipped normal is a mesh whose winding is
            // backwards, and this project has already lost a day to exactly that (`docs/gotchas.md`:
            // a wrongly wound mesh renders BLACK and reads as a lighting bug).
            //
            // Generous, because its whole job is to be far enough away to read as distance. The top
            // is deliberately LEFT OPEN: the ride ends by climbing out of it, and a lid over that is
            // a lid over the only daylight in the game.
            Bounds shaft = building;
            shaft.Expand(new Vector3(ShaftInnerMargin * 2f, ShaftInnerMargin, ShaftInnerMargin * 2f));
            // The path has to be inside it - the car flies east of everything, so the box is grown to
            // whatever the ride actually needs rather than assumed to contain it.
            foreach (Vector3 point in path ?? new Vector3[0]) shaft.Encapsulate(point);
            shaft.Expand(new Vector3(ShaftClearance * 2f, 0f, ShaftClearance * 2f));

            // ~~AN IMPORTED BACKDROP~~ BUILT, BELOW. See `BuildGantries` for why a sculpture cannot
            // do this job however large it is scaled.

            // **AND EVERY ONE OF THEM IS OVERSIZED BY THE THICKNESS OF THE OTHERS.**
            //
            // This is where the sky was coming in. Each wall sits 2m OUTSIDE the box, so a west wall
            // sized to `shaft.size.z` spans exactly min.z to max.z - and the south wall, which is
            // supposed to close that end, sits at min.z - 2 and starts at min.x. Neither of them
            // covers x < min.x AND z < min.z, so the four corners were 4m x 4m open slits running
            // the full 85m height of the shaft. From anywhere on the ride with a line through one,
            // that is a tall bright strip of skybox - which is exactly how it was reported, and it
            // survived building a roof because the roof was never the hole.
            //
            // `Overlap` is the wall thickness doubled: enough for every slab to run past the ones it
            // meets. Cheap, and it cannot be got wrong the way a corner post can.
            const float Overlap = 8f;

            Slab(root.transform, "Shaft_Floor", shellMat,
                 new Vector3(shaft.center.x, shaft.min.y - 2f, shaft.center.z),
                 new Vector3(shaft.size.x + Overlap, 4f, shaft.size.z + Overlap));
            Slab(root.transform, "Shaft_West", shellMat,
                 new Vector3(shaft.min.x - 2f, shaft.center.y, shaft.center.z),
                 new Vector3(4f, shaft.size.y, shaft.size.z + Overlap));
            Slab(root.transform, "Shaft_East", shellMat,
                 new Vector3(shaft.max.x + 2f, shaft.center.y, shaft.center.z),
                 new Vector3(4f, shaft.size.y, shaft.size.z + Overlap));
            Slab(root.transform, "Shaft_South", shellMat,
                 new Vector3(shaft.center.x, shaft.center.y, shaft.min.z - 2f),
                 new Vector3(shaft.size.x + Overlap, shaft.size.y, 4f));
            Slab(root.transform, "Shaft_North", shellMat,
                 new Vector3(shaft.center.x, shaft.center.y, shaft.max.z + 2f),
                 new Vector3(shaft.size.x + Overlap, shaft.size.y, 4f));

            // **AND A ROOF, WITH A HOLE ONLY WHERE THE RIDE GOES OUT** (2026-09-01, by request:
            // sky is visible in the distance).
            //
            // The top was left wide open because the ride climbs out of it. True of the last ten
            // seconds and wrong for the other fifty: from down in the shaft the open top is a bright
            // patch a long way off, and a facility with a skylight is not a facility.
            //
            // **THE FIRST ATTEMPT LEFT A GAP AND THAT IS WORTH RECORDING.** It centred the four
            // slabs on the CABLE and sized them from the shaft's half-width, which only covers the
            // shaft if the two share a centre - and they do not, because the cable runs up the east
            // side. One side came up short by exactly the offset between them, so the sky moved
            // rather than went. Each slab is measured between the shaft's own edge and the well now,
            // so neither assumption is being made.
            Vector3 exit = path != null && path.Length > 0 ? path[path.Length - 1] : shaft.center;
            float wellHalf = ShaftWellSpan / 2f;
            float roofY = shaft.max.y + 2f;

            void RoofSlab(string name, float x0, float x1, float z0, float z1)
            {
                if (x1 - x0 < 0.5f || z1 - z0 < 0.5f) return;
                Slab(root.transform, name, shellMat,
                     new Vector3((x0 + x1) / 2f, roofY, (z0 + z1) / 2f),
                     new Vector3(x1 - x0, 4f, z1 - z0));

                // **AND IT CASTS NOTHING.** This is the one thing between the sun and everything in
                // the shaft, so a shadow-casting lid would put the whole exterior - racks, building,
                // cable and all - into shade, and the fix for a bright patch of sky would be a black
                // ending. It exists to stop the eye leaving, not to stop the light arriving.
                Transform t = root.transform.Find(name);
                Renderer r = t != null ? t.GetComponent<Renderer>() : null;
                if (r != null) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            // West and east of the well, full depth; then north and south of it, only as wide as the
            // well so the four do not overlap into the opening.
            // Out to 4m PAST the walls, not to the middle of them - same reason as the overlap
            // above, and the roof/wall join is a seam the eye is looking straight up at.
            RoofSlab("Shaft_Roof_West", shaft.min.x - 4f, exit.x - wellHalf, shaft.min.z - 4f, shaft.max.z + 4f);
            RoofSlab("Shaft_Roof_East", exit.x + wellHalf, shaft.max.x + 4f, shaft.min.z - 4f, shaft.max.z + 4f);
            RoofSlab("Shaft_Roof_South", exit.x - wellHalf, exit.x + wellHalf, shaft.min.z - 4f, exit.z - wellHalf);
            RoofSlab("Shaft_Roof_North", exit.x - wellHalf, exit.x + wellHalf, exit.z + wellHalf, shaft.max.z + 4f);

            Debug.Log($"[SceneBuilder] Shaft roof at y={roofY:0.#}, with a {ShaftWellSpan}m well at "
                    + $"({exit.x:0.#}, {exit.z:0.#}) where the ride leaves. Everything else is closed.");

            int cells = BuildCellGrid(root.transform, cycleRoots, building, shaft, cableX, cellMat);
            BuildGantries(root.transform, shaft, building, shellMat);

            Debug.Log($"[SceneBuilder] Exterior: shaft {shaft.size.x:0.#} x {shaft.size.y:0.#} x "
                    + $"{shaft.size.z:0.#}m round a building of {building.size.x:0.#} x "
                    + $"{building.size.y:0.#} x {building.size.z:0.#}m, {cells} cells in the racks. "
                    + "Roofed, bar the well the ride climbs out through.");

            return new[] { root };
        }

        // **THE BACKDROP IS BUILT, NOT IMPORTED** (2026-09-01, by request).
        //
        // It was `sci-fi_environment_in_eevee.glb` for one build - abstract greeble, 578k triangles,
        // scaled to 265m. Play's verdict was that it did not look good, and the reason is worth
        // keeping: it is a SCULPTURE. It has a composition, a middle, and shapes that read as
        // individual objects, so blown up to 265m it looks like a prop that has been blown up. What
        // this shot wants is the opposite of a composition - it wants a thing with no middle and no
        // edge, that repeats until it stops being countable.
        //
        // WHAT IS BEING BUILT, and each piece is doing one job:
        //
        //   - **RACKS** (`BuildCellGrid`) - the cells, on a regular pitch. The repetition.
        //   - **GANTRIES** - beams spanning the whole shaft at fixed heights, passing behind and in
        //     front of everything. They are what give the racks a floor to stand on and a ceiling to
        //     stop at, so the eye reads storeys rather than a wall of boxes.
        //   - **SERVICE STACKS** - tall thin columns between the ranks, on the same pitch. Vertical
        //     rhythm against the gantries' horizontal one, which is the whole of what makes an
        //     industrial interior read as one.
        //   - **STRIP LIGHTS** - long thin emissive runs along the gantries. A laboratory is a place
        //     that is lit evenly and completely, and the strips are the only thing here that says
        //     somebody maintains it.
        //
        // **AND IT IS ALL TOO BIG ON PURPOSE.** A gantry is 3m deep - half the height of a room the
        // player spent an hour in - and there are dozens of them. The building the whole game
        // happens inside is one cell in a rack of two hundred. That discomfort is the point.
        //
        // Cheap: every piece is a `PrimitiveType.Cube` on a shared material, in an object that is
        // inactive until the reveal.
        private static void BuildGantries(Transform parent, Bounds shaft, Bounds building,
                                          Material shellMat)
        {
            GameObject root = new GameObject("Gantries");
            root.transform.SetParent(parent, false);

            Material stripMat = MakeEmissiveMaterial("ShaftStrip", new Color(0.95f, 0.97f, 1f), 2.4f);

            int beams = 0, stacks = 0;

            // THE GANTRIES. Every storey, across the shaft's full width, on both sides of the
            // building - deliberately NOT through it.
            for (float y = shaft.min.y + GantryPitch; y < shaft.max.y; y += GantryPitch)
            {
                foreach (float z in new[] { building.min.z - GantryStandoff,
                                            building.max.z + GantryStandoff })
                {
                    GameObject beam = Prim(PrimitiveType.Cube, $"Gantry_{beams}", root.transform,
                        Vector3.zero, new Vector3(shaft.size.x * 0.92f, GantryDepth, GantryDepth),
                        shellMat, removeCollider: true);
                    beam.transform.position = new Vector3(shaft.center.x, y, z);

                    // The strip runs along its underside, which is where a light in a ceiling grid
                    // goes and where it can be seen from below.
                    GameObject strip = Prim(PrimitiveType.Cube, $"Strip_{beams}", root.transform,
                        Vector3.zero,
                        new Vector3(shaft.size.x * 0.88f, StripDepth, GantryDepth * 0.45f),
                        stripMat, removeCollider: true);
                    strip.transform.position = new Vector3(shaft.center.x,
                                                           y - GantryDepth / 2f - StripDepth / 2f, z);
                    beams++;
                }
            }

            // THE SERVICE STACKS. Columns from the shaft floor to its top, on the racks' own pitch
            // so the two grids agree rather than beating against each other.
            float pitch = RoomWidth * 1.5f;
            for (float x = shaft.min.x + pitch; x < shaft.max.x; x += pitch)
                foreach (float z in new[] { building.min.z - GantryStandoff * 1.6f,
                                            building.max.z + GantryStandoff * 1.6f })
                {
                    // Not through the ride.
                    if (Mathf.Abs(x - shaft.center.x) < RoomWidth) continue;

                    GameObject stack = Prim(PrimitiveType.Cube, $"Stack_{stacks++}", root.transform,
                        Vector3.zero,
                        new Vector3(StackWidth, shaft.size.y * 0.98f, StackWidth),
                        shellMat, removeCollider: true);
                    stack.transform.position = new Vector3(x, shaft.center.y, z);
                }

            Debug.Log($"[SceneBuilder] Shaft structure: {beams} gantry beam(s) on a {GantryPitch}m "
                    + $"pitch and {stacks} service stack(s). Everything is a cube; nothing is a "
                    + "sculpture.");
        }

        // **THE RACKS. Identical cells, on a regular pitch, going away in every direction.**
        //
        // The repetition is the whole point and so is the regularity: a scatter reads as rubble and a
        // grid reads as storage. They are plain boxes because they are seen from thirty metres for
        // twenty seconds, half of them unlit, in the one scene where every cycle in the game is awake
        // at once - anything more detailed is geometry nobody can resolve at a cost that lands
        // exactly where the frame budget is worst.
        //
        // **HELD OUT OF THE BUILDING BY TEST, not by a chosen offset.** Every candidate is checked
        // against the real bounds before it is built, so a room that moves cannot end up with a cell
        // block inside it - which is the failure this replaces.
        private static int BuildCellGrid(Transform parent, Transform[] cycleRoots, Bounds building,
                                         Bounds shaft, float cableX, Material cellMat)
        {
            GameObject racks = new GameObject("CellRacks");
            racks.transform.SetParent(parent, false);

            // One cell is about the size of a room in the game, which is what makes the reading
            // work: the player recognises the scale before they work out what they are looking at.
            Vector3 cell = new Vector3(RoomWidth, RoomHeight, RoomDepth);

            // **~~A GAP OF HALF A CELL~~ A DIVIDER, 2026-09-01, and this reverses a stated choice.**
            //
            // It read: *"a gap of half a cell, so the rack reads as separate rooms rather than as a
            // solid wall with lines on it"*. That was a decision, and the request is the other one:
            // the racks should read as CRAMMED - cells packed against cells, the way the played
            // cycles already are.
            //
            // The vertical pitch was never the problem and does not move: `RoomHeight` plus the
            // service void is exactly `StoreyDrop`, so a rack already had the same storey spacing as
            // the cycles the player walked through. It was the two HORIZONTAL ones - gaps of 4.4m and
            // 6.3m - that made the field read as separate towers standing apart while the real rooms
            // share their walls. Now all three match the building.
            //
            // Two rooms in here are `2 * WallThickness` apart. So are two cells.
            Vector3 pitch = cell + new Vector3(WallThickness * 2f,
                                               ServiceVoid + WallThickness * 2f,
                                               WallThickness * 2f);

            // The building's own envelope, plus the margin the cells have to stay clear of. Anything
            // whose footprint touches this is skipped.
            // **WHAT THE CELLS HAVE TO AVOID IS THE ROOMS, NOT THE BOUNDING BOX ROUND THEM**
            // (2026-09-01, by request: *"the rooms I walked through should have countless rooms right
            // beside them and right above them"*).
            //
            // The whole envelope was being held clear, and that envelope is 40 x 44 x 164m of mostly
            // EMPTY SPACE - cycle 2 wanders twenty metres west and two rooms south, so its bounding
            // box contains far more air than building. Excluding it put the nearest fake cell a long
            // way from the nearest real room, and the racks read as a separate structure standing
            // near the player's one rather than as the same lattice continuing.
            //
            // Tested against each room's own bounds instead, so cells fill the gaps BETWEEN the real
            // rooms as well as the space around them. The result is what was asked for: walk out of
            // a cell and the cells above it, below it and either side of it are the same cell.
            var rooms = new List<Bounds>();
            foreach (Transform cycle in cycleRoots ?? new Transform[0])
            {
                if (cycle == null) continue;
                foreach (Transform t in cycle.GetComponentsInChildren<Transform>(true))
                {
                    if (t == null || !t.name.StartsWith("Room") || t.name.EndsWith("_Root")) continue;
                    Bounds b = MeasuredBounds(t.gameObject);
                    if (b.size.sqrMagnitude > 1f) rooms.Add(b);
                }
            }

            // A hand's breadth, so a cell may touch a room's outer face but never share space with
            // it. `CellKeepOut` is what that is; it is no longer holding the whole envelope clear.
            Vector3 clearance = Vector3.one * CellKeepOut;

            int built = 0;
            int index = 0;
            bool capped = false;

            // **THE GRID IS ANCHORED TO THE BUILDING, NOT TO THE SHAFT.** It ran from the shaft's
            // own corner, which is an arbitrary phase - so a cell landed wherever the arithmetic put
            // it and the two lattices were simply offset from each other. Started from the building's
            // centre and stepped outward, a cell sits where the NEXT ROOM ALONG would be, which is
            // the whole of what makes it read as one lattice rather than two.
            Vector3 origin = building.center;
            int nx = Mathf.CeilToInt((shaft.size.x * 0.5f) / pitch.x);
            int nz = Mathf.CeilToInt((shaft.size.z * 0.5f) / pitch.z);
            int ny = Mathf.CeilToInt((shaft.size.y * 0.5f) / pitch.y);

            for (int ix = -nx; ix <= nx; ix++)
                for (int iz = -nz; iz <= nz; iz++)
                    for (int iy = -ny; iy <= ny; iy++)
                    {
                        index++;
                        var at = origin + new Vector3(ix * pitch.x, iy * pitch.y, iz * pitch.z);

                        // Not through a room the player has walked in.
                        var box = new Bounds(at, cell + clearance);
                        bool clash = false;
                        foreach (Bounds room in rooms)
                            if (room.Intersects(box)) { clash = true; break; }
                        if (clash) continue;
                        // And not through the cable either: a cell in the way of the ride is a cell
                        // the car flies into.
                        if (Mathf.Abs(at.x - cableX) < cell.x * 1.5f) continue;
                        if (built >= MaxCells)
                        {
                            capped = true;
                            continue;
                        }

                        GameObject block = Prim(PrimitiveType.Cube, $"Cell_{index}", racks.transform,
                            Vector3.zero, cell, cellMat, removeCollider: true);
                        block.transform.position = at;

                        // ONE IN FIVE IS OCCUPIED. Any more and the shot reads as a lit city; any
                        // fewer and the point is missed. The lit ones are what make the dark ones
                        // legible as cells rather than as masonry.
                        //
                        // Chosen off the INDEX rather than at random, so two builds of this scene are
                        // the same scene - the same rule the cell count and the pitch follow.
                        if (index % 5 == 0)
                            Prim(PrimitiveType.Cube, "Window", block.transform,
                                 new Vector3(-0.505f, 0f, 0f), new Vector3(0.02f, 0.34f, 0.66f),
                                 CellGlowMaterial(), removeCollider: true);

                        built++;
                    }

            if (capped)
                Debug.LogError($"[SceneBuilder] The cell racks hit `MaxCells` ({MaxCells}). The field "
                             + "is not shaped by that cap - it fills whole columns from one corner "
                             + "and stops, so hitting it leaves three quarters of the shaft empty on "
                             + "one side. Raise it, or tighten the region the grid runs over.");

            return built;
        }

        // One material for every lit window, made once. Forty instances of an emissive material is
        // forty materials and forty draw calls; one is one.
        private static Material cellGlowMaterial;

        private static Material CellGlowMaterial() =>
            cellGlowMaterial != null ? cellGlowMaterial
            : cellGlowMaterial = MakeEmissiveMaterial("CellGlow",
                new Color(0.93f, 0.96f, 1f), 2.2f);

        // A face of the shaft. Named rather than inlined because there are five of them and each is
        // one line of intent buried in three of arithmetic.
        private static void Slab(Transform parent, string name, Material mat,
                                 Vector3 centre, Vector3 size)
        {
            GameObject go = Prim(PrimitiveType.Cube, name, parent, Vector3.zero, size, mat,
                                 removeCollider: true);
            go.transform.position = centre;
        }

        // EVERYTHING THE BUILDING IS, as one box. Taken off the renderers rather than off the room
        // list, because the room list does not include the corridors, the shafts or the slide.
        private static Bounds BuildingBounds(Transform[] cycleRoots)
        {
            var bounds = new Bounds();
            bool any = false;

            if (cycleRoots != null)
                foreach (Transform cycle in cycleRoots)
                {
                    if (cycle == null) continue;
                    foreach (Renderer renderer in cycle.GetComponentsInChildren<Renderer>(true))
                    {
                        if (renderer == null) continue;
                        if (!any) { bounds = renderer.bounds; any = true; }
                        else bounds.Encapsulate(renderer.bounds);
                    }
                }

            return any ? bounds : new Bounds(Vector3.zero, Vector3.one * 40f);
        }

        private static float PathLength(Vector3[] path)
        {
            float length = 0f;
            if (path == null) return 0f;
            for (int i = 1; i < path.Length; i++) length += Vector3.Distance(path[i - 1], path[i]);
            return length;
        }
    }
}
