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
    // One-shot whitebox scene assembly for the Iteration Room prototype.
    // Run via Unity menu "Iteration Room/Build Whitebox Scene", or headlessly:
    //   Unity.exe -batchmode -quit -projectPath <path> -executeMethod IterationRoom.EditorTools.SceneBuilder.Build
    // **PARTIAL SINCE 2026-08-31, AND BROKEN UP PROPERLY ON 2026-09-02.** This file reached 25,000
    // lines and 650 members, and every new room had made it worse. `SceneBuilder.Departure.cs` was
    // the first piece taken out; the rest now sits beside it, ONE FILE PER AREA:
    //
    //   Capture    the pictures the build renders: menu background, previews, app icon, press shot
    //   Pipeline   folders, layers, URP, post-processing, lighting, per-scene environment
    //   Textures   every generated texture, sprite and icon
    //   Baking     scene splitting, reflection probes, the GI bake
    //   Materials  materials and the primitive helpers that wear them
    //   Fixtures   switches, taps, the tank, the bucket, the wall panel display
    //   Rooms      shells, the slide and its ride path, the empty room, signs
    //   Core       `BuildCore` and everything structural: shells, slabs, panel walls, gas, exits
    //   Chess / CycleTwoProps / CycleTwoRooms / Keys        cycle 1 and 2's contents
    //   CycleThree / Beam                                   cycle 3's storeys, ladder, cube and light
    //   Ghost / Audio / Hud / Meshes / Menu / Departure     the rest of the build
    //
    // **NOTHING MOVED BUT LOCATION.** The split is mechanical and is verified by reconstructing the
    // original body byte for byte before anything is written; a partial class is one class, so
    // everything private in any part stays reachable from every other part. The seams are ANCHORED
    // ON MEMBER TEXT rather than on line numbers, so re-running it survives the file growing.
    //
    // **WHAT STAYS HERE: the constants, and `Build`.** Every tuned number in the project is in this
    // file (CLAUDE.md 2), and the one method that says what order the world is assembled in is the
    // one thing that should not have to be found.
    public static partial class SceneBuilder
    {
        private const string ScenePath = "Assets/Scenes/IterationRoom.unity";
        private const string MenuScenePath = "Assets/Scenes/MainMenu.unity";
        // How far in from the left edge the title and the button column sit, at the canvas's 1920
        // reference width. One number, so the two cannot drift apart.
        // **HOW FAR THE TITLE SCREEN'S COLUMN SITS FROM THE LEFT EDGE.** 132 -> 84 on 2026-08-28,
        // by request ("버튼 위치를 좀더 사이드로 붙여도 좋아").
        //
        // The margin is doing one job: keeping the words off the edge of the frame. Past that, every
        // pixel it spends is picture it takes away - and the picture here is a render of the room the
        // game is set in. Tighter reads as a title card with a menu at its edge rather than as a menu
        // laid over a photograph, which is the arrangement this screen has been moving toward since
        // the title was pushed off-centre.
        private const float MenuLeftMargin = 84f;

        // ONE SCENE PER CYCLE, on top of the core one. `IterationRoom` keeps the player, the HUD, the
        // loop and the join between storeys; everything a cycle IS moves out into these.
        //
        // The order is play order, and it is the same order `CycleSceneLoader` reads them back in - so
        // a cycle's position in this array is what says which cycle it is. Append, never reorder.
        private static readonly string[] CycleSceneNames = { "Cycle1", "Cycle2", "Cycle3", "Cycle4" };
        private static string CycleScenePath(string name) => $"Assets/Scenes/{name}.unity";
        private const string MaterialsDir = "Assets/Materials";
        private const string PrefabsDir = "Assets/Prefabs";
        private const string ShadersDir = "Assets/Shaders";
        // Item ids are a WIRE VALUE - KeyLock asks for "Key", BalloonTool and GhostReplayer both
        // ask for "Tool". Declared once so a rename cannot silently disarm one side of a gate.
        private const string ToolItemId = "Tool";
        // ~~CycleTwoToolItemId~~ UNUSED since 2026-08-15: cycle 2's nightstand went, and with it the
        // three pins it carried. Kept as the reserved name for whatever tool cycle 2 eventually has,
        // so a second `"Tool"` pool can never be created by accident (see ItemRegistry on why an id
        // silently becoming a supply is the collision worth catching).
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
        // Room2-2's buckets. ONE ID FOR FOUR OBJECTS, which `ItemRegistry` has supported since the pin
        // drawer: an id can name a SUPPLY. Each bucket still obeys the one-object rule and returns to
        // its own origin - what is forbidden is two holders of one object, never two objects of one id.
        private const string BucketItemId = "Bucket";
        // The block on cycle 2's chest of drawers. It has no job yet - see BuildDresser - so the id
        // says what the object IS rather than what it opens.
        private const string CycleTwoBlockItemId = "Block2";
        // Cycle 1's cube, on the nightstand. Its own id rather than cycle 2's for the reason
        // `BuildDresserCube` gives: one of them is 8.3kg on a scale and the other is a trinket.
        private const string CycleOneBlockItemId = "Block1";
        // The notice on room1-1's floor. Its own id like every other carryable, even though nothing
        // ever asks for it by name - `ItemRegistry` still has to be able to sweep it home.
        private const string IntakeNoticeItemId = "Notice";
        // THE POOL'S OWN CARRYABLES. Ids naming a SUPPLY, like the pins and the buckets: three ducks
        // and two beach balls share one id each, and every instance obeys the one-object rule and
        // returns to its own spot on the water (CLAUDE.md §1.2).
        // **PREFIXES, NOT IDS.** `BuildFloatingProps` appends `_0`, `_1`... so each duck and each
        // beach ball carries its own id - see the note at that assignment for why a shared one was a
        // bug rather than a saving.
        private const string DuckItemId = "Duck2";
        private const string BeachBallItemId = "BeachBall2";

        // ROOM2-0'S KEYS: nine billiard balls in the chest of drawers, and each one is its own id.
        //
        // NOT A SUPPLY, unlike the pins, the buckets, the ducks and the beach balls - and that is the
        // whole point of them. A supply says "any of these will do"; here WHICH ONE is the answer to
        // the puzzle, so every ball is a distinct object with a distinct socket that will take it and
        // nothing else. Nine ids, nine one-member pools, four of which a pedestal wants.
        private const string BilliardIdPrefix = "Billiard";

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

        // The root every third-party model lives under. `ReadModelCredits` walks it, so an asset
        // dropped anywhere inside is credited without anything else being edited.
        private const string ArtAssetsDir = "Assets/ArtAssets";

        private const string FurnitureDir = "Assets/ArtAssets/Furniture";
        // Kept apart from the furniture because neither is furniture, and because the tree is the
        // only asset in the project whose SHAPE is load-bearing - its clear trunk is the bridge.
        private const string NatureDir = "Assets/ArtAssets/Nature";
        private const string PlayDir = "Assets/ArtAssets/Play";
        private const string ToolsDir = "Assets/ArtAssets/Tools";
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

        // **WHAT A LIT PANEL'S ALBEDO IS, AND WHY IT IS NOT 1.0** (2026-08-25).
        //
        // Albedo 1.0 is a surface that returns every photon it receives and absorbs none. Nothing
        // does; fresh white paint is about 0.85, which is what the FLOOR was already set to when play
        // reported it as "too white" - and the walls and ceiling were left at 1.0, because with no
        // bounce being computed it made no difference.
        //
        // **It makes all the difference now, and it is a DIVERGENCE.** Indirect light is a geometric
        // series in albedo: at 0.85 it converges to 1/(1-0.85) = 6.7x the direct light, and adding
        // bounces past about eight changes nothing. At 1.0 it does not converge at all - every bounce
        // adds the SAME amount again, forever - so the room's brightness became a function of the
        // bounce COUNT rather than of its lighting. That is exactly what play saw: two bounces looked
        // fine, eight was bright, sixteen was "over-bright". The count was never the problem.
        //
        // **This must match `WallPanelDisplay.onColor`, which is why it is one constant.** The bake
        // reads the material and the runtime paints the property block, so if they disagree the room
        // is lit for one wall and rendered as another.
        //
        // **WAS (0.85, 0.85, 0.86) UNTIL 2026-09-04 - B a hair over R/G, on every white surface in
        // the building, floor included (the floor duplicated this exact triple rather than
        // referencing it, which is fixed below too).** Invisible on a wall, which is lit mostly by
        // its own albedo plus one bounce of direct light; not invisible on a bake that sums eight
        // GI bounces (`docs/rendering-notes.md`), where the same ~1% tilt compounds bounce over
        // bounce and came out on every face of Room1's baked reflection cubemap as a measured 2-5%
        // blue bias (checked directly - see `docs/gotchas.md`). Now that the floor sits properly
        // inside its own reflection probe (`ProbeBoxMargin`) and shows that bake at high smoothness
        // and some metallic, the compounded tilt reads as a visibly blue floor. Flattened to true
        // grey; nothing else about this constant (its level, its use for the panel/ceiling lit
        // state) changes.
        private static readonly Color PanelLitColor = new Color(0.85f, 0.85f, 0.85f);

        // **HOW BRIGHT A CEILING FIXTURE IS - and since the ambient went to zero, this is the ONLY
        // thing that sets how bright the building is** (2026-08-25).
        //
        // It was 10.5 in three separate literals and one derivation, which is three places to forget.
        // 10.5 was itself derived - `9 x (5.408/5.0)^2` - to hold the floor where it had been signed
        // off at a lower ceiling, back when flat ambient was carrying most of the room.
        //
        // **7, because 10.5 was measurably blowing the floor out.** Sampling the menu render's floor
        // gave (241, 255, 255) with 39% of its pixels at or past 254 - clipped, not bright. This is
        // the second time that has been measured on this exact surface; the first was answered by
        // dropping the floor's albedo to 0.85, which is now as low as it should go.
        //
        // **This is the right lever and the other two are not.** Bounces are converged at eight now
        // that albedo is below 1, so cutting them would deliberately under-compute light to hide
        // having too much of it; and albedo below ~0.8 stops being white paint and makes the cell
        // grey. Intensity scales the direct light and everything that bounces off it TOGETHER, which
        // is the only change that dims the room without changing what it looks like.
        // Read off `LightingTuner` 2026-08-26, with the ambient bands above. It came DOWN from 10.5
        // as the ambient went up - see there. 10.5 was itself derived (`9 x (5.408/5.0)^2`) to hold
        // the floor at a brightness signed off under a lower ceiling; this replaces that derivation
        // with a look.
        private const float CeilingLightIntensity = 7.022f;

        // **HOW BRIGHT THE VISIBLE PANEL IS - WHICH IS NOT HOW BRIGHT THE ROOM IS.**
        //
        // A ceiling fixture is two objects: the spot above at `CeilingLightIntensity`, which lights
        // the room, and an emissive panel, which is the white rectangle you see in the ceiling. They
        // are one thing modelled twice, and `MakeEmissiveMaterial` marks the panel `RealtimeEmissive`
        // so that exactly one of them contributes to GI (see the note there - making both contribute
        // lit the room twice over). **So this number changes nothing about the light in the room.**
        //
        // **3.5 -> 2.0, 2026-09-01, and it is an ANTIALIASING fix.** Measured off a screenshot: the
        // panels' rims had literally zero intermediate pixels - 133 straight to 255 with nothing
        // between - while the wall grooves in the same frame had eight. The coverage was being
        // computed correctly and then thrown away:
        //
        //     25% covered -> 0.25 x 3.5 + 0.75 x 0.23 = 1.05 linear
        //     50%         -> 1.87
        //     75%         -> 2.68
        //
        // Every one of those is past the display's white point, so all three resolve to 255 and the
        // edge is a hard step. **No amount of MSAA reaches it** - the samples are taken and then
        // clipped. The only fix is for partial coverage to land below white, which is this.
        //
        // WHAT IT DOES CHANGE, and none of it is the room's brightness:
        //   - the panel is less glaring to look straight at;
        //   - bloom, which has a 1.2 threshold - so the overshoot goes from 2.3 to 0.8, about a third
        //     of the glow it had;
        //   - reflections. The walls are 0.85 smoothness and the reflection probes bake the room with
        //     these panels in it, so a dimmer panel is a dimmer highlight in every wall and floor.
        //
        // **IT IS READ IN TWO PLACES AND BOTH MUST MOVE TOGETHER.** The fixtures here, and
        // `ChessReward.litPanelEmission`, which is what Room2West brings its own ceiling back up to.
        // That one was the component's own default until this constant existed, which is the state
        // CLAUDE.md 2 exists to prevent - a tuned number only an inspector knows.
        private const float CeilingPanelEmission = 2.0f;

        // **155 DEGREES, WIDENED FROM 130** (same pass). A wider cone from 5.41m spreads the same
        // flux over more floor, so the bright pool under each fixture softens and the room reads
        // evenly lit rather than spotted. It is the other half of the same decision as the intensity
        // above and they move together: widening alone would dim the floor, dimming alone would keep
        // the pools.
        private const float CeilingSpotAngle = 156.798f;

        // Room2West's ceiling fixtures, filled in by BuildShell and read by one thing: the chess
        // board dims them and brings them back up as its reward. Held here rather than found by name
        // later, because a lookup by name is a second statement of what BuildCeilingLights called them.
        private static Light[] Room2WestLights;
        // ~~Room1's own fixtures, kept because `LightingTuner` drove them~~ - the tuner
        // is where the lighting is tuned, so it is the one room whose lights something else holds.
        private static Renderer[] Room2WestPanels;

        // **ROOM2-1'S FIXTURES AND ITS TWO FIXTURE MATERIALS, HELD FOR THE SECOND PROBE BAKE.**
        // That room is dark for the whole build - `BuildCycleTwoShell` switches it off the moment it
        // has built it - so the ordinary bake at the end of `Build()` can only ever photograph an
        // unlit room. `BakeSwitchRoomLitProbe` turns it back on for one extra bake, and needs the
        // same four things the build used to turn it off. Held rather than found by name later, for
        // the reason the Room2West pair above gives: a lookup is a second statement of a name.
        private static Light[] Room2OneLights;
        private static Renderer[] Room2OnePanels;
        private static Material Room2OneFixtureLit;
        private static Material Room2OneFixtureOff;
        private static RoomCondition Room2OneLitWhen;

        // **HOW MUCH OF THE ROOM SURVIVES WITH ITS LIGHTS OFF, IN ONE PLACE.** Two things have to
        // agree on it or the room contradicts itself: `RoomBlackout` scales the AMBIENT by it, and
        // `ProbeLightSwap` scales the probe's INTENSITY by it. Set them differently and the walls
        // reflect a room at a brightness the room is not - which is the fault play found as "a
        // bright patch when you get close to a wall", from the other side.
        //
        // A fifth rather than nothing because the switches have to stay findable - see the note at
        // the call site.
        private const float Room2OneDarkFraction = 0.2f;

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
        // **THE SAME SHOT WITH HALF THE ROOM'S FIXTURES OFF.** The title screen flickers by fading
        // this over the one above - see `MenuFlicker` for why a photograph cannot do it any other
        // way, and `CaptureMenuBackground` for the one rule that keeps the pair usable: both frames
        // are taken from the same camera in the same pose, so nothing between them moves except the
        // light.
        private const string MenuBackgroundDarkPath = TexturesDir + "/MenuBackgroundDark.png";
        // THE APP ICON IS A RENDER OF THE ROOM, taken on the same build and from the same camera as
        // the two above. `PlayerBuilder` reads this file and hands it to `PlayerSettings.SetIcons`;
        // nothing in the game draws it. Square and tighter - see `CaptureAppIcon` for the framing
        // and for the one thing a menu background does not have to survive: being 32 pixels wide.
        private const string AppIconPath = TexturesDir + "/AppIcon.png";
        // A PRESS SHOT OF ROOM1, and it lives OUTSIDE Assets on purpose - nothing in the game draws
        // it, so importing it would only add a texture to every player build. `Build/` is where the
        // things that leave this machine already live.
        private const string Room1ShotPath = "Build/Screenshots/room1.png";
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

        // Must match the range Tools/generate_narration.py writes out. Past this the announcer
        // falls back to a generic line rather than going silent.
        private const int NarrationIterationLines = 30;

        // What the PA says on the way up in the cable car, in order. Five, matching
        // `Tools/generate_narration.py` and the five points `EndingDeparture.NarrateTheRide` fires
        // them at - a mismatch is silence at the end rather than an error, so it is one number.
        private const int NarrationRideLines = 13;

        // How many cycles the spoken report can name, and how high its counts go. Must match
        // `REPORT_CYCLES` / `REPORT_MAX` in `Tools/generate_narration.py` - the generator writes one
        // clip per value and this loads them by the same names, so a mismatch is a null in the middle
        // of a sentence. `NarrationDirector.Pick` clamps rather than dropping, so a run past the top
        // says the highest figure it has instead of a gap.
        private const int NarrationReportCycles = 4;
        private const int NarrationReportMax = 99;

        // The attribution shown at the bottom of the title screen. Everything in this project is
        // generated from a script except the two furniture models and the HUD typeface, so this is
        // ~~`CreditsLine`~~ **REPLACED 2026-08-23 by a generated credits PAGE.** One dim line reading
        // "FURNITURE MODELS: CREATIVE COMMONS" named no author, no title, no licence version and no
        // link - a statement that a licence exists somewhere rather than an attribution, for
        // twenty-six CC-BY models. See `ReadModelCredits` and `BuildCreditsPage`.

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
        //
        // **25mm -> 15mm, 2026-09-01, and the grazing-angle clause above is exactly why.**
        //
        // Play reported the black seams breaking up into dashes on walls seen edge-on. It is not
        // resolution and it is not the aliasing everyone reaches for first: the groove is a real
        // trench 50mm wide and 25mm deep, so past a certain angle the near panel's own edge OCCLUDES
        // the dark backing at the bottom of it. Between "fully visible" and "fully hidden" there is a
        // band of angles where the occlusion resolves differently pixel to pixel, and a line that is
        // there in one pixel and gone in the next is a line that looks broken.
        //
        // The angle it starts at is `atan(width / depth)` from the wall's normal, so the depth is the
        // only term worth touching - the width is what makes the grid read at all:
        //
        //     25mm -> starts at 63.4 deg, a 26.6 deg band of flicker
        //     15mm -> starts at 73.3 deg, a 16.7 deg band
        //     10mm -> starts at 78.7 deg, an 11.3 deg band          <- here
        //
        // **AND THEN 15mm -> 10mm THE SAME DAY, FOR A DIFFERENT REASON, MEASURED OFF THE PIXELS.**
        //
        // 15 fixed the breaking-up and the seams still read as soft and a little dirty rather than
        // as clean black lines. Reading the actual luma across one at 4x magnification says why, and
        // says it is NOT antialiasing:
        //
        //     11 -> 55 -> 132 -> 125 -> 161 -> 153 -> 161 -> 162 -> 183 -> 196
        //
        // Two things in that. There are eight distinct intermediate values, so the antialiasing is
        // working - and the ramp is NOT MONOTONIC. It rises, dips, rises, dips. Antialiasing cannot
        // do that. What can is a sequence of SURFACES: the panel face, the lit chamfer on its rim,
        // the groove's side wall, and the backing at the bottom.
        //
        // **THE SIDE WALL IS MOST OF THAT BAND.** The chamfer is under two pixels at the distance
        // that was measured; the 15mm of side wall, seen at an angle, is the rest of the six. So the
        // seam is not a line at all, it is a small lit trench - which is exactly what the first
        // paragraph of this comment says it was built to be, and exactly what stops it reading as a
        // crisp black line. **More MSAA cannot touch it** (see the note by `m_MSAA`, which is at 4
        // and is where it should stay); the only lever is how much side wall there is to see.
        //
        // `PanelChamfer` goes 6mm -> 3mm with it, which is the constraint that kept this at 15
        // before: 10mm of relief under a 6mm chamfer leaves 4mm of straight side and the panel
        // starts becoming a shallow pyramid. At 3mm it leaves 7mm, which is still a groove wall.
        //
        // What it costs, and it is a real trade: shallower shadow in the seam, less for SSAO to
        // resolve the grid off the depth buffer (see the near-clip note in `BuildPlayer`), and a
        // thinner lit rim. **The building reads slightly flatter and the lines read sharper.** If
        // that is the wrong way round, 15/6 and 25/6 are both one edit away.
        private const float GrooveDepth = 0.010f;

        // HOW WIDE THE CHAMFER ON A PANEL'S FRONT RIM IS - see `ChamferedPanelMesh`.
        //
        // 6mm against the panel's own relief. Large enough that the band is a line the eye reads
        // rather than an aliased pixel at the far end of a room, and small enough that a good deal of
        // straight side is left to be the groove wall - a chamfer that ate the whole depth would turn
        // every panel into a shallow pyramid and lose the flat face the room is made of.
        //
        // **6mm -> 3mm, 2026-09-01, and it went with the groove.** `GrooveDepth` went to 10mm to
        // make the seams read as lines rather than as small trenches, and 10mm of relief under a 6mm
        // chamfer leaves 4mm of straight side - the shallow pyramid this note warns about. 3mm
        // leaves 7mm.
        //
        // It is also half of what was making the band soft in the first place: a chamfer is a lit
        // rim, and a lit rim between a white face and a black backing is a third value in the middle
        // of the transition. Narrower rim, tighter transition.
        //
        // **THE RISK THIS TAKES IS THE ONE THE PARAGRAPH ABOVE NAMES**: too small and the band is an
        // aliased pixel rather than a line the eye reads. MSAA is at 4 now (it was 2 when that was
        // written), which is what makes 3mm affordable - but it is the thing to look at if the
        // panels start to look like plain cubes at the far end of a room.
        private const float PanelChamfer = 0.003f;

        // HOW FAR THE WEAR PASS IS ALLOWED TO DULL A SURFACE, as a fraction of its authored
        // smoothness - see `MakeSmoothnessMap`. 0.72 means the dullest patch reflects about three
        // quarters as sharply as the cleanest, which on the walls' 0.85 is a range of 0.61 to 0.85.
        //
        // Deliberately modest. This is a pass that should be impossible to point at and easy to miss
        // when it is switched off; a wide range reads as dirt, and the facility is not dirty, it is
        // used. Turn it toward 1.0 to disable the effect without removing the texture.
        // **1.0 MEANS THE WEAR MAP IS OFF, AND THAT IS DELIBERATE (2026-08-25).**
        //
        // It was added the same day to break up "perfectly uniform roughness", which really is one of
        // the strongest tells that a surface is rendered. It replaced that tell with a worse one:
        // reported from play, twice, as pale grey squares repeating across the walls and floor.
        //
        // **The cause is the TILING, not the noise, and it cannot be tuned out.** URP/Lit drives every
        // secondary map from `_BaseMap`'s single UV transform, so the wear map is locked to the
        // (5,3)-per-panel tiling the NORMAL map needs for its fine grain. That puts fifteen copies of
        // the same tile on every panel and the same fifteen on all 88 of them - and value noise has
        // its extrema on a lattice, so what repeats is a regular grid of soft blobs. Decorrelating the
        // octaves (coprime periods, a third octave) was simulated and does not help: the repeat is the
        // artifact, not the octave design. It shows worst at GRAZING angles, where the specular is
        // strongest, which is why the head-on walls always looked fine.
        //
        // **If per-surface variation is wanted back, it has to come from somewhere that is not a
        // tiled texture.** The building is made of discrete panels, so the shape that fits is one
        // smoothness value PER PANEL through the property block `WallPanelDisplay` already owns - no
        // tiling, and variation at the scale the architecture actually has.
        //
        // The map is still generated and still carries metallic in red; only its alpha is now a flat
        // 1.0, which multiplies smoothness by nothing. Set this back below 1 to re-enable it.
        private const float WearFloor = 1.0f;

        // **HOW GLOSSY A WALL PANEL IS, IN ONE PLACE.** Every room's panels are given this, and
        // `CheckWallSmoothness` fails the build if the asset does not come out carrying it - which
        // is the only way this particular fault announces itself, a matte wall being a perfectly
        // valid material. 0.03 is the matte default `MakeColorMaterial` stamps on everything; at
        // that roughness there is no specular response at all and the room stops reflecting itself.
        //
        // **0.65 WAS TRIED HERE FOR ONE DAY AND REVERTED, 2026-09-04, by request: the sharper wall
        // is the better-looking one.** Worth keeping the finding that sent it to 0.65, because the
        // observation behind it was correct and someone will notice it again:
        //
        // A ceiling fixture is TWO objects - a square emissive panel, and a separate Spot `Light`
        // standing in for its glow (`BuildCeilingLights`; URP has no realtime area light). What a
        // glossy wall shows is the LIGHT's specular lobe, and a point source's lobe is ROUND however
        // square the panel throwing it is, so the highlight and the fixture disagree on screen.
        // Two ways out were examined:
        //   - Narrow the cone so it never reaches a wall. Ruled out by geometry, not by taste: at
        //     this ceiling height it would have to come down to about 52 degrees to clear the
        //     nearest wall, and four fixtures that tight leave dark gaps across an 8.75 x 10.5m
        //     floor. There is no angle that both misses the walls and lights the floor.
        //   - Soften the SURFACE (this constant). It works - the discs really do spread out and the
        //     wall reads as an even gradient - and it costs nothing in brightness, since no light or
        //     ambient value moves. What it also does, which is why it came back out, is blur the
        //     wall's own mirroring of the room along with the highlight: same knock-on
        //     `FloorSmoothness`' own history records at this value, "a reflection probe samples a
        //     blurred mip... a white room averaged to a flat sheet".
        //
        // So the round highlight is the price of a wall that reflects at all, and it has now been
        // looked at both ways round and the price judged worth paying. A third option nobody has
        // built: give the fixtures a light COOKIE shaped like the panel, which is the only thing
        // that would make the highlight square without touching either the cone or the surface.
        private const float WallSmoothness = 0.85f;

        // **HOW GLOSSY THE FLOOR IS - AND IT IS BACK TO 0.65, THE VALUE IT HELD BEFORE
        // 2026-09-03. A REFLECTIVE FLOOR WAS BUILT, MADE TO WORK, PLAYED, AND REJECTED
        // (2026-09-04, by request: "I thought reflection would be good, it is really not").**
        //
        // This is a rejection of the LOOK, not of a broken feature, and the difference matters to
        // anyone tempted to try it again. It genuinely worked by the end: the floor really was
        // mirroring the room in play. The three things that had to be fixed to get there are all
        // still in the build, because each is a correctness fix that stands on its own and two of
        // them are what the WALLS reflect through:
        //
        //   - `ProbeBoxMargin` - the probe box was sized to exactly RoomWidth/RoomHeight/RoomDepth,
        //     so its faces landed exactly on the floor slab's top and each wall's inner face, and
        //     both renderers straddled the boundary instead of sitting inside it. The floor and the
        //     side walls were never inside their own room's reflection probe at all, and fell back
        //     to the default skybox - which is the whole reason four rebuilds of THIS constant and
        //     `FloorMetallic` came back byte-identical: nothing was reaching the surface to tune.
        //   - `WithFlatReflection` on `CaptureMenuBackground` - the title-screen capture races its
        //     own probe data, the way `CaptureCyclePreview` already documented.
        //   - `PanelLitColor` flattened to true grey - a ~1% blue tilt on every white surface,
        //     invisible on a wall, not invisible once a bake of eight GI bounces is being mirrored.
        //
        // **What was rejected is the mirror itself.** A polished floor in a white cell reads as wet
        // or as glass rather than as a floor, and it fought the calm even look the room is for
        // (`mainmenu_example.png`). 0.65 is not "reflection off" - it is the roughness that was
        // tuned for STRUCTURE: the grain reads, the room does not stand in it. If a reflective
        // floor is ever wanted again the material dials are here and the plumbing above already
        // works; what to re-read first is this paragraph, not the plumbing.
        private const float FloorSmoothness = 0.65f;

        // **NO METAL IN THE FLOOR, AND THE CONSTANT IS KEPT AT 0 RATHER THAN DELETED.** 0.22 was
        // reasoned from this project's own cube-room glass finding - at `_Metallic` 0 a dielectric
        // only reflects through Fresnel F0, about 4%, which next to a diffuse term twenty times its
        // size is easy to lose - and that reasoning is still correct. It is the effect it buys that
        // was not wanted (see `FloorSmoothness`). Left as a named zero so the next attempt starts
        // from the finding rather than rediscovering it.
        private const float FloorMetallic = 0f;

        // **AND THE GRAIN GOES BACK UP WITH IT, 0.12 -> 0.6.** These two have moved together every
        // time either has moved, and the reason is the one recorded at the call site: the floor's
        // relief modulates its SPECULAR, so the sharper the reflection the more the grain shreds
        // it. 0.12 existed only to keep a mirror legible; with the mirror gone
        // (`FloorSmoothness`), the grain is free to do its own job again, which is keeping the
        // floor from reading as a perfectly uniform field.
        private const float FloorBump = 0.6f;

        // **HOW FAR A ROOM'S REFLECTION-PROBE BOX REACHES PAST THE WALLS IT ENCLOSES.**
        // `BuildReflectionProbe` used to size that box at exactly RoomWidth/RoomHeight/RoomDepth, so
        // every face landed exactly on the floor slab's top, the ceiling, and each wall's inner
        // face - and a wall panel's own bounds sit BEHIND that face by its thickness, a floor slab's
        // BELOW it by its own. Both renderers straddled the box boundary instead of sitting inside
        // it, so Unity's automatic probe assignment read them as outside this probe and fell back to
        // the scene's default reflection (the blue procedural skybox) - see `BuildReflectionProbe`
        // for how a substituted magenta cubemap confirmed exactly that.
        //
        // **AND IT HAS A CEILING AS WELL AS A FLOOR: 0.5 -> 0.25, 2026-09-04. TOO MUCH MARGIN MAKES
        // NEIGHBOURING ROOMS' BOXES OVERLAP, WHICH IS ITS OWN BUG.** The rooms are a corridor, and
        // consecutive rooms sit `RoomPitch` apart - only `2 * WallDepth + DoorPocketDepth` (0.32m
        // here) of divider between one room's box and the next. A 0.5m margin adds 0.25m to each
        // side, closes that 0.32m and leaves the two boxes intersecting by 0.18m - measured in the
        // built scene, every consecutive pair in cycle 2. Anything inside that slab is in TWO
        // probes at once, so Unity blends them, and a room whose fixtures are switched off
        // (room2-1, `AllLightsOn`) can reflect the lit room next door.
        //
        // **The number has to clear the floor slab and stay under the divider**, and those are far
        // apart, so this is not tight: the fault it was raised for is the FLOOR, whose bounds run
        // 0.10m BELOW y=0 - the slab's own thickness - and whose centre therefore sat below a box
        // whose bottom face was exactly y=0. 0.25 puts the box bottom at -0.125 (floor contained),
        // the sides at +-4.50 against the floor's +-4.49 (contained), and still leaves 0.07m of
        // clear air between one room's box and the next. Raising it back toward 0.5 buys nothing
        // and re-opens the overlap.
        private const float ProbeBoxMargin = 0.25f;

        // Natural door proportions, deliberately NOT snapped to the grid - the panelling is cut
        // around it instead, so it reads as a doorway rather than a missing panel.
        private const float DoorWidth = 1.3f;
        private const float DoorHeight = 2.5f;
        private const float DoorThickness = 0.06f;
        // Clearance left between the two rooms' walls. The door lives in this cavity and slides
        // sideways into it, so when it's open both walls hide it - a pocket door.
        private const float DoorPocketDepth = 0.1f;

        // How high a step the player walks up without jumping. See where it is applied for why it is
        // this and not higher: it has to clear half a grid row and stay under the jump.
        private const float PlayerStepOffset = 0.72f;

        // ROOM3-1'S GATES ARE PANELS, NOT DOORS. An ordinary door in this building is 1.3 x 2.5 and
        // reads as a door; a gate is TWO CELLS of the wall's own grid wide and TWO ROWS tall, cut on
        // the grid lines, so what opens is a piece of the wall rather than a thing set into it.
        //
        // Only the HEIGHT is a constant. The width and where along the wall it sits are `GateSpan`'s
        // to answer, because they depend on whether that wall's cell count is even - and a constant
        // here would be a second answer that happens to agree.
        private const float GateHeight = 2f * GridCellHeight;    // 2.7038


        // Where room3-2N's CENTRE sits, and the corridor's far end is derived from it. Its own south
        // face is half of its doubled depth back from here, which is not `RoomDepth / 2` any more -
        // getting that wrong leaves the corridor ending inside the room or short of it, and neither
        // announces itself.
        // **EVERY CORRIDOR DIMENSION IS AN EXACT MULTIPLE OF THE GRID, and the first version was not.**
        // `BuildPanelWall` divides a wall's width by its ROUNDED cell count, so a 22.05m corridor came
        // out in cells of 1.696 where every other wall in the building is 1.75 - a different rhythm,
        // and visible the moment you look down it from room3-1. Its height did the same thing one axis
        // over: corridor plus shaft came to 5.5076, which is four rows of 1.3519 plus a 0.1m SLIVER,
        // and a cut-off panel against the top is the exact fault play called out on the tree hall.
        //
        // Thirteen cells and four rows. The room beyond is placed FROM the corridor rather than the
        // corridor being measured between the rooms, because only one of the two can be the multiple.
        private const float CorridorRun = 13f * GridCellWidth;          // 22.75
        private const float CorridorShaftHeight = 2f * GridCellHeight;  // 2.7038 - shaft top is row 4

        // Room3-2N's centre. FOUR wall build-ups between the two rooms' inner faces, not two: one
        // for each room's own wall, and one more at each end for the corridor's overrun to sit in
        // WITHOUT touching them. See the corridor's own note on why touching is the whole problem.
        private const float NorthRoomZ =
            RoomDepth / 2f + 4f * WallDepth + CorridorRun + RoomDepth;

        // The east-west equivalent of `RoomPitch`. Rooms are 8.75 across and 10.5 deep, so a
        // neighbour to the side sits at a different remove from one in front - and the term that is
        // NOT the room's own size is identical, which is the point: the two walls meet with the same
        // build-up and the same pocket a door slides into, so a gate works on any of the four.
        private const float RoomPitchX = RoomWidth + 2f * WallDepth + DoorPocketDepth;

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

        // THE SAME HOLE SEEN FROM THE CYCLE BELOW, mirrored in Z.
        //
        // The two slabs the shaft passes through are cut with the same Rect in each room's own local
        // XZ, which lined them up for as long as both rooms faced the same way. Cycle 2 is turned
        // 180 degrees now (see CycleTwoYaw), so its first room's local +Z is the world's -Z and the
        // unmirrored Rect would cut its ceiling four metres from where cycle 1 cut its floor - a
        // shaft into solid slab, and nothing else in the build would have complained.
        private static readonly Rect CycleExitHoleFromBelow = Rect.MinMaxRect(
            -GridCellWidth / 2f, -CycleExitZ - GridCellWidth / 2f,
             GridCellWidth / 2f, -CycleExitZ + GridCellWidth / 2f);

        // Cycle 2 sits one storey down and runs BACK the way cycle 1 came. Its first room is directly
        // beneath cycle 1's last, so the drop is short and vertical and the player can see the bed
        // through the opening before committing to it; everything after it walks toward -Z.
        // HOW MANY CYCLES THE GAME HAS. Read by the title screen's picker; `LoopManager` derives the
        // same fact from the length of its own array, which is the authority. Adding a cycle means
        // changing both, and they are meant to be found together.
        private const int CycleCount = 3;

        private const float CycleTwoFirstRoomZ = 5f * RoomPitch;

        // HOW FAR APART TWO ROOMS SIT AT A CORNER, where one presents its WEST wall and the next
        // presents its NORTH wall.
        //
        // `RoomPitch` cannot serve, and that is the one piece of arithmetic the ring turns on: a room
        // is 8.75 across and 10.5 deep, so two rooms meeting along the same axis are a different
        // distance apart than two meeting across a corner. Half of each, plus the same divider every
        // other join has.
        private const float CornerPitch = RoomWidth / 2f + RoomDepth / 2f + 2f * WallDepth + DoorPocketDepth;

        // ---------------------------------------------------------------------------------------
        // CYCLE 2 IS TURNED ROUND, 2026-08-16, and this one line is what let the tree hall exist.
        //
        // The ring used to switchback back along -Z, which ran it directly UNDER cycle 1's corridor.
        // That is what capped the hall's ceiling: raise a room to 17.5m with cycle 1 five metres
        // above it and the ceiling drives up through the floor of the room above. The first version
        // worked around it by keeping half the hall short.
        //
        // Turning the ring 180 degrees ABOUT THE BED removes the constraint instead of dodging it.
        // The bed has to stay where it is - it is what the exit shaft drops into - so the rotation
        // pivots there, and every other room swings out to z > 59.5, which is past the end of cycle
        // 1 entirely. **Nothing is above cycle 2 any more except its own first room**, so any room
        // in it can be any height, now and for every cycle-2 room designed later.
        //
        // Implemented as a rotation on the cycle root with the pivot folded into its position, so
        // that everything INSIDE cycle 2 is authored in the same local frame it always was.
        private static readonly Quaternion CycleTwoYaw = Quaternion.Euler(0f, 180f, 0f);

        // THE TREE HALL: room2-3, room2-4 and room2-5 merged into ONE space.
        //
        // ONE BOX, at ONE height. The first version kept the three rooms' differing widths - a 10.5m
        // bay opening into an 8.75m run, with a soffit over the step - because the bay could not be
        // raised. With the ring turned round that reason is gone, and the hall is a single room:
        // 10.5m across, 30.45m along, 17.57m to the ceiling.
        private const float TreeHallHeight = GridCellHeight * 13f;   // 17.5746, thirteen grid cells

        // ACROSS the hall. It is `RoomDepth` because the r2 join fixes it there: r2's south face has
        // to meet the hall's north wall and r2 is an ordinary room, so widening past this would mean
        // moving r2 rather than the hall.
        private const float TreeHallWidth = RoomDepth;               // 10.5

        // The north wall is the one BOTH doorways are in - r2's near the east end, r6's near the
        // west - so everything else is measured off it. Making the two openings share a wall is what
        // let the width be uniform at all: they used to be in walls 0.875m apart, which is precisely
        // the step the soffit was covering. See BuildCycleTwoShell for the 0.875m leg-3 shift that
        // pays for it.
        private const float TreeHallNorthFace = RoomDepth / 2f;                 // +5.25
        private const float TreeHallSouthFace = TreeHallNorthFace - TreeHallWidth;

        // The two ends, unmoved: r3's east wall and r5's west wall.
        private const float TreeHallEastFace = RoomWidth / 2f;                              // +4.375
        private const float TreeHallWestFace = -CornerPitch - RoomPitch - RoomDepth / 2f;   // -26.075

        // THE PIT: room2-4's full 10.5m, wall to wall across the hall, which is what makes it
        // uncrossable. Placed to leave a ledge at each end rather than at room2-4's own footprint -
        // that would have put the lip 0.35m inside the entrance and left nowhere to stand and swing.
        // The near ledge carries the tree, the far one carries the door out.
        private const float TreePitEastEdge = -5.5f;
        private const float TreePitWidth = RoomDepth;                                 // 10.5
        private const float TreePitWestEdge = TreePitEastEdge - TreePitWidth;         // -16.0

        // How far down it goes. Deep enough that the bottom is not part of the picture - falling in
        // is fatal (see KillVolume), so this is what the drop looks like, not somewhere to land.
        private const float TreePitDepth = 26f;

        // Where the two doorways sit along the north wall - the entrance under r2, the exit under
        // r6. Both are a long way clear of the lip: a doorway on the edge of a fatal drop is a place
        // to be nudged into it.
        private const float TreeHallEntranceX = 0f;
        private const float TreeHallExitX = -CornerPitch - RoomPitch;                 // -20.825

        // ---------------------------------------------------------------------------------------
        // ROOM2-5: THE SLIDE, ITS MOUTH, AND THE ROOM ON THE OTHER SIDE OF THE WALL.
        //
        // THE SLIDE AND THE SEESAW WERE PLACED BY HAND IN THE EDITOR AND READ BACK, the same way
        // room2-1's switch height was. Three things about these numbers matter:
        //   - each is the MODEL NODE's own pose in the hall's frame, not a wrapper's, so it can be
        //     applied straight to the imported instance and land where the editor showed it;
        //   - the 270 in each rotation is the glTF import's Z-up correction. It is PART OF THE POSE,
        //     not noise - "tidying" it to zero lays the model on its face;
        //   - the scales are what the hand placement left rather than a fresh fit. The slide keeps
        //     the 0.5403 its old room2-6 fit produced; the seesaw's 0.3709 is a nudge off its 0.3808.
        private static readonly Vector3 SlideLocalPosition = new Vector3(-24.8773f, 0.0610f, -4.8604f);
        private static readonly Vector3 SlideLocalEuler = new Vector3(270f, 183.654f, 0f);
        private const float SlideLocalScale = 0.5403f;

        private static readonly Vector3 SeesawLocalPosition = new Vector3(-19.8104f, 0.9773f, 2.2949f);
        private static readonly Vector3 SeesawLocalEuler = new Vector3(270f, 76f, 0f);
        private const float SeesawLocalScale = 0.3709f;

        // THE MOUTH: where the chute goes through the south wall, and the whole reason the room
        // beyond it exists.
        //
        // From the hall's west corner east to just past the chute's outer rail. That is slightly
        // WIDER than the one panel cell cleared by hand: the rails are wider than a cell, and a chute
        // clipping the edge of its own opening is a hole that does not look like the thing going
        // through it. 1.7m tall, which clears a rider sitting up - the chute's surface is still half a
        // metre off the floor where it passes the wall.
        private const float SlideMouthEast = -23.72f;
        private const float SlideMouthHeight = 1.7f;

        // THE RIDE, in hall coordinates - and only its X and Z. The head is the flat platform the
        // stairs arrive at, the foot is where the chute's tip passes the wall plane, the landing is
        // out on the floor of the room beyond. EVERY HEIGHT IS RAYCAST at build time off whatever is
        // actually under the line, so the path is the chute's own surface rather than a description
        // of it, and a slide that is moved or replaced gets a new path for free. See SampleRidePath.
        private static readonly Vector2 SlideRideHead = new Vector2(-24.70f, 0.15f);
        private static readonly Vector2 SlideRideFoot = new Vector2(-24.70f, -5.50f);
        // FURTHER INTO THE ROOM than it used to be. The run-out is no longer a run-out: the chute's tip
        // now hangs inside the landing room near the top of its wall, so this leg is a plunge into the
        // water rather than a slide across a floor, and how far it reaches is what sets how steep that
        // plunge is. At -9.80 it is about 4.3m along for 4.0m down - 43 degrees, which is a slide's
        // last stretch. -8.40 made it 55, which is a fall with a direction.
        private static readonly Vector2 SlideRideLanding = new Vector2(-24.70f, -9.80f);
        private const int SlideRideSamples = 26;
        // The plunge off the end of it. More samples than the six the old flat run-out needed, because
        // this leg is now a CURVE - see SampleRidePath - and six points across a four-metre arc is a
        // staircase.
        private const int SlideRidePlungeSamples = 12;

        // THE ROOM THE SLIDE LANDS IN: an ordinary shell hung on the hall's south wall at the west
        // end, its west wall flush with the hall's own - so the mouth is in a corner rather than
        // adrift in a long wall. Nothing is above or below it: cycle 2 was turned round in 2026-08-16
        // and this is SOUTH of the hall, which is past the end of cycle 1 entirely.
        private const float SlideRoomCentreX = TreeHallWestFace + RoomWidth / 2f;              // -21.7
        private const float SlideRoomCentreZ = TreeHallSouthFace - (2f * WallDepth + DoorPocketDepth)
                                             - RoomDepth / 2f;                                 // -10.85

        // HOW FAR BELOW THE HALL IT SITS, and this number is DERIVED rather than chosen (2026-08-19,
        // second pass). It was `-StoreyDrop` - a full storey plus the service void - and at that depth
        // the two openings did not meet: the chute left the hall at the hall's own floor and the room's
        // opening was five metres further down, so the rider passed through solid slab and arrived
        // falling out of the ceiling, and the hole in the hall's wall looked into a void rather than
        // into the room.
        //
        // The room's CEILING is now exactly the top of the mouth. Both walls are then cut over the same
        // 1.7m band (`SlideMouthHeight`), so the hall's opening and the room's are ONE HOLE with a
        // wall's thickness of tunnel between them: standing at the top of the slide you look through it
        // and see the room, its water and the balls on it before you commit to the ride.
        //
        // What it costs is a drop - the chute's tip is at the top of that wall and the floor is a room's
        // height below it. That is the point rather than a leftover: it is a slide over a pool, and the
        // water is what it drops into.
        private const float SlideRoomFloorY = SlideMouthHeight - RoomHeight;                   // -3.708

        // THE WAY BACK. A slide is one-way, so without a door this room is somewhere the player can
        // only wait out the clock. At the far end of the wall from the mouth: down the slide, across
        // the room, back up through the door.
        private const float SlideRoomDoorX = -18.90f;
        // Where the south wall is split into two spans - between the mouth and the doorway, because
        // `SubtractRect` takes one hole per wall and this wall now has two. Same trick as the north.
        private const float SlideRoomWallSplitX = -21.70f;

        // THE ROOM IS FLOODED, 2026-08-19 by request: the slide does not land on a floor, it lands in
        // water, and plastic balls float on it. It is PRESENTATION AND NOTHING ELSE - the water is not
        // solid, not fatal, not a puzzle and not recorded. The player wades out of it.
        //
        // 1.2m, which is waist-to-chest on a 1.8m body and the depth that was asked for. It is also the
        // deepest this can go without becoming a mechanic: the eye is at 1.6m, so at 1.2m the player
        // looks DOWN at the surface from above it, and anything deeper puts the waterline across the
        // camera and needs an underwater view, a swim and a way out of it that this room does not have.
        private const float PoolDepth = 1.2f;
        // The grid step the surface is built on. See WaterMeshes.Sheet for why a room-sized quad is not
        // enough: the shader's wobble lives on the vertices, so a sheet with none in the middle is flat
        // in the middle. ~3,000 triangles at this room's size.
        private const float PoolSurfaceCell = 0.25f;
        // HOW MANY BALLS FLOAT ON IT. Not a carpet: things that float SPREAD, and the ball pit in
        // room2-7 is what a floor covered in balls already looks like. Three hundred over ninety square
        // metres is a ball every half metre or so - enough that they are everywhere the eye goes and
        // sparse enough that the water is still the thing under them.
        //
        // It is also what the drift costs. These are individual renderers rather than one merged mesh
        // (see FloatingBalls), so the count is a draw-call count, and a couple of hundred instanced
        // spheres is cheap where a couple of thousand would not be.
        //
        // CUT FROM 300 the same day it was written, once the ducks and the beach balls went on the
        // water: three hundred small balls is a surface the bigger things have to be picked out OF,
        // and the whole reason to have them is that they are seen.
        private const int PoolBallCount = 210;
        // The same 220mm ball the pit is built from, which means the same generated mesh asset.
        private const float PoolBallRadius = BallRadius;
        // How high a ball rides. A hollow plastic ball is nearly all air, so it sits high in the water -
        // a sphere half under reads as a heavy one, which is a different object.
        private const float PoolBallFloat = 0.72f;

        // CYCLE 3, AND IT IS PLACED BY MEASUREMENT RATHER THAN BY THE RING.
        //
        // Cycle 1 and cycle 2 are stacked on the ORIGIN - cycle 2 is one `StoreyDrop` under cycle 1's
        // last room and turned about its own bed. Cycle 3 cannot be: cycle 2's walk wanders twenty
        // metres west and two rooms south of where it started, and it ENDS in room2-0. The bed a
        // player wakes in has to be under the hatch they fell through, so these two numbers are
        // room2-0's own centre, and the storey below it is where cycle 3 begins.
        //
        // Read off the built scene rather than derived: room2-0's world position is the product of the
        // tree hall's origin, the slide room's offsets and `CycleTwoYaw`, and re-deriving that chain
        // here would be a second copy of it to keep in step. If room2-0 ever moves, these move with it
        // and the build says so - `AssertUnderHatch` below fails the build if they drift apart.
        private const float CycleThreeX = 21.7f;
        private const float CycleThreeZ = 108.5f;
        // Room2-0's floor is at -10.916; one storey under it is where cycle 3's floor goes.
        private const float CycleThreeFloorY = -10.916f - StoreyDrop;

        // ROOM2-0: THE END OF CYCLE 2, and the four pedestals that break it.
        //
        // THE SHAPE OF THE PUZZLE. Four pedestals rise as the player comes through the last door, each
        // with a recess in its top and one object drawn on the side facing them. What goes in the
        // recess is the billiard ball whose NUMBER is how many of that object cycle 2 contains - four
        // buckets, five axes, two beach balls, three rubber ducks - so the answer is not written
        // anywhere and cannot be, it is a count of rooms the player has already walked through. All
        // four right and the cycle breaks, exactly as cycle 1's console breaks cycle 1.
        //
        // WHY A ROW AND NOT A SQUARE ROUND THE HATCH, which was the first arrangement tried on paper:
        // a square puts two of the four pictograms facing AWAY from a player walking in, and the
        // pictogram is the only thing that says which pedestal is which. A row facing the door shows
        // all four at once from the threshold.
        private const float PedestalWidth = 1.10f;
        private const float PedestalDepth = 0.80f;
        // Waist height, like cycle 1's console and for the same reason: the recess in the top is
        // looked DOWN into from a 1.6m eye rather than squared up to.
        private const float PedestalHeight = 1.05f;
        // 1.95 apart spans 5.85 of the room's 8.75, which leaves a stride between neighbours - enough
        // that standing at one is unambiguously standing at one, and `BuildFinalSlot`'s reach volumes
        // (1.3 wide) cannot overlap.
        private const float PedestalPitch = 1.95f;
        // South of centre, with the hatch at the room's middle in FRONT of the row: the way down opens
        // between the player and the door they came in by, which is where they are looking anyway.
        private const float PedestalRowZ = -2.80f;

        // THE BOWL IN THE TOP OF A PEDESTAL, cut to the ball's own radius with 5mm of daylight round
        // it - enough that the ball drops in rather than being pressed into a shell of its exact size,
        // and little enough that nothing else in the game would sit in it.
        private const float SocketBowlRadius = BilliardBallSize / 2f + 0.005f;
        // How thick the cap the bowl is cut out of has to be. The bowl's own depth is its radius; the
        // rest is material under the pole, so the cap is a solid thing with a hole in it rather than a
        // shell that meets itself at a point.
        private const float SocketCapThickness = SocketBowlRadius + 0.006f;

        // HOW FAR A GHOST MAY REACH FOR A DUCK OR A BEACH BALL. The pool room is 8.75 x 10.5, and the
        // drain that drags them all to its middle is at most 6.8m from any corner of it - so this
        // covers every way one of them can have moved since the recording was made, and stops well
        // short of being able to reach a neighbouring room. See `CarryableItem.ghostTakeReach`.
        private const float PoolPropTakeReach = 9f;

        // A BILLIARD BALL, at more than twice life size (57mm). Every object in this building is
        // oversized - a 220mm ball-pit ball, a 160mm Rubik's cube, a 13m tree - and a ball has to be
        // read by its NUMBER from across a drawer, which a 57mm sphere at arm's length is not.
        private const float BilliardBallSize = 0.13f;

        // **WHICH WAY UP THE DIGIT IS, AND IT IS ONE SIGN.** The held pose reads the number's upright
        // off the texture's V axis (see `OrientBilliardBall`), and whether +V is up or down on the
        // printed digit depends on the exporter's convention and Unity's import flip - two guesses
        // multiplied together. Rather than pick one and hope, this is the sign of that answer: if the
        // numbers come out upside down in the hand, it is -1f, and nothing else changes.
        private const float BilliardDigitFlip = 1f;

        // THE WAY DOWN OUT OF CYCLE 2, one grid cell in the middle of room2-0's floor. Centred on the
        // room rather than offset like cycle 1's `CycleExitHole`, which sits 2m behind that console
        // because the console is in the middle of ITS room; here the pedestals are the thing that is
        // off-centre and the hole is what "the floor opens in the middle" means.
        private static readonly Rect CycleTwoExitHole = Rect.MinMaxRect(
            -GridCellWidth / 2f, -GridCellWidth / 2f,
             GridCellWidth / 2f,  GridCellWidth / 2f);

        // Where the tree stands: 0.85m back from the lip, which is what it takes to keep the stump's
        // own collider off the pit - `AssertNotWalkable` caught 0.45m on the first build, which is
        // what that assert is for.
        // PLUS, not minus: the near ledge is EAST of the pit's east edge, and subtracting put the
        // tree 0.85m out over the hole. `AssertNotWalkable` caught it on the first build - which is
        // twice now that this one constant has been wrong and twice that the same assert has said so.
        private const float TreeStandX = TreePitEastEdge + 0.85f;                     // -4.65
        // Where the trunk is cut, and where the notch is bitten. A real chopping height, and it is
        // what divides the model in two: below stays as the stump, above becomes the bridge.
        private const float TreeCutHeight = 1.2f;
        // SUNK, so the base reads as growing out of the floor rather than as a model set down on it.
        // The flare at the bottom of this trunk is what gives it away: standing exactly on y=0 it
        // meets the floor along a hard line all the way round, which nothing that grew there would.
        private const float TreeSinkDepth = 0.24f;
        // Clearance left between the crown and the side walls. The tree is scaled to FIT rather than
        // set to a number - see BuildTree - because the crown is 11.72m across at native size and
        // the hall is 10.5m, so a native-scale tree grows through both walls.
        private const float TreeWallClearance = 0.35f;
        // How many notch stages are cut. Each is a real hole in the trunk mesh, built here and
        // switched at runtime - see BuildTree for why the notch is geometry rather than a decal.
        private const int TreeNotchStages = 8;
        // How far the V opens per metre of depth - about 22 degrees off the cut plane. It is HERE
        // rather than inside the carve because the heartwood has to be exactly as tall as the notch
        // is, and the two disagreeing is what leaves a hole you can see through the tree.
        private const float TreeNotchHalfAngleTan = 0.40f;


        // The sensitivity room. Deliberately NOT a multiple of RoomPitch in the positive direction
        // - it is not part of the chain and must never be walked into, so it sits behind Room1 with
        // a room's worth of nothing between them.

        // THERE IS ONE BUILD, and the probe bake is part of it.
        //
        // A "fast, no probes" menu item lived here until 2026-08-15 and was REMOVED as a trap rather
        // than as a saving. It left the existing `.exr` files in place, so a fast-built scene comes up
        // looking lit and reflective while reflecting the building as it was at some earlier build -
        // and this project's walls are 0.85 smoothness and its escape objects are metallic 0.9, which
        // is to say almost entirely what they reflect. A stale reflection does not announce itself;
        // it just makes every judgement about how the room LOOKS quietly untrustworthy, which is the
        // one thing a build is for.
        //
        // The bake is genuinely most of the build's time - fifteen probes, six faces each. If that
        // becomes intolerable the answer is the per-cycle scene split (`docs/cycle-design.md` §7b),
        // which cuts what has to be baked, rather than a switch that bakes nothing and says nothing.
        // REBUILDS ONE CYCLE, INTO ITS OWN SCENE, AND BAKES ONLY ITS PROBES.
        //
        // This is what the scene split was FOR. The full build re-bakes all fifteen reflection probes,
        // six faces each, and that is nearly all of the minutes it takes - while a session spent moving
        // a switch in room2-2 is not looking at cycle 1 at all. Here the scene contains one cycle, so
        // `BakeReflectionProbes` finds eight probes instead of fifteen and `Cycle1.unity` is not touched.
        //
        // NOT THE SAME BARGAIN AS THE OLD "fast, no probes" BUILD, which was removed as a trap. That one
        // left STALE cubemaps in place and said nothing about it, so a room could reflect a building
        // that no longer existed. This bakes every probe it is responsible for, in full; what it leaves
        // alone is a different cycle, whose geometry this cannot have changed because it is not in the
        // scene. Nothing goes out of date without the file it belongs to being rebuilt.
        //
        // The core scene is not needed and not opened. Everything cycle 2 points at outside itself is
        // re-established at runtime by `CycleBinding`, which is why the core references below are null.
        // **IT REBUILDS THE CYCLE, NOT THE PLAYER.** Anything that lives on the player, the HUD or the
        // loop is in the CORE scene and this does not touch it - which is the whole point, and also the
        // one way it misleads. Adding `BucketPlacer` to the player and then rebuilding only cycle 2
        // produced a game where the buckets existed, the stands existed, and left click did nothing at
        // all, because the component that answers it had never been built.
        //
        // The rule that follows: **a change to a room is a cycle build; a change to the player, the
        // HUD or the loop is a full build.** When in doubt, full.
        [MenuItem("Iteration Room/Rebuild Cycle 2 Only")]
        public static void RebuildCycleTwo()
        {
            EnsureFolders();
            PlayerSettings.runInBackground = true;

            EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            Scene scene = SceneManager.GetActiveScene();

            // THE DEFAULT CAMERA AND LIGHT GO, and forgetting this shipped a silent room.
            //
            // A cycle scene is loaded ADDITIVELY on top of the core one, which already has the player's
            // camera and the one AudioListener the game is allowed to have. `NewSceneSetup.
            // DefaultGameObjects` hands over a Main Camera carrying a listener of its own, so the built
            // player ran with TWO - Unity keeps one and the other's spatial audio simply does not
            // arrive. It presents as "the taps make no sound", which is nothing like its cause.
            //
            // The full build does not have this problem because it puts the player in the same scene
            // and clears these on its way past. A per-cycle build has to do it for itself - the same
            // hazard as the door audio: whatever `Build` does for a cycle, the cycle's own path must
            // do too.
            foreach (GameObject go in scene.GetRootGameObjects())
                if (go.GetComponent<Camera>() != null || go.GetComponent<Light>() != null)
                    Object.DestroyImmediate(go);

            // **0.85, NOT WHITE** - settled 2026-08-20, and the
            // reason it is not 1.0 is measurable rather than a matter of taste. Albedo 1.0 is a
            // surface that returns every photon that hits it; nothing does, fresh white paint is
            // about 0.85, and a floor is always darker than the walls above it. With four spots at
            // intensity 10.5 pointing straight down, a 1.0 floor CLIPPED - the title screen's own
            // capture had this floor at (235, 253, 255), two channels already at the top - and play
            // read it exactly: stand in the middle, look down, it is too white.
            //
            // The alternatives were both wider. Dropping the light intensity darkens the walls with
            // it, and pulling exposure down in the volume changes every surface in the game. This
            // changes the one surface that was wrong, and it still reads WHITE: it is 15% off
            // clipping and the eye has nothing brighter on screen to compare it against.
            // Same white as every panel and the ceiling - referenced now rather than duplicated as
            // its own literal, which is how the floor and the walls drifted a hair apart in blue
            // without anyone writing that choice down. See `PanelLitColor` for why that mattered.
            Material floorMat = MakeColorMaterial("FloorWhite", PanelLitColor);
            Material grooveMat = MakeColorMaterial("GrooveDark", new Color(0.04f, 0.04f, 0.045f));
            Material propMat = MakeColorMaterial("PropLight", new Color(0.85f, 0.85f, 0.85f));
            Material panelMat = MakeColorMaterial("PanelWhite", PanelLitColor);
            Texture2D surfaceGrain = MakeNoiseNormalMap("SurfaceGrain", 512, 2.5f);
            ApplySurfaceDetail(panelMat, surfaceGrain, 0.2f, new Vector2(5f, 3f), WallSmoothness);
            // The wall keeps its 0.85 mirroring of the room and loses only the round phantom a
            // point light draws on top of it - see NoDirectSpecular.
            NoDirectSpecular(panelMat);
            // **BOTH NUMBERS NOW LIVE IN `FloorSmoothness` / `FloorBump`, AND BOTH HAVE MOVED
            // AGAIN** (2026-09-03: 0.65 -> 0.9 and 0.6 -> 0.12, so the floor reflects the room).
            // What follows is why they were 0.65 and 0.6, which is kept because every word of it is
            // still the argument that has to be answered before either moves a third time.
            //
            // SMOOTHNESS 0.65, UP FROM 0.3, and BUMP 0.6, DOWN FROM 1.8. Settled 2026-08-20. They are
            // one decision, not two, and the order they happened in is the whole lesson.
            //
            // The smoothness first: a floor at smoothness 0 is a perfectly uniform field, and the eye
            // reads a field with no variation in it as blown out rather than as bright - which was
            // half of what "too white" meant. At 0.65 the ceiling fixtures lay pools across the floor
            // and the 26x30 grain rides in them, so the floor has structure to read distance off.
            //
            // THEN THE BUMP, because raising the gloss is what exposed it. Wall and floor share one
            // normal map at one physical grain size (~0.35m repeat on both) and differed ONLY in this
            // number - 0.2 against 1.8, nine times apart. While the floor was matte that relief only
            // reached the diffuse and read as the tooth of sealed concrete. Glossy, the same grain
            // modulates the SPECULAR, and the floor stopped being the matte counterpart to a glazed
            // wall and became a third finish that was neither. Play called it as the wall and the
            // floor not going together.
            //
            // 0.6, not the wall's 0.2, and the asymmetry is earned: a wall is a grid of 1.7x0.9m
            // panels with near-black grooves between them, so it already has geometry breaking up its
            // reflection. The floor is one 9x10.9m slab with nothing of the sort, and its grain is the
            // only thing doing that job.
            //
            // WHAT TO WATCH IF EITHER MOVES AGAIN: specular is added on top of diffuse, so both of
            // these push the floor back toward the clipping that 0.85 albedo was chosen to stop. Judge
            // them standing under a fixture, not in the middle of the room.
            ApplySurfaceDetail(floorMat, surfaceGrain, FloorBump, new Vector2(26f, 30f),
                               FloorSmoothness);
            // **THE FLOOR LOSES ITS ROUND GLINT TOO** (2026-09-04, by request, after play checked
            // which of the two round things on a floor this was: it MOVED with the camera, so it is
            // the mirror image of a fixture rather than the pool of light one throws. A pool does not
            // move, and would have wanted a cookie instead - see NoDirectSpecular for why the two
            // have opposite answers.)
            //
            // **The floor pays more for this than the walls did, and that is worth knowing before
            // reverting or repeating it.** A wall keeps its 0.85 mirroring, so removing the phantom
            // leaves the panel's real square reflection behind it. The floor is at 0.65 with no
            // metallic, where the environment term is weak - so what is left in the glint's place is
            // close to nothing, and the floor reads flatter. That is the trade, and it is one line.
            NoDirectSpecular(floorMat);
            // Metallic is not part of `ApplySurfaceDetail` - every OTHER surface it touches (walls,
            // ceiling) is meant to stay a true dielectric, so this is set here, on the floor
            // material alone, rather than threading a new parameter through a call every other
            // caller would have to pass as zero.
            if (floorMat.HasProperty("_Metallic")) floorMat.SetFloat("_Metallic", FloorMetallic);
            EditorUtility.SetDirty(floorMat);

            (Transform root, Transform bedSpawn, ParticleSystem[] gas,
             Door[] doors, RoomCondition[] conditions,
             GhostInteractable[] signals, Transform[] rooms, Transform[] loweredRooms,
             FinalRoomSequence finalRoom, CycleExit _wayOut) =
                BuildCycleTwoShell(floorMat, grooveMat, panelMat, propMat, null);

            (Cycle cycle, _) = AssembleCycleTwo(
                root, bedSpawn, doors, conditions, signals, gas, rooms, loweredRooms, finalRoom,
                floorMat, propMat, MakeTestCardTexture("TvTestCard"), MakeStaticTexture("TvStatic", 64),
                null, null, null);

            // Anything a cycle needs must be reachable from the cycle's own path, not only from
            // `Build` - see WireDoorAudio, which this omitting is exactly how cycle 2 shipped with
            // seven silent doors.
            WireDoorAudio(doors);

            // The same lighting the full build sets up, and it has to be here rather than inherited:
            // a probe bakes what the scene is lit by, and an unlit scene bakes eight black cubemaps.
            SetupLighting();
            AddFallingToEveryCarryable();
            BakeReflectionProbes();

            // The cycle's world goes under its own component, exactly as the split leaves it, so the
            // scene has one root and `CycleSceneLoader` finds it the same way.
            root.SetParent(cycle.transform, true);
            if (cycle.wallPanels != null) cycle.wallPanels.transform.SetParent(cycle.transform, true);
            SleepCycle(root);

            Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, CycleScenePath("Cycle2"));
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[SceneBuilder] Cycle 2 rebuilt on its own at " + CycleScenePath("Cycle2")
                    + " - cycle 1 and the core scene were not touched.");
        }

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

            // **THE FLOOR ONLY.** The ceiling has had its own material since 2026-08-20
            // (`CeilingMaterial`, smoothness 0.3) and the two are further apart than ever now that
            // the floor is at `FloorSmoothness` - a glossy ceiling would hang a second copy of the
            // room over the player's head, which is not what anybody asked for.
            // **0.85, NOT WHITE** - settled 2026-08-20, and the
            // reason it is not 1.0 is measurable rather than a matter of taste. Albedo 1.0 is a
            // surface that returns every photon that hits it; nothing does, fresh white paint is
            // about 0.85, and a floor is always darker than the walls above it. With four spots at
            // intensity 10.5 pointing straight down, a 1.0 floor CLIPPED - the title screen's own
            // capture had this floor at (235, 253, 255), two channels already at the top - and play
            // read it exactly: stand in the middle, look down, it is too white.
            //
            // The alternatives were both wider. Dropping the light intensity darkens the walls with
            // it, and pulling exposure down in the volume changes every surface in the game. This
            // changes the one surface that was wrong, and it still reads WHITE: it is 15% off
            // clipping and the eye has nothing brighter on screen to compare it against.
            // Same white as every panel and the ceiling - referenced now rather than duplicated as
            // its own literal, which is how the floor and the walls drifted a hair apart in blue
            // without anyone writing that choice down. See `PanelLitColor` for why that mattered.
            Material floorMat = MakeColorMaterial("FloorWhite", PanelLitColor);
            // Sits at the bottom of every groove and inside the door pocket. Near-black so the
            // seams read the way the old painted-on grid lines did.
            Material grooveMat = MakeColorMaterial("GrooveDark", new Color(0.04f, 0.04f, 0.045f));
            Material propMat = MakeColorMaterial("PropLight", new Color(0.85f, 0.85f, 0.85f));
            // White is the resting state - what the panels are for all but the first seconds of an
            // iteration, and what the reflection probes bake against. WallPanelDisplay drives them
            // to WallPanelColor and back through a property block at runtime.
            Material panelMat = MakeColorMaterial("PanelWhite", PanelLitColor);
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
            // THE FLOOR IS NO LONGER MATTE BY COMPARISON - it was, and that pairing is what this
            // paragraph was written against, but it went to smoothness 0.65 on 2026-08-20. What now
            // separates the two is relief rather than gloss: the wall keeps its whisper of grain
            // (0.2) and the floor carries three times it (0.6), because the wall has a panel grid to
            // break up its reflection and the floor is one bare slab. See the floor's own comment.
            Texture2D surfaceGrain = MakeNoiseNormalMap("SurfaceGrain", 512, 2.5f);
            ApplySurfaceDetail(panelMat, surfaceGrain, 0.2f, new Vector2(5f, 3f), WallSmoothness);
            // The wall keeps its 0.85 mirroring of the room and loses only the round phantom a
            // point light draws on top of it - see NoDirectSpecular.
            NoDirectSpecular(panelMat);
            // Floor and ceiling: plain white, matte, with the same plaster grain the walls get -
            // just at a far higher repeat count, since a slab face is 9 x 10.9m against a wall
            // panel's 1.7 x 0.9m.
            // **BOTH NUMBERS NOW LIVE IN `FloorSmoothness` / `FloorBump`, AND BOTH HAVE MOVED
            // AGAIN** (2026-09-03: 0.65 -> 0.9 and 0.6 -> 0.12, so the floor reflects the room).
            // What follows is why they were 0.65 and 0.6, which is kept because every word of it is
            // still the argument that has to be answered before either moves a third time.
            //
            // SMOOTHNESS 0.65, UP FROM 0.3, and BUMP 0.6, DOWN FROM 1.8. Settled 2026-08-20. They are
            // one decision, not two, and the order they happened in is the whole lesson.
            //
            // The smoothness first: a floor at smoothness 0 is a perfectly uniform field, and the eye
            // reads a field with no variation in it as blown out rather than as bright - which was
            // half of what "too white" meant. At 0.65 the ceiling fixtures lay pools across the floor
            // and the 26x30 grain rides in them, so the floor has structure to read distance off.
            //
            // THEN THE BUMP, because raising the gloss is what exposed it. Wall and floor share one
            // normal map at one physical grain size (~0.35m repeat on both) and differed ONLY in this
            // number - 0.2 against 1.8, nine times apart. While the floor was matte that relief only
            // reached the diffuse and read as the tooth of sealed concrete. Glossy, the same grain
            // modulates the SPECULAR, and the floor stopped being the matte counterpart to a glazed
            // wall and became a third finish that was neither. Play called it as the wall and the
            // floor not going together.
            //
            // 0.6, not the wall's 0.2, and the asymmetry is earned: a wall is a grid of 1.7x0.9m
            // panels with near-black grooves between them, so it already has geometry breaking up its
            // reflection. The floor is one 9x10.9m slab with nothing of the sort, and its grain is the
            // only thing doing that job.
            //
            // WHAT TO WATCH IF EITHER MOVES AGAIN: specular is added on top of diffuse, so both of
            // these push the floor back toward the clipping that 0.85 albedo was chosen to stop. Judge
            // them standing under a fixture, not in the middle of the room.
            ApplySurfaceDetail(floorMat, surfaceGrain, FloorBump, new Vector2(26f, 30f),
                               FloorSmoothness);
            // **THE FLOOR LOSES ITS ROUND GLINT TOO** (2026-09-04, by request, after play checked
            // which of the two round things on a floor this was: it MOVED with the camera, so it is
            // the mirror image of a fixture rather than the pool of light one throws. A pool does not
            // move, and would have wanted a cookie instead - see NoDirectSpecular for why the two
            // have opposite answers.)
            //
            // **The floor pays more for this than the walls did, and that is worth knowing before
            // reverting or repeating it.** A wall keeps its 0.85 mirroring, so removing the phantom
            // leaves the panel's real square reflection behind it. The floor is at 0.65 with no
            // metallic, where the environment term is weak - so what is left in the glint's place is
            // close to nothing, and the floor reads flatter. That is the trade, and it is one line.
            NoDirectSpecular(floorMat);
            // Metallic is not part of `ApplySurfaceDetail` - every OTHER surface it touches (walls,
            // ceiling) is meant to stay a true dielectric, so this is set here, on the floor
            // material alone, rather than threading a new parameter through a call every other
            // caller would have to pass as zero.
            if (floorMat.HasProperty("_Metallic")) floorMat.SetFloat("_Metallic", FloorMetallic);
            EditorUtility.SetDirty(floorMat);

            GameObject room = new GameObject("Room");
            BuildShell(room.transform, floorMat, grooveMat, panelMat);

            // MADE HERE rather than beside the loop it belongs to, because cycle 2's haul room counts
            // the ghosts standing beside its load and is built moments from now. Nothing else about
            // it changes - `LoopManager` still parents every ghost to it.
            GameObject ghostParent = new GameObject("Ghosts");

            // Cycle 2, one storey down. Deliberately OUTSIDE `room` - see BuildCycleTwoShell for why
            // the panel gather below is the reason.
            (Transform cycleTwoRoot, Transform cycleTwoBedSpawn, ParticleSystem[] cycleTwoGas,
             Door[] cycleTwoDoors, RoomCondition[] cycleTwoConditions,
             GhostInteractable[] cycleTwoSignals, Transform[] cycleTwoRooms,
             Transform[] cycleTwoLoweredRooms,
             // The hatch element is DISCARDED: room2-0 stopped building its own the day cycle 3
             // existed, so this is always null now. It is built on `CycleJoin_2_3` below, in the core
             // scene, because one `CycleExit` drives a lid in two different cycles' scenes.
             FinalRoomSequence cycleTwoFinalRoom, _) =
                BuildCycleTwoShell(floorMat, grooveMat, panelMat, propMat, ghostParent.transform);

            // Every wall panel, gathered by parent name rather than threaded back out through
            // BuildShell/BuildRoomShell/BuildPanelWall - the panels are the only children of a
            // "*_Panels" node, so this stays correct without four signature changes.
            var wallPanelRenderers = new System.Collections.Generic.List<Renderer>();
            foreach (Renderer r in room.GetComponentsInChildren<Renderer>())
            {
                if (r.transform.parent == null || !r.transform.parent.name.EndsWith("_Panels")) continue;
                wallPanelRenderers.Add(r);
            }

            // What a panel shows once it fails. Generated rather than sourced, like every other
            // texture in this build, and shared by both cycles' displays.
            Texture2D testCard = MakeTestCardTexture("TvTestCard");
            Texture2D staticNoise = MakeStaticTexture("TvStatic", 64);

            WallPanelDisplay wallDisplay = MakeWallPanelDisplay(
                "WallPanelDisplay", wallPanelRenderers.ToArray(), testCard, staticNoise);

            // Cycle 2's own display is built with the rest of cycle 2, in AssembleCycleTwo - see there
            // for why everything that cycle IS now lives behind one call.

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

            // **WHERE THE PLAYER STANDS BEFORE THE FIRST ITERATION MOVES THEM**, which is now a
            // formality: iteration 1 teleports to `bedSpawn` at the top of `RunLoop` regardless, and
            // with the calibration room gone (2026-08-31) there is nothing between loading and that
            // teleport. Kept as an object rather than dropped because `BuildPlayer` wants a pose to
            // build at, and the middle of a lit room is a better one to fail into than the origin.
            GameObject calibSpawnGO = new GameObject("StartSpawn");
            calibSpawnGO.transform.SetParent(room.transform, false);
            // The bed's own pose, in the bed's own room. Nothing is ever seen from here.
            calibSpawnGO.transform.localPosition = new Vector3(0f, 0f, -0.7f);
            calibSpawnGO.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            Transform startSpawn = calibSpawnGO.transform;

            (GameObject player, FirstPersonController fpc, PlayerRecorder recorder, CameraShaker shaker, PlayerHand hand) = BuildPlayer(startSpawn, ghostInteractables);

            GhostReplayer ghostPrefab = BuildGhostPrefab();
            // Already built, above - cycle 2's haul room needs it, and that is built with the shell.
            // (kept here as a comment so the old creation site is not re-added)

            (IterationLabel label, WakeUpSequence wakeUp, Transform canvas, CanvasGroup loadingBackdrop, CaptureRig capture) = BuildUI(hand);
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

            // And the bucket's own left click, beside the chess placer and the swing. None of the
            // three can clash: each is silent unless the hand holds the one thing it acts on, and the
            // hand holds exactly one thing at a time.
            BucketPlacer bucketPlacer = player.AddComponent<BucketPlacer>();
            bucketPlacer.hand = hand;

            // And room3-2N's blocks, the fourth thing on that button and unable to clash with the
            // other three for the same reason: a block in the hand is not a pin, a piece or a pail.
            BedlamPlacer bedlamPlacer = player.AddComponent<BedlamPlacer>();
            bedlamPlacer.hand = hand;

            // And the ladder's, the fifth thing on that button. Same guarantee as the other four:
            // silent unless the hand holds the one thing it acts on.
            LadderPlacer ladderPlacer = player.AddComponent<LadderPlacer>();
            ladderPlacer.hand = hand;
            placer.board = chessBoard;
            // The mouse prompt rides the board's lit square, which is the same rule the swing prompt
            // follows: the label goes on the thing the click acts on, not on the hand it is held in.
            hints.placer = placer;
            hints.bucketPlacer = bucketPlacer;
            hints.bedlamPlacer = bedlamPlacer;
            hints.ladderPlacer = ladderPlacer;

            // The touch layer, under the pause menu so a paused game's buttons draw over it.
            TouchControls touch = BuildTouchControls(canvas);

            // The rest of the capture rig, now that the prompts, the touch layer and the player's
            // camera all exist. Everything it needs is named here rather than found at runtime.
            WireCaptureRig(capture, hints.gameObject, touch.gameObject,
                           player.GetComponentInChildren<Camera>());

            // Escape's overlay covers the HUD, the prompts and the eyelids...
            BuildPauseMenu(canvas, fpc);
            // ...and the ending covers even that. Built last so nothing in the game can draw over
            // the last thing the player sees. (Pausing is locked out for its duration anyway - see
            // PauseMenu - but the draw order should not depend on that being true.)
            EndingSequence ending = BuildEndingScreen(canvas);
            // Above even that, because it is the first thing the run shows and nothing else is
            // running while it is up.

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

            // Appended after the fact because these live in rooms built later than the hint display.
            // **THE LIST NO LONGER OPENS WITH THE CALIBRATION BUTTON** (2026-08-31): that was the one
            // E fixture outside the loop, and its room is gone. Every remaining entry is inside a
            // cycle, which is what `CycleBinding` was always the right place for.
            var hintTargets = new System.Collections.Generic.List<MonoBehaviour>(hints.interactTargets);
            // And the console's three recesses, which are E fixtures like any other now that the
            // clock runs through the room they are in.
            hintTargets.AddRange(finalRoom.slots);
            // CYCLE 2'S OWN E FIXTURES. Gathered off the cycle root rather than threaded back out of
            // `BuildCycleTwoShell`, so every switch a later room adds joins this list by existing -
            // the prompt is the only thing that tells a player a fixture is a fixture at all, and a
            // new one silently missing from here is exactly the fault that shipped in the last build.
            // Inactive included: the cycle starts asleep.
            hintTargets.AddRange(cycleTwoRoot.GetComponentsInChildren<LightSwitch>(true));
            hintTargets.AddRange(cycleTwoRoot.GetComponentsInChildren<WaterTap>(true));
            // Room2-0's four recesses, and room2-6's three valves, on the same footing. Gathered by
            // type off the root for the reason stated above rather than named one by one.
            hintTargets.AddRange(cycleTwoRoot.GetComponentsInChildren<FinalSlot>(true));
            hintTargets.AddRange(cycleTwoRoot.GetComponentsInChildren<Valve>(true));
            hints.interactTargets = hintTargets.ToArray();
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
            // **AND THE CONTROLS, ON THE SAME WALL AND NEVER AT THE SAME TIME.** This one is
            // iteration 1 only and the N sign below is iteration 2 onward, which is the whole of how
            // they share the south wall. See `BuildControlsWall`.
            BuildControlsWall(room.transform, 0f);

            // AND THE NOTICE ON THE FLOOR IN FRONT OF IT. Between the bed and the wall, off the
            // centre line so it is not the first thing the wake-up's own camera move sweeps over -
            // found by walking toward the pictograms rather than handed over before the player has
            // moved. **It is not iteration-1 only**, unlike the wall above: a physical object that
            // vanished at the top of iteration 2 would be the one thing in this game that does not
            // obey the loop.
            // **TURNED END FOR END** (2026-08-31, by request). 152 had the page's head pointing back
            // at the bed, so a player walking up to it from the wake-up read it upside down. 332 is
            // the same angle across the floor with the writing the right way up for somebody
            // approaching from the bed.
            BuildIntakeNotice(room.transform, new Vector3(0.55f, 0f, -3.6f), 332f);

            PanelMessage wallMessage = BuildWallMessage(room.transform, 0f);
            wallMessage.showFromIteration = 2;
            // Long enough to clear "Iteration 2, 60 seconds remaining." Announcements replace each
            // other rather than stacking, so a chime at t=0 in this room truncates the loop's own
            // line - which no room had to worry about while this lived three rooms away.
            wallMessage.announceDelay = 5f;
            wallMessage.retireOnEndCycle = canvas.GetComponentInChildren<EndCycleControl>(true);

            // EVERY DOOR IN THE GAME, both cycles, and cycle 2's were missing until 2026-08-15: this
            // took a hand-written list of cycle 1's five, so the ring's seven were built with no
            // AudioSource and no clip and opened in silence. Nothing reported it because a door that
            // makes no sound looks exactly like a door.
            //
            // Concatenated rather than a second call, because the pads and the wake-up in there are
            // one-per-game and would be wired twice.
            var everyDoor = new System.Collections.Generic.List<Door>
                { door, doorRed, doorBlue, doorYellow, door3 };
            everyDoor.AddRange(cycleTwoDoors);

            (NarrationDirector narration, RoomAmbience ambience) =
                BuildAudio(player, everyDoor.ToArray(), wakeUp);

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
            CheckGhostSignals("Cycle 1", ghostInteractables);
            CheckCycleFinishable("Cycle 1", room.transform, finalRoom);
            cycleOne.ghostInteractables = ghostInteractables;
            cycleOne.wallPanels = wallDisplay;
            cycleOne.worldRoot = room.transform;

            // CYCLE 2. A bed, a room and nothing else yet - which is exactly what the boundary needs
            // to be exercised, and no more.
            //
            // AND IT ENDS THE SAME WAY CYCLE 1 DOES, since 2026-08-20: room2-0's four pedestals are
            // its console, four billiard balls are its objects, and `Cycle.Complete` is a real
            // question again.
            (Cycle cycleTwo, WallPanelDisplay cycleTwoDisplay) = AssembleCycleTwo(
                cycleTwoRoot, cycleTwoBedSpawn, cycleTwoDoors, cycleTwoConditions, cycleTwoSignals,
                cycleTwoGas, cycleTwoRooms, cycleTwoLoweredRooms, cycleTwoFinalRoom, floorMat,
                propMat, testCard, staticNoise, shaker, hand, narration);


            // CYCLE 3. One sealed room under room2-0's hatch: a bed, an empty chest and the gas.
            // What cycle 2 was on the day it was started, and for the same reason - the boundary is
            // the thing being exercised, and a puzzle would be in the way of testing it.
            (Transform cycleThreeRoot, Transform cycleThreeBedSpawn, ParticleSystem[] cycleThreeGas,
             Transform cycleThreeRoom, GhostInteractable[] cycleThreeSignals,
             CrushingBarrier cycleThreeBarrier, BeamLift[] cycleThreeLifts) =
                BuildCycleThreeShell(floorMat, grooveMat, panelMat, propMat);

            // THE JOIN BETWEEN CYCLE 2 AND CYCLE 3, and it is in the CORE scene for the reason
            // cycle 1's is: one `CycleExit` drives a lid in the floor above and a lid in the ceiling
            // below, and a reference across a scene boundary comes back null with no error.
            //
            // Positioned so that `BuildCycleExit`'s own `+CycleExitZ` lands the hatch exactly on
            // room2-0's centre, which is where its floor hole is cut. The Y is room2-0's floor plane.
            GameObject join23 = new GameObject("CycleJoin_2_3");
            join23.transform.position =
                new Vector3(CycleThreeX, CycleThreeFloorY + StoreyDrop, CycleThreeZ - CycleExitZ);

            CycleExit cycleTwoExit = BuildCycleExit(join23.transform, 0f, floorMat, fpc.transform);
            if (cycleTwoFinalRoom != null) cycleTwoFinalRoom.wayOut = cycleTwoExit;

            // And the tube the player falls down, joining room2-0's floor to room3-1's ceiling.
            BuildExitShaft(join23.transform, "ExitShaft_Cycle2", 0f, floorMat);

            AssertUnderHatch(cycleTwoExit != null ? cycleTwoExit.transform : null);

            (Cycle cycleThree, WallPanelDisplay cycleThreeDisplay) = AssembleCycleThree(
                cycleThreeRoot, cycleThreeBedSpawn, cycleThreeGas, cycleThreeSignals,
                testCard, staticNoise, cycleThreeBarrier, cycleThreeLifts);

            // CYCLE 4, and the join into it. The same three objects cycle 3's boundary is made of,
            // in the same order and for the same reasons - the only thing different is the height
            // the hatch is at, because room3-0 is two storeys up inside room3-2N.
            (Transform cycleFourRoot, Transform cycleFourBedSpawn, ParticleSystem[] cycleFourGas,
             GhostInteractable[] cycleFourSignals) =
                BuildCycleFourShell(floorMat, grooveMat, panelMat, propMat);

            GameObject join34 = new GameObject("CycleJoin_3_4");
            join34.transform.position =
                new Vector3(CycleFourX, CycleFourFloorY + StoreyDrop, CycleFourZ - CycleExitZ);

            // **UNDER CYCLE 3, SO IT IS IN CYCLE 3'S SCENE** (2026-09-03).
            //
            // This sat at the top of the core scene, and play kept reporting a hard-edged white
            // rectangle in the middle of room3-2N's floor. That is this lid, and the reason is that
            // **A SURFACE HAS TO BE BAKED IN THE SAME SCENE AS THE GEOMETRY THAT LIGHTS IT.**
            // Lightmapping is per scene: `BakeLighting` bakes `Cycle3` with room3-2N's walls, floor
            // and ceiling in it, and the lid was not there to receive any of that. It was also not
            // baked at all - `IterationRoom` has no ProbeVolume, so the bake refuses it outright -
            // but giving the core scene one would not have helped, because a bake of the core scene
            // contains no room: the lid would come out lit as a slab floating in an empty world,
            // wrong in a different way.
            //
            // **THE COST, WHICH IS REAL AND IS NOT PAID TODAY.** It now sleeps with cycle 3. This lid
            // is room3-2N's FLOOR and also room4-1's CEILING, so if cycle 4 is ever made playable
            // (`Cycle.playable`) the room below loses its ceiling the moment cycle 3 is put to
            // sleep. Cycle 4 is built and not played, and the lid's job as CLAUDE.md states it is
            // the floor - keeping the walk to the cable car off an open pit - so cycle 3 is the
            // right owner now. Whoever turns cycle 4 on has to look here.
            //
            // `SetParent` keeps the world position, and the two cross-scene references this creates
            // are already rebound at runtime: `CycleBinding` sets `exit.player` and
            // `finalRoom.wayOut`. `finalRoom` is in this scene too, so that one stops crossing a
            // boundary at all.
            join34.transform.SetParent(cycleThreeRoot, true);

            CycleExit cycleThreeExit = BuildRoomSizedExit(join34.transform, floorMat, fpc.transform);
            if (cycleThree != null && cycleThree.finalRoom != null)
                cycleThree.finalRoom.wayOut = cycleThreeExit;

            // The skirt round the void between room3-2N's floor and room4-1's walls. Sized to the
            // OPENING rather than to a grid cell, for the same reason the lid is.
            BuildRoomSizedSkirt(join34.transform, floorMat);

            AssertCycleFourClear(cycleThreeRoot, cycleFourRoot);
            AssertUnderCycleFourHole(cycleFourRoot);

            (Cycle cycleFour, WallPanelDisplay cycleFourDisplay) = AssembleCycleFour(
                cycleFourRoot, cycleFourBedSpawn, cycleFourGas, cycleFourSignals,
                testCard, staticNoise);

            // **CYCLE 4 IS BUILT AND NOT PLAYED** (2026-08-31, by request). Everything above stays:
            // the shell, the bed, the chest, its own scene, its place in `CycleSceneNames`. What
            // changes is that `LoopManager` will not offer it a bed - see `Cycle.playable`, which is
            // the whole of the switch and the whole of turning it back on.
            //
            // WHY BUILT AT ALL, when the game stops before it. Because the ending FLIES PAST IT. The
            // cable car leaves room3-2N and climbs the outside of the building, and the last thing
            // under it as it pulls away is a finished cell with nothing in it - which says "there is
            // more of this" in the one register this game has ever used, which is architecture. A
            // cycle 4 deleted to end the game at three would have taken that shot with it.
            cycleFour.playable = false;

            // THE ENDING'S DEPARTURE. Built last of all, because it measures itself off every cycle
            // in the building and all four have to exist first - the shaft clears the eastmost wall
            // of any of them, and the flight path has a waypoint at each one's floor.
            Transform roomNorth = cycleThreeRoot.Find("Room3_2N_Root/Room3_2N");
            if (roomNorth == null)
                Debug.LogError("[SceneBuilder] No Room3_2N under cycle 3 - the ending has no room to "
                             + "put its board, its breach or its cable car in, and the game will "
                             + "reach the last cycle and simply stop. Check what BuildBigRoom named it.");

            EndingDeparture departure = BuildEndingDeparture(
                roomNorth, cycleThreeRoot,
                new[] { room.transform, cycleTwoRoot, cycleThreeRoot, cycleFourRoot },
                fpc.transform, fpc, shaker, ghostPrefab, ghostParent.transform,
                floorMat, panelMat, grooveMat);
            if (cycleThree != null) cycleThree.departure = departure;

            GameObject loopGO = new GameObject("LoopManager");
            LoopManager loop = loopGO.AddComponent<LoopManager>();
            loop.loopDuration = 60f;
            // Taken down once the cycles are in and the game knows what it is showing - see
            // LoopManager.RunLoop.
            loop.loadingBackdrop = loadingBackdrop;
            // NOT ASSIGNED HERE ANY MORE. `cycles` is filled at runtime from `CycleSceneLoader`,
            // because each cycle is a scene of its own and Unity drops a serialized reference that
            // crosses one. Assigning it would look right in the Inspector and be null in the player.
            CycleSceneLoader sceneLoader = loopGO.AddComponent<CycleSceneLoader>();
            sceneLoader.cycleSceneNames = CycleSceneNames;
            loop.sceneLoader = sceneLoader;
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
            // Both live on the HUD canvas, found the same way the wall sign finds the control it
            // teaches. Reset and driven at a cycle boundary respectively.
            loop.endCycleControl = canvas.GetComponentInChildren<EndCycleControl>(true);
            loop.sleepingGas = canvas.GetComponentInChildren<SleepingGas>(true);
            // The room-side half of the gas. The wash on the canvas is what it feels like; the vapour
            // is what it looks like.
            if (loop.sleepingGas != null) loop.sleepingGas.emitters = cycleTwoGas;

            // AND THE SAME WIRING AGAIN, AT RUNTIME. Everything above is correct and stays; this is
            // the groundwork for splitting the cycles into scenes of their own, where every reference
            // that crosses one comes back NULL and says nothing about it. See CycleBinding.
            CycleBinding binding = loopGO.AddComponent<CycleBinding>();
            loop.binding = binding;
            binding.loop = loop;
            binding.hand = hand;
            binding.cameraShaker = shaker;
            binding.narration = narration;
            binding.player = fpc.transform;
            binding.sleepingGas = loop.sleepingGas;
            binding.hints = hints;
            binding.endCycleControl = loop.endCycleControl;
            binding.placer = placer;
            binding.bucketPlacer = bucketPlacer;
            binding.bedlamPlacer = bedlamPlacer;
            binding.ladderPlacer = ladderPlacer;
            binding.swingTool = player.GetComponent<BalloonTool>();
            // Where the ending's frozen past selves are parented - core scene, so it crosses like
            // everything else here. See the departure block in `CycleBinding.Bind`.
            binding.ghostParent = ghostParent.transform;
            // One per cycle, in cycle order. Cycle 2's is null, and that null is what says it is the
            // last cycle - see LoopManager, which derives "last" from having no successor.
            // BOTH CYCLES HAVE ONE NOW. Cycle 1's sits on the JOIN between the two storeys, outside
            // either cycle root, because it is a hole through both of them; cycle 2's is inside
            // room2-0, because there is nothing under that floor for it to join TO - it is a hatch in
            // the last room rather than a seam between two. When cycle 3 exists, it becomes a seam and
            // moves out here with cycle 1's.
            // THREE CYCLES, AND THE LAST ONE'S NULL IS WHAT MAKES IT LAST. Cycle 3 has no successor
            // and no hatch of its own yet; `LoopManager` derives "last" from the array rather than
            // from a count, so the day cycle 4 exists this grows by one entry.
            binding.wayOuts = new[] { cycleOneExit, cycleTwoExit, cycleThreeExit, null };
            // **NOTHING BELONGS TO NO CYCLE ANY MORE.** This held exactly one entry, the calibration
            // room's start button, because that room ran before the first iteration and could not be
            // gathered off a cycle root. The room is gone (2026-08-31) and the field stays: it is the
            // right shape for the next fixture that lives outside the cycles, and an empty array says
            // "none today" where deleting it would make the next one a structural change.
            binding.coreHintTargets = new MonoBehaviour[0];

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

            // BEFORE THE BAKE, and that is the point rather than an ordering detail. This leaves every
            // fixture at `LightShadows.None` and hands the list to a runtime component - so the probe
            // cubemaps, which render 360 degrees and see most of the corridor at once, bake without
            // asking for thirty shadow maps apiece. See `ShadowBudget`.
            WireShadowBudget();

            Debug.Log($"[SceneBuilder] Panel meshes: {panelMeshNames.Count} distinct sizes in use "
                    + $"({panelMeshesBuilt} generated this run, the rest served from disk). Identical "
                    + "grid cells share one asset, so the building goes on batching; the partial "
                    + "panels around doorways are what mint new ones. Hundreds here would mean the "
                    + "sizes have stopped repeating - see ChamferedPanelMesh.");

            CheckHintAnchors();
            CheckWallSmoothness();

            BakeReflectionProbes();

            // **THE CORE SCENE HAS NO PROBE VOLUME ANY MORE**, and that is not an omission. It had
            // one for the calibration room alone - the only ROOM the core scene ever held - and with
            // that room gone (2026-08-31) what is left out here is the player, the canvas and the
            // ghost prefab. None of them is lit by baked bounce. The cycles still get theirs in
            // `SplitCyclesIntoScenes`.

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
            SleepCycle(cycleFourRoot);

            // AND CYCLE 1, which used to ship AWAKE because it is the one the game opens in - and that
            // is precisely the bug. Scenes load additively and asynchronously, so between a cycle's
            // scene arriving and `LoopManager` deciding which one should be awake there are frames
            // where whatever the scene was SAVED with is on screen. Starting the game at cycle 2 (the
            // debug shortcut) therefore opened on a flash of cycle 1's rooms seen from cycle 2's bed.
            //
            // With every root asleep on disk, a loading cycle is never visible and the decision about
            // which one is awake is made in exactly one place - see LoopManager, which now wakes one
            // unconditionally rather than relying on a scene's saved state.
            SleepCycle(room.transform);

            Directory.CreateDirectory("Assets/Scenes");

            // SAVED BEFORE THE SPLIT, AND AGAIN AFTER IT, and the first save is not optional: a scene
            // that has never been written to disk is "untitled", and Unity refuses to open a second
            // scene additively alongside an untitled one. The split creates the cycle scenes exactly
            // that way, so without this it fails outright.
            EditorSceneManager.SaveScene(scene, ScenePath);

            // THE SPLIT, AND IT HAPPENS LAST FOR A REASON. Everything above builds and bakes in ONE
            // scene, exactly as it always did - a reflection probe renders the world around it, so it
            // has to be baked while that world is still assembled. Only once the cubemaps are on disk
            // does each cycle move out into a scene of its own.
            SplitCyclesIntoScenes(new[] { cycleOne, cycleTwo, cycleThree, cycleFour },
                                  new[] { wallDisplay, cycleTwoDisplay, cycleThreeDisplay,
                                          cycleFourDisplay });

            // And again, because the cycles have just left it. The first save wrote a scene that still
            // contained them.
            EditorSceneManager.SaveScene(scene, ScenePath);

            // THE TITLE SCREEN'S PHOTOGRAPH, and it is FRAMED rather than borrowed.
            //
            // It used to be taken from the bed spawn, on the reasoning that the first frame of the
            // game is the honest thing to advertise. What that actually produces is a wall: Room1 is
            // an empty white box, the spawn looks straight down it, and the shot came back as one
            // flat grey panel grid filling the frame with the bright part of the room - the floor,
            // the ceiling fixtures, the bed - all outside it.
            //
            // **AND THEN IT WAS FRAMED AGAIN, 2026-08-20, SQUARE TO ONE WALL.** The corner shot did
            // put the whole room in frame and that turned out to be the problem: two wall grids
            // running in different directions with the ceiling in a third, converging on a vanishing
            // point in the middle of the picture. There is no calm area anywhere in it, and the menu
            // sets its type over near-black grooves on white at full contrast.
            //
            // A wall parallel to the image plane does not converge AT ALL - every groove projects
            // perfectly horizontal or perfectly vertical - so the same room, shot square, reads as a
            // designed surface rather than as a screenshot. That is the whole change: the room is
            // just as bright and just as white, and the geometry stops arguing with the words.
            //
            // Safe to move both the player and the camera here: the scene has already been saved
            // above, and this in-memory edit is discarded when the room is reopened from disk at the
            // end of this method.
            player.transform.SetPositionAndRotation(bedSpawn.position, bedSpawn.rotation);

            // AWAKE FOR THE PHOTOGRAPH, then straight back to sleep. Cycle 1's root ships asleep now
            // (see SleepCycle, and the flash it fixes) and this runs after that, so without waking it
            // the capture is a photograph of an empty scene and the title screen goes black.
            bool wasAwake = room.activeSelf;
            room.SetActive(true);

            Camera shotCam = player.GetComponentInChildren<Camera>();
            if (shotCam != null)
            {
                // Room1 is centred on the origin: 8.75 across, 10.5 deep, 5.41 to the ceiling. These
                // are world coordinates because that is the frame the room is built in.
                //
                // THE SOUTH WALL, AND IT IS CHOSEN FOR WHAT IT DOES NOT HAVE. Room1 is built with
                // `Rect.zero` for its south cutout, so that wall is the one unbroken panel grid in the
                // building - no doorway, no pocket, no fixture. And every piece of furniture in the
                // room is in the +Z half (the nightstand is at z=1.35 and the bed beside it), so a
                // camera at z=+0.5 facing -Z has all of it BEHIND the lens rather than in the shot.
                //
                // Dead centre in X on purpose. A grid is symmetrical and a picture of one that is
                // nearly-but-not-quite centred reads as a mistake; the asymmetry on this screen is the
                // menu column, which is where asymmetry belongs.
                // **SQUARE TO THE FAR WALL, FROM THE BACK OF THE ROOM.** The first square shot filled
                // the frame with the wall alone and it came back as wallpaper: fifteen cells, no
                // depth, and a surface that is in shade at that angle so the room stopped being
                // bright - which is the one thing the picture had going for it.
                //
                // Standing back instead puts the floor, the ceiling and both side walls in frame with
                // the far wall small in the middle. **The front wall's grid still does not converge**
                // - that is what "square" buys and it is kept - while everything else runs to a single
                // vanishing point dead centre. One-point perspective is the most deliberate-looking
                // thing a room can do, and it costs nothing but where the camera stands.
                //
                // Facing the NORTH wall, which is the one with the doorway: the shot gets a subject
                // (a door in a white wall, which is the whole game) and the bed in the middle
                // distance, without either being the corner-shot clutter this replaced.
                const float wallZ = RoomDepth / 2f;                   // +5.25
                const float standoff = 9.85f;                         // camera to wall
                // Wider than the flat shot's 40 because the frame now has to reach the side walls, and
                // still narrower than the player's 60 - a 60 here bows the grid at the corners.
                // **COMPOSED FOR THE OVERSCANNED VIEW, NOT THE RAW ONE.** `MenuBackdrop` draws this
                // image 8% past every edge so it has room to drift, which magnifies it by 1.16 and
                // crops that 8% off - so the picture a player actually sees is TIGHTER than the file.
                // Play caught it as "the composition was good until it started moving".
                //
                // The magnification cannot be removed; an image can only be slid inside a window if it
                // is bigger than the window. So the capture is widened by the same factor instead:
                // 36 degrees is the framing that was wanted, tan(18) x 1.16 is what has to be captured
                // to still see 36 of it after the crop, and that is 41.3.
                const float shotFov = 41.3f;
                // **THE CEILING IS FRAMED OUT ENTIRELY, and that is the fix rather than a compromise.**
                //
                // It is the darkest surface in the building by a wide margin - it faces away from every
                // downlight and is lit by ambient alone - so in a shot that included it, it read as a
                // hole in the roof rather than as a surface. Lighting it was tried and reverted: an
                // emissive ceiling fixes the picture and makes the ROOM wrong, and the room is not the
                // menu's to change.
                //
                // A surface that is not in the frame cannot be the wrong colour. What is left is what
                // the downlights actually hit - the floor at (235,253,255) and the walls - which is
                // also what the player looks at in play, so the menu and the game agree.
                //
                // A LEVEL CAMERA IS NON-NEGOTIABLE - pitching down would put more floor in frame and
                // take the square grid with it, because the far wall only projects without convergence
                // while it is parallel to the image plane. So the ceiling has to be excluded by HEIGHT
                // and LENS together, and the two trade against each other: the top of the frame lands
                // at `eyeY + standoff * tan(fov/2)`, which has to stay under the 5.41m wall.
                //
                // 2.00m and 41.3 degrees puts the top of the CAPTURE at 5.71m, which is 0.30m above
                // the wall - so the file does contain a sliver of ceiling, in its top 4.1%. That is
                // deliberate and it is safe: `MenuBackdrop` slides this image within an 8% margin and
                // only ever spends `verticalRatio` (0.22) of the vertical one, so the top 6.2% of the
                // file is cropped away no matter where the drift is. 4.1 sits inside 6.2 with room to
                // spare, and what the player sees is the 36-degree framing with no ceiling in it.
                //
                // **The two numbers are coupled**: widen the lens or raise the camera and more ceiling
                // enters the file; raise `MenuBackdrop.verticalRatio` and less of the top is hidden.
                const float eyeY = 2.0f;

                Vector3 eye = new Vector3(0f, eyeY, wallZ - standoff);
                shotCam.fieldOfView = shotFov;
                shotCam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(Vector3.forward, Vector3.up));
                Debug.Log($"[SceneBuilder] Menu background: square to the north wall from {eye}, fov {shotFov}");
            }

            CaptureMenuBackground(shotCam, room.transform);
            CaptureAppIcon(shotCam);
            CaptureRoom1Shot(shotCam);
            room.SetActive(wasAwake);

            BuildMainMenuScene();

            // The build settings scene list was empty, so a standalone player would have shipped
            // with no scenes at all. Reasserted on every build rather than set once, because the
            // list lives in ProjectSettings and nothing else here maintains it.
            //
            // MainMenu is index 0, so that is where a standalone player opens.
            // And every cycle scene after them. A scene that is not in this list cannot be loaded by
            // name at runtime at all - `SceneManager.LoadSceneAsync` simply returns null - so a cycle
            // missing from here is a cycle that does not exist in a built player.
            var buildScenes = new System.Collections.Generic.List<EditorBuildSettingsScene>
            {
                new EditorBuildSettingsScene(MenuScenePath, true),
                new EditorBuildSettingsScene(ScenePath, true),
            };
            foreach (string name in CycleSceneNames)
                buildScenes.Add(new EditorBuildSettingsScene(CycleScenePath(name), true));
            EditorBuildSettings.scenes = buildScenes.ToArray();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // BuildMainMenuScene left the menu open. Put the room back, so building from the GUI
            // leaves the Editor looking at the thing that was just built.
            EditorSceneManager.OpenScene(ScenePath);

            Debug.Log("[SceneBuilder] IterationRoom scene built at " + ScenePath);
            Debug.Log("[SceneBuilder] MainMenu scene built at " + MenuScenePath);
        }
    }
}
