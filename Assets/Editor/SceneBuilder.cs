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
        private const string FurnitureDir = "Assets/ArtAssets/Furniture";
        private const string SettingsDir = "Assets/Settings";
        // Wall panel albedo. Near-white is the reference film's clinical room; the dark value reads
        // as a switched-off display, which only becomes legible because the panels are glossy and
        // have a reflection probe to mirror - a matte dark panel would just be a black hole.
        // Kept well clear of GrooveDark (0.04) so the seams still read against it.
        private static readonly Color WallPanelColor = new Color(0.13f, 0.135f, 0.15f);

        private const string TexturesDir = "Assets/Textures";
        private const string IconsDir = TexturesDir + "/Icons";
        private const string MenuBackgroundPath = TexturesDir + "/MenuBackground.png";
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

        // The sensitivity room. Deliberately NOT a multiple of RoomPitch in the positive direction
        // - it is not part of the chain and must never be walked into, so it sits behind Room1 with
        // a room's worth of nothing between them.
        private const string CalibrationRoomName = "CalibrationRoom";
        private const float CalibrationRoomZ = -2f * RoomPitch;

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

            GameObject displayGO = new GameObject("WallPanelDisplay");
            WallPanelDisplay wallDisplay = displayGO.AddComponent<WallPanelDisplay>();
            wallDisplay.panels = wallPanelRenderers.ToArray();
            wallDisplay.offColor = WallPanelColor;
            wallDisplay.onColor = Color.white;

            // Four recessed downlights per room, plus Trilight ambient standing in for the bounce
            // URP is not computing. The scene's default Directional Light is deleted rather than
            // dimmed - these are sealed boxes with a ceiling slab, so a sun has no way in.
            SetupLighting();
            BuildPostProcessing();
            ConfigureAmbientOcclusion();
            ConfigureLightingPipeline();

            (Transform bed, Transform bedSpawn) = BuildBed(room.transform, propMat);
            GameObject nightstand = BuildNightstand(room.transform);
            (Drawer drawer, CarryableItem tool) = BuildNightstandDrawer(room.transform, nightstand, propMat);
            // Out in the open floor area past the foot of the bed, matching room_layout_sample.png.
            FloorButton floorButton = BuildFloorButton(room.transform, propMat, "FloorButton",
                new Vector3(2.8f, 0.03f, -1.75f));
            Door door = BuildPadDoor(room.transform, "Door", 0f, new[] { floorButton }, propMat);

            // Room2: a roomful of balloons and a key door. The key door is not a GhostInteractable -
            // carrying is not part of a recording, so a ghost cannot open it for you. Getting
            // yourself to it holding the key is the last thing the room asks.
            (Door door2, KeyLock keyLock) = BuildKeyDoor(room.transform, RoomPitch, propMat);
            (BalloonField balloonField, CarryableItem key) = BuildBalloons(room.transform);

            // Room3: FOUR pads and one door that needs all of them at once. Deliberately the
            // plainest room of the three - no items, nothing to search, nothing to carry. Room2
            // already costs the player a key retrieval every iteration, and a second expensive room
            // behind it would be unreachable rather than hard. What Room3 costs is ITERATIONS: one
            // to stand on each pad, and a fifth to walk through while four past selves hold them.
            //
            // Laid out as a rectangle rather than scattered. Four things in a regular grid read as
            // one set at a glance - the player should never have to hunt for the fourth - and the
            // symmetry says "all of these" where an irregular arrangement would invite guessing
            // that some subset might do.
            //
            // All four sit off the centre line, so the straight walk from the south door to the
            // north one steps on none of them. Standing on one is the only way to learn what the
            // others are for, so the room has to show them together and never trigger by accident.
            const float roomThreeZ = 2f * RoomPitch;
            FloorButton[] roomThreePads =
            {
                BuildFloorButton(room.transform, propMat, "FloorButton3A", new Vector3(-3.2f, 0.03f, roomThreeZ - 3f)),
                BuildFloorButton(room.transform, propMat, "FloorButton3B", new Vector3(3.2f, 0.03f, roomThreeZ - 3f)),
                BuildFloorButton(room.transform, propMat, "FloorButton3C", new Vector3(-3.2f, 0.03f, roomThreeZ + 3f)),
                BuildFloorButton(room.transform, propMat, "FloorButton3D", new Vector3(3.2f, 0.03f, roomThreeZ + 3f)),
            };
            Door door3 = BuildPadDoor(room.transform, "Door3", roomThreeZ, roomThreePads, propMat);

            // The way out, and the only exit condition in the game. Sat on Room3's north threshold
            // rather than past it: the FarCap is right behind that doorway and the pocket in front
            // of it is too shallow for the controller to stand in, so there is no "through" to
            // detect. See EscapeTrigger.
            GameObject escapeGO = new GameObject("EscapeTrigger");
            escapeGO.transform.SetParent(room.transform, false);
            escapeGO.transform.localPosition = new Vector3(0f, 0f, roomThreeZ + RoomDepth / 2f);
            EscapeTrigger escape = escapeGO.AddComponent<EscapeTrigger>();
            escape.door = door3;
            // Half the doorway plus a little, so only a player actually in the opening qualifies -
            // without this, anyone at the same Z anywhere along the north wall would count.
            escape.halfWidth = DoorWidth / 2f + 0.05f;

            // Room3 is also where the room finally explains the end-cycle control, on all four
            // walls. Narration is wired after BuildAudio, below.
            PanelMessage wallMessage = BuildWallMessage(room.transform, roomThreeZ);

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
                roomThreePads[0], roomThreePads[1], roomThreePads[2], roomThreePads[3],
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
            BuildControlHints(canvas, player.GetComponentInChildren<Camera>(), hand,
                new MonoBehaviour[] { drawer, tool, keyLock, key });

            // Escape's overlay covers the HUD, the prompts and the eyelids...
            BuildPauseMenu(canvas, fpc);
            // ...and the ending covers even that. Built last so nothing in the game can draw over
            // the last thing the player sees. (Pausing is locked out for its duration anyway - see
            // PauseMenu - but the draw order should not depend on that being true.)
            EndingSequence ending = BuildEndingScreen(canvas);
            // Above even that, because it is the first thing the run shows and nothing else is
            // running while it is up.
            SensitivityCalibration calibration = BuildCalibrationPage(canvas);

            (NarrationDirector narration, RoomAmbience ambience) =
                BuildAudio(player, new[] { door, door2, door3 },
                           new[] { floorButton, roomThreePads[0], roomThreePads[1], roomThreePads[2], roomThreePads[3] },
                           wakeUp);

            wallMessage.narration = narration;

            GameObject loopGO = new GameObject("LoopManager");
            LoopManager loop = loopGO.AddComponent<LoopManager>();
            loop.loopDuration = 60f;
            loop.bedSpawnPoint = bedSpawn;
            loop.playerRecorder = recorder;
            loop.playerController = fpc;
            loop.ghostInteractables = ghostInteractables;
            loop.doors = new[] { door, door2, door3 };
            loop.drawers = new[] { drawer };
            loop.playerHand = hand;
            loop.balloonField = balloonField;
            loop.ghostPrefab = ghostPrefab;
            loop.ghostParent = ghostParent.transform;
            loop.iterationLabel = label;
            loop.wakeUpSequence = wakeUp;
            loop.narration = narration;
            loop.ambience = ambience;
            loop.wallPanels = wallDisplay;
            loop.cameraShaker = shaker;
            loop.escapeTrigger = escape;
            loop.endingSequence = ending;
            loop.calibration = calibration;

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
        // than a failed build.
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
            Texture2D shot = null;

            try
            {
                cam.targetTexture = rt;

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
                shot = new Texture2D(width, height, TextureFormat.RGB24, false);
                shot.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
                shot.Apply();

                File.WriteAllBytes(MenuBackgroundPath, shot.EncodeToPNG());
            }
            finally
            {
                // Restored in a finally: leaving a target texture on the player camera would mean
                // the game renders into a RenderTexture instead of the screen, and the saved scene
                // would carry it.
                cam.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                if (shot != null) Object.DestroyImmediate(shot);
                rt.Release();
                Object.DestroyImmediate(rt);
            }

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
            so.FindProperty("m_Settings.Downsample").boolValue = false;
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
            // 4096, not 2048: six shadowed fixtures share one atlas, and at 2048 URP logs
            // "Reduced additional punctual light shadows resolution by 2 to make 6 shadow maps
            // fit" and quietly drops each map to 512.
            SetEnumByName(so, "m_AdditionalLightsShadowmapResolution", "_4096");
            SetIfPresent(so, "m_SoftShadowsSupported", true);

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
        private static void BuildCeilingLights(Transform parent, string roomName, float zCenter, Material emissiveMat, bool castShadows)
        {
            GameObject root = new GameObject(roomName + "_CeilingLights");
            root.transform.SetParent(parent, false);

            // Two columns on the wall-panel rhythm, two rows down the room's length.
            float[] xs = { -GridCellWidth, GridCellWidth };
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
                    Prim(PrimitiveType.Cube, "Panel", fixture.transform,
                        new Vector3(0f, -panelThickness / 2f, 0f),
                        new Vector3(panelSize, panelThickness, panelSize),
                        emissiveMat, removeCollider: true);

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
                }
            }
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

        // The same silhouette BuildKeyShape cuts in three dimensions: ring bow, shaft, two teeth
        // off one side only. A symmetrical bit would read as a cross.
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
        private static void BuildReflectionProbe(Transform parent, string roomName, float zCenter)
        {
            GameObject go = new GameObject(roomName + "_ReflectionProbe");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, RoomHeight * 0.5f, zCenter);

            ReflectionProbe probe = go.AddComponent<ReflectionProbe>();
            probe.mode = UnityEngine.Rendering.ReflectionProbeMode.Baked;
            probe.boxProjection = true;
            probe.size = new Vector3(RoomWidth, RoomHeight, RoomDepth);
            // 512, not 256: at the wall smoothness used here the reflection is sharp enough that a
            // 256 cubemap shows the ceiling fixtures as vague smears rather than panels.
            probe.resolution = 512;
            probe.clearFlags = ReflectionProbeClearFlags.Skybox;
            probe.nearClipPlane = 0.05f;
            probe.farClipPlane = 40f;

            Directory.CreateDirectory(TexturesDir);
            Lightmapping.BakeReflectionProbe(probe, $"{TexturesDir}/{roomName}_Reflection.exr");
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

        // A lit material you can see through. Same URP transparent set-up as MakeGhostMaterial -
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
            mat.SetFloat("_Blend", 0f);
            mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.DisableKeyword("_ALPHATEST_ON");
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

        private static void BuildShell(Transform parent, Material floorMat, Material grooveMat, Material panelMat)
        {
            // The doorway is cut out of the panelling as an exact rectangle, so panels frame the
            // door instead of the door having to fit a whole number of cells.
            Rect doorway = new Rect(-DoorWidth / 2f, 0f, DoorWidth, DoorHeight);

            // Three identical rooms in a line, each sharing a divider with the next: a room's north
            // wall and its neighbour's south wall face each other across the door pocket, each with
            // the same doorway cut out of its panelling, its backing and its collision, so the
            // opening is a real hole. Room1 is the only one with no doorway to the south.
            BuildRoomShell(parent, "Room1", 0f, floorMat, grooveMat, panelMat, Rect.zero, doorway);
            BuildRoomShell(parent, "Room2", RoomPitch, floorMat, grooveMat, panelMat, doorway, doorway);
            BuildRoomShell(parent, "Room3", 2f * RoomPitch, floorMat, grooveMat, panelMat, doorway, doorway);

            BuildDoorPocketFill(parent, "DoorPocketFill_1", 0f, grooveMat, capFarSide: false);
            BuildDoorPocketFill(parent, "DoorPocketFill_2", RoomPitch, grooveMat, capFarSide: false);
            // Room3's north doorway has no room behind it yet, so its pocket is capped: opening
            // that door reveals a sealed reveal rather than a hole to the outside, and the cap
            // keeps its collider so the player cannot walk out of the world. Adding Room4 - or the
            // ending, which is what should really go here - is this flag going false plus another
            // BuildRoomShell at 3 * RoomPitch.
            BuildDoorPocketFill(parent, "DoorPocketFill_3", 2f * RoomPitch, grooveMat, capFarSide: true);

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
            BuildCeilingLights(parent, "Room3", 2f * RoomPitch, fixtureMat, castShadows: false);
            BuildCeilingLights(parent, CalibrationRoomName, CalibrationRoomZ, fixtureMat, castShadows: false);

            // Built after the lights, so the probes capture the rooms already lit. The calibration
            // room needs its own: the walls are at 0.85 smoothness, and without a probe to reflect
            // they mirror the procedural sky and come out tinted blue.
            BuildReflectionProbe(parent, "Room1", 0f);
            BuildReflectionProbe(parent, "Room2", RoomPitch);
            BuildReflectionProbe(parent, "Room3", 2f * RoomPitch);
            BuildReflectionProbe(parent, CalibrationRoomName, CalibrationRoomZ);
        }

        // Fills the cavity between the two rooms' walls, everywhere except the volume the door slab
        // actually sweeps through.
        //
        // Left as one open pocket, that cavity ran the full width of the building and exited to the
        // sky at both ends - standing in the doorway and looking sideways showed a 0.1m x 5m slot
        // straight to the outside, which is the "you can see through between the walls" report.
        // Capping it also gives the opening a proper reveal instead of a hollow slot at the jamb.
        private static void BuildDoorPocketFill(Transform parent, string name, float roomCenterZ, Material mat, bool capFarSide)
        {
            GameObject fill = new GameObject(name);
            fill.transform.SetParent(parent, false);

            float pocketCenterZ = roomCenterZ + RoomDepth / 2f + WallDepth + DoorPocketDepth / 2f;

            // Matches the floor and ceiling slabs, so the fill reaches the outer face of the side
            // walls and the divider is closed off at the same plane they are.
            float halfWidth = RoomWidth / 2f + WallDepth;
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

        private static void BuildRoomShell(Transform parent, string roomName, float zCenter, Material floorMat, Material grooveMat, Material panelMat, Rect southCutout, Rect northCutout)
        {
            GameObject room = new GameObject(roomName);
            room.transform.SetParent(parent, false);
            Transform t = room.transform;

            float minX = -RoomWidth / 2f, maxX = RoomWidth / 2f;
            float minZ = zCenter - RoomDepth / 2f, maxZ = zCenter + RoomDepth / 2f;

            // Slabs run the full room pitch, not just the interior, so neighbouring rooms' floors
            // meet exactly under the divider. Sized to the interior they'd leave an open gap in the
            // doorway threshold and the player would drop through it.
            Vector3 slabSize = new Vector3(RoomWidth + 2f * WallDepth, WallThickness, RoomPitch);
            Prim(PrimitiveType.Cube, "Floor", t, new Vector3(0f, -WallThickness / 2f, zCenter), slabSize, floorMat);
            Prim(PrimitiveType.Cube, "Ceiling", t, new Vector3(0f, RoomHeight + WallThickness / 2f, zCenter), slabSize, floorMat);

            BuildPanelWall(t, "Wall_South", new Vector3(0f, 0f, minZ), Vector3.right, Vector3.forward, RoomWidth, grooveMat, panelMat, southCutout);
            BuildPanelWall(t, "Wall_North", new Vector3(0f, 0f, maxZ), Vector3.right, Vector3.back, RoomWidth, grooveMat, panelMat, northCutout);
            BuildPanelWall(t, "Wall_West", new Vector3(minX, 0f, zCenter), Vector3.forward, Vector3.right, RoomDepth, grooveMat, panelMat, Rect.zero);
            BuildPanelWall(t, "Wall_East", new Vector3(maxX, 0f, zCenter), Vector3.forward, Vector3.left, RoomDepth, grooveMat, panelMat, Rect.zero);
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

        private static (Transform bed, Transform spawn) BuildBed(Transform parent, Material mat)
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
            (GameObject bed, _) = PlaceModel($"{FurnitureDir}/messy_bed.glb", parent, "Bed",
                new Vector3(0f, 0f, 0.7f), 0f, 0.00941f, addBoxCollider: true,
                rotation: Quaternion.Euler(-90f, 180f, 0f));

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
            spawn.transform.localPosition = new Vector3(0f, 0.05f, -0.7f);
            spawn.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

            return (bed.transform, spawn.transform);
        }

        // Ships with a lamp and a vase on it, so it restores what was lost when the bed model with
        // the built-in bedside table was swapped out. Z-up in centimetres, like the bed models.
        private static GameObject BuildNightstand(Transform parent)
        {
            (GameObject nightstand, _) = PlaceModel($"{FurnitureDir}/nightstand.glb", parent, "Nightstand",
                new Vector3(-0.95f, 0f, 1.35f), 0f, 0.01f, addBoxCollider: true,
                rotation: Quaternion.Euler(-90f, 180f, 0f));
            return nightstand;
        }

        // The drawer the balloon tool lives in.
        //
        // nightstand.glb bakes its whole body into one mesh (Nightstand_Nightstand_0), so there is
        // no drawer node in the model to pull out - this is generated geometry sized off the
        // model's own measured front face so it sits flush with it.
        private static (Drawer, CarryableItem) BuildNightstandDrawer(Transform parent, GameObject nightstand, Material mat)
        {
            Renderer body = null;
            Transform bodyNode = FindDescendant(nightstand.transform, "Nightstand_Nightstand_0");
            if (bodyNode != null) body = bodyNode.GetComponent<Renderer>();

            // Measured rather than hardcoded, and logged: the model's units and pivot are both odd
            // (see PlaceModel), so these numbers are worth being able to read back off a build.
            Bounds b = body != null ? body.bounds : new Bounds(new Vector3(-0.95f, 0.25f, 1.35f), new Vector3(0.56f, 0.5f, 0.38f));
            Debug.Log($"[SceneBuilder] Nightstand body bounds min={b.min} max={b.max}");

            // The front face is the one looking down the room, away from the pillow - the model is
            // placed rotated 180 so its drawers face the foot of the bed, which is where the player
            // wakes up looking.
            float frontZ = b.min.z;
            float width = Mathf.Min(b.size.x * 0.78f, 0.5f);
            float height = Mathf.Min(b.size.y * 0.26f, 0.14f);
            float depth = Mathf.Min(b.size.z * 0.8f, 0.34f);
            float centreY = b.min.y + b.size.y * 0.62f;

            GameObject root = new GameObject("NightstandDrawer");
            root.transform.SetParent(parent, false);
            root.transform.position = new Vector3(b.center.x, centreY, frontZ);

            GameObject bodyGO = new GameObject("DrawerBody");
            bodyGO.transform.SetParent(root.transform, false);

            // A front panel plus a shallow tray behind it. The tray is what the tool sits in, and
            // it is what makes an open drawer read as open from across the room.
            Prim(PrimitiveType.Cube, "Front", bodyGO.transform, Vector3.zero,
                new Vector3(width, height, 0.02f), mat, removeCollider: true);
            Prim(PrimitiveType.Cube, "TrayBase", bodyGO.transform, new Vector3(0f, -height / 2f + 0.01f, depth / 2f),
                new Vector3(width * 0.94f, 0.02f, depth), mat, removeCollider: true);
            Prim(PrimitiveType.Cube, "TrayLeft", bodyGO.transform, new Vector3(-width * 0.47f, 0f, depth / 2f),
                new Vector3(0.02f, height, depth), mat, removeCollider: true);
            Prim(PrimitiveType.Cube, "TrayRight", bodyGO.transform, new Vector3(width * 0.47f, 0f, depth / 2f),
                new Vector3(0.02f, height, depth), mat, removeCollider: true);
            Prim(PrimitiveType.Cube, "Handle", bodyGO.transform, new Vector3(0f, 0f, -0.03f),
                new Vector3(width * 0.34f, 0.022f, 0.04f),
                MakeColorMaterial("DrawerHandle", new Color(0.22f, 0.22f, 0.24f)), removeCollider: true);

            BoxCollider trigger = root.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = new Vector3(width + 0.9f, 1.4f, 1.4f);

            Drawer drawerComp = root.AddComponent<Drawer>();
            drawerComp.drawerBody = bodyGO.transform;
            drawerComp.audioSource = MakeSource(root.transform, "DrawerAudio", 1f, 0.8f);
            drawerComp.openClip = LoadClip(SfxDir, "sfx_drawer_open");
            // Straight out of the front face, far enough that the tray clears the carcass.
            drawerComp.openLocalOffset = new Vector3(0f, 0f, -(depth + 0.04f));

            // The tool: a slim pin on a dark handle. Parented to the drawer body, so it rides out
            // with the drawer instead of hanging in the air in front of a shut one.
            GameObject toolRoot = new GameObject("BalloonTool");
            toolRoot.transform.SetParent(bodyGO.transform, false);
            toolRoot.transform.localPosition = new Vector3(0f, 0.02f, depth * 0.55f);

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
            toolItem.itemId = "Tool";
            toolItem.displayName = "PIN";
            toolItem.icon = PinIcon();
            toolItem.showInHand = true;
            toolItem.requiresOpenDrawer = drawerComp;
            toolItem.audioSource = MakeSource(toolRoot.transform, "PickupAudio", 1f, 0.8f);
            toolItem.pickupClip = LoadClip(SfxDir, "sfx_item_pickup");

            return (drawerComp, toolItem);
        }

        // One key shape, used three times over: lying on the floor once its balloon bursts, seen
        // through the skin of the balloon that holds it, and mounted on Room2's lock so the thing
        // on the wall says what it wants. Laid out in the XY plane facing -Z.
        private static void BuildKeyShape(Transform parent, Vector3 centre, float scale, Material mat, Material holeMat)
        {
            float z = centre.z;

            // A ring, faked as a disc with a smaller disc of the backing colour punched into it -
            // without the hole it reads as a lollipop rather than a key.
            GameObject bow = Prim(PrimitiveType.Cylinder, "Bow", parent,
                new Vector3(centre.x, centre.y + 0.062f * scale, z),
                new Vector3(0.078f * scale, 0.005f * scale, 0.078f * scale), mat, removeCollider: true);
            bow.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            if (holeMat != null)
            {
                GameObject hole = Prim(PrimitiveType.Cylinder, "BowHole", parent,
                    new Vector3(centre.x, centre.y + 0.062f * scale, z - 0.004f * scale),
                    new Vector3(0.036f * scale, 0.006f * scale, 0.036f * scale), holeMat, removeCollider: true);
                hole.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            }

            Prim(PrimitiveType.Cube, "Shaft", parent,
                new Vector3(centre.x, centre.y - 0.022f * scale, z),
                new Vector3(0.019f * scale, 0.13f * scale, 0.011f * scale), mat, removeCollider: true);

            // Two teeth, off one side only - a symmetrical bit reads as a cross.
            Prim(PrimitiveType.Cube, "Tooth1", parent,
                new Vector3(centre.x + 0.024f * scale, centre.y - 0.048f * scale, z),
                new Vector3(0.03f * scale, 0.016f * scale, 0.011f * scale), mat, removeCollider: true);
            Prim(PrimitiveType.Cube, "Tooth2", parent,
                new Vector3(centre.x + 0.024f * scale, centre.y - 0.081f * scale, z),
                new Vector3(0.03f * scale, 0.016f * scale, 0.011f * scale), mat, removeCollider: true);
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
        private static (BalloonField, CarryableItem) BuildBalloons(Transform parent)
        {
            const int balloonCount = 70;
            const int fieldSeed = 20260810;

            Material keyMat = MakeColorMaterial("KeyBrass", new Color(0.85f, 0.68f, 0.24f));
            SetSmoothness(keyMat, 0.75f);
            // Darker than the loose key, because it is being read through a pink skin: at the
            // brass colour it washes out into the balloon and stops being a shape.
            Material keyVisualMat = MakeColorMaterial("KeyInBalloon", new Color(0.3f, 0.24f, 0.1f));

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

            // Picked from the same fixed seed as the spawn points, so the key is in the same
            // balloon in every run - which is what makes "I know which one it is" worth having.
            int keyIndex = new System.Random(fieldSeed).Next(balloonCount);
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
                balloon.holdsKey = i == keyIndex;

                // The key, visible through the skin of the one balloon that has it. A child of the
                // balloon, so Balloon.SetInPlay hides and shows it along with everything else and
                // it vanishes the moment the balloon bursts.
                if (balloon.holdsKey) BuildKeyShape(go.transform, Vector3.zero, 0.85f, keyVisualMat, null);
                balloon.audioSource = MakeSource(go.transform, "PopAudio", 1f, 0.8f);
                // A fixed detune per balloon, from the field's own seed. One clip across seventy
                // balloons reads as a machine gun; a balloon keeping the same voice every
                // iteration is one more thing about the room that stays put.
                balloon.audioSource.pitch = 0.86f + (float)pitchRng.NextDouble() * 0.3f;
                balloon.popClip = LoadClip(SfxDir, "sfx_balloon_pop");

                balloons[i] = balloon;
            }

            // The key. Pocketed rather than held, so picking it up does not knock the tool out of
            // the hand you needed to get it with.
            GameObject keyRoot = new GameObject("Key");
            keyRoot.transform.SetParent(root.transform, false);
            keyRoot.transform.position = new Vector3(0f, 0.06f, RoomPitch);

            BuildKeyShape(keyRoot.transform, Vector3.zero, 1f, keyMat, null);

            BoxCollider keyTrigger = keyRoot.AddComponent<BoxCollider>();
            keyTrigger.isTrigger = true;
            keyTrigger.size = new Vector3(0.9f, 0.9f, 0.9f);

            CarryableItem keyItem = keyRoot.AddComponent<CarryableItem>();
            keyItem.itemId = "Key";
            keyItem.displayName = "KEY";
            keyItem.icon = KeyIcon();
            keyItem.showInHand = false;
            keyItem.audioSource = MakeSource(keyRoot.transform, "PickupAudio", 1f, 0.9f);
            keyItem.pickupClip = LoadClip(SfxDir, "sfx_item_pickup");

            BalloonField field = root.AddComponent<BalloonField>();
            field.balloons = balloons;
            field.key = keyItem;
            field.seed = fieldSeed;
            field.roomCenterZ = RoomPitch;

            // Park the pool now rather than waiting for the first iteration to do it. Built objects
            // sit at their parent's origin, which for these is the middle of Room1.
            field.ComputeSpawnPoints();
            field.ResetField();

            return (field, keyItem);
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
        private static (Door door, DoorIndicator indicator, float wallInnerZ) BuildDoorShell(
            Transform parent, string name, float roomCenterZ, Material mat)
        {
            GameObject doorRoot = new GameObject(name);
            doorRoot.transform.SetParent(parent, false);
            // The root carries the room offset, so every measurement below stays in the same local
            // frame it was tuned in back when there was only one door.
            doorRoot.transform.localPosition = new Vector3(0f, 0f, roomCenterZ);

            // The slab lives in the pocket between this room's wall and the next room's, so sliding
            // it sideways tucks it inside the wall build-up rather than dragging it across the
            // panelling. wallInnerZ is the room surface; the pocket starts WallDepth behind that.
            float wallInnerZ = RoomDepth / 2f;
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
        private static Door BuildPadDoor(Transform parent, string name, float roomCenterZ,
                                         FloorButton[] pads, Material mat)
        {
            (Door door, DoorIndicator indicator, float _) = BuildDoorShell(parent, name, roomCenterZ, mat);

            // The lamp tracks the condition, not the door, so it goes green the moment the pads are
            // all held. With no button anywhere it is the room's only readout for that, which makes
            // it load-bearing rather than decorative - and in Room3 it is the only way to tell "one
            // of my past selves has arrived" from "both have".
            indicator.requiredFloorButtons = pads;
            door.requiredFloorButtons = pads;

            return door;
        }

        // Room2's way out. No pad and no condition to hold open, and deliberately not a button:
        // the shape of the thing on the wall is the puzzle telling you what it wants.
        private static (Door, KeyLock) BuildKeyDoor(Transform parent, float roomCenterZ, Material mat)
        {
            (Door door, DoorIndicator indicator, float wallInnerZ) = BuildDoorShell(parent, "Door2", roomCenterZ, mat);

            GameObject lockRoot = new GameObject("KeyLock");
            lockRoot.transform.SetParent(door.transform, false);
            lockRoot.transform.localPosition = new Vector3(-GridCellWidth, GridCellHeight * 1.5f, wallInnerZ - 0.06f);

            // Taller and narrower than the door button next door: across a room the two have to
            // read as different kinds of thing rather than as the same switch twice.
            GameObject plate = Prim(PrimitiveType.Cube, "Visual", lockRoot.transform, Vector3.zero,
                new Vector3(0.2f, 0.3f, 0.09f), mat, removeCollider: true);

            // A plain keyhole, NOT the key silhouette this used to carry. The etching said what the
            // lock wanted, which was the right idea while nothing could ever be put in it - but the
            // key is now literally inserted here, and an inserted key sitting on top of an engraved
            // one reads as two keys. A keyhole says the same thing and leaves the socket empty.
            Material slotMat = MakeColorMaterial("KeySlot", new Color(0.06f, 0.06f, 0.07f));
            BuildKeyholeSlot(lockRoot.transform, new Vector3(0f, -0.03f, -0.047f), slotMat);

            // Where an accepted key is parked. Slightly proud of the plate and scaled down a
            // touch, so it sits ON the lock rather than inside it, with its shaft over the hole.
            GameObject socket = new GameObject("KeySocket");
            socket.transform.SetParent(lockRoot.transform, false);
            socket.transform.localPosition = new Vector3(0f, 0.044f, -0.062f);
            socket.transform.localScale = Vector3.one * 0.85f;

            BoxCollider trigger = lockRoot.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = new Vector3(0.8f, 0.8f, 1.2f);

            KeyLock keyLock = lockRoot.AddComponent<KeyLock>();
            keyLock.door = door;
            keyLock.keySocket = socket.transform;
            keyLock.lockRenderer = plate.GetComponent<Renderer>();
            // The lamp over this door reports "you are carrying the key" the way the other one
            // reports "the pad is held".
            indicator.keyLock = keyLock;

            return (door, keyLock);
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

            PlayerRecorder recorder = player.AddComponent<PlayerRecorder>();
            recorder.interactables = ghostInteractables;

            // Parented under the camera rather than the player, so a carried item rides the view -
            // including through the wake-up, where WakeUpSequence poses the camera directly and
            // anything hung off the body would swing independently of where you are looking.
            GameObject handAnchor = new GameObject("HandAnchor");
            handAnchor.transform.SetParent(camGO.transform, false);

            PlayerHand hand = player.AddComponent<PlayerHand>();
            hand.holdAnchor = handAnchor.transform;

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

        private static GhostReplayer BuildGhostPrefab()
        {
            string prefabPath = $"{PrefabsDir}/Ghost.prefab";

            Material ghostMat = MakeGhostMaterial();

            // A rough human silhouette rather than a capsule. It's held at such a low alpha that
            // the crudeness of the primitives never reads - only the outline does. Laid out from
            // the feet up, because the recorded position is the player's transform, which sits at
            // floor level (the CharacterController is what's centred at mid-body).
            GameObject ghost = new GameObject("Ghost");
            Prim(PrimitiveType.Sphere, "Head", ghost.transform, new Vector3(0f, 1.6f, 0f), new Vector3(0.23f, 0.25f, 0.23f), ghostMat, removeCollider: true);
            Prim(PrimitiveType.Capsule, "Torso", ghost.transform, new Vector3(0f, 1.17f, 0f), new Vector3(0.34f, 0.32f, 0.23f), ghostMat, removeCollider: true);

            Transform leftArm = BuildGhostLimb(ghost.transform, "LeftArm", new Vector3(-0.235f, 1.45f, 0f), 0.6f, 0.1f, ghostMat);
            Transform rightArm = BuildGhostLimb(ghost.transform, "RightArm", new Vector3(0.235f, 1.45f, 0f), 0.6f, 0.1f, ghostMat);
            Transform leftLeg = BuildGhostLimb(ghost.transform, "LeftLeg", new Vector3(-0.105f, 0.88f, 0f), 0.86f, 0.14f, ghostMat);
            Transform rightLeg = BuildGhostLimb(ghost.transform, "RightLeg", new Vector3(0.105f, 0.88f, 0f), 0.86f, 0.14f, ghostMat);

            GhostReplayer replayer = ghost.AddComponent<GhostReplayer>();
            replayer.leftArm = leftArm;
            replayer.rightArm = rightArm;
            replayer.leftLeg = leftLeg;
            replayer.rightLeg = rightLeg;

            GameObject prefabAsset = PrefabUtility.SaveAsPrefabAsset(ghost, prefabPath);
            Object.DestroyImmediate(ghost);

            return prefabAsset.GetComponent<GhostReplayer>();
        }

        // The bed model ships with two pillows, both sitting off the centre line. Hide one and slide
        // the survivor onto the bed's axis. They're separate nodes in the glb, so this is a transform
        // tweak, not a mesh edit - if a future bed model bakes its pillows into the bedding mesh,
        // this can't work and the model has to change instead.
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

        // The limb mesh hangs off an empty pivot placed at the shoulder or hip, so rotating it
        // swings the limb from that joint. Rotating the capsule directly would spin it about its
        // own centre, which is where a primitive's pivot sits.
        private static Transform BuildGhostLimb(Transform parent, string name, Vector3 jointLocalPos, float length, float thickness, Material mat)
        {
            GameObject pivot = new GameObject(name);
            pivot.transform.SetParent(parent, false);
            pivot.transform.localPosition = jointLocalPos;

            // Unity's capsule is 2 units tall and 1 wide, so halve the length and pass thickness
            // straight through as the diameter.
            Prim(PrimitiveType.Capsule, name + "Mesh", pivot.transform,
                new Vector3(0f, -length / 2f, 0f), new Vector3(thickness, length / 2f, thickness),
                mat, removeCollider: true);

            return pivot.transform;
        }

        private static Material MakeGhostMaterial()
        {
            string path = $"{MaterialsDir}/GhostFaint.mat";
            Shader unlit = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");

            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(unlit);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = unlit;

            // Unlit on purpose: a lit ghost picks up shading and specular that would give the
            // primitives away as primitives. Alpha this low leaves only a suggestion of a figure.
            Color faint = new Color(0.02f, 0.02f, 0.03f, 0.16f);
            mat.color = faint;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", faint);

            // Standard URP transparent set-up. Without the keyword and blend modes the shader
            // stays opaque and the ghost renders as a solid black mannequin.
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

            EditorUtility.SetDirty(mat);
            return mat;
        }

        // Assembles every audio source and resolves its clips by filename convention. Clips that
        // aren't there yet resolve to null and every player guards on that, so the scene is fully
        // playable with an empty SFX folder - dropping a correctly-named file in and rebuilding is
        // all it takes to make that cue audible. See Assets/Audio/SFX/README.md for the names.
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
            const int slotCount = 4;
            const float slotSize = 58f, gap = 14f;

            GameObject go = new GameObject("CarriedItems");
            go.transform.SetParent(canvas, false);

            RectTransform rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(slotCount * (slotSize + gap), slotSize);
            rect.anchoredPosition = new Vector2(36f, -30f);

            // Four is two more than the puzzle currently has, which is the point: a slot is a few
            // bytes and growing the pool later means rebuilding the scene.
            Image[] slots = new Image[slotCount];
            for (int i = 0; i < slotCount; i++)
            {
                GameObject slotGO = new GameObject($"Slot{i}");
                slotGO.transform.SetParent(go.transform, false);

                Image image = slotGO.AddComponent<Image>();
                // Red like the rest of the HUD: the walls are near-white, so a white glyph
                // disappears into them.
                image.color = Color.red;
                image.raycastTarget = false;
                image.preserveAspect = true;
                image.enabled = false;

                RectTransform slotRect = image.GetComponent<RectTransform>();
                slotRect.anchorMin = new Vector2(0f, 1f);
                slotRect.anchorMax = new Vector2(0f, 1f);
                slotRect.pivot = new Vector2(0f, 1f);
                slotRect.sizeDelta = new Vector2(slotSize, slotSize);
                slotRect.anchoredPosition = new Vector2(i * (slotSize + gap), 0f);

                slots[i] = image;
            }

            CarriedItemsDisplay display = go.AddComponent<CarriedItemsDisplay>();
            display.hand = hand;
            display.slots = slots;
        }

        // The two control prompts. A grey disc over whatever the player has walked up to, with an
        // E on it, and a second disc carrying a mouse glyph that appears on the pin the moment it
        // is in hand. Each is shown once and then retired for good - see ControlHintDisplay.
        //
        // Built last of everything on the canvas so it draws over the eyelids and the HUD, and
        // parented to a full-screen rect so a screen point converts straight to an anchoredPosition.
        private static void BuildControlHints(Transform canvas, Camera playerCamera, PlayerHand hand,
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
            display.hand = hand;
            display.interactTargets = interactTargets;
            display.area = area;
            display.interactGroup = interactGroup;
            display.interactRect = interactRect;
            display.swingGroup = swingGroup;
            display.swingRect = swingRect;
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

            IterationLabel label = groupGO.AddComponent<IterationLabel>();
            label.canvasGroup = group;
            label.label = text;

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

            EndingSequence ending = root.AddComponent<EndingSequence>();
            ending.scrimGroup = scrimGroup;
            ending.cardGroup = cardGroup;
            ending.headline = headline;
            ending.detail = detail;
            ending.menuScene = "MainMenu";

            return ending;
        }

        // Room3 telling the player about the end-cycle control, on all four walls at once.
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
            float halfWidth = RoomWidth / 2f;
            float halfDepth = RoomDepth / 2f;
            // Above both doorways (DoorHeight 2.5) so the message never straddles an opening, and
            // high enough to read as signage rather than as something at eye level.
            const float y = 3.4f;

            // Each canvas's forward (+Z) points INTO its wall, i.e. away from the room. That reads
            // backwards and it is the opposite of what was built first, which came out mirrored on
            // all four walls.
            //
            // The rule: a world-space canvas is legible when its forward matches the direction the
            // viewer is LOOKING, not when it points at the viewer. Unity's own default scene is the
            // proof - camera at z = -10 looking toward +Z, canvas at the origin unrotated, text the
            // right way round. So a wall message must face the same way as the eyes reading it, and
            // a player at the middle of the room looks outwards at every one of these.
            var faces = new CanvasGroup[4];
            faces[0] = MakeWallFace(root.transform, "South", new Vector3(0f, y, -halfDepth + standoff), Quaternion.Euler(0f, 180f, 0f));
            faces[1] = MakeWallFace(root.transform, "North", new Vector3(0f, y, halfDepth - standoff), Quaternion.identity);
            faces[2] = MakeWallFace(root.transform, "West", new Vector3(-halfWidth + standoff, y, 0f), Quaternion.Euler(0f, -90f, 0f));
            faces[3] = MakeWallFace(root.transform, "East", new Vector3(halfWidth - standoff, y, 0f), Quaternion.Euler(0f, 90f, 0f));

            PanelMessage message = root.AddComponent<PanelMessage>();
            message.faces = faces;
            message.roomCenterZ = roomCenterZ;
            message.halfDepth = halfDepth;
            return message;
        }

        private static CanvasGroup MakeWallFace(Transform parent, string name, Vector3 localPosition, Quaternion localRotation)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);

            Canvas canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;

            // Authored large and scaled down, which is the standard way to keep world-space UI text
            // from rendering as a handful of blocky pixels: the font rasterises at the RectTransform
            // size, not the final world size.
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(1600f, 440f);
            rect.localScale = Vector3.one * 0.0045f;   // -> 7.2m x 1.98m, inside an 8.75m wall

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

            // The plate. This is what makes it a display rather than paint - GrooveDark is the same
            // near-black that sits at the bottom of every groove in the room.
            GameObject plateGO = new GameObject("Plate");
            plateGO.transform.SetParent(go.transform, false);
            Image plate = plateGO.AddComponent<Image>();
            plate.color = new Color(0.04f, 0.04f, 0.045f, 0.94f);
            plate.raycastTarget = false;
            Stretch(plate.GetComponent<RectTransform>());

            MakeWallLine(go.transform, "Headline", "H O L D   [ N ]", 132, Color.red,
                new Vector2(0f, 78f), new Vector2(1600f, 190f));
            // Not spaced out, unlike the headline: this line is 29 characters and spacing it would
            // put it past the wall. The headline carries the treatment for both.
            MakeWallLine(go.transform, "Detail", "TO SKIP TO THE NEXT ITERATION", 74,
                new Color(1f, 0.35f, 0.35f, 0.9f), new Vector2(0f, -90f), new Vector2(1600f, 130f));

            return group;
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
            Button quitButton = MakeMenuButton(menuGO.transform, "QuitButton", "QUIT", new Vector2(0f, -118f));

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

            // Sized to the block of text, and low enough in the frame that the middle of the view -
            // where a player naturally looks while turning - stays clear.
            GameObject plateGO = new GameObject("Plate");
            plateGO.transform.SetParent(root.transform, false);
            Image plate = plateGO.AddComponent<Image>();
            plate.color = new Color(0.04f, 0.04f, 0.045f, 0.82f);
            plate.raycastTarget = false;
            RectTransform plateRect = plate.GetComponent<RectTransform>();
            plateRect.anchorMin = new Vector2(0.5f, 0.5f);
            plateRect.anchorMax = new Vector2(0.5f, 0.5f);
            plateRect.sizeDelta = new Vector2(760f, 330f);
            plateRect.anchoredPosition = new Vector2(0f, -190f);

            MakeMenuLine(root.transform, "Headline", "M O U S E   S E N S I T I V I T Y", 34,
                Color.red, new Vector2(0f, -70f), new Vector2(1400f, 60f));
            MakeMenuLine(root.transform, "Instruction", "LOOK AROUND THE ROOM", 22,
                new Color(1f, 0.35f, 0.35f, 0.85f), new Vector2(0f, -118f), new Vector2(1200f, 36f));

            Text value = MakeMenuLine(root.transform, "Value", "1.10", 30, Color.red,
                new Vector2(0f, -172f), new Vector2(400f, 44f));

            // A readout rather than a control: with the pointer captured there is no cursor to drag
            // a slider with, which is the price of measuring the same deltas the game does. The
            // draggable one lives in the pause menu.
            GameObject barGO = new GameObject("Gauge");
            barGO.transform.SetParent(root.transform, false);
            Image bar = barGO.AddComponent<Image>();
            bar.color = new Color(0f, 0f, 0f, 0.6f);
            bar.raycastTarget = false;
            RectTransform barRect = bar.GetComponent<RectTransform>();
            barRect.anchorMin = new Vector2(0.5f, 0.5f);
            barRect.anchorMax = new Vector2(0.5f, 0.5f);
            barRect.sizeDelta = new Vector2(620f, 12f);
            barRect.anchoredPosition = new Vector2(0f, -214f);

            GameObject fillGO = new GameObject("Fill");
            fillGO.transform.SetParent(barGO.transform, false);
            Image fill = fillGO.AddComponent<Image>();
            fill.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillAmount = 0.5f;
            fill.color = Color.red;
            fill.raycastTarget = false;
            Stretch(fill.GetComponent<RectTransform>());

            // Wheel only. Keys are not offered because the player is walking around in here and
            // both the arrows and A/D are bound to the Horizontal axis - every press that nudged
            // the number would also strafe them.
            MakeMenuLine(root.transform, "AdjustHint", "SCROLL TO ADJUST", 20,
                new Color(1f, 0.35f, 0.35f, 0.7f), new Vector2(0f, -252f), new Vector2(1200f, 36f));
            // Doubles as the pointer-lock state: a browser can refuse the capture, and this is
            // where the page says so and asks for the click that fixes it.
            Text lockHint = MakeMenuLine(root.transform, "LockHint", "[ENTER]  TO BEGIN", 24,
                Color.red, new Vector2(0f, -300f), new Vector2(1200f, 40f));

            SensitivityCalibration calibration = root.AddComponent<SensitivityCalibration>();
            calibration.group = group;
            calibration.fill = fill;
            calibration.valueLabel = value;
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
                if (child == root.transform || child.name == "PauseMenu") continue;
                hidden.Add(child.gameObject);
            }
            calibration.hideWhileActive = hidden.ToArray();

            return calibration;
        }

        private static Text MakeMenuLine(Transform parent, string name, string content, int fontSize,
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
