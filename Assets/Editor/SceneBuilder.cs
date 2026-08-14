using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace IterationRoom.EditorTools
{
    // One-shot whitebox scene assembly for the Iteration Room prototype.
    // Run via Unity menu "Iteration Room/Build Whitebox Scene", or headlessly:
    //   Unity.exe -batchmode -quit -projectPath <path> -executeMethod IterationRoom.EditorTools.SceneBuilder.Build
    public static class SceneBuilder
    {
        private const string ScenePath = "Assets/Scenes/IterationRoom.unity";
        private const string MenuScenePath = "Assets/Scenes/MainMenu.unity";
        private const string MaterialsDir = "Assets/Materials";
        private const string PrefabsDir = "Assets/Prefabs";
        private const string ShadersDir = "Assets/Shaders";
        // Item ids are a WIRE VALUE - KeyLock asks for "Key", BalloonTool and GhostReplayer both
        // ask for "Tool". Declared once so a rename cannot silently disarm one side of a gate.
        private const string ToolItemId = "Tool";
        private const string CycleTwoToolItemId = "Tool2";

        // CYCLE 2'S ESCAPE OBJECTS: three shards of one broken ring.
        //
        // Not the cube, sphere and prism again. Those were three DIFFERENT shapes, and finding a
        // shape for a shaped hole is the verb cycle 1 is made of; three pieces of the same broken
        // thing say something else - that what the facility is missing is one object, and it is in
        // pieces inside the core.
        //
        // Three ids rather than one shared one, because `ItemRegistry` maps an id to exactly ONE
        // socket with the last writer winning - a shared id would leave two of the three recesses
        // unreachable.
        // Room3's axes. A SUPPLY sharing one id, exactly as the pins are: what the room needs is
        // several people each holding one, and `ItemRegistry` resolves an id to a free instance.
        private const string AxeItemId = "Axe2";

        private const string ShardAItemId = "Shard2A";
        private const string ShardBItemId = "Shard2B";
        private const string ShardCItemId = "Shard2C";
        // Yellow keeps the id "Key" it has always had. It is a wire value shared by the key object and
        // its lock, and both come from here, so renaming it would be safe - but there is nothing to buy
        // and the yellow key, its door and its lock are the ones already play-tested.
        private const string KeyItemId = "Key";
        private const string RedKeyItemId = "KeyRed";
        private const string BlueKeyItemId = "KeyBlue";

        // One key, described once. Three of these exist and every part of the chain reads the same
        // entry - the key's own material, the tint on its HUD icon, the plate on its lock, the band on
        // its door - which is the whole mechanism by which the colours cannot drift apart.
        //
        // A DISPLAY colour and a KEY colour, and they are not the same value. The key is a shaded metal
        // object lit by ceiling fixtures, so its own colour has to survive being darkened; the plate,
        // band and icon are flat and read at their face value. A single colour that worked as brushed
        // metal came out muddy on a wall plate, and one that read on the wall came out fluorescent on
        // the key.
        private struct KeySpec
        {
            public string itemId;
            public string materialName;
            public Color metal;      // the key object, tinted onto the glb's own gold shading
            public Color display;    // lock plate, door band, HUD icon
        }

        private static readonly KeySpec[] Room2Keys =
        {
            // Yellow is the glb's own gold, untinted - the existing key exactly as it was.
            new KeySpec { itemId = KeyItemId, materialName = "KeyGold",
                          metal = Color.white, display = new Color(0.85f, 0.68f, 0.24f) },
            // Dark red rather than a bright one: the room's own warning red is (0.74, 0.09, 0.09) and a
            // key that matched it would read as part of the facility's signage rather than as an object.
            new KeySpec { itemId = RedKeyItemId, materialName = "KeyRed",
                          metal = new Color(1.15f, 0.30f, 0.26f), display = new Color(0.62f, 0.10f, 0.10f) },
            // The blue tint is EXTREME on purpose, and the first attempt at it came out green. The tint
            // multiplies the glb's own gold, which is (0.831, 0.686, 0.216) - so blue is the channel
            // being scaled by 0.216 while green is scaled by 0.686. A tint that looks blue on paper
            // (0.42, 0.72, 1.35) lands on (0.349, 0.494, 0.291), green-dominant, which is what shipped
            // and what had to be fixed. Working backwards from the wanted (0.13, 0.32, 0.86) instead
            // gives these, and a B of nearly 4 is simply what undoing a 0.216 costs.
            new KeySpec { itemId = BlueKeyItemId, materialName = "KeyBlue",
                          metal = new Color(0.16f, 0.47f, 3.98f), display = new Color(0.16f, 0.36f, 0.72f) },
        };
        // The three objects the final room wants, one per puzzle room. All three exist now, so all
        // three of Room4's recesses name one - which is what makes `FinalSlot.AllFilled` a real
        // question. Nothing asks it yet: see TODO.md for why the last button is still ungated.
        private const string TriangleKeyItemId = "KeyTriangle";
        private const string CubeKeyItemId = "KeyCube";
        private const string SphereKeyItemId = "KeySphere";

        private const string FurnitureDir = "Assets/ArtAssets/Furniture";
        // Meshes this file builds rather than imports. Written to disk because a Mesh created at
        // build time and left in memory does not survive the scene being saved - see
        // TriangularPrismMesh.
        private const string GeneratedDir = "Assets/ArtAssets/Generated";
        // 5.4m across the board: 1.8 first, then twice that, then half again. Derived, not chosen -
        // chess.glb spans 3.155m at its 0.1776 import scale, so it is 17.77m at scale 1 and 5.4 / 17.77
        // is this. A square is 0.675m and the pieces stand 0.46 to 1.09, so a king is waist-high and the
        // board is something walked around rather than looked at.
        private const float ChessScale = 0.3039f;

        // How big a piece is in the HAND, as a fraction of its size on the board. The tallest piece is
        // 1.09m at the scale above and a hold 0.5m from the eye can show about 0.58m of height, so a
        // piece at its own size fills the screen twice over. 0.28 brings a king to 0.30m - large enough
        // to tell a king from a pawn, small enough to see the room past it.
        private const float HeldPieceScale = 0.28f;

        // How far a held piece is turned about its own vertical. Face-on, a bishop and a pawn are the
        // same silhouette; a three-quarter view is what makes the thing in your hand identifiable.
        private const float HeldPieceYaw = 25f;

        // Eight files and eight ranks. Named rather than inlined because the grid is MEASURED off the
        // opening position - see BuildChessSet - and this is the number that measurement divides by.
        private const int SquaresPerSide = 8;

        // HOW MANY PIECES ARE MISSING OFF THE BOARD, and therefore how long the puzzle is. It is the
        // one number that sets Room2West's cost, so it lives here.
        //
        // Twelve, because the cost is paid in ITERATIONS and they compound: a player who tidies four in
        // a loop has four past selves tidying four for them in the next, so twelve is about three
        // iterations of work and not twelve.
        //
        // MEASURED 2026-08-12: the whole game clears in FIFTEEN iterations, up from the four it took
        // before this room and the cube room were on the critical path. So this constant is the
        // game's length dial, and twelve is not a small number - it is most of the run. Turn it down
        // before adding rooms, not after.
        //
        // The other twenty stay on their squares. That is not padding - it is the statement of the
        // puzzle: a board that is nearly right shows the player exactly which squares are empty, and
        // there is nothing to work out and no layout to memorise.
        private const int ScatteredPieceCount = 12;

        // Fixed, because a scene is BUILD OUTPUT: two builds of the same commit have to lay the room
        // out identically, or a bug found in one is not reproducible in the next.
        private const int ChessScatterSeed = 20260812;
        private const int CubeScatterSeed = 20260813;
        private const string SettingsDir = "Assets/Settings";
        // Wall panel albedo. Near-white is the reference film's clinical room; the dark value reads
        // as a switched-off display, which only becomes legible because the panels are glossy and
        // have a reflection probe to mirror - a matte dark panel would just be a black hole.
        // Kept well clear of GrooveDark (0.04) so the seams still read against it.
        private static readonly Color WallPanelColor = new Color(0.13f, 0.135f, 0.15f);

        // Room2West's ceiling fixtures, filled in by BuildShell and read by one thing: the chess
        // board dims them and brings them back up as its reward. Held here rather than found by name
        // later, because a lookup by name is a second statement of what BuildCeilingLights called them.
        private static Light[] Room2WestLights;
        private static Renderer[] Room2WestPanels;

        private const string TexturesDir = "Assets/Textures";
        // Symbols printed on the cube room's cubes and on the recesses that want them. Their own
        // folder because they are WORLD textures - mipmapped, opaque, imported as Default - where
        // everything in Icons/ is a HUD sprite with its shape in the alpha.
        private const string SymbolsDir = TexturesDir + "/Symbols";
        // Near-white paper and near-black ink, which is the building's own palette. Contrast rather
        // than colour is the whole point: a spade has to be a spade to a player who cannot tell red
        // from green, and at whatever brightness the room happens to be.
        private static readonly Color SymbolPaper = new Color(0.88f, 0.88f, 0.90f);
        private static readonly Color SymbolInk = new Color(0.07f, 0.07f, 0.09f);
        private const string IconsDir = TexturesDir + "/Icons";
        private const string MenuBackgroundPath = TexturesDir + "/MenuBackground.png";
        // The menu capture's tripwire colour, and how much of it a frame is allowed to show.
        // Magenta because the room is white panelling and black grooves and cannot produce it; the
        // threshold is 1% against a measured 0 stray pixels out of 1920x1080, so the margin is
        // slack, not a tuned number. Why a tripwire at all: `docs/gotchas.md`.
        private static readonly Color MenuCaptureTripwire = Color.magenta;
        private const float MenuCaptureMaxStray = 0.01f;
        private const string FontsDir = "Assets/Fonts";
        private const string AudioDir = "Assets/Audio";
        private const string VoiceDir = AudioDir + "/Voice";
        private const string SfxDir = AudioDir + "/SFX";

        // Must match the range Tools/generate_narration.ps1 writes out. Past this the announcer
        // falls back to a generic line rather than going silent.
        private const int NarrationIterationLines = 30;

        // The attribution shown at the bottom of the title screen. Everything in this project is
        // generated from a script except the two furniture models and the HUD typeface, so this is
        // the complete list of what came from somewhere else.
        //
        // ⚠️ IF THE MODELS ARE CC-BY RATHER THAN CC0 THIS IS NOT YET SUFFICIENT: BY requires the
        // creator's name, and ideally a link to the source. Fill those in here the moment they are
        // known - it is one string, and it is the only thing standing between this build and being
        // properly credited.
        private const string CreditsLine =
            "FURNITURE MODELS: CREATIVE COMMONS   ·   TYPE: JETBRAINS MONO (SIL OFL)";

        // Balloons get their own physics layer so the player's CharacterController can exclude it
        // outright. See FirstPersonController.PushOverlapping for why not colliding with them at
        // all is the point rather than a shortcut.
        private const string BalloonLayerName = "Balloon";

        // Room shell dimensions. The wall-grid tiling is derived from these (see MakeGridMaterial),
        // so changing a dimension here keeps the grid cells the right physical size automatically -
        // don't hardcode tiling numbers anywhere else.
        private const float RoomWidth = 8.75f;   // X span, i.e. the door wall
        private const float RoomDepth = 10.5f;   // Z span, i.e. the side walls
        // Not a round number, and worth knowing why: this is what the golden-ratio pass left the
        // room at, when it was five phi-proportioned cells tall. The cells have since been made
        // taller and the ratio has gone, but the height itself was deliberately kept - it is what
        // the fixture intensity and the two reflection probes are tuned against, and moving it
        // would invalidate both for no gain.
        private const float RoomHeight = 5.4077968f;
        private const float WallThickness = 0.1f;

        // Wall grid cells are deliberately landscape: 1.75 x 1.3519, a ratio of about 1.29.
        //
        // Two hard constraints sit behind these numbers. A cell's WIDTH has to divide both 8.75 and
        // 10.5 exactly, or the last column of a wall overshoots its end (BuildPanelWall lays cells
        // at fixed GridCellWidth steps after rounding the column count). A cell's HEIGHT has to
        // divide RoomHeight exactly, or the top row is a part cell and the panelling runs off cut
        // in half at the ceiling - which is what a 4.5m room height once did.
        //
        // The height constraint is enforced by construction rather than by care: RoomHeight is the
        // fixed dimension and the cell height is derived from it. To change how tall the cells are,
        // change GridRows - four rows instead of five is what took them from 1.0816 to 1.3519.
        //
        // The cells were briefly exactly phi:1 (1.75 x 1.0816, five rows). That is where the odd
        // room height below comes from, and the ratio went when the cells were made taller; phi
        // could not have survived the change anyway, since it needs a specific height and this asks
        // for a different one.
        private const float GridCellWidth = 1.75f;
        private const int GridRows = 4;
        private const float GridCellHeight = RoomHeight / GridRows;         // 1.3519
        // Wide enough to read as a real reveal between panels rather than as a drawn line. It was
        // 0.082 while the cells were shorter; the taller the panel, the more groove it takes to
        // keep the same visual weight.
        private const float GridLineThickness = 0.05f;

        // How far the gaps between wall panels are recessed. This is what turns the grid from a
        // drawing into geometry: the recesses catch real shadow and ambient occlusion.
        // Deep enough to catch ambient occlusion, shallow enough that the panels' white side faces
        // don't wash the seam out when you view a wall at a grazing angle.
        private const float GrooveDepth = 0.025f;

        // Natural door proportions, deliberately NOT snapped to the grid - the panelling is cut
        // around it instead, so it reads as a doorway rather than a missing panel.
        private const float DoorWidth = 1.3f;
        private const float DoorHeight = 2.5f;
        private const float DoorThickness = 0.06f;
        // Clearance left between the two rooms' walls. The door lives in this cavity and slides
        // sideways into it, so when it's open both walls hide it - a pocket door.
        private const float DoorPocketDepth = 0.1f;

        // Total build-up of a wall from the room surface outwards: recess + backing slab.
        private const float WallDepth = GrooveDepth + WallThickness;
        // Centre-to-centre spacing of adjacent rooms: one room, both sides of the divider, and the
        // pocket between them.
        private const float RoomPitch = RoomDepth + 2f * WallDepth + DoorPocketDepth;

        // THE GAP BETWEEN ONE CYCLE'S FLOOR AND THE NEXT ONE'S CEILING, and it exists for a reason
        // that only showed up once there was a room underneath.
        //
        // **Every `RewardPlinth` retracts about a metre BELOW its floor.** Room1-0's console sinks
        // 1.13m, which was invisible while there was nothing under it and became a box hanging out of
        // room2-1's ceiling the moment there was. Housing it did not help: the housing hung into the
        // room too. The only fix that leaves the room below clean is for the machinery to have
        // somewhere to be that is not in either room - which is what a real building would do.
        //
        // 1.6m: the console's 1.13m of travel, its housing, and clearance. The shaft between the two
        // holes runs through this, so it is also how long the drop feels before the lower room opens
        // up underneath.
        private const float ServiceVoid = 1.6f;

        // HOW FAR DOWN THE NEXT STOREY IS. A cycle's rooms sit one of these below the cycle before it.
        //
        // The upper room's floor spans y -WallThickness..0 and the lower room's ceiling spans
        // -(ServiceVoid + 2*WallThickness)..-(ServiceVoid + WallThickness), with the void between
        // them. The hole cut through both lines up because it is the same Rect in each room's local
        // XZ - see BuildExitShaft for what joins them.
        private const float StoreyDrop = RoomHeight + 2f * WallThickness + ServiceVoid;

        // WHERE A CYCLE'S WAY OUT IS, in room-local Z. Behind the console from the player's approach,
        // which is always from -Z: the console body ends at z = 0.575, so this clears it, and the north
        // wall is at 5.25, so it clears that too.
        private const float CycleExitZ = 2f;

        // One wall grid cell across, and square. The grid gives the SIZE - a hole on the building's own
        // module reads as a piece of the structure coming away rather than as a hatch fitted to a
        // person - and squaring it is what makes it something to fall down rather than step through.
        //
        // The same Rect is handed to the room above's FLOOR and the room below's CEILING, in each one's
        // local XZ. That is why they line up: not two numbers kept in agreement, one number used twice.
        private static readonly Rect CycleExitHole = Rect.MinMaxRect(
            -GridCellWidth / 2f, CycleExitZ - GridCellWidth / 2f,
             GridCellWidth / 2f, CycleExitZ + GridCellWidth / 2f);

        // Cycle 2 sits one storey down and runs BACK the way cycle 1 came. Its first room is directly
        // beneath cycle 1's last, so the drop is short and vertical and the player can see the bed
        // through the opening before committing to it; everything after it walks toward -Z.
        // HOW MANY CYCLES THE GAME HAS. Read by the title screen's picker; `LoopManager` derives the
        // same fact from the length of its own array, which is the authority. Adding a cycle means
        // changing both, and they are meant to be found together.
        private const int CycleCount = 2;

        private const float CycleTwoFirstRoomZ = 5f * RoomPitch;

        // HOW FAR APART TWO ROOMS SIT AT A CORNER, where one presents its WEST wall and the next
        // presents its NORTH wall.
        //
        // `RoomPitch` cannot serve, and that is the one piece of arithmetic the ring turns on: a room
        // is 8.75 across and 10.5 deep, so two rooms meeting along the same axis are a different
        // distance apart than two meeting across a corner. Half of each, plus the same divider every
        // other join has.
        private const float CornerPitch = RoomWidth / 2f + RoomDepth / 2f + 2f * WallDepth + DoorPocketDepth;

        // THE WINDOW ONTO THE CORE: three grid cells wide, two tall, starting one cell up. Sized off
        // the grid rather than picked, so it reads as part of the wall's own module - the same
        // argument the floor opening in `room1-0` is sized by.
        private static readonly Rect CoreWindow = Rect.MinMaxRect(
            -1.5f * GridCellWidth, GridCellHeight, 1.5f * GridCellWidth, 3f * GridCellHeight);

        // ROOM2-1'S COMBINATION, left to right along the south wall as the player faces it from the
        // door. One digit per grid cell, one pad in front of each.
        //
        // Five, because five is what a room's worth of iterations looks like: a past self can do one
        // pad and leave, so the room is about that many trips. Raising or lowering this is the length
        // dial for this room, the way the chess room's twelve is for that one.
        private static readonly int[] CycleTwoCombination = { 3, 1, 4, 1, 5 };

        // The sensitivity room. Deliberately NOT a multiple of RoomPitch in the positive direction
        // - it is not part of the chain and must never be walked into, so it sits behind Room1 with
        // a room's worth of nothing between them.
        private const string CalibrationRoomName = "CalibrationRoom";
        private const float CalibrationRoomZ = -2f * RoomPitch;

        // SKIPS THE REFLECTION PROBE BAKE, and that is nearly the whole of the build.
        //
        // Fifteen probes, each rendering the scene six times at 512, is 35 MB of cubemap and the
        // overwhelming majority of a rebuild - the geometry itself is fast. During a session spent
        // moving a wall or retuning a pad, none of that work is being looked at.
        //
        // Existing `.exr` files are left in place, so the scene still comes up lit and reflective; it
        // simply reflects the building as it was at the last full build. **Use the full build before
        // judging anything reflective**, and always before a commit that moves geometry - the same
        // bargain `-nographics` already makes (see BakeReflectionProbes).
        [MenuItem("Iteration Room/Build Whitebox Scene (fast, no probes)")]
        public static void BuildFast()
        {
            skipProbeBake = true;
            try { Build(); }
            finally { skipProbeBake = false; }
        }

        private static bool skipProbeBake;

        [MenuItem("Iteration Room/Build Whitebox Scene")]
        public static void Build()
        {
            EnsureFolders();

            // Without this, play mode stops ticking the moment the Editor loses focus, so any
            // MCP-driven test (teleport the player, open the door, screenshot) captures a stale
            // frame and looks like nothing happened. Setting it during play mode doesn't stick,
            // hence doing it here.
            PlayerSettings.runInBackground = true;

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            Camera defaultCam = Camera.main;
            if (defaultCam != null) Object.DestroyImmediate(defaultCam.gameObject);

            // One material for both slabs. Floor and ceiling were split apart for a marble floor;
            // that was dropped in favour of a plain white one, and with the two surfaces identical
            // again the split was only duplication.
            Material floorMat = MakeColorMaterial("FloorWhite", Color.white);
            // Sits at the bottom of every groove and inside the door pocket. Near-black so the
            // seams read the way the old painted-on grid lines did.
            Material grooveMat = MakeColorMaterial("GrooveDark", new Color(0.04f, 0.04f, 0.045f));
            Material propMat = MakeColorMaterial("PropLight", new Color(0.85f, 0.85f, 0.85f));
            // White is the resting state - what the panels are for all but the first seconds of an
            // iteration, and what the reflection probes bake against. WallPanelDisplay drives them
            // to WallPanelColor and back through a property block at runtime.
            Material panelMat = MakeColorMaterial("PanelWhite", Color.white);
            // Emission is enabled with a black colour: the keyword has to be compiled in for
            // WallPanelDisplay's collapse flare to have anything to drive, and a property block
            // cannot turn a shader keyword on. Black means it contributes nothing until then.
            panelMat.EnableKeyword("_EMISSION");
            panelMat.SetColor("_EmissionColor", Color.black);
            panelMat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            EditorUtility.SetDirty(panelMat);

            // Tiling is per face, and a wall panel's face is roughly 1.7 x 0.9m while a floor slab
            // is 9 x 10.9m - so the two need very different repeat counts to land on the same
            // physical grain size (~0.35m per repeat).
            // bumpScale is ~2.5, not the <1 that looks like a sane default. Central differences
            // across a smooth multi-octave field give very small gradients, so the generated map is
            // genuinely shallow: at 0.7 the walls rendered perfectly flat and the detail was
            // invisible even with the camera against them. 10 turns it into stucco. 2.5 reads as
            // painted plaster up close and disappears at room distance, which is the goal.
            // The floor is left smoother, as a harder finish would be.
            // Walls read as a smooth glazed panel - closer to ceramic tile or a switched-off
            // display than to plaster. That means gloss, and almost no relief: the grain is kept
            // at a whisper (0.2) purely so the specular isn't a perfectly uniform sheet, which is
            // what makes a flat surface look CG. Smoothness 0.85 is what actually does the work,
            // and it only reads correctly because the reflection probes give it the room to mirror
            // rather than the blue sky.
            //
            // The floor stays matte by comparison, as a hard-wearing floor finish would be against
            // a glazed wall.
            Texture2D surfaceGrain = MakeNoiseNormalMap("SurfaceGrain", 512, 2.5f);
            ApplySurfaceDetail(panelMat, surfaceGrain, 0.2f, new Vector2(5f, 3f), 0.85f);
            // Floor and ceiling: plain white, matte, with the same plaster grain the walls get -
            // just at a far higher repeat count, since a slab face is 9 x 10.9m against a wall
            // panel's 1.7 x 0.9m.
            ApplySurfaceDetail(floorMat, surfaceGrain, 1.8f, new Vector2(26f, 30f), 0.18f);

            GameObject room = new GameObject("Room");
            BuildShell(room.transform, floorMat, grooveMat, panelMat);

            // Cycle 2, one storey down. Deliberately OUTSIDE `room` - see BuildCycleTwoShell for why
            // the panel gather below is the reason.
            (Transform cycleTwoRoot, Transform cycleTwoBedSpawn, ParticleSystem[] cycleTwoGas,
             Door[] cycleTwoDoors, RoomCondition[] cycleTwoConditions,
             GhostInteractable[] cycleTwoSignals, Transform[] cycleTwoRooms) =
                BuildCycleTwoShell(floorMat, grooveMat, panelMat, propMat);

            // Every wall panel, gathered by parent name rather than threaded back out through
            // BuildShell/BuildRoomShell/BuildPanelWall - the panels are the only children of a
            // "*_Panels" node, so this stays correct without four signature changes.
            var wallPanelRenderers = new System.Collections.Generic.List<Renderer>();
            foreach (Renderer r in room.GetComponentsInChildren<Renderer>())
            {
                if (r.transform.parent == null || !r.transform.parent.name.EndsWith("_Panels")) continue;
                // The calibration room is left out. It is seen once, before the loop starts, and
                // never again - so booting and flaring its ~88 panels would be a per-frame property
                // block written to renderers nobody can look at. Property blocks already break SRP
                // batching across this array; there is no reason to make it a third longer.
                if (InCalibrationRoom(r.transform)) continue;
                wallPanelRenderers.Add(r);
            }

            // What a panel shows once it fails. Generated rather than sourced, like every other
            // texture in this build, and shared by both cycles' displays.
            Texture2D testCard = MakeTestCardTexture("TvTestCard");
            Texture2D staticNoise = MakeStaticTexture("TvStatic", 64);

            WallPanelDisplay wallDisplay = MakeWallPanelDisplay(
                "WallPanelDisplay", wallPanelRenderers.ToArray(), testCard, staticNoise);

            // ONE DISPLAY PER CYCLE. The ERROR spreading from a console means *this bed's cycle is
            // over*, so a panel in a cycle the player has not reached has no business failing - and
            // the gather is by name, which would otherwise sweep up every storey at once.
            var cycleTwoPanels = new System.Collections.Generic.List<Renderer>();
            foreach (Renderer r in cycleTwoRoot.GetComponentsInChildren<Renderer>())
            {
                if (r.transform.parent == null || !r.transform.parent.name.EndsWith("_Panels")) continue;
                cycleTwoPanels.Add(r);
            }
            WallPanelDisplay cycleTwoDisplay = MakeWallPanelDisplay(
                "WallPanelDisplay_Cycle2", cycleTwoPanels.ToArray(), testCard, staticNoise);

            // Four recessed downlights per room, plus Trilight ambient standing in for the bounce
            // URP is not computing. The scene's default Directional Light is deleted rather than
            // dimmed - these are sealed boxes with a ceiling slab, so a sun has no way in.
            SetupLighting();
            BuildPostProcessing();
            ConfigureAmbientOcclusion();
            ConfigureLightingPipeline();

            (Transform bed, Transform bedSpawn) = BuildBed(room.transform, propMat);
            (Drawer drawer, CarryableItem[] pins) = BuildNightstand(room.transform);
            // Out in the open floor area past the foot of the bed, matching room_layout_sample.png.
            FloorButton floorButton = BuildFloorButton(room.transform, propMat, "FloorButton",
                new Vector3(2.8f, 0.03f, -1.75f));
            Door door = BuildPadDoor(room.transform, "Door", 0f, new[] { floorButton }, propMat);

            // Room2: a roomful of balloons and three keys. None of the three doors they open is a
            // GhostInteractable - carrying is not part of a recording, so a ghost cannot open one for
            // you. Getting yourself to each one holding its key is what the room asks.
            //
            // THREE KEYED DOORS, one per key colour, and the colours are the only instruction the
            // player gets - but they are no longer three exits off Room2's own walls. The whole run is
            // ONE CORRIDOR now: Room2 -> Room2West -> Room2East -> Room3 -> Room4, and a coloured door
            // sits at each of the first three joins, in the order the keys have to be spent. Red and
            // blue used to be side doors off Room2 leading straight to these same two rooms; they keep
            // exactly that pairing (red still opens onto the chess room, blue still onto the cube room)
            // and only their POSITION moved, from a wall in Room2 to the threshold of the room itself.
            // Yellow was Room2's original way out and is still the last of the three, now handing off
            // to Room3 instead of straight to it. Each door's lock and its key are built from the same
            // KeySpec entry, so a plate cannot end up wanting a key that does not match it.
            (Door doorRed, KeyLock lockRed) =
                BuildKeyDoor(room.transform, "DoorRed", RoomPitch, propMat, Room2Keys[1]);
            (Door doorBlue, KeyLock lockBlue) =
                BuildKeyDoor(room.transform, "DoorBlue", 2f * RoomPitch, propMat, Room2Keys[2]);
            (Door doorYellow, KeyLock lockYellow) =
                BuildKeyDoor(room.transform, "DoorYellow", 3f * RoomPitch, propMat, Room2Keys[0]);
            (BalloonField balloonField, CarryableItem[] keys) = BuildBalloons(room.transform);

            // Room2West, behind the red door: a chess set in the middle of the floor, with a dozen of
            // its pieces scattered around it and its lights turned down until they are all back.
            //
            // ON THE FLOOR at 1.8m rather than on a table, because at that size the pieces are 0.4m
            // tall - things a person could pick up and put down, which is the shape any puzzle here is
            // going to want. A board at table height and table scale would be scenery.
            //
            // 0.1013 comes from measuring: the glb arrives at import scale 0.1776 spanning 3.155m, so
            // it is 17.77m at scale 1 and 1.8 / 17.77 is this. The -90 X is the same Z-up correction the
            // bed and the nightstand need - PlaceModel REPLACES the prefab's own rotation, so the 270 X
            // the import gives it does not survive and has to be asked for again.
            //
            // Room2West is a room on the chain now, the same shape and orientation as Room1-4, entered
            // from its SOUTH wall - not the rotated side room this used to be, entered from the east.
            // The doorway hint only guards the entrance (south); the room also has a north doorway
            // through to Room2East, and pieces are not scattered densely enough near it to need a
            // second guard - the same one-doorway simplification the room lived with before, when a
            // second doorway did not yet exist.
            Vector3 room2WestCentre = new Vector3(0f, 0f, 2f * RoomPitch);
            Vector3 room2EastCentre = new Vector3(0f, 0f, 3f * RoomPitch);
            Vector3 room2WestDoorway = new Vector3(0f, 0f, room2WestCentre.z - RoomDepth / 2f + 0.6f);
            (CarryableItem[] chessPieces, ChessBoard chessBoard) =
                BuildChessSet(room.transform, room2WestCentre, room2WestDoorway, propMat,
                              Room2WestLights, Room2WestPanels);

            // Room2East, behind the blue door: six symbol cubes on the floor and six recesses in the
            // walls that want them, paying out the blue sphere.
            (CarryableItem[] symbolCubes, CubeRoom cubeRoom) =
                BuildCubeRoom(room.transform, room2EastCentre, propMat);

            // Room3: TWO pads and one door that needs both at once. Deliberately the plainest room
            // of the three - no items, nothing to search, nothing to carry. Room2 already costs the
            // player a key retrieval every iteration, and a second expensive room behind it would be
            // unreachable rather than hard. What Room3 costs is ITERATIONS: one to stand on each
            // pad, and a third to walk through while two past selves hold them.
            //
            // ONE AGAINST EACH SIDE WALL, facing each other across the room at its mid-depth. Two is
            // the smallest count that still says "not something one person can do", and a facing
            // PAIR is the clearest arrangement there is for it - a player who finds one is looking
            // straight at the other, so there is nothing to hunt for and nothing to suggest that a
            // subset might do.
            //
            // Mirror-symmetric about the room's north-south axis, which the earlier triangle could
            // not be: a triangle symmetric about that axis has to put a vertex ON it, and that axis
            // is the straight walk from the south door to the north one. With a pair, the symmetry
            // and the clear walk come free - each pad sits 3.2m off the line, eight times its own
            // reach. Standing on one is the only way to learn what the other is for, so the room has
            // to show them together and never trigger by accident.
            const float roomThreeZ = 4f * RoomPitch;
            // 3.2 from the centre line leaves 1.175m to the wall face - against the wall as read
            // from the middle of the room, with room to stand on the pad rather than in the wall.
            const float padWallX = 3.2f;
            FloorButton[] roomThreePads =
            {
                BuildFloorButton(room.transform, propMat, "FloorButton3A", new Vector3(-padWallX, 0.03f, roomThreeZ)),
                BuildFloorButton(room.transform, propMat, "FloorButton3B", new Vector3(padWallX, 0.03f, roomThreeZ)),
            };
            Door door3 = BuildPadDoor(room.transform, "Door3", roomThreeZ, roomThreePads, propMat);

            // ROOM3 PAYS OUT TOO, on exactly the condition that opens its door. Both pads held raises
            // a plinth carrying the yellow triangle, and lets it go again when one is released - the
            // same promise the door makes, because it is the same rule (`FloorButton.AllActive`).
            //
            // DEAD CENTRE, between the two pads and on the straight walk from the south door to the
            // north one. That axis was ruled out for a PAD, and for a reason that does not apply here:
            // a pad there fires by accident, where this is the one thing in the room the player is
            // meant to walk into. It also keeps the mirror symmetry the pair of pads is built on.
            (Transform yellowPlinth, Transform yellowSeat, CarryableItem yellowKey) =
                BuildKeyPlinth(room.transform, "YellowPlinth", new Vector3(0f, 0f, roomThreeZ), propMat,
                    SlotShape.Triangle, new Color(0.95f, 0.78f, 0.12f), "KeyTriangleMat",
                    TriangleKeyItemId, "TRIANGLE", TriangleIcon());

            RewardPlinth yellowRise = yellowPlinth.gameObject.AddComponent<RewardPlinth>();
            yellowRise.pads = roomThreePads;
            yellowRise.plinth = yellowPlinth;
            yellowRise.key = yellowKey;
            yellowRise.keySeat = yellowSeat;
            // Clear of the floor slab with the object's own height counted in, so nothing shows
            // through before the pads are held.
            yellowRise.riseHeight = 1.25f;

            // THE WAY INTO ROOM4, and no longer the way out of the game. Crossing this latches
            // "the player got here this iteration", which is what raises the console through the
            // floor beyond it; the clock keeps running and what ends a run is putting all three
            // escape objects into that console. Still sat ON the threshold rather than past it,
            // which is now simply where a doorway is - it used to be forced, because the FarCap was
            // right behind that opening with no "through" to stand in.
            // See EscapeTrigger and FinalRoomSequence.
            GameObject escapeGO = new GameObject("EscapeTrigger");
            escapeGO.transform.SetParent(room.transform, false);
            escapeGO.transform.localPosition = new Vector3(0f, 0f, roomThreeZ + RoomDepth / 2f);
            EscapeTrigger escape = escapeGO.AddComponent<EscapeTrigger>();
            escape.door = door3;
            // Half the doorway plus a little, so only a player actually in the opening qualifies -
            // without this, anyone at the same Z anywhere along the north wall would count.
            escape.halfWidth = DoorWidth / 2f + 0.05f;

            // The wire format for ghost playback: an entry's index is its bit in
            // RecordedFrame.signals, so this is appended to and never reordered. The recorder and
            // the loop are handed the same array, so the two can never drift out of order.
            //
            // The drawer is in it - pulling a drawer needs no inventory, so a ghost can repeat it.
            // The KEY LOCK is deliberately NOT: the condition that lets the player open that door
            // is holding the key, and a ghost cannot hold anything. See the note on KeyLock.
            //
            // The door button used to sit between these two and has been removed with it; the
            // drawer therefore moved from bit 2 to bit 1. That is safe only because timelines live
            // for one session of play and are never persisted - reordering this at runtime would
            // invalidate every recording made so far.
            GhostInteractable[] ghostInteractables =
            {
                floorButton, drawer,
                roomThreePads[0], roomThreePads[1],
            };

            // The player starts in the calibration room, not at the bed. Iteration 1 teleports them
            // to bedSpawn at the top of RunLoop regardless, so this only decides where they stand
            // while setting the sensitivity.
            GameObject calibSpawnGO = new GameObject("CalibrationSpawn");
            calibSpawnGO.transform.SetParent(room.transform, false);
            // The SAME offset and facing as the bed spawn, one room over: (0, -0.7) looking at 180.
            // That is not tidiness, it is the point of the room.
            //
            // Turning in place has the same angular rate whatever is in front of you, so the number
            // being set is identical either way - but how FAST it feels is not. What the eye
            // actually counts is detail crossing the view, and a far wall puts many more panel
            // edges in a degree than a near one. An earlier version stood the player 3m from the
            // south wall facing the length of the room, with 8.25m of wall ahead against the game's
            // 4.55m: same sensitivity, noticeably quicker to look at. Matching the pose makes the
            // calibration frame the frame the game opens on.
            calibSpawnGO.transform.localPosition = new Vector3(0f, 0f, CalibrationRoomZ - 0.7f);
            calibSpawnGO.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            Transform calibrationSpawn = calibSpawnGO.transform;

            (GameObject player, FirstPersonController fpc, PlayerRecorder recorder, CameraShaker shaker, PlayerHand hand) = BuildPlayer(calibrationSpawn, ghostInteractables);

            GhostReplayer ghostPrefab = BuildGhostPrefab();
            GameObject ghostParent = new GameObject("Ghosts");

            (IterationLabel label, WakeUpSequence wakeUp, Transform canvas) = BuildUI(hand);
            wakeUp.wallPanels = wallDisplay;

            // Everything E does something to, in the order the player is likely to meet it. The
            // prompt is shown over whichever of these is nearest and currently wants it - every
            // time, not once; see ControlHintDisplay for why that changed. Room1's door is not in
            // the list any more because it has no control to press.
            //
            // ALL THREE pins go in, not just the first: the display picks the nearest one that wants a
            // prompt, so a drawer holding three prompts over whichever is being looked at. Once one
            // is taken the other two go quiet - CarryableItem.WantsInteractHint asks the hand whether
            // it already has this id, which it had no reason to before an id could be a supply.
            // Three keys and three locks go in for the same reason. Nearest-wins means standing at a
            // door prompts over that door's lock and not over the other two, and standing over a key on
            // the floor prompts over the key - which is exactly what the coloured doors need, since the
            // prompt is what says "this is the thing you can act on from here".
            var pinTargets = new System.Collections.Generic.List<MonoBehaviour> { drawer };
            pinTargets.AddRange(pins);
            pinTargets.Add(lockRed);
            pinTargets.Add(lockBlue);
            pinTargets.Add(lockYellow);
            pinTargets.AddRange(keys);
            // And every chess piece, so E over one shows the prompt like E over anything else. Thirty-two
            // more entries in a list NearestWantingHint walks every frame, which is nothing next to what
            // it would cost to have the one room full of takeable objects be the room with no prompts.
            pinTargets.AddRange(chessPieces);
            // And Room3's triangle, which is only ever reachable while two past selves are standing on
            // the pads - the one moment in the game where a prompt appears because somebody ELSE is
            // holding a condition open.
            pinTargets.Add(yellowKey);
            // The cube room, both halves of it: the six cubes on the floor and the six recesses that
            // want them. The recesses are E fixtures like the drawer and the locks, so they get the
            // same disc; nearest-wins keeps a player standing at one wall from being prompted by the
            // recess behind them.
            pinTargets.AddRange(symbolCubes);
            pinTargets.AddRange(cubeRoom.slots);
            pinTargets.Add(cubeRoom.reward.key);
            ControlHintDisplay hints = BuildControlHints(canvas, player.GetComponentInChildren<Camera>(),
                player.GetComponent<BalloonTool>(), pinTargets.ToArray());

            // Putting a piece back. On the player beside BalloonTool, sharing the left button with it and
            // unable to clash: that one is silent unless the pin is held, this one unless a piece is, and
            // PlayerHand has one item out at a time.
            ChessPlacer placer = player.AddComponent<ChessPlacer>();
            placer.playerCamera = player.GetComponentInChildren<Camera>();
            placer.hand = hand;
            placer.board = chessBoard;
            // The mouse prompt rides the board's lit square, which is the same rule the swing prompt
            // follows: the label goes on the thing the click acts on, not on the hand it is held in.
            hints.placer = placer;

            // Escape's overlay covers the HUD, the prompts and the eyelids...
            BuildPauseMenu(canvas, fpc);
            // ...and the ending covers even that. Built last so nothing in the game can draw over
            // the last thing the player sees. (Pausing is locked out for its duration anyway - see
            // PauseMenu - but the draw order should not depend on that being true.)
            EndingSequence ending = BuildEndingScreen(canvas);
            // Above even that, because it is the first thing the run shows and nothing else is
            // running while it is up.
            SensitivityCalibration calibration = BuildCalibrationPage(canvas);
            CalibrationStartButton startButton = BuildCalibrationWall(room.transform, CalibrationRoomZ, calibration);

            // Room4 and the plate that ends the run. Its narration is wired after BuildAudio below;
            // control is LoopManager's to take, at the scrim, so nothing here needs the player.
            FinalRoomSequence finalRoom = BuildFinalRoom(room.transform, 5f * RoomPitch, propMat,
                                                        door3, wallDisplay, shaker, hand,
                                                        CubeKeyItemId, SphereKeyItemId, TriangleKeyItemId);

            // THE WAY ON, in room1-0's floor behind that console. Wiring it is what makes cycle 1 not
            // the last cycle - LoopManager reads "is there another entry in the array", and this is
            // the door that entry is reached through.
            // THE JOIN BELONGS TO NEITHER CYCLE, so it hangs off its own root rather than off cycle
            // 1's.
            //
            // It was under `Room`, which is cycle 1's world root - and that root is deactivated the
            // moment cycle 1 ends. So the lid over the hole went with it, and room2-1 spent the whole
            // of cycle 2 with an open square in its ceiling. It reads as a patch of wrong colour
            // rather than as a hole, which is why it took a walk-through to notice: what is visible
            // through it is the void between the storeys.
            //
            // The shaft has the same problem in the other direction - it is what the player falls
            // down, at the one moment both cycles are awake - so it moves here too. The console
            // housing does NOT: it is cycle 1's machinery, it sits inside the service void where
            // neither room can see it, and it should sleep when cycle 1 does.
            GameObject join = new GameObject("CycleJoin_1_2");

            CycleExit cycleOneExit = BuildCycleExit(join.transform, 5f * RoomPitch, floorMat, fpc.transform);
            finalRoom.wayOut = cycleOneExit;

            // The console retracts a metre below the floor, which is now a metre INTO room2-1. Give
            // it something to retract into - see BuildPlinthHousing. Sized off the plinth (1.70 x
            // 1.15) with clearance, and deep enough to swallow its full 1.13m travel.
            BuildPlinthHousing(room.transform, "ConsoleHousing",
                new Vector3(0f, 0f, 5f * RoomPitch), 1.86f, 1.31f, 1.45f, floorMat);

            // And the tube the player falls down, joining this floor's hole to the ceiling hole of
            // the room below across the service void.
            BuildExitShaft(join.transform, "ExitShaft_Cycle1", 5f * RoomPitch, floorMat);

            // Appended after the fact because both buttons live in rooms built later than the hint
            // display. They are the two E fixtures OUTSIDE the loop - one before the first
            // iteration, one after the last - and they get the same grey disc as every other one,
            // which is the point: the run opens and closes on the game's own prompt.
            var hintTargets = new System.Collections.Generic.List<MonoBehaviour>(hints.interactTargets)
            {
                startButton,
            };
            // And the console's three recesses, which are E fixtures like any other now that the
            // clock runs through the room they are in.
            hintTargets.AddRange(finalRoom.slots);
            hints.interactTargets = hintTargets.ToArray();
            hints.calibration = calibration;
            // "The player got through the last door", which arms Room4's console. Wired after the
            // fact because the trigger is built with Room3 and the console with Room4.
            finalRoom.arrival = escape;

            // Room2's wordless sign. Built here rather than with Room2 because it needs the
            // player's BalloonTool: it retires on the first pop, not on the first visit.
            BuildBalloonPictogram(room.transform, RoomPitch, player.GetComponent<BalloonTool>());

            // The end-cycle sign, IN ROOM1 AND FROM ITERATION 2 - it used to hang in Room3 and go up
            // on the first visit there. Same four walls, same words, different address:
            //
            //   - Room1 is where every iteration BEGINS, so the player is standing still with
            //     nothing to do yet, which is the only moment in this game that is reliably free.
            //     Room3 could only ever catch them mid-errand.
            //   - Iteration 2 is the earliest the message means anything. It is an offer to skip
            //     dead time, and a player who has not yet watched a clock run out has no dead time
            //     to skip. Room3 was the old way of buying that same delay - "by the time they get
            //     here they will have felt it" - expressed as distance instead of as iterations.
            //   - It retires on the ACTION, not on leaving the room, and here that is load-bearing
            //     rather than a refinement: the player can be out of Room1 in under three seconds,
            //     so the leave-once rule could retire a sign nobody read. See PanelMessage.
            //
            // Built here for the same reason as the pictogram - it needs something off the canvas.
            PanelMessage wallMessage = BuildWallMessage(room.transform, 0f);
            wallMessage.showFromIteration = 2;
            // Long enough to clear "Iteration 2, 60 seconds remaining." Announcements replace each
            // other rather than stacking, so a chime at t=0 in this room truncates the loop's own
            // line - which no room had to worry about while this lived three rooms away.
            wallMessage.announceDelay = 5f;
            wallMessage.retireOnEndCycle = canvas.GetComponentInChildren<EndCycleControl>(true);

            (NarrationDirector narration, RoomAmbience ambience) =
                BuildAudio(player, new[] { door, doorRed, doorBlue, doorYellow, door3 },
                           new[] { floorButton, roomThreePads[0], roomThreePads[1] },
                           wakeUp);

            wallMessage.narration = narration;
            // Announced at the PRESS rather than when the player stepped through the doorway:
            // walking into a room is not what breaks a cycle. See FinalRoomSequence.
            finalRoom.narration = narration;

            // CYCLE 1'S WORLD, gathered onto one object. Every field here used to sit directly on
            // LoopManager, which was right while there was one bed and stops being right the moment
            // there are two - `balloonField` cannot be *the* balloon field with two in the scene.
            //
            // **Cycle 1's `wayOut` is deliberately left null, and that is what makes it the last
            // cycle.** LoopManager derives "last" from having no successor in the array rather than
            // from a number, so adding cycle 2 is adding an entry and wiring an exit - nothing here
            // needs a count.
            GameObject cycleOneGO = new GameObject("Cycle1");
            Cycle cycleOne = cycleOneGO.AddComponent<Cycle>();
            cycleOne.bedSpawnPoint = bedSpawn;
            // Every door on the one corridor. This array is what the cycle SHUTS at the top of an
            // iteration, and a door that can be opened has to be one of them.
            cycleOne.doors = new[] { door, doorRed, doorBlue, doorYellow, door3 };
            cycleOne.drawers = new[] { drawer };
            cycleOne.balloonField = balloonField;
            cycleOne.chessBoard = chessBoard;
            cycleOne.cubeRoom = cubeRoom;
            cycleOne.finalRoom = finalRoom;
            cycleOne.ghostInteractables = ghostInteractables;
            cycleOne.wallPanels = wallDisplay;
            cycleOne.worldRoot = room.transform;

            // CYCLE 2. A bed, a room and nothing else yet - which is exactly what the boundary needs
            // to be exercised, and no more.
            //
            // Its `finalRoom` is null, so `Cycle.Complete` is false forever and the loop simply keeps
            // iterating there. That is the honest state of a cycle with no puzzles in it rather than a
            // gap: there is nothing to finish, so nothing finishes. Wiring a console is what will end
            // it, the same way cycle 1 ends.
            GameObject cycleTwoGO = new GameObject("Cycle2");
            Cycle cycleTwo = cycleTwoGO.AddComponent<Cycle>();
            cycleTwo.bedSpawnPoint = cycleTwoBedSpawn;
            cycleTwo.doors = cycleTwoDoors;
            cycleTwo.drawers = cycleTwoRoot.GetComponentsInChildren<Drawer>(true);
            // Its own array, numbered from ZERO - cycle 2's five pads are bits 0-4, where cycle 1's
            // pad is also bit 0. Legal because every ghost is destroyed at the boundary, so no
            // surviving timeline refers to cycle 1's bits, and the recorder is repointed at this
            // array when the cycle starts. See Cycle.ghostInteractables.
            cycleTwo.ghostInteractables = cycleTwoSignals;
            cycleTwo.conditions = cycleTwoConditions;

            // ROOM0: the console the three shards go into, and the end of the cycle.
            //
            // The same `FinalRoomSequence` cycle 1 ends on, which is the point rather than a saving -
            // a cycle ends the way a cycle ends, and only what goes INTO the console differs. Its
            // `doorBehind` is the door out of room7, sealed as the break starts.
            Transform ringRoom0 = cycleTwoRooms[7];
            FinalRoomSequence cycleTwoFinal = BuildFinalRoom(ringRoom0, 0f, propMat, cycleTwoDoors[6],
                                                             cycleTwoDisplay, shaker, hand,
                                                             ShardAItemId, ShardBItemId, ShardCItemId);
            cycleTwoFinal.narration = narration;
            cycleTwo.finalRoom = cycleTwoFinal;

            // A plinth retracts a metre under its own floor, and there is nothing below cycle 2 yet -
            // but there will be, and a plinth with no housing is exactly the fault that put cycle 1's
            // console through room2-1's ceiling. Built now rather than found again later.
            BuildPlinthHousing(ringRoom0, "ConsoleHousing_Cycle2", Vector3.zero, 1.86f, 1.31f, 1.45f, floorMat);

            // THE THREE SHARDS, on the floor of the three rooms that will later win them.
            //
            // Free for now, and deliberately so: this makes cycle 2 finishable end to end before any
            // of its puzzles exist, so the ring, the console and the boundary out of it can all be
            // walked and judged on their own. Each shard moves onto the core's payout as its room is
            // built.
            Mesh shardMesh = RingShardMesh(112f, 0.17f, 0.30f, 0.055f);
            Material shardMat = MakePolishedMetalMaterial("RingShard", new Color(0.93f, 0.88f, 0.72f), 0f);
            var shardIds = new[] { ShardAItemId, ShardBItemId, ShardCItemId };
            // rooms 2, 4 and 6 - the three that see the core.
            var shardRooms = new[] { cycleTwoRooms[1], cycleTwoRooms[3], cycleTwoRooms[5] };
            for (int i = 0; i < shardIds.Length; i++)
                BuildRingShard(shardRooms[i], $"Shard_{(char)('A' + i)}", shardIds[i],
                               // Off the centre line, so it is neither in a doorway nor under the
                               // east lever - see BuildChorus for the same constraint.
                               new Vector3(1.5f, 0f, 1.8f), i * 120f, shardMesh, shardMat);
            cycleTwo.worldRoot = cycleTwoRoot;

            cycleTwo.wallPanels = cycleTwoDisplay;

            // Everything down there stands on a floor one storey below zero, and every carryable has
            // to be told so or it falls through it. Nothing is carryable in cycle 2 yet; the call is
            // here so that stops being true safely.
            SetFloorBase(cycleTwoRoot, -StoreyDrop);

            GameObject loopGO = new GameObject("LoopManager");
            LoopManager loop = loopGO.AddComponent<LoopManager>();
            loop.loopDuration = 60f;
            loop.cycles = new[] { cycleOne, cycleTwo };
            loop.playerRecorder = recorder;
            loop.playerController = fpc;
            loop.playerHand = hand;
            loop.ghostPrefab = ghostPrefab;
            loop.ghostParent = ghostParent.transform;
            loop.iterationLabel = label;
            loop.wakeUpSequence = wakeUp;
            loop.narration = narration;
            loop.ambience = ambience;
            loop.cameraShaker = shaker;
            loop.endingSequence = ending;
            loop.calibration = calibration;
            // Both live on the HUD canvas, found the same way the wall sign finds the control it
            // teaches. Reset and driven at a cycle boundary respectively.
            loop.endCycleControl = canvas.GetComponentInChildren<EndCycleControl>(true);
            loop.sleepingGas = canvas.GetComponentInChildren<SleepingGas>(true);
            // The room-side half of the gas. The wash on the canvas is what it feels like; the vapour
            // is what it looks like.
            if (loop.sleepingGas != null) loop.sleepingGas.emitters = cycleTwoGas;

            // THE PROBES ARE BAKED HERE, LAST, AND THAT IS A FIX RATHER THAN A TIDY-UP.
            //
            // They were baked where they are built, inside BuildShell, and it did not survive: every
            // probe in the saved scene came back with a NULL bakedTexture even though all seven .exr
            // files were on disk and correct. BuildShell runs before SetupLighting, and changing the
            // scene's lighting after a bake is what drops the reference - so the game shipped with
            // seven reflection probes that reflected nothing at all.
            //
            // What that silently cost is much wider than the probes: the walls are deliberately
            // glossy (smoothness 0.85, see ApplySurfaceDetail) and that whole decision is written
            // down as only working "because the reflection probes give it the room to mirror". They
            // were not. And it is the real reason behind this project's "cannot light a pure metal"
            // rule - a metal is ALL reflection, so a metal with an empty probe has nothing to be.
            //
            // Baking last is also strictly better than baking early: the rooms are furnished by now,
            // so what a glossy wall mirrors is the room as the player sees it rather than a bare
            // shell. Before the save, because the reference lives in the scene.
            // Before the bake, because it adds an AudioSource child to every carryable and the sweep
            // that marks static renderers walks the scene as it finds it. A scene walk rather than a
            // line in every room's builder: a new carryable anywhere gets the behaviour without its
            // author having to know the rule exists.
            AddFallingToEveryCarryable();

            BakeReflectionProbes();

            // ASLEEP UNTIL ITS TURN - and AFTER the bake, which is the whole reason this is here
            // rather than beside the rest of cycle 2's wiring.
            //
            // A probe renders the scene from its own position, and a disabled renderer does not
            // render. Deactivated first, room2-1 and room2-2 baked an empty room: their .exr files
            // came out half the size of every other room's, which is what a cubemap of nothing
            // compresses to. Walls at 0.85 smoothness are almost entirely what they reflect, so that
            // would have shipped as two rooms lit by a void.
            //
            // WHY DEACTIVATE AT ALL. Frustum culling already keeps an unseen cycle off the screen for
            // nothing, but it does NOT stop scripts: about two hundred components in this scene poll
            // every frame - fifty carryables, fifty falls, seventy balloons, the pads, the doors - and
            // they run whether or not anybody is in their cycle. This stops all of it, drops the
            // renderers and lights, and unregisters the carryables through their own OnDisable, which
            // is exactly right: a cycle nobody can reach should not be in ItemRegistry's sweep.
            //
            // LoopManager wakes the next one before the hatch opens - the player has to be able to
            // look down through it and see the bed - and puts the old one away behind the eyelids.
            SleepCycle(cycleTwoRoot);

            Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);

            // Grabbed while the room is still the open scene: this is the frame the title screen
            // sits behind. The player is posed at the BED for it, not left where they actually
            // start - since the calibration room they spawn in is an empty white box, and a menu
            // advertising that would be advertising the wrong game. Safe to move them here because
            // the scene has already been saved above, and the in-memory edit is discarded when the
            // room is reopened from disk at the end of this method.
            player.transform.SetPositionAndRotation(bedSpawn.position, bedSpawn.rotation);
            CaptureMenuBackground(player.GetComponentInChildren<Camera>());

            BuildMainMenuScene();

            // The build settings scene list was empty, so a standalone player would have shipped
            // with no scenes at all. Reasserted on every build rather than set once, because the
            // list lives in ProjectSettings and nothing else here maintains it.
            //
            // MainMenu is index 0, so that is where a standalone player opens.
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(MenuScenePath, true),
                new EditorBuildSettingsScene(ScenePath, true),
            };

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // BuildMainMenuScene left the menu open. Put the room back, so building from the GUI
            // leaves the Editor looking at the thing that was just built.
            EditorSceneManager.OpenScene(ScenePath);

            Debug.Log("[SceneBuilder] IterationRoom scene built at " + ScenePath);
            Debug.Log("[SceneBuilder] MainMenu scene built at " + MenuScenePath);
        }

        // Renders the player camera to a PNG for the title screen to sit behind. It is literally
        // the first frame of the game - the view down the room from the foot of the bed, which is
        // exactly what the Editor's Game view shows before Play is pressed.
        //
        // Regenerated on every build that can render, so the menu can never advertise a room that
        // no longer exists. Under `-nographics` there is no device to render with: the capture is
        // SKIPPED and the previous PNG stands, because a stale background is a far better outcome
        // than a failed build. A frame that renders but comes back WRONG is held to the same rule,
        // and the tripwire below is what makes "wrong" something this can actually tell.
        private static void CaptureMenuBackground(Camera cam)
        {
            if (cam == null)
            {
                Debug.LogWarning("[SceneBuilder] No player camera; menu background not captured.");
                return;
            }

            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                Debug.LogWarning("[SceneBuilder] No graphics device (-nographics): keeping the existing "
                               + "menu background. Rebuild from the Editor to refresh it.");
                return;
            }

            const int width = 1920, height = 1080;

            RenderTexture rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            RenderTexture previousActive = RenderTexture.active;
            RenderTexture previousTarget = cam.targetTexture;
            CameraClearFlags previousFlags = cam.clearFlags;
            Color previousBackground = cam.backgroundColor;
            Texture2D shot = null;
            byte[] png = null;
            float stray = 1f;

            try
            {
                cam.targetTexture = rt;

                // The tripwire. The camera clears to Skybox in play, and the rooms are sealed boxes
                // with the camera INSIDE one, so a correct frame is geometry edge to edge and the
                // clear never shows - which means the clear colour reaching the PNG is proof that
                // something did not draw, or that the camera is not where it should be. Clearing to
                // magenta instead makes that proof readable. It cannot change a good frame, because
                // a good frame contains none of it.
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = MenuCaptureTripwire;

                shot = new Texture2D(width, height, TextureFormat.RGB24, false);

                // Rendered up to TWICE. The frame this has been seen to get wrong was the first one
                // after the URP asset was rewritten earlier in the same build: URP drops and
                // rebuilds its pipeline instance when its asset changes, and a request submitted
                // into that window came back as bare skybox with every lit surface missing. A
                // second request is enough, and costs one frame on the build that needs it.
                const int attempts = 2;
                for (int attempt = 1; attempt <= attempts && png == null; attempt++)
                {
                    // URP does not support a bare Camera.Render() from arbitrary code - a render
                    // request is the supported route, and it is also what actually runs the volume
                    // stack (tonemapping, bloom, vignette) the room's look is tuned against. The
                    // fallback is there for a pipeline that does not advertise the request.
                    var request = new UniversalRenderPipeline.SingleCameraRequest { destination = rt };
                    if (RenderPipeline.SupportsRenderRequest(cam, request))
                        RenderPipeline.SubmitRenderRequest(cam, request);
                    else
                        cam.Render();

                    RenderTexture.active = rt;
                    shot.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
                    shot.Apply();

                    stray = StrayClearFraction(shot);
                    if (stray <= MenuCaptureMaxStray)
                        png = shot.EncodeToPNG();
                    else if (attempt < attempts)
                        Debug.LogWarning($"[SceneBuilder] Menu background attempt {attempt}: {stray:P1} of the "
                                       + "frame is clear colour, so the room did not render. Retrying.");
                }
            }
            finally
            {
                // Restored in a finally: leaving a target texture on the player camera would mean
                // the game renders into a RenderTexture instead of the screen, and the saved scene
                // would carry it. The clear flags go back for the same reason - the room is sealed
                // so it would never show, but the scene should not carry a setting made for a
                // one-off capture.
                cam.targetTexture = previousTarget;
                cam.clearFlags = previousFlags;
                cam.backgroundColor = previousBackground;
                RenderTexture.active = previousActive;
                if (shot != null) Object.DestroyImmediate(shot);
                rt.Release();
                Object.DestroyImmediate(rt);
            }

            // Nothing written, so the previous PNG stands - same call as `-nographics`. An ERROR
            // rather than a warning because this one already shipped once: a skybox went to itch.io
            // behind the title screen and the build reported success.
            if (png == null)
            {
                Debug.LogError($"[SceneBuilder] Menu background NOT captured - {stray:P1} of the frame came back "
                             + "clear colour on both attempts. The previous PNG is kept. Build again from the "
                             + "Editor; if it persists, the camera is no longer inside the room.");
                return;
            }

            File.WriteAllBytes(MenuBackgroundPath, png);

            AssetDatabase.ImportAsset(MenuBackgroundPath, ImportAssetOptions.ForceUpdate);
            if (AssetImporter.GetAtPath(MenuBackgroundPath) is TextureImporter importer)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.mipmapEnabled = false;
                importer.maxTextureSize = 2048;
                importer.SaveAndReimport();
            }

            Debug.Log($"[SceneBuilder] Menu background captured to {MenuBackgroundPath}");
        }

        // What fraction of the frame is still wearing the camera's clear colour. Compared with a
        // tolerance rather than for equality because the readback is sRGB-converted, and only on the
        // two channels that separate magenta from anything the room contains.
        private static float StrayClearFraction(Texture2D shot)
        {
            Color32 want = MenuCaptureTripwire;
            Color32[] pixels = shot.GetPixels32();
            int stray = 0;
            for (int i = 0; i < pixels.Length; i++)
            {
                Color32 p = pixels[i];
                if (Mathf.Abs(p.r - want.r) < 56 && Mathf.Abs(p.g - want.g) < 56 && Mathf.Abs(p.b - want.b) < 56)
                    stray++;
            }
            return pixels.Length == 0 ? 1f : (float)stray / pixels.Length;
        }

        private static void EnsureFolders()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Editor")) AssetDatabase.CreateFolder("Assets", "Editor");
            if (!AssetDatabase.IsValidFolder(MaterialsDir)) AssetDatabase.CreateFolder("Assets", "Materials");
            if (!AssetDatabase.IsValidFolder(PrefabsDir)) AssetDatabase.CreateFolder("Assets", "Prefabs");
            if (!AssetDatabase.IsValidFolder(SettingsDir)) AssetDatabase.CreateFolder("Assets", "Settings");
            if (!AssetDatabase.IsValidFolder(TexturesDir)) AssetDatabase.CreateFolder("Assets", "Textures");
            if (!AssetDatabase.IsValidFolder(AudioDir)) AssetDatabase.CreateFolder("Assets", "Audio");
            if (!AssetDatabase.IsValidFolder(VoiceDir)) AssetDatabase.CreateFolder(AudioDir, "Voice");
            if (!AssetDatabase.IsValidFolder(SfxDir)) AssetDatabase.CreateFolder(AudioDir, "SFX");
            if (!AssetDatabase.IsValidFolder(IconsDir)) AssetDatabase.CreateFolder(TexturesDir, "Icons");
        }

        // Layers have to exist in ProjectSettings/TagManager.asset before anything can be put on
        // one, and there is no scripting API that creates them - the settings asset is edited
        // directly. Idempotent by name, so a rebuild reuses the slot instead of burning a new one
        // every time (there are only 24 user layers, and this runs on every build).
        private static int EnsureLayer(string layerName)
        {
            int existing = LayerMask.NameToLayer(layerName);
            if (existing >= 0) return existing;

            Object[] assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (assets == null || assets.Length == 0)
            {
                Debug.LogWarning($"[SceneBuilder] TagManager.asset unreadable; '{layerName}' stays on Default.");
                return 0;
            }

            SerializedObject tagManager = new SerializedObject(assets[0]);
            SerializedProperty layers = tagManager.FindProperty("layers");

            // 0-7 are Unity's own - Default, TransparentFX, Ignore Raycast, Water, UI and three
            // reserved blanks. The blanks look free and are not: writing into one is silently
            // dropped, so the search starts at 8.
            for (int i = 8; i < layers.arraySize; i++)
            {
                SerializedProperty slot = layers.GetArrayElementAtIndex(i);
                if (!string.IsNullOrEmpty(slot.stringValue)) continue;

                slot.stringValue = layerName;
                tagManager.ApplyModifiedProperties();
                AssetDatabase.SaveAssets();
                Debug.Log($"[SceneBuilder] Created layer '{layerName}' at index {i}.");
                return i;
            }

            Debug.LogWarning($"[SceneBuilder] No free user layer for '{layerName}'; staying on Default.");
            return 0;
        }

        // Ambient occlusion is a renderer feature on the URP renderer asset rather than a volume
        // override, so it lives outside the scene and has to be reconciled separately. Adding it
        // is also not idempotent on its own - a retried add silently stacks duplicates - so this
        // collapses the list back down to exactly one configured instance.
        private static void ConfigureAmbientOcclusion()
        {
            var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>($"{SettingsDir}/IterationRenderer.asset");
            if (rendererData == null) return;

            ScriptableRendererFeature ssao = rendererData.rendererFeatures
                .Find(f => f != null && f.GetType().Name == "ScreenSpaceAmbientOcclusion");
            if (ssao == null) return;

            SerializedObject so = new SerializedObject(ssao);
            // Deliberately a tight contact-shadow radius. Anything wide (0.12 was tried) throws a
            // broad dithered halo around small objects - the door button, the door edges - and on
            // flat white walls that reads as dirt rather than shading.
            so.FindProperty("m_Settings.Radius").floatValue = 0.045f;
            so.FindProperty("m_Settings.Intensity").floatValue = 0.7f;
            so.FindProperty("m_Settings.DirectLightingStrength").floatValue = 0.25f;
            // Interleaved gradient resolves cleaner than blue noise once the radius is this small.
            so.FindProperty("m_Settings.AOMethod").enumValueIndex = 1;
            // These enums run High=0, Medium=1, Low=2 - 0 is the BEST quality, which reads
            // backwards. Setting Samples to 2 drops it to 4 samples and sprays occlusion noise
            // around every object; that is not a lighting bug, it's this field.
            so.FindProperty("m_Settings.Samples").enumValueIndex = 0;
            so.FindProperty("m_Settings.NormalSamples").enumValueIndex = 0;
            so.FindProperty("m_Settings.BlurQuality").enumValueIndex = 0;
            // HALF RESOLUTION. SSAO is a full-screen pass and this is a WebGL build; running it at
            // full res was costing a lot for very little, because the radius above is 0.045 - the
            // occlusion it draws is a thin contact line under objects, and a thin line survives
            // being resolved at half res. Turn this back off if the contact shading starts to
            // crawl along the door edges.
            so.FindProperty("m_Settings.Downsample").boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(ssao);
            AssetDatabase.SaveAssets();
        }

        // Post-processing is where most of the "not a flat whitebox" impression comes from.
        private static void BuildPostProcessing()
        {
            string profilePath = $"{SettingsDir}/IterationVolume.asset";
            VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(profilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, profilePath);
            }

            Tonemapping tonemapping = GetOrAddOverride<Tonemapping>(profile);
            tonemapping.mode.overrideState = true;
            // Neutral, not ACES: ACES crushes the near-white walls into grey and warms them.
            tonemapping.mode.value = TonemappingMode.Neutral;

            Vignette vignette = GetOrAddOverride<Vignette>(profile);
            vignette.intensity.overrideState = true;
            vignette.intensity.value = 0.25f;
            vignette.smoothness.overrideState = true;
            vignette.smoothness.value = 0.4f;

            Bloom bloom = GetOrAddOverride<Bloom>(profile);
            bloom.threshold.overrideState = true;
            // The walls sit near 1.0 luminance, so a default threshold would bloom the entire room.
            bloom.threshold.value = 1.2f;
            bloom.intensity.overrideState = true;
            bloom.intensity.value = 0.2f;

            ColorAdjustments color = GetOrAddOverride<ColorAdjustments>(profile);
            color.contrast.overrideState = true;
            color.contrast.value = 8f;

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();

            GameObject go = new GameObject("PostProcessing");
            Volume volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 0f;
            volume.sharedProfile = profile;
        }

        private static T GetOrAddOverride<T>(VolumeProfile profile) where T : VolumeComponent
        {
            if (profile.TryGet(out T existing)) return existing;

            T added = profile.Add<T>(true);
            // Volume overrides live as sub-assets of the profile; without this they are lost on reload.
            AssetDatabase.AddObjectToAsset(added, profile);
            return added;
        }

        // URP has no "Standard" shader and Built-in has no URP Lit, so resolve whichever the
        // project is actually on rather than hardcoding one and silently producing magenta.
        private static Shader OpaqueShader()
        {
            Shader urp = Shader.Find("Universal Render Pipeline/Lit");
            return urp != null ? urp : Shader.Find("Standard");
        }

        // Standard calls it _Glossiness, URP Lit calls it _Smoothness.
        private static void SetSmoothness(Material mat, float value)
        {
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", value);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", value);
        }

        // URP gates additional (non-main) lights behind the pipeline asset, and the defaults are
        // tuned for mobile: too few lights per object and no shadows from them. Reconciled here
        // rather than left to the asset, so the scene's lighting can't be broken by an asset reset.
        private static void ConfigureLightingPipeline()
        {
            var urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>($"{SettingsDir}/IterationURP.asset");
            if (urp == null) return;

            SerializedObject so = new SerializedObject(urp);

            // Enums are set BY NAME, never by index. URP serialises this one as
            // [Disabled, PerPixel, PerVertex] - PerPixel is index 1, not the 2 you would guess
            // from the inspector's ordering, and picking 2 silently gives per-vertex lighting,
            // which washes the room into flat blocks: precisely the look this change exists to fix.
            SetEnumByName(so, "m_AdditionalLightsRenderingMode", "PerPixel");
            // Every surface in the room is reached by all six fixtures, and anything under this
            // limit means a slab silently drops the lights beyond it. 8 is URP's maximum.
            SetIfPresent(so, "m_AdditionalLightsPerObjectLimit", 8);
            SetIfPresent(so, "m_AdditionalLightShadowsSupported", true);
            // Also an enum, not a pixel count - assigning 2048 as an int throws "enum index is out
            // of range". One shared atlas holds every additional light's shadow map.
            //
            // 2048. This was 4096 on the grounds that SIX shadowed fixtures shared the atlas and
            // 2048 made URP log "Reduced additional punctual light shadows resolution by 2 to make
            // 6 shadow maps fit" and drop each map to 512. **There are four now** - only Room1's
            // ceiling casts, and the count has moved since - so 2048 gives each of them a 1024 map
            // with no reduction, at a quarter of the atlas. If the shadowed count ever climbs past
            // four, watch the console for that exact warning and put this back.
            SetEnumByName(so, "m_AdditionalLightsShadowmapResolution", "_2048");
            SetIfPresent(so, "m_SoftShadowsSupported", true);

            // OFF, because there is no main light. URP's main light is the brightest DIRECTIONAL
            // light and this project deletes the scene's - the rooms are sealed boxes with a
            // ceiling slab, so a sun has no way in. Keeping its shadows enabled reserved a 2048 map
            // and a shadow pass for a light that does not exist.
            SetIfPresent(so, "m_MainLightShadowsSupported", false);

            // 15, not 30. Shadows are cast only in Room1, which is 10.5m deep - 30m of shadow
            // distance was reaching two rooms past anything that casts. Halving it also doubles the
            // effective texel density of what is left, so this is a quality gain as much as a cost
            // one.
            SetFloatIfPresent(so, "m_ShadowDistance", 15f);

            // GhostFaint samples _CameraDepthTexture to fade where a ghost crosses solid geometry,
            // and that texture only exists if something asks for it. SSAO happens to request depth
            // as a pass input today, which is exactly the kind of accident that breaks the day the
            // renderer feature is retuned - so the dependency is declared here rather than relied on.
            SetIfPresent(so, "m_RequireDepthTexture", true);

            // URP GATES BOX PROJECTION AND PROBE BLENDING AT THE ASSET, and both were OFF. Setting
            // `probe.boxProjection = true` on the component - which BuildReflectionProbe has always
            // done, with a comment explaining why this room shape needs it - does nothing at all
            // while this is false. Blending matters at the doorways, where two rooms' probes
            // overlap and an unblended switch pops as the player walks through.
            SetIfPresent(so, "m_ReflectionProbeBoxProjection", true);
            SetIfPresent(so, "m_ReflectionProbeBlending", true);

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(urp);
            AssetDatabase.SaveAssets();
        }

        private static void SetIfPresent(SerializedObject so, string path, int value)
        {
            SerializedProperty p = so.FindProperty(path);
            if (p != null && p.propertyType != SerializedPropertyType.Enum) p.intValue = value;
        }

        // Resolves the index from the serialised enum's own name list, so a reordered or extended
        // URP enum can't quietly select a different mode.
        private static void SetEnumByName(SerializedObject so, string path, string valueName)
        {
            SerializedProperty p = so.FindProperty(path);
            if (p == null || p.propertyType != SerializedPropertyType.Enum) return;

            int index = System.Array.IndexOf(p.enumNames, valueName);
            if (index < 0)
            {
                Debug.LogWarning($"[SceneBuilder] {path} has no value '{valueName}' - left at "
                    + p.enumNames[p.enumValueIndex] + ". Options: " + string.Join(", ", p.enumNames));
                return;
            }
            p.enumValueIndex = index;
        }

        private static void SetFloatIfPresent(SerializedObject so, string path, float value)
        {
            SerializedProperty p = so.FindProperty(path);
            if (p != null && p.propertyType == SerializedPropertyType.Float) p.floatValue = value;
        }

        private static void SetIfPresent(SerializedObject so, string path, bool value)
        {
            SerializedProperty p = so.FindProperty(path);
            if (p != null) p.boolValue = value;
        }

        private static void SetupLighting()
        {
            // Even, clinical-white lighting: carry almost all of it on flat ambient (which hits every
            // surface equally, including the ceiling, which a directional light never reaches) and
            // leave the directional as a weak, steeply-angled source purely for enough shading to
            // read geometry. A strong low-angle directional blew out one wall while the opposite
            // wall read grey.
            // Trilight, NOT Flat - and this is the single most important line in here.
            //
            // The room used to be lit by flat ambient at (0.95, 0.95, 0.97) with no lights in it
            // at all, so ambient was doing 100% of the lighting. That is exactly why it read as a
            // whitebox: flat ambient adds the *same* amount to every surface regardless of
            // orientation, distance or occlusion, so wall, ceiling and floor all came out
            // identical. No falloff, no direction, no contrast, nothing to read depth from.
            //
            // Real fixtures do the lighting now (BuildCeilingLights). Ambient is demoted to
            // standing in for the bounce URP isn't computing, and Trilight makes that stand-in
            // directional: it lights a surface by which way it faces. That maps straight onto the
            // problem downlights have - they hammer the floor and never touch the ceiling.
            //   ground  -> hits DOWNWARD faces, i.e. the ceiling. Highest of the three, standing in
            //             for light kicked back up off the bright floor.
            //   equator -> the walls.
            //   sky     -> hits UPWARD faces, i.e. the floor, which the spots already cover. Lowest,
            //             or the floor blows out to featureless white.
            // Expect the ground colour to bleed onto the walls too - that's spherical harmonics
            // doing what bounce would, and it's why the walls read lit rather than painted.
            //
            // Tuned by eye in play mode, 2026-08-10, over two passes. (The IMGUI panel that was
            // used to find these has since been deleted - the values are settled.)
            //
            // The fixtures came down (15 -> 9) and the floor band with them, but the walls went
            // back UP (0.644) after a pass at 0.138 - at that level the spots' falloff toward the
            // corners was reading as gloom rather than as shape, and the room lost the clinical
            // evenness it is supposed to have. So the shape of it is: floor lowest (0.155, the
            // spots already hammer it), walls and ceiling high and close together (0.644 / 0.719),
            // which is what a room lit by recessed panels and white paint actually looks like.
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor     = new Color(0.155f, 0.155f, 0.175f);
            RenderSettings.ambientEquatorColor = new Color(0.644f, 0.644f, 0.664f);
            RenderSettings.ambientGroundColor  = new Color(0.719f, 0.719f, 0.739f);
            RenderSettings.ambientIntensity = 1f;
            // Assigning the colours does NOT rebuild the ambient probe. Without this they are
            // stored and never reach a shader, and every tweak looks like it did nothing.
            DynamicGI.UpdateEnvironment();

            // Reflections now come from per-room probes that see the actual white room, so this no
            // longer has to be crushed to 0.1 to stop the blue sky bleeding onto the panels.
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            RenderSettings.reflectionIntensity = 1f;

            // The default Directional Light is deleted, not dimmed. These rooms are sealed boxes
            // with a ceiling slab over them - a sun has no way in, so it contributed exactly
            // nothing while still costing a shadow pass. (It was even less use once the corner
            // seams and the door pocket were closed.)
            foreach (Light light in Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude))
                if (light.type == LightType.Directional) Object.DestroyImmediate(light.gameObject);
        }

        // Recessed ceiling fixtures - the actual light in the room.
        //
        // Spots rather than points, aimed straight down, because a downlight's cone is what gives
        // the floor bright pools and lets the walls fall off toward the corners. That gradient is
        // most of what separates a room from a whitebox; a point light just washes everything.
        //
        // castShadows is off for Room2: shadows from additional lights all share one atlas, and
        // Room2 is an empty shell with nothing in it to cast any.
        // xCenter is 0 for every room in the north-south chain and non-zero only for Room2's two side
        // rooms, which sit off that axis.
        //
        // Returns the four Lights AND the four emissive panels they sit in, because one room's
        // fixtures are now something a puzzle drives: Room2West starts dark and comes up when its
        // board is finished. The panels come back too because dimming the lights alone leaves four
        // bright ceiling tiles lighting nothing, which reads as broken rather than as off.
        private static (Light[] lights, Renderer[] panels) BuildCeilingLights(Transform parent, string roomName, float zCenter, Material emissiveMat, bool castShadows, float xCenter = 0f)
        {
            GameObject root = new GameObject(roomName + "_CeilingLights");
            root.transform.SetParent(parent, false);

            var built = new System.Collections.Generic.List<Light>();
            var panels = new System.Collections.Generic.List<Renderer>();

            // Two columns on the wall-panel rhythm, two rows down the room's length.
            float[] xs = { xCenter - GridCellWidth, xCenter + GridCellWidth };
            float[] zs = { zCenter - 2.6f, zCenter + 2.6f };

            const float panelSize = 1.4f;
            const float panelThickness = 0.04f;

            int index = 0;
            foreach (float x in xs)
            {
                foreach (float z in zs)
                {
                    GameObject fixture = new GameObject($"Fixture_{index++}");
                    fixture.transform.SetParent(root.transform, false);
                    fixture.transform.localPosition = new Vector3(x, RoomHeight, z);

                    // Sits just under the ceiling plane so it reads as set into it. Emission is
                    // pushed well past 1.0 so it clears the deliberately high bloom threshold.
                    GameObject panel = Prim(PrimitiveType.Cube, "Panel", fixture.transform,
                        new Vector3(0f, -panelThickness / 2f, 0f),
                        new Vector3(panelSize, panelThickness, panelSize),
                        emissiveMat, removeCollider: true);
                    panels.Add(panel.GetComponent<Renderer>());

                    GameObject lightGO = new GameObject("Light");
                    lightGO.transform.SetParent(fixture.transform, false);
                    lightGO.transform.localPosition = new Vector3(0f, -panelThickness, 0f);
                    lightGO.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

                    Light light = lightGO.AddComponent<Light>();
                    light.type = LightType.Spot;
                    // Wide and soft-edged, so it behaves like a panel rather than a torch.
                    light.spotAngle = 130f;
                    light.innerSpotAngle = 45f;
                    light.range = 11f;
                    // Found by eye against Neutral tonemapping, in play mode.
                    // A 130-degree cone from over 5m up spreads its energy over most of the room,
                    // so this reads lower than it is: at 4 the room came out a dim grey.
                    //
                    // History, because it has moved three times for three different reasons: 11
                    // with six fixtures, 15 when the count dropped to four, 9 once the ambient fill
                    // was cut back (the fill had been doing more of the lighting than it looked),
                    // and now 10.5 because the golden-ratio grid raised the ceiling.
                    //
                    // That last one is DERIVED, not tuned: the fixtures moved from 5.0m to 5.408m,
                    // and 9 * (5.408/5.0)^2 = 10.5 holds the floor at the brightness that was
                    // signed off at the lower ceiling. Inverse square is an approximation here -
                    // the cone also spreads wider from higher up - so treat it as a starting point
                    // and check it by eye.
                    light.intensity = 10.5f;
                    // Barely off white - clinical rather than domestic, without tinting the room.
                    light.color = new Color(0.99f, 0.99f, 1f);
                    light.shadows = castShadows ? LightShadows.Soft : LightShadows.None;
                    light.shadowStrength = 0.75f;
                    light.renderMode = LightRenderMode.ForcePixel;
                    built.Add(light);
                }
            }

            return (built.ToArray(), panels.ToArray());
        }

        // Surface detail, generated rather than sourced, so the project stays reproducible from
        // scripts like everything else here.
        //
        // The room's walls and slabs are perfectly uniform flat colour, which is the other half of
        // why it read as a whitebox: real lights now sweep across them, but there is no micro-relief
        // for that light to catch, so every surface still shades as one continuous gradient. A fine
        // normal map gives the light something to break up on - the difference between "painted
        // white" and "a painted white wall".
        //
        // Multi-octave value noise, made *tileable* by wrapping the lattice at each octave's period
        // (see Hash). Mathf.PerlinNoise is not tileable and would seam at every repeat.
        // The picture every wall panel shows once it fails: seven colour bars with a dark band
        // under them carrying the word ERROR.
        //
        // A TEST CARD rather than a warning sign, and that is the whole idea. A sign is something a
        // room PUTS UP, and a facility still capable of putting a sign up has not failed. This is
        // the image a display shows when it has nothing left to show - so a room built entirely out
        // of displays does not report the fault, it becomes it.
        //
        // 126 x 100 for seven exact 18px bars, uncompressed and point-filtered: block colour with
        // hard edges is precisely what DXT smears, and a soft test card is a poster of one.
        private static Texture2D MakeTestCardTexture(string name)
        {
            const int w = 126, h = 100;
            const int bandHeight = 30;          // the dark strip along the bottom
            const int bars = 7;

            // The broadcast order, brightest to darkest by luma - which is what makes a row of them
            // read as a test card rather than as a row of coloured squares.
            Color[] bar =
            {
                Color.white,
                new Color(1f, 1f, 0f),
                new Color(0f, 1f, 1f),
                new Color(0f, 1f, 0f),
                new Color(1f, 0f, 1f),
                new Color(1f, 0f, 0f),
                new Color(0f, 0f, 1f),
            };

            Color band = new Color(0.04f, 0.04f, 0.045f);   // GrooveDark, like every dark plate here
            Color ink = new Color(1f, 0.13f, 0.11f);        // and the same red as every display

            Color[] px = new Color[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    px[y * w + x] = y < bandHeight
                        ? band
                        : bar[Mathf.Clamp(x * bars / w, 0, bars - 1)];
                }
            }

            // ERROR, drawn from a 5x7 bitmap because there is no font rasteriser to hand when the
            // target is a Texture2D. Three glyphs is all five letters need.
            const int glyphScale = 3;
            int textWidth = (5 * 5 + 4) * glyphScale;       // five glyphs, four one-pixel gaps
            int originX = (w - textWidth) / 2;
            int originY = (bandHeight - 7 * glyphScale) / 2;
            string word = "ERROR";
            for (int c = 0; c < word.Length; c++)
                BlitGlyph(px, w, h, word[c], originX + c * 6 * glyphScale, originY, glyphScale, ink);

            return WriteTexture(name, w, h, px, TextureWrapMode.Repeat, FilterMode.Point);
        }

        // Rows top-to-bottom, so a glyph reads the right way up when it is written into a texture
        // whose y = 0 is the BOTTOM.
        private static void BlitGlyph(Color[] px, int w, int h, char c, int x0, int y0, int scale, Color ink)
        {
            string[] rows;
            switch (c)
            {
                case 'E': rows = new[] { "11111", "10000", "10000", "11110", "10000", "10000", "11111" }; break;
                case 'R': rows = new[] { "11110", "10001", "10001", "11110", "10100", "10010", "10001" }; break;
                case 'O': rows = new[] { "01110", "10001", "10001", "10001", "10001", "10001", "01110" }; break;
                default: return;
            }

            for (int row = 0; row < rows.Length; row++)
            {
                for (int col = 0; col < 5; col++)
                {
                    if (rows[row][col] != '1') continue;
                    for (int sy = 0; sy < scale; sy++)
                    {
                        for (int sx = 0; sx < scale; sx++)
                        {
                            int x = x0 + col * scale + sx;
                            int y = y0 + (rows.Length - 1 - row) * scale + sy;
                            if (x < 0 || x >= w || y < 0 || y >= h) continue;
                            px[y * w + x] = ink;
                        }
                    }
                }
            }
        }

        // Monochrome grain, tiled several times across a panel and scrolled a whole texture at a
        // time between frames. Deterministic through System.Random rather than UnityEngine.Random,
        // which BuildOnsets depends on being left alone.
        private static Texture2D MakeStaticTexture(string name, int size)
        {
            var rng = new System.Random(20260812);
            Color[] px = new Color[size * size];
            for (int i = 0; i < px.Length; i++)
            {
                // Weighted toward the dark end. Even static is mostly black - an even spread comes
                // out as flat grey at any distance and stops reading as noise at all.
                float v = (float)rng.NextDouble();
                v *= v;
                px[i] = new Color(v, v, v);
            }

            return WriteTexture(name, size, size, px, TextureWrapMode.Repeat, FilterMode.Point);
        }

        private static Texture2D WriteTexture(string name, int w, int h, Color[] pixels,
                                              TextureWrapMode wrap, FilterMode filter)
        {
            string path = $"{TexturesDir}/{name}.png";

            Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.SetPixels(pixels);
            tex.Apply();

            Directory.CreateDirectory(TexturesDir);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = true;
                importer.wrapMode = wrap;
                importer.filterMode = filter;
                // NPOT sizes are rescaled to the nearest power of two by default, which would turn
                // 126 x 100 into 128 x 128 and put the seven exact bars back on fractional pixels.
                importer.npotScale = TextureImporterNPOTScale.None;
                // Block colour with hard edges is the worst case for DXT, and this is nothing else.
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static Texture2D MakeNoiseNormalMap(string name, int size, float bumpStrength)
        {
            string path = $"{TexturesDir}/{name}.png";

            float[] height = new float[size * size];
            // Coarsest octave is a broad undulation like a skimmed wall; finest is near-pixel grain.
            int[] periods = { 8, 16, 32, 64 };
            float[] weights = { 0.5f, 0.28f, 0.15f, 0.07f };
            for (int o = 0; o < periods.Length; o++)
                AddNoiseOctave(height, size, periods[o], weights[o], 1000 + o * 977);

            Color[] pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // Central differences, wrapped, so the derived normals tile with the heights.
                    float dx = height[WrapIndex(x - 1, y, size)] - height[WrapIndex(x + 1, y, size)];
                    float dy = height[WrapIndex(x, y - 1, size)] - height[WrapIndex(x, y + 1, size)];
                    Vector3 n = new Vector3(dx * bumpStrength, dy * bumpStrength, 1f).normalized;
                    pixels[y * size + x] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
                }
            }

            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false, true);
            tex.SetPixels(pixels);
            tex.Apply();

            Directory.CreateDirectory(TexturesDir);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null)
            {
                // Without NormalMap the PNG imports as a colour texture and the shader reads the
                // raw RGB as a normal, which tilts every surface toward +X/+Y.
                importer.textureType = TextureImporterType.NormalMap;
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.anisoLevel = 4;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static void AddNoiseOctave(float[] height, int size, int period, float weight, int seed)
        {
            float cell = (float)size / period;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float fx = x / cell, fy = y / cell;
                    int x0 = Mathf.FloorToInt(fx), y0 = Mathf.FloorToInt(fy);
                    float tx = SmoothStep(fx - x0), ty = SmoothStep(fy - y0);

                    float bottom = Mathf.Lerp(LatticeHash(x0, y0, period, seed), LatticeHash(x0 + 1, y0, period, seed), tx);
                    float top = Mathf.Lerp(LatticeHash(x0, y0 + 1, period, seed), LatticeHash(x0 + 1, y0 + 1, period, seed), tx);
                    height[y * size + x] += Mathf.Lerp(bottom, top, ty) * weight;
                }
            }
        }

        private static float SmoothStep(float t) => t * t * (3f - 2f * t);

        private static int WrapIndex(int x, int y, int size)
        {
            x = ((x % size) + size) % size;
            y = ((y % size) + size) % size;
            return y * size + x;
        }

        // The lattice coordinate wraps at `period`, which is precisely what makes each octave -
        // and therefore the whole map - tile seamlessly.
        private static float LatticeHash(int x, int y, int period, int seed)
        {
            x = ((x % period) + period) % period;
            y = ((y % period) + period) % period;
            unchecked
            {
                int h = x * 374761393 + y * 668265263 + seed * 1274126177;
                h = (h ^ (h >> 13)) * 1274126177;
                return ((h ^ (h >> 16)) & 0x7fffffff) / (float)0x7fffffff;
            }
        }

        // A tiny software rasteriser for the HUD's glyphs. They are drawn from code for the same
        // reason the wall grain and the narration are: everything here has to rebuild from a
        // script, and a folder of sourced PNGs was exactly the part that could not.
        //
        // Shapes are predicates over a 0..1 square with the origin bottom-left, matching Unity's
        // texture coordinates so a row index needs no flipping. Coverage is supersampled 4x4, which
        // is the whole reason a 128px disc has a clean edge rather than a staircase.
        private sealed class IconCanvas
        {
            private const int Supersample = 4;

            private readonly int size;
            private readonly float[] coverage;

            public IconCanvas(int size)
            {
                this.size = size;
                coverage = new float[size * size];
            }

            // sign -1 cuts back out of what is already drawn - the hole in a key's bow, the hollow
            // inside the mouse outline. Predicates compose, so an intersection is just &&.
            public void Shape(System.Func<Vector2, bool> inside, float sign = 1f)
            {
                float step = 1f / (size * Supersample);
                const float samples = Supersample * Supersample;

                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float hits = 0f;
                        for (int sy = 0; sy < Supersample; sy++)
                            for (int sx = 0; sx < Supersample; sx++)
                            {
                                Vector2 p = new Vector2(
                                    (x * Supersample + sx + 0.5f) * step,
                                    (y * Supersample + sy + 0.5f) * step);
                                if (inside(p)) hits++;
                            }

                        if (hits == 0f) continue;
                        int i = y * size + x;
                        coverage[i] = Mathf.Clamp01(coverage[i] + sign * (hits / samples));
                    }
                }
            }

            public void Disc(Vector2 centre, float radius, float sign = 1f) =>
                Shape(p => (p - centre).sqrMagnitude <= radius * radius, sign);

            public void Ring(Vector2 centre, float outer, float inner, float sign = 1f) =>
                Shape(p =>
                {
                    float sqr = (p - centre).sqrMagnitude;
                    return sqr <= outer * outer && sqr >= inner * inner;
                }, sign);

            // Described by centre, half extents and an angle rather than by four corners, so the
            // needle and its handle can share one diagonal.
            public void Bar(Vector2 centre, Vector2 halfExtents, float degrees = 0f, float sign = 1f)
            {
                float rad = -degrees * Mathf.Deg2Rad;
                float cos = Mathf.Cos(rad), sin = Mathf.Sin(rad);

                Shape(p =>
                {
                    Vector2 d = p - centre;
                    // Un-rotate the sample rather than rotating the rectangle: an axis-aligned
                    // containment test is the only one that stays trivially correct.
                    Vector2 local = new Vector2(d.x * cos - d.y * sin, d.x * sin + d.y * cos);
                    return Mathf.Abs(local.x) <= halfExtents.x && Mathf.Abs(local.y) <= halfExtents.y;
                }, sign);
            }

            // The same glyph as an OPAQUE picture, for the ones that go on an object in the world
            // rather than in the HUD. Alpha is not an option there: a lit surface with the shape in
            // its alpha is a white card, because nothing in this project reads alpha off an opaque
            // material. The coverage becomes ink over paper instead.
            public Texture2D ToOpaqueTexture(string name, Color paper, Color ink)
            {
                Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = name };
                Color[] pixels = new Color[size * size];
                for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.Lerp(paper, ink, coverage[i]);
                tex.SetPixels(pixels);
                tex.Apply();
                return tex;
            }

            // White, with the whole shape carried in alpha: the HUD tints these through
            // Image.color, and a glyph with baked-in colour could not be recoloured to match.
            public Texture2D ToTexture(string name)
            {
                Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = name };
                Color[] pixels = new Color[size * size];
                for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color(1f, 1f, 1f, coverage[i]);
                tex.SetPixels(pixels);
                tex.Apply();
                return tex;
            }
        }

        // The same glyph as a world texture: opaque, mipmapped, and imported as a plain texture
        // rather than a sprite. Mipmaps matter here where they do not in the HUD - these are read at
        // four metres across a room, and an unmipped glyph at that distance is a shimmering mess.
        private static Texture2D SaveSymbolTexture(IconCanvas canvas, string name)
            => SaveSymbolTexture(canvas, name, SymbolPaper, SymbolInk);

        // The same, with the two values named rather than assumed. The cube room's inclusion wants
        // them INVERTED - a near-black core carrying a pale glyph - because it is read through glass
        // in a white room, where ink-on-paper is a white card inside a white cube seen against a
        // white wall. Which of the two is the glyph never changes; only which one is bright does.
        private static Texture2D SaveSymbolTexture(IconCanvas canvas, string name, Color paper, Color ink)
        {
            if (!Directory.Exists(SymbolsDir)) Directory.CreateDirectory(SymbolsDir);

            string path = $"{SymbolsDir}/{name}.png";
            Texture2D tex = canvas.ToOpaqueTexture(name, paper, ink);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            if (AssetImporter.GetAtPath(path) is TextureImporter importer)
            {
                importer.textureType = TextureImporterType.Default;
                importer.alphaIsTransparency = false;
                importer.mipmapEnabled = true;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.filterMode = FilterMode.Bilinear;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // A lit material with a picture on it, which is the first one in this building - every other
        // surface here is a flat colour. `_BaseMap` AND `mainTexture` are both set because URP reads
        // the first and a good deal of Unity's own tooling still reads the second.
        private static Material MakeSymbolMaterial(string name, Texture2D map)
        {
            string path = $"{MaterialsDir}/{name}.mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(OpaqueShader());
                AssetDatabase.CreateAsset(mat, path);
            }

            mat.shader = OpaqueShader();
            mat.color = Color.white;
            mat.SetTexture("_BaseMap", map);
            mat.mainTexture = map;
            // Matte. A glyph is being read, and a specular highlight across it is the one thing that
            // stops it being readable from an angle.
            SetSmoothness(mat, 0.08f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static Sprite SaveSprite(IconCanvas canvas, string name)
        {
            string path = $"{IconsDir}/{name}.png";
            Texture2D tex = canvas.ToTexture(name);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            if (AssetImporter.GetAtPath(path) is TextureImporter importer)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.filterMode = FilterMode.Bilinear;
                importer.wrapMode = TextureWrapMode.Clamp;
                // Uncompressed: these are a few KB each, and block compression frays the edge of a
                // glyph that is nothing but alpha.
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        // The pin, as a silhouette: a stubby grip and a long needle on one diagonal. Drawn on the
        // diagonal rather than upright because upright it reads as a nail, and because a HUD slot
        // is square - a diagonal uses the corners.
        private static Sprite PinIcon()
        {
            var icon = new IconCanvas(128);
            Vector2 along = new Vector2(0.7071f, 0.7071f);
            Vector2 mid = new Vector2(0.5f, 0.5f);
            // Everything is placed by distance along that one diagonal, so the parts stay in line
            // however they are resized.
            System.Func<float, Vector2> at = t => mid + along * t;

            // The needle is deliberately fatter than the real pin is. This is drawn at 128px and
            // displayed at 58: a truly slim needle lands on one screen pixel and disappears.
            icon.Bar(at(0.235f), new Vector2(0.030f, 0.185f), -45f);
            icon.Bar(at(-0.15f), new Vector2(0.058f, 0.19f), -45f);   // grip
            icon.Disc(at(-0.34f), 0.058f);                            // rounded butt
            icon.Disc(at(0.045f), 0.045f);                            // ferrule, where the two meet
            return SaveSprite(icon, "icon_pin");
        }

        // ---- the figure pictograms: one body, six poses --------------------------------------------
        //
        // The calibration wall used to caption its controls in words (MOVE, JUMP, SPRINT...). These
        // replace them, which is the same move the Room2 sign makes and for the same reason: the game
        // has no text elsewhere and a player who cannot read English should still be able to start it.
        //
        // ONE body plan, six poses, and that is the point rather than tidiness. Six figures each drawn
        // to look right on its own is how a set of pictograms ends up looking like six pictograms from
        // six different sets - the same head size, limb length and thickness across all of them is what
        // makes them read as one person doing different things.
        private const float FigureLimb = 0.075f;    // limb thickness, fat enough to survive 128px
        private const float FigureHead = 0.088f;    // head radius
        private const float FigureThigh = 0.20f;
        private const float FigureShin = 0.19f;
        private const float FigureUpperArm = 0.16f;
        private const float FigureForearm = 0.15f;

        // Draws a limb segment from `from` in `direction` and returns its far end, so segments chain
        // into a bent limb. Angles are given in degrees CLOCKWISE FROM STRAIGHT DOWN, because every
        // limb here hangs off a joint and down is the rest pose.
        private static Vector2 FigureBone(IconCanvas icon, Vector2 from, float degrees, float length)
        {
            float rad = degrees * Mathf.Deg2Rad;
            Vector2 dir = new Vector2(Mathf.Sin(rad), -Mathf.Cos(rad));
            Vector2 to = from + dir * length;
            // Bar takes half-extents and a rotation; the sign convention is the pin icon's.
            icon.Bar((from + to) * 0.5f, new Vector2(FigureLimb * 0.5f, length * 0.5f),
                     -Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg);
            // Joint discs at every hinge. Without them a bent limb shows the corner of one bar poking
            // past the other and the elbow reads as a break.
            icon.Disc(to, FigureLimb * 0.5f);
            return to;
        }

        // Head, spine and a joint at each end. Returns the shoulder and hip so the caller can hang
        // limbs off them, which is the only thing any of the poses differ by.
        private static (Vector2 shoulder, Vector2 hip) FigureTorso(IconCanvas icon, Vector2 hip,
                                                                   float spineLength, float leanDegrees)
        {
            float rad = leanDegrees * Mathf.Deg2Rad;
            Vector2 up = new Vector2(Mathf.Sin(rad), Mathf.Cos(rad));
            Vector2 shoulder = hip + up * spineLength;

            icon.Bar((hip + shoulder) * 0.5f, new Vector2(FigureLimb * 0.55f, spineLength * 0.5f),
                     -leanDegrees);
            icon.Disc(hip, FigureLimb * 0.5f);
            icon.Disc(shoulder, FigureLimb * 0.5f);
            // The head sits off the shoulder along the same lean, so a leaning figure's head leads.
            icon.Disc(shoulder + up * (FigureHead + 0.035f), FigureHead);
            return (shoulder, hip);
        }

        // MOVE. A mid-stride walk: one leg forward and planted, one trailing, arms opposing. Upright,
        // because upright against the run's lean is what tells the two apart at a glance - the stride
        // widths alone are too similar at this size.
        private static Sprite FigureWalkIcon()
        {
            var icon = new IconCanvas(128);
            var (shoulder, hip) = FigureTorso(icon, new Vector2(0.47f, 0.44f), 0.26f, 2f);
            FigureBone(icon, FigureBone(icon, hip, 26f, FigureThigh), 12f, FigureShin);
            FigureBone(icon, FigureBone(icon, hip, -22f, FigureThigh), -6f, FigureShin);
            FigureBone(icon, FigureBone(icon, shoulder, -20f, FigureUpperArm), -44f, FigureForearm);
            FigureBone(icon, FigureBone(icon, shoulder, 18f, FigureUpperArm), 40f, FigureForearm);
            return SaveSprite(icon, "icon_figure_walk");
        }

        // SPRINT. The same body leaning into it, with a longer stride and arms driving harder. The
        // LEAN is what reads as speed; a running figure drawn upright reads as a wider walk.
        private static Sprite FigureRunIcon()
        {
            var icon = new IconCanvas(128);
            var (shoulder, hip) = FigureTorso(icon, new Vector2(0.44f, 0.45f), 0.26f, 20f);
            FigureBone(icon, FigureBone(icon, hip, 48f, FigureThigh), 20f, FigureShin);
            FigureBone(icon, FigureBone(icon, hip, -34f, FigureThigh), -74f, FigureShin);
            FigureBone(icon, FigureBone(icon, shoulder, -52f, FigureUpperArm), -104f, FigureForearm);
            FigureBone(icon, FigureBone(icon, shoulder, 46f, FigureUpperArm), 96f, FigureForearm);
            return SaveSprite(icon, "icon_figure_run");
        }

        // CROUCH. Knees folded hard, hip dropped, spine tipped forward to balance over the feet. The
        // dropped hip is the whole silhouette: a figure with bent knees at standing height reads as
        // someone about to jump.
        private static Sprite FigureCrouchIcon()
        {
            var icon = new IconCanvas(128);
            var (shoulder, hip) = FigureTorso(icon, new Vector2(0.47f, 0.31f), 0.24f, 16f);
            FigureBone(icon, FigureBone(icon, hip, 58f, FigureThigh * 0.92f), -30f, FigureShin * 0.92f);
            FigureBone(icon, FigureBone(icon, hip, 30f, FigureThigh * 0.92f), -52f, FigureShin * 0.92f);
            FigureBone(icon, FigureBone(icon, shoulder, 30f, FigureUpperArm), 74f, FigureForearm * 0.9f);
            return SaveSprite(icon, "icon_figure_crouch");
        }

        // JUMP. Both feet off the floor and tucked the same way, arms up. Symmetry is what separates it
        // from the walk - a stride says one foot is down, and two matching legs say neither is.
        private static Sprite FigureJumpIcon()
        {
            var icon = new IconCanvas(128);
            var (shoulder, hip) = FigureTorso(icon, new Vector2(0.5f, 0.46f), 0.25f, 0f);
            FigureBone(icon, FigureBone(icon, hip, 30f, FigureThigh * 0.85f), 66f, FigureShin * 0.85f);
            FigureBone(icon, FigureBone(icon, hip, -30f, FigureThigh * 0.85f), -66f, FigureShin * 0.85f);
            FigureBone(icon, FigureBone(icon, shoulder, 156f, FigureUpperArm), 168f, FigureForearm * 0.85f);
            FigureBone(icon, FigureBone(icon, shoulder, -156f, FigureUpperArm), -168f, FigureForearm * 0.85f);
            // The floor it has left. Without it the pose is just a figure with odd legs; with it there
            // is a gap under the feet, and the gap is the jump.
            icon.Bar(new Vector2(0.5f, 0.055f), new Vector2(0.30f, 0.022f));
            return SaveSprite(icon, "icon_figure_jump");
        }

        // INTERACT. A figure with one arm out to a panel on the wall - the panel is what makes it
        // "press this" rather than "wave". Drawn as one of the room's own wall cells, because that is
        // literally what E is pressed at.
        private static Sprite FigurePressIcon()
        {
            var icon = new IconCanvas(128);
            var (shoulder, hip) = FigureTorso(icon, new Vector2(0.36f, 0.42f), 0.26f, 4f);
            FigureBone(icon, FigureBone(icon, hip, 12f, FigureThigh), 4f, FigureShin);
            FigureBone(icon, FigureBone(icon, hip, -14f, FigureThigh), -4f, FigureShin);
            // The reaching arm, straight out and level, ending at the panel.
            FigureBone(icon, FigureBone(icon, shoulder, 88f, FigureUpperArm), 92f, FigureForearm);
            FigureBone(icon, FigureBone(icon, shoulder, -14f, FigureUpperArm), -8f, FigureForearm);

            // Panel, and a groove down its left edge so it reads as set into a wall.
            icon.Bar(new Vector2(0.85f, 0.60f), new Vector2(0.075f, 0.135f));
            icon.Bar(new Vector2(0.745f, 0.60f), new Vector2(0.012f, 0.155f), 0f, -1f);
            return SaveSprite(icon, "icon_figure_press");
        }

        // LOOK AROUND. A head seen FROM ABOVE with an arc over it - the only one of the six not drawn
        // from the side, because a head turning is invisible in profile. The arc is an annulus segment
        // with a head on each end, which is a turn in both directions and therefore a look rather than
        // a glance.
        private static Sprite FigureLookIcon()
        {
            var icon = new IconCanvas(128);
            Vector2 centre = new Vector2(0.5f, 0.35f);

            icon.Disc(centre, 0.15f);
            // Nose, so the head has a facing and the arc has something to be turning.
            icon.Bar(new Vector2(centre.x, centre.y + 0.175f), new Vector2(0.035f, 0.045f));

            // The sweep: a ring, kept to its upper half and to the outside of the head.
            icon.Shape(p =>
            {
                float r = (p - centre).magnitude;
                return r > 0.245f && r < 0.315f && p.y > centre.y + 0.02f;
            });
            // Arrowheads on both ends of that arc. Placed on the ring at +-58 degrees from straight up.
            for (int s = -1; s <= 1; s += 2)
            {
                float rad = 58f * s * Mathf.Deg2Rad;
                Vector2 tip = centre + new Vector2(Mathf.Sin(rad), Mathf.Cos(rad)) * 0.28f;
                icon.Shape(p => (p - tip).magnitude < 0.075f && Vector2.Dot(p - tip, new Vector2(Mathf.Cos(rad) * s, -Mathf.Sin(rad) * s)) > 0f);
            }
            return SaveSprite(icon, "icon_figure_look");
        }

        // TAB. Not one of the six body poses - switching what is in the hand moves no limb a figure
        // could show - so this is the one calibration-wall control captioned with an object glyph
        // instead: two arrows chasing each other round a ring, the ordinary mark for "cycle through".
        // Built the same way the look icon's turn arc is, a ring cut to an arc with a half-disc
        // arrowhead at its leading end, just carried all the way round instead of stopping at a head.
        private static Sprite CycleIcon()
        {
            var icon = new IconCanvas(128);
            Vector2 centre = new Vector2(0.5f, 0.5f);
            const float outer = 0.36f, inner = 0.275f;
            const float mid = (outer + inner) * 0.5f;

            // Two arcs, opposite halves of the ring, each a little short of a true half so its
            // arrowhead has clear ring under it rather than overlapping the far arc.
            float[] starts = { 15f, 195f };
            float[] ends = { 165f, 345f };

            for (int i = 0; i < 2; i++)
            {
                float from = starts[i], to = ends[i];
                icon.Shape(p =>
                {
                    Vector2 d = p - centre;
                    float r = d.magnitude;
                    if (r < inner || r > outer) return false;
                    float deg = Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg;
                    if (deg < 0f) deg += 360f;
                    return deg >= from && deg <= to;
                });

                // The arrowhead sits at the arc's leading end - `to`, since the sweep runs clockwise -
                // a half disc cut along the tangent so it points the way the ring is turning.
                float tipRad = to * Mathf.Deg2Rad;
                Vector2 tip = centre + new Vector2(Mathf.Sin(tipRad), Mathf.Cos(tipRad)) * mid;
                float tangentRad = (to + 90f) * Mathf.Deg2Rad;
                Vector2 dir = new Vector2(Mathf.Sin(tangentRad), Mathf.Cos(tangentRad));
                icon.Shape(p => (p - tip).magnitude < 0.085f && Vector2.Dot(p - tip, dir) > 0f);
            }
            return SaveSprite(icon, "icon_cycle");
        }

        // A balloon: egg-shaped body, knot, short string. Wider at the top than a circle and narrowed
        // to the knot, because a plain disc with a string under it reads as a lollipop.
        private static Sprite BalloonIcon()
        {
            var icon = new IconCanvas(128);
            Vector2 centre = new Vector2(0.5f, 0.60f);
            icon.Shape(p =>
            {
                float dx = (p.x - centre.x) / 0.285f;
                // Narrower below the middle: that taper is the whole silhouette.
                float ry = p.y < centre.y ? 0.30f : 0.255f;
                float dy = (p.y - centre.y) / ry;
                return dx * dx + dy * dy <= 1f;
            });
            icon.Bar(new Vector2(0.5f, 0.285f), new Vector2(0.036f, 0.030f));   // knot
            icon.Bar(new Vector2(0.5f, 0.175f), new Vector2(0.014f, 0.085f));   // string
            return SaveSprite(icon, "icon_balloon");
        }

        // The same balloon a moment later: a scrap of skin still on the knot, and shards going out.
        // The shards are what carries it - a torn remnant on its own reads as a damaged balloon
        // rather than one that has just gone.
        private static Sprite BalloonBurstIcon()
        {
            var icon = new IconCanvas(128);
            Vector2 centre = new Vector2(0.5f, 0.56f);

            // Eight shards at uneven lengths. Even ones read as a sun, which is a different sign.
            float[] degrees = { 12f, 52f, 88f, 126f, 168f, 212f, 258f, 312f };
            float[] lengths = { 0.20f, 0.14f, 0.22f, 0.15f, 0.19f, 0.13f, 0.17f, 0.15f };
            for (int i = 0; i < degrees.Length; i++)
            {
                float rad = degrees[i] * Mathf.Deg2Rad;
                Vector2 dir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
                // Placed at half its own length out from the gap, so the shards do not meet in the
                // middle - the hole is what says the balloon is gone.
                Vector2 at = centre + dir * (0.105f + lengths[i] * 0.5f);
                icon.Bar(at, new Vector2(0.026f, lengths[i] * 0.5f), -degrees[i] + 90f);
            }

            icon.Bar(new Vector2(0.5f, 0.285f), new Vector2(0.036f, 0.030f));   // knot, still there
            icon.Bar(new Vector2(0.5f, 0.175f), new Vector2(0.014f, 0.085f));   // string
            return SaveSprite(icon, "icon_balloon_burst");
        }

        // A plain right-pointing arrow, for the pictogram rows. Shaft plus a solid head built from a
        // half-plane test rather than three bars, so the point cannot come out blunt at 128px.
        private static Sprite ArrowRightIcon()
        {
            var icon = new IconCanvas(128);
            icon.Bar(new Vector2(0.40f, 0.5f), new Vector2(0.24f, 0.055f));
            icon.Shape(p =>
            {
                if (p.x < 0.60f || p.x > 0.90f) return false;
                // Half-width shrinks to nothing at the tip.
                float half = Mathf.Lerp(0.20f, 0f, (p.x - 0.60f) / 0.30f);
                return Mathf.Abs(p.y - 0.5f) <= half;
            });
            return SaveSprite(icon, "icon_arrow_right");
        }

        // The key's silhouette at HUD size: ring bow, shaft, two teeth off one side only. A
        // symmetrical bit would read as a cross. Drawn rather than rendered from gold_key.glb,
        // because at 58px the model's bit resolves to a smudge - the same call the pin icon makes.
        private static Sprite KeyIcon()
        {
            var icon = new IconCanvas(128);
            icon.Ring(new Vector2(0.5f, 0.72f), 0.20f, 0.095f);
            icon.Bar(new Vector2(0.5f, 0.35f), new Vector2(0.045f, 0.20f));
            icon.Bar(new Vector2(0.60f, 0.26f), new Vector2(0.07f, 0.042f));
            icon.Bar(new Vector2(0.60f, 0.155f), new Vector2(0.07f, 0.042f));
            return SaveSprite(icon, "icon_key");
        }

        // The grey disc both control hints sit on.
        private static Sprite HintDiscSprite()
        {
            var icon = new IconCanvas(128);
            icon.Disc(new Vector2(0.5f, 0.5f), 0.48f);
            return SaveSprite(icon, "icon_hint_disc");
        }

        // A mouse seen from above, outlined, with the left button filled - which is the entire
        // message. Built from one capsule distance function so the outline, the hollow and the
        // button are all the same shape at three insets, and none of them can drift apart.
        private static Sprite MouseLeftIcon()
        {
            var icon = new IconCanvas(128);
            const float radius = 0.215f;
            const float topY = 0.655f, bottomY = 0.345f;

            System.Func<Vector2, float> toSpine = p =>
                (p - new Vector2(0.5f, Mathf.Clamp(p.y, bottomY, topY))).magnitude;

            icon.Shape(p => toSpine(p) <= radius);
            icon.Shape(p => toSpine(p) <= radius - 0.042f, -1f);
            icon.Shape(p => toSpine(p) <= radius - 0.072f && p.x < 0.484f && p.y > 0.60f);
            return SaveSprite(icon, "icon_mouse_left");
        }

        // The same mouse with no button filled - the calibration page's "look around" row, where
        // highlighting a button would say "click", which is the one thing that row does not mean.
        // The two seams are what keep it from reading as a plain capsule.
        private static Sprite MouseIcon()
        {
            var icon = new IconCanvas(128);
            const float radius = 0.215f;
            const float topY = 0.655f, bottomY = 0.345f;

            System.Func<Vector2, float> toSpine = p =>
                (p - new Vector2(0.5f, Mathf.Clamp(p.y, bottomY, topY))).magnitude;

            icon.Shape(p => toSpine(p) <= radius);
            icon.Shape(p => toSpine(p) <= radius - 0.042f, -1f);
            const float hollow = radius - 0.042f;
            icon.Shape(p => toSpine(p) <= hollow && Mathf.Abs(p.y - 0.605f) < 0.017f);
            icon.Shape(p => toSpine(p) <= hollow && p.y > 0.605f && Mathf.Abs(p.x - 0.5f) < 0.017f);
            return SaveSprite(icon, "icon_mouse");
        }

        // A speaker with sound coming off it - "turn your volume up", over the calibration room's
        // BEGIN button. The PA and every room's audio cues are half of what the facility tells the
        // player, and this is the last screen before the loop's clock starts, so it is the one place
        // a muted or silent tab can still be caught rather than discovered mid-iteration.
        private static Sprite VolumeIcon()
        {
            var icon = new IconCanvas(128);

            // The body: a plain box, standing in for the speaker cabinet.
            icon.Bar(new Vector2(0.28f, 0.5f), new Vector2(0.07f, 0.11f));
            // The cone, widening away from the box - the same tapered half-plane test the right
            // arrow's head uses, just wider so it reads as a speaker rather than an arrow.
            icon.Shape(p =>
            {
                if (p.x < 0.28f || p.x > 0.46f) return false;
                float half = Mathf.Lerp(0.11f, 0.22f, (p.x - 0.28f) / 0.18f);
                return Mathf.Abs(p.y - 0.5f) <= half;
            });

            // Three sound waves fanning out to the right, each a band of an arc rather than a full
            // ring so they read as coming FROM the cone rather than surrounding it.
            Vector2 mouth = new Vector2(0.46f, 0.5f);
            for (int i = 0; i < 3; i++)
            {
                float r = 0.13f + i * 0.085f;
                const float thickness = 0.026f;
                icon.Shape(p =>
                {
                    Vector2 d = p - mouth;
                    float dist = d.magnitude;
                    if (dist < r - thickness || dist > r + thickness) return false;
                    float deg = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
                    return Mathf.Abs(deg) <= 55f;
                });
            }
            return SaveSprite(icon, "icon_volume");
        }

        // tiling is in repeats across one face. URP/Lit drives the normal map's UVs from _BaseMap's
        // transform, not _BumpMap's, so setting the scale on _BumpMap does nothing at all.
        private static void ApplySurfaceDetail(Material mat, Texture2D normalMap, float bumpScale, Vector2 tiling, float smoothness)
        {
            if (normalMap == null) return;

            mat.EnableKeyword("_NORMALMAP");
            mat.SetTexture("_BumpMap", normalMap);
            mat.SetFloat("_BumpScale", bumpScale);
            mat.SetTextureScale("_BaseMap", tiling);
            // Raised off the old 0.03: at that roughness there is almost no specular response, so
            // the relief only shows in diffuse shading and barely reads. Still well short of the
            // 0.5 default that mirrors the skybox onto the walls.
            SetSmoothness(mat, smoothness);
            EditorUtility.SetDirty(mat);
        }

        // A glossy surface is only as good as what it has to reflect, and the only reflection
        // source here was the procedural sky - which is blue, and which the room can't even see.
        // Raising smoothness without this paints a blue cast over every white panel; that is the
        // exact failure the old 0.03-smoothness-everywhere rule existed to dodge.
        //
        // Baked, and baked right here as part of the build. A Realtime probe was tried first, on
        // the reasoning that SceneBuilder rebuilds the scene every run so baked data would always
        // be stale - but a realtime probe renders nothing until play mode, so `probe.texture` came
        // back EMPTY and the glossy walls had nothing at all to reflect. Baking it during Build()
        // solves both: the cubemap is a real asset, and it is regenerated in lockstep with the
        // geometry it captures. It is two small cubemaps, so it costs no meaningful build time.
        //
        // Box projection matters more than usual for a room this shape - it reprojects the cubemap
        // onto the box bounds, so a reflected wall stays put on the wall instead of sliding around
        // as the camera moves.
        // xCenter and the swap flag are both for Room2's side rooms: they sit off the chain's axis, and
        // their long dimension runs along X where every other room's runs along Z - so the probe's box
        // has to be turned with the room or box projection reflects the wrong walls.
        private static void BuildReflectionProbe(Transform parent, string roomName, float zCenter,
                                                float xCenter = 0f, bool longAxisIsX = false)
        {
            GameObject go = new GameObject(roomName + "_ReflectionProbe");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(xCenter, RoomHeight * 0.5f, zCenter);

            ReflectionProbe probe = go.AddComponent<ReflectionProbe>();
            // CUSTOM, NOT BAKED, and this one word is the whole bug. A Baked probe does not carry
            // its cubemap: the reference lives in the SCENE'S LIGHTING DATA ASSET, which this build
            // never generates, so `bakedTexture` was set in memory by the bake and came back NULL
            // the moment the scene was saved and reopened. A Custom probe holds the cubemap in a
            // field on the component, which serialises with everything else. Verified by reopening
            // the saved scene and reading it back - see BakeReflectionProbes.
            probe.mode = UnityEngine.Rendering.ReflectionProbeMode.Custom;
            probe.boxProjection = true;
            probe.size = longAxisIsX
                ? new Vector3(RoomDepth, RoomHeight, RoomWidth)
                : new Vector3(RoomWidth, RoomHeight, RoomDepth);
            // 512, not 256: at the wall smoothness used here the reflection is sharp enough that a
            // 256 cubemap shows the ceiling fixtures as vague smears rather than panels.
            probe.resolution = 512;
            probe.clearFlags = ReflectionProbeClearFlags.Skybox;
            probe.nearClipPlane = 0.05f;
            probe.farClipPlane = 40f;

            // NOT BAKED HERE. It used to be, and the reference did not survive to the saved scene -
            // see BakeReflectionProbes, called at the end of Build(), for what went wrong and why
            // last is the right place for it.
        }

        // Bake every probe in the scene, whatever built it. Walking the scene rather than taking a
        // list means a room added later cannot be forgotten - which is exactly how this would break
        // again, and quietly, since an unbaked probe looks like a slightly flat room rather than
        // like an error.
        //
        // `-nographics` cannot render, and a bake is a render: the whole pass is skipped there. That
        // leaves the previous .exr files in place, which is the same bargain CaptureMenuBackground
        // makes - stale reflection data is a far better outcome than a failed build.
        private static void SleepCycle(Transform root)
        {
            if (root == null) return;
            root.gameObject.SetActive(false);
            Debug.Log($"[SceneBuilder] {root.name} starts asleep; LoopManager wakes it at the boundary.");
        }

        private static void BakeReflectionProbes()
        {
            if (skipProbeBake)
            {
                Debug.Log("[SceneBuilder] Reflection probes NOT baked (fast build); existing cubemaps left in place. "
                        + "Run the full build before judging anything reflective.");
                return;
            }

            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                Debug.Log("[SceneBuilder] Reflection probes NOT baked (-nographics); existing cubemaps left in place.");
                return;
            }

            int flagged = MarkReflectionProbeStatic();

            Directory.CreateDirectory(TexturesDir);
            ReflectionProbe[] probes = UnityEngine.Object.FindObjectsByType<ReflectionProbe>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            int wired = 0;
            foreach (ReflectionProbe probe in probes)
            {
                // The name carries the room, and the room names the cubemap - so a rebuild
                // overwrites the same asset rather than accumulating one per build.
                string roomName = probe.name.Replace("_ReflectionProbe", string.Empty);
                string path = $"{TexturesDir}/{roomName}_Reflection.exr";
                if (!Lightmapping.BakeReflectionProbe(probe, path)) continue;

                // THE ASSIGNMENT IS THE POINT, not the bake. The bake writes the .exr and that part
                // always worked - seven correct cubemaps sat on disk for as long as this was broken.
                // What was missing was a reference that survives the save, and on a Custom probe
                // that is this field. Imported explicitly first: the file has just been written
                // underneath the AssetDatabase, and loading it without the import returns null.
                AssetDatabase.ImportAsset(path);
                Cubemap cube = AssetDatabase.LoadAssetAtPath<Cubemap>(path);
                if (cube == null) continue;
                probe.customBakedTexture = cube;
                wired++;
            }

            // Reported as a count against a count, because the failure this exists to catch is a
            // probe that ends up with no cubemap - which renders as a room that looks very slightly
            // flat, and nothing else. If this ever says anything but n/n, the metal will go black.
            Debug.Log($"[SceneBuilder] Reflection probes baked and wired: {wired}/{probes.Length} " +
                      $"({flagged} renderers reflection-probe-static)");
        }

        // WHAT A BAKED PROBE CAPTURES IS STATIC GEOMETRY, AND NOTHING IN THIS SCENE WAS STATIC.
        //
        // This is the bottom of the reflection-probe bug and the last of three separate faults on
        // one feature. The other two - baking before SetupLighting invalidated it, and a Baked probe
        // keeping its cubemap in lighting data this build never generates - were both real, and
        // fixing them still left the objects mirroring a brown default skybox. The reason is that
        // `Lightmapping.BakeReflectionProbe` renders only renderers flagged ReflectionProbeStatic,
        // SceneBuilder creates every object from script and never flagged one, so all seven probes
        // were faithfully capturing an empty world. Proved by assigning the cubemap as the GLOBAL
        // reflection and getting the same brown: the probe was applying, its contents were sky.
        //
        // ONLY the ReflectionProbeStatic flag is set. ContributeGI would put this scene into
        // lightmapping it does not use, and BatchingStatic would change how it draws - neither is
        // being asked for, and a static flag set for the wrong reason is very hard to notice later.
        //
        // MOVERS ARE EXCLUDED, by the components that move them rather than by name. A door baked
        // open, or a plinth baked at the height SceneBuilder authors it at rather than the sunk
        // position it starts the game in, would put a thing in the reflection that is not in the
        // room. Everything else - shell, panels, fixtures, furniture, board - is genuinely fixed.
        private static int MarkReflectionProbeStatic()
        {
            int flagged = 0;
            Renderer[] renderers = UnityEngine.Object.FindObjectsByType<Renderer>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            foreach (Renderer r in renderers)
            {
                if (MovesDuringPlay(r.transform)) continue;
                GameObjectUtility.SetStaticEditorFlags(r.gameObject, StaticEditorFlags.ReflectionProbeStatic);
                flagged++;
            }
            return flagged;
        }

        // Walks up, because the thing that moves is usually a parent of the thing that renders - a
        // door's slab, a plinth's key, a ghost's mesh.
        private static bool MovesDuringPlay(Transform t)
        {
            while (t != null)
            {
                if (t.GetComponent<CarryableItem>() != null) return true;
                if (t.GetComponent<Door>() != null) return true;
                if (t.GetComponent<RewardPlinth>() != null) return true;
                if (t.GetComponent<Balloon>() != null) return true;
                if (t.GetComponent<Drawer>() != null) return true;
                if (t.GetComponent<GhostReplayer>() != null) return true;
                // The cover over a cycle's way out. Authored closed and slid aside at the boundary,
                // so baked in it would leave a shut floor in every reflection of a room the player is
                // standing in with it open.
                if (t.GetComponent<CycleExit>() != null) return true;
                // The gas. A particle system baked into a probe would put vapour in the reflection
                // of a room that has not been gassed yet.
                if (t.GetComponent<ParticleSystem>() != null) return true;
                if (t.CompareTag("Player")) return true;
                t = t.parent;
            }
            return false;
        }

        private static Material MakeEmissiveMaterial(string name, Color color, float emission)
        {
            string path = $"{MaterialsDir}/{name}.mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(OpaqueShader());
                AssetDatabase.CreateAsset(mat, path);
            }

            mat.shader = OpaqueShader();
            mat.color = color;
            SetSmoothness(mat, 0.1f);

            // The keyword matters as much as the colour: set _EmissionColor alone and URP leaves
            // the emission pass compiled out, so the panel renders as plain white.
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", color * emission);
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;

            EditorUtility.SetDirty(mat);
            return mat;
        }

        // A POLISHED, COLOURED METAL with a glow still in it. The three escape objects wear this and
        // nothing else does.
        //
        // They were flat emissive at 2.4 with smoothness 0.1, which is a matte object with the light
        // turned up inside it: no highlight anywhere on the surface, the middle blown out to near
        // white, and the colour only readable at the silhouette. Three of those on a console read as
        // three coloured blocks rather than as the things the whole run was spent collecting.
        //
        // EVERY NUMBER HERE WAS RENDERED AND LOOKED AT, not reasoned to, and the first three
        // attempts were worse than what they replaced. What each one is and what it cost to find:
        //
        // - METALLIC 0.9. The project rule says a pure metal renders BLACK here, and that rule is a
        //   symptom: a metal is entirely reflection, and every reflection probe in this scene had a
        //   NULL baked texture, so a metal had nothing to be. Baking the probes (see
        //   BakeReflectionProbes) is what makes this value available at all. At 0.8 with an EMPTY
        //   probe the object came out darker and deader than the flat colour it replaced - worth
        //   knowing, because that is what this looks like if the bake ever silently breaks again.
        // - SMOOTHNESS 0.97, near-mirror, and this is the one that does the work. The obvious
        //   setting is the 0.85-0.88 the walls use, and it is wrong for a small object: at that
        //   roughness a featureless white room blurs into a flat wash and the thing reads as
        //   plastic. Near-mirror instead resolves the CEILING FIXTURES as two distinct bright
        //   shapes on the top face, which is the only structure this white box has to offer - and
        //   structure is what the eye reads as metal.
        // - REFLECTANCE IS THE ACCENT LIFTED 30% TOWARD WHITE, not the accent itself. Base colour
        //   means something different on a metal: it is what the surface reflects, not what it is
        //   painted. A real coloured metal's reflectance is high (gold is ~1.0/0.77/0.34), so
        //   feeding in a display colour like 0.16/0.40/0.95 gives a dark, muddy mirror. The lift
        //   keeps the hue - these three are told apart by colour, in a hurry, across a console.
        // - EMISSION 0.40, down from 2.4. The ORIGINAL reason for emission stands: each of these
        //   arrives in the same second as the biggest lighting change its room has. But 2.4 blew the
        //   middle out to near-white and left the colour readable only at the silhouette, which is
        //   what made these look like coloured blocks. At 0.40 the metal is what is seen and the
        //   glow is a floor under it - this object cannot go black however the probes behave.
        //
        // Emission takes the ACCENT, not the lifted reflectance: the glow should be the object's own
        // colour at full strength, where the mirror should not.
        private static Material MakePolishedMetalMaterial(string name, Color accent, float emission)
        {
            string path = $"{MaterialsDir}/{name}.mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(OpaqueShader());
                AssetDatabase.CreateAsset(mat, path);
            }

            Color reflectance = Color.Lerp(accent, Color.white, 0.30f);

            mat.shader = OpaqueShader();
            mat.color = reflectance;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", reflectance);
            SetSmoothness(mat, 0.97f);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0.9f);

            // The keyword matters as much as the colour: set _EmissionColor alone and URP leaves the
            // emission pass compiled out - the same trap MakeEmissiveMaterial documents.
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", accent * emission);
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;

            EditorUtility.SetDirty(mat);
            return mat;
        }

        // A lit material you can see through. The URP transparent set-up needs all of _Surface,
        // _SrcBlend, _DstBlend and _ZWrite AND the keyword -
        // the blend modes and the _SURFACE_TYPE_TRANSPARENT keyword are BOTH required, and setting
        // the alpha alone leaves the shader opaque - but on Lit rather than Unlit, because unlike a
        // ghost these are real objects in a lit room and have to take the ceiling lights.
        private static Material MakeTranslucentMaterial(string name, Color color, float smoothness)
        {
            string path = $"{MaterialsDir}/{name}.mat";
            Shader lit = OpaqueShader();

            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(lit);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = lit;
            mat.color = color;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            SetSmoothness(mat, smoothness);

            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_ZWrite", 0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.DisableKeyword("_ALPHATEST_ON");

            // PREMULTIPLIED alpha, and all three of the keyword, the _Blend enum and the blend
            // factors have to say so together. They did not: the keyword had been on the asset
            // since before the first ship while this forced SrcAlpha, so a scene build left alpha
            // multiplied TWICE and URP's material validator undid it again on the next build-target
            // switch. The Editor showed duller balloons than the player did, and four .mat files
            // turned up modified after every WebGL build.
            //
            // Settled by capture, three ways: premultiplied + One reads as pink translucent
            // balloons; premultiplied + SrcAlpha washes them grey; keyword off makes the bodies
            // very nearly VANISH, because without it the diffuse no longer carries them and only
            // the opaque knots are left. So the keyword stays and everything else follows it.
            mat.EnableKeyword("_ALPHAPREMULTIPLY_ON");
            // _Blend stays 0 (Alpha). Writing 1 (Premultiply) makes URP re-derive the keywords on
            // save and it DISABLES _ALPHAPREMULTIPLY_ON, leaving One blending over non-premultiplied
            // colour - the balloons come out washed toward white. 0 with the keyword forced on is
            // the pairing that has always been on the asset and the one that renders correctly.
            mat.SetFloat("_Blend", 0f);
            mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
            mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

            EditorUtility.SetDirty(mat);
            return mat;
        }

        // Clear glass - the cube room's cubes, 2026-08-13. Same transparent set-up
        // MakeTranslucentMaterial uses (both keywords, both blend factors, the premultiplied pairing
        // that actually renders instead of vanishing).
        //
        // `map` IS OPTIONAL AND IS NOW ALWAYS NULL. It used to carry the glyph as an alpha channel,
        // etching the symbol into the six faces; the symbol is a solid core suspended INSIDE the
        // glass now (BuildSymbolCube), so the body is one shared clear material for all six cubes
        // rather than six that differ only in what is printed on them.
        //
        // High smoothness reads as glass here for the same reason it reads as metal on the escape
        // objects: this project has no real refraction, so the ONLY thing selling a hard, clear
        // surface is a sharp reflection with real geometry (the ceiling fixtures) to catch in it -
        // see MakePolishedMetalMaterial and the reflection-probe notes in docs/rendering-notes.md.
        //
        // `tint` is deliberately the SAME colour for all six cubes - see CubeSymbols' own note on
        // "symbols, not colours" for why a per-cube tint would undo the room's one accessibility
        // rule - and its ALPHA is how much glass there is to see through. It came down when the
        // symbol moved inside: the body now has something behind it that has to be read.
        private static Material MakeGlassMaterial(string name, Texture2D map, Color tint, float smoothness)
        {
            string path = $"{MaterialsDir}/{name}.mat";
            Shader lit = OpaqueShader();

            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(lit);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = lit;
            mat.color = tint;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", tint);
            mat.SetTexture("_BaseMap", map);
            mat.mainTexture = map;
            SetSmoothness(mat, smoothness);
            // A flat 0 metallic read as thin, flat plastic wrap rather than a hard cut surface -
            // play called it cheap. Real glass is not a metal, but this project has no refraction
            // (see the note above), so a weak dielectric specular is the wrong tool here for the
            // same reason it is the right one everywhere else: without it, the ceiling fixtures this
            // cube ought to be throwing back barely show up at all. Short of the escape objects' own
            // 0.9 - this still has to read as SEE-THROUGH, and a fully metallic surface would not.
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0.35f);

            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_ZWrite", 0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.SetFloat("_Blend", 0f);
            mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
            mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static Material MakeColorMaterial(string name, Color color)
        {
            string path = $"{MaterialsDir}/{name}.mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(OpaqueShader());
                AssetDatabase.CreateAsset(mat, path);
            }

            mat.shader = OpaqueShader();
            mat.color = color;
            // Matte: the default 0.5 smoothness mirrors the skybox onto the room surfaces.
            SetSmoothness(mat, 0.03f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static GameObject Prim(PrimitiveType type, string name, Transform parent, Vector3 localPos, Vector3 localScale, Material mat = null, bool trigger = false, bool removeCollider = false)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = localScale;

            if (mat != null) go.GetComponent<Renderer>().sharedMaterial = mat;

            Collider col = go.GetComponent<Collider>();
            if (removeCollider && col != null) Object.DestroyImmediate(col);
            else if (col != null) col.isTrigger = trigger;

            return go;
        }

        // Instantiates an imported model (Kenney OBJ furniture) and repositions it so its
        // rendered bounds are centered on targetXZCenter with its base sitting at floorY -
        // needed because these models' pivots are at odd mesh corners, not their footprint center.
        private static (GameObject instance, Bounds worldBounds) PlaceModel(string objAssetPath, Transform parent, string name, Vector3 targetXZCenter, float floorY, float uniformScale, bool addBoxCollider, Quaternion rotation = default)
        {
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(objAssetPath);
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(source, parent);
            instance.name = name;
            instance.transform.localScale = Vector3.one * uniformScale;
            // Bounds are measured AFTER rotating, so a model authored Z-up or facing the wrong way
            // still lands centred and sitting on the floor.
            instance.transform.localRotation = rotation == default ? Quaternion.identity : rotation;
            instance.transform.localPosition = Vector3.zero;

            Renderer[] renderers = instance.GetComponentsInChildren<Renderer>();
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);

            Vector3 delta = new Vector3(targetXZCenter.x - bounds.center.x, floorY - bounds.min.y, targetXZCenter.z - bounds.center.z);
            instance.transform.localPosition += delta;
            Vector3 finalCenter = bounds.center + delta;
            Bounds finalBounds = new Bounds(finalCenter, bounds.size);

            if (addBoxCollider)
            {
                BoxCollider box = instance.AddComponent<BoxCollider>();
                box.center = instance.transform.InverseTransformPoint(finalCenter);
                // The bounds are world-axis-aligned, so bring the size back into the instance's
                // own axes before assigning - otherwise a rotated model gets a collider with its
                // height and depth swapped.
                Vector3 localSize = Quaternion.Inverse(instance.transform.localRotation) * finalBounds.size;
                box.size = new Vector3(Mathf.Abs(localSize.x), Mathf.Abs(localSize.y), Mathf.Abs(localSize.z)) / uniformScale;
            }

            return (instance, finalBounds);
        }

        // CYCLE 2'S BUILDING, one storey below cycle 1's and running back the other way.
        //
        // THE Y OFFSET IS ON THE ROOT, and that is what made a second storey cheap. Every builder in
        // this file places its output with `localPosition` under the parent it is handed, so a parent
        // moved down carries all of them with it - `BuildRoomShell`, `BuildCeilingLights` and
        // `BuildReflectionProbe` needed no Y parameter and no change at all. The alternative was
        // threading a storey height through five signatures and every call site.
        //
        // A SIBLING OF "Room", not a child, and that is load-bearing rather than tidiness: the wall
        // panel gather in `Build` walks everything under `Room` and takes anything parented to a
        // "*_Panels" node. Built inside it, cycle 2's panels would silently join cycle 1's `ERROR`
        // glitch - a cycle failing in a room the player has not reached yet. Outside it, the two sets
        // are separate for free and each cycle gets its own display.
        //
        // Cycle 2 has ONE room and no puzzles yet. Its rooms are sealed on both walls for now; the
        // switchback means the next one goes at 4 * RoomPitch and this room grows a SOUTH doorway when
        // it does.
        // One cycle's wall panels, as the thing that boots them, flares them and finally fails them.
        private static WallPanelDisplay MakeWallPanelDisplay(string name, Renderer[] panels,
                                                             Texture2D testCard, Texture2D staticNoise)
        {
            GameObject go = new GameObject(name);
            WallPanelDisplay display = go.AddComponent<WallPanelDisplay>();
            display.panels = panels;
            display.offColor = WallPanelColor;
            display.onColor = Color.white;
            display.testCard = testCard;
            display.staticNoise = staticNoise;
            return display;
        }

        // ONE ROOM OF THE RING: a parent carrying its world position and facing, with the room built
        // at local zero inside it.
        //
        // **A parent per room rather than a parent per leg**, which is what makes the whole ring
        // reuse builders that only ever take a `zCenter`. `BuildRoomShell`, `BuildCeilingLights` and
        // `BuildReflectionProbe` know nothing about X or rotation; handed a rotated, positioned parent
        // and a zCenter of zero, every one of them lands correctly with no new parameters. The
        // alternative was threading an X and a yaw through five signatures and every call site.
        //
        // Travel is always local -Z, so every room's NORTH wall is where you came in and its SOUTH
        // wall is where you leave - except at a corner, where you leave through the WEST wall.
        // **West is always the way on**, either to the next room or, mid-leg, onto the core.
        private static Transform BuildRingRoom(Transform parent, string name, float x, float z, float yaw,
                                               Material floorMat, Material grooveMat, Material panelMat,
                                               Rect south, Rect north, Rect west,
                                               Rect ceilingHole = default)
        {
            GameObject holder = new GameObject(name + "_Root");
            holder.transform.SetParent(parent, false);
            holder.transform.localPosition = new Vector3(x, 0f, z);
            holder.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);

            BuildRoomShell(holder.transform, name, 0f, floorMat, grooveMat, panelMat,
                south, north, west, Rect.zero, Rect.zero, ceilingHole);

            return holder.transform;
        }

        // The pane in a core window. Not a door and not a wall - a sheet of glass filling a hole that
        // is never opened, so it is the one piece of this building the player can see through and
        // never pass.
        private static void BuildCoreWindow(Transform roomRoot, Material glassMat)
        {
            float w = CoreWindow.width, h = CoreWindow.height;
            GameObject pane = Prim(PrimitiveType.Cube, "CoreWindow", roomRoot,
                new Vector3(-RoomWidth / 2f, CoreWindow.yMin + h / 2f, CoreWindow.center.x),
                new Vector3(0.05f, h, w), glassMat);
            // Kept solid on purpose. The window is a hole in the wall's collision as well as its
            // panelling - `SubtractRect` cuts both - so without this the player walks into the core.
            pane.name = "CoreWindow";
        }

        // THE MACHINE THE RING IS BUILT AROUND.
        //
        // A sealed casing the player never enters, seen only through four windows. Inset from the
        // rooms' inner faces rather than flush with them, which does two things: it avoids two
        // coplanar walls fighting over the same plane, and it leaves a shallow chamber so the core
        // reads as a thing standing in a shaft rather than as a solid block of building.
        //
        // Deliberately plain at this stage. What the core DOES - clamps releasing, an axle turning,
        // shards pushed out through hatches - belongs with the puzzles that drive it, and a casing
        // that already moved would be machinery with nothing operating it.
        private static Transform BuildCore(Transform parent, Vector3 centre, float span, Material propMat)
        {
            GameObject core = new GameObject("Core");
            core.transform.SetParent(parent, false);
            core.transform.localPosition = centre;

            Material casing = MakeColorMaterial("CoreCasing", new Color(0.17f, 0.18f, 0.21f));
            SetSmoothness(casing, 0.55f);

            // Solid, and the only thing between the player and the core's insides.
            Prim(PrimitiveType.Cube, "Casing", core.transform,
                new Vector3(0f, RoomHeight / 2f, 0f),
                new Vector3(span, RoomHeight, span), casing);

            // Something to look at through the glass: a lit column standing in the casing's mouth on
            // each of the four window faces. Emissive rather than lit, so it reads through glass in a
            // room whose own lights are behind the viewer.
            Material glow = MakeEmissiveMaterial("CoreGlow", new Color(0.45f, 0.75f, 1f), 2.6f);
            float face = span / 2f;
            var faces = new[]
            {
                new Vector3( face, 0f, 0f), new Vector3(-face, 0f, 0f),
                new Vector3(0f, 0f,  face), new Vector3(0f, 0f, -face),
            };
            for (int i = 0; i < faces.Length; i++)
            {
                Vector3 inward = -faces[i].normalized * 0.28f;
                Prim(PrimitiveType.Cylinder, $"CoreRotor_{i}", core.transform,
                    faces[i] + inward + Vector3.up * (GridCellHeight * 2f),
                    new Vector3(0.55f, GridCellHeight * 0.9f, 0.55f), glow, removeCollider: true);
            }

            return core.transform;
        }

        // Returns `rooms` in ring order - room1 first, room0 last - because room0's console needs
        // things that do not exist yet when the shell is built: this cycle's wall panels are gathered
        // FROM the shell, and the shaker and the hand belong to the player, who is built later.
        // Handing the transforms back and finishing room0 at the call site is what keeps that
        // ordering honest rather than shuffling half of `Build` around it.
        private static (Transform root, Transform bedSpawn, ParticleSystem[] gas,
                        Door[] doors, RoomCondition[] conditions, GhostInteractable[] signals,
                        Transform[] rooms) BuildCycleTwoShell(
            Material floorMat, Material grooveMat, Material panelMat, Material propMat)
        {
            GameObject root = new GameObject("Room_Cycle2");
            root.transform.position = new Vector3(0f, -StoreyDrop, 0f);

            Rect doorway = new Rect(-DoorWidth / 2f, 0f, DoorWidth, DoorHeight);

            // THE RING. Eight rooms around the perimeter of a 3x3 block with the core in the middle,
            // turning WEST three times - at room3, room5 and room7, which are its corners.
            //
            // Room1 keeps the position it already had, so the drop from `room1-0` lands exactly where
            // it did. Everything else is walked out from there: `RoomPitch` along a leg, `CornerPitch`
            // across a turn, and a quarter turn of yaw at each corner.
            //
            // **Only four rooms share a wall with the core** - 2, 4, 6 and 0, the mid-leg ones. The
            // corners touch it diagonally and cannot see in at all. That is not a limitation to work
            // around; it is the rhythm the layout hands over for free: one glass room and one corner
            // room per leg.
            float ringZ = CycleTwoFirstRoomZ;

            Transform r1 = BuildRingRoom(root.transform, "Room2_1", 0f, ringZ, 0f,
                floorMat, grooveMat, panelMat, doorway, Rect.zero, Rect.zero, CycleExitHole);
            Transform r2 = BuildRingRoom(root.transform, "Room2_2", 0f, ringZ - RoomPitch, 0f,
                floorMat, grooveMat, panelMat, doorway, doorway, CoreWindow);
            Transform r3 = BuildRingRoom(root.transform, "Room2_3", 0f, ringZ - 2f * RoomPitch, 0f,
                floorMat, grooveMat, panelMat, Rect.zero, doorway, doorway);

            float legTwoZ = ringZ - 2f * RoomPitch;
            Transform r4 = BuildRingRoom(root.transform, "Room2_4", -CornerPitch, legTwoZ, 90f,
                floorMat, grooveMat, panelMat, doorway, doorway, CoreWindow);
            Transform r5 = BuildRingRoom(root.transform, "Room2_5", -CornerPitch - RoomPitch, legTwoZ, 90f,
                floorMat, grooveMat, panelMat, Rect.zero, doorway, doorway);

            float legThreeX = -CornerPitch - RoomPitch;
            Transform r6 = BuildRingRoom(root.transform, "Room2_6", legThreeX, legTwoZ + CornerPitch, 180f,
                floorMat, grooveMat, panelMat, doorway, doorway, CoreWindow);
            Transform r7 = BuildRingRoom(root.transform, "Room2_7", legThreeX, legTwoZ + CornerPitch + RoomPitch, 180f,
                floorMat, grooveMat, panelMat, Rect.zero, doorway, doorway);

            Transform r0 = BuildRingRoom(root.transform, "Room2_0", legThreeX + CornerPitch,
                legTwoZ + CornerPitch + RoomPitch, 270f,
                floorMat, grooveMat, panelMat, Rect.zero, doorway, CoreWindow);

            var rooms = new[] { r1, r2, r3, r4, r5, r6, r7, r0 };
            var names = new[] { "Room2_1", "Room2_2", "Room2_3", "Room2_4",
                                "Room2_5", "Room2_6", "Room2_7", "Room2_0" };

            // The four windows, and the machine behind them. The casing is inset so it is not
            // coplanar with the walls it is seen through - see BuildCore.
            Material glassMat = MakeTranslucentMaterial("CoreGlass", new Color(0.72f, 0.82f, 0.88f, 0.16f), 0.94f);
            BuildCoreWindow(r2, glassMat);
            BuildCoreWindow(r4, glassMat);
            BuildCoreWindow(r6, glassMat);
            BuildCoreWindow(r0, glassMat);

            const float coreInset = 0.15f;
            float coreSpan = RoomPitch + CornerPitch - RoomWidth - 2f * coreInset;
            Vector3 coreCentre = new Vector3(
                (0f - RoomWidth / 2f + legThreeX + RoomWidth / 2f) / 2f, 0f,
                (legTwoZ + RoomWidth / 2f + legTwoZ + CornerPitch + RoomPitch - RoomWidth / 2f) / 2f);
            BuildCore(root.transform, coreCentre, coreSpan, propMat);

            // THE DOORS, each built under the room it leaves FROM and set in that room's exit wall.
            // Straight joins use the south wall (yaw 180 from the builder's default north); the three
            // corners use the west wall (yaw 270) and the room's narrower half-extent.
            var doors = new Door[7];
            doors[0] = BuildPadDoor(r1, "Door2_1", 0f, new FloorButton[0], propMat, yaw: 180f);
            doors[1] = BuildPadDoor(r2, "Door2_2", 0f, new FloorButton[0], propMat, yaw: 180f);
            doors[2] = BuildPadDoor(r3, "Door2_3", 0f, new FloorButton[0], propMat, yaw: 270f,
                                    wallHalfExtent: RoomWidth / 2f);
            doors[3] = BuildPadDoor(r4, "Door2_4", 0f, new FloorButton[0], propMat, yaw: 180f);
            doors[4] = BuildPadDoor(r5, "Door2_5", 0f, new FloorButton[0], propMat, yaw: 270f,
                                    wallHalfExtent: RoomWidth / 2f);
            doors[5] = BuildPadDoor(r6, "Door2_6", 0f, new FloorButton[0], propMat, yaw: 180f);
            doors[6] = BuildPadDoor(r7, "Door2_7", 0f, new FloorButton[0], propMat, yaw: 270f,
                                    wallHalfExtent: RoomWidth / 2f);

            // One pocket per join, all uncapped: every one has a room through it.
            BuildDoorPocketFill(r1, "Pocket2_1", 0f, grooveMat, capFarSide: false, yaw: 180f);
            BuildDoorPocketFill(r2, "Pocket2_2", 0f, grooveMat, capFarSide: false, yaw: 180f);
            BuildDoorPocketFill(r3, "Pocket2_3", 0f, grooveMat, capFarSide: false,
                                wallHalfExtent: RoomWidth / 2f, crossHalfWidth: RoomDepth / 2f, yaw: 270f);
            BuildDoorPocketFill(r4, "Pocket2_4", 0f, grooveMat, capFarSide: false, yaw: 180f);
            BuildDoorPocketFill(r5, "Pocket2_5", 0f, grooveMat, capFarSide: false,
                                wallHalfExtent: RoomWidth / 2f, crossHalfWidth: RoomDepth / 2f, yaw: 270f);
            BuildDoorPocketFill(r6, "Pocket2_6", 0f, grooveMat, capFarSide: false, yaw: 180f);
            BuildDoorPocketFill(r7, "Pocket2_7", 0f, grooveMat, capFarSide: false,
                                wallHalfExtent: RoomWidth / 2f, crossHalfWidth: RoomDepth / 2f, yaw: 270f);

            Material fixtureMat = MakeEmissiveMaterial("CeilingFixtureCycle2", Color.white, 3.5f);
            for (int i = 0; i < rooms.Length; i++)
            {
                BuildCeilingLights(rooms[i], names[i], 0f, fixtureMat, castShadows: false);
                BuildReflectionProbe(rooms[i], names[i], 0f);
            }

            // The bed, on room1's own root. `BuildBed` goes through `PlaceModel`, which corrects a
            // WORLD-space delta - so it still has to be told the storey and the room's world Z, and it
            // is only safe here because leg 1 carries no rotation.
            (_, Transform spawn) = BuildBed(r1, propMat,
                                            floorY: -StoreyDrop, zCentre: ringZ, yaw: 180f);

            // The nightstand, turned with the bed. A rotated wrapper is safe for this one in a way it
            // is not for the bed: everything under it is placed with `localPosition`, so the parent's
            // rotation is simply inherited.
            GameObject furniture = new GameObject("Room2_1_Furniture");
            furniture.transform.SetParent(r1, false);
            furniture.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            BuildNightstand(furniture.transform, CycleTwoToolItemId);

            (NumberLock numberLock, CountPad[] pads) = BuildNumberLock(r1, 0f, propMat);
            doors[0].condition = numberLock;

            // ROOM2: the chorus. Its door is the one out of room2, into room3.
            (Chorus chorus, ChorusLever[] chorusLevers) = BuildChorus(rooms[1], propMat);
            doors[1].condition = chorus;
            doors[1].openUntilPuzzled = false;

            // ROOM3: the tree. Its door is the corner one out of room3, into room4.
            (Tree tree, ChopStation[] chopStations, CarryableItem[] axes) =
                BuildTreeRoom(rooms[2], propMat);
            doors[2].condition = tree;
            doors[2].openUntilPuzzled = false;

            // NO PUZZLES BEHIND THESE YET, so they stand open and the ring can be walked. Each flag
            // comes off as its room is built - see Door.openUntilPuzzled.
            //
            // Room1's is included even though its number lock works, because the ring cannot be
            // walked without it. Turning the lock back on is deleting one line.
            // Set on every door first and cleared again by each room as its puzzle lands, so the
            // flag is impossible to forget: a room that gets built takes its own door off the list.
            foreach (Door d in doors) d.openUntilPuzzled = true;

            ParticleSystem[] gas = BuildGasEmitters(r1, "Room2_1_Gas", 0f);

            // EVERY SIGNAL IN THIS CYCLE, IN ONE ARRAY, and its order is the wire format - an
            // entry's index is its bit in RecordedFrame.signals. Append as rooms are built, never
            // reorder: a bit that changes meaning is a past self holding the wrong thing.
            var signals = new System.Collections.Generic.List<GhostInteractable>();
            signals.AddRange(pads);
            signals.AddRange(chorusLevers);
            signals.AddRange(chopStations);

            return (root.transform, spawn, gas, doors,
                    new RoomCondition[] { numberLock, chorus, tree },
                    signals.ToArray(), rooms);
        }

        // WHAT A RETRACTED PLINTH RETRACTS INTO.
        //
        // Every `RewardPlinth` is authored raised and sunk by `riseHeight` in Awake, so when it is
        // down it sits about a metre BELOW the floor. That was invisible for as long as there was
        // nothing under the floor to see it from. With a storey below, room1-0's console hangs a
        // metre out of room2-1's ceiling - the reported "cycle 1's structures are visible in cycle
        // 2's ceiling".
        //
        // Five sides, open at the top, so the plinth still rises straight out of it. From above
        // nothing changes: the floor slab is opaque and the housing is under it. From below it reads
        // as building services, which is what a retracting plinth would actually need.
        //
        // Only built where a room exists underneath. The other three plinths - Room3's, the cube
        // room's, and the chess reward - sink into nothing today, and will want this when cycle 2
        // grows rooms beneath them.
        private static void BuildPlinthHousing(Transform parent, string name, Vector3 centreXZ,
                                               float footprintX, float footprintZ, float depth, Material mat)
        {
            GameObject well = new GameObject(name);
            well.transform.SetParent(parent, false);
            // Hung from the underside of the floor slab, which is where its open top belongs.
            well.transform.localPosition = new Vector3(centreXZ.x, -WallThickness, centreXZ.z);

            float halfX = footprintX / 2f, halfZ = footprintZ / 2f;

            Prim(PrimitiveType.Cube, "Bottom", well.transform,
                new Vector3(0f, -depth - WallThickness / 2f, 0f),
                new Vector3(footprintX + 2f * WallThickness, WallThickness, footprintZ + 2f * WallThickness), mat);

            Prim(PrimitiveType.Cube, "Side_West", well.transform,
                new Vector3(-halfX - WallThickness / 2f, -depth / 2f, 0f),
                new Vector3(WallThickness, depth, footprintZ + 2f * WallThickness), mat);
            Prim(PrimitiveType.Cube, "Side_East", well.transform,
                new Vector3(halfX + WallThickness / 2f, -depth / 2f, 0f),
                new Vector3(WallThickness, depth, footprintZ + 2f * WallThickness), mat);
            Prim(PrimitiveType.Cube, "Side_South", well.transform,
                new Vector3(0f, -depth / 2f, -halfZ - WallThickness / 2f),
                new Vector3(footprintX, depth, WallThickness), mat);
            Prim(PrimitiveType.Cube, "Side_North", well.transform,
                new Vector3(0f, -depth / 2f, halfZ + WallThickness / 2f),
                new Vector3(footprintX, depth, WallThickness), mat);
        }

        // DIGITS, DRAWN AS STROKES RATHER THAN AS A BITMAP.
        //
        // There is already a 5x7 bitmap in this file, for the word ERROR on a failing panel - and it
        // is right there, because a test card is a machine drawing itself. It is wrong here twice
        // over: the wall digits are meant to look like somebody scrawled them, and a blocky glyph
        // blown up to fill a 1.75m panel is a QR code.
        //
        // So each digit is a set of polylines in a 0..1 box, stamped with a round brush. That gives
        // two things a bitmap cannot: a `wobble` that displaces every point by a seeded amount, which
        // is what makes the wall read as handwriting; and a thickness that can be tuned per use, so
        // the same paths serve a metre of graffiti and a 20cm readout on a pad.
        private static Vector2[][] DigitStrokes(int digit)
        {
            switch (digit)
            {
                case 0: return new[] { Ring(0.5f, 0.5f, 0.26f, 0.42f, 14) };
                case 1: return new[]
                {
                    new[] { new Vector2(0.34f, 0.74f), new Vector2(0.52f, 0.94f) },
                    new[] { new Vector2(0.52f, 0.94f), new Vector2(0.50f, 0.07f) },
                };
                case 2: return new[]
                {
                    new[]
                    {
                        new Vector2(0.22f, 0.76f), new Vector2(0.33f, 0.93f), new Vector2(0.58f, 0.94f),
                        new Vector2(0.74f, 0.80f), new Vector2(0.66f, 0.60f), new Vector2(0.24f, 0.10f),
                        new Vector2(0.80f, 0.09f),
                    },
                };
                case 3: return new[]
                {
                    new[]
                    {
                        new Vector2(0.23f, 0.86f), new Vector2(0.44f, 0.95f), new Vector2(0.70f, 0.86f),
                        new Vector2(0.66f, 0.66f), new Vector2(0.44f, 0.56f),
                    },
                    new[]
                    {
                        new Vector2(0.44f, 0.56f), new Vector2(0.72f, 0.46f), new Vector2(0.74f, 0.22f),
                        new Vector2(0.52f, 0.07f), new Vector2(0.24f, 0.15f),
                    },
                };
                case 4: return new[]
                {
                    new[] { new Vector2(0.66f, 0.94f), new Vector2(0.19f, 0.33f) },
                    new[] { new Vector2(0.19f, 0.33f), new Vector2(0.84f, 0.33f) },
                    new[] { new Vector2(0.66f, 0.94f), new Vector2(0.66f, 0.07f) },
                };
                case 5: return new[]
                {
                    new[] { new Vector2(0.74f, 0.93f), new Vector2(0.29f, 0.92f) },
                    new[] { new Vector2(0.29f, 0.92f), new Vector2(0.26f, 0.55f) },
                    new[]
                    {
                        new Vector2(0.26f, 0.55f), new Vector2(0.56f, 0.60f), new Vector2(0.76f, 0.42f),
                        new Vector2(0.66f, 0.14f), new Vector2(0.30f, 0.10f),
                    },
                };
                case 6: return new[]
                {
                    new[]
                    {
                        new Vector2(0.70f, 0.90f), new Vector2(0.40f, 0.74f), new Vector2(0.25f, 0.40f),
                        new Vector2(0.34f, 0.12f), new Vector2(0.62f, 0.08f), new Vector2(0.76f, 0.30f),
                        new Vector2(0.62f, 0.50f), new Vector2(0.30f, 0.44f),
                    },
                };
                case 7: return new[]
                {
                    new[] { new Vector2(0.20f, 0.92f), new Vector2(0.80f, 0.92f) },
                    new[] { new Vector2(0.80f, 0.92f), new Vector2(0.40f, 0.07f) },
                };
                case 8: return new[]
                {
                    Ring(0.5f, 0.71f, 0.21f, 0.21f, 11),
                    Ring(0.5f, 0.28f, 0.25f, 0.24f, 11),
                };
                case 9: return new[]
                {
                    new[]
                    {
                        new Vector2(0.72f, 0.52f), new Vector2(0.42f, 0.60f), new Vector2(0.26f, 0.78f),
                        new Vector2(0.40f, 0.94f), new Vector2(0.68f, 0.90f), new Vector2(0.74f, 0.62f),
                        new Vector2(0.58f, 0.20f), new Vector2(0.30f, 0.08f),
                    },
                };
                default: return new Vector2[0][];
            }
        }

        // A closed loop, for the digits that are mostly one.
        private static Vector2[] Ring(float cx, float cy, float rx, float ry, int steps)
        {
            var pts = new Vector2[steps + 1];
            for (int i = 0; i <= steps; i++)
            {
                float a = i / (float)steps * Mathf.PI * 2f;
                pts[i] = new Vector2(cx + Mathf.Sin(a) * rx, cy + Mathf.Cos(a) * ry);
            }
            return pts;
        }

        // wobble: how far each point wanders, as a fraction of the box. Zero draws the path as
        // authored, which is what a machine readout wants; 0.03 is a hand that is not being careful.
        private static Texture2D MakeDigitTexture(string name, int digit, int size,
                                                  float thickness, float wobble, Color ink, int seed)
        {
            Color[] px = new Color[size * size];
            // Transparent, not white: these are decals over a wall panel and a pad face, so the
            // background has to not exist rather than be a colour that happens to match today.
            for (int i = 0; i < px.Length; i++) px[i] = new Color(ink.r, ink.g, ink.b, 0f);

            var rng = new System.Random(seed);
            float radius = thickness * size * 0.5f;

            foreach (Vector2[] stroke in DigitStrokes(digit))
            {
                if (stroke.Length < 2) continue;

                // Displaced once per POINT rather than per sample, so the line wanders instead of
                // going furry - a per-sample jitter reads as noise, not as a hand.
                var pts = new Vector2[stroke.Length];
                for (int i = 0; i < stroke.Length; i++)
                {
                    float jx = ((float)rng.NextDouble() * 2f - 1f) * wobble;
                    float jy = ((float)rng.NextDouble() * 2f - 1f) * wobble;
                    pts[i] = new Vector2(stroke[i].x + jx, stroke[i].y + jy);
                }

                for (int i = 0; i < pts.Length - 1; i++)
                {
                    Vector2 a = pts[i] * size;
                    Vector2 b = pts[i + 1] * size;
                    // Dense enough that consecutive dabs overlap even at the thinnest setting.
                    int steps = Mathf.Max(2, Mathf.CeilToInt(Vector2.Distance(a, b) / Mathf.Max(1f, radius * 0.4f)));
                    for (int stepIndex = 0; stepIndex <= steps; stepIndex++)
                    {
                        Vector2 at = Vector2.Lerp(a, b, stepIndex / (float)steps);
                        // Thinning toward the end of each stroke, the way a stroke lifts off.
                        float taper = 1f - 0.25f * (i + stepIndex / (float)steps) / Mathf.Max(1, pts.Length - 1);
                        Dab(px, size, at, radius * taper, ink);
                    }
                }
            }

            return WriteTexture(name, size, size, px, TextureWrapMode.Clamp, FilterMode.Bilinear);
        }

        // One round mark. Soft at the rim by one pixel, which is all the antialiasing a stroke this
        // thick needs and enough that the edge does not stair-step across a 1.75m panel.
        private static void Dab(Color[] px, int size, Vector2 at, float radius, Color ink)
        {
            int minX = Mathf.Max(0, Mathf.FloorToInt(at.x - radius - 1f));
            int maxX = Mathf.Min(size - 1, Mathf.CeilToInt(at.x + radius + 1f));
            int minY = Mathf.Max(0, Mathf.FloorToInt(at.y - radius - 1f));
            int maxY = Mathf.Min(size - 1, Mathf.CeilToInt(at.y + radius + 1f));

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), at);
                    float a = Mathf.Clamp01(radius - d);
                    if (a <= 0f) continue;

                    int i = y * size + x;
                    // Kept, never averaged: overlapping dabs must not lighten each other, or every
                    // crossing in the stroke would show as a bright knot.
                    px[i].a = Mathf.Max(px[i].a, a);
                }
            }
        }

        // A SOFT ROUND BLOB, which is the whole of what a vapour particle needs to be. Generated
        // like every other texture here rather than sourced.
        //
        // The falloff is squared rather than linear: a linear ramp gives a disc with a visible rim,
        // and what has to be invisible is the EDGE of the sprite - a hundred overlapping billboards
        // only read as one cloud if none of them has a boundary you can find.
        private static Texture2D MakeSoftBlobTexture(string name, int size)
        {
            Color[] px = new Color[size * size];
            float half = size / 2f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f - half) / half;
                    float dy = (y + 0.5f - half) / half;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(1f - d);
                    a *= a;
                    px[y * size + x] = new Color(1f, 1f, 1f, a);
                }
            }

            return WriteTexture(name, size, size, px, TextureWrapMode.Clamp, FilterMode.Bilinear);
        }

        // WHERE THE GAS COMES FROM: the top of every wall, all four sides, nowhere to stand that is
        // not under one.
        //
        // PARTICLES, after two simpler attempts failed for the same reason. A lit slot along each
        // wall left four near-black strips in a white room permanently, in exchange for a moment; a
        // translucent box that grew downward left a zero-height quad at the wall head that still
        // caught a specular highlight. Both were trying to fake a volume with a surface, and a
        // surface always has an edge you can find.
        //
        // What makes this read as vapour rather than as sprites is the NOISE module - without it a
        // hundred soft blobs drifting on straight lines look exactly like a hundred soft blobs. The
        // damping is the other half: the gas decelerates as it comes off the wall, so it pours in
        // and then hangs, which is what a heavier-than-air gas does in a sealed room.
        private static ParticleSystem[] BuildGasEmitters(Transform parent, string name, float roomCenterZ)
        {
            GameObject root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = new Vector3(0f, 0f, roomCenterZ);

            Texture2D blob = MakeSoftBlobTexture("GasBlob", 128);
            Material gasMat = MakeParticleMaterial("GasVapour", blob);

            float y = RoomHeight - 0.25f;
            float halfX = RoomWidth / 2f;
            float halfZ = RoomDepth / 2f;

            // Position, the direction it blows, and how wide the emitter slot is along the wall.
            var walls = new (Vector3 at, Vector3 inward, float run)[]
            {
                (new Vector3(0f, y, -halfZ + 0.3f), Vector3.forward, RoomWidth * 0.8f),
                (new Vector3(0f, y,  halfZ - 0.3f), Vector3.back,    RoomWidth * 0.8f),
                (new Vector3(-halfX + 0.3f, y, 0f), Vector3.right,   RoomDepth * 0.8f),
                (new Vector3( halfX - 0.3f, y, 0f), Vector3.left,    RoomDepth * 0.8f),
            };

            var systems = new System.Collections.Generic.List<ParticleSystem>();

            for (int i = 0; i < walls.Length; i++)
            {
                GameObject go = new GameObject($"GasEmitter_{i}");
                go.transform.SetParent(root.transform, false);
                go.transform.localPosition = walls[i].at;
                // Aimed inward AND down. Gas coming straight off a wall reads as a fan; this is a
                // vent in a ceiling corner, so it should already be falling as it arrives.
                go.transform.localRotation = Quaternion.LookRotation(
                    (walls[i].inward + Vector3.down * 0.7f).normalized, Vector3.up);

                ParticleSystem ps = go.AddComponent<ParticleSystem>();
                ConfigureGasEmitter(ps, walls[i].run, gasMat);
                systems.Add(ps);
            }

            return systems.ToArray();
        }

        private static void ConfigureGasEmitter(ParticleSystem ps, float run, Material mat)
        {
            // Stopped, and stopped from the start: the room is not being gassed until it is.
            ParticleSystem.MainModule main = ps.main;
            main.playOnAwake = false;
            main.loop = true;
            main.duration = 12f;
            // LONG-LIVED and SLOW. Vapour that clears in two seconds is a puff of smoke; this has to
            // still be in the air while the player goes down on top of it.
            main.startLifetime = new ParticleSystem.MinMaxCurve(7f, 11f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.22f, 0.55f);
            main.startSize = new ParticleSystem.MinMaxCurve(1.7f, 3.4f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            // Very low alpha per particle. The cloud is built out of overlap, not out of any one
            // billboard being visible - which is what stops it looking like sprites.
            main.startColor = new Color(0.95f, 0.965f, 0.98f, 0.085f);
            // Barely heavier than air. Enough that it settles rather than hangs at head height.
            main.gravityModifier = 0.014f;
            // WORLD, or the whole cloud would drag along with anything that moved the emitter.
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 260;

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = 13f;

            // A slot along the wall rather than a point, so it arrives as a sheet.
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(run, 0.25f, 0.15f);
            shape.randomDirectionAmount = 0.12f;

            // It pours in and then hangs. Without the damping it crosses the room and piles up
            // against the far wall, which is a wind tunnel rather than a room filling.
            ParticleSystem.LimitVelocityOverLifetimeModule limit = ps.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.dampen = 0.055f;
            limit.limit = new ParticleSystem.MinMaxCurve(0.5f);

            // THE ONE THAT MAKES IT VAPOUR. Straight-line blobs read as sprites however soft they
            // are; a slow large-scale wobble reads as something moving through air.
            ParticleSystem.NoiseModule noise = ps.noise;
            noise.enabled = true;
            noise.strength = new ParticleSystem.MinMaxCurve(0.55f);
            noise.frequency = 0.17f;
            noise.scrollSpeed = new ParticleSystem.MinMaxCurve(0.09f);
            noise.damping = true;
            noise.quality = ParticleSystemNoiseQuality.High;

            // Fade in over the first fifth, hold, fade out over the last third - so nothing ever
            // appears or vanishes, which is the other half of not looking like sprites.
            ParticleSystem.ColorOverLifetimeModule colour = ps.colorOverLifetime;
            colour.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, 0.22f),
                    new GradientAlphaKey(0.85f, 0.62f),
                    new GradientAlphaKey(0f, 1f),
                });
            colour.color = new ParticleSystem.MinMaxGradient(gradient);

            // Spreading as it goes, which is what a gas does and what hides the billboard edges.
            ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
            size.enabled = true;
            var grow = new AnimationCurve();
            grow.AddKey(0f, 0.55f);
            grow.AddKey(1f, 1.45f);
            size.size = new ParticleSystem.MinMaxCurve(1f, grow);

            ParticleSystem.RotationOverLifetimeModule spin = ps.rotationOverLifetime;
            spin.enabled = true;
            spin.z = new ParticleSystem.MinMaxCurve(-0.25f, 0.25f);

            var psr = ps.GetComponent<ParticleSystemRenderer>();
            psr.renderMode = ParticleSystemRenderMode.Billboard;
            psr.alignment = ParticleSystemRenderSpace.View;
            psr.sharedMaterial = mat;
            psr.sortMode = ParticleSystemSortMode.Distance;
            // Nothing this soft should be casting or catching a shadow.
            psr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            psr.receiveShadows = false;
        }

        // A FLAT DECAL: an unlit, alpha-blended quad for something drawn ON a surface - the digits
        // scrawled on the wall and the readouts lying on the pads.
        //
        // Unlit rather than Lit, and that is the point rather than a saving. Ink on a wall has no
        // shading of its own; a Lit decal picks up the ceiling fixtures and reads as a sticker with a
        // sheen. It also sidesteps the trap that produced a black band along the wall head, where a
        // Lit transparent material kept its specular at zero alpha.
        //
        // Deliberately NOT the particle material next door, which is the same idea for a different
        // renderer: URP's particle shaders expect vertex streams a MeshRenderer does not supply.
        private static Material MakeDecalMaterial(string name, Texture2D map, Color tint)
        {
            string path = $"{MaterialsDir}/{name}.mat";
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");

            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = shader;
            mat.SetTexture("_BaseMap", map);
            mat.mainTexture = map;
            mat.SetColor("_BaseColor", tint);
            mat.color = tint;

            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.SetFloat("_ZWrite", 0f);
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

            EditorUtility.SetDirty(mat);
            return mat;
        }

        // Unlit and alpha-blended, NOT premultiplied. The translucent Lit material this replaces
        // preserved specular at zero alpha, which is exactly how an invisible box ended up as a
        // visible band along the wall head - see BuildGasEmitters. Unlit has no specular to preserve.
        private static Material MakeParticleMaterial(string name, Texture2D map)
        {
            string path = $"{MaterialsDir}/{name}.mat";
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");

            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = shader;
            mat.SetTexture("_BaseMap", map);
            mat.mainTexture = map;
            mat.SetColor("_BaseColor", Color.white);

            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.SetFloat("_ZWrite", 0f);
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

            EditorUtility.SetDirty(mat);
            return mat;
        }

        // ROOM2-1'S PUZZLE: five digits scrawled on the south wall, and five counting pads standing
        // one grid cell out from it, one in front of each.
        //
        // WHY THE WALL AND NOT A SIGN. The digits go on the panel grid itself, in the second row up -
        // reachable height, where somebody standing in this room would have written them. The grid is
        // what makes "one digit per cell, in order" legible without a single line of instruction: the
        // wall is already divided into five columns and the answer is one per column.
        //
        // WHY ONE CELL OUT. A pad hard against the wall would be read as part of the wall; a cell's
        // gap makes the pairing an arrangement in the room rather than a label. It is also far enough
        // that the digit above is still in view while standing on the pad, which matters because the
        // player will be counting.
        private static (NumberLock, CountPad[]) BuildNumberLock(Transform parent, float roomCenterZ, Material propMat)
        {
            GameObject root = new GameObject("NumberLock");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = new Vector3(0f, 0f, roomCenterZ);

            // THE WALL OPPOSITE THE DOOR, and one grid cell out from it.
            //
            // The doorway is cut in this room's -Z wall, so the numbers go on +Z: the player comes in
            // by dropping through the ceiling, turns to find the way out, and the combination is on
            // the wall behind them rather than on the one they are walking toward. Reading it and
            // leaving are two different directions, which is what stops the room being solved facing
            // one way without ever looking round.
            float wallZ = RoomDepth / 2f;
            float padZ = wallZ - GridCellWidth;

            // Ten readouts and five pieces of graffiti. The pad digits are drawn clean and thin - a
            // machine showing a number - and the wall ones thick and wobbling, drawn by a hand.
            // RED, not white. They were white on a pale emissive face, which is very nearly
            // invisible - play reported the pads as having no numbers at all. Red is also what every
            // other readout in this game uses (the iteration label, the clock, the ending card),
            // because the walls are near-white and white text has nowhere to sit.
            //
            // The ink's RGB is what the texture is filled with; only the alpha is drawn. So this one
            // colour is the whole of the change.
            var readouts = new Texture2D[10];
            for (int d = 0; d < 10; d++)
                readouts[d] = MakeDigitTexture($"Digit_{d}", d, 128, 0.11f, 0f,
                                               new Color(0.92f, 0.10f, 0.08f), 4700 + d);

            // One material for all five readouts; CountPad swaps the texture through a property
            // block, so the pads share it without sharing a digit.
            Material digitMat = MakeDecalMaterial("PadDigit", readouts[0], Color.white);

            var pads = new CountPad[CycleTwoCombination.Length];

            for (int i = 0; i < CycleTwoCombination.Length; i++)
            {
                int digit = CycleTwoCombination[i];
                // Centre of the i-th grid column. Five columns across a wall of RoomWidth.
                float x = (i - (CycleTwoCombination.Length - 1) / 2f) * GridCellWidth;

                // THE GRAFFITI. A decal quad standing proud of the panel rather than a texture on the
                // panel's own material: the panels share one material and are driven by property
                // blocks for the boot and the ERROR glitch, so painting one of them would fight that.
                //
                // Each digit gets its own seed, so the two 1s in 3-1-4-1-5 are not the same 1 - which
                // is the whole difference between handwriting and a font.
                Texture2D scrawl = MakeDigitTexture($"Scrawl_{i}_{digit}", digit, 256, 0.055f, 0.028f,
                                                    Color.white, 9100 + i * 31);
                // Near-black ink, slightly transparent, so it reads as marked ON the panel rather
                // than as a black shape floating in front of it.
                Material scrawlMat = MakeDecalMaterial($"WallInk_{i}", scrawl,
                                                       new Color(0.13f, 0.13f, 0.15f, 0.92f));

                GameObject mark = Prim(PrimitiveType.Quad, $"Scrawl_{i}", root.transform,
                    // Second row up, centred in its cell. Proud of the panel face by a hair so it
                    // cannot z-fight with it.
                    new Vector3(x, GridCellHeight * 1.5f, wallZ - 0.032f),
                    new Vector3(GridCellWidth * 0.62f, GridCellHeight * 0.72f, 1f),
                    scrawlMat, removeCollider: true);
                // Facing into the room. A quad's front is -Z of its own transform.
                // A Unity quad faces -Z, which is already into the room from the +Z wall.
                mark.transform.localRotation = Quaternion.identity;

                pads[i] = BuildCountPad(root.transform, $"CountPad_{i}", new Vector3(x, 0f, padZ),
                                        digit, propMat, digitMat, readouts);
            }

            NumberLock numberLock = root.AddComponent<NumberLock>();
            numberLock.pads = pads;
            return (numberLock, pads);
        }

        // One counting pad. Chunkier than room1-1's plain disc, because this one has to be READ as
        // well as stood on: a ring, a recessed face that changes colour when it matches, and the
        // digit it currently holds lying on that face.
        private static CountPad BuildCountPad(Transform parent, string name, Vector3 localPos,
                                              int target, Material propMat, Material digitMat,
                                              Texture2D[] readouts)
        {
            GameObject root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = localPos;

            // Everything that moves hangs off this, so the pad can sink without the logical volume -
            // which is the root - moving with it.
            GameObject plunger = new GameObject("Plunger");
            plunger.transform.SetParent(root.transform, false);

            const float padRadius = 0.42f;

            // STACKED, NOT NESTED, and that is the second attempt at this.
            //
            // The first built a "ring" that was actually a SOLID cylinder 55mm tall and then set the
            // face and the readout INTO it at 43mm and 50mm - both entirely inside solid geometry, so
            // the pad rendered as a plain dark disc with no number and no state. Nothing about the
            // numbers looked wrong; the parts were simply underneath.
            //
            // Every height here is absolute and every part sits ABOVE the one under it. Note a Unity
            // cylinder is TWO units tall, so its y-scale is a HALF-height - which is what made the
            // first version's arithmetic come out shallow.
            // 100mm, up from 50mm. At half this the press was invisible at a walking glance, and
            // for a fixture whose whole job is to be pressed a countable number of times that is the
            // wrong thing to be subtle about. It is still low enough to walk onto rather than step up
            // onto - the controller's step offset clears it.
            const float ringTop = 0.10f;
            const float faceThickness = 0.014f;
            const float faceTop = ringTop + 0.010f;      // proud of the ring, not sunk into it
            const float digitLift = 0.0015f;

            Material ringMat = MakeColorMaterial("CountPadRing", new Color(0.20f, 0.21f, 0.24f));
            SetSmoothness(ringMat, 0.62f);

            Prim(PrimitiveType.Cylinder, "Ring", plunger.transform,
                new Vector3(0f, ringTop / 2f, 0f),
                new Vector3(padRadius * 2f, ringTop / 2f, padRadius * 2f), ringMat,
                removeCollider: true);

            // Standing proud of the ring and overlapping it, so there is no seam to see between them.
            // Emissive and driven by CountPad, so a matched pad reads from across the room without
            // having to walk over and look at the number.
            Material faceMat = MakeEmissiveMaterial("CountPadFace", new Color(0.55f, 0.60f, 0.68f), 1.6f);
            GameObject face = Prim(PrimitiveType.Cylinder, "Face", plunger.transform,
                new Vector3(0f, faceTop - faceThickness / 2f, 0f),
                new Vector3(padRadius * 1.55f, faceThickness / 2f, padRadius * 1.55f), faceMat,
                removeCollider: true);

            // The readout, lying flat and clear of everything. Rotated +90 about X, which turns a
            // quad's -Z normal to face straight up.
            GameObject digit = Prim(PrimitiveType.Quad, "Digit", plunger.transform,
                new Vector3(0f, faceTop + digitLift, 0f),
                new Vector3(padRadius * 1.2f, padRadius * 1.2f, 1f), digitMat,
                removeCollider: true);
            digit.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            CountPad pad = root.AddComponent<CountPad>();
            pad.target = target;
            // Derived from the visible disc, so the hit area and the thing you can see cannot drift
            // apart - the mistake FloorButton documents having made.
            pad.activationRadius = padRadius + 0.05f;
            pad.faceRenderer = face.GetComponent<Renderer>();
            pad.digitRenderer = digit.GetComponent<Renderer>();
            pad.digitTextures = readouts;
            pad.plunger = plunger.transform;
            pad.audioSource = MakeSource(root.transform, "PadAudio", spatialBlend: 1f, volume: 0.85f);
            pad.stepClip = LoadClip(SfxDir, "sfx_floor_button_press");
            return pad;
        }

        // WHAT THE PLAYER FALLS THROUGH between the two storeys: a square tube joining the hole in
        // one cycle's floor to the hole in the next one's ceiling, across the service void.
        //
        // Without it the void is open to the room - you would drop out of the floor into a roof space
        // with the plinth housing and the underside of the slab on show. With it, the drop reads as
        // going down a shaft, and the void either side stays sealed.
        //
        // Four walls and no cap at either end, because both ends are the holes it exists to connect.
        private static void BuildExitShaft(Transform parent, string name, float roomCenterZ, Material mat)
        {
            GameObject shaft = new GameObject(name);
            shaft.transform.SetParent(parent, false);
            // Hung from the underside of the floor it starts at, running down the whole void.
            shaft.transform.localPosition = new Vector3(0f, -WallThickness, roomCenterZ + CycleExitZ);

            float half = GridCellWidth / 2f;
            float outer = GridCellWidth + 2f * WallThickness;

            Prim(PrimitiveType.Cube, "Shaft_West", shaft.transform,
                new Vector3(-half - WallThickness / 2f, -ServiceVoid / 2f, 0f),
                new Vector3(WallThickness, ServiceVoid, outer), mat);
            Prim(PrimitiveType.Cube, "Shaft_East", shaft.transform,
                new Vector3(half + WallThickness / 2f, -ServiceVoid / 2f, 0f),
                new Vector3(WallThickness, ServiceVoid, outer), mat);
            Prim(PrimitiveType.Cube, "Shaft_South", shaft.transform,
                new Vector3(0f, -ServiceVoid / 2f, -half - WallThickness / 2f),
                new Vector3(GridCellWidth, ServiceVoid, WallThickness), mat);
            Prim(PrimitiveType.Cube, "Shaft_North", shaft.transform,
                new Vector3(0f, -ServiceVoid / 2f, half + WallThickness / 2f),
                new Vector3(GridCellWidth, ServiceVoid, WallThickness), mat);
        }

        // The lid over that hole: a slab of floor that slides aside when the console is full.
        //
        // Slides sideways INTO the surrounding floor slab, which is solid, so it is out of sight the
        // moment it is open - the same thing a door slab does inside its pocket, without needing a
        // pocket built for it. Authored closed, so the room can be looked at in the editor as the
        // player first meets it.
        private static CycleExit BuildCycleExit(Transform parent, float roomCenterZ, Material floorMat, Transform player)
        {
            GameObject go = new GameObject("CycleExit");
            go.transform.SetParent(parent, false);
            // At the floor plane of the room it belongs to, which is what `throughDrop` is measured
            // from - "the player has fallen well below the floor they were standing on".
            go.transform.localPosition = new Vector3(0f, 0f, roomCenterZ + CycleExitZ);

            // BOTH LIDS ARE FLUSH WITH THE SURFACE THEY FILL, and both retract into the service
            // void - the floor one drops as it slides, the ceiling one rises.
            //
            // The first version recessed each by 10mm to dodge a coplanar-face z-fight against the
            // slab it slid into. That worked and left a 10mm hatch outline in the ceiling of the room
            // below, lit differently from everything round it - which is exactly what a lid is not
            // supposed to look like when it is shut. Retracting out of the slab instead means there
            // is nothing to be coplanar with, so the lid can fill its hole exactly.
            GameObject upper = Prim(PrimitiveType.Cube, "Cover_Floor", go.transform,
                new Vector3(0f, -WallThickness / 2f, 0f),
                new Vector3(GridCellWidth, WallThickness, GridCellWidth), floorMat);

            float ceilingMid = -(ServiceVoid + 1.5f * WallThickness);
            GameObject lower = Prim(PrimitiveType.Cube, "Cover_Ceiling", go.transform,
                new Vector3(0f, ceilingMid, 0f),
                new Vector3(GridCellWidth, WallThickness, GridCellWidth), floorMat);

            CycleExit exit = go.AddComponent<CycleExit>();
            exit.covers = new[] { upper.transform, lower.transform };
            // Sideways by its own width so the hole is fully clear, and out of its own slab by rather
            // more than the slab is thick so neither lid can be seen edge-on through the opening.
            exit.openOffsets = new[]
            {
                new Vector3(GridCellWidth, -0.16f, 0f),
                new Vector3(GridCellWidth,  0.16f, 0f),
            };
            exit.player = player;
            exit.audioSource = MakeSource(go.transform, "ExitAudio", spatialBlend: 1f, volume: 0.9f);
            exit.openClip = LoadClip(SfxDir, "sfx_door_open");
            exit.sealClip = LoadClip(SfxDir, "sfx_power_down");
            return exit;
        }

        private static void BuildShell(Transform parent, Material floorMat, Material grooveMat, Material panelMat)
        {
            // The doorway is cut out of the panelling as an exact rectangle, so panels frame the
            // door instead of the door having to fit a whole number of cells.
            Rect doorway = new Rect(-DoorWidth / 2f, 0f, DoorWidth, DoorHeight);

            // Six rooms in a line, each sharing a divider with the next: a room's north wall and its
            // neighbour's south wall face each other across the door pocket, each with the same
            // doorway cut out of its panelling, its backing and its collision, so the opening is a
            // real hole. Room1 is the only one with no doorway to the south, and ROOM4 THE ONLY ONE
            // WITH NONE TO THE NORTH - it is the end of the building, and there is deliberately
            // nothing past it to look at or walk to.
            //
            // Room2West and Room2East used to be a pair of side rooms turned ninety degrees off
            // Room2's east and west walls. They are ON THE CHAIN now, the same shape and orientation
            // as every other room here, between Room2 and Room3 - see Build() for why: a hub with
            // three doors home to the same room read as a walk back and forth between them rather
            // than as progress, and the fix was to let the coloured doors BE the corridor instead of
            // branching off it.
            BuildRoomShell(parent, "Room1", 0f, floorMat, grooveMat, panelMat, Rect.zero, doorway);
            BuildRoomShell(parent, "Room2", RoomPitch, floorMat, grooveMat, panelMat, doorway, doorway);
            BuildRoomShell(parent, "Room2West", 2f * RoomPitch, floorMat, grooveMat, panelMat, doorway, doorway);
            BuildRoomShell(parent, "Room2East", 3f * RoomPitch, floorMat, grooveMat, panelMat, doorway, doorway);
            BuildRoomShell(parent, "Room3", 4f * RoomPitch, floorMat, grooveMat, panelMat, doorway, doorway);
            // Identical to the others in every way, and that is the point rather than a saving:
            // the room the player finally gets out into is the same white cell they have been in
            // for the whole run.
            //
            // EXCEPT FOR THE HOLE IN ITS FLOOR. `room1-0` is the hinge: it closes cycle 1 and opens
            // onto cycle 2, and the way on is a grid cell of floor behind the console that slides
            // aside once the console is full. Its north wall stays solid - there is still nothing past
            // the end of the building, the way out is DOWN.
            BuildRoomShell(parent, "Room4", 5f * RoomPitch, floorMat, grooveMat, panelMat, doorway, Rect.zero,
                floorHole: CycleExitHole);

            // One pocket per join, all UNCAPPED: every one of them has a room through it, and a cap
            // would be a wall across the only way to the next. Room4 needs no pocket of its own - its
            // north wall has no doorway cut in it, so there is no cavity there to close, the same
            // reason the calibration room has none.
            BuildDoorPocketFill(parent, "DoorPocketFill_1", 0f, grooveMat, capFarSide: false);
            BuildDoorPocketFill(parent, "DoorPocketFill_2", RoomPitch, grooveMat, capFarSide: false);
            BuildDoorPocketFill(parent, "DoorPocketFill_3", 2f * RoomPitch, grooveMat, capFarSide: false);
            BuildDoorPocketFill(parent, "DoorPocketFill_4", 3f * RoomPitch, grooveMat, capFarSide: false);
            BuildDoorPocketFill(parent, "DoorPocketFill_5", 4f * RoomPitch, grooveMat, capFarSide: false);

            // A sealed copy of the same shell, well clear of the chain, used for nothing but the
            // mouse-sensitivity step before iteration 1. Rect.zero for both cutouts, so it has no
            // doorways at all - the player is meant to look around it, not leave it, and there is
            // nothing to leave through.
            //
            // Empty on purpose: no bed, no pad, no door, no lamp. What is being judged is how fast
            // the room goes round, and furniture is something to look AT rather than something that
            // shows motion. The panelling does that better than any prop - it is a regular grid, so
            // the grooves sweeping past give the eye an unambiguous read on speed.
            //
            // Far enough out that no slab or wall overrun can touch Room1: this spans Z -26.95 to
            // -16.45 against Room1's -5.25 to 5.25.
            BuildRoomShell(parent, CalibrationRoomName, CalibrationRoomZ,
                floorMat, grooveMat, panelMat, Rect.zero, Rect.zero);

            Material fixtureMat = MakeEmissiveMaterial("CeilingFixture", Color.white, 3.5f);

            // Shadows only in Room1. Every additional light's shadow shares one atlas, and the
            // rooms past the first hold nothing that casts a shadow worth the map: Room2 is
            // balloons, Room3 is two floor pads.
            BuildCeilingLights(parent, "Room1", 0f, fixtureMat, castShadows: true);
            BuildCeilingLights(parent, "Room2", RoomPitch, fixtureMat, castShadows: false);
            // Room2West's fixtures are kept: the chess board dims them and brings them back up as
            // its reward, so something downstream needs the references rather than just the room.
            (Room2WestLights, Room2WestPanels) =
                BuildCeilingLights(parent, "Room2West", 2f * RoomPitch, fixtureMat, castShadows: false);
            BuildCeilingLights(parent, "Room2East", 3f * RoomPitch, fixtureMat, castShadows: false);
            BuildCeilingLights(parent, "Room3", 4f * RoomPitch, fixtureMat, castShadows: false);
            BuildCeilingLights(parent, "Room4", 5f * RoomPitch, fixtureMat, castShadows: false);
            BuildCeilingLights(parent, CalibrationRoomName, CalibrationRoomZ, fixtureMat, castShadows: false);

            // Built after the lights, so the probes capture the rooms already lit. The calibration
            // room needs its own: the walls are at 0.85 smoothness, and without a probe to reflect
            // they mirror the procedural sky and come out tinted blue.
            BuildReflectionProbe(parent, "Room1", 0f);
            BuildReflectionProbe(parent, "Room2", RoomPitch);
            BuildReflectionProbe(parent, "Room2West", 2f * RoomPitch);
            BuildReflectionProbe(parent, "Room2East", 3f * RoomPitch);
            BuildReflectionProbe(parent, "Room3", 4f * RoomPitch);
            BuildReflectionProbe(parent, "Room4", 5f * RoomPitch);
            BuildReflectionProbe(parent, CalibrationRoomName, CalibrationRoomZ);
        }

        // Fills the cavity between the two rooms' walls, everywhere except the volume the door slab
        // actually sweeps through.
        //
        // Left as one open pocket, that cavity ran the full width of the building and exited to the
        // sky at both ends - standing in the doorway and looking sideways showed a 0.1m x 5m slot
        // straight to the outside, which is the "you can see through between the walls" report.
        // Capping it also gives the opening a proper reveal instead of a hollow slot at the jamb.
        //
        // Rotated by yaw for a door in a side wall, for the same reason BuildDoorShell is: the pocket
        // geometry is identical, only its axis differs, and a second copy of it would be a second place
        // for the "you can see between the walls" bug to come back. wallHalfExtent is the wall's
        // distance from the room centre along the door's normal, crossHalfWidth how far the fill runs
        // along the wall.
        private static void BuildDoorPocketFill(Transform parent, string name, float roomCenterZ, Material mat,
                                                bool capFarSide,
                                                float wallHalfExtent = RoomDepth / 2f,
                                                float crossHalfWidth = RoomWidth / 2f + WallDepth,
                                                float yaw = 0f)
        {
            GameObject fill = new GameObject(name);
            fill.transform.SetParent(parent, false);
            // The room offset and the rotation both live on the root now, so the measurements below
            // are relative and read the same at any yaw.
            fill.transform.localPosition = new Vector3(0f, 0f, roomCenterZ);
            fill.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);

            float pocketCenterZ = wallHalfExtent + WallDepth + DoorPocketDepth / 2f;

            // Matches the floor and ceiling slabs, so the fill reaches the outer face of the walls it
            // runs between and the divider is closed off at the same plane they are.
            float halfWidth = crossHalfWidth;
            Rect pocket = Rect.MinMaxRect(-halfWidth, 0f, halfWidth, RoomHeight);

            // The slab's full sweep: closed at -DoorWidth/2, open a further DoorWidth to the right.
            // The clearance keeps the fill's faces off the door's, which would otherwise be exactly
            // coincident planes.
            const float clearance = 0.01f;
            Rect doorSweep = Rect.MinMaxRect(
                -DoorWidth / 2f - clearance, 0f,
                DoorWidth / 2f + DoorWidth + clearance, DoorHeight + clearance);

            // No colliders: the walls either side of the doorway already carry the collision, and
            // the only way to reach this volume is through the opening the fill deliberately leaves.
            int piece = 0;
            foreach (Rect part in SubtractRect(pocket, doorSweep))
            {
                if (part.width <= 0.001f || part.height <= 0.001f) continue;

                Prim(PrimitiveType.Cube, $"Fill_{piece++}", fill.transform,
                    new Vector3(part.center.x, part.center.y, pocketCenterZ),
                    new Vector3(part.width, part.height, DoorPocketDepth),
                    mat, removeCollider: true);
            }

            // Seals the far mouth of the pocket where there is no next room to close it. Without
            // this the doorway is a hole to the outside once the door slides clear - the same
            // failure the pocket fill exists to stop, just at the other end of the building. It
            // KEEPS its collider: this is the end of the world for now, and the player must not be
            // able to walk out through the door they just unlocked.
            if (!capFarSide) return;

            Prim(PrimitiveType.Cube, "FarCap", fill.transform,
                new Vector3(0f, RoomHeight / 2f, pocketCenterZ + DoorPocketDepth / 2f + WallThickness / 2f),
                new Vector3(halfWidth * 2f, RoomHeight, WallThickness),
                mat);
        }

        // westCutout/eastCutout default to none and nothing in the chain uses them any more -
        // Room2West and Room2East were the one thing that did, back when they were side rooms turned
        // ninety degrees off Room2's walls. Left on `BuildPanelWall`, which has always taken a cutout
        // in wall-local coordinates and been indifferent to which axis the wall runs along, on the
        // reasoning that a wall with a doorway in an unusual side is cheap to keep able and expensive
        // to re-derive the day something needs it again.
        // floorHole / ceilingHole: a rectangle in ROOM-LOCAL XZ (x across the room, y of the Rect being
        // Z relative to zCenter) to leave out of that slab. Rect.zero means none, which is every room
        // but the two a cycle boundary passes through.
        private static void BuildRoomShell(Transform parent, string roomName, float zCenter, Material floorMat, Material grooveMat, Material panelMat, Rect southCutout, Rect northCutout, Rect westCutout = default, Rect eastCutout = default, Rect floorHole = default, Rect ceilingHole = default)
        {
            GameObject room = new GameObject(roomName);
            room.transform.SetParent(parent, false);
            Transform t = room.transform;

            float minX = -RoomWidth / 2f, maxX = RoomWidth / 2f;
            float minZ = zCenter - RoomDepth / 2f, maxZ = zCenter + RoomDepth / 2f;

            BuildSlab(t, "Floor", -WallThickness / 2f, zCenter, floorMat, floorHole);
            BuildSlab(t, "Ceiling", RoomHeight + WallThickness / 2f, zCenter, floorMat, ceilingHole);

            BuildPanelWall(t, "Wall_South", new Vector3(0f, 0f, minZ), Vector3.right, Vector3.forward, RoomWidth, grooveMat, panelMat, southCutout);
            BuildPanelWall(t, "Wall_North", new Vector3(0f, 0f, maxZ), Vector3.right, Vector3.back, RoomWidth, grooveMat, panelMat, northCutout);
            BuildPanelWall(t, "Wall_West", new Vector3(minX, 0f, zCenter), Vector3.forward, Vector3.right, RoomDepth, grooveMat, panelMat, westCutout);
            BuildPanelWall(t, "Wall_East", new Vector3(maxX, 0f, zCenter), Vector3.forward, Vector3.left, RoomDepth, grooveMat, panelMat, eastCutout);
        }

        // A floor or a ceiling, with an optional hole in it.
        //
        // Slabs run the full room PITCH, not just the interior, so neighbouring rooms' floors meet
        // exactly under the divider. Sized to the interior they would leave an open gap in the doorway
        // threshold and the player would drop through it.
        //
        // THE HOLE REUSES `SubtractRect`, which is the whole reason a floor opening turned out to be
        // cheap. That function is pure Rect arithmetic - it has no idea its inputs are usually
        // along-a-wall and height - so handing it (x, z) instead cuts a floor exactly the way it cuts
        // panelling, collision and backing. Without a hole this emits one cube named `Floor` or
        // `Ceiling` exactly as before, which keeps every room that has no opening byte-identical.
        private static void BuildSlab(Transform t, string name, float yCenter, float zCenter, Material mat, Rect holeLocalXZ)
        {
            float halfX = RoomWidth / 2f + WallDepth;
            Rect slab = Rect.MinMaxRect(-halfX, zCenter - RoomPitch / 2f, halfX, zCenter + RoomPitch / 2f);

            // The hole arrives in room-local Z; the slab is in the room's own frame, where Z is
            // absolute. One offset reconciles them, and it is why the SAME Rect can be handed to two
            // rooms one storey apart and line up.
            Rect hole = holeLocalXZ.width > 0f && holeLocalXZ.height > 0f
                ? Rect.MinMaxRect(holeLocalXZ.xMin, zCenter + holeLocalXZ.yMin,
                                  holeLocalXZ.xMax, zCenter + holeLocalXZ.yMax)
                : new Rect();

            var parts = SubtractRect(slab, hole);

            if (parts.Count == 1)
            {
                Prim(PrimitiveType.Cube, name, t, new Vector3(0f, yCenter, zCenter),
                     new Vector3(slab.width, WallThickness, slab.height), mat);
                return;
            }

            for (int i = 0; i < parts.Count; i++)
            {
                Rect part = parts[i];
                Prim(PrimitiveType.Cube, $"{name}_{i + 1}", t,
                     new Vector3(part.center.x, yCenter, part.center.y),
                     new Vector3(part.width, WallThickness, part.height), mat);
            }
        }

        // Builds one wall as a recessed backing slab plus a grid of raised panels, so the seams are
        // actual grooves rather than a painted-on pattern.
        // faceCenterAtBase: the point on the finished panel surface at the wall's mid-width and floor level.
        // inward: unit normal pointing into the room.
        // cutout: a rectangle in wall-local (along-the-wall, height) coordinates to leave empty.
        // Rect.zero means none.
        private static void BuildPanelWall(Transform parent, string name, Vector3 faceCenterAtBase, Vector3 rightDir, Vector3 inward, float wallWidth, Material backingMat, Material panelMat, Rect cutout)
        {
            Vector3 depthAxis = new Vector3(Mathf.Abs(inward.x), Mathf.Abs(inward.y), Mathf.Abs(inward.z));
            Vector3 widthAxis = new Vector3(Mathf.Abs(rightDir.x), Mathf.Abs(rightDir.y), Mathf.Abs(rightDir.z));

            Rect wallRect = Rect.MinMaxRect(-wallWidth / 2f, 0f, wallWidth / 2f, RoomHeight);

            // Backing and collision overrun both ends of the wall by its own depth, so adjacent
            // walls interpenetrate at the corners instead of merely abutting.
            //
            // Sized to the interior exactly, two perpendicular walls meet along a line and leave a
            // WallDepth-square column that neither one covers, running the full height of the room.
            // It never opens wide enough for a raycast to find - the void sits behind a zero-width
            // seam - but rendering two slabs that only touch produces pinhole cracks at the corner
            // from grazing angles. Note this is why thickening the walls would make it WORSE: the
            // void is WallDepth squared.
            //
            // The panel grid is left at the interior width, so the visible cell layout is unchanged
            // and the overrun is buried inside the adjacent wall.
            Rect structureRect = Rect.MinMaxRect(
                wallRect.xMin - WallDepth, wallRect.yMin, wallRect.xMax + WallDepth, wallRect.yMax);

            // The backing slab is cut by the same doorway rect as the panels. As one solid slab it
            // left the doorway a dead-end recess: you could walk through (collision was already
            // cut) but you were looking at wall.
            GameObject backing = new GameObject(name);
            backing.transform.SetParent(parent, false);
            int slab = 0;
            foreach (Rect part in SubtractRect(structureRect, cutout))
            {
                if (part.width <= 0.001f || part.height <= 0.001f) continue;

                Prim(PrimitiveType.Cube, $"Backing_{slab++}", backing.transform,
                    faceCenterAtBase + rightDir * part.center.x + Vector3.up * part.center.y
                        - inward * (GrooveDepth + WallThickness / 2f),
                    widthAxis * part.width + Vector3.up * part.height + depthAxis * WallThickness,
                    backingMat, removeCollider: true);
            }

            // Collision is built separately from invisible boxes rather than left on the backing,
            // for two reasons: the slab sits GrooveDepth too far back (the player would walk past
            // the panels and the camera's near plane would clip through the wall), and it has to be
            // cut around the doorway.
            GameObject collision = new GameObject(name + "_Collision");
            collision.transform.SetParent(parent, false);
            int solid = 0;
            foreach (Rect part in SubtractRect(structureRect, cutout))
            {
                if (part.width <= 0.001f || part.height <= 0.001f) continue;

                GameObject block = new GameObject($"Solid_{solid++}");
                block.transform.SetParent(collision.transform, false);
                block.transform.localPosition = faceCenterAtBase
                    + rightDir * part.center.x
                    + Vector3.up * part.center.y
                    - inward * (WallDepth / 2f);

                BoxCollider blockCollider = block.AddComponent<BoxCollider>();
                blockCollider.size = widthAxis * part.width + Vector3.up * part.height + depthAxis * WallDepth;
            }

            GameObject panels = new GameObject(name + "_Panels");
            panels.transform.SetParent(parent, false);

            float groove = GridLineThickness;
            int cols = Mathf.RoundToInt(wallWidth / GridCellWidth);

            int piece = 0;
            for (int col = 0; col < cols; col++)
            {
                float cellAlong = -wallWidth / 2f + col * GridCellWidth;

                for (int row = 0; row * GridCellHeight < RoomHeight - 0.001f; row++)
                {
                    float bottom = row * GridCellHeight;
                    // The top row is a partial cell whenever the room height isn't a whole number
                    // of cells, so clamp it rather than letting panels poke through the ceiling.
                    float top = Mathf.Min(bottom + GridCellHeight, RoomHeight);

                    Rect panel = Rect.MinMaxRect(
                        cellAlong + groove / 2f, bottom + groove / 2f,
                        cellAlong + GridCellWidth - groove / 2f, top - groove / 2f);

                    foreach (Rect part in SubtractRect(panel, cutout))
                    {
                        if (part.width <= 0.02f || part.height <= 0.02f) continue;

                        Vector3 pos = faceCenterAtBase
                            + rightDir * part.center.x
                            + Vector3.up * part.center.y
                            - inward * (GrooveDepth / 2f);
                        Vector3 scale = widthAxis * part.width
                            + Vector3.up * part.height
                            + depthAxis * GrooveDepth;

                        Prim(PrimitiveType.Cube, $"Panel_{piece++}", panels.transform, pos, scale, panelMat, removeCollider: true);
                    }
                }
            }
        }

        // Returns `panel` minus `hole` as up to four axis-aligned pieces (below, above, left,
        // right of the hole). Lets a doorway of any size be cut out of the panel grid without the
        // door having to line up with cell boundaries.
        private static System.Collections.Generic.List<Rect> SubtractRect(Rect panel, Rect hole)
        {
            var parts = new System.Collections.Generic.List<Rect>();

            bool overlaps = hole.width > 0f && hole.height > 0f && panel.Overlaps(hole);
            if (!overlaps)
            {
                parts.Add(panel);
                return parts;
            }

            float bandMin = Mathf.Max(panel.yMin, hole.yMin);
            float bandMax = Mathf.Min(panel.yMax, hole.yMax);

            if (hole.yMin > panel.yMin)
                parts.Add(Rect.MinMaxRect(panel.xMin, panel.yMin, panel.xMax, hole.yMin));
            if (hole.yMax < panel.yMax)
                parts.Add(Rect.MinMaxRect(panel.xMin, hole.yMax, panel.xMax, panel.yMax));
            if (hole.xMin > panel.xMin)
                parts.Add(Rect.MinMaxRect(panel.xMin, bandMin, hole.xMin, bandMax));
            if (hole.xMax < panel.xMax)
                parts.Add(Rect.MinMaxRect(hole.xMax, bandMin, panel.xMax, bandMax));

            return parts;
        }

        // floorY / zCentre are WORLD, and they have to be, which is the trap this signature exists to
        // close. `PlaceModel` measures the model's RENDERED bounds - which are world-space - and
        // corrects `localPosition` by the difference, so its `targetXZCenter` and `floorY` are world
        // heights whatever the parent is doing. Parenting a bed to a node carrying the room's offset
        // therefore does NOT move it: cycle 2's bed came out standing in Room1, on cycle 1's floor,
        // exactly where a bed asked for y=0 and z=0.7 belongs.
        //
        // The spawn point below is unaffected - it is placed with `localPosition` and inherits the
        // parent normally - which is why the player woke in the right room with no bed in it.
        // yaw turns the whole arrangement about the room centre - the bed, which end of the room it is
        // at, and which way the sleeper faces - rather than spinning the model on the spot. Cycle 2
        // uses 180 so its bed head is at the other end from cycle 1's.
        //
        // Applied as a SIGN on the layout rather than a rotation on the parent, because `PlaceModel`
        // corrects `localPosition` by a world-space delta: under a rotated parent that correction goes
        // in the wrong direction entirely. Anything placed with plain `localPosition` (the spawn point
        // here, the nightstand elsewhere) can use a rotated wrapper quite safely.
        private static (Transform bed, Transform spawn) BuildBed(Transform parent, Material mat,
                                                                 float floorY = 0f, float zCentre = 0f,
                                                                 float yaw = 0f)
        {
            // Combined bed + bedside table model, which replaces the old separate Kenney bed,
            // drawer, lamp and cup. It is authored Z-up in centimetres, hence the -90 X rotation
            // and the 0.01 scale - without those it arrives 100x too big and lying on its side.
            // Chosen for the rumpled, slept-in bedding: the loop opens every iteration by waking up
            // here, so the bed has to look like someone just got out of it. The alternatives were
            // all neatly made (or, in old_bed's case, a bare frame with no mattress at all).
            // -90 X uprights it (authored Z-up), then 180 Y turns the headboard toward the door.
            // Keep the glTF materials it ships with - they carry metallic/roughness maps that a
            // hand-rolled URP/Lit stand-in would drop.
            // +1 at yaw 0, -1 at yaw 180. Rounded, so it is exactly a sign rather than a float that
            // happens to be near one.
            float facing = Mathf.Round(Mathf.Cos(yaw * Mathf.Deg2Rad));

            (GameObject bed, _) = PlaceModel($"{FurnitureDir}/messy_bed.glb", parent, "Bed",
                new Vector3(0f, 0f, zCentre + 0.7f * facing), floorY, 0.00941f, addBoxCollider: true,
                rotation: Quaternion.Euler(-90f, 180f + yaw, 0f));

            UseSinglePillow(bed, keepName: "Pillow_2", hideName: "Pillow_1", centreX: 0f);

            // This model bakes contact shadows into its occlusion maps, which only looks right in
            // the exact pose it was authored in. Moving the pillow and removing its neighbour left
            // those shadows painted onto the bedding - a dark smear beside the pillow, and a hard
            // black band across the pillow itself. Switch baked occlusion off for both; the scene's
            // SSAO supplies real contact shadows that follow the geometry instead.
            DisableBakedOcclusion(bed, materialName: "Sheet", assetName: "BedSheet");
            DisableBakedOcclusion(bed, materialName: "Pillow_2", assetName: "BedPillow");

            // Hard against the foot of the bed, facing away from it down the room - you have just
            // got up, so the bed is behind you and the open floor is what you see.
            //
            // This sat at Z=-1.75 facing 180 (away from everything), which put the player 1.42m
            // past the foot of the bed with their back to it - so the wake-up sequence, which
            // poses the eye as if lying down and then sits you up, ended with the bed out of
            // frame entirely and you looking at the blank south wall. It never read as waking up
            // in the bed because you were nowhere near it.
            //
            // The player cannot be spawned *in* the bed: the model carries a full-volume
            // BoxCollider (Y 0..0.89) and the CharacterController would spawn interpenetrating it
            // and get shoved out on the first frame. The bed's foot is at Z=-0.33 and the
            // controller's radius is 0.3, so -0.7 is as close as it can stand without overlapping.
            // Keeping X=0 preserves the deliberate door/bed/spawn centre line.
            GameObject spawn = new GameObject("BedSpawnPoint");
            spawn.transform.SetParent(parent, false);
            spawn.transform.localPosition = new Vector3(0f, 0.05f, -0.7f * facing);
            spawn.transform.localRotation = Quaternion.Euler(0f, 180f + yaw, 0f);

            return (bed.transform, spawn.transform);
        }

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


        // THE SIX SYMBOLS, drawn once and used twice: as an opaque picture on a cube and on the
        // recess that wants it, and as a HUD glyph in the carried row. One drawing rather than two
        // means the thing on the wall and the thing in the readout can never drift apart.
        //
        // A CARD FAMILY, extended past the four suits with a star and a crescent. The spec named
        // three; six cubes need six, and picking the two extras from the same visual world - flat,
        // solid, symmetrical, no interior detail - is what keeps them reading as one set rather than
        // as four suits and two strays.
        private struct SymbolSpec
        {
            public string id;
            public string name;
            public System.Action<IconCanvas> draw;
        }

        private static SymbolSpec[] CubeSymbols() => new[]
        {
            new SymbolSpec { id = "Cube_Spade", name = "spade", draw = icon =>
            {
                // The blade: a triangle standing on two lobes.
                icon.Shape(pt =>
                {
                    if (pt.y < 0.42f || pt.y > 0.90f) return false;
                    float k = (0.90f - pt.y) / 0.48f;
                    return Mathf.Abs(pt.x - 0.5f) <= 0.40f * k;
                });
                icon.Disc(new Vector2(0.30f, 0.44f), 0.20f);
                icon.Disc(new Vector2(0.70f, 0.44f), 0.20f);
                // The stem, wide at the foot and narrow where it meets the blade.
                icon.Shape(pt =>
                {
                    if (pt.y < 0.10f || pt.y > 0.44f) return false;
                    float k = (pt.y - 0.10f) / 0.34f;
                    return Mathf.Abs(pt.x - 0.5f) <= Mathf.Lerp(0.17f, 0.045f, k);
                });
            }},
            new SymbolSpec { id = "Cube_Heart", name = "heart", draw = icon =>
            {
                // The textbook parametric heart curve - the one every graphing calculator and
                // Valentine's card uses - traced as a polygon and filled by point-in-polygon, rather
                // than two discs butted against a triangle. Discs-and-a-wedge always read as exactly
                // that up close: two circles with a corner where the straight edges meet them. This
                // is one closed curve with no seam anywhere on it.
                const int n = 240;
                Vector2[] poly = new Vector2[n];
                float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
                for (int i = 0; i < n; i++)
                {
                    float t = i * Mathf.PI * 2f / n;
                    float sx = 16f * Mathf.Pow(Mathf.Sin(t), 3f);
                    float sy = 13f * Mathf.Cos(t) - 5f * Mathf.Cos(2f * t) - 2f * Mathf.Cos(3f * t) - Mathf.Cos(4f * t);
                    poly[i] = new Vector2(sx, sy);
                    minX = Mathf.Min(minX, sx); maxX = Mathf.Max(maxX, sx);
                    minY = Mathf.Min(minY, sy); maxY = Mathf.Max(maxY, sy);
                }
                float span = Mathf.Max(maxX - minX, maxY - minY);
                float cx = (minX + maxX) * 0.5f, cy = (minY + maxY) * 0.5f;
                const float fill = 0.74f; // how much of the 0..1 canvas the curve's own box fills
                for (int i = 0; i < n; i++)
                    poly[i] = new Vector2(0.5f + (poly[i].x - cx) / span * fill,
                                          0.5f + (poly[i].y - cy) / span * fill);

                icon.Shape(p =>
                {
                    bool inside = false;
                    for (int i = 0, j = n - 1; i < n; j = i++)
                    {
                        Vector2 a = poly[i], b = poly[j];
                        if ((a.y > p.y) != (b.y > p.y)
                            && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x)
                            inside = !inside;
                    }
                    return inside;
                });
            }},
            new SymbolSpec { id = "Cube_Clover", name = "clover", draw = icon =>
            {
                // A four-petal rose - r = cos(2*theta), rotated 45 degrees so the petals fall where
                // the old diagonal 2x2 discs did - instead of four circles and a hub disc filling the
                // gap between them. The rose passes through its own centre on all four petals at
                // once, so it needs no separate hub: the seam the old version had at every join
                // between a leaf-disc and the hub is simply not a feature of this curve.
                const float radius = 0.40f;
                Vector2 centre = new Vector2(0.5f, 0.58f);
                icon.Shape(p =>
                {
                    Vector2 d = p - centre;
                    float r = d.magnitude;
                    if (r > radius) return false;
                    float theta = Mathf.Atan2(d.y, d.x) - Mathf.PI / 4f;
                    return r <= radius * Mathf.Abs(Mathf.Cos(2f * theta));
                });
                icon.Bar(new Vector2(0.5f, 0.155f), new Vector2(0.036f, 0.125f));
            }},
            new SymbolSpec { id = "Cube_Diamond", name = "diamond", draw = icon =>
                icon.Shape(pt => Mathf.Abs(pt.x - 0.5f) / 0.34f + Mathf.Abs(pt.y - 0.5f) / 0.44f <= 1f)
            },
            new SymbolSpec { id = "Cube_Star", name = "star", draw = icon =>
            {
                // Five points, and the EDGES HAVE TO BE STRAIGHT. Lerping the radius between the tip
                // and the valley across each sector is the obvious version and it draws a FLOWER -
                // the valleys come out round and the points blunt, because a star is not linear in
                // angle. What it is instead is a straight segment from tip to valley, so the test is
                // which side of that segment the sample falls on.
                const float sector = Mathf.PI * 2f / 5f;
                const float outer = 0.47f, inner = 0.20f;
                Vector2 tip = new Vector2(0f, outer);
                Vector2 valley = new Vector2(inner * Mathf.Sin(sector * 0.5f), inner * Mathf.Cos(sector * 0.5f));
                Vector2 edge = valley - tip;
                // Which side of that edge the centre is on. Everything inside the star is on it too.
                float centreSide = edge.x * -tip.y - edge.y * -tip.x;
                icon.Shape(pt =>
                {
                    Vector2 d = pt - new Vector2(0.5f, 0.5f);
                    float r = d.magnitude;
                    if (r > outer) return false;
                    if (r < 0.0001f) return true;
                    // Folded into one half-sector, so one segment describes all ten edges.
                    float angle = Mathf.Atan2(d.x, d.y);
                    float t = Mathf.Abs(Mathf.Repeat(angle + sector * 0.5f, sector) - sector * 0.5f);
                    Vector2 f = new Vector2(r * Mathf.Sin(t), r * Mathf.Cos(t));
                    float side = edge.x * (f.y - tip.y) - edge.y * (f.x - tip.x);
                    return side * centreSide > 0f;
                });
            }},
            new SymbolSpec { id = "Cube_Moon", name = "moon", draw = icon =>
            {
                icon.Disc(new Vector2(0.44f, 0.5f), 0.40f);
                // sign -1 cuts the bite out, which is the whole shape.
                icon.Disc(new Vector2(0.62f, 0.56f), 0.355f, -1f);
            }},
        };

        // Room2East, behind the blue door: six cubes on the floor, six recesses in the walls and the
        // floor, and a blue sphere on a plinth when every cube is home.
        //
        // THREE ON THE FLOOR, ONE WEST, TWO EAST, by request (2026-08-13) - the room used to put two
        // on every wall that had no doorway. West keeps one recess on its own mid-line, which a
        // doorway can never reach (a doorway is always cut into a Z wall); east keeps two, spaced
        // clear of both its doorways; the remaining three sit on the floor, clear of the walls, the
        // doorways and the plinth in the room's own middle. The recesses find the player's hand
        // themselves, the way every CarryableItem already does - this room is built before the player
        // is, and reordering the build to hand one in would put the player's construction behind a
        // room that does not need it.
        //
        // THE CUBES ARE GLASS, in a silver frame, with the symbol suspended INSIDE - all by request.
        // Glass rather than the wall/floor plates' own opaque material because the ask was specifically
        // for the cube, not the recess it goes home to: the recess stays the flat matte ink the room's
        // "no legend on the wall" rule already asks of it, and the cube is the one thing in the room
        // that gets to look precious. Every cube shares ONE glass tint - see MakeGlassMaterial - for
        // the same reason the plates share one ink colour: SYMBOLS, NOT COLOURS is the room's whole
        // design rule, and a cube tinted by its own answer would undo that as surely as a lit plate did.
        private static (CarryableItem[] cubes, CubeRoom room) BuildCubeRoom(
            Transform parent, Vector3 roomCentre, Material propMat)
        {
            const float cubeSize = 1.0f;    // 3/4 again, by request - everything else follows it
            const float slotY = 2.5f;       // a wide frame at the old chest-height centre clips the floor
            const float alongWall = 3.3f;   // clear of the frame beside it and of the doorway past it

            GameObject root = new GameObject("CubeRoom");
            root.transform.SetParent(parent, false);

            // The reward first, because the scatter has to keep clear of it.
            (Transform plinth, Transform seat, CarryableItem sphere) = BuildKeyPlinth(
                parent, "SpherePlinth", roomCentre, propMat,
                SlotShape.Round, new Color(0.16f, 0.40f, 0.95f), "KeySphereMat",
                SphereKeyItemId, "SPHERE", SphereIcon());

            RewardPlinth rise = plinth.gameObject.AddComponent<RewardPlinth>();
            rise.plinth = plinth;
            rise.key = sphere;
            rise.keySeat = seat;
            rise.riseHeight = 1.25f;

            // pos/rot/floor for all six recesses. A floor mount's rotation (-90 about X) is the same
            // one the chess board's own reward plinth was verified against: it carries local +Z onto
            // world +Y, so "the seat sits cubeSize*0.30 out along local +Z" - the wall recesses' own
            // rule, below - means exactly "up off the floor" here without a second formula.
            float halfX = RoomWidth / 2f;
            var mounts = new (Vector3 pos, Quaternion rot, bool floor)[]
            {
                (new Vector3(roomCentre.x - halfX, slotY, roomCentre.z), Quaternion.Euler(0f, 90f, 0f), false),
                (new Vector3(roomCentre.x + halfX, slotY, roomCentre.z - alongWall), Quaternion.Euler(0f, -90f, 0f), false),
                (new Vector3(roomCentre.x + halfX, slotY, roomCentre.z + alongWall), Quaternion.Euler(0f, -90f, 0f), false),
                (new Vector3(roomCentre.x - 2.2f, 0f, roomCentre.z - 3.0f), Quaternion.Euler(-90f, 0f, 0f), true),
                (new Vector3(roomCentre.x + 2.4f, 0f, roomCentre.z + 1.8f), Quaternion.Euler(-90f, 0f, 0f), true),
                (new Vector3(roomCentre.x - 2.2f, 0f, roomCentre.z + 3.0f), Quaternion.Euler(-90f, 0f, 0f), true),
            };

            SymbolSpec[] symbols = CubeSymbols();
            var cubes = new CarryableItem[symbols.Length];
            var slots = new SymbolSlot[symbols.Length];

            // ONE body material for all six, built once, because the body is the same clear glass on
            // every cube now - what differs is the core inside it. It used to be six materials that
            // differed only by the glyph printed on their faces.
            Material glassMat = MakeGlassMaterial("GlassBody", null,
                                                  new Color(0.92f, 0.96f, 1.00f, 0.28f), 0.97f);

            // Deterministic, like the chess scatter and for the same reason: a scene is build output,
            // so two builds of one commit have to lay the room out identically.
            System.Random rng = new System.Random(CubeScatterSeed);

            // FOUR FIXED FLOOR PLACES, JITTERED - not the chess room's rejection sampler, which this
            // room used until 2026-08-13 and which put the cubes in a huddle. The sampler is right
            // for a room whose only obstacle is a board in the middle; this one has a plinth in its
            // centre, THREE of its six recesses on the floor, and a doorway at each end, and every
            // one of those is a keep-out the sampler has to satisfy at once. What is left is a thin
            // ring, and 400 attempts at "far enough from everything" inside a thin ring either fails
            // into the fallback - which returns a point that satisfies NOTHING - or lands them all in
            // whichever lobe of it the seed happened to like.
            //
            // Written out, the room is laid out on purpose and cannot huddle. The rng still decides
            // the jitter and every cube's yaw, so it does not read as a grid, and the build stays
            // reproducible. Offsets are from the room's own centre: X is the short axis (8.75), Z the
            // long one (10.5), the plinth is at 0,0 and the floor recesses at (-2.2,-3.0), (2.4,1.8)
            // and (-2.2,3.0) - every number below is placed against those.
            var places = new[]
            {
                new Vector2( 2.75f, -1.35f),   // east, opposite the two west recesses
                new Vector2( 1.85f, -3.45f),   // south-east, clear of the entrance's swing
                new Vector2(-2.70f,  0.40f),   // west, between the two west recesses
                new Vector2( 0.10f,  3.45f),   // north, past the far side of the plinth
            };
            // WHICH CUBE STANDS WHERE, and how high. Index into `places`, and a level: 1 means this
            // cube is stacked on the one before it, which by construction names the same place. Two
            // towers of two rather than one of three - a metre cube three high is taller than the
            // player and reads as scenery to walk round rather than a thing to take apart. Placed at
            // opposite ends of the room so the shape is legible from wherever you come in.
            var stand = new (int place, int level)[]
            {
                (0, 0), (0, 1),   // tower, east
                (1, 0),
                (2, 0), (2, 1),   // tower, west
                (3, 0),
            };

            // One jitter per PLACE, not per cube: a stacked cube has to sit on the one below it, and
            // it can only do that if both were told the same place to be.
            var floorSpot = new Vector2[places.Length];
            for (int p = 0; p < places.Length; p++)
                floorSpot[p] = places[p] + new Vector2(((float)rng.NextDouble() * 2f - 1f) * 0.22f,
                                                       ((float)rng.NextDouble() * 2f - 1f) * 0.22f);

            // One yaw per PLACE as well, and a stacked cube takes its own small offset off it rather
            // than a fresh angle: a cube balanced across the corners of the one below it would be a
            // physics claim this project cannot make good on, and a few degrees out reads as stacked
            // by hand where a clean 0 reads as a shop display.
            var baseYaw = new float[places.Length];
            for (int p = 0; p < places.Length; p++) baseYaw[p] = (float)rng.NextDouble() * 360f;

            for (int i = 0; i < symbols.Length; i++)
            {
                SymbolSpec spec = symbols[i];

                // A thin ring behind the glyph on every WORLD-facing copy - the plate and the cube -
                // so each reads as a struck medallion rather than bare ink. Left off the HUD glyph
                // below: that one is 58px in a fixed-width row, where a second stroke competing with
                // the symbol is clutter rather than polish.
                System.Action<IconCanvas> withRing = icon =>
                {
                    icon.Ring(new Vector2(0.5f, 0.5f), 0.475f, 0.430f);
                    spec.draw(icon);
                };

                var paper = new IconCanvas(128);
                withRing(paper);
                Material plateMat = MakeSymbolMaterial("Symbol_" + spec.name,
                                                       SaveSymbolTexture(paper, "symbol_" + spec.name));

                // The same drawing again, INVERTED, for the core suspended inside the glass. The wall
                // plate is ink on paper because it is a printed sign; the core is read through a
                // tinted body in a white room, where a pale face is a white card inside a white cube
                // against a white wall. Its own texture rather than the plate's, so "the sign is
                // dark-on-light and the object is light-on-dark" is a fact about this room's look
                // rather than something a shared file forces on both.
                var core = new IconCanvas(128);
                withRing(core);
                Material coreMat = MakeSymbolMaterial("Inclusion_" + spec.name,
                    SaveSymbolTexture(core, "symbol_core_" + spec.name,
                                      new Color(0.06f, 0.06f, 0.08f), new Color(0.93f, 0.95f, 1.00f)));

                var glyph = new IconCanvas(128);
                spec.draw(glyph);
                Sprite hudIcon = SaveSprite(glyph, "icon_symbol_" + spec.name);

                (int place, int level) = stand[i];
                slots[i] = BuildSymbolSlot(root.transform, spec, plateMat, mounts[i].pos, mounts[i].rot,
                                           cubeSize, mounts[i].floor);
                cubes[i] = BuildSymbolCube(root.transform, spec, glassMat, coreMat, hudIcon, cubeSize,
                    new Vector3(roomCentre.x + floorSpot[place].x, 0f, roomCentre.z + floorSpot[place].y),
                    baseYaw[place] + (level == 0 ? 0f : ((float)rng.NextDouble() * 2f - 1f) * 13f),
                    level);

                // Take the bottom of a tower and the top of it falls. `stand` puts a stacked cube
                // directly after the one it stands on, at the same place, so the support is always
                // the cube built one step earlier - there is no lookup to get wrong.
                if (level > 0) StandOn(cubes[i], cubes[i - 1]);

                slots[i].acceptedItemId = spec.id;
            }

            CubeRoom room = root.AddComponent<CubeRoom>();
            room.cubes = cubes;
            room.slots = slots;
            room.reward = rise;
            foreach (SymbolSlot slot in slots) slot.room = room;

            // The closest pair is the one number that says whether the room reads as scattered or as
            // a huddle, and it is worth printing rather than eyeballing: measured in XZ only, so a
            // tower does not report itself as two cubes on top of each other.
            float closest = float.MaxValue;
            for (int a = 0; a < cubes.Length; a++)
                for (int b = a + 1; b < cubes.Length; b++)
                {
                    Vector3 d = cubes[a].transform.position - cubes[b].transform.position;
                    float flat = new Vector2(d.x, d.z).magnitude;
                    if (flat > 0.01f) closest = Mathf.Min(closest, flat);
                }

            int stacked = 0;
            foreach (var s in stand) if (s.level > 0) stacked++;

            Debug.Log($"[SceneBuilder] Cube room: {cubes.Length} cubes ({stacked} stacked), "
                    + $"{slots.Length} recesses, closest pair {closest:F2}m apart (cube is {cubeSize}m), "
                    + $"plinth at {plinth.position}");
            return (cubes, room);
        }

        // One recess: a symbol plate in a dark frame, standing proud of its mount, with a seat in
        // front of it for the cube. Local +Z is the recess's own outward normal - into the room for a
        // wall mount, straight up for a floor mount (its rotation carries local +Z onto world +Y) - so
        // the frame, plate and seat are built once here and never need to know which kind they are.
        // Only the reach trigger differs between the two, because "stand in front of it" and "stand
        // over it" are different shapes.
        private static SymbolSlot BuildSymbolSlot(Transform parent, SymbolSpec spec, Material faceMat,
                                                  Vector3 position, Quaternion rotation, float cubeSize,
                                                  bool floorMounted)
        {
            GameObject root = new GameObject("Slot_" + spec.name);
            root.transform.SetParent(parent, false);
            root.transform.position = position;
            root.transform.localRotation = rotation;

            // A REAL HOLLOW, not a dark rectangle pretending to be one. It used to be a flat frame
            // with the glyph panel laid on top of it, on the reasoning that there is no CSG here so a
            // cavity has to be near-black inside a lighter border - true, but only ever convincing
            // head-on. A raised RIM makes the same hole out of geometry: four bars standing proud of
            // the wall with the glyph panel at the bottom of the well between them, so the depth is
            // something the light and the viewing angle agree about instead of a trick of tone.
            //
            // The wall cannot be cut into, which is what decides the direction. Everything at local
            // -Z is behind an opaque wall and simply invisible, so the well is built OUT of the
            // surface. `lip` is therefore both how far the fixture protrudes and how deep the recess
            // reads - shallow, because a deep box at head height on a corridor wall stops being a
            // recess and becomes a shelf.
            float mouth = cubeSize + 0.10f;    // the opening: 5cm of clearance round the cube
            const float lip = 0.16f;           // how far the rim stands proud = how deep it reads
            const float rimBand = 0.20f;       // the width of the rim itself
            float frameSize = mouth + 2f * rimBand;

            Material rimMat = MakeColorMaterial("SymbolSlotFrame", new Color(0.10f, 0.10f, 0.12f));

            // Four bars, mitred the way BuildCubeEdging mitres its own and for the same reason: bars
            // cut to one length overlap at the corners with coplanar faces and flicker. The
            // horizontals run the full width and take the corners; the verticals stop against them.
            var rim = new Renderer[4];
            float band = (mouth + rimBand) / 2f;
            rim[0] = Prim(PrimitiveType.Cube, "RimTop", root.transform,
                new Vector3(0f, band, lip / 2f), new Vector3(frameSize, rimBand, lip),
                rimMat, removeCollider: true).GetComponent<Renderer>();
            rim[1] = Prim(PrimitiveType.Cube, "RimBottom", root.transform,
                new Vector3(0f, -band, lip / 2f), new Vector3(frameSize, rimBand, lip),
                rimMat, removeCollider: true).GetComponent<Renderer>();
            rim[2] = Prim(PrimitiveType.Cube, "RimLeft", root.transform,
                new Vector3(-band, 0f, lip / 2f), new Vector3(rimBand, mouth, lip),
                rimMat, removeCollider: true).GetComponent<Renderer>();
            rim[3] = Prim(PrimitiveType.Cube, "RimRight", root.transform,
                new Vector3(band, 0f, lip / 2f), new Vector3(rimBand, mouth, lip),
                rimMat, removeCollider: true).GetComponent<Renderer>();

            // The floor of the well, carrying the glyph. A cube rather than a quad: every face of a
            // Unity cube takes the whole texture, so the one facing the room shows the glyph the
            // right way round with nothing to configure. Sized to the mouth, so the well has no
            // visible seam between its walls and its back.
            Prim(PrimitiveType.Cube, "Plate", root.transform,
                new Vector3(0f, 0f, 0.015f), new Vector3(mouth, mouth, 0.03f), faceMat,
                removeCollider: true);

            // Where the cube ends up. Deep enough that it is INSIDE the well rather than resting on
            // its lip - a third of the cube is past the rim, and the back of it is inside the wall,
            // which is the one thing this project can do that a real hollow cannot: the wall is the
            // occluder, so the cube looks buried rather than clipped. It also covers its own symbol
            // once it is home, which is the correct thing for it to do - that pairing has been
            // answered by the time anything is placed.
            GameObject seat = new GameObject("Seat");
            seat.transform.SetParent(root.transform, false);
            seat.transform.localPosition = new Vector3(0f, 0f, cubeSize * 0.30f);

            // A wall recess is stood IN FRONT OF; a floor recess is stood OVER. The box below is
            // sized for whichever this is - wide on the two axes now horizontal, tall enough on the
            // third to catch a standing player regardless of exactly how close they walk up.
            BoxCollider reach = root.AddComponent<BoxCollider>();
            reach.isTrigger = true;
            if (floorMounted)
            {
                reach.center = new Vector3(0f, 0f, cubeSize * 0.6f);
                reach.size = new Vector3(3.4f, 3.4f, 3.0f);
            }
            else
            {
                reach.center = new Vector3(0f, -1.5f, cubeSize * 0.6f);
                reach.size = new Vector3(3.4f, 3.5f, 3.0f);
            }

            SymbolSlot slot = root.AddComponent<SymbolSlot>();
            slot.seat = seat.transform;
            slot.plateRenderers = rim;
            slot.audioSource = MakeSource(root.transform, "SlotAudio", 1f, 0.8f);
            slot.insertClip = LoadClip(SfxDir, "sfx_floor_button_press");

            // Presented clear of the mouth before it slides home: the cube is 1m and sits with its
            // centre cubeSize*0.30 inside the seat, so it has to start far enough out that no part of
            // it begins already through the rim. The tilt is small and deliberately not symmetrical -
            // a cube that straightens up as it goes in reads as placed by a hand, where one that
            // arrives square reads as a machine feeding it.
            slot.insertOffer = new Vector3(0f, 0f, cubeSize * 0.55f + lip);
            slot.insertTilt = new Vector3(-7f, 13f, 4f);
            slot.insertDuration = 0.45f;
            return slot;
        }

        // One cube, where the room was disturbed - on the floor, or on another cube. Everything here
        // is the chess piece over again - trigger, CarryableItem, hand pose - except that what
        // identifies it is suspended inside it, and its body is glass in a steel frame rather than
        // the recess's opaque ink (see BuildCubeRoom).
        //
        // `floorY` stays half a cube whatever `level` says, and that is right rather than an
        // oversight: a stack is how the room was FOUND, not a property of the cube. Take the top one
        // and put it down and it belongs on the floor; only the loop's own ReturnToOrigin rebuilds
        // the tower, which is exactly the rewind it is there to do.
        private static CarryableItem BuildSymbolCube(Transform parent, SymbolSpec spec, Material glassMat,
                                                     Material coreMat, Sprite icon, float cubeSize,
                                                     Vector3 spot, float yaw, int level)
        {
            // World metres of reach past a face, NOT a multiple of the cube: the reach an arm has is
            // the same whatever size the thing is, so both numbers below are divided back out by
            // cubeSize rather than written as a ratio that silently changes meaning every time the
            // cube is resized. Taller than it is wide because the trigger has to overlap a STANDING
            // player's capsule, whose bounds start well above a knee-high cube's lid.
            const float reachOut = 0.45f;
            const float reachUp = 0.60f;
            // World thickness of one edge of the silver frame, same reasoning.
            const float edge = 0.055f;

            // `level` 0 stands on the floor, 1 stands on the cube below it. Stacked by a WHOLE EDGE
            // THICKNESS more than the cube's own height, so the upper cube's bottom bars come to rest
            // ON the lower cube's top bars: each bar straddles its edge, so stacking by exactly
            // cubeSize would put the two frames through each other with coplanar faces - the same
            // flicker BuildCubeEdging mitres its own corners to avoid - and the two glass faces in
            // the same plane on top of it.
            GameObject cube = Prim(PrimitiveType.Cube, spec.id, parent,
                new Vector3(spot.x, cubeSize / 2f + level * (cubeSize + edge), spot.z),
                Vector3.one * cubeSize, glassMat, removeCollider: true);
            // Turned where it fell, about its own vertical only: a cube tipped onto a corner would
            // read as physics that has run, and nothing here simulates.
            cube.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);

            BuildCubeEdging(cube.transform, edge / cubeSize);

            // THE SYMBOL, SUSPENDED INSIDE THE GLASS rather than printed on its faces - by request,
            // 2026-08-13. A solid core rather than the glyph alone floating in the middle: the glyph
            // alone means an alpha-clipped shell whose near and far faces are both visible through
            // each other, which reads as two symbols rather than one seen in depth. Opaque, so the
            // near face hides the far one and there is exactly one mark to read from any direction -
            // including from ABOVE, which a flat wafer could not answer and which matters in a room
            // whose cubes are on the floor and stacked. Opaque also settles the sort order for free:
            // it draws in the opaque queue with depth, and the glass blends over it afterwards.
            Prim(PrimitiveType.Cube, "Core", cube.transform, Vector3.zero, Vector3.one * 0.58f,
                 coreMat, removeCollider: true);

            BoxCollider trigger = cube.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = new Vector3(1f + 2f * reachOut / cubeSize,
                                       1f + 2f * reachUp / cubeSize,
                                       1f + 2f * reachOut / cubeSize);

            // SOLID, so a metre-plus of glass is walked around rather than through. On a CHILD, and
            // that is load-bearing rather than tidiness: CarryableItem takes `GetComponent<Collider>()`
            // as its reach trigger, so a solid box added to this same object would be a coin toss over
            // which of the two becomes the reach - the trap BuildKeyPlinth documents. Handed to the
            // item as `blocker` so every path that puts the cube in a hand can switch it off; see
            // CarryableItem.blocker for why holding a solid box would be a bug in two directions.
            //
            // Sized to the glass itself, not to the frame that stands proud of it by half an edge:
            // the frame is a detail you can put a shoulder through without noticing, and matching the
            // collider to the silhouette a player reads as "the cube" is what makes it feel solid.
            GameObject solid = new GameObject("Blocker");
            solid.transform.SetParent(cube.transform, false);
            BoxCollider block = solid.AddComponent<BoxCollider>();

            CarryableItem item = cube.AddComponent<CarryableItem>();
            item.blocker = block;
            item.itemId = spec.id;
            item.displayName = spec.name.ToUpperInvariant();
            item.icon = icon;
            item.floorY = cubeSize / 2f;
            item.handLocalPosition = HandPoseFor(cubeSize);
            // Off-axis on two axes, so what is in the hand is seen as a CUBE - square-on it is a
            // square, which is one of the six symbols and would read as a mistake.
            item.handLocalEuler = new Vector3(-20f, 28f, 0f);
            // ITS OWN SIZE. It used to be held at 0.20 - the size the cube was before it grew - so a
            // metre of glass would not fill the screen. It is a metre of glass, and it is carried
            // like one now: further out, lower, and genuinely in the way. That is the trade, taken
            // deliberately (2026-08-13). HandPoseFor is where it is tuned.
            item.handLocalScale = Vector3.one * cubeSize;
            return item;
        }

        // `upper` rests on `lower` and drops to the floor when it stops doing so. The values live
        // here and the mechanism lives in FallingItem, like every other pairing in this file.
        //
        // The landing borrows the floor pads' clunk, pitched well down. It is the only impact in the
        // library and a metre of glass hitting a floor in silence is worse than a borrowed sound -
        // see docs/audio.md on the pads themselves.
        private static void StandOn(CarryableItem upper, CarryableItem lower)
        {
            FallingItem stack = AddFalling(upper);
            stack.support = lower;
        }

        // WHERE A HELD OBJECT SITS, from how big it is. Held objects are their TRUE SIZE now - the
        // shrink-to-fit that every carryable used to carry is gone - so a big one has to be held
        // further out and lower, the way a person carries a box rather than a key.
        //
        // Grows SUB-LINEARLY with size on purpose. Scaling the distance in step with the object
        // would put everything at the same angular size, which is exactly what the old shrink did by
        // another route: a metre of glass would look like the 20cm prop it used to be. These
        // coefficients keep a small object at roughly the pose it always had and push a large one out
        // far enough to be seen at all, while still reading as large.
        //
        // `size` is the object's world size across, and the caller knows it because it built it.
        private static Vector3 HandPoseFor(float size)
        {
            return new Vector3(0.28f + size * 0.20f,
                              -(0.22f + size * 0.40f),
                               0.45f + size * 0.55f);
        }

        // Everything that can be carried can be DROPPED, and a dropped object falls. One component
        // per carryable, added here so a new carryable anywhere in the building gets it without its
        // author having to know - the same reason the reflection-probe sweep walks the scene rather
        // than trusting every room to opt in.
        //
        // Idempotent: the cube room's towers wire their own support before this runs, and get it
        // back rather than a second copy.
        private static FallingItem AddFalling(CarryableItem item)
        {
            FallingItem fall = item.GetComponent<FallingItem>();
            if (fall != null) return fall;

            fall = item.gameObject.AddComponent<FallingItem>();
            fall.item = item;
            // Its own clip, and it had to be: the landing borrowed the floor pads' clunk pitched
            // down, and that clip is a struck C6 left to ring for a second, so a dropped object
            // announced itself like a doorbell. Pitching a pitched sound down does not stop it being
            // pitched. `sfx_item_drop` is 85ms with its energy under 500Hz - see Tools/generate_sfx.
            fall.audioSource = MakeSource(item.transform, "FallAudio", 1f, 0.85f);
            // A fixed offset PER OBJECT rather than a random one per landing: two cubes off one
            // tower must not land in unison, and the build has to be reproducible. Derived from the
            // name, so an object's own thud is the same weight every iteration - which is a thing
            // the player can learn, where a per-landing shuffle is just noise.
            fall.audioSource.pitch = 0.94f + (Mathf.Abs(item.name.GetHashCode()) % 13) * 0.01f;
            fall.landClip = LoadClip(SfxDir, "sfx_item_drop");
            return fall;
        }

        // TELLS EVERY CARRYABLE UNDER `root` WHICH STOREY IT IS ON. One call per cycle subtree,
        // rather than a line in every builder that makes a carryable.
        //
        // `CarryableItem.floorY` is the object's own half-thickness above ITS OWN floor, and that
        // number is genuinely the object's business. Which floor is not - it is a property of where
        // the thing was placed, so it is set here, in one sweep, at the point the storey is already
        // known. Asking each builder to remember to add it is asking for exactly one of them to
        // forget, and the failure mode is silent: the object falls through the floor to the height of
        // the storey above and hangs there.
        //
        // The ground storey needs no call at all - `floorBaseY` defaults to zero, which is why every
        // value in this file could be written as a bare half-height for as long as there was one
        // level, and why none of them had to change when there stopped being.
        private static int SetFloorBase(Transform root, float floorY)
        {
            if (root == null) return 0;

            CarryableItem[] items = root.GetComponentsInChildren<CarryableItem>(includeInactive: true);
            foreach (CarryableItem item in items) item.floorBaseY = floorY;

            Debug.Log($"[SceneBuilder] Floor base {floorY:0.###} set on {items.Length} carryables under {root.name}");
            return items.Length;
        }

        private static int AddFallingToEveryCarryable()
        {
            int added = 0;
            CarryableItem[] items = UnityEngine.Object.FindObjectsByType<CarryableItem>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            foreach (CarryableItem item in items)
                if (item.GetComponent<FallingItem>() == null) { AddFalling(item); added++; }

            Debug.Log($"[SceneBuilder] Falling: {items.Length} carryables, {added} newly wired");
            return added;
        }

        // The silver frame: the twelve edges of the cube, in polished steel, on a glass body. Glass
        // alone has no silhouette of its own in a white room - it is only what it reflects, so from
        // the wrong angle a clear cube is a faint smudge on a white floor and the symbol on it looks
        // like it is floating. The frame is what draws the SHAPE, and it does it in the one material
        // this project can already light convincingly (see MakePolishedMetalMaterial and SS3 on why a
        // metal here is entirely its reflections).
        //
        // `t` is the edge thickness in the cube's own LOCAL units, so a caller expressing it in metres
        // divides by the cube's size once and the frame stays in proportion at any size - including in
        // the hand, where the whole object is rescaled to 0.20.
        //
        // The bars are MITRED rather than all cut to one length, and the reason is z-fighting rather
        // than fussiness: twelve equal bars overlap inside each corner with coplanar side faces, which
        // flicker. So one axis takes the corners outright - X, run the full width and half an edge
        // proud at each end - and the other eight stop exactly where it starts. Faces then touch
        // without ever overlapping.
        private static void BuildCubeEdging(Transform cube, float t)
        {
            Material steel = MakePolishedMetalMaterial(
                "CubeEdgeSteel", new Color(0.88f, 0.89f, 0.93f), 0f);

            // Every bar sits astride its edge - half of it inside the glass, half proud - so the frame
            // catches a highlight along its length instead of lying flush and vanishing into the face
            // it is drawn on.
            var lengths = new[]
            {
                new Vector3(1f + t, t, t),
                new Vector3(t, 1f - t, t),
                new Vector3(t, t, 1f - t),
            };

            for (int axis = 0; axis < 3; axis++)
            {
                for (int corner = 0; corner < 4; corner++)
                {
                    float a = (corner & 1) == 0 ? -0.5f : 0.5f;
                    float b = (corner & 2) == 0 ? -0.5f : 0.5f;
                    Vector3 pos = axis == 0 ? new Vector3(0f, a, b)
                                : axis == 1 ? new Vector3(a, 0f, b)
                                            : new Vector3(a, b, 0f);

                    Prim(PrimitiveType.Cube, $"Edge{axis}{corner}", cube, pos, lengths[axis], steel,
                         removeCollider: true);
                }
            }
        }

        // A filled disc and a filled square, for the two escape objects whose shape IS their name.
        // Deliberately plain: the row they appear in already says which is which by tint, and an
        // outline at 58px closes up under the filter.
        private static Sprite SphereIcon()
        {
            var icon = new IconCanvas(128);
            icon.Disc(new Vector2(0.5f, 0.5f), 0.34f);
            return SaveSprite(icon, "icon_key_sphere");
        }

        private static Sprite SquareIcon()
        {
            var icon = new IconCanvas(128);
            icon.Bar(new Vector2(0.5f, 0.5f), new Vector2(0.30f, 0.30f));
            return SaveSprite(icon, "icon_key_square");
        }

        // The yellow triangle in silhouette. A plain solid triangle rather than an outline: the row
        // it appears in is 58px, the other two glyphs there are solid, and an outline at that size is
        // two thin lines that close up under the bilinear filter.
        private static Sprite TriangleIcon()
        {
            var icon = new IconCanvas(128);
            icon.Shape(p =>
            {
                if (p.y < 0.22f || p.y > 0.80f) return false;
                // 0 at the apex, 1 at the base - so the half-width opens out on the way down.
                float k = (0.80f - p.y) / 0.58f;
                return Mathf.Abs(p.x - 0.5f) <= 0.40f * k;
            });
            return SaveSprite(icon, "icon_key_triangle");
        }

        // The nightstand and the drawer in it, built here rather than imported.
        //
        // It used to be nightstand.glb with a generated drawer bolted to its front face, because the
        // model bakes its whole body into one mesh and has no drawer node to pull. That read exactly
        // as what it was: a pale slab stuck on a dark cabinet, sliding out to reveal the two drawer
        // fronts the model already had painted on it. There is no recess to import, so the carcass
        // has to be built to have one - and the same numbers then cut the opening and fill it, which
        // is the whole reason the two halves are one method.
        //
        // The model's lamp and pot are rebuilt from primitives here; they were the reason it was
        // chosen and the room would be barer without them. Geometry only, no Light: the shadow atlas
        // is sized for exactly the four fixtures that cast, and a fifth would silently halve them.
        //
        // The footprint reproduces the model's measured bounds - min (-1.23, 0, 1.16), max
        // (-0.67, 0.59, 1.52) - so nothing else in the room has to move.
        // pinItemId: the id the three pins share. A parameter rather than a constant because a second
        // cycle gets its own nightstand, and `ItemRegistry` maps an id to ONE socket with the last
        // writer winning - two cycles' pins under one id would be a single six-deep supply that
        // either cycle's ghosts could draw from.
        private static (Drawer, CarryableItem[]) BuildNightstand(Transform parent, string pinItemId = ToolItemId)
        {
            // Footprint centre, on the floor. The front face looks down the room, away from the
            // pillow, which is the side the player is on when they turn round from the bed.
            Vector3 centre = new Vector3(-0.95f, 0f, 1.35f);
            const float w = 0.56f, d = 0.36f, h = 0.59f;
            const float panel = 0.018f;     // carcass stock
            const float legH = 0.075f;      // floor to the underside of the case
            const float topT = 0.032f;      // the slab the lamp stands on

            float caseBottom = legH;
            float caseTop = h - topT;
            const float bayH = 0.17f;              // the drawer opening
            float bayTop = caseTop;
            float bayBottom = bayTop - bayH;

            Material wood = MakeColorMaterial("NightstandWood", new Color(0.14f, 0.085f, 0.06f));
            SetSmoothness(wood, 0.25f);
            // Brass, as the model's pulls were. Not metallic: this project cannot light a pure metal
            // (see docs/gotchas.md), so it is a warm colour with some gloss instead.
            Material brass = MakeColorMaterial("DrawerHandle", new Color(0.72f, 0.55f, 0.25f));
            SetSmoothness(brass, 0.55f);

            GameObject unit = new GameObject("Nightstand");
            unit.transform.SetParent(parent, false);
            // LOCAL, not world. It was `position`, which was the same thing for as long as the only
            // nightstand was in a room whose parent sat at the origin - and silently wrong the moment
            // one is built under a parent carrying a storey offset, where it would land on the floor
            // above.
            unit.transform.localPosition = centre;

            // Carcass: five boards with the front left OFF. That absence is the recess - there is
            // nothing else to build, and it is what the imported mesh could not give.
            Prim(PrimitiveType.Cube, "Top", unit.transform, new Vector3(0f, h - topT / 2f, 0f),
                new Vector3(w + 0.03f, topT, d + 0.02f), wood);
            Prim(PrimitiveType.Cube, "Bottom", unit.transform, new Vector3(0f, caseBottom + panel / 2f, 0f),
                new Vector3(w, panel, d), wood);
            Prim(PrimitiveType.Cube, "Back", unit.transform, new Vector3(0f, (caseBottom + caseTop) / 2f, d / 2f - panel / 2f),
                new Vector3(w, caseTop - caseBottom, panel), wood);
            Prim(PrimitiveType.Cube, "SideLeft", unit.transform, new Vector3(-w / 2f + panel / 2f, (caseBottom + caseTop) / 2f, 0f),
                new Vector3(panel, caseTop - caseBottom, d), wood);
            Prim(PrimitiveType.Cube, "SideRight", unit.transform, new Vector3(w / 2f - panel / 2f, (caseBottom + caseTop) / 2f, 0f),
                new Vector3(panel, caseTop - caseBottom, d), wood);
            // Separates the drawer bay above from an open shelf below. A second drawer front would
            // have matched the old model better and lied: only one drawer opens.
            Prim(PrimitiveType.Cube, "Divider", unit.transform, new Vector3(0f, bayBottom - panel / 2f, 0f),
                new Vector3(w - panel * 2f, panel, d - panel), wood);

            for (int i = 0; i < 4; i++)
            {
                float lx = (i % 2 == 0 ? -1f : 1f) * (w / 2f - 0.035f);
                float lz = (i < 2 ? -1f : 1f) * (d / 2f - 0.035f);
                Prim(PrimitiveType.Cube, $"Leg{i}", unit.transform, new Vector3(lx, legH / 2f, lz),
                    new Vector3(0.042f, legH, 0.042f), wood);
            }

            BuildNightstandLamp(unit.transform, new Vector3(-0.13f, h, 0.03f));
            BuildNightstandCube(unit.transform, new Vector3(0.16f, h, -0.05f));

            // The drawer's own root sits at the CENTRE OF ITS FRONT PANEL, not at the unit origin,
            // because Drawer.HintAnchor is drawerBody - anchored at the floor the E prompt would
            // float at the player's feet.
            float frontZ = -d / 2f + panel / 2f;
            float frontY = (bayBottom + bayTop) / 2f;

            GameObject root = new GameObject("NightstandDrawer");
            root.transform.SetParent(parent, false);
            root.transform.position = centre + new Vector3(0f, frontY, frontZ);

            GameObject bodyGO = new GameObject("DrawerBody");
            bodyGO.transform.SetParent(root.transform, false);

            // Inset into the opening rather than laid over it, by 5mm all round: the gap is what
            // shows there is a hole here even with the drawer shut.
            const float trayD = 0.30f;
            float frontW = w - panel * 2f - 0.01f;
            float frontH = bayH - 0.01f;

            // The tray is LINED, in pale grey, and the wood stops at the front panel. This is the one
            // place the unit is not the colour it wants to be: the pin is a dark handle with a bright
            // 6mm needle on it, and against wood the handle disappears - the first version of this
            // carcass lost the tool completely, because the tray it inherited had been white. A liner
            // is what a drawer has anyway, and it puts the contrast back where it is load-bearing.
            Material liner = MakeColorMaterial("DrawerLiner", new Color(0.60f, 0.58f, 0.55f));
            SetSmoothness(liner, 0.15f);

            Prim(PrimitiveType.Cube, "Front", bodyGO.transform, Vector3.zero,
                new Vector3(frontW, frontH, panel), wood, removeCollider: true);
            Prim(PrimitiveType.Cube, "TrayBase", bodyGO.transform, new Vector3(0f, -frontH / 2f + 0.008f, trayD / 2f),
                new Vector3(frontW - 0.02f, 0.016f, trayD), liner, removeCollider: true);
            Prim(PrimitiveType.Cube, "TrayLeft", bodyGO.transform, new Vector3(-frontW / 2f + 0.008f, 0f, trayD / 2f),
                new Vector3(0.016f, frontH * 0.8f, trayD), liner, removeCollider: true);
            Prim(PrimitiveType.Cube, "TrayRight", bodyGO.transform, new Vector3(frontW / 2f - 0.008f, 0f, trayD / 2f),
                new Vector3(0.016f, frontH * 0.8f, trayD), liner, removeCollider: true);
            Prim(PrimitiveType.Cube, "TrayBack", bodyGO.transform, new Vector3(0f, 0f, trayD),
                new Vector3(frontW - 0.02f, frontH * 0.8f, 0.016f), liner, removeCollider: true);
            Prim(PrimitiveType.Cube, "Pull", bodyGO.transform, new Vector3(0f, 0f, -panel / 2f - 0.012f),
                new Vector3(frontW * 0.42f, 0.016f, 0.024f), brass, removeCollider: true);

            BoxCollider trigger = root.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = new Vector3(w + 0.9f, 1.4f, 1.4f);

            Drawer drawerComp = root.AddComponent<Drawer>();
            drawerComp.drawerBody = bodyGO.transform;
            drawerComp.audioSource = MakeSource(root.transform, "DrawerAudio", 1f, 0.8f);
            drawerComp.openClip = LoadClip(SfxDir, "sfx_drawer_open");
            // Two thirds out, not all the way. Clearing the carcass entirely was right when the
            // drawer was a slab on a solid face and had nowhere to be; now that there is an opening
            // to sit in, leaving a third of the tray inside is what reads as a drawer rather than a
            // tray hanging in mid-air. It still puts the tool well clear of the front.
            drawerComp.openLocalOffset = new Vector3(0f, 0f, -trayD * 0.68f);

            // THREE pins, not one, laid out across the tray.
            //
            // Popping requires the pin in hand for ghosts as for the player, and with one pin in the
            // world that capped the entire room at ONE popper at any moment - so Room2's
            // accumulation, which is the thing every iteration is supposed to add to, did not survive
            // its own rule. The mitigation was always a supply rather than a softer rule
            // (docs/decisions.md), and ItemRegistry now resolves an id to a free instance so several
            // objects can share one.
            //
            // Three buys the player and TWO past selves popping at once. It is not a cap on ghosts,
            // only on simultaneous poppers: a third ghost reaching for a fourth pin finds none, and
            // its errand simply does not happen. Raising the number is now one edit here.
            CarryableItem[] pins = new CarryableItem[3];
            float pinSpacing = (frontW - 0.09f) / 2f;
            for (int i = 0; i < pins.Length; i++)
                pins[i] = BuildPin(bodyGO.transform, i,
                    new Vector3((i - 1) * pinSpacing, -frontH / 2f + 0.032f, trayD * 0.38f), drawerComp, pinItemId);

            return (drawerComp, pins);
        }

        // One pin: a slim needle on a dark handle. Parented to the drawer body, so it rides out with
        // the drawer instead of hanging in the air in front of a shut one, and sat forward in the
        // tray so an open drawer presents it.
        //
        // All three share ToolItemId, which is the point - a recorded "took the Tool" has to be
        // satisfiable by whichever one is going spare. They differ only in name, so the console can
        // say which object a ghost picked up.
        private static CarryableItem BuildPin(Transform drawerBody, int index, Vector3 localPos, Drawer drawer, string itemId)
        {
            GameObject toolRoot = new GameObject(index == 0 ? "BalloonTool" : $"BalloonTool_{index}");
            toolRoot.transform.SetParent(drawerBody, false);
            toolRoot.transform.localPosition = localPos;

            Material handleMat = MakeColorMaterial("ToolHandle", new Color(0.16f, 0.16f, 0.18f));
            Material pinMat = MakeColorMaterial("ToolPin", new Color(0.78f, 0.79f, 0.82f));
            SetSmoothness(pinMat, 0.7f);

            Prim(PrimitiveType.Cylinder, "Handle", toolRoot.transform, new Vector3(0f, 0f, -0.045f),
                new Vector3(0.028f, 0.045f, 0.028f), handleMat, removeCollider: true)
                .transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            Prim(PrimitiveType.Cylinder, "Pin", toolRoot.transform, new Vector3(0f, 0f, 0.05f),
                new Vector3(0.006f, 0.05f, 0.006f), pinMat, removeCollider: true)
                .transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            BoxCollider toolTrigger = toolRoot.AddComponent<BoxCollider>();
            toolTrigger.isTrigger = true;
            toolTrigger.size = new Vector3(0.7f, 0.7f, 0.7f);

            CarryableItem toolItem = toolRoot.AddComponent<CarryableItem>();
            toolItem.itemId = itemId;
            toolItem.displayName = "PIN";
            toolItem.icon = PinIcon();
            // Nearer and higher than CarryableItem's default, which put almost none of the pin on
            // screen: at fov 60 a hold 0.42m out has 0.242m of half-height, so the default y of
            // -0.24 sat the tool's CENTRE on the bottom edge of the frame and cropped the handle
            // into the corner. 0.32m out puts half-height at 0.185m, so -0.125 is 68% down - the
            // whole tool inside the frame with room under it, and bigger for being closer.
            //
            // x is 0.14 rather than the 0.175 that looked right in the Editor, which runs a 2.03
            // ultrawide viewport: checked again at 16:9 the handle came within 5% of the right edge,
            // and 16:9 is the narrower case the build has to survive.
            //
            // The ROTATION is deliberately the tuned default, unchanged. It is what the swing arc
            // was built around - BalloonTool pitches about the item's local X from this rest pose, so
            // yawing it turns the chop into a sideways slash. Yawed versions also read worse, not
            // better: past about 25 degrees the handle turns broadside and becomes a black slab.
            toolItem.handLocalPosition = new Vector3(0.14f, -0.125f, 0.32f);
            toolItem.requiresOpenDrawer = drawer;
            toolItem.audioSource = MakeSource(toolRoot.transform, "PickupAudio", 1f, 0.8f);
            toolItem.pickupClip = LoadClip(SfxDir, "sfx_item_pickup");

            return toolItem;
        }

        // The drum-shade lamp the imported nightstand carried, rebuilt from three cylinders. It gives
        // off no light - see BuildNightstand for why - so the shade is a pale matte solid rather than
        // anything emissive: a lamp that glowed without lighting the room would read as broken.
        private static void BuildNightstandLamp(Transform unit, Vector3 baseLocal)
        {
            Material metal = MakeColorMaterial("LampStem", new Color(0.12f, 0.09f, 0.07f));
            SetSmoothness(metal, 0.45f);
            Material shade = MakeColorMaterial("LampShade", new Color(0.87f, 0.85f, 0.79f));
            SetSmoothness(shade, 0.08f);

            GameObject lamp = new GameObject("Lamp");
            lamp.transform.SetParent(unit, false);
            lamp.transform.localPosition = baseLocal;

            // Cylinder primitives are 1 unit across and TWO tall, so the Y scale is a half-height.
            Prim(PrimitiveType.Cylinder, "Foot", lamp.transform, new Vector3(0f, 0.011f, 0f),
                new Vector3(0.092f, 0.011f, 0.092f), metal);
            Prim(PrimitiveType.Cylinder, "Stem", lamp.transform, new Vector3(0f, 0.10f, 0f),
                new Vector3(0.034f, 0.078f, 0.034f), metal, removeCollider: true);
            Prim(PrimitiveType.Cylinder, "Shade", lamp.transform, new Vector3(0f, 0.245f, 0f),
                new Vector3(0.20f, 0.072f, 0.20f), shade);
        }

        // The little Rubik's cube beside the lamp, swapped in for a potted plant 2026-08-13 - the
        // point is still just that the top of the nightstand is not bare, not anything about which
        // prop it is.
        //
        // `baseLocal` is where the pot's own root used to sit - `unit`'s local space, Y already at
        // the nightstand's top surface - so PlaceModel is handed unit.position + baseLocal as its
        // WORLD target rather than being reparented under a second empty the way the pot's two
        // primitives were. addBoxCollider false: a solid decorative object nobody can reach behind
        // the lamp does not need one, and it is one fewer collider on a footprint this small.
        private static void BuildNightstandCube(Transform unit, Vector3 baseLocal)
        {
            Vector3 target = unit.position + baseLocal;

            // Measured off the imported model: its renderer bounds are 0.13m across at the prefab's
            // own default root scale of 1.15, so 0.113m per unit of PlaceModel's uniformScale here.
            // 0.8 lands just under 0.09m - small enough to read as a desk toy next to the lamp
            // rather than a prop competing with it. Rotation matches the prefab's own saved root
            // rotation (270, 0, 0) - PlaceModel overwrites rotation to identity unless told
            // otherwise, and identity here stood the cube on a corner.
            PlaceModel($"{FurnitureDir}/rubiks_cube.glb", unit, "Cube",
                new Vector3(target.x, 0f, target.z), target.y, 0.8f,
                addBoxCollider: false, rotation: Quaternion.Euler(-90f, 0f, 0f));
        }

        // One key shape, used three times over: lying on the floor once its balloon bursts, seen
        // through the skin of the balloon that holds it, and mounted on Room2's lock so the thing
        // on the wall says what it wants. Laid out in the XY plane facing -Z.
        private const string GoldKeyPath = FurnitureDir + "/gold_key.glb";

        // Measured off the glb at scale 1, logged on every build so a re-export that moves them is
        // caught rather than discovered: the key runs 8.00 along Y with the BOW at +Y and the bit
        // at -Y, and its shaft turns about the line x=0, z=-0.03.
        private const float KeyModelLength = 8.0f;
        private const float KeyModelTipY = -7.493f;
        // Where the bow stops being a ring and becomes shaft. This is the waterline: everything
        // below it goes into the lock, everything above it stays out where the player can see it.
        private const float KeyModelBowJunctionY = -2.3f;
        private const float KeyModelShaftAxisZ = -0.03f;

        // 0.20m overall, which is what the five primitives it replaces measured. Not a coincidence
        // and not a free choice: Room2's puzzle is spotting the key THROUGH a balloon from across
        // the room, and that was play-tested at this size. A prop key rather than a real 6cm one.
        private const float KeyLength = 0.20f;
        private const float KeyScale = KeyLength / KeyModelLength;
        // Distance from the bow junction to the tip - i.e. how much key there is to push in.
        private const float KeyInsertTravel = (KeyModelBowJunctionY - KeyModelTipY) * KeyScale;

        // How the key hangs when it is NOT in the lock: bow up, bit down, turned off-axis. Only the
        // resting pose - CarryableItem.InsertInto zeroes the root's rotation, which is exactly the
        // frame the insert-and-turn animation wants, and ReturnToOrigin puts this back.
        private static readonly Quaternion KeyRestRotation = Quaternion.Euler(90f, 35f, 0f);

        // The gold key model, laid into its parent so that +Z is "into the lock" and a roll about
        // local Z is "turn the key". Everything downstream - the socket, the insertion, the turn -
        // depends on this frame, so it is established once here rather than at each call site.
        private static GameObject BuildKeyModel(Transform parent, float scaleMul, Material overrideMat,
                                                bool centreOnParent = false)
        {
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(GoldKeyPath);
            if (source == null)
            {
                Debug.LogError($"[SceneBuilder] Key model missing at {GoldKeyPath}");
                return null;
            }

            GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(source, parent);
            model.name = "KeyModel";

            float scale = KeyScale * scaleMul;
            model.transform.localScale = Vector3.one * scale;

            // Two rotations, composed in this order and NOT written as one Euler: Unity evaluates
            // Euler as Y*X*Z, so a single triple would apply the roll before the lay-down and put
            // the bit back where it started.
            //   -90 about X  lays the key down, bow toward -Z (out at the player), bit into +Z.
            //   +90 about Z  rolls it so the flat of the bit stands VERTICAL, matching the
            //                keyhole's vertical slot. A key entering a slot sideways is the wrong
            //                key, and the slot is the one part of the lock that says which way up.
            model.transform.localRotation = Quaternion.AngleAxis(90f, Vector3.forward)
                                          * Quaternion.AngleAxis(-90f, Vector3.right);

            // Put the shaft's own axis on the parent's Z axis. Without this the turn is a wobble
            // about a line 0.75mm off the shaft - small, but it is the difference between a key
            // turning and a key being waggled.
            Vector3 axisPoint = new Vector3(0f, 0f, KeyModelShaftAxisZ) * scale;
            model.transform.localPosition = -(model.transform.localRotation * axisPoint);

            // The origin left by the line above is the BOW, which is what the lock wants - it is the
            // point that seats on the plate face, and CarryableItem.InsertInto drops the root exactly
            // there. It is wrong for the balloon: the key hangs off the origin by 0.09 and ends up
            // shouldered against one side of a 0.24-radius sphere instead of suspended in it, which
            // reads as a key stuck to the skin rather than one floating inside. Measured and
            // subtracted rather than hardcoded, so it survives the key being rescaled.
            if (centreOnParent)
            {
                Renderer[] centred = model.GetComponentsInChildren<Renderer>();
                Bounds local = new Bounds(parent.InverseTransformPoint(centred[0].bounds.center), Vector3.zero);
                foreach (Renderer r in centred)
                {
                    local.Encapsulate(parent.InverseTransformPoint(r.bounds.min));
                    local.Encapsulate(parent.InverseTransformPoint(r.bounds.max));
                }
                model.transform.localPosition -= local.center;
            }

            if (overrideMat != null)
                foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true))
                {
                    Material[] mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++) mats[i] = overrideMat;
                    r.sharedMaterials = mats;
                }

            // Logged because every constant above is a measurement off this file, and a re-export
            // that moves them should show up in the build log rather than as a key sticking out of a
            // wall. (The nightstand used to be read the same way and no longer needs to be - it is
            // built from authored numbers now, not measured off a mesh.)
            if (scaleMul >= 1f)
            {
                Renderer[] rs = model.GetComponentsInChildren<Renderer>();
                Bounds b = rs[0].bounds;
                for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
                // Longest axis, not Z: the loose key's root carries KeyRestRotation, so which world
                // axis the length lands on depends on where this was called from.
                float longest = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
                Debug.Log($"[SceneBuilder] Key model size={b.size} longest={longest:0.000} (expects {KeyLength:0.000})");
            }

            return model;
        }

        // A CLONE of the glb's own Gold, because glb materials are sub-assets regenerated on every
        // reimport and anything written to them is lost - the same trap DisableBakedOcclusion works
        // around for the bed. `tint` scales the base colour: 1 for the loose key, dark for the copy
        // inside a balloon, which is read through a pink translucent skin and washes out at gold.
        //
        // THE ONE VALUE CHANGED IS metallicFactor, 1.0 -> 0.3, and it is not a style preference.
        // A fully metallic surface has NO diffuse term: every photon it shows is a reflection of
        // its surroundings. Reflections here come from a baked probe, and whatever this room's
        // probes are handing a metal it is not the white box they were baked in - the key rendered
        // BLACK with a thin gold rim (the rim being the only direct specular), floating in the
        // middle of a bright white room. Verified it was the metalness and not the lighting: with
        // occlusionTexture_strength forced to 0 nothing changed, and at 0.3 the key came back gold.
        //
        // This departs from "keep the glTF materials these ship with". That rule exists to stop a
        // hand-rolled URP/Lit stand-in dropping a model's metallic/roughness MAPS - and this model
        // has no textures at all, only factors, so there is nothing to drop. It is also the more
        // robust setting for this room: a key with a diffuse term reads as gold wherever it is put,
        // where a mirror only reads as gold where the probe happens to be right.
        private static Material KeyMaterial(string assetName, float tint) =>
            KeyMaterial(assetName, Color.white * tint);

        // tint is now a COLOUR rather than a brightness, which is what lets a red and a blue key be the
        // same object as the gold one. It is still applied by SCALING the glb's own baseColorFactor
        // rather than by assigning a chosen triple - so the model keeps its shading and the result stays
        // correct whichever colour space the glTF shader reads that property in. A red channel above 1
        // is deliberate and is why: scaling a dark gold by 1.0 would give a dark red, and the key has to
        // stay bright enough to be spotted through a balloon from across the room.
        private static Material KeyMaterial(string assetName, Color tint)
        {
            Material gold = null;
            foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(GoldKeyPath))
                if (o is Material m && !m.name.StartsWith("__preview__")) { gold = m; break; }

            if (gold == null)
            {
                Debug.LogWarning($"[SceneBuilder] gold_key.glb has no material - {assetName} left flat");
                return MakeColorMaterial(assetName, new Color(0.85f, 0.68f, 0.24f) * tint);
            }

            string path = $"{MaterialsDir}/{assetName}.mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(gold);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = gold.shader;
            mat.CopyPropertiesFromMaterial(gold);
            mat.shaderKeywords = gold.shaderKeywords;

            mat.SetFloat("metallicFactor", 0.3f);
            const string baseColor = "baseColorFactor";
            if (tint != Color.white && mat.HasProperty(baseColor))
                mat.SetColor(baseColor, gold.GetColor(baseColor) * tint);

            EditorUtility.SetDirty(mat);
            return mat;
        }

        // The hole a key goes into: a round seat for the shaft with a narrow slot under it.
        // Two flat decals sitting on the plate face rather than a cut - the plate is a 0.09m cube
        // and boring a hole through it would show the wall behind.
        private static void BuildKeyholeSlot(Transform parent, Vector3 centre, Material mat)
        {
            GameObject seat = Prim(PrimitiveType.Cylinder, "KeyholeSeat", parent, centre,
                new Vector3(0.055f, 0.002f, 0.055f), mat, removeCollider: true);
            seat.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            Prim(PrimitiveType.Cube, "KeyholeSlot", parent,
                new Vector3(centre.x, centre.y - 0.037f, centre.z),
                new Vector3(0.022f, 0.055f, 0.004f), mat, removeCollider: true);
        }

        // Room2's balloons, pooled rather than spawned. The pool is what makes a balloon's id mean
        // the same thing in every iteration, which is what a ghost's recorded pops refer to.
        private static (BalloonField, CarryableItem[]) BuildBalloons(Transform parent)
        {
            const int balloonCount = 70;
            const int fieldSeed = 20260810;

            GameObject root = new GameObject("BalloonField");
            root.transform.SetParent(parent, false);

            // Translucent, so whatever is inside a balloon shows through it as a shape. That is
            // the whole point: the key balloon is now findable by looking rather than by bursting
            // seventy of them and hoping, which is what made the search luck before.
            Material pink = MakeTranslucentMaterial("BalloonPink", new Color(0.98f, 0.44f, 0.68f, 0.62f), 0.72f);
            Material knotMat = MakeColorMaterial("BalloonKnot", new Color(0.76f, 0.28f, 0.5f));

            // Bouncy and slippery. Combine on Maximum so a balloon still bounces off the floor and
            // the walls, which are plain matte colliders with no bounce of their own.
            PhysicsMaterial rubber = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>($"{MaterialsDir}/BalloonRubber.physicMaterial");
            if (rubber == null)
            {
                rubber = new PhysicsMaterial("BalloonRubber");
                AssetDatabase.CreateAsset(rubber, $"{MaterialsDir}/BalloonRubber.physicMaterial");
            }
            rubber.bounciness = 0.55f;
            rubber.dynamicFriction = 0.28f;
            rubber.staticFriction = 0.28f;
            rubber.bounceCombine = PhysicsMaterialCombine.Maximum;
            rubber.frictionCombine = PhysicsMaterialCombine.Minimum;
            EditorUtility.SetDirty(rubber);

            // THREE keys, in three balloons, from the same fixed seed as the spawn points - so each key
            // is in the same balloon in every run, which is what makes "I know where the blue one is"
            // worth having. Distinct by construction rather than by luck: drawing three times from 70
            // collides about one run in twenty-four, and a collision would silently put two keys in one
            // balloon and leave a door with no key anywhere.
            var keyIndices = new System.Collections.Generic.List<int>();
            System.Random keyRng = new System.Random(fieldSeed);
            while (keyIndices.Count < Room2Keys.Length)
            {
                int candidate = keyRng.Next(balloonCount);
                if (!keyIndices.Contains(candidate)) keyIndices.Add(candidate);
            }

            System.Random pitchRng = new System.Random(fieldSeed + 1);

            // Their own layer, so the player's controller can exclude them outright and there is
            // never any contact to be lifted by. Set on the root only - the collider lives there,
            // and the children are visuals.
            int balloonLayer = EnsureLayer(BalloonLayerName);

            Balloon[] balloons = new Balloon[balloonCount];
            for (int i = 0; i < balloonCount; i++)
            {
                GameObject go = new GameObject($"Balloon_{i}");
                go.transform.SetParent(root.transform, false);
                go.layer = balloonLayer;

                Prim(PrimitiveType.Sphere, "Body", go.transform, Vector3.zero,
                    new Vector3(0.48f, 0.58f, 0.48f), pink, removeCollider: true);
                Prim(PrimitiveType.Cube, "Knot", go.transform, new Vector3(0f, -0.3f, 0f),
                    new Vector3(0.06f, 0.07f, 0.06f), knotMat, removeCollider: true);

                SphereCollider col = go.AddComponent<SphereCollider>();
                col.radius = 0.27f;
                col.sharedMaterial = rubber;

                Rigidbody rb = go.AddComponent<Rigidbody>();
                rb.mass = 0.04f;
                // Gravity off: Balloon.FixedUpdate applies its own much gentler fall instead. Unity
                // has no per-body gravity scale, and the alternative - damping a full 9.81 down to
                // a drift - takes so much damping that the balloons stop bouncing and start
                // behaving like they are underwater. Low damping plus a small pull is light AND
                // lively; heavy damping is only slow.
                rb.useGravity = false;
                rb.linearDamping = 1.1f;
                rb.angularDamping = 1.2f;
                // Parked until the field releases them. Built live, seventy spheres would start the
                // scene interpenetrating at the origin - which is in Room1, beside the bed.
                rb.isKinematic = true;
                col.enabled = false;

                Balloon balloon = go.AddComponent<Balloon>();
                balloon.id = i;

                // The key inside, visible through the skin of whichever balloon has one. A child of the
                // balloon, so Balloon.SetInPlay hides and shows it along with everything else and it
                // vanishes the moment the balloon bursts. Hung off its own child so it keeps the bow-up
                // resting pose: BuildKeyModel lays the key along +Z for the lock, and a key floating
                // horizontally inside a balloon reads as debris.
                //
                // In the KEY'S OWN COLOUR, which is what makes the search a search for a particular
                // key rather than for "a key". The balloon's own reference is wired below, once the
                // key objects exist.
                int keySlot = keyIndices.IndexOf(i);
                if (keySlot >= 0)
                {
                    GameObject keyVisual = new GameObject("KeyVisual");
                    keyVisual.transform.SetParent(go.transform, false);
                    keyVisual.transform.localRotation = KeyRestRotation;
                    BuildKeyModel(keyVisual.transform, 0.85f,
                                  KeyMaterial(Room2Keys[keySlot].materialName + "InBalloon",
                                              Room2Keys[keySlot].metal * 0.36f),
                                  centreOnParent: true);
                }
                balloon.audioSource = MakeSource(go.transform, "PopAudio", 1f, 0.8f);
                // A fixed detune per balloon, from the field's own seed. One clip across seventy
                // balloons reads as a machine gun; a balloon keeping the same voice every
                // iteration is one more thing about the room that stays put.
                balloon.audioSource.pitch = 0.86f + (float)pitchRng.NextDouble() * 0.3f;
                balloon.popClip = LoadClip(SfxDir, "sfx_balloon_pop");

                balloons[i] = balloon;
            }

            // THE THREE KEYS. Each is the same object as the one key that was here before - same model,
            // same scale, same hand pose, same seated-orientation-is-identity trick - differing only by
            // its KeySpec. Built in a loop precisely so they cannot drift apart from each other.
            //
            // Their origins are spread along the room's mid-line rather than stacked, because origin is
            // where the loop puts a key back at the top of every iteration: three keys sharing one
            // origin would be three keys inside each other, and `ReturnAllToOrigin` would do it every
            // sixty seconds. Pocketed rather than held on pickup, so taking one does not knock the pin
            // out of the hand that got it.
            var keyItems = new CarryableItem[Room2Keys.Length];
            for (int k = 0; k < Room2Keys.Length; k++)
            {
                KeySpec spec = Room2Keys[k];

                GameObject keyRoot = new GameObject(spec.materialName);
                keyRoot.transform.SetParent(root.transform, false);
                keyRoot.transform.position = new Vector3((k - 1) * 0.5f, 0.06f, RoomPitch);
                // The resting pose lives on the ROOT, not on the model, and that is load-bearing:
                // InsertInto zeroes the root's rotation, which is precisely the frame KeyLock's
                // insert-and-turn works in, and ReturnToOrigin restores this at the top of the loop.
                keyRoot.transform.localRotation = KeyRestRotation;

                BuildKeyModel(keyRoot.transform, 1f, KeyMaterial(spec.materialName, spec.metal));

                BoxCollider keyTrigger = keyRoot.AddComponent<BoxCollider>();
                keyTrigger.isTrigger = true;
                keyTrigger.size = new Vector3(0.9f, 0.9f, 0.9f);

                CarryableItem keyItem = keyRoot.AddComponent<CarryableItem>();
                keyItem.itemId = spec.itemId;
                keyItem.displayName = "KEY";
                keyItem.icon = KeyIcon();
                // One silhouette for all three, tinted. Three drawn shapes would be three things to
                // learn where the colour is already the whole message.
                keyItem.iconTint = spec.display;
                // In the hand the key points the way it goes into a lock - teeth forward, bit vertical
                // - because the root's identity rotation IS the seated orientation (see BuildKeyModel).
                // Held nose-first means walking up to the lock and pressing E needs no mental rotation.
                // Angled up slightly and further from the eye than the pin, which is a short stub where
                // this is 0.20m long and would otherwise cross the middle of the screen.
                keyItem.handLocalPosition = HandPoseFor(KeyScale);
                keyItem.handLocalEuler = new Vector3(-12f, -14f, 0f);
                // LIES FLAT WHEN PUT DOWN. The root's built pose is KeyRestRotation - bow up,
                // standing on its blade - which is the pose of a key left somewhere to be FOUND, not
                // of one that has been dropped. A key stood on end where it fell reads as placed on
                // purpose. This is the same quarter turn BalloonField gives a key coming out of a
                // burst balloon, now stated once on the object instead of at each place that puts
                // one down.
                keyItem.restRoll = 90f;
                keyItem.audioSource = MakeSource(keyRoot.transform, "PickupAudio", 1f, 0.9f);
                keyItem.pickupClip = LoadClip(SfxDir, "sfx_item_pickup");

                keyItems[k] = keyItem;
                // The balloon holding this key learns which one it is. Done here rather than in the
                // balloon loop above because the key objects do not exist yet at that point.
                balloons[keyIndices[k]].heldKey = keyItem;
            }

            BalloonField field = root.AddComponent<BalloonField>();
            field.balloons = balloons;
            field.seed = fieldSeed;
            field.roomCenterZ = RoomPitch;

            // Park the pool now rather than waiting for the first iteration to do it. Built objects
            // sit at their parent's origin, which for these is the middle of Room1.
            field.ComputeSpawnPoints();
            field.ResetField();

            return (field, keyItems);
        }

        private static FloorButton BuildFloorButton(Transform parent, Material mat, string name, Vector3 position)
        {
            // Root at unit scale so the activation volume is defined in clean world units;
            // the flattened cylinder mesh lives on an unscaled-collider visual child instead.
            GameObject root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = position;

            // Unity's cylinder is 1 unit across, so the X/Z scale is the diameter.
            const float padRadius = 0.35f;
            GameObject visual = Prim(PrimitiveType.Cylinder, "Visual", root.transform, Vector3.zero,
                new Vector3(padRadius * 2f, 0.03f, padRadius * 2f), mat, removeCollider: true);

            // No collider: FloorButton is a logical volume tested against the player's centre, not
            // a physics trigger. Its radius is derived from the visible pad here so the hit area
            // and the thing you can see can't drift apart - a hair proud of the edge, no more.
            FloorButton fb = root.AddComponent<FloorButton>();
            fb.activationRadius = padRadius + 0.05f;
            fb.buttonRenderer = visual.GetComponent<Renderer>();
            return fb;
        }

        // The slab, its pocket, and the lamp above it - everything both doors have in common. What
        // differs is only what unlocks them: Room1's is a button wired to a floor pad, Room2's is a
        // key lock fed by whatever a balloon gave up.
        // yaw turns the whole door, which is how a door in a SIDE wall costs nothing: the slab, the
        // pocket offset, the lamp and the slide are all measured in this root's local frame, so
        // rotating the root puts every one of them on a different wall with no second copy of the
        // measurements. 0 is a north wall, 90 the east, -90 the west.
        //
        // wallHalfExtent is how far the wall is from the room centre along the door's own normal -
        // RoomDepth/2 for the end walls this was written for, RoomWidth/2 for a side wall. It used to
        // be hardcoded, which is the only thing that made this Z-only.
        private static (Door door, DoorIndicator indicator, float wallInnerZ) BuildDoorShell(
            Transform parent, string name, float roomCenterZ, Material mat,
            float wallHalfExtent = RoomDepth / 2f, float yaw = 0f)
        {
            GameObject doorRoot = new GameObject(name);
            doorRoot.transform.SetParent(parent, false);
            // The root carries the room offset, so every measurement below stays in the same local
            // frame it was tuned in back when there was only one door. The offset is in the PARENT's
            // frame and the rotation is the root's own, so the two do not interfere.
            doorRoot.transform.localPosition = new Vector3(0f, 0f, roomCenterZ);
            doorRoot.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);

            // The slab lives in the pocket between this room's wall and the next room's, so sliding
            // it sideways tucks it inside the wall build-up rather than dragging it across the
            // panelling. wallInnerZ is the room surface; the pocket starts WallDepth behind that.
            float wallInnerZ = wallHalfExtent;
            float slabZ = wallInnerZ + WallDepth + DoorPocketDepth / 2f;

            // The slab KEEPS its collider. The doorway is cut out of both walls' collision, so the
            // closed door is the only thing standing between the two rooms - built without one, the
            // player just walks through it and the puzzle is bypassable. Once open the slab sits
            // entirely behind the wall's own collision, so it never blocks the opening it cleared.
            GameObject panel = Prim(PrimitiveType.Cube, "DoorPanel", doorRoot.transform,
                new Vector3(0f, DoorHeight / 2f, slabZ),
                new Vector3(DoorWidth, DoorHeight, DoorThickness), mat);

            // A single lamp block split down the middle: red half on the left, green on the right,
            // only ever one of them lit. Both halves share one emissive material and are driven
            // apart by DoorIndicator through property blocks.
            Material lampMat = MakeEmissiveMaterial("IndicatorLamp", Color.white, 1f);
            const float lampWidth = 0.34f, lampHeight = 0.11f, lampDepth = 0.04f;
            float lampY = DoorHeight + 0.28f;
            // Proud of the wall surface by half its depth, so it sits on the panelling.
            float lampZ = wallInnerZ - lampDepth / 2f;

            GameObject lampRoot = new GameObject("DoorIndicator");
            lampRoot.transform.SetParent(doorRoot.transform, false);

            GameObject redHalf = Prim(PrimitiveType.Cube, "RedHalf", lampRoot.transform,
                new Vector3(-lampWidth / 4f, lampY, lampZ),
                new Vector3(lampWidth / 2f, lampHeight, lampDepth), lampMat, removeCollider: true);
            GameObject greenHalf = Prim(PrimitiveType.Cube, "GreenHalf", lampRoot.transform,
                new Vector3(lampWidth / 4f, lampY, lampZ),
                new Vector3(lampWidth / 2f, lampHeight, lampDepth), lampMat, removeCollider: true);

            Door door = doorRoot.AddComponent<Door>();
            // Slides right (+X as seen from inside the room) by its own width, so it clears the
            // opening exactly.
            door.openLocalOffset = new Vector3(DoorWidth, 0f, 0f);
            door.doorPanel = panel.transform;

            DoorIndicator indicator = lampRoot.AddComponent<DoorIndicator>();
            indicator.redHalf = redHalf.GetComponent<Renderer>();
            indicator.greenHalf = greenHalf.GetComponent<Renderer>();
            indicator.door = door;

            return (door, indicator, wallInnerZ);
        }

        // A powered door with no control of its own, held open while every pad handed to it is
        // held. Room1 passes one pad, Room3 two.
        //
        // There was a DoorButton on the wall beside Room1's until a play-test found nobody could
        // locate it - testers held the pad, walked to the door and expected it to open. See Door's
        // own note for why they were right and what removing the button buys. Note the wall beside
        // these doors is bare, which is part of the point: there is no affordance left to mislead.
        // yaw / wallHalfExtent pass straight through to `BuildDoorShell`, which has always taken them -
        // this only ever hid them. The ring needs both: its three corner doors sit in a WEST wall
        // (yaw 270) whose half-extent is `RoomWidth/2`, not the `RoomDepth/2` a north wall has.
        private static Door BuildPadDoor(Transform parent, string name, float roomCenterZ,
                                         FloorButton[] pads, Material mat,
                                         float yaw = 0f, float wallHalfExtent = RoomDepth / 2f)
        {
            (Door door, DoorIndicator indicator, float _) =
                BuildDoorShell(parent, name, roomCenterZ, mat, wallHalfExtent, yaw);

            // The lamp tracks the condition, not the door, so it goes green the moment the pads are
            // all held. With no button anywhere it is the room's only readout for that, which makes
            // it load-bearing rather than decorative - and in Room3 it is the only way to tell "one
            // of my past selves has arrived" from "both have".
            indicator.requiredFloorButtons = pads;
            door.requiredFloorButtons = pads;

            return door;
        }

        // A keyed door on the north wall of the room centred at roomCenterZ - no pad and no condition
        // to hold open, and deliberately not a button: the shape of the thing on the wall is the
        // puzzle telling you what it wants. Called three times now, once per coloured key, at three
        // different joins in the corridor - identical every time except the wall it ends up in and
        // the key it wants, which is the point: three doors that behave the same and differ only by
        // colour is what makes the colour readable as the rule.
        private static (Door, KeyLock) BuildKeyDoor(Transform parent, string name, float roomCenterZ, Material mat, KeySpec spec)
        {
            (Door door, DoorIndicator indicator, float wallInnerZ) =
                BuildDoorShell(parent, name, roomCenterZ, mat);
            return (door, AttachKeyLock(door, indicator, wallInnerZ, mat, spec));
        }

        // The lock beside a door, and the colour that says which key it wants.
        //
        // Factored out of BuildKeyDoor when Room2 grew from one keyed door to three. Every one of them
        // is the same fixture at a different colour, and the colour comes from the same KeySpec the key
        // itself is built from - so a lock cannot end up wanting a key that does not match the plate
        // beside it. That was the whole risk in this feature: the colours are the only instructions the
        // player gets, and two places deciding them independently is how they come apart.
        private static KeyLock AttachKeyLock(Door door, DoorIndicator indicator, float wallInnerZ,
                                             Material mat, KeySpec spec)
        {
            GameObject lockRoot = new GameObject("KeyLock");
            lockRoot.transform.SetParent(door.transform, false);
            lockRoot.transform.localPosition = new Vector3(-GridCellWidth, GridCellHeight * 1.5f, wallInnerZ - 0.06f);

            // Taller and narrower than the door button next door: across a room the two have to
            // read as different kinds of thing rather than as the same switch twice.
            //
            // IN THE KEY'S COLOUR, and this is the primary signal - it is the thing the player is
            // standing in front of with a key in hand. The band on the door says the same thing from
            // across the room; this says it at arm's length.
            Material plateMat = MakeColorMaterial(spec.materialName + "Plate", spec.display);
            SetSmoothness(plateMat, 0.45f);
            GameObject plate = Prim(PrimitiveType.Cube, "Visual", lockRoot.transform, Vector3.zero,
                new Vector3(0.2f, 0.3f, 0.09f), plateMat, removeCollider: true);

            // A plain keyhole, NOT the key silhouette this used to carry. The etching said what the
            // lock wanted, which was the right idea while nothing could ever be put in it - but the
            // key is now literally inserted here, and an inserted key sitting on top of an engraved
            // one reads as two keys. A keyhole says the same thing and leaves the socket empty.
            Material slotMat = MakeColorMaterial("KeySlot", new Color(0.06f, 0.06f, 0.07f));
            BuildKeyholeSlot(lockRoot.transform, new Vector3(0f, -0.03f, -0.047f), slotMat);

            // Where an accepted key ends up: SEATED, not parked on the surface. The key used to be
            // laid flat against the plate like a sticker, which is what "insert" meant when the
            // key was five primitives. It is a real key now and it goes IN.
            //
            // The plate is a 0.09-deep cube, so its face is at z -0.045. Putting the bow junction
            // exactly on that face leaves the bow standing proud where it can be seen and everything
            // from the shaft down inside the plate - and behind the plate is 0.015 of air and then
            // 0.125 of wall, so the 0.13 of shaft and bit has somewhere to be and is hidden the
            // whole way. Nothing is clipped and nothing reaches Room3 (the tip stops 0.10 short).
            const float plateFaceZ = -0.045f;
            float bowJunction = -KeyModelBowJunctionY * KeyScale;
            GameObject socket = new GameObject("KeySocket");
            socket.transform.SetParent(lockRoot.transform, false);
            // Y matches the keyhole seat below, so the shaft runs into the hole rather than past it.
            socket.transform.localPosition = new Vector3(0f, -0.03f, plateFaceZ - bowJunction);

            BoxCollider trigger = lockRoot.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = new Vector3(0.8f, 0.8f, 1.2f);

            KeyLock keyLock = lockRoot.AddComponent<KeyLock>();
            keyLock.door = door;
            keyLock.keySocket = socket.transform;
            // How far back the key is presented before it slides in - exactly the length of key
            // that ends up buried, so it starts with its tip at the hole rather than at a distance
            // someone picked.
            keyLock.insertTravel = KeyInsertTravel;
            keyLock.lockRenderer = plate.GetComponent<Renderer>();
            // THE ONE THING THAT MAKES A DOOR PICKY. Both the player's gate (hand.Holding) and a
            // ghost's hand-over (ItemRegistry.FindSocket, keyed on AcceptedItemId) read this, so the
            // matching is enforced in one place for living and dead alike.
            keyLock.requiredItemId = spec.itemId;
            // The lamp over this door reports "you are carrying the key" the way the other one
            // reports "the pad is held".
            if (indicator != null) indicator.keyLock = keyLock;

            // NO BAND ON THE DOOR. There was one, in the same colour, on the reasoning that the plate is
            // 0.2m wide and the far side of the room is where the player decides which key to look for.
            // It went because the plate turned out to be enough on its own and the band was not free: a
            // whole slab wearing a colour reads as a coloured DOOR, which invites the idea that the door
            // itself is the thing that changes, where the lock is what actually differs. One coloured
            // fixture beside three identical doors says that more precisely than three coloured doors.
            return keyLock;
        }

        private static (GameObject, FirstPersonController, PlayerRecorder, CameraShaker, PlayerHand) BuildPlayer(Transform spawn, GhostInteractable[] ghostInteractables)
        {
            GameObject player = new GameObject("Player");
            player.tag = "Player";
            player.transform.position = spawn.position;
            player.transform.rotation = spawn.rotation;

            CharacterController cc = player.AddComponent<CharacterController>();
            cc.height = 1.8f;
            cc.radius = 0.3f;
            cc.center = new Vector3(0f, 0.9f, 0f);

            // The controller does not collide with balloons at all. They are still solid to each
            // other, to the floor and to the walls - this excludes exactly one pair. Without it the
            // player can stand on one, and the moment a balloon under a standing player takes any
            // upward push at all, stepOffset walks them up onto it and they ride it to the ceiling.
            // FirstPersonController pushes them from an overlap query instead of by contact.
            int balloonLayer = EnsureLayer(BalloonLayerName);
            cc.excludeLayers = 1 << balloonLayer;

            // A rig sits between the player and the camera purely so CameraShaker has somewhere to
            // write. FirstPersonController rewrites the camera's own localPosition and euler angles
            // every frame, so a shake applied to the camera itself would be wiped instantly.
            GameObject rigGO = new GameObject("CameraRig");
            rigGO.transform.SetParent(player.transform, false);
            CameraShaker shaker = rigGO.AddComponent<CameraShaker>();

            GameObject camGO = new GameObject("PlayerCamera");
            camGO.transform.SetParent(rigGO.transform, false);
            camGO.transform.localPosition = new Vector3(0f, 1.6f, 0f);
            Camera cam = camGO.AddComponent<Camera>();
            camGO.AddComponent<AudioListener>();
            cam.tag = "MainCamera";

            // Unity's default near plane of 0.3 is the same as the controller's radius, and that
            // does not work: what pokes through a wall is the near plane's CORNER, not its centre,
            // and at fov 60 that corner reaches 0.463m (0.532m ultrawide) while the capsule stops
            // the camera 0.3m away - less once skinWidth lets the wall penetrate the capsule. The
            // whole wall build-up is only WallDepth (0.125m) thick, so the frustum clipped clean
            // through it: stand against a wall or the door, look sideways, and you saw the far side
            // of the room. At 0.05 the corner reaches 0.077m, comfortably inside the standoff.
            cam.nearClipPlane = 0.05f;
            // Pulled in from the default 1000 to keep depth precision concentrated where the scene
            // actually is (~22m across both rooms). The panels are 0.025m proud of their backing,
            // and SSAO resolves those grooves off the depth buffer.
            cam.farClipPlane = 100f;

            // Opt the camera into the volume stack - URP cameras ignore post-processing otherwise.
            UniversalAdditionalCameraData camData = camGO.AddComponent<UniversalAdditionalCameraData>();
            camData.renderPostProcessing = true;
            camData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;

            FirstPersonController fpc = player.AddComponent<FirstPersonController>();
            fpc.playerCamera = cam;
            fpc.pushLayers = 1 << balloonLayer;
            // 2D: these are the player's own feet, not something across the room. Fired off the same
            // step phase the head bob is drawn from, so the sound and the movement are one event.
            fpc.footstepSource = MakeSource(player.transform, "FootstepAudio", 0f, 1f);
            // Three, cycled by step number rather than picked at random - a random order re-uses the
            // same clip twice in a row often enough to be heard doing it.
            fpc.footstepClips = new[]
            {
                LoadClip(SfxDir, "sfx_footstep_1"),
                LoadClip(SfxDir, "sfx_footstep_2"),
                LoadClip(SfxDir, "sfx_footstep_3"),
            };

            PlayerRecorder recorder = player.AddComponent<PlayerRecorder>();
            recorder.interactables = ghostInteractables;

            // Parented under the camera rather than the player, so a carried item rides the view -
            // including through the wake-up, where WakeUpSequence poses the camera directly and
            // anything hung off the body would swing independently of where you are looking.
            GameObject handAnchor = new GameObject("HandAnchor");
            handAnchor.transform.SetParent(camGO.transform, false);

            PlayerHand hand = player.AddComponent<PlayerHand>();
            hand.holdAnchor = handAnchor.transform;
            // A put-down stops at a wall rather than going through it. Balloons excluded for the
            // same reason the controller excludes them - an object put down in Room2 would otherwise
            // be stopped short by whatever balloon happened to be drifting past.
            hand.dropBlockers = ~(1 << balloonLayer);

            // Stops a held object reaching through a wall the player is standing against. Balloons
            // are excluded from the cast for the same reason the controller excludes them: the item
            // would otherwise shy away from every balloon in Room2 as the player walked through them.
            HeldItemClearance clearance = handAnchor.AddComponent<HeldItemClearance>();
            clearance.anchor = handAnchor.transform;
            clearance.eye = camGO.transform;
            clearance.hand = hand;
            clearance.ignoreRoot = player.transform;
            clearance.blockers = ~(1 << balloonLayer);
            // Takes and surrenders go into the same timeline the frames do, so a past self can
            // repeat them. Only items flagged ghostCarryable are written.
            hand.recorder = recorder;

            BalloonTool tool = player.AddComponent<BalloonTool>();
            tool.playerCamera = cam;
            tool.hand = hand;
            tool.recorder = recorder;
            // 2D: this is the player's own arm, not something across the room. The clip slot is
            // left empty - there is no swing sound generated yet, and borrowing the bed's sheet
            // rustle for it would be worse than silence. BalloonTool guards on null.
            tool.audioSource = MakeSource(player.transform, "ToolAudio", 0f, 0.5f);

            return (player, fpc, recorder, shaker, hand);
        }

        private const string GhostModelPath = "Assets/ArtAssets/Smooth_Male_Casual@Walking.fbx";

        // The ghosts are real people now - a rigged, animated, opaque figure rather than the six
        // faint primitives that came before. That is a deliberate change of what a ghost IS: it
        // used to read as an afterimage of you, and it now reads as a person who was here. The
        // reference film shows past selves as people, so this is the faithful reading; the cost is
        // that six of them in a room is a crowd rather than a memory.
        //
        // The model is Quaternius' Smooth Male Casual (CC0), 7,932 triangles with twelve clips
        // baked in. Its own materials are already flat-colour URP/Lit with no textures, which is
        // exactly what this room wants - the walls carry the detail, the figures do not.
        // Transform.Find only walks one level and takes a path; a rig's bone can be at any depth
        // under any number of exporter-invented parents, so this searches by name instead.
        private static Transform FindDeep(Transform root, string name)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }

        // A place on a ghost's skeleton that a carried object can be parented to, in METRES.
        //
        // TWO OBJECTS, AND THE PAIR IS THE WHOLE POINT. This FBX has a `HumanArmature` node at scale
        // 100 inside a body scaled to 0.377, so every bone's lossyScale is 37.7: parent a 0.2m key
        // to one and you get a 7.5m key, flung metres away because its local offset is multiplied by
        // the same number. The OUTER node cancels that and must stay at localPosition zero - an
        // offset written there would be in the 37.7x space too, so 3cm arrives as 1.13m. The offset
        // therefore goes on the CHILD, where the scale is already 1 and centimetres mean centimetres.
        //
        // Factored out when the ghost grew a second one of these. Getting it wrong is not a subtle
        // failure - it is an object the size of a room - but it is an easy one to get wrong twice,
        // so the check is built in: the returned anchor's lossyScale is asserted at 1.
        private static Transform MakeGhostAnchor(Transform rigRoot, Transform fallback, string boneName,
                                                 string label, Vector3 localOffset)
        {
            Transform bone = FindDeep(rigRoot, boneName);
            if (bone == null)
                Debug.LogWarning($"[SceneBuilder] ghost rig has no {boneName} - {label} items will ride the root");

            GameObject scaleNode = new GameObject(label + "Scale");
            scaleNode.transform.SetParent(bone != null ? bone : fallback, false);
            scaleNode.transform.localPosition = Vector3.zero;

            Vector3 rigScale = scaleNode.transform.lossyScale;
            scaleNode.transform.localScale = new Vector3(
                Mathf.Approximately(rigScale.x, 0f) ? 1f : 1f / rigScale.x,
                Mathf.Approximately(rigScale.y, 0f) ? 1f : 1f / rigScale.y,
                Mathf.Approximately(rigScale.z, 0f) ? 1f : 1f / rigScale.z);

            GameObject anchor = new GameObject(label + "Anchor");
            anchor.transform.SetParent(scaleNode.transform, false);
            anchor.transform.localPosition = localOffset;

            Debug.Log($"[SceneBuilder] Ghost {label} anchor on {boneName}: rig scale {rigScale.x:0.###} -> "
                + $"{anchor.transform.lossyScale.x:0.###} (expects 1)");

            return anchor.transform;
        }

        private static GhostReplayer BuildGhostPrefab()
        {
            string prefabPath = $"{PrefabsDir}/Ghost.prefab";

            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(GhostModelPath);
            if (model == null)
            {
                Debug.LogError($"[SceneBuilder] Ghost model missing at {GhostModelPath}");
                return null;
            }

            GameObject ghost = new GameObject("Ghost");
            GameObject body = (GameObject)PrefabUtility.InstantiatePrefab(model);
            body.transform.SetParent(ghost.transform, false);
            body.name = "Body";

            // Measured, not guessed: the rig stands 4.739m at scale 1 (bone extents, verified in
            // the editor), so 0.377 puts it at 1.75m - a hair under the player's 1.8m controller
            // and right for the 1.6m eye height they are seen from.
            body.transform.localScale = Vector3.one * 0.377f;

            // Colliders would break the game outright: a ghost is something you walk THROUGH, and
            // one that pushed the player would change the recording being made against it.
            foreach (Collider c in body.GetComponentsInChildren<Collider>(true))
                Object.DestroyImmediate(c);

            SkinnedMeshRenderer skin = body.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (skin != null)
            {
                skin.sharedMaterials = GhostMaterials(skin.sharedMaterials);
                // Shadows are OFF again, and that reverses a call made when the ghosts were opaque
                // ("a person with no shadow reads as a bug"). It does not survive the figures
                // becoming afterimages: a hollow rim that throws a solid, fully detailed shadow on
                // the floor puts every piece of detail this shader exists to remove straight back
                // into the room, in the one place the eye is guaranteed to read it as real.
                skin.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                skin.receiveShadows = false;
                // The bounds Unity computes for a skinned mesh are the bind pose's, and this rig's
                // are wrong enough that ghosts vanish when their origin leaves the frustum.
                skin.updateWhenOffscreen = true;
            }

            Animator animator = body.GetComponent<Animator>();
            if (animator == null) animator = body.AddComponent<Animator>();
            animator.runtimeAnimatorController = GhostAnimatorController();
            animator.applyRootMotion = false;

            GhostReplayer replayer = ghost.AddComponent<GhostReplayer>();
            replayer.animator = animator;
            // A ghost pops only while holding the tool, the same test BalloonTool applies to the
            // player. Same constant on both sides so the two gates cannot drift apart.
            replayer.popToolItemId = ToolItemId;

            // Where the EQUIPPED item rides. Hung off the rig's right hand so it swings with the arm
            // through the walk cycle - parented to the ghost's root it would slide along beside the
            // figure, which reads as an object being dragged rather than carried.
            //
            // Metres, in the hand's own frame. Just past the wrist so the key sits in the fist
            // rather than inside it. This is the one place to tune a ghost's grip.
            replayer.carryAnchor = MakeGhostAnchor(body.transform, ghost.transform, "MiddleHand.R",
                                                   "Carry", new Vector3(0f, -0.03f, 0.04f));

            // AND WHERE EVERYTHING ELSE IT CARRIES RIDES - a belt line across the hips.
            //
            // A ghost used to hide every item but the equipped one, which made those items
            // UNTAKEABLE as well as unseen, because CarryableItem.IsAvailable reads `visible`. A
            // past self holding pin and key with the pin out put the key somewhere the living player
            // could neither see nor reach. Wearing them is what makes "who has the key" answerable
            // and the answer reachable, and it needs no HUD to say it.
            //
            // The HIPS rather than the hand, because a stowed object should not swing with the arm;
            // rather than the ghost root, because at the root it slides beside the figure. The
            // 0.09 forward clears the body so an object sits ON the belt rather than in the pelvis,
            // and 0.13 apart is enough that two 0.22-scaled escape objects do not intersect.
            replayer.stowAnchor = MakeGhostAnchor(body.transform, ghost.transform, "Hips",
                                                  "Stow", Vector3.zero);
            replayer.stowLocalOrigin = new Vector3(0f, 0f, 0.09f);
            replayer.stowStep = new Vector3(0.13f, 0f, 0f);

            GameObject prefabAsset = PrefabUtility.SaveAsPrefabAsset(ghost, prefabPath);
            Object.DestroyImmediate(ghost);


            return prefabAsset.GetComponent<GhostReplayer>();
        }

        // ONE material across every submesh, where the opaque version cloned six (Skin, Shirt,
        // Pants, Hair, Eyes, Socks). Keeping them tinted apart would have survived the fresnel and
        // handed back the very thing the change removes: a rim that still says shirt-here,
        // trousers-there is a legible person, just a see-through one. The array still has to be
        // the submesh count long, so the same material is handed to every slot.
        //
        // The old Assets/Materials/Ghost*.mat are left on disk and simply unreferenced - they are
        // the FBX's own colours and cost nothing, and going back means pointing at them again.
        private static Material[] GhostMaterials(Material[] source)
        {
            Material faint = GhostFaintMaterial();
            var result = new Material[source.Length];
            for (int i = 0; i < source.Length; i++) result[i] = faint;
            return result;
        }

        // Tuning lives here rather than in the .shader's defaults, for the same reason every other
        // number in this project does: the shader is the mechanism, SceneBuilder is the value.
        private static Material GhostFaintMaterial()
        {
            string path = $"{MaterialsDir}/GhostFaint.mat";
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>($"{ShadersDir}/GhostFaint.shader");
            if (shader == null)
            {
                Debug.LogError($"[SceneBuilder] GhostFaint.shader missing from {ShadersDir}");
                return MakeColorMaterial("GhostFaintFallback", new Color(0.7f, 0.76f, 0.86f));
            }

            // GhostFaint.mat already exists from the era of the six faint primitives, on URP/Lit.
            // Reassigning the shader on the existing asset keeps its GUID, so nothing that
            // referenced it has to be found and repointed.
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = shader;
            // The asset arrives carrying _SURFACE_TYPE_TRANSPARENT from its URP/Lit days. Swapping
            // the shader does not drop it - it just moves to m_InvalidKeywords and sits in the
            // diff forever. This shader declares no keywords at all, so the set is emptied.
            mat.shaderKeywords = new string[0];

            // Cool and desaturated, not white: at white the rim reads as a highlight on the wall
            // panelling behind it, which is the one colour in the room it must not be confused for.
            // Written through .linear - the project renders linear and SetColor takes the value
            // as-is, so an sRGB triple passed straight in comes out pale.
            mat.SetColor("_BaseColor", ((Color)new Color32(178, 194, 220, 255)).linear);
            mat.SetColor("_RimColor", ((Color)new Color32(224, 237, 255, 255)).linear);
            mat.SetFloat("_RimPower", 2.2f);
            mat.SetFloat("_RimAlpha", 0.75f);
            mat.SetFloat("_CoreAlpha", 0.06f);
            mat.SetFloat("_BottomFade", 0.35f);
            mat.SetFloat("_SoftFade", 0.12f);
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

            EditorUtility.SetDirty(mat);
            return mat;
        }

        // Three states, no transitions, because GhostReplayer scrubs each of them by hand - it
        // pins animator.speed at 0 and writes the normalized time itself. See the note there for
        // why the walk has to advance with distance rather than with the clock.
        private static RuntimeAnimatorController GhostAnimatorController()
        {
            string path = $"{PrefabsDir}/GhostAnimator.controller";
            var controller = AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(path);
            if (controller == null)
                controller = UnityEditor.Animations.AnimatorController.CreateAnimatorControllerAtPath(path);

            var machine = controller.layers[0].stateMachine;
            // Rebuilt from scratch each time, since adding is not idempotent and a retried build
            // would otherwise stack duplicate states - the same failure ConfigureAmbientOcclusion
            // guards against with the SSAO feature.
            foreach (var child in machine.states) machine.RemoveState(child.state);

            AddGhostState(machine, "Walk", "Man_Walk", true);
            AddGhostState(machine, "Idle", "Man_Idle", false);
            AddGhostState(machine, "Swing", "Man_SwordSlash", false);

            EditorUtility.SetDirty(controller);
            return controller;
        }

        private static void AddGhostState(UnityEditor.Animations.AnimatorStateMachine machine,
                                          string stateName, string clipSuffix, bool isDefault)
        {
            AnimationClip clip = null;
            foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(GhostModelPath))
            {
                var candidate = o as AnimationClip;
                if (candidate == null) continue;

                // LoadAllAssetsAtPath hands back the editor's hidden "__preview__" copies of every
                // clip alongside the real ones, and they sort first. Binding a state to one leaves
                // an animator that works in the editor and has nothing to play in a build - it
                // looked completely correct until the states were dumped and read.
                if (candidate.name.StartsWith("__preview__")) continue;

                // Clips arrive named "HumanArmature|Man_Walk", so match the tail rather than the
                // whole string - the armature prefix is the exporter's, not ours.
                if (candidate.name.EndsWith(clipSuffix)) { clip = candidate; break; }
            }
            if (clip == null) Debug.LogWarning($"[SceneBuilder] ghost clip '{clipSuffix}' not found");

            var state = machine.AddState(stateName);
            state.motion = clip;
            state.writeDefaultValues = true;
            if (isDefault) machine.defaultState = state;
        }

        private static void UseSinglePillow(GameObject bed, string keepName, string hideName, float centreX)
        {
            Transform hide = FindDescendant(bed.transform, hideName);
            if (hide != null) hide.gameObject.SetActive(false);

            Transform keep = FindDescendant(bed.transform, keepName);
            if (keep == null) return;

            Renderer[] renderers = keep.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return;

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);

            // Shift in world space: the model root is rotated, so nudging localPosition.x would
            // move the pillow along the model's axis rather than the room's.
            keep.position += new Vector3(centreX - bounds.center.x, 0f, 0f);
        }

        // Swaps a model's material for a copy with baked ambient occlusion switched off. The glb's
        // own materials are sub-assets that get regenerated on every reimport, so the copy has to
        // live as its own asset rather than being edited in place. The scene's SSAO still supplies
        // real contact shadows, so nothing is lost.
        private static void DisableBakedOcclusion(GameObject model, string materialName, string assetName)
        {
            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.sharedMaterial == null || renderer.sharedMaterial.name != materialName) continue;

                string path = $"{MaterialsDir}/{assetName}.mat";
                Material copy = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (copy == null)
                {
                    copy = new Material(renderer.sharedMaterial);
                    AssetDatabase.CreateAsset(copy, path);
                }

                // Re-copy every build so the glb staying the source of truth for the other maps.
                copy.shader = renderer.sharedMaterial.shader;
                copy.CopyPropertiesFromMaterial(renderer.sharedMaterial);
                if (copy.HasProperty("occlusionTexture_strength")) copy.SetFloat("occlusionTexture_strength", 0f);

                EditorUtility.SetDirty(copy);
                renderer.sharedMaterial = copy;
            }
            AssetDatabase.SaveAssets();
        }

        private static Transform FindDescendant(Transform root, string name)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }

        private static (NarrationDirector, RoomAmbience) BuildAudio(GameObject player, Door[] doors, FloorButton[] floorButtons, WakeUpSequence wakeUp)
        {
            GameObject root = new GameObject("Audio");

            // --- the PA announcer ---
            GameObject paGO = new GameObject("PA");
            paGO.transform.SetParent(root.transform, false);
            NarrationDirector narration = paGO.AddComponent<NarrationDirector>();
            // 2D on purpose: a room-wide tannoy has no position you could walk away from.
            narration.voiceSource = MakeSource(paGO.transform, "Voice", 0f, 1f);
            narration.chimeSource = MakeSource(paGO.transform, "Chime", 0f, 0.7f);
            // Both go through the same speaker, so both get the same treatment.
            AddTannoyFilters(narration.voiceSource);
            AddTannoyFilters(narration.chimeSource);

            AudioClip[] iterationLines = new AudioClip[NarrationIterationLines];
            for (int i = 0; i < iterationLines.Length; i++)
                iterationLines[i] = LoadClip(VoiceDir, $"voice_iteration_{i + 1:00}");
            narration.iterationLines = iterationLines;
            narration.iterationGenericLine = LoadClip(VoiceDir, "voice_iteration_generic");
            narration.tenSecondsLine = LoadClip(VoiceDir, "voice_ten_seconds");

            // Element 0 is "Nine.", counting down to "One." at the end.
            AudioClip[] countdownLines = new AudioClip[9];
            for (int i = 0; i < countdownLines.Length; i++)
                countdownLines[i] = LoadClip(VoiceDir, $"voice_count_{9 - i}");
            narration.countdownLines = countdownLines;
            narration.newCycleLine = LoadClip(VoiceDir, "voice_new_cycle");
            narration.cycleTerminatedLine = LoadClip(VoiceDir, "voice_cycle_terminated");
            narration.cycleBrokenLine = LoadClip(VoiceDir, "voice_cycle_broken");
            narration.manualTerminationLine = LoadClip(VoiceDir, "voice_manual_termination");
            narration.announcementChime = LoadClip(SfxDir, "sfx_chime");

            // --- room tone and machinery ---
            GameObject ambienceGO = new GameObject("Ambience");
            ambienceGO.transform.SetParent(root.transform, false);
            RoomAmbience ambience = ambienceGO.AddComponent<RoomAmbience>();
            ambience.musicSource = MakeSource(ambienceGO.transform, "Music", 0f, 0.5f, loop: true);
            ambience.machineSource = MakeSource(ambienceGO.transform, "Machines", 0f, 0.7f);
            ambience.ominousLoop = LoadClip(SfxDir, "sfx_ominous_loop");
            // The two halves of the loop boundary, split across the blackout by LoopManager.
            ambience.pullIn = LoadClip(SfxDir, "sfx_pull_in");
            ambience.powerDown = LoadClip(SfxDir, "sfx_power_down");

            // --- the floor pads ---
            // Positional and parented to each pad, which is the entire point: the door lamp only
            // reports the condition to someone looking at the door, but the clunk reaches you
            // wherever you are. Hearing a ghost step onto a pad behind you is how the puzzle tells
            // you the door is live - and in Room3, where two pads have to go down, it is how you
            // count them without turning round.
            AudioClip padPress = LoadClip(SfxDir, "sfx_floor_button_press");
            AudioClip padRelease = LoadClip(SfxDir, "sfx_floor_button_release");
            foreach (FloorButton pad in floorButtons)
            {
                pad.audioSource = MakeSource(pad.transform, "FloorButtonAudio", 1f, 0.9f);
                pad.pressClip = padPress;
                pad.releaseClip = padRelease;
            }

            // --- the doors ---
            AudioClip doorOpen = LoadClip(SfxDir, "sfx_door_open");
            foreach (Door d in doors)
            {
                d.audioSource = MakeSource(d.transform, "DoorAudio", 1f, 1f);
                d.openClip = doorOpen;
            }

            // --- the body waking up ---
            // 2D and parented to the player: this is the player's own breath, not a sound in the
            // room. It hangs off the player rather than the WakeUpSequence, which lives on the UI
            // canvas where a positional source would be meaningless.
            wakeUp.bodySource = MakeSource(player.transform, "BodyAudio", 0f, 1f);
            wakeUp.gaspClip = LoadClip(SfxDir, "sfx_gasp");
            wakeUp.sheetRustleClip = LoadClip(SfxDir, "sfx_sheet_rustle");

            return (narration, ambience);
        }

        // Turns a clean AudioSource into a wall-mounted PA horn firing into a hard, empty room.
        //
        // Done as a runtime filter chain rather than baked into the WAVs on purpose: the clips stay
        // clean masters, the treatment is one Inspector tweak away from being retuned, and a
        // replacement take dropped into Assets/Audio/Voice/ inherits the whole thing for free.
        //
        // Component order IS the signal chain - Unity runs the filters top to bottom on the
        // GameObject, so these are added in the order they should process. Reverb last: putting it
        // ahead of the distortion would grit up the tail as well as the voice, which reads as a
        // broken speaker rather than a room.
        private static void AddTannoyFilters(AudioSource source)
        {
            GameObject go = source.gameObject;

            // Band-limit first. A horn driver has no bottom and no top, and losing both is most of
            // what makes a voice read as "coming out of a speaker" rather than as narration - the
            // ear identifies the channel long before it identifies the reverb.
            AudioHighPassFilter hp = go.AddComponent<AudioHighPassFilter>();
            hp.cutoffFrequency = 340f;
            hp.highpassResonanceQ = 1f;

            AudioLowPassFilter lp = go.AddComponent<AudioLowPassFilter>();
            lp.cutoffFrequency = 3600f;
            // Slightly resonant rather than flat: a real horn has a peak up there, and that peak is
            // the nasal honk of every station announcement ever made.
            lp.lowpassResonanceQ = 1.6f;

            // Just enough drive to suggest an overdriven line. Past ~0.3 the words stop being
            // intelligible, and the announcer has actual information in them (the iteration number).
            AudioDistortionFilter dist = go.AddComponent<AudioDistortionFilter>();
            dist.distortionLevel = 0.17f;

            // The slap off the far wall. 105ms is roughly the round trip across a room this size,
            // so it reads as this room rather than as an effect.
            AudioEchoFilter echo = go.AddComponent<AudioEchoFilter>();
            echo.delay = 105f;
            echo.decayRatio = 0.22f;
            echo.dryMix = 1f;
            echo.wetMix = 0.33f;

            // And the tail. Preset is set to User first: assigning any individual property switches
            // it there anyway, and setting it explicitly keeps the intent readable.
            AudioReverbFilter verb = go.AddComponent<AudioReverbFilter>();
            verb.reverbPreset = AudioReverbPreset.User;
            verb.dryLevel = 0f;
            verb.room = -350f;
            // Dark tail. Hard painted panels absorb almost nothing low and quite a lot high, and a
            // bright tail would fight the band-limiting the horn just did.
            verb.roomHF = -900f;
            verb.decayTime = 2.1f;
            verb.decayHFRatio = 0.55f;
            verb.reflectionsLevel = -650f;
            verb.reverbLevel = 250f;
            verb.diffusion = 100f;
            verb.density = 100f;
        }

        // spatialBlend 0 is 2D (heard the same everywhere), 1 is fully positional.
        private static AudioSource MakeSource(Transform parent, string name, float spatialBlend, float volume, bool loop = false)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);

            AudioSource source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = loop;
            source.spatialBlend = spatialBlend;
            source.volume = volume;
            // Linear rather than the logarithmic default: the room is only ~10m across, and the
            // default curve is still near full volume across the whole of it.
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 1f;
            source.maxDistance = 14f;
            return source;
        }

        // Extension-agnostic, so a sourced .ogg or .mp3 drops in as readily as a .wav.
        private static AudioClip LoadClip(string dir, string baseName)
        {
            foreach (string ext in new[] { ".wav", ".ogg", ".mp3", ".aif", ".aiff" })
            {
                AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>($"{dir}/{baseName}{ext}");
                if (clip != null) return clip;
            }
            return null;
        }

        // The HUD face. Monospace on purpose, and for two reasons at once.
        //
        // Diegetically, everything on screen belongs to the facility rather than to the player -
        // the iteration number, the clock, the control that ends a cycle - and a terminal face says
        // that where a proportional one reads as a game's own UI. The room is built out of wall
        // *displays* for the same reason.
        //
        // Practically, the countdown is a number that changes every second: in a proportional font
        // its digits are different widths, so it reflows and twitches on the spot every tick. In a
        // monospace one it does not move at all.
        //
        // NOTE ON LICENSING: Consolas is Microsoft's, copied out of C:/Windows/Fonts. Fine for a
        // prototype that never leaves this machine, NOT fine to ship. Swapping it is one file and
        // this path - JetBrains Mono or IBM Plex Mono are both SIL OFL and drop straight in.
        private static Font UIFont()
        {
            // The STATIC Regular, not the variable font that ships alongside it: uGUI's legacy
            // Font has no axis control, so a variable face is a coin toss on which weight renders.
            Font font = AssetDatabase.LoadAssetAtPath<Font>($"{FontsDir}/JetBrains_Mono/static/JetBrainsMono-Regular.ttf");
            // Falls back rather than throwing: a missing font should leave the UI ugly and legible,
            // not leave the scene unbuildable.
            return font != null ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        // Top-left readout of what is in the player's pockets. Carrying is state the loop rewinds
        // and the key is invisible once pocketed, so without this "do I still have the key" is only
        // answerable by walking to the door and trying it.
        private static void BuildCarriedItems(Transform canvas, PlayerHand hand)
        {
            // ONE SLOT. It was a row of eight, sized for a pocket that could hold every symbol cube
            // at once; the hand holds exactly one object now and the other seven were a readout of
            // something that cannot happen.
            const float slotSize = 58f;

            GameObject go = new GameObject("CarriedItems");
            go.transform.SetParent(canvas, false);

            RectTransform rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(slotSize, slotSize);
            rect.anchoredPosition = new Vector2(36f, -30f);

            GameObject slotGO = new GameObject("Slot");
            slotGO.transform.SetParent(go.transform, false);

            Image image = slotGO.AddComponent<Image>();
            // Red like the rest of the HUD: the walls are near-white, so a white glyph disappears
            // into them.
            image.color = Color.red;
            image.raycastTarget = false;
            image.preserveAspect = true;
            image.enabled = false;

            RectTransform slotRect = image.GetComponent<RectTransform>();
            slotRect.anchorMin = new Vector2(0f, 1f);
            slotRect.anchorMax = new Vector2(0f, 1f);
            slotRect.pivot = new Vector2(0f, 1f);
            slotRect.sizeDelta = new Vector2(slotSize, slotSize);
            slotRect.anchoredPosition = Vector2.zero;

            // The put-down prompt, under the icon rather than out in the middle of the screen: it
            // explains this readout, so it belongs on it. A child of the row, so moving the row
            // moves the label with it.
            GameObject hintGO = new GameObject("DropHint");
            hintGO.transform.SetParent(go.transform, false);

            CanvasGroup hintGroup = hintGO.AddComponent<CanvasGroup>();
            hintGroup.alpha = 0f;
            hintGroup.blocksRaycasts = false;
            hintGroup.interactable = false;

            Text hint = hintGO.AddComponent<Text>();
            hint.font = UIFont();
            hint.fontSize = 16;
            hint.alignment = TextAnchor.UpperLeft;
            hint.color = Color.red;
            // Bracketed key then the verb, the same shape as the end-cycle control's label.
            hint.text = "[E] — PUT DOWN";
            // 0.6 * 16 * 16 is about 154px against a 300px rect, but overflow is set anyway - a
            // silently rewrapping HUD label has bitten this project twice.
            hint.horizontalOverflow = HorizontalWrapMode.Overflow;
            hint.verticalOverflow = VerticalWrapMode.Overflow;
            hint.raycastTarget = false;

            RectTransform hintRect = hint.GetComponent<RectTransform>();
            hintRect.anchorMin = new Vector2(0f, 1f);
            hintRect.anchorMax = new Vector2(0f, 1f);
            hintRect.pivot = new Vector2(0f, 1f);
            hintRect.sizeDelta = new Vector2(300f, 22f);
            // anchoredPosition, never localPosition: the latter is stale on a RectTransform, and
            // it must be written AFTER AddComponent<Text> replaced the Transform.
            hintRect.anchoredPosition = new Vector2(0f, -(slotSize + 10f));

            CarriedItemsDisplay display = go.AddComponent<CarriedItemsDisplay>();
            display.hand = hand;
            display.slot = image;
            display.dropHint = hintGroup;
        }

        // The two control prompts. A grey disc over whatever the player has walked up to, with an
        // E on it, and a second disc carrying a mouse glyph that appears on the pin the moment it
        // is in hand. Each is shown once and then retired for good - see ControlHintDisplay.
        //
        // Built last of everything on the canvas so it draws over the eyelids and the HUD, and
        // parented to a full-screen rect so a screen point converts straight to an anchoredPosition.
        private static ControlHintDisplay BuildControlHints(Transform canvas, Camera playerCamera, BalloonTool swingTool,
                                              MonoBehaviour[] interactTargets)
        {
            Sprite disc = HintDiscSprite();

            GameObject root = new GameObject("ControlHints");
            root.transform.SetParent(canvas, false);
            RectTransform area = root.AddComponent<RectTransform>();
            area.anchorMin = Vector2.zero;
            area.anchorMax = Vector2.one;
            area.offsetMin = Vector2.zero;
            area.offsetMax = Vector2.zero;

            (CanvasGroup interactGroup, RectTransform interactRect) =
                MakeHintBadge(area, "InteractHint", disc);
            // The key itself, in the HUD's monospace face - the same one the iteration number and
            // the clock use, because this is the facility labelling its own equipment.
            GameObject glyphGO = new GameObject("Glyph");
            glyphGO.transform.SetParent(interactRect, false);
            Text glyph = glyphGO.AddComponent<Text>();
            glyph.font = UIFont();
            glyph.fontSize = 44;
            glyph.alignment = TextAnchor.MiddleCenter;
            glyph.color = Color.white;
            glyph.raycastTarget = false;
            glyph.text = "E";
            RectTransform glyphRect = glyph.GetComponent<RectTransform>();
            glyphRect.anchorMin = Vector2.zero;
            glyphRect.anchorMax = Vector2.one;
            glyphRect.offsetMin = Vector2.zero;
            glyphRect.offsetMax = Vector2.zero;

            (CanvasGroup swingGroup, RectTransform swingRect) = MakeHintBadge(area, "SwingHint", disc);
            GameObject mouseGO = new GameObject("Glyph");
            mouseGO.transform.SetParent(swingRect, false);
            Image mouse = mouseGO.AddComponent<Image>();
            mouse.sprite = MouseLeftIcon();
            mouse.color = Color.white;
            mouse.raycastTarget = false;
            mouse.preserveAspect = true;
            RectTransform mouseRect = mouse.GetComponent<RectTransform>();
            mouseRect.anchorMin = new Vector2(0.5f, 0.5f);
            mouseRect.anchorMax = new Vector2(0.5f, 0.5f);
            mouseRect.sizeDelta = new Vector2(48f, 48f);
            mouseRect.anchoredPosition = Vector2.zero;

            ControlHintDisplay display = root.AddComponent<ControlHintDisplay>();
            display.playerCamera = playerCamera;
            display.swingTool = swingTool;
            display.interactTargets = interactTargets;
            display.area = area;
            display.interactGroup = interactGroup;
            display.interactRect = interactRect;
            display.swingGroup = swingGroup;
            display.swingRect = swingRect;
            return display;
        }

        private static (CanvasGroup, RectTransform) MakeHintBadge(Transform parent, string name, Sprite disc)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);

            RectTransform rect = go.AddComponent<RectTransform>();
            // Centred anchors: ControlHintDisplay writes a screen position straight into
            // anchoredPosition, which only lines up if the anchor is the middle of the parent.
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(78f, 78f);

            CanvasGroup group = go.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            // Never eats input: the end-cycle control is a real uGUI button, and a prompt that
            // swallowed clicks would be a bug that only shows up when both are on screen.
            group.blocksRaycasts = false;
            group.interactable = false;

            Image background = go.AddComponent<Image>();
            background.sprite = disc;
            // Grey and translucent - present enough to read against a white wall, quiet enough not
            // to become the thing you look at.
            background.color = new Color(0.25f, 0.25f, 0.27f, 0.78f);
            background.raycastTarget = false;

            return (group, rect);
        }

        private static (IterationLabel label, WakeUpSequence wakeUp, Transform canvas) BuildUI(PlayerHand hand)
        {
            GameObject canvasGO = new GameObject("Canvas");
            Canvas canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Scale with the screen rather than the default constant pixel size, which pins the
            // text to a fixed point size and leaves it tiny at 4K and oversized in a small window.
            CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            // Split the difference between matching width and height, so neither a wide nor a tall
            // window blows the UI up.
            scaler.matchWidthOrHeight = 0.5f;

            canvasGO.AddComponent<GraphicRaycaster>();

            // Built first so it sits at the back of the canvas: the iteration label and the timer
            // then draw ON TOP of the closed eyelids instead of being blacked out by them.
            WakeUpSequence wakeUp = BuildEyelids(canvasGO.transform);

            GameObject groupGO = new GameObject("IterationLabelGroup");
            groupGO.transform.SetParent(canvasGO.transform, false);
            CanvasGroup group = groupGO.AddComponent<CanvasGroup>();
            group.alpha = 0f;

            RectTransform rect = groupGO.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            // Dead centre. It used to sit 150px above, which read as a subtitle floating over the
            // room; the label is the iteration announcing itself, so it belongs on the middle of
            // the screen with nothing else on it.
            rect.sizeDelta = new Vector2(1200f, 140f);
            rect.anchoredPosition = Vector2.zero;

            GameObject textGO = new GameObject("Label");
            textGO.transform.SetParent(groupGO.transform, false);
            Text text = textGO.AddComponent<Text>();
            text.font = UIFont();
            // Bigger than it was, because the letters are now spaced out (see IterationLabel) and
            // tracked-out text at 42 reads as small print rather than as a title card.
            text.fontSize = 54;
            text.alignment = TextAnchor.MiddleCenter;
            // Never wrap. The spaced-out string is full of spaces, so a rect a shade too narrow
            // would break the label across two lines mid-word - "ITERATIO / N 12".
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            // Red, not white - the room walls are near-white, so white text is invisible.
            text.color = Color.red;
            RectTransform textRect = text.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            // CYCLE N, above the iteration and inside the SAME CanvasGroup so the two fade as one
            // card rather than as two announcements.
            //
            // Smaller than the iteration deliberately: the cycle is the frame and the iteration is
            // the subject. Anchored to the group's top edge and pushed clear of it, so the stack
            // stays centred on the screen as a whole.
            GameObject cycleGO = new GameObject("CycleLabel");
            cycleGO.transform.SetParent(groupGO.transform, false);
            Text cycleText = cycleGO.AddComponent<Text>();
            cycleText.font = UIFont();
            cycleText.fontSize = 34;
            cycleText.alignment = TextAnchor.MiddleCenter;
            cycleText.horizontalOverflow = HorizontalWrapMode.Overflow;
            cycleText.verticalOverflow = VerticalWrapMode.Overflow;
            cycleText.color = Color.red;
            RectTransform cycleRect = cycleText.GetComponent<RectTransform>();
            cycleRect.anchorMin = new Vector2(0f, 1f);
            cycleRect.anchorMax = new Vector2(1f, 1f);
            cycleRect.pivot = new Vector2(0.5f, 0.5f);
            cycleRect.sizeDelta = new Vector2(0f, 60f);
            cycleRect.anchoredPosition = new Vector2(0f, 42f);
            // Off until there has been more than one bed - IterationLabel.Show enables it. Cycle 1
            // says nothing, the way iteration 1 gets no reset announcement.
            cycleText.enabled = false;

            IterationLabel label = groupGO.AddComponent<IterationLabel>();
            label.canvasGroup = group;
            label.label = text;
            label.cycleLabel = cycleText;

            BuildSleepingGas(canvasGO.transform);
            BuildCountdownTimer(canvasGO.transform);
            BuildEndCycleControl(canvasGO.transform);
            BuildCarriedItems(canvasGO.transform, hand);

            // uGUI buttons do nothing without one of these in the scene, and NewScene's default
            // objects are only a camera and a light.
            GameObject eventSystem = new GameObject("EventSystem");
            eventSystem.AddComponent<UnityEngine.EventSystems.EventSystem>();
            eventSystem.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();

            return (label, wakeUp, canvasGO.transform);
        }

        // Escape's overlay. Built after everything else on the canvas so it draws over the HUD,
        // the control prompts and the eyelids - a pause menu behind a closed eyelid would be a
        // strange thing to discover.
        private static void BuildPauseMenu(Transform canvas, FirstPersonController playerController)
        {
            GameObject root = new GameObject("PauseMenu");
            root.transform.SetParent(canvas, false);
            Stretch(root.AddComponent<RectTransform>());

            CanvasGroup group = root.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = false;
            group.interactable = false;

            // Heavier than the title screen's scrim: this one has to read as the game having
            // stopped, where the menu's only has to hold text off a picture.
            GameObject scrimGO = new GameObject("Scrim");
            scrimGO.transform.SetParent(root.transform, false);
            Image scrim = scrimGO.AddComponent<Image>();
            scrim.color = new Color(0f, 0f, 0f, 0.72f);
            // Left as a raycast target on purpose: it is what stops a click reaching the end-cycle
            // control sitting underneath it.
            scrim.raycastTarget = true;
            Stretch(scrim.GetComponent<RectTransform>());

            GameObject titleGO = new GameObject("Title");
            titleGO.transform.SetParent(root.transform, false);
            Text title = titleGO.AddComponent<Text>();
            title.font = UIFont();
            title.fontSize = 52;
            title.alignment = TextAnchor.MiddleCenter;
            title.color = Color.red;
            // Spaced out in the string, as everything else in this typeface is.
            title.text = "P A U S E D";
            title.horizontalOverflow = HorizontalWrapMode.Overflow;
            title.verticalOverflow = VerticalWrapMode.Overflow;
            title.raycastTarget = false;
            RectTransform titleRect = title.GetComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0.5f, 0.5f);
            titleRect.anchorMax = new Vector2(0.5f, 0.5f);
            titleRect.sizeDelta = new Vector2(1000f, 100f);
            titleRect.anchoredPosition = new Vector2(0f, 170f);

            Button resume = MakeMenuButton(root.transform, "ResumeButton", "RESUME", new Vector2(0f, 40f));
            Button toMenu = MakeMenuButton(root.transform, "MenuButton", "MAIN MENU", new Vector2(0f, -40f));
            Button quit = MakeMenuButton(root.transform, "QuitButton", "QUIT", new Vector2(0f, -120f));

            // Below the buttons rather than above them: this is a setting, not an action, and the
            // three things a paused player most often wants stay where they were.
            const float settingsY = -200f;
            MakeRowLabel(root.transform, "SensitivityLabel", "MOUSE SENSITIVITY",
                new Vector2(-150f, settingsY), new Vector2(260f, 30f), TextAnchor.MiddleLeft);
            Slider sensitivity = MakeSlider(root.transform, "SensitivitySlider",
                new Vector2(80f, settingsY), new Vector2(200f, 26f));
            Text sensitivityValue = MakeRowLabel(root.transform, "SensitivityValue", "0.00",
                new Vector2(225f, settingsY), new Vector2(80f, 30f), TextAnchor.MiddleLeft);

            GameObject hintGO = new GameObject("Hint");
            hintGO.transform.SetParent(root.transform, false);
            Text hint = hintGO.AddComponent<Text>();
            hint.font = UIFont();
            hint.fontSize = 18;
            hint.alignment = TextAnchor.MiddleCenter;
            hint.color = new Color(1f, 0.35f, 0.35f, 0.7f);
            hint.text = "[ESC] TO RESUME";
            hint.raycastTarget = false;
            RectTransform hintRect = hint.GetComponent<RectTransform>();
            hintRect.anchorMin = new Vector2(0.5f, 0.5f);
            hintRect.anchorMax = new Vector2(0.5f, 0.5f);
            hintRect.sizeDelta = new Vector2(600f, 36f);
            hintRect.anchoredPosition = new Vector2(0f, -262f);

            PauseMenu pause = root.AddComponent<PauseMenu>();
            pause.playerController = playerController;
            pause.group = group;
            pause.resumeButton = resume;
            pause.menuButton = toMenu;
            pause.quitButton = quit;
            pause.sensitivitySlider = sensitivity;
            pause.sensitivityValue = sensitivityValue;
        }

        // The ending: a full-screen black scrim and a card over it. Two separate CanvasGroups
        // rather than one, because the whole shape of the ending is that the room goes first and
        // the card arrives afterwards, with a beat of nothing in between.
        //
        // Deliberately NOT the wake-up's eyelids, even though they are black panels over the same
        // canvas and would have been free. The lids blink, and the blink is the loop taking you -
        // it is the visual signature of the exact thing that has just failed. Reusing it here would
        // say the cycle turned over.
        private static EndingSequence BuildEndingScreen(Transform canvas)
        {
            GameObject root = new GameObject("EndingScreen");
            root.transform.SetParent(canvas, false);
            Stretch(root.AddComponent<RectTransform>());

            GameObject scrimGO = new GameObject("Scrim");
            scrimGO.transform.SetParent(root.transform, false);
            CanvasGroup scrimGroup = scrimGO.AddComponent<CanvasGroup>();
            scrimGroup.alpha = 0f;
            // Never a raycast target, unlike the pause scrim. There is nothing underneath left to
            // click by the time this is up, and blocking would only matter if something could still
            // be interacted with - which would be a bug, not a thing to defend against here.
            scrimGroup.blocksRaycasts = false;
            Stretch(scrimGO.AddComponent<RectTransform>());

            Image scrim = scrimGO.AddComponent<Image>();
            // Fully opaque, unlike the pause overlay's 0.72: this is not a layer over the room, it
            // is the room being gone.
            scrim.color = Color.black;
            scrim.raycastTarget = false;

            GameObject cardGO = new GameObject("Card");
            cardGO.transform.SetParent(root.transform, false);
            CanvasGroup cardGroup = cardGO.AddComponent<CanvasGroup>();
            cardGroup.alpha = 0f;
            cardGroup.blocksRaycasts = false;
            Stretch(cardGO.AddComponent<RectTransform>());

            GameObject headlineGO = new GameObject("Headline");
            headlineGO.transform.SetParent(cardGO.transform, false);
            Text headline = headlineGO.AddComponent<Text>();
            headline.font = UIFont();
            headline.fontSize = 52;
            headline.alignment = TextAnchor.MiddleCenter;
            headline.color = Color.red;
            // Filled in by EndingSequence, which spaces it out in the string. Seeded here only so
            // the object is not blank in the saved scene.
            headline.text = "C Y C L E   B R O K E N";
            headline.horizontalOverflow = HorizontalWrapMode.Overflow;
            headline.verticalOverflow = VerticalWrapMode.Overflow;
            headline.raycastTarget = false;
            RectTransform headlineRect = headline.GetComponent<RectTransform>();
            headlineRect.anchorMin = new Vector2(0.5f, 0.5f);
            headlineRect.anchorMax = new Vector2(0.5f, 0.5f);
            headlineRect.sizeDelta = new Vector2(1400f, 100f);
            headlineRect.anchoredPosition = new Vector2(0f, 30f);

            GameObject detailGO = new GameObject("Detail");
            detailGO.transform.SetParent(cardGO.transform, false);
            Text detail = detailGO.AddComponent<Text>();
            detail.font = UIFont();
            detail.fontSize = 22;
            detail.alignment = TextAnchor.MiddleCenter;
            // Dimmer than the headline, and not spaced out: this is the facility's record of the
            // run rather than its verdict on it, and it is the one number the player earned.
            detail.color = new Color(1f, 0.35f, 0.35f, 0.75f);
            detail.text = "ESCAPED ON ITERATION 1";
            detail.horizontalOverflow = HorizontalWrapMode.Overflow;
            detail.verticalOverflow = VerticalWrapMode.Overflow;
            detail.raycastTarget = false;
            RectTransform detailRect = detail.GetComponent<RectTransform>();
            detailRect.anchorMin = new Vector2(0.5f, 0.5f);
            detailRect.anchorMax = new Vector2(0.5f, 0.5f);
            detailRect.sizeDelta = new Vector2(1000f, 40f);
            detailRect.anchoredPosition = new Vector2(0f, -40f);

            // The run's total clock time, under the iteration count - the facility's record again,
            // not its verdict, so it gets the same treatment `detail` does rather than the
            // headline's. Smaller and dimmer still: this is the second line of a record, not a
            // second thing being announced.
            GameObject timeDetailGO = new GameObject("TimeDetail");
            timeDetailGO.transform.SetParent(cardGO.transform, false);
            Text timeDetail = timeDetailGO.AddComponent<Text>();
            timeDetail.font = UIFont();
            timeDetail.fontSize = 18;
            timeDetail.alignment = TextAnchor.MiddleCenter;
            timeDetail.color = new Color(1f, 0.35f, 0.35f, 0.6f);
            timeDetail.text = "TOTAL TIME 0:00";
            timeDetail.horizontalOverflow = HorizontalWrapMode.Overflow;
            timeDetail.verticalOverflow = VerticalWrapMode.Overflow;
            timeDetail.raycastTarget = false;
            RectTransform timeDetailRect = timeDetail.GetComponent<RectTransform>();
            timeDetailRect.anchorMin = new Vector2(0.5f, 0.5f);
            timeDetailRect.anchorMax = new Vector2(0.5f, 0.5f);
            timeDetailRect.sizeDelta = new Vector2(1000f, 34f);
            timeDetailRect.anchoredPosition = new Vector2(0f, -72f);

            EndingSequence ending = root.AddComponent<EndingSequence>();
            ending.scrimGroup = scrimGroup;
            ending.cardGroup = cardGroup;
            ending.headline = headline;
            ending.detail = detail;
            ending.timeDetail = timeDetail;
            ending.menuScene = "MainMenu";

            return ending;
        }

        // The room telling the player about the end-cycle control, on all four walls at once. Hung
        // in Room1 now and shown from iteration 2 - see the call site for why it moved out of Room3.
        //
        // World-space canvases hung just proud of the panelling rather than the panels themselves
        // being lit to spell it. That was the first idea and it does not survive contact with the
        // grid: a wall is 5 or 6 cells across by 4 tall, so panel-as-pixel gives a 6x4 display and
        // nothing legible can be written in it. What makes this still read as the wall rather than
        // as a poster is the dark plate behind the text - a section of white panelling switching to
        // near-black with red type on it is exactly what a display doing something looks like, and
        // it is the same red-on-near-black the HUD already uses.
        private static PanelMessage BuildWallMessage(Transform parent, float roomCenterZ)
        {
            GameObject root = new GameObject("WallMessage");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = new Vector3(0f, 0f, roomCenterZ);

            // Proud of the panel faces, which sit on the room bound itself. Small enough to read as
            // printed on the wall, large enough that no camera angle z-fights with it.
            const float standoff = 0.05f;
            float halfDepth = RoomDepth / 2f;
            // Set so the plate's BOTTOM edge clears the door lamp, not merely the doorway. The
            // plate is 1.98m tall, so at 3.95 it spans 2.96..4.94: above the lamp at 2.725..2.835
            // by 0.125m and under the 5.408 ceiling by 0.47. At the 3.4 it was first built at it
            // covered the lamp on the north wall completely - and every room in the chain has that
            // lamp, so the number carried across the move to Room1 unchanged.
            //
            // All four faces share the height even though only the north one has a lamp under it:
            // two walls are in view at once from most of this room, and a sign that changes height
            // between them reads as a mistake.
            const float y = 3.95f;

            // All four walls. In Room1 the player wakes facing 180 degrees - down the room at the
            // SOUTH wall, away from the bed - and then turns for the door in the north one, so the
            // two walls that matter here are opposite each other. Covering all four costs nothing
            // extra and removes the question. It retires against the action (see PanelMessage), so
            // the sign is not paid for once per iteration for the rest of the run.
            //
            // Each canvas's forward (+Z) points INTO its wall, i.e. away from the room. That reads
            // backwards and it is the opposite of what was built first, which came out mirrored.
            //
            // The rule: a world-space canvas is legible when its forward matches the direction the
            // viewer is LOOKING, not when it points at the viewer. Unity's own default scene is the
            // proof - camera at z = -10 looking toward +Z, canvas at the origin unrotated, text the
            // right way round. So a wall message must face the same way as the eyes reading it, and
            // a player at the middle of the room looks outwards at every one of these.
            CanvasGroup[] faces = MakeFourWallFaces(root.transform, y, standoff, FillTerminationMessage);

            PanelMessage message = root.AddComponent<PanelMessage>();
            message.faces = faces;
            message.roomCenterZ = roomCenterZ;
            message.halfDepth = halfDepth;
            return message;
        }

        // Room4 - the room past the last door, and everything in it.
        //
        // The shell is a plain white cell like the other three (BuildShell), and that is the point
        // rather than a saving: the room the player finally gets out into looks exactly like the one
        // they have been trying to get out of. What makes it the ending is that NOTHING ELSE IS IN
        // IT. One object, and it is not there when they walk in - it comes up out of the floor, the
        // only thing in the game that does, so there is nothing to look for and nowhere else to go.
        //
        // The plinth is authored in its RAISED position and sunk at runtime by FinalRoomSequence.
        // Authoring it underground instead would leave a scene whose one prop is invisible and
        // impossible to check without pressing Play.
        private static FinalRoomSequence BuildFinalRoom(Transform parent, float roomCenterZ,
                                                        Material propMat, Door doorBehind,
                                                        WallPanelDisplay wallPanels,
                                                        CameraShaker cameraShaker, PlayerHand hand,
                                                        string cubeId, string sphereId, string prismId)
        {
            GameObject root = new GameObject("FinalRoom");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = new Vector3(0f, 0f, roomCenterZ);

            // Waist height, so the plate on top is looked DOWN at from a 1.6m eye rather than
            // squared up to like the calibration wall's. The two plates are the same fixture at
            // opposite ends of the run, and the difference in how you stand over them is the only
            // thing separating "begin" from "end".
            const float plinthHeight = 1.05f;
            // WIDER THAN IT IS DEEP now, where it used to be a 1.15 square. Three recesses in a row
            // need a run of top surface, and a console you stand at the front of - sockets across the
            // back, the press at the front - is a clearer object than a square block with four things
            // crowded onto it. The depth is unchanged, so the walk round it is what it always was.
            const float plinthWidth = 1.70f;
            const float plinthDepth = 1.15f;
            const float plateProud = 0.02f;

            // The three recesses, in a row across the top. 0.46 apart spans 1.38 of the 1.55 of
            // usable plate. They used to sit in the back half with a press at the front; the press is
            // gone, so the row is centred and the console is the three things it asks for.
            const float slotPitch = 0.46f;
            const float slotSize = 0.26f;
            const float slotRowZ = 0f;

            Material plateMat = MakeColorMaterial("FinalPlate", new Color(0.05f, 0.05f, 0.055f));

            GameObject plinth = new GameObject("Plinth");
            plinth.transform.SetParent(root.transform, false);
            plinth.transform.localPosition = Vector3.zero;

            // Keeps its collider - it is a solid object in the middle of the room, and the player
            // has to walk round it to the plate. Rising through someone standing exactly on the
            // centre would shove them aside on the next frame, which is ugly but not reachable: the
            // rise starts as they clear the doorway, six metres away.
            Prim(PrimitiveType.Cube, "Body", plinth.transform,
                new Vector3(0f, plinthHeight / 2f, 0f),
                new Vector3(plinthWidth, plinthHeight, plinthDepth), propMat);

            // The dark inset the button sits in, so the top of the plinth reads as a switched-off
            // display among white surfaces - the same near-black as every groove in the building.
            Prim(PrimitiveType.Cube, "TopPlate", plinth.transform,
                new Vector3(0f, plinthHeight + plateProud / 2f, 0f),
                new Vector3(plinthWidth * 0.91f, plateProud, plinthDepth * 0.78f), plateMat,
                removeCollider: true);

            // THE THREE RECESSES, one per escape object. Built before the button so the row is the
            // first thing in the hierarchy as it is the first thing on the console.
            //
            // Shapes, not colours, are what say which is which - the same principle the cube room's
            // spec insists on - so each is cut to the silhouette of the thing it takes: a square hole,
            // a round hole, a triangular hole. The colour is carried by the rim on top of that,
            // because the three objects are distinct in both and there is no reason to spend only one.
            var slots = new FinalSlot[3];
            slots[0] = BuildFinalSlot(plinth.transform, "Slot_Cube", SlotShape.Square,
                new Vector3(-slotPitch, plinthHeight + plateProud, slotRowZ), slotSize,
                new Color(0.85f, 0.10f, 0.10f));
            slots[1] = BuildFinalSlot(plinth.transform, "Slot_Sphere", SlotShape.Round,
                new Vector3(0f, plinthHeight + plateProud, slotRowZ), slotSize,
                new Color(0.16f, 0.40f, 0.95f));
            slots[2] = BuildFinalSlot(plinth.transform, "Slot_Prism", SlotShape.Triangle,
                new Vector3(slotPitch, plinthHeight + plateProud, slotRowZ), slotSize,
                new Color(0.95f, 0.78f, 0.12f));

            // Which object each recess takes, handed in rather than written here, because two of the
            // three do not exist yet and an empty id is the honest way to say that: an undeclared slot
            // accepts nothing, prompts for nothing, and is skipped by FinalSlot.AllFilled.
            slots[0].acceptedItemId = cubeId;
            slots[1].acceptedItemId = sphereId;
            slots[2].acceptedItemId = prismId;

            // NO BUTTON. There was one here - a plate that ended the game on a single press - and
            // three recesses that each want a named object say everything it said, in a room the
            // player now has to reach with all three inside a running sixty seconds.

            // NO ERROR SIGN ON THE WALLS. It was four wall-sized canvases like Room3's message, and
            // that was the wrong instrument: a sign is something the room PUTS UP, and a facility
            // that can still put a sign up has not failed. The word lives on the panels themselves
            // now - every one of them a screen showing a test card with ERROR on it - so the room
            // does not report the fault, it IS the fault. See WallPanelDisplay.
            // THE CONSOLE IS A RewardPlinth, the same fixture the other three rooms pay out on. It
            // rises when the player reaches Room4 and sinks when the loop takes them back - which is
            // only a question now that the clock runs through this room. The old rise was a coroutine
            // fired by an event that could happen once; an iteration can end in here.
            RewardPlinth console = plinth.AddComponent<RewardPlinth>();
            console.plinth = plinth.transform;
            // Clears the floor by a hair, so nothing shows through the slab before it is meant to.
            console.riseHeight = plinthHeight + plateProud + 0.06f;
            console.riseSeconds = 2.4f;

            FinalRoomSequence sequence = root.AddComponent<FinalRoomSequence>();
            sequence.console = console;
            sequence.slots = slots;
            foreach (FinalSlot slot in slots) { slot.sequence = sequence; slot.hand = hand; }
            sequence.doorBehind = doorBehind;
            sequence.wallPanels = wallPanels;
            sequence.cameraShaker = cameraShaker;
            // TEN SECONDS from the press to the scrim starting, and that is a floor rather than a
            // pause: the door takes 1s to seal and the panels take glitchOnset 6.5s to fail across
            // the whole building. At the 3.4s this was first built at, all of that was still
            // arriving when the screen went black. The room has to be SEEN broken, or the last
            // thing the player did has no visible consequence.
            sequence.breakDuration = 10f;

            return sequence;
        }

        // Which silhouette a recess is cut to. Named rather than passed as a mesh, because the
        // caller is stating what the hole IS for, and where that mesh comes from is this file's
        // problem - two of the three are Unity primitives and the third has to be built.
        private enum SlotShape { Square, Round, Triangle }

        // One recess: a coloured rim with a darker hole inside it, and a seat at the bottom for
        // whatever goes in.
        //
        // NOT A REAL HOLE, because there is no CSG here and the top plate is one box. What reads as
        // depth is the pair: a rim standing 12mm proud of the plate and a near-black floor 4mm above
        // it, so the eye takes the dark shape as the inside of a well. That is the same trick the
        // plinth's own TopPlate already plays - near-black among white surfaces reads as a cavity.
        private static FinalSlot BuildFinalSlot(Transform parent, string name, SlotShape shape,
                                                Vector3 localTop, float size, Color accent)
        {
            const float rimProud = 0.012f;
            const float floorProud = 0.004f;
            const float rimInset = 0.80f;   // the hole, as a fraction of the rim

            GameObject root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = localTop;

            // ONE material for all three rims. Each says something different about itself - idle,
            // ready, filled - and says it through a property block, which is what FinalSlot drives.
            Material rimMat = MakeEmissiveMaterial("FinalSlotRim", Color.white, 1f);
            Material holeMat = MakeColorMaterial("FinalSlotHole", new Color(0.03f, 0.03f, 0.035f));

            GameObject rim = ShapePrim(shape, "Rim", root.transform,
                new Vector3(0f, rimProud / 2f, 0f), new Vector3(size, rimProud, size), rimMat);
            ShapePrim(shape, "Hole", root.transform,
                new Vector3(0f, floorProud / 2f + rimProud * 0.35f, 0f),
                new Vector3(size * rimInset, floorProud, size * rimInset), holeMat);

            // Where the object lands. At the floor of the well rather than on the plate, so an
            // inserted object sits IN the recess - the seat is the socket CarryableItem parents to.
            GameObject seat = new GameObject("Seat");
            seat.transform.SetParent(root.transform, false);
            seat.transform.localPosition = new Vector3(0f, rimProud * 0.35f + floorProud, 0f);

            // THE REACH, and it has to reach past the console rather than sit over it. The recesses
            // are in the TOP of a 1.15m-deep block, so a volume centred on a recess is inside the
            // block: the nearest a player can physically get is pressed against the front face, which
            // is 0.45m short of where the first version's box began. It was unreachable, and only
            // standing at it in play found that - the geometry all measured correctly.
            //
            // Biased toward -Z, which is the way in from Room3, and wide enough that the three boxes
            // overlap. Overlapping is right: a player at the middle of the console is at all three
            // recesses, and which one answers is decided by what is in their hand.
            BoxCollider reach = root.AddComponent<BoxCollider>();
            reach.isTrigger = true;
            reach.center = new Vector3(0f, -0.55f, -0.45f);
            reach.size = new Vector3(1.5f, 2.6f, 2.7f);

            FinalSlot slot = root.AddComponent<FinalSlot>();
            slot.seat = seat.transform;
            slot.rimRenderer = rim.GetComponent<Renderer>();
            slot.idleColor = accent * 0.35f;
            slot.readyColor = accent;
            slot.filledColor = accent;
            slot.audioSource = MakeSource(root.transform, "SlotAudio", 1f, 0.8f);
            slot.insertClip = LoadClip(SfxDir, "sfx_floor_button_press");
            // acceptedItemId is deliberately left EMPTY. None of the three escape objects exists as
            // a carryable yet, and a slot that names an id nothing wears would prompt for a press
            // that cannot succeed. See TODO.md.
            return slot;
        }

        // `bevelled` swaps the primitive for a generated chamfered version of the same silhouette.
        // Only the three escape objects ask for it - the recesses they go into keep the plain
        // shapes, because a hole is read as an outline and a chamfer on one is detail nobody looks
        // at. See BevelledPrismMesh for what the chamfer buys and what it costs.
        private static GameObject ShapePrim(SlotShape shape, string name, Transform parent,
                                            Vector3 localPos, Vector3 localScale, Material mat,
                                            bool keepCollider = false, bool bevelled = false)
        {
            if (bevelled)
            {
                GameObject bev = new GameObject(name);
                bev.transform.SetParent(parent, false);
                bev.transform.localPosition = localPos;
                bev.transform.localScale = localScale;
                bev.AddComponent<MeshFilter>().sharedMesh = BevelledPrismMesh(shape, 0.06f);
                bev.AddComponent<MeshRenderer>().sharedMaterial = mat;
                if (keepCollider) bev.AddComponent<BoxCollider>();
                return bev;
            }

            if (shape == SlotShape.Square)
                return Prim(PrimitiveType.Cube, name, parent, localPos, localScale, mat,
                            removeCollider: !keepCollider);

            if (shape == SlotShape.Round)
                // A Unity cylinder is 2 units tall at scale 1, so its Y has to be halved to mean the
                // same thing the cube's does. Everything else here states a thickness in metres.
                return Prim(PrimitiveType.Cylinder, name, parent, localPos,
                    new Vector3(localScale.x, localScale.y * 0.5f, localScale.z), mat,
                    removeCollider: !keepCollider);

            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = localScale;
            // Chamfer 0: the same three-sided prism this always used, minus the inverted caps. Unity
            // has no triangular primitive, so this is the one shape of the three that has always had
            // to be generated.
            go.AddComponent<MeshFilter>().sharedMesh = BevelledPrismMesh(shape, 0f);
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            // A box round a prism. The shape is what the player reads; what they bump into can be the
            // bounding box, and a MeshCollider for a triangle you walk past is a cost with no payer.
            if (keepCollider) go.AddComponent<BoxCollider>();
            return go;
        }

        // ONE GENERATOR FOR EVERY PRISM IN THE GAME: an n-gon extruded, optionally with its top and
        // bottom rims chamfered. Square is a 4-gon, Triangle a 3-gon, Round a 48-gon.
        //
        // It replaces TriangularPrismMesh, which it is a superset of at chamfer 0 - AND WHICH HAD
        // BOTH CAPS WOUND INSIDE OUT. Unity's front face is the one whose cross(v1-v0, v2-v0) points
        // along the normal (checked against the built-in Quad, whose winding and normals are known),
        // and that prism's caps pointed into the solid while its sides pointed out. The caps were
        // therefore backface-culled: looking down at the yellow triangle you were seeing through its
        // top face to the inside of the far side. It survived unnoticed because the material was
        // emissive at 2.4 and saturated, so the inside of the shape is very nearly the same image as
        // the outside - and it would NOT survive the polished metal above, which is what sent
        // anybody to look. Anything generated here now gets its winding checked against that Quad.
        //
        // WHY A CHAMFER AT ALL. A Unity cube has six flat faces meeting at perfectly sharp edges, so
        // under any lighting the whole of each face is one shade and the object is three flat tones
        // in a row. That is what makes a primitive look like a primitive, and no material fixes it,
        // because there is no geometry near the edge for a highlight to run along. A 6% chamfer puts
        // a narrow band at every rim facing halfway between two faces, and on a polished metal that
        // band catches a bright line that moves as the player turns. It is the cheapest thing in
        // rendering that reads as "manufactured" rather than "default".
        //
        // The same generator handles Round, which lets the three actually match: a puck with a
        // machined rim beside a chamfered cube beside a chamfered prism reads as one set of objects
        // from one factory, where a Unity cylinder's razor rim next to a chamfered cube does not.
        //
        // SMOOTHING IS PER SHAPE. Square and Triangle give every face its own vertices, so the edges
        // stay hard - the existing prism's rule, and a chamfer whose bands got smoothed into the
        // faces would be a lumpy cube rather than a bevelled one. Round shares its side vertices all
        // the way round, so RecalculateNormals averages across the barrel and the chamfers and the
        // puck comes out genuinely round instead of a 48-sided drum.
        //
        // SAVED AS AN ASSET, for the reason TriangularPrismMesh gives: a Mesh made with `new Mesh()`
        // and assigned in a scene is a runtime object with no home, and the scene serialises a
        // reference to nothing.
        //
        // NORMALISED TO A UNIT BOUNDING BOX like the prism it generalises, so a localScale of 0.30
        // still means 0.30 ACROSS for every one of the three. The barrel ring is the widest part, so
        // it is the ring the bounds are taken from.
        // ROOM3: THE TREE. `Iteration - Future Ideas.md` §3, and the room the whole design has been
        // promising - the one whose payoff is five past selves swinging at once while the living
        // player walks between them.
        //
        // WHICH WAY IT FALLS IS FORCED. This is a corner room: the player comes in through the NORTH
        // wall and leaves through the WEST. A tree that fell north would block the way in on every
        // later iteration, and one that fell west would block the way out - so east is the only
        // direction left, and the trunk is sized to land inside the room rather than through its wall.
        //
        // THE TRUNK IS NOT WHAT BLOCKS THE ROOM - the buttresses either side of it are. A trunk alone
        // leaves a gap at each wall wide enough to walk round, and the puzzle would be optional.
        private static (Tree tree, ChopStation[] stations, CarryableItem[] axes)
            BuildTreeRoom(Transform roomRoot, Material propMat)
        {
            GameObject root = new GameObject("TreeRoom");
            root.transform.SetParent(roomRoot, false);

            Material barkMat = MakeColorMaterial("TreeBark", new Color(0.20f, 0.14f, 0.10f));
            SetSmoothness(barkMat, 0.18f);
            Material markMat = MakeEmissiveMaterial("ChopMark", new Color(0.45f, 0.47f, 0.52f), 0f);

            const float trunkRadius = 1.05f;
            // Short enough that lying east it stops inside the room: 1.05 of trunk plus 3.1 of length
            // against a wall 4.375 out.
            const float trunkHeight = 3.1f;
            const float wallHeight = 2.3f;

            // Hinged at the base on the EAST side of the trunk, so the whole thing tips about the
            // point it would actually break at.
            GameObject hinge = new GameObject("TreeHinge");
            hinge.transform.SetParent(root.transform, false);
            hinge.transform.localPosition = new Vector3(trunkRadius, 0f, 0f);

            Prim(PrimitiveType.Cylinder, "Trunk", hinge.transform,
                 new Vector3(-trunkRadius, trunkHeight / 2f, 0f),
                 new Vector3(trunkRadius * 2f, trunkHeight / 2f, trunkRadius * 2f), barkMat);

            // The buttresses: what actually seals the room, from the trunk to each side wall. They
            // tip with the trunk, which is what "the tree came down" has to look like - the way is
            // clear because the whole thing moved, not because a barrier was switched off.
            float halfX = RoomWidth / 2f;
            float span = halfX - trunkRadius + WallDepth;
            Prim(PrimitiveType.Cube, "Buttress_West", hinge.transform,
                 new Vector3(-trunkRadius - trunkRadius - span / 2f, wallHeight / 2f, 0f),
                 new Vector3(span, wallHeight, 1.1f), barkMat);
            Prim(PrimitiveType.Cube, "Buttress_East", hinge.transform,
                 new Vector3(-trunkRadius + trunkRadius + span / 2f, wallHeight / 2f, 0f),
                 new Vector3(span, wallHeight, 1.1f), barkMat);

            // FIVE PLACES TO STAND, on the north side - the side the player arrives from, and the
            // side the tree does not fall onto. Spread across the room's width so five bodies fit
            // without standing in each other.
            var stations = new ChopStation[5];
            for (int i = 0; i < stations.Length; i++)
            {
                float x = (i - 2) * 1.7f;
                GameObject go = new GameObject($"ChopStation_{i}");
                go.transform.SetParent(root.transform, false);
                go.transform.localPosition = new Vector3(x, 0f, 1.9f);

                GameObject mark = Prim(PrimitiveType.Cylinder, "Mark", go.transform,
                    new Vector3(0f, 0.015f, 0f), new Vector3(1.1f, 0.015f, 1.1f), markMat,
                    removeCollider: true);

                ChopStation station = go.AddComponent<ChopStation>();
                station.axeItemId = AxeItemId;
                station.activationRadius = 0.6f;
                station.markRenderer = mark.GetComponent<Renderer>();
                stations[i] = station;
            }

            // SIX AXES FOR FIVE STATIONS, on a rack against the south wall - which this room has to
            // itself, having no doorway in it. One spare, because a supply that exactly matches the
            // requirement makes a single axe left in the wrong place unrecoverable.
            Material headMat = MakePolishedMetalMaterial("AxeHead", new Color(0.86f, 0.87f, 0.90f), 0f);
            Material haftMat = MakeColorMaterial("AxeHaft", new Color(0.42f, 0.28f, 0.16f));
            var axes = new CarryableItem[6];
            for (int i = 0; i < axes.Length; i++)
                axes[i] = BuildAxe(root.transform, $"Axe_{i}",
                    new Vector3((i - 2.5f) * 0.85f, 0f, -RoomDepth / 2f + 0.7f), headMat, haftMat);

            Tree tree = root.AddComponent<Tree>();
            tree.stations = stations;
            tree.choppersNeeded = 5;
            tree.hinge = hinge.transform;
            tree.audioSource = MakeSource(root.transform, "TreeAudio", spatialBlend: 1f, volume: 1f);
            tree.fallClip = LoadClip(SfxDir, "sfx_power_down");
            return (tree, stations, axes);
        }

        // One axe. `BuildPin` is the template - the simplest complete carryable in the project - and
        // this is the same thing at a different size with a head on it.
        private static CarryableItem BuildAxe(Transform parent, string name, Vector3 localPos,
                                              Material headMat, Material haftMat)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;

            GameObject body = new GameObject("Body");
            body.transform.SetParent(go.transform, false);
            Prim(PrimitiveType.Cylinder, "Haft", body.transform, new Vector3(0f, 0.42f, 0f),
                 new Vector3(0.07f, 0.42f, 0.07f), haftMat, removeCollider: true);
            Prim(PrimitiveType.Cube, "Head", body.transform, new Vector3(0f, 0.80f, 0.06f),
                 new Vector3(0.09f, 0.20f, 0.26f), headMat, removeCollider: true);

            BoxCollider reach = go.AddComponent<BoxCollider>();
            reach.isTrigger = true;
            reach.center = new Vector3(0f, 0.5f, 0f);
            reach.size = new Vector3(0.7f, 1.1f, 0.7f);

            CarryableItem item = go.AddComponent<CarryableItem>();
            item.itemId = AxeItemId;
            item.displayName = "AXE";
            item.floorY = 0.04f;
            // Laid down rather than standing, so an axe on the floor reads as dropped rather than
            // as planted.
            item.restRoll = 90f;
            item.handLocalPosition = HandPoseFor(0.9f);
            item.handLocalEuler = new Vector3(-18f, 0f, 8f);
            item.audioSource = MakeSource(go.transform, "AxeAudio", spatialBlend: 1f, volume: 0.8f);
            item.pickupClip = LoadClip(SfxDir, "sfx_item_pickup");
            return item;
        }

        // ROOM2'S PUZZLE: three levers that come back up on their own.
        //
        // SPREAD AS FAR APART AS THE ROOM ALLOWS, and that spacing IS the difficulty. The levers hold
        // for four seconds and the walk between two of them is most of that, so one person can just
        // about catch two and can never catch three. Bring them closer and one past self is enough;
        // push them further and even two cannot overlap.
        //
        // **CLEAR OF BOTH DOORWAYS.** This room has a door in the middle of its north wall and
        // another in the middle of its south wall, and the first version put a lever squarely in each
        // - a solid housing standing in the only way through. So the two wall-mounted ones sit well
        // off centre, on opposite sides, which also buys the longest span in the room: corner to
        // corner across the diagonal.
        //
        // And clear of the window. The west wall is what this room is about; a lever in front of it
        // would stand between the player and the core.
        private static (Chorus chorus, ChorusLever[] levers) BuildChorus(Transform roomRoot, Material propMat)
        {
            GameObject root = new GameObject("Chorus");
            root.transform.SetParent(roomRoot, false);

            Material postMat = MakeColorMaterial("ChorusPost", new Color(0.22f, 0.23f, 0.27f));
            SetSmoothness(postMat, 0.55f);
            Material armMat = MakeColorMaterial("ChorusArm", new Color(0.78f, 0.79f, 0.82f));
            SetSmoothness(armMat, 0.7f);
            Material lampMat = MakeEmissiveMaterial("ChorusLamp", new Color(0.42f, 0.45f, 0.52f), 0f);

            float halfX = RoomWidth / 2f, halfZ = RoomDepth / 2f;
            // The doorways are 1.3 wide and centred, so 2.6 off centre clears them with room to walk
            // between. The third goes on the east wall, the only one with neither a door nor a window.
            const float clearOfDoor = 2.6f;
            var spots = new (Vector3 at, float yaw)[]
            {
                (new Vector3(clearOfDoor, 0f, halfZ - 0.45f), 180f),
                (new Vector3(-clearOfDoor, 0f, -halfZ + 0.45f), 0f),
                (new Vector3(halfX - 0.45f, 0f, 0f), 90f),
            };

            var levers = new ChorusLever[spots.Length];
            for (int i = 0; i < spots.Length; i++)
            {
                GameObject go = new GameObject($"ChorusLever_{i}");
                go.transform.SetParent(root.transform, false);
                go.transform.localPosition = spots[i].at;
                go.transform.localRotation = Quaternion.Euler(0f, spots[i].yaw, 0f);

                Prim(PrimitiveType.Cube, "Housing", go.transform, new Vector3(0f, 0.75f, 0f),
                     new Vector3(0.46f, 1.5f, 0.26f), postMat);

                // The arm hangs off a pivot at the top of the housing, so it swings about a point
                // rather than about its own middle.
                GameObject pivot = new GameObject("Pivot");
                pivot.transform.SetParent(go.transform, false);
                pivot.transform.localPosition = new Vector3(0f, 1.32f, -0.13f);

                Prim(PrimitiveType.Cube, "Arm", pivot.transform, new Vector3(0f, 0f, -0.22f),
                     new Vector3(0.09f, 0.09f, 0.46f), armMat, removeCollider: true);
                Prim(PrimitiveType.Sphere, "Grip", pivot.transform, new Vector3(0f, 0f, -0.44f),
                     new Vector3(0.15f, 0.15f, 0.15f), armMat, removeCollider: true);

                // The state light, high on the housing so it is readable from the far end of the
                // room - which is where the player will be standing when they need to know.
                GameObject lamp = Prim(PrimitiveType.Cube, "Lamp", go.transform,
                    new Vector3(0f, 1.62f, 0f), new Vector3(0.3f, 0.09f, 0.16f), lampMat,
                    removeCollider: true);

                ChorusLever lever = go.AddComponent<ChorusLever>();
                lever.arm = pivot.transform;
                lever.lampRenderer = lamp.GetComponent<Renderer>();
                lever.hintAnchor = pivot.transform;
                // TWO SECONDS. The room's three separations are 4.4s, 3.2s and 2.0s of walking, so at
                // four seconds one person could hold two of the three together and only the long
                // diagonal was out of reach. At two, every pair but the shortest is beyond one person
                // - and that shortest one is a dead heat rather than a walk.
                lever.holdSeconds = 2f;
                lever.audioSource = MakeSource(go.transform, "LeverAudio", spatialBlend: 1f, volume: 0.85f);
                lever.pullClip = LoadClip(SfxDir, "sfx_drawer_open");
                lever.releaseClip = LoadClip(SfxDir, "sfx_floor_button_release");
                levers[i] = lever;
            }

            Chorus chorus = root.AddComponent<Chorus>();
            chorus.levers = levers;
            return (chorus, levers);
        }

        // ONE SHARD, as a carryable. `BuildPin` is the template this follows - the simplest complete
        // carryable in the project.
        //
        // The mesh is shared; only the yaw differs, and it is the yaw that says which third of the
        // ring this is. Held, they all look alike, which is correct: what the player is collecting is
        // three of a thing, not three different things.
        private static CarryableItem BuildRingShard(Transform parent, string name, string itemId,
                                                     Vector3 localPos, float yaw, Mesh mesh, Material mat)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;

            // The mesh on a CHILD, carrying the yaw. The item's own transform stays axis-aligned so
            // the hand pose and the reach trigger are read off something unrotated - the same
            // separation `MakeChessPiece` makes for the mirrored pieces.
            GameObject body = new GameObject("Body");
            body.transform.SetParent(go.transform, false);
            body.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            body.AddComponent<MeshFilter>().sharedMesh = mesh;
            body.AddComponent<MeshRenderer>().sharedMaterial = mat;

            // The reach volume, on the item itself - CarryableItem takes GetComponent<Collider>() as
            // its trigger, so this must be the only collider on this object.
            BoxCollider reach = go.AddComponent<BoxCollider>();
            reach.isTrigger = true;
            reach.center = new Vector3(0f, 0.12f, 0f);
            reach.size = new Vector3(0.9f, 0.5f, 0.9f);

            CarryableItem item = go.AddComponent<CarryableItem>();
            item.itemId = itemId;
            item.displayName = "SHARD";
            item.floorY = 0.03f;
            item.handLocalPosition = HandPoseFor(0.45f);
            item.handLocalEuler = new Vector3(12f, 0f, 0f);
            item.audioSource = MakeSource(go.transform, "ShardAudio", spatialBlend: 1f, volume: 0.8f);
            item.pickupClip = LoadClip(SfxDir, "sfx_item_pickup");
            return item;
        }

        // A THIRD OF A RING: the shape all three of cycle 2's escape objects share.
        //
        // Generated rather than composed out of primitives, because the one thing this shape has to
        // do is look BROKEN OFF - an arc with two flat radial faces where it parted from its
        // neighbours. A box approximation gives a staircase on the inner and outer curves, which
        // reads as a low-poly wedge rather than as a piece of something.
        //
        // Saved as an asset and served from cache on the next build, exactly as `BevelledPrismMesh`
        // is: a Mesh created at edit time and assigned to a scene object is lost on reload unless it
        // lives somewhere.
        //
        // All three shards are the SAME mesh, rotated. They are thirds of one ring, so they are
        // identical by construction - and a puzzle that asked the player to tell them apart would be
        // cycle 1's shape-matching again under a new name.
        private static Mesh RingShardMesh(float sweepDegrees, float innerRadius, float outerRadius, float thickness)
        {
            string assetName = $"RingShard{Mathf.RoundToInt(sweepDegrees)}";
            string path = GeneratedDir + "/" + assetName + ".mesh";
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null) return existing;

            const int segments = 24;
            float half = thickness / 2f;
            var verts = new System.Collections.Generic.List<Vector3>();
            var tris = new System.Collections.Generic.List<int>();

            // Four rings of vertices - inner and outer, top and bottom - walked together so every
            // face of the solid can be stitched from the same index arithmetic.
            for (int i = 0; i <= segments; i++)
            {
                float a = Mathf.Deg2Rad * (-sweepDegrees / 2f + sweepDegrees * i / segments);
                float sin = Mathf.Sin(a), cos = Mathf.Cos(a);
                verts.Add(new Vector3(sin * innerRadius, half, cos * innerRadius));
                verts.Add(new Vector3(sin * outerRadius, half, cos * outerRadius));
                verts.Add(new Vector3(sin * innerRadius, -half, cos * innerRadius));
                verts.Add(new Vector3(sin * outerRadius, -half, cos * outerRadius));
            }

            void Quad(int a, int b, int c, int d)
            {
                tris.Add(a); tris.Add(b); tris.Add(c);
                tris.Add(a); tris.Add(c); tris.Add(d);
            }

            for (int i = 0; i < segments; i++)
            {
                int p0 = i * 4, p1 = (i + 1) * 4;
                Quad(p0 + 0, p0 + 1, p1 + 1, p1 + 0);   // top
                Quad(p0 + 2, p1 + 2, p1 + 3, p0 + 3);   // bottom
                Quad(p0 + 1, p0 + 3, p1 + 3, p1 + 1);   // outer curve
                Quad(p0 + 0, p1 + 0, p1 + 2, p0 + 2);   // inner curve
            }

            // THE BROKEN ENDS. Flat radial faces, and the whole reason this is not a torus segment
            // with open sides - they are what the eye reads as a fracture.
            int last = segments * 4;
            Quad(0, 2, 3, 1);
            Quad(last + 1, last + 3, last + 2, last + 0);

            Mesh mesh = new Mesh { name = assetName };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            if (!Directory.Exists(GeneratedDir)) Directory.CreateDirectory(GeneratedDir);
            AssetDatabase.CreateAsset(mesh, path);
            AssetDatabase.SaveAssets();
            return AssetDatabase.LoadAssetAtPath<Mesh>(path);
        }

        private static Mesh BevelledPrismMesh(SlotShape shape, float chamfer)
        {
            // The chamfer is in the asset name, so a shape and its bevelled twin are two assets and
            // neither can be served from the other's cache. It is also what retires the old
            // TriangularPrism.mesh without having to find and delete it: nothing asks for that path
            // any more.
            string assetName = $"Prism{shape}{Mathf.RoundToInt(chamfer * 100f)}";
            string path = GeneratedDir + "/" + assetName + ".mesh";
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null) return existing;

            // Round gets enough segments that the silhouette is a circle at arm's length, which is
            // where these are looked at hardest - it is in the hand for most of a final lap.
            int sides = shape == SlotShape.Round ? 48 : (shape == SlotShape.Triangle ? 3 : 4);
            bool smooth = shape == SlotShape.Round;

            // Where vertex 0 sits. The triangle keeps its apex at +Z, away from the player and so the
            // top of the shape as it is looked down on - the existing prism's choice, kept because
            // the recess in Room4 is cut from the unbevelled mesh at the same angle. A 4-gon starts
            // at 45 degrees, which is what turns it into an axis-aligned square rather than a
            // diamond.
            float turn = shape == SlotShape.Triangle ? Mathf.PI * 0.5f
                       : shape == SlotShape.Square ? Mathf.PI * 0.25f : 0f;

            Vector2[] unit = new Vector2[sides];
            for (int i = 0; i < sides; i++)
            {
                float a = turn + i * Mathf.PI * 2f / sides;
                unit[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            }

            // Fit the widest ring to a unit box in XZ. A triangle inscribed in a circle is neither
            // square nor centred on that circle, so without this it comes out smaller and offset
            // against the other two - which in a row of three reads as a mistake, not as a triangle.
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            foreach (Vector2 v in unit)
            {
                minX = Mathf.Min(minX, v.x); maxX = Mathf.Max(maxX, v.x);
                minY = Mathf.Min(minY, v.y); maxY = Mathf.Max(maxY, v.y);
            }
            Vector2 centre = new Vector2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f);
            Vector2 span = new Vector2(maxX - minX, maxY - minY);
            for (int i = 0; i < sides; i++)
                unit[i] = new Vector2((unit[i].x - centre.x) / span.x, (unit[i].y - centre.y) / span.y);

            // Rings bottom to top. With a chamfer there are four - bottom cap edge, bottom of the
            // barrel, top of the barrel, top cap edge - and the caps are pulled in by the chamfer
            // while the barrel stays full width. Without one there are just the two ends, and no
            // degenerate zero-height band is emitted for RecalculateNormals to choke on.
            //
            // 6% is the chamfer these objects use. Big enough to catch a light at 0.30m across - 18mm
            // on the real object, about the width of the highlight it exists to hold - and small
            // enough that the silhouette is still unmistakably a cube, a triangle or a disc, which is
            // the one thing these three must never stop being: the recesses are cut to match.
            float[] ringY, ringScale;
            if (chamfer > 0f)
            {
                float inset = 1f - chamfer * 2f;
                ringY = new[] { -0.5f, -0.5f + chamfer, 0.5f - chamfer, 0.5f };
                ringScale = new[] { inset, 1f, 1f, inset };
            }
            else
            {
                ringY = new[] { -0.5f, 0.5f };
                ringScale = new[] { 1f, 1f };
            }
            int rings = ringY.Length;

            var verts = new System.Collections.Generic.List<Vector3>();
            var tris = new System.Collections.Generic.List<int>();

            Vector3 At(int ring, int i) =>
                new Vector3(unit[i].x * ringScale[ring], ringY[ring], unit[i].y * ringScale[ring]);

            // WINDING, stated once because getting it wrong is invisible on an emissive object and
            // fatal on a metal one: Unity's front face is the triangle whose cross(v1-v0, v2-v0)
            // points ALONG the outward normal. The ring runs counter-clockwise in XZ seen from +Y.
            if (smooth)
            {
                // One continuous side surface with shared vertices, so the normals average round the
                // circumference and across both chamfers - a rounded-off puck rather than a drum.
                int baseIndex = verts.Count;
                for (int r = 0; r < rings; r++)
                    for (int i = 0; i < sides; i++) verts.Add(At(r, i));

                for (int r = 0; r < rings - 1; r++)
                    for (int i = 0; i < sides; i++)
                    {
                        int j = (i + 1) % sides;
                        int a = baseIndex + r * sides + i, b = baseIndex + r * sides + j;
                        int c = baseIndex + (r + 1) * sides + j, d = baseIndex + (r + 1) * sides + i;
                        tris.Add(a); tris.Add(d); tris.Add(c);
                        tris.Add(a); tris.Add(c); tris.Add(b);
                    }
            }
            else
            {
                // Every band of every face gets its own four vertices, so nothing smooths into
                // anything: the faces stay flat and the chamfers stay as distinct bands.
                for (int r = 0; r < rings - 1; r++)
                    for (int i = 0; i < sides; i++)
                    {
                        int j = (i + 1) % sides;
                        int b = verts.Count;
                        verts.Add(At(r, i)); verts.Add(At(r, j));
                        verts.Add(At(r + 1, j)); verts.Add(At(r + 1, i));
                        tris.Add(b); tris.Add(b + 3); tris.Add(b + 2);
                        tris.Add(b); tris.Add(b + 2); tris.Add(b + 1);
                    }
            }

            // Caps, as fans off their own vertices so they stay flat whatever the sides do. Wound
            // opposite ways so both face outward - and note the TOP fan runs backwards against the
            // ring. A fan following a counter-clockwise ring produces a downward normal, which is
            // exactly the inversion the mesh this replaces shipped with.
            int topBase = verts.Count;
            for (int i = 0; i < sides; i++) verts.Add(At(rings - 1, i));
            for (int i = 1; i < sides - 1; i++)
            {
                tris.Add(topBase); tris.Add(topBase + i + 1); tris.Add(topBase + i);
            }

            int botBase = verts.Count;
            for (int i = 0; i < sides; i++) verts.Add(At(0, i));
            for (int i = 1; i < sides - 1; i++)
            {
                tris.Add(botBase); tris.Add(botBase + i); tris.Add(botBase + i + 1);
            }

            Mesh mesh = new Mesh { name = assetName };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            // No RecalculateTangents: tangents are derived from UVs, this mesh has none, and nothing
            // wearing it uses a normal map. Asking for them buys a warning and a garbage array.
            mesh.RecalculateBounds();

            if (!Directory.Exists(GeneratedDir)) Directory.CreateDirectory(GeneratedDir);
            AssetDatabase.CreateAsset(mesh, path);
            AssetDatabase.SaveAssets();
            return mesh;
        }

        // The canvas, the plate and the placement, shared by every wall sign in the game. What goes ON
        // it comes from the caller: Room3's message is two lines of text, Room2's is a row of
        // pictograms, and both want exactly this plate at exactly this height on all four walls.
        private static CanvasGroup MakeWallFace(Transform parent, string name, Vector3 localPosition,
                                                Quaternion localRotation, System.Action<Transform> fillContent,
                                                float worldWidth = 7.2f, bool withPlate = true,
                                                float authoredHeight = 440f)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);

            Canvas canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;

            // Authored large and scaled down, which is the standard way to keep world-space UI text
            // from rendering as a handful of blocky pixels: the font rasterises at the RectTransform
            // size, not the final world size.
            // Always authored 1600 WIDE whatever the final size, so every sign in the game rasterises
            // at the same resolution and the x layout numbers inside fillContent mean the same thing at
            // every scale. Only the scale changes: 7.2m for a wall-wide message, 3.6m over a door.
            //
            // The authored HEIGHT is separate because a sign's aspect is set by what it has to cover.
            // 440 is the text plate's shape; the side-wall pictograms are 618, which is what makes their
            // box exactly two rows of the wall grid tall at their width.
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(1600f, authoredHeight);
            rect.localScale = Vector3.one * (worldWidth / 1600f);

            // Placed through anchoredPosition3D, set after the Canvas exists. AddComponent<Canvas>()
            // replaces the GameObject's plain Transform with a RectTransform, and a RectTransform's
            // local x and y come from its anchored position - so this is the field that means
            // anything. Anchors centred, so the offset is measured from the parent's origin (a
            // non-RectTransform parent is treated as a zero-size rect there).
            //
            // Worth knowing when verifying this in the saved scene, because it looks exactly like a
            // bug: a RectTransform's serialized m_LocalPosition is stale - here it stays {0,0,0}
            // while m_AnchoredPosition carries the real placement, and Unity recomputes the former
            // on load. Read m_AnchoredPosition, not m_LocalPosition.
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition3D = localPosition;
            rect.localRotation = localRotation;

            CanvasGroup group = go.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = false;
            group.interactable = false;

            // The plate. This is what makes it a display rather than PAINT - GrooveDark is the same
            // near-black that sits at the bottom of every groove in the room.
            //
            // Optional, because that distinction is the whole choice. A lit black plate is the
            // facility interrupting, which is right for the message that stops an iteration; it is
            // far too loud for a small hint over a doorway, which wants to read as something
            // stencilled on the wall and be ignorable once it has been understood.
            if (withPlate)
            {
                GameObject plateGO = new GameObject("Plate");
                plateGO.transform.SetParent(go.transform, false);
                Image plate = plateGO.AddComponent<Image>();
                plate.color = new Color(0.04f, 0.04f, 0.045f, 0.94f);
                plate.raycastTarget = false;
                Stretch(plate.GetComponent<RectTransform>());
            }

            fillContent?.Invoke(go.transform);

            return group;
        }

        // All four walls of one room, at one height, carrying one piece of content.
        //
        // Each canvas's forward (+Z) points INTO its wall, i.e. away from the room. That reads
        // backwards and it is the opposite of what was built first, which came out mirrored.
        //
        // The rule: a world-space canvas is legible when its forward matches the direction the viewer
        // is LOOKING, not when it points at the viewer. Unity's own default scene is the proof -
        // camera at z = -10 looking toward +Z, canvas at the origin unrotated, text the right way
        // round. So a wall sign must face the same way as the eyes reading it, and a player anywhere
        // in the middle of a room looks outwards at every one of these.
        //
        // Worth knowing: this is why wall signs are canvases and not textures on the panelling. A
        // panel is a PrimitiveType.Cube, and a cube's +X and -X faces carry opposite U - so the same
        // texture reads mirrored on the east wall against the west (docs/gotchas.md). A canvas has no
        // such handedness, only the facing rule above, which is one decision instead of per-wall
        // bookkeeping.
        private static CanvasGroup[] MakeFourWallFaces(Transform parent, float y, float standoff,
                                                       System.Action<Transform> fillContent)
        {
            float halfWidth = RoomWidth / 2f;
            float halfDepth = RoomDepth / 2f;

            return new[]
            {
                MakeWallFace(parent, "South", new Vector3(0f, y, -halfDepth + standoff), Quaternion.Euler(0f, 180f, 0f), fillContent),
                MakeWallFace(parent, "North", new Vector3(0f, y, halfDepth - standoff), Quaternion.identity, fillContent),
                MakeWallFace(parent, "West", new Vector3(-halfWidth + standoff, y, 0f), Quaternion.Euler(0f, -90f, 0f), fillContent),
                MakeWallFace(parent, "East", new Vector3(halfWidth - standoff, y, 0f), Quaternion.Euler(0f, 90f, 0f), fillContent),
            };
        }

        // Room3's content: the two text lines that were the only thing a wall face ever held.
        private static void FillTerminationMessage(Transform face)
        {
            MakeWallLine(face, "Headline", "H O L D   [ N ]", 132, Color.red,
                new Vector2(0f, 78f), new Vector2(1600f, 190f));
            // Not spaced out, unlike the headline: this line is 29 characters and spacing it would
            // put it past the wall. The headline carries the treatment for both.
            MakeWallLine(face, "Detail", "TO SKIP TO THE NEXT ITERATION", 74,
                new Color(1f, 0.35f, 0.35f, 0.9f), new Vector2(0f, -90f), new Vector2(1600f, 130f));
        }

        // Room2's sign: PIN, then LEFT CLICK, then a balloon, then that balloon gone. No words at all.
        //
        // It exists because of a specific failure, not as decoration. A player on the itch build could
        // not work out how to burst a balloon and gave up - "kept trying and closed it". The
        // left-click prompt now sits on the balloon a swing would burst, but that prompt only appears
        // while the pin is IN HAND, so it says nothing at all to the player who walked past the drawer
        // and arrived here empty-handed. This is the sign for that player: it names the tool, the
        // button and the outcome, in the order they happen.
        //
        // Four icons and three arrows, drawn to the same 1600px width the text lines use. Sizes and
        // centres are laid out from one total so the row stays centred if any of them change.
        //
        // CHARCOAL ON THE BARE WALL, no plate behind it. The first version was red on a lit black
        // plate, borrowed from Room3's message, and it dominated the room - which is wrong for this
        // one. Room3's sign interrupts an iteration and should be impossible to miss once; this is a
        // standing hint that has to be findable when wanted and ignorable for the rest of the run, so
        // it reads as stencilled on the panelling. The value sits well clear of GrooveDark (0.04) so
        // it cannot be mistaken for a seam, and the arrows go dimmer still - they are punctuation, and
        // at equal weight the row reads as seven things rather than four and three.
        //
        // The icon size is a parameter because the two placements want different weights: 190 over the
        // door leaves margin either side of the row, 295 fills the authored width exactly for the big
        // side-wall signs. The arrow keeps its ratio to the icon so the row's rhythm is the same at
        // both sizes.
        private static void FillBalloonPictogram(Transform face, float icon)
        {
            float arrow = icon * (90f / 190f);
            float total = icon * 4f + arrow * 3f;
            float x = -total / 2f;

            Color ink = new Color(0.20f, 0.20f, 0.23f, 0.72f);
            // The arrows are RED while the things they connect stay charcoal, which inverts what the
            // first version did - it dimmed them, on the grounds that they are punctuation. Colour
            // separates them better than weight does: the eye now gets the direction of the sequence
            // before it has identified any of the four objects, and "left to right, then it pops" is
            // the half of this sign that has to survive being glanced at. Same red the facility uses
            // for its own text, held slightly off full so it reads as printed rather than lit.
            Color punctuation = new Color(0.82f, 0.12f, 0.12f, 0.85f);

            Sprite[] sprites = { PinIcon(), MouseLeftIcon(), BalloonIcon(), BalloonBurstIcon() };
            string[] names = { "Pin", "Click", "Balloon", "Burst" };
            Sprite arrowSprite = ArrowRightIcon();

            for (int i = 0; i < sprites.Length; i++)
            {
                MakeWallIcon(face, names[i], sprites[i], new Vector2(x + icon / 2f, 0f), icon, ink);
                x += icon;

                if (i == sprites.Length - 1) continue;
                MakeWallIcon(face, $"Arrow{i}", arrowSprite, new Vector2(x + arrow / 2f, 0f), arrow, punctuation);
                x += arrow;
            }
        }

        private static void MakeWallIcon(Transform parent, string name, Sprite sprite,
                                         Vector2 anchoredPosition, float size, Color color)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);

            Image image = go.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            // The icons are square and drawn square; preserveAspect keeps a future non-square one
            // from being stretched to fit rather than fitted.
            image.preserveAspect = true;

            RectTransform rect = image.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(size, size);
            rect.anchoredPosition = anchoredPosition;
        }

        // Room2's pictogram: the two SIDE walls, retired the moment the player pops anything.
        //
        // The side walls, and only those. A sign went over the key door first, on the reasoning that
        // the way out is the one surface every player in here is guaranteed to face - and it was
        // removed, because that is a description of where a player is LOOKING and not of where they
        // are stuck. Someone who cannot burst a balloon is standing among balloons turning on the
        // spot, which puts a side wall in front of them; and a hint over the exit reads as being about
        // the exit. Two big signs beside the puzzle say it where the puzzle is.
        //
        // South is bare for the opposite reason: it is at the player's back on the way in, and nobody
        // turns round for it.
        //
        // These are the ANSWER rather than a label, so they are sized to be read from anywhere in the
        // room - 7.0m, up from the 2.2m first tried and the 3.6m after it, both of which read as too
        // small from across the room. Charcoal on bare wall, no plate, at every size.
        //
        // The retirement is the other difference from Room3's. That sign goes on the first visit out,
        // because skipping an iteration is learned in one reading. This one stays until the player has
        // actually burst something, because the player it exists for is the one who arrives with empty
        // hands, fails, and walks back out to look for the tool - under the leave-once rule they would
        // return to a blank wall, having been shown the answer at the one moment they could not use it.
        private static PanelMessage BuildBalloonPictogram(Transform parent, float roomCenterZ, BalloonTool tool)
        {
            GameObject root = new GameObject("BalloonPictogram");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = new Vector3(0f, 0f, roomCenterZ);

            const float standoff = 0.05f;
            float halfWidth = RoomWidth / 2f;
            float halfDepth = RoomDepth / 2f;

            // THE WIDTH IS SIZED TO THE WALL GRID, and the height is sized to clear a doorway.
            //
            // A side wall is RoomDepth long, so it is 10.5 / 1.75 = SIX columns: the sign spans the
            // middle four, 7.0m, with the column boundaries falling at -3.5 and +3.5. That is why it
            // reads as printed on those panels rather than floating across them.
            //
            // The HEIGHT used to be grid-aligned too - the middle two rows, centred on RoomHeight/2 -
            // and it cannot be any more, because Room2's side walls now have DOORWAYS in them. A door
            // is 2.5 tall with its lamp at 2.725..2.835, and the icons in a sign centred at 2.7039 hang
            // down to 2.06: two of the four would have sat across the top of the door. Centred at 3.55
            // the icons run 2.905..4.195, clearing the lamp by 7cm, and only the middle arrow is over
            // the doorway at all. Nothing is lost visually since there is no plate - the rect is
            // invisible and only the row of icons reads.
            const float sideWidth = GridCellWidth * 4f;                  // 7.0, four columns
            const float sideAuthoredHeight = 618f;
            const float sideY = 3.55f;                                   // icons 2.905..4.195

            // 295 fills the authored width exactly - 4 icons plus 3 arrows at the row's own ratio come
            // to 1600 - so the sequence spans all four columns. A four-step row laid out horizontally
            // cannot also be two rows TALL: at 295 the icons are 1.29m against the block's 2.70m, and
            // making them taller would mean fewer than four fitting across. So the row fills the width
            // of the eight panels and sits centred in their height.
            const float sideIcon = 295f;

            // Each canvas's forward (+Z) points INTO its wall: a world-space canvas is legible when its
            // forward matches the direction the viewer is LOOKING, not when it points at the viewer.
            // See MakeFourWallFaces for the full rule, and for why signs are canvases and not textures
            // on the panelling.
            var faces = new[]
            {
                MakeWallFace(root.transform, "West", new Vector3(-halfWidth + standoff, sideY, 0f),
                             Quaternion.Euler(0f, -90f, 0f), f => FillBalloonPictogram(f, sideIcon),
                             sideWidth, withPlate: false, authoredHeight: sideAuthoredHeight),
                MakeWallFace(root.transform, "East", new Vector3(halfWidth - standoff, sideY, 0f),
                             Quaternion.Euler(0f, 90f, 0f), f => FillBalloonPictogram(f, sideIcon),
                             sideWidth, withPlate: false, authoredHeight: sideAuthoredHeight),
            };

            PanelMessage message = root.AddComponent<PanelMessage>();
            message.faces = faces;
            message.roomCenterZ = roomCenterZ;
            message.halfDepth = halfDepth;
            message.retireOnPop = tool;
            return message;
        }

        private static void MakeWallLine(Transform parent, string name, string content, int fontSize,
                                         Color color, Vector2 anchoredPosition, Vector2 size)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);

            Text text = go.AddComponent<Text>();
            text.font = UIFont();
            text.fontSize = fontSize;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = color;
            text.text = content;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;

            RectTransform rect = text.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = anchoredPosition;
        }

        private static Text MakeRowLabel(Transform parent, string name, string content,
                                         Vector2 anchoredPosition, Vector2 size, TextAnchor alignment)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);

            Text text = go.AddComponent<Text>();
            text.font = UIFont();
            text.fontSize = 18;
            text.alignment = alignment;
            text.color = new Color(1f, 0.35f, 0.35f, 0.85f);
            text.text = content;
            // Overflow, so a rect a shade too narrow cannot break the label across two lines - the
            // same reason IterationLabel sets it.
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;

            RectTransform rect = text.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = anchoredPosition;

            return text;
        }

        // A uGUI Slider assembled by hand. Three things about it are not optional, because Slider
        // drives them itself every frame and gets them from the hierarchy rather than from fields:
        // the fill must be the child of a container rect (Slider rewrites the fill's anchors within
        // its parent), the handle likewise, and both must leave their offsets at zero or the value
        // it computes lands somewhere other than where it draws.
        private static Slider MakeSlider(Transform parent, string name, Vector2 anchoredPosition, Vector2 size)
        {
            const float handleWidth = 22f;
            Sprite uiSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");

            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            RectTransform rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = anchoredPosition;

            GameObject bgGO = new GameObject("Background");
            bgGO.transform.SetParent(go.transform, false);
            Image bg = bgGO.AddComponent<Image>();
            bg.sprite = uiSprite;
            bg.type = Image.Type.Sliced;
            bg.color = new Color(0.16f, 0.04f, 0.04f, 0.9f);
            RectTransform bgRect = bg.GetComponent<RectTransform>();
            bgRect.anchorMin = new Vector2(0f, 0.3f);
            bgRect.anchorMax = new Vector2(1f, 0.7f);
            bgRect.offsetMin = Vector2.zero;
            bgRect.offsetMax = Vector2.zero;

            // Inset by half the handle at each end, so the handle's centre reaches the track's ends
            // at 0 and 1 rather than hanging off them.
            GameObject fillAreaGO = new GameObject("Fill Area");
            fillAreaGO.transform.SetParent(go.transform, false);
            RectTransform fillArea = fillAreaGO.AddComponent<RectTransform>();
            fillArea.anchorMin = new Vector2(0f, 0.3f);
            fillArea.anchorMax = new Vector2(1f, 0.7f);
            fillArea.offsetMin = new Vector2(handleWidth / 2f, 0f);
            fillArea.offsetMax = new Vector2(-handleWidth / 2f, 0f);

            GameObject fillGO = new GameObject("Fill");
            fillGO.transform.SetParent(fillAreaGO.transform, false);
            Image fill = fillGO.AddComponent<Image>();
            fill.sprite = uiSprite;
            fill.type = Image.Type.Sliced;
            fill.color = new Color(0.7f, 0.11f, 0.11f, 0.95f);
            RectTransform fillRect = fill.GetComponent<RectTransform>();
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;

            GameObject handleAreaGO = new GameObject("Handle Slide Area");
            handleAreaGO.transform.SetParent(go.transform, false);
            RectTransform handleArea = handleAreaGO.AddComponent<RectTransform>();
            handleArea.anchorMin = new Vector2(0f, 0f);
            handleArea.anchorMax = new Vector2(1f, 1f);
            handleArea.offsetMin = new Vector2(handleWidth / 2f, 0f);
            handleArea.offsetMax = new Vector2(-handleWidth / 2f, 0f);

            GameObject handleGO = new GameObject("Handle");
            handleGO.transform.SetParent(handleAreaGO.transform, false);
            Image handle = handleGO.AddComponent<Image>();
            handle.sprite = uiSprite;
            handle.type = Image.Type.Sliced;
            handle.color = Color.white;
            RectTransform handleRect = handle.GetComponent<RectTransform>();
            handleRect.anchorMin = new Vector2(0f, 0f);
            handleRect.anchorMax = new Vector2(0f, 1f);
            handleRect.pivot = new Vector2(0.5f, 0.5f);
            // Only the width is fixed; a zero height against top-and-bottom anchors is full height.
            handleRect.sizeDelta = new Vector2(handleWidth, 0f);
            handleRect.anchoredPosition = Vector2.zero;

            Slider slider = go.AddComponent<Slider>();
            slider.direction = Slider.Direction.LeftToRight;
            slider.fillRect = fillRect;
            slider.handleRect = handleRect;
            slider.targetGraphic = handle;
            slider.wholeNumbers = false;
            // The range and the starting value are PauseMenu's to set - they belong to GameSettings,
            // and seeding them here would put the same number in two places.

            // Same treatment as the buttons: a white graphic tinted by the ColorBlock, since a
            // multiply against an already-dark handle has nothing left to brighten with.
            ColorBlock colors = slider.colors;
            colors.normalColor = new Color(0.62f, 0.1f, 0.1f, 1f);
            colors.highlightedColor = new Color(0.85f, 0.2f, 0.2f, 1f);
            colors.pressedColor = new Color(1f, 0.35f, 0.35f, 1f);
            colors.selectedColor = colors.highlightedColor;
            colors.fadeDuration = 0.12f;
            slider.colors = colors;

            return slider;
        }

        // The title screen: a camera, a canvas and a still of the room. Deliberately almost
        // nothing, because the whole point of it being a separate scene is that it opens instantly
        // while the room behind it takes a real moment to come in.
        private static void BuildMainMenuScene()
        {
            Scene menu = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            GameObject camGO = new GameObject("MenuCamera");
            camGO.tag = "MainCamera";
            Camera cam = camGO.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            // Black behind everything: the background image is stretched to the screen, but a
            // window shaped nothing like 16:9 should fall away to black rather than to the URP
            // default blue.
            cam.backgroundColor = Color.black;
            camGO.AddComponent<AudioListener>();

            GameObject canvasGO = new GameObject("Canvas");
            Canvas canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            canvasGO.AddComponent<GraphicRaycaster>();

            // The captured room, and a scrim over it. The room is near-white and the HUD's red
            // would fight it head-on; the scrim also puts the picture behind the title rather than
            // beside it, which is what a background is for.
            GameObject backgroundGO = new GameObject("Background");
            backgroundGO.transform.SetParent(canvasGO.transform, false);
            Image background = backgroundGO.AddComponent<Image>();
            Sprite shot = AssetDatabase.LoadAssetAtPath<Sprite>(MenuBackgroundPath);
            background.sprite = shot;
            // A missing capture leaves a deliberate dark panel rather than uGUI's default white
            // box, so a build without a graphics device still produces a menu that looks intended.
            background.color = shot != null ? Color.white : new Color(0.06f, 0.06f, 0.07f, 1f);
            background.raycastTarget = false;
            Stretch(background.GetComponent<RectTransform>());

            GameObject scrimGO = new GameObject("Scrim");
            scrimGO.transform.SetParent(canvasGO.transform, false);
            Image scrim = scrimGO.AddComponent<Image>();
            scrim.color = new Color(0f, 0f, 0f, 0.5f);
            scrim.raycastTarget = false;
            Stretch(scrim.GetComponent<RectTransform>());

            GameObject menuGO = new GameObject("Menu");
            menuGO.transform.SetParent(canvasGO.transform, false);
            CanvasGroup menuGroup = menuGO.AddComponent<CanvasGroup>();
            Stretch(menuGO.AddComponent<RectTransform>());

            GameObject titleGO = new GameObject("Title");
            titleGO.transform.SetParent(menuGO.transform, false);
            Text title = titleGO.AddComponent<Text>();
            title.font = UIFont();
            title.fontSize = 86;
            title.alignment = TextAnchor.MiddleCenter;
            title.color = Color.red;
            // Spaced out in the string, exactly as IterationLabel does it and for the same reason:
            // uGUI's Text has no tracking control at all, and in a monospace face a space is one
            // cell. The in-game label and the title then read as the same typeface doing the same
            // thing, which is the point - both are the facility talking.
            title.text = "I T E R A T I O N";
            title.horizontalOverflow = HorizontalWrapMode.Overflow;
            title.verticalOverflow = VerticalWrapMode.Overflow;
            title.raycastTarget = false;
            RectTransform titleRect = title.GetComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0.5f, 0.5f);
            titleRect.anchorMax = new Vector2(0.5f, 0.5f);
            titleRect.sizeDelta = new Vector2(1400f, 140f);
            titleRect.anchoredPosition = new Vector2(0f, 190f);

            Button playButton = MakeMenuButton(menuGO.transform, "PlayButton", "PLAY", new Vector2(0f, -30f));
            // A DEVELOPMENT SHORTCUT, and labelled loudly enough that it cannot be mistaken for
            // content. Drops straight into the cycle boundary with cycle 1 already finished, which
            // is otherwise about eight minutes of play away - see DebugStart.
            //
            // It sits between PLAY and QUIT rather than at the bottom because it is pressed far more
            // often than QUIT during development, and it is click-only: Return starts the real game,
            // and the key somebody presses to begin playing must never reach this.
            Button continueButton = MakeMenuButton(menuGO.transform, "ContinueButton",
                                                   "CONTINUE", new Vector2(0f, -118f));
            Button testButton = MakeMenuButton(menuGO.transform, "TestBoundaryButton",
                                               "TEST: CYCLE BOUNDARY", new Vector2(0f, -206f));
            Button quitButton = MakeMenuButton(menuGO.transform, "QuitButton", "QUIT", new Vector2(0f, -294f));

            // THE CYCLE PICKER, on a page of its own over the same background. A title screen that
            // grows a row every time the game grows a cycle stops being a title screen.
            GameObject continueGO = new GameObject("Continue");
            continueGO.transform.SetParent(canvasGO.transform, false);
            CanvasGroup continueGroup = continueGO.AddComponent<CanvasGroup>();
            continueGroup.alpha = 0f;
            continueGroup.blocksRaycasts = false;
            Stretch(continueGO.AddComponent<RectTransform>());

            // One per cycle. The count is read from the cycle list rather than written here, so
            // adding a cycle adds its button - the same rule LoopManager follows for deciding which
            // cycle is the last.
            var cycleButtons = new Button[CycleCount];
            for (int i = 0; i < CycleCount; i++)
                cycleButtons[i] = MakeMenuButton(continueGO.transform, $"CycleButton_{i + 1}",
                                                 $"CYCLE {i + 1}", new Vector2(0f, -30f - i * 88f));

            Button continueBack = MakeMenuButton(continueGO.transform, "ContinueBackButton",
                                                 "BACK", new Vector2(0f, -30f - CycleCount * 88f));

            // The loading state, built over the same middle of the screen the buttons occupy so
            // one replaces the other in place instead of the eye having to travel.
            GameObject loadingGO = new GameObject("Loading");
            loadingGO.transform.SetParent(canvasGO.transform, false);
            CanvasGroup loadingGroup = loadingGO.AddComponent<CanvasGroup>();
            loadingGroup.alpha = 0f;
            loadingGroup.blocksRaycasts = false;
            Stretch(loadingGO.AddComponent<RectTransform>());

            GameObject loadingLabelGO = new GameObject("Label");
            loadingLabelGO.transform.SetParent(loadingGO.transform, false);
            Text loadingLabel = loadingLabelGO.AddComponent<Text>();
            loadingLabel.font = UIFont();
            loadingLabel.fontSize = 22;
            loadingLabel.alignment = TextAnchor.MiddleCenter;
            loadingLabel.color = Color.red;
            loadingLabel.text = "LOADING 0%";
            loadingLabel.raycastTarget = false;
            RectTransform loadingLabelRect = loadingLabel.GetComponent<RectTransform>();
            loadingLabelRect.anchorMin = new Vector2(0.5f, 0.5f);
            loadingLabelRect.anchorMax = new Vector2(0.5f, 0.5f);
            loadingLabelRect.sizeDelta = new Vector2(600f, 40f);
            loadingLabelRect.anchoredPosition = new Vector2(0f, -20f);

            GameObject barGO = new GameObject("Bar");
            barGO.transform.SetParent(loadingGO.transform, false);
            Image bar = barGO.AddComponent<Image>();
            bar.color = new Color(0f, 0f, 0f, 0.6f);
            bar.raycastTarget = false;
            RectTransform barRect = bar.GetComponent<RectTransform>();
            barRect.anchorMin = new Vector2(0.5f, 0.5f);
            barRect.anchorMax = new Vector2(0.5f, 0.5f);
            barRect.sizeDelta = new Vector2(560f, 8f);
            barRect.anchoredPosition = new Vector2(0f, -60f);

            GameObject fillGO = new GameObject("Fill");
            fillGO.transform.SetParent(barGO.transform, false);
            Image fill = fillGO.AddComponent<Image>();
            // A filled Image needs a sprite to have anything to fill; this is uGUI's own.
            fill.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillAmount = 0f;
            fill.color = Color.red;
            fill.raycastTarget = false;
            Stretch(fill.GetComponent<RectTransform>());

            // Attribution, bottom centre. Small and dim: it has to be present and it must not
            // compete with the two buttons. This is the only place it appears - a build that hands
            // credit somewhere the player has to go looking is not really handing it over.
            GameObject creditsGO = new GameObject("Credits");
            creditsGO.transform.SetParent(canvasGO.transform, false);
            Text credits = creditsGO.AddComponent<Text>();
            credits.font = UIFont();
            credits.fontSize = 15;
            credits.alignment = TextAnchor.LowerCenter;
            credits.color = new Color(1f, 1f, 1f, 0.42f);
            credits.text = CreditsLine;
            credits.horizontalOverflow = HorizontalWrapMode.Overflow;
            credits.raycastTarget = false;
            RectTransform creditsRect = credits.GetComponent<RectTransform>();
            creditsRect.anchorMin = new Vector2(0.5f, 0f);
            creditsRect.anchorMax = new Vector2(0.5f, 0f);
            creditsRect.pivot = new Vector2(0.5f, 0f);
            creditsRect.sizeDelta = new Vector2(1600f, 30f);
            creditsRect.anchoredPosition = new Vector2(0f, 22f);

            MainMenu mainMenu = canvasGO.AddComponent<MainMenu>();
            mainMenu.menuGroup = menuGroup;
            mainMenu.loadingGroup = loadingGroup;
            mainMenu.playButton = playButton;
            mainMenu.quitButton = quitButton;
            mainMenu.testBoundaryButton = testButton;
            mainMenu.continueButton = continueButton;
            mainMenu.continueBackButton = continueBack;
            mainMenu.continueGroup = continueGroup;
            mainMenu.cycleButtons = cycleButtons;
            mainMenu.loadingFill = fill;
            mainMenu.loadingLabel = loadingLabel;

            // uGUI buttons are inert without one, and an empty scene has nothing at all in it.
            GameObject eventSystem = new GameObject("EventSystem");
            eventSystem.AddComponent<UnityEngine.EventSystems.EventSystem>();
            eventSystem.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();

            EditorSceneManager.SaveScene(menu, MenuScenePath);
        }

        // The step between PLAY and the load: move the mouse, watch the room turn, set the number
        // before the clock has ever started. See SensitivityCalibration for why the pointer is
        // captured here and why that forces the wheel and the arrow keys instead of a dragged
        // slider - the pause menu's slider stays as the way to change it later.
        //
        // Laid out around the exact middle of the screen, which is where the buttons were: the page
        // this replaces and the page it becomes occupy the same space, so nothing has to travel.
        // The sensitivity step, built on the ROOM's canvas rather than the menu's - the player sets
        // it while standing in the finished room looking through the real camera. See
        // SensitivityCalibration for the two attempts that came before and why neither worked.
        //
        // The readout sits on its own dark plate rather than behind a full-screen scrim, so the
        // room stays at full brightness behind it. The thing being judged is how the room moves;
        // dimming it to make the text legible would be dimming the subject.
        private static SensitivityCalibration BuildCalibrationPage(Transform canvas)
        {
            GameObject root = new GameObject("Calibration");
            root.transform.SetParent(canvas, false);
            CanvasGroup group = root.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = false;
            Stretch(root.AddComponent<RectTransform>());

            // Almost nothing is drawn here any more: the control list, the gauge and the value
            // all moved onto the calibration room's own south wall, which is the wall the player
            // spawns facing. What is left on the SCREEN is the one line that has to follow the eye
            // wherever it goes, because it is how the player leaves.
            //
            // No plate behind it. The line is empty for all of a normal run - it only appears
            // when the browser has refused pointer capture - and a plate under an empty string is
            // a black bar across the bottom of the screen for no reason.
            Text lockHint = MakeMenuLine(root.transform, "LockHint", "[ENTER]  TO BEGIN", 24,
                Color.red, new Vector2(0f, -400f), new Vector2(1200f, 40f));

            SensitivityCalibration calibration = root.AddComponent<SensitivityCalibration>();
            calibration.group = group;
            calibration.lockHint = lockHint;

            // Everything else on the canvas goes away while this is up. The countdown reading 1:00
            // and a dimmed END CYCLE control are describing a loop that has not started, and the
            // player is being asked to judge how the ROOM moves - anything else on screen is
            // something for the eye to land on instead.
            //
            // Expressed as "all but these two" rather than as a list of what to hide, so a HUD
            // element added later is covered without anyone remembering to come back here. PauseMenu
            // stays live because a player who wants out must not be trapped by a settings screen.
            var hidden = new System.Collections.Generic.List<GameObject>();
            foreach (Transform child in canvas)
            {
                // ControlHints stays on as well as PauseMenu: the start button on the wall is an
                // IInteractHintTarget, so the E disc has to be able to appear over it.
                if (child == root.transform || child.name == "PauseMenu" || child.name == "ControlHints") continue;
                hidden.Add(child.gameObject);
            }
            calibration.hideWhileActive = hidden.ToArray();

            return calibration;
        }

        // The calibration room's south wall - the one the player spawns facing - carrying the
        // control list, the sensitivity gauge and the value, plus two physical plates below it that
        // raise and lower the setting.
        //
        // On the wall rather than on the screen because the room is built out of displays, so the
        // facility explaining itself on one is the same move Room3 makes. It also puts the readout
        // and the control that drives it in the same place: the player presses E at a plate and
        // watches the number above it move, which no screen overlay can do.
        //
        // The plates carry a second job. They are the only thing in the room E can be pressed at,
        // so the control list above can name E and have it be true in the same room. A practice
        // ball existed for exactly that and was deleted once these took the work over - one fixture
        // doing two jobs beats two doing one each.
        private static CalibrationStartButton BuildCalibrationWall(Transform parent, float roomCenterZ,
                                                                   SensitivityCalibration calibration)
        {
            GameObject root = new GameObject("CalibrationWall");
            root.transform.SetParent(parent, false);

            float wallZ = roomCenterZ - RoomDepth / 2f;   // inner face of the south wall

            // --- the display ---
            GameObject faceGO = new GameObject("Display");
            faceGO.transform.SetParent(root.transform, false);

            Canvas canvas = faceGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;

            RectTransform rect = faceGO.GetComponent<RectTransform>();
            // Taller than it was, because the plate that used to bound it is gone. Nothing physical
            // sits under this on the south wall, so the height is free.
            rect.sizeDelta = new Vector2(1600f, 820f);
            rect.localScale = Vector3.one * 0.004f;       // -> 6.4m x 3.28m on an 8.75m x 5.41m wall
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            // Set through anchoredPosition3D and AFTER the Canvas exists - see MakeWallFace for the
            // two traps in that sentence.
            rect.anchoredPosition3D = new Vector3(0f, 3.05f, wallZ + 0.05f);
            // Forward points INTO the wall: a world-space canvas is legible when its forward matches
            // the direction the viewer is LOOKING, and a player in this room looks south at it.
            rect.localRotation = Quaternion.Euler(0f, 180f, 0f);

            CanvasGroup wallGroup = faceGO.AddComponent<CanvasGroup>();
            wallGroup.alpha = 0f;
            wallGroup.blocksRaycasts = false;
            wallGroup.interactable = false;

            // NO PLATE. This used to sit on a lit near-black slab, the same one Room3's message uses,
            // and it was removed for the reason the Room2 pictogram's was: a plate is the facility
            // interrupting, which suits a sign that stops an iteration and does not suit the room
            // quietly labelling its own controls. Stencilled on the panelling, it belongs to the wall.
            //
            // Two consequences, both handled below. Everything got BIGGER, because a plate was the only
            // thing making the old sizes feel filled - on bare wall the same glyphs read as small. And
            // the colours changed: the old red-on-black values were chosen against a dark ground and
            // wash out on white panelling.

            // --- controls, upper half ---
            // Sized in canvas units; at 0.004 scale an 84px cap is 0.34m of wall, up from 0.25m.
            const float key = 84f, gap = 9f, step = key + gap, capGap = 30f;
            const float keysW = 3f * key + 2f * gap;   // 270
            // Charcoal, not the old pale red: on white panelling a light red is barely there, and this
            // is the same ink the Room2 sign uses, so the game's two wordless displays match. Well
            // clear of GrooveDark (0.04) so a glyph cannot be mistaken for a seam.
            Color wallText = new Color(0.20f, 0.20f, 0.23f, 0.78f);

            const float keysX = -430f;
            // Spaced from the edges of what sits on each row, not by a uniform pitch: the W/A/S/D
            // block is two caps deep and straddles its row where SPACE is a single 44px bar. A
            // uniform pitch is what put the bar inside the A/S/D row twice on the screen version.
            // Spaced from the bottom edge of what sits on the row above, not by a uniform pitch: the
            // W/A/S/D block is two caps deep and straddles its row where SPACE is a single bar. A
            // uniform pitch is what put the bar inside the A/S/D row twice on the screen version.
            const float rowMove = 300f;          // W 304.5..388.5, A/S/D 211.5..295.5
            const float rowJump = 162.5f;        // SPACE 133.5..191.5, so 20 clear of A/S/D
            const float rowInteract = 71.5f;     // E 29.5..113.5, so 20 clear of SPACE

            MakeKeyCap(faceGO.transform, "KeyW", "W", new Vector2(keysX, rowMove + step / 2f), new Vector2(key, key), 36);
            MakeKeyCap(faceGO.transform, "KeyA", "A", new Vector2(keysX - step, rowMove - step / 2f), new Vector2(key, key), 36);
            MakeKeyCap(faceGO.transform, "KeyS", "S", new Vector2(keysX, rowMove - step / 2f), new Vector2(key, key), 36);
            MakeKeyCap(faceGO.transform, "KeyD", "D", new Vector2(keysX + step, rowMove - step / 2f), new Vector2(key, key), 36);
            MakeKeyCap(faceGO.transform, "KeySpace", "SPACE", new Vector2(keysX, rowJump), new Vector2(keysW, 58f), 26);
            MakeKeyCap(faceGO.transform, "KeyE", "E", new Vector2(keysX, rowInteract), new Vector2(key, key), 36);

            // FIGURES INSTEAD OF WORDS. The room says nothing else in English, and this wall is the
            // first thing a player sees - so the one screen that has to be understood before the game
            // starts is the worst place to require reading. Same treatment as the Room2 sign.
            //
            // 100 against the keycaps' 84, because a figure needs more room than a letter to read at
            // all: the glyph is a whole body where a cap is one character. Centred on the row rather
            // than left-aligned as the old captions were, so both columns line up as a grid whatever
            // each row's key happens to be.
            const float figure = 100f;
            const float keyFigureX = keysX + keysW / 2f + capGap + figure / 2f;

            MakeWallIcon(faceGO.transform, "MoveFigure", FigureWalkIcon(), new Vector2(keyFigureX, rowMove), figure, wallText);
            MakeWallIcon(faceGO.transform, "JumpFigure", FigureJumpIcon(), new Vector2(keyFigureX, rowJump), figure, wallText);
            MakeWallIcon(faceGO.transform, "InteractFigure", FigurePressIcon(), new Vector2(keyFigureX, rowInteract), figure, wallText);

            // The right column is the mouse alone. Sprint and crouch were briefly here and moved to the
            // side walls: they are the two controls that are about HOW you cross a room, and putting
            // them on the walls you cross between says that better than a fifth row can.
            const float glyph = 104f;
            const float glyphX = 300f;
            // Level with the middle of the keyboard block, which spans 29.5 to 388.5.
            const float rowLook = 209f;
            const float rightFigureX = glyphX + glyph / 2f + capGap + figure / 2f;

            GameObject mouseGO = new GameObject("MouseGlyph");
            mouseGO.transform.SetParent(faceGO.transform, false);
            Image mouse = mouseGO.AddComponent<Image>();
            mouse.sprite = MouseIcon();
            mouse.color = wallText;
            mouse.raycastTarget = false;
            mouse.preserveAspect = true;
            RectTransform mouseRect = mouse.GetComponent<RectTransform>();
            mouseRect.anchorMin = new Vector2(0.5f, 0.5f);
            mouseRect.anchorMax = new Vector2(0.5f, 0.5f);
            mouseRect.sizeDelta = new Vector2(glyph, glyph);
            mouseRect.anchoredPosition = new Vector2(glyphX, rowLook);

            MakeWallIcon(faceGO.transform, "LookFigure", FigureLookIcon(), new Vector2(rightFigureX, rowLook), figure, wallText);

            // ~~TAB, level with E~~ REMOVED with Tab itself, 2026-08-13. The hand holds one object
            // and E is the whole of handling it: press to pick up, press again to put down. The
            // second half of that is taught by the HUD's own prompt under the carried icon, where it
            // can appear at the moment there is something to put down - which a wall in the first
            // room cannot do.

            // --- sensitivity, lower half ---
            // The reds stay red - this is the facility's own voice and red on white panelling is the
            // strongest thing in the room - but everything that WAS a pale red on black had to darken,
            // because pale red on white is barely a mark.
            Color wallRed = new Color(0.74f, 0.09f, 0.09f, 1f);

            MakeMenuLine(faceGO.transform, "Headline", "M O U S E   S E N S I T I V I T Y", 48,
                wallRed, new Vector2(0f, -40f), new Vector2(1500f, 70f));

            Text value = MakeMenuLine(faceGO.transform, "Value", "1.10", 56, wallRed,
                new Vector2(0f, -130f), new Vector2(500f, 70f));

            GameObject barGO = new GameObject("Gauge");
            barGO.transform.SetParent(faceGO.transform, false);
            Image bar = barGO.AddComponent<Image>();
            // The empty track. It was near-opaque black, which on a dark plate was a recess and on white
            // panelling would be a solid bar with a red one inside it. Light enough now to read as the
            // groove the fill sits in.
            bar.color = new Color(0.18f, 0.18f, 0.20f, 0.30f);
            bar.raycastTarget = false;
            RectTransform barRect = bar.GetComponent<RectTransform>();
            barRect.anchorMin = new Vector2(0.5f, 0.5f);
            barRect.anchorMax = new Vector2(0.5f, 0.5f);
            barRect.sizeDelta = new Vector2(950f, 28f);
            barRect.anchoredPosition = new Vector2(0f, -200f);

            GameObject fillGO = new GameObject("Fill");
            fillGO.transform.SetParent(barGO.transform, false);
            Image fill = fillGO.AddComponent<Image>();
            fill.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillAmount = 0.5f;
            fill.color = wallRed;
            fill.raycastTarget = false;
            Stretch(fill.GetComponent<RectTransform>());

            MakeMenuLine(faceGO.transform, "AdjustHint", "SCROLL TO ADJUST", 30,
                new Color(0.62f, 0.10f, 0.10f, 0.85f), new Vector2(0f, -250f), new Vector2(1400f, 44f));
            MakeMenuLine(faceGO.transform, "BeginHint", "PRESS [E] AT THE PANEL BEHIND YOU", 34,
                wallRed, new Vector2(0f, -320f), new Vector2(1400f, 50f));

            // The two side walls: one control each, and nothing else on them.
            CanvasGroup sprintWall = MakeCalibrationSideWall(root.transform, "SprintWall", roomCenterZ,
                west: true, keyLabel: "SHIFT", figureSprite: FigureRunIcon(), ink: wallText);
            CanvasGroup crouchWall = MakeCalibrationSideWall(root.transform, "CrouchWall", roomCenterZ,
                west: false, keyLabel: "CTRL", figureSprite: FigureCrouchIcon(), ink: wallText);

            calibration.wallGroups = new[] { wallGroup, sprintWall, crouchWall };
            calibration.fill = fill;
            calibration.valueLabel = value;

            // --- the start button, on the OPPOSITE wall ---
            // The display is on the south wall and this is on the north, six metres behind the
            // player's back. That separation is the point: reading the panel and reaching the way
            // out costs a full 180 turn and a walk, which is exactly the thing the step is asking
            // the player to judge. Adjust at the display, turn, feel it, press - and if it was
            // wrong, turn back.
            //
            // It is one CELL OF THE WALL GRID rather than a box stuck on the wall: panel-sized,
            // sat 15mm proud, dark where its neighbours are white. The room's fiction is that the
            // panels are displays, so a single cell lit up and asking to be pressed is the room
            // speaking its own language - where a green slab was an object from another game.
            Material plateMat = MakeColorMaterial("CalibrationPlate", new Color(0.05f, 0.05f, 0.055f));
            // The keyword has to be compiled in for the press flash; a property block cannot turn a
            // shader keyword on. Black means it contributes nothing until E is pressed.
            plateMat.EnableKeyword("_EMISSION");
            plateMat.SetColor("_EmissionColor", Color.black);
            plateMat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            EditorUtility.SetDirty(plateMat);

            const float panelW = GridCellWidth - GridLineThickness;    // 1.70, the visible face
            const float panelH = GridCellHeight - GridLineThickness;   // 1.3019
            const float proud = 0.03f;
            // Column 2 of 5 and row 1 of 4 on an end wall, i.e. dead centre horizontally at the
            // second row up. Cell centres are (i + 0.5) * cell, so this is 2.028m - head height,
            // which is where a wall panel worth reading belongs. Reach is a horizontal test, so
            // the height costs nothing.
            float buttonY = 1.5f * GridCellHeight;
            float northWallZ = roomCenterZ + RoomDepth / 2f;

            GameObject buttonRoot = new GameObject("StartButton");
            buttonRoot.transform.SetParent(root.transform, false);
            buttonRoot.transform.localPosition = new Vector3(0f, buttonY, northWallZ - proud / 2f);

            GameObject visual = Prim(PrimitiveType.Cube, "Visual", buttonRoot.transform, Vector3.zero,
                new Vector3(panelW, panelH, proud), plateMat, removeCollider: true);

            // The word, on the cell itself. Same treatment as every other display in the project:
            // spaced caps in the string, since uGUI has no tracking control and a space is one cell
            // of a monospace face.
            GameObject labelGO = new GameObject("Label");
            labelGO.transform.SetParent(buttonRoot.transform, false);
            Canvas labelCanvas = labelGO.AddComponent<Canvas>();
            labelCanvas.renderMode = RenderMode.WorldSpace;

            RectTransform labelRect = labelGO.GetComponent<RectTransform>();
            labelRect.sizeDelta = new Vector2(850f, 650f);
            labelRect.localScale = Vector3.one * 0.002f;               // -> 1.70m x 1.30m
            labelRect.anchorMin = new Vector2(0.5f, 0.5f);
            labelRect.anchorMax = new Vector2(0.5f, 0.5f);
            labelRect.pivot = new Vector2(0.5f, 0.5f);
            // anchoredPosition3D and AFTER the Canvas - see MakeWallFace for both traps.
            labelRect.anchoredPosition3D = new Vector3(0f, 0f, -(proud / 2f + 0.004f));
            // Identity: this wall faces north, so a player reading it is looking along +Z, and a
            // world-space canvas is legible when its forward matches the viewer's look direction.
            labelRect.localRotation = Quaternion.identity;

            // The speaker, above the word rather than beside it - this is the room's last screen
            // before the clock starts, and the PA and every room's audio cues are half of what the
            // facility tells the player from here on. Red, the same voice BEGIN speaks in, because
            // this is the facility's own instruction and not a HUD element laid over the room.
            MakeWallIcon(labelGO.transform, "VolumeHint", VolumeIcon(), new Vector2(0f, 195f), 160f, Color.red);

            MakeMenuLine(labelGO.transform, "Text", "B E G I N", 150, Color.red,
                Vector2.zero, new Vector2(850f, 220f));

            CalibrationStartButton button = buttonRoot.AddComponent<CalibrationStartButton>();
            button.calibration = calibration;
            button.buttonRenderer = visual.GetComponent<Renderer>();
            button.audioSource = MakeSource(buttonRoot.transform, "ButtonAudio", 1f, 0.8f);
            button.pressClip = LoadClip(SfxDir, "sfx_floor_button_press");
            return button;
        }

        private static RectTransform MakePlate(Transform parent, string name, Vector2 anchoredPosition, Vector2 size)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);

            Image plate = go.AddComponent<Image>();
            // GrooveDark, the same near-black that sits at the bottom of every groove in the room -
            // so a panel of UI reads as part of the facility rather than as an overlay.
            plate.color = new Color(0.04f, 0.04f, 0.045f, 0.82f);
            plate.raycastTarget = false;

            RectTransform rect = plate.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = anchoredPosition;
            return rect;
        }

        // A key cap: a red rounded rect with a near-black one inset inside it, which is a border
        // without needing a border sprite. uGUI's own UISprite is 9-sliced, so one sprite gives
        // every size of cap the same corner radius - a square W and a wide SPACE bar included.
        // One of the calibration room's side walls: a single key and the figure it produces, and
        // nothing else on the whole wall.
        //
        // Sprint and crouch went here rather than staying as two more rows on the south wall, and the
        // reason is what they are: the other four controls are things you do in a place, where these
        // two are how you CROSS one. Putting them on the walls the player crosses between - and one
        // each, so a wall means a control - says that better than a fifth and sixth row can. It also
        // means the player meets them by turning their head, which is the thing this room exists to
        // teach them to do.
        //
        // Bigger than anything on the south wall: it is one control on 10.5m of wall, so the sizes that
        // made a dense list legible would look like a stamp in the corner here.
        private static CanvasGroup MakeCalibrationSideWall(Transform parent, string name, float roomCenterZ,
                                                          bool west, string keyLabel, Sprite figureSprite,
                                                          Color ink)
        {
            const float standoff = 0.05f;
            const float y = 2.4f;
            const float worldWidth = 4.6f;

            float x = (west ? -1f : 1f) * (RoomWidth / 2f - standoff);
            // Forward INTO the wall, which for a side wall means a quarter turn. A world-space canvas is
            // legible when its forward matches the direction the viewer is LOOKING - see
            // MakeFourWallFaces - and a player in this room looking at the west wall is looking -X.
            Quaternion rotation = Quaternion.Euler(0f, west ? -90f : 90f, 0f);

            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);

            Canvas canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;

            RectTransform rect = go.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(1000f, 620f);
            rect.localScale = Vector3.one * (worldWidth / 1000f);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition3D = new Vector3(x, y, roomCenterZ);
            rect.localRotation = rotation;

            CanvasGroup group = go.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = false;
            group.interactable = false;

            // Key above, figure below, both centred - a column rather than the south wall's rows,
            // because one pair has no list to line up with and centring it owns the wall.
            MakeKeyCap(go.transform, "Key", keyLabel, new Vector2(0f, 150f), new Vector2(460f, 120f), 54);
            MakeWallIcon(go.transform, "Figure", figureSprite, new Vector2(0f, -110f), 300f, ink);
            return group;
        }

        private static void MakeKeyCap(Transform parent, string name, string label,
                                       Vector2 anchoredPosition, Vector2 size, int fontSize = 26)
        {
            Sprite rounded = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");

            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);

            Image outer = go.AddComponent<Image>();
            outer.sprite = rounded;
            outer.type = Image.Type.Sliced;
            outer.color = new Color(0.75f, 0.14f, 0.14f, 0.95f);
            outer.raycastTarget = false;
            RectTransform rect = outer.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = anchoredPosition;

            GameObject faceGO = new GameObject("Face");
            faceGO.transform.SetParent(go.transform, false);
            Image face = faceGO.AddComponent<Image>();
            face.sprite = rounded;
            face.type = Image.Type.Sliced;
            face.color = new Color(0.06f, 0.05f, 0.06f, 0.98f);
            face.raycastTarget = false;
            RectTransform faceRect = face.GetComponent<RectTransform>();
            faceRect.anchorMin = Vector2.zero;
            faceRect.anchorMax = Vector2.one;
            // Inset on all four sides, so the outer colour shows as an even rim at any cap size.
            faceRect.offsetMin = new Vector2(3f, 3f);
            faceRect.offsetMax = new Vector2(-3f, -3f);

            GameObject textGO = new GameObject("Label");
            textGO.transform.SetParent(go.transform, false);
            Text text = textGO.AddComponent<Text>();
            text.font = UIFont();
            text.fontSize = fontSize;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = new Color(1f, 0.45f, 0.45f, 1f);
            text.text = label;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            Stretch(text.GetComponent<RectTransform>());
        }

        // The word beside a key or a glyph. Takes the x its text should START at, since every
        // caption in a column has to begin at the same place whatever its length - the rect is
        // centred, so the offset below is half its width. 190 fits the longest of these
        // ("LOOK AROUND", 11 monospace cells) at either size it is used in.
        //
        // The colour is a parameter because these appear on two very different backgrounds: the
        // wall display puts a near-black plate under them and wants the HUD's light red, where
        // anything drawn straight onto the room's white panelling needs a much darker one.
        private static void MakeCaption(Transform parent, string name, string content, Vector2 leftEdge,
                                        Color? color = null, int fontSize = 22) =>
            MakeMenuLine(parent, name, content, fontSize, color ?? new Color(0.7f, 0.06f, 0.06f, 1f),
                leftEdge + new Vector2(95f, 0f), new Vector2(190f, 30f), TextAnchor.MiddleLeft);

        private static Text MakeMenuLine(Transform parent, string name, string content, int fontSize,
                                         Color color, Vector2 anchoredPosition, Vector2 size,
                                         TextAnchor alignment = TextAnchor.MiddleCenter)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);

            Text text = go.AddComponent<Text>();
            text.font = UIFont();
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = color;
            text.text = content;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;

            RectTransform rect = text.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = anchoredPosition;

            return text;
        }

        private static bool InCalibrationRoom(Transform t)
        {
            for (Transform p = t; p != null; p = p.parent)
                if (p.name == CalibrationRoomName) return true;
            return false;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static Button MakeMenuButton(Transform parent, string name, string label, Vector2 anchoredPosition)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);

            RectTransform rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(340f, 66f);
            rect.anchoredPosition = anchoredPosition;

            // White, with the dark look coming entirely from the ColorBlock below: a Button tints
            // its target graphic by multiplying, so a background that is already near-black has
            // nothing left to brighten with on hover.
            Image background = go.AddComponent<Image>();
            background.color = Color.white;

            GameObject textGO = new GameObject("Label");
            textGO.transform.SetParent(go.transform, false);
            Text text = textGO.AddComponent<Text>();
            text.font = UIFont();
            text.fontSize = 26;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.red;
            text.text = label;
            text.raycastTarget = false;
            Stretch(text.GetComponent<RectTransform>());

            Button button = go.AddComponent<Button>();
            button.targetGraphic = background;

            ColorBlock colors = button.colors;
            colors.normalColor = new Color(0f, 0f, 0f, 0.55f);
            colors.highlightedColor = new Color(0.34f, 0.04f, 0.04f, 0.8f);
            colors.pressedColor = new Color(0.6f, 0.08f, 0.08f, 0.9f);
            colors.selectedColor = colors.highlightedColor;
            colors.disabledColor = new Color(0f, 0f, 0f, 0.3f);
            colors.fadeDuration = 0.12f;
            button.colors = colors;

            return button;
        }

        // Two black panels that meet in the middle. They're driven through their anchors at
        // runtime, so the sizes set here don't matter - only that they exist and are full-width.
        private static WakeUpSequence BuildEyelids(Transform canvasParent)
        {
            GameObject root = new GameObject("Eyelids");
            root.transform.SetParent(canvasParent, false);
            RectTransform rootRect = root.AddComponent<RectTransform>();
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;

            RectTransform top = MakeEyelid(root.transform, "EyelidTop");
            RectTransform bottom = MakeEyelid(root.transform, "EyelidBottom");

            WakeUpSequence wakeUp = root.AddComponent<WakeUpSequence>();
            wakeUp.topLid = top;
            wakeUp.bottomLid = bottom;
            return wakeUp;
        }

        private static RectTransform MakeEyelid(Transform parent, string name)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);

            Image image = go.AddComponent<Image>();
            image.color = Color.black;
            image.raycastTarget = false;

            RectTransform rect = go.GetComponent<RectTransform>();
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return rect;
        }

        private static void BuildCountdownTimer(Transform canvasParent)
        {
            GameObject go = new GameObject("CountdownTimer");
            go.transform.SetParent(canvasParent, false);

            RectTransform rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.sizeDelta = new Vector2(160f, 50f);
            rect.anchoredPosition = new Vector2(-20f, -20f);

            Text text = go.AddComponent<Text>();
            text.font = UIFont();
            text.fontSize = 32;
            text.alignment = TextAnchor.UpperRight;
            text.color = Color.red;

            CountdownTimer timer = go.AddComponent<CountdownTimer>();
            timer.label = text;
        }

        // The end-cycle control, immediately left of the countdown. Built last so it draws over the
        // eyelids rather than under them.
        // THE GAS THAT ENDS A CYCLE. A full-screen wash and a valve, and nothing that warns.
        //
        // A screen wash rather than a particle system, and that is a considered choice rather than a
        // shortcut: gas the player is inside is not gas they can look at. Volumetric fog in the room
        // would be something happening over there, and the beat is that it is happening to them.
        // Sitting on the HUD canvas also means it is drawn OVER the eyelids' parent, so the wash and
        // the blink stack correctly.
        //
        // 2D audio for the same reason. The player has no idea where the vents are and is not meant
        // to - a positioned hiss invites them to turn and look for it.
        private static void BuildSleepingGas(Transform canvasParent)
        {
            GameObject go = new GameObject("SleepingGas");
            go.transform.SetParent(canvasParent, false);

            Image haze = go.AddComponent<Image>();
            // Authored fully transparent. SleepingGas.Administer writes the alpha, and Clear puts it
            // back - a haze left up would open the next cycle behind a white sheet.
            haze.color = new Color(0.92f, 0.94f, 0.96f, 0f);
            // The wash must never eat a click. Nothing under it is interactive at a boundary, but a
            // full-screen Image defaults to raycast target and this is exactly the kind of invisible
            // blocker that is impossible to find later.
            haze.raycastTarget = false;

            RectTransform rect = haze.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            SleepingGas gas = go.AddComponent<SleepingGas>();
            gas.haze = haze;
            gas.audioSource = MakeSource(go.transform, "GasAudio", spatialBlend: 0f, volume: 0.7f);
            gas.hissClip = LoadClip(SfxDir, "sfx_gas_hiss");
        }

        private static void BuildEndCycleControl(Transform canvasParent)
        {
            GameObject go = new GameObject("EndCycleControl");
            go.transform.SetParent(canvasParent, false);

            RectTransform rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            // Wide enough for the label to fit on one line, which it did not: "HOLD [N] - END
            // CYCLE" is 20 monospace cells, i.e. ~192px at fontSize 16, inside a box that was 168
            // wide - so uGUI wrapped it onto two cramped lines and it read as decoration next to
            // the countdown rather than as a control. That is very likely why testers asked for a
            // skip button that has existed since the first build.
            rect.sizeDelta = new Vector2(224f, 40f);
            // The countdown is 160 wide, inset 20 from the corner, so it ends 180 in.
            rect.anchoredPosition = new Vector2(-192f, -20f);

            // Fades out while the clock isn't running, so the control reads as unavailable rather
            // than broken during the wake-up.
            CanvasGroup group = go.AddComponent<CanvasGroup>();

            Image background = go.AddComponent<Image>();
            background.color = new Color(0f, 0f, 0f, 0.55f);

            // The hold gauge, drawn between the background and the label so it fills behind the
            // text rather than over it.
            GameObject fillGO = new GameObject("Fill");
            fillGO.transform.SetParent(go.transform, false);
            Image fill = fillGO.AddComponent<Image>();
            // A filled Image needs a sprite to have anything to fill; the built-in UI sprite is the
            // one uGUI itself uses for buttons.
            fill.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillAmount = 0f;
            fill.color = new Color(0.75f, 0.1f, 0.1f, 0.85f);
            fill.raycastTarget = false;
            RectTransform fillRect = fill.GetComponent<RectTransform>();
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;

            GameObject labelGO = new GameObject("Label");
            labelGO.transform.SetParent(go.transform, false);
            Text label = labelGO.AddComponent<Text>();
            label.font = UIFont();
            label.fontSize = 16;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = Color.red;
            // "HOLD" states the interaction, and the key is advertised because it is the one that
            // works with the cursor locked.
            label.text = "HOLD [N] — END CYCLE";
            // Belt and braces with the wider rect above: a label that silently rewraps is how this
            // became unreadable in the first place, and at some resolutions the scaler will shave
            // a pixel off.
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.raycastTarget = false;
            RectTransform labelRect = label.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            EndCycleControl control = go.AddComponent<EndCycleControl>();
            control.fill = fill;
            control.group = group;
        }
    }
}
