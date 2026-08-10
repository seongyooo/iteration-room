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
        private const string FontsDir = "Assets/Fonts";
        private const string AudioDir = "Assets/Audio";
        private const string VoiceDir = AudioDir + "/Voice";
        private const string SfxDir = AudioDir + "/SFX";

        // Must match the range Tools/generate_narration.ps1 writes out. Past this the announcer
        // falls back to a generic line rather than going silent.
        private const int NarrationIterationLines = 30;

        // Room shell dimensions. The wall-grid tiling is derived from these (see MakeGridMaterial),
        // so changing a dimension here keeps the grid cells the right physical size automatically -
        // don't hardcode tiling numbers anywhere else.
        private const float RoomWidth = 8.75f;   // X span, i.e. the door wall
        private const float RoomDepth = 10.5f;   // Z span, i.e. the side walls
        // A whole number of grid cells (5 x 1m) on purpose: at 4.5m the top row was a half cell,
        // so the panelling ran off cut in half at the ceiling.
        private const float RoomHeight = 5f;
        private const float WallThickness = 0.1f;

        // Wall grid cells are deliberately landscape. Because a cell is wider than it is tall, a
        // square texture cell would stretch the vertical lines relative to the horizontal ones, so
        // the texture is baked with anisotropic line widths that cancel that out exactly.
        private const float GridCellWidth = 1.75f;
        private const float GridCellHeight = 1f;
        private const float GridLineThickness = 0.082f;

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
                if (r.transform.parent != null && r.transform.parent.name.EndsWith("_Panels"))
                    wallPanelRenderers.Add(r);

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
            BuildNightstand(room.transform);
            FloorButton floorButton = BuildFloorButton(room.transform, propMat);
            (Door door, DoorButton doorButton) = BuildDoor(room.transform, floorButton, propMat);

            // The wire format for ghost playback: an entry's index is its bit in
            // RecordedFrame.signals. The recorder and the loop are handed the same array so the
            // two can never drift out of order.
            GhostInteractable[] ghostInteractables = { floorButton, doorButton };

            (GameObject player, FirstPersonController fpc, PlayerRecorder recorder, CameraShaker shaker) = BuildPlayer(bedSpawn, ghostInteractables);

            GhostReplayer ghostPrefab = BuildGhostPrefab();
            GameObject ghostParent = new GameObject("Ghosts");

            (IterationLabel label, WakeUpSequence wakeUp) = BuildUI();
            wakeUp.wallPanels = wallDisplay;

            (NarrationDirector narration, RoomAmbience ambience) =
                BuildAudio(player, door, floorButton, wakeUp);

            // TEMPORARY. An in-play panel for finding the room's brightness by eye instead of
            // rebuilding between guesses - press F1 in play mode. Delete this line and the script
            // once the values are settled and pasted back into SetupLighting/BuildCeilingLights.
            new GameObject("LightingTuner").AddComponent<LightingTuner>();

            GameObject loopGO = new GameObject("LoopManager");
            LoopManager loop = loopGO.AddComponent<LoopManager>();
            loop.loopDuration = 60f;
            loop.bedSpawnPoint = bedSpawn;
            loop.playerRecorder = recorder;
            loop.playerController = fpc;
            loop.ghostInteractables = ghostInteractables;
            loop.door = door;
            loop.ghostPrefab = ghostPrefab;
            loop.ghostParent = ghostParent.transform;
            loop.iterationLabel = label;
            loop.wakeUpSequence = wakeUp;
            loop.narration = narration;
            loop.ambience = ambience;
            loop.wallPanels = wallDisplay;
            loop.cameraShaker = shaker;

            Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);

            // The build settings scene list was empty, so a standalone player would have shipped
            // with no scenes at all. Reasserted on every build rather than set once, because the
            // list lives in ProjectSettings and nothing else here maintains it.
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[SceneBuilder] IterationRoom scene built at " + ScenePath);
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
            // Tuned in play mode with Dev/LightingTuner, 2026-08-10, over two passes.
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
                    // Found by eye against Neutral tonemapping, in play mode (Dev/LightingTuner).
                    // A 130-degree cone from 5m up spreads its energy over most of the room, so
                    // this reads lower than it is: at 4 the room came out a dim grey.
                    //
                    // History, because it has moved twice for different reasons: 11 with six
                    // fixtures, then 15 when the count dropped to four, then down to 9 once the
                    // ambient fill was cut back. The fill was doing more of the lighting than it
                    // looked, so trimming it left the spots over-driven.
                    light.intensity = 9f;
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

            // Two identical rooms sharing a divider: the loop room's north wall and the next room's
            // south wall face each other across the door pocket, each with the same doorway cut out
            // of its panelling, its backing and its collision, so the opening is a real hole.
            BuildRoomShell(parent, "Room1", 0f, floorMat, grooveMat, panelMat, Rect.zero, doorway);
            BuildRoomShell(parent, "Room2", RoomPitch, floorMat, grooveMat, panelMat, doorway, Rect.zero);

            BuildDoorPocketFill(parent, grooveMat);

            Material fixtureMat = MakeEmissiveMaterial("CeilingFixture", Color.white, 3.5f);
            BuildCeilingLights(parent, "Room1", 0f, fixtureMat, castShadows: true);
            BuildCeilingLights(parent, "Room2", RoomPitch, fixtureMat, castShadows: false);

            // Built after the lights, so the probe captures the room already lit.
            BuildReflectionProbe(parent, "Room1", 0f);
            BuildReflectionProbe(parent, "Room2", RoomPitch);
        }

        // Fills the cavity between the two rooms' walls, everywhere except the volume the door slab
        // actually sweeps through.
        //
        // Left as one open pocket, that cavity ran the full width of the building and exited to the
        // sky at both ends - standing in the doorway and looking sideways showed a 0.1m x 5m slot
        // straight to the outside, which is the "you can see through between the walls" report.
        // Capping it also gives the opening a proper reveal instead of a hollow slot at the jamb.
        private static void BuildDoorPocketFill(Transform parent, Material mat)
        {
            GameObject fill = new GameObject("DoorPocketFill");
            fill.transform.SetParent(parent, false);

            float pocketCenterZ = RoomDepth / 2f + WallDepth + DoorPocketDepth / 2f;

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

        private static FloorButton BuildFloorButton(Transform parent, Material mat)
        {
            // Root at unit scale so the trigger collider is defined in clean world units;
            // the flattened cylinder mesh lives on an unscaled-collider visual child instead.
            GameObject root = new GameObject("FloorButton");
            root.transform.SetParent(parent, false);
            // Out in the open floor area past the foot of the bed, matching room_layout_sample.png reference.
            root.transform.localPosition = new Vector3(2.8f, 0.03f, -1.75f);

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

        private static (Door, DoorButton) BuildDoor(Transform parent, FloorButton floorButton, Material mat)
        {
            GameObject doorRoot = new GameObject("Door");
            doorRoot.transform.SetParent(parent, false);

            // The slab lives in the pocket between this room's wall and the next room's, so sliding
            // it sideways tucks it inside the wall build-up rather than dragging it across the
            // panelling. wallInnerZ is the room surface; the pocket starts WallDepth behind that.
            float wallInnerZ = RoomDepth / 2f;
            float slabZ = wallInnerZ + WallDepth + DoorPocketDepth / 2f;

            // The slab KEEPS its collider. The doorway is cut out of both walls' collision, so the
            // closed door is the only thing standing between the two rooms - built without one, the
            // player just walks through it and the whole two-iteration puzzle is bypassable. Once
            // open the slab sits at x 0.65..1.95, entirely behind the wall's own collision, so it
            // never blocks the opening it just cleared.
            GameObject panel = Prim(PrimitiveType.Cube, "DoorPanel", doorRoot.transform, new Vector3(0f, DoorHeight / 2f, slabZ), new Vector3(DoorWidth, DoorHeight, DoorThickness), mat);

            // A single lamp block split down the middle: red half on the left, green on the right,
            // only ever one of them lit. Replaces a sphere, which read as a bauble rather than as
            // a status light. Both halves share one emissive material and are driven apart by
            // DoorIndicator through property blocks.
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
            // Slides right (+X as seen from inside the loop room) by its own width, so it clears
            // the opening exactly.
            door.openLocalOffset = new Vector3(DoorWidth, 0f, 0f);
            door.doorPanel = panel.transform;
            DoorIndicator indicator = lampRoot.AddComponent<DoorIndicator>();
            indicator.redHalf = redHalf.GetComponent<Renderer>();
            indicator.greenHalf = greenHalf.GetComponent<Renderer>();
            // Tracks the condition, not the door: green the moment the floor button is held, so
            // the lamp tells the player the door is openable before they walk over to try it.
            indicator.requiredFloorButton = floorButton;
            indicator.door = door;

            GameObject buttonRoot = new GameObject("DoorButton");
            buttonRoot.transform.SetParent(parent, false);
            // On the LEFT of the door: the slab slides right, so a button on that side would end up
            // buried behind it. Centred inside a grid cell rather than straddling a groove, at hand
            // height for a 1.6m eye level.
            buttonRoot.transform.localPosition = new Vector3(-GridCellWidth, GridCellHeight * 1.5f, wallInnerZ - 0.06f);

            GameObject buttonVisual = Prim(PrimitiveType.Cube, "Visual", buttonRoot.transform, Vector3.zero, new Vector3(0.2f, 0.2f, 0.1f), mat, removeCollider: true);

            BoxCollider trigger = buttonRoot.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = new Vector3(0.8f, 0.8f, 1.2f);

            DoorButton doorButton = buttonRoot.AddComponent<DoorButton>();
            doorButton.requiredFloorButton = floorButton;
            doorButton.door = door;
            doorButton.buttonRenderer = buttonVisual.GetComponent<Renderer>();

            return (door, doorButton);
        }

        private static (GameObject, FirstPersonController, PlayerRecorder, CameraShaker) BuildPlayer(Transform spawn, GhostInteractable[] ghostInteractables)
        {
            GameObject player = new GameObject("Player");
            player.tag = "Player";
            player.transform.position = spawn.position;
            player.transform.rotation = spawn.rotation;

            CharacterController cc = player.AddComponent<CharacterController>();
            cc.height = 1.8f;
            cc.radius = 0.3f;
            cc.center = new Vector3(0f, 0.9f, 0f);

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

            PlayerRecorder recorder = player.AddComponent<PlayerRecorder>();
            recorder.interactables = ghostInteractables;

            return (player, fpc, recorder, shaker);
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
        private static (NarrationDirector, RoomAmbience) BuildAudio(GameObject player, Door door, FloorButton floorButton, WakeUpSequence wakeUp)
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

            // --- the floor button ---
            // Positional and parented to the pad, which is the entire point: the door lamp only
            // reports the condition to someone looking at the door, but the clunk reaches you
            // wherever you are. Hearing a ghost step onto the pad behind you is how the puzzle
            // tells you the door is live.
            floorButton.audioSource = MakeSource(floorButton.transform, "FloorButtonAudio", 1f, 0.9f);
            floorButton.pressClip = LoadClip(SfxDir, "sfx_floor_button_press");
            floorButton.releaseClip = LoadClip(SfxDir, "sfx_floor_button_release");

            // --- the door ---
            door.audioSource = MakeSource(door.transform, "DoorAudio", 1f, 1f);
            door.openClip = LoadClip(SfxDir, "sfx_door_open");

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
            Font font = AssetDatabase.LoadAssetAtPath<Font>($"{FontsDir}/Consolas.ttf");
            // Falls back rather than throwing: a missing font should leave the UI ugly and legible,
            // not leave the scene unbuildable.
            return font != null ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        private static (IterationLabel label, WakeUpSequence wakeUp) BuildUI()
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

            // uGUI buttons do nothing without one of these in the scene, and NewScene's default
            // objects are only a camera and a light.
            GameObject eventSystem = new GameObject("EventSystem");
            eventSystem.AddComponent<UnityEngine.EventSystems.EventSystem>();
            eventSystem.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();

            return (label, wakeUp);
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
            rect.sizeDelta = new Vector2(168f, 40f);
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
