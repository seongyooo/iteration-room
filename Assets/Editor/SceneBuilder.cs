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

            Material floorMat = MakeColorMaterial("FloorWhite", Color.white);
            // Sits at the bottom of every groove and inside the door pocket. Near-black so the
            // seams read the way the old painted-on grid lines did.
            Material grooveMat = MakeColorMaterial("GrooveDark", new Color(0.04f, 0.04f, 0.045f));
            Material propMat = MakeColorMaterial("PropLight", new Color(0.85f, 0.85f, 0.85f));
            Material panelMat = MakeColorMaterial("PanelWhite", Color.white);

            GameObject room = new GameObject("Room");
            BuildShell(room.transform, floorMat, grooveMat, panelMat);

            // No dedicated room light - a point light read as too harsh/blown-out. Instead,
            // brighten the scene's default Directional Light and switch to flat ambient so the
            // whole room reads evenly bright instead of having one hot spot.
            SetupLighting();
            BuildPostProcessing();
            ConfigureAmbientOcclusion();

            (Transform bed, Transform bedSpawn) = BuildBed(room.transform, propMat);
            GameObject nightstand = BuildNightstand(room.transform);
            FloorButton floorButton = BuildFloorButton(room.transform, propMat);
            (Door door, DoorButton doorButton) = BuildDoor(room.transform, floorButton, propMat);

            // The wire format for ghost playback: an entry's index is its bit in
            // RecordedFrame.signals. The recorder and the loop are handed the same array so the
            // two can never drift out of order.
            GhostInteractable[] ghostInteractables = { floorButton, doorButton };

            (GameObject player, FirstPersonController fpc, PlayerRecorder recorder) = BuildPlayer(bedSpawn, ghostInteractables);

            GhostReplayer ghostPrefab = BuildGhostPrefab();
            GameObject ghostParent = new GameObject("Ghosts");

            (IterationLabel label, WakeUpSequence wakeUp) = BuildUI();

            (NarrationDirector narration, RoomAmbience ambience) =
                BuildAudio(player, door, wakeUp, nightstand.transform);

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

            Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
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

        private static void SetupLighting()
        {
            // Even, clinical-white lighting: carry almost all of it on flat ambient (which hits every
            // surface equally, including the ceiling, which a directional light never reaches) and
            // leave the directional as a weak, steeply-angled source purely for enough shading to
            // read geometry. A strong low-angle directional blew out one wall while the opposite
            // wall read grey.
            RenderSettings.ambientMode = AmbientMode.Flat;
            // Tuned against URP + Neutral tonemapping. URP responds to ambient differently from
            // Built-in: 1.05 here blows the whole room to flat white, 0.72 leaves the walls grey
            // while the directional still blows out the floor.
            RenderSettings.ambientLight = new Color(0.95f, 0.95f, 0.97f);
            RenderSettings.ambientIntensity = 1f;

            // The blue procedural skybox was tinting every surface through reflections.
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            RenderSettings.reflectionIntensity = 0.1f;

            foreach (Light light in Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude))
            {
                if (light.type != LightType.Directional) continue;

                light.transform.rotation = Quaternion.Euler(72f, 200f, 0f);
                light.color = Color.white;
                // Kept low on purpose: pointing this steeply down, anything higher blows out the
                // floor long before it does anything useful for the walls.
                light.intensity = 0.25f;
                light.shadows = LightShadows.Soft;
                light.shadowStrength = 0.5f;
                break;
            }
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

            // The backing slab is cut by the same doorway rect as the panels. As one solid slab it
            // left the doorway a dead-end recess: you could walk through (collision was already
            // cut) but you were looking at wall.
            GameObject backing = new GameObject(name);
            backing.transform.SetParent(parent, false);
            int slab = 0;
            foreach (Rect part in SubtractRect(wallRect, cutout))
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
            foreach (Rect part in SubtractRect(wallRect, cutout))
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

            GameObject spawn = new GameObject("BedSpawnPoint");
            spawn.transform.SetParent(parent, false);
            spawn.transform.localPosition = new Vector3(0f, 0.05f, -1.75f);
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

            GameObject visual = Prim(PrimitiveType.Cylinder, "Visual", root.transform, Vector3.zero, new Vector3(0.7f, 0.03f, 0.7f), mat, removeCollider: true);

            CapsuleCollider trigger = root.AddComponent<CapsuleCollider>();
            trigger.isTrigger = true;
            trigger.radius = 0.5f;
            trigger.height = 0.5f;

            FloorButton fb = root.AddComponent<FloorButton>();
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

            GameObject indicator = Prim(PrimitiveType.Sphere, "DoorIndicatorLight", doorRoot.transform, new Vector3(0f, DoorHeight + 0.35f, wallInnerZ - 0.075f), new Vector3(0.15f, 0.15f, 0.15f), mat, removeCollider: true);

            Door door = doorRoot.AddComponent<Door>();
            // Slides right (+X as seen from inside the loop room) by its own width, so it clears
            // the opening exactly.
            door.openLocalOffset = new Vector3(DoorWidth, 0f, 0f);
            door.doorPanel = panel.transform;
            door.indicatorRenderer = indicator.GetComponent<Renderer>();

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

        private static (GameObject, FirstPersonController, PlayerRecorder) BuildPlayer(Transform spawn, GhostInteractable[] ghostInteractables)
        {
            GameObject player = new GameObject("Player");
            player.tag = "Player";
            player.transform.position = spawn.position;
            player.transform.rotation = spawn.rotation;

            CharacterController cc = player.AddComponent<CharacterController>();
            cc.height = 1.8f;
            cc.radius = 0.3f;
            cc.center = new Vector3(0f, 0.9f, 0f);

            GameObject camGO = new GameObject("PlayerCamera");
            camGO.transform.SetParent(player.transform, false);
            camGO.transform.localPosition = new Vector3(0f, 1.6f, 0f);
            Camera cam = camGO.AddComponent<Camera>();
            camGO.AddComponent<AudioListener>();
            cam.tag = "MainCamera";

            // Opt the camera into the volume stack - URP cameras ignore post-processing otherwise.
            UniversalAdditionalCameraData camData = camGO.AddComponent<UniversalAdditionalCameraData>();
            camData.renderPostProcessing = true;
            camData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;

            FirstPersonController fpc = player.AddComponent<FirstPersonController>();
            fpc.playerCamera = cam;

            PlayerRecorder recorder = player.AddComponent<PlayerRecorder>();
            recorder.interactables = ghostInteractables;

            return (player, fpc, recorder);
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
        private static (NarrationDirector, RoomAmbience) BuildAudio(GameObject player, Door door, WakeUpSequence wakeUp, Transform nightstand)
        {
            GameObject root = new GameObject("Audio");

            // --- the PA announcer ---
            GameObject paGO = new GameObject("PA");
            paGO.transform.SetParent(root.transform, false);
            NarrationDirector narration = paGO.AddComponent<NarrationDirector>();
            // 2D on purpose: a room-wide tannoy has no position you could walk away from.
            narration.voiceSource = MakeSource(paGO.transform, "Voice", 0f, 1f);
            narration.chimeSource = MakeSource(paGO.transform, "Chime", 0f, 0.7f);

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
            narration.announcementChime = LoadClip(SfxDir, "sfx_chime");

            // --- room tone and machinery ---
            GameObject ambienceGO = new GameObject("Ambience");
            ambienceGO.transform.SetParent(root.transform, false);
            RoomAmbience ambience = ambienceGO.AddComponent<RoomAmbience>();
            ambience.musicSource = MakeSource(ambienceGO.transform, "Music", 0f, 0.5f, loop: true);
            ambience.machineSource = MakeSource(ambienceGO.transform, "Machines", 0f, 0.7f);
            // Positional, and parented to the nightstand: the rattle is the lamp and vase standing
            // on it, so it has to come from over there rather than from the middle of your head.
            ambience.propRattleSource = MakeSource(nightstand, "PropRattle", 1f, 0.9f);
            ambience.ominousLoop = LoadClip(SfxDir, "sfx_ominous_loop");
            ambience.resetSting = LoadClip(SfxDir, "sfx_reset_sting");
            ambience.machinesRev = LoadClip(SfxDir, "sfx_machines_rev");
            ambience.propRattle = LoadClip(SfxDir, "sfx_glass_rattle");

            // --- footsteps ---
            // Player only. FootstepPlayer reads nothing but the transform, so the identical
            // component dropped on the Ghost prefab would give the ghosts footsteps too - left off
            // deliberately, since ghosts accumulate without limit and so would their steps.
            FootstepPlayer footsteps = player.AddComponent<FootstepPlayer>();
            footsteps.source = MakeSource(player.transform, "Footsteps", 0f, 0.8f);
            footsteps.footstepClips = LoadNumberedClips(SfxDir, "sfx_footstep_", 4);

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

        // Collects sfx_footstep_1..N, skipping gaps, so partially-filled sets still work.
        private static AudioClip[] LoadNumberedClips(string dir, string prefix, int count)
        {
            var clips = new System.Collections.Generic.List<AudioClip>();
            for (int i = 1; i <= count; i++)
            {
                AudioClip clip = LoadClip(dir, prefix + i);
                if (clip != null) clips.Add(clip);
            }
            return clips.ToArray();
        }

        private static (IterationLabel label, WakeUpSequence wakeUp) BuildUI()
        {
            GameObject canvasGO = new GameObject("Canvas");
            Canvas canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasGO.AddComponent<CanvasScaler>();
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
            rect.sizeDelta = new Vector2(600f, 100f);
            rect.anchoredPosition = new Vector2(0f, 150f);

            GameObject textGO = new GameObject("Label");
            textGO.transform.SetParent(groupGO.transform, false);
            Text text = textGO.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 42;
            text.alignment = TextAnchor.MiddleCenter;
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
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 32;
            text.alignment = TextAnchor.UpperRight;
            text.color = Color.red;

            CountdownTimer timer = go.AddComponent<CountdownTimer>();
            timer.label = text;
        }
    }
}
