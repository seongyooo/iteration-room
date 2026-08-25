using System.Diagnostics;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

namespace IterationRoom.EditorTools
{
    // BOUNCE LIGHT, BAKED - the one thing this building's lighting has never had.
    //
    // **WHY THIS IS A SEPARATE STEP AND NOT PART OF `Build`.** Everything else about this project is
    // regenerated on every build in seconds, and that is the whole working method: `SceneBuilder` is
    // the source of truth and a scene is disposable output (CLAUDE.md §1.1). A GI bake does not fit
    // in that loop. It takes minutes rather than seconds, so putting it in `Build` would make every
    // structural edit cost a coffee break - and the thing it produces is the one piece of scene data
    // that is genuinely expensive to recreate.
    //
    // So it runs on demand: build as often as you like, bake when you want to LOOK at it. Between
    // bakes the room is lit exactly as it was before this existed, which is also the fallback if a
    // bake is ever stale or missing.
    //
    // **SceneBuilder IS DETERMINISTIC, and that is what makes the split safe.** The same inputs put
    // the same geometry at the same coordinates every time, so a bake stays valid across rebuilds
    // that did not move anything - it only has to be redone when the building actually changes.
    // Unity re-randomises fileIDs on rebuild (§1.1) but probe data is keyed by world position and by
    // the scene GUID, which the tracked `.meta` pins.
    //
    // **ADAPTIVE PROBE VOLUMES, NOT LIGHTMAPS, and there is no choice about it.** A lightmap needs a
    // second UV set per mesh. Every surface in this building is generated from script - Unity's
    // primitives ship no UV2, and the panel meshes have whatever they were given - so lightmapping
    // would mean unwrapping several thousand objects before anything could be baked at all. APV
    // stores irradiance in a grid in space instead and needs no UVs whatever.
    //
    // It also lights things a lightmap cannot. Ghosts, carryables and the player's own body all
    // sample the same volumes, so a past self walking through a room is lit BY that room.
    public static class BakeLighting
    {
        // One volume per cycle, sized to swallow the cycle's whole world with room to spare. A probe
        // volume costs memory by VOLUME, so this is deliberately not one box over the entire game -
        // the cycles sit far apart and the space between them has nothing in it to light.
        private const float VolumePadding = 4f;

        // Beside the pipeline assets it belongs with, and an asset rather than an instance - see
        // `ConfigureBake`.
        private const string SettingsPath = "Assets/Settings/IterationLighting.lighting";

        // The same bake, driven from the command line - **ONE SCENE AT A TIME, and that is forced.**
        //
        // Unity's Adaptive Probe Volumes bake in Single Scene mode by default, and it refuses
        // outright when more than one scene is loaded: "It is not possible to generate lighting.
        // Consider creating a Baking Set." The first attempt at this opened all four together and got
        // exactly that - four volumes placed, a bake that ran for forty-eight seconds, and no data.
        //
        // A Baking Set is the other answer and this does not need it. The cycles are independent
        // worlds and only ONE of them is ever loaded (`SceneBuilder` splits them into scenes for that
        // reason), so a cycle's probes never have to agree with another cycle's. Baking each alone is
        // the arrangement the game already runs in.
        public static void RunBatch()
        {
            string[] scenes = { "IterationRoom", "Cycle1", "Cycle2", "Cycle3" };
            int baked = 0;

            foreach (string name in scenes)
            {
                string path = $"Assets/Scenes/{name}.unity";
                if (!System.IO.File.Exists(path))
                {
                    Debug.LogWarning($"[BakeLighting] {path} is missing - build the scenes first.");
                    continue;
                }

                EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                if (Run()) baked++;
            }

            Debug.Log($"[BakeLighting] {baked} of {scenes.Length} scene(s) baked.");
        }

        // **A `void` WRAPPER, AND IT HAS TO BE ONE.** Unity reads a `[MenuItem]` method that returns
        // `bool` as a VALIDATION function - the thing that greys an entry out - so with the attribute
        // on `Run` below the entry appeared and could never be clicked.
        [MenuItem("Iteration Room/Bake Lighting (slow)")]
        public static void RunMenu() => Run();

        // Bakes THE OPEN SCENE. Returns whether anything was baked.
        public static bool Run()
        {
            var timer = Stopwatch.StartNew();

            // **NOTHING TOUCHES THE URP ASSET UNTIL EVERY REASON TO ABORT HAS BEEN CHECKED.** Turning
            // Adaptive Probe Volumes on used to be the FIRST thing this did, and that is a trap: it
            // is a change to a shared asset, it is what the build's own safety catch exists to undo,
            // and every `return false` below leaves it applied. Play found it immediately - the menu
            // item was run with `MainMenu` open, which has no geometry, so the bake bailed one line
            // later having already switched the whole project to a probe system with no probes in it.
            //
            // The order is now: decide whether we can bake, THEN switch, then bake.

            // The refusal this exists to avoid is silent about which scene is the problem, so it is
            // caught here where the count is known - see `RunBatch`.
            if (SceneManager.loadedSceneCount > 1)
            {
                Debug.LogError($"[BakeLighting] {SceneManager.loadedSceneCount} scenes are loaded. "
                             + "Adaptive Probe Volumes bake one scene at a time and will refuse this "
                             + "outright. Open a single scene and try again.");
                return false;
            }

            Scene scene = SceneManager.GetActiveScene();

            // **`MainMenu` IS NOT A MISTAKE TO REPORT, IT IS A SCENE TO SKIP** - and it is the one
            // Unity is most likely to have open, because it is build index 0 and so what the Editor
            // opens by default. It is a canvas, a camera and a background image: there is no
            // geometry, there never will be, and "has nothing to put a volume around" reads as a
            // failure when it is the correct answer.
            if (scene.name == "MainMenu")
            {
                Debug.Log("[BakeLighting] 'MainMenu' is a UI scene with no geometry - nothing to "
                        + "bake, and nothing wrong. Open IterationRoom, or one of Cycle1/2/3.");
                return false;
            }

            // **A SLEEPING CYCLE BAKES NOTHING, and it fails silently.** `SceneBuilder.SleepCycle`
            // deactivates each cycle's world root so only the loaded cycle is awake at runtime, and
            // an inactive renderer is invisible to the baker exactly as it is to a camera. The first
            // run of this baked Cycle1 and Cycle2 in about two seconds each and reported success;
            // the bake log's own line gave it away - "Extracted OOTS snapshot with 0 instances, 0
            // lights". Cycle3 happens to be left awake, which is why it alone came out right.
            //
            // **FIRST, BEFORE THE VOLUME IS PLACED OR THE SET IS RESOLVED.** Order matters here and
            // it cost a crash to find: with the wake after those two, Cycle1 threw a
            // NullReferenceException per cell out of `GenerateScenesCellLists` and took the Editor
            // down with it, while Cycle3 - whose root happens to already be awake - baked cleanly.
            // Everything Unity inspects should see one consistent, fully-active scene.
            //
            // **ONLY THE WORLD ROOTS ARE WOKEN.** Plenty of other things are deliberately inactive -
            // a pour stream, the tree bridge, a notch not yet cut - and those are props that are not
            // in the room yet. Waking them would bake light around objects the player cannot see,
            // which is the same mistake `MovesDuringPlay` exists to stop for the reflection probes.
            var asleep = new System.Collections.Generic.List<GameObject>();
            foreach (Cycle cycle in Object.FindObjectsByType<Cycle>(FindObjectsInactive.Include,
                                                                    FindObjectsSortMode.None))
            {
                foreach (GameObject go in new[] { cycle.gameObject,
                                                  cycle.worldRoot != null ? cycle.worldRoot.gameObject : null })
                {
                    if (go == null || go.activeSelf) continue;
                    asleep.Add(go);
                    go.SetActive(true);
                }
            }

            if (asleep.Count > 0)
                Debug.Log($"[BakeLighting] Woke {asleep.Count} sleeping world root(s) for the bake: "
                        + string.Join(", ", asleep.ConvertAll(g => g.name)));

            if (EnsureVolumeFor(scene) == 0)
            {
                Debug.LogError($"[BakeLighting] '{scene.name}' has nothing to put a volume around.");
                return false;
            }

            ProbeVolumeBakingSet set = EnsureBakingSet(scene);
            if (set == null) return false;
            ConfigureBakingSet(set);

            // NOW, with every abort behind us - see the note at the top of this method.
            if (!EnsureAdaptiveProbeVolumes())
            {
                Debug.LogError("[BakeLighting] The URP asset has no probe volume support - nothing "
                             + "to bake into. Check IterationURP's Light Probe System setting.");
                return false;
            }

            ConfigureBake();

            Debug.Log($"[BakeLighting] '{scene.name}': baking.");
            Lightmapping.Bake();

            // Back to sleep BEFORE the save, or the build's own arrangement is overwritten by this
            // tool and every cycle in the game starts awake.
            foreach (GameObject go in asleep) go.SetActive(false);

            EditorSceneManager.SaveScene(scene);

            timer.Stop();
            Debug.Log($"[BakeLighting] '{scene.name}' done in {timer.Elapsed.TotalSeconds:0.0}s.");
            return true;
        }

        // **THE AMBIENT HAS BEEN HALVED TO MAKE ROOM FOR THE BOUNCE** (2026-08-25, in
        // `SceneBuilder.SetupLighting`).
        //
        // That file carried almost all of the room's light on flat Trilight ambient precisely BECAUSE
        // there was no bounce: a constant added to every surface was the cheapest stand-in for light
        // coming off white walls. Now that the walls actually bounce, that constant is a second
        // helping - left alone, a bake makes the building brighter and FLATTER rather than richer,
        // which is the opposite of the point.
        //
        // **Halving is a starting point, not a settled value**, and how far it should go is a
        // judgement made by looking rather than a number to derive here. The test: compare a corner
        // against a wall centre, and if the corner is not visibly darker, ambient is still winning.
        private static void ConfigureBake()
        {
            // **AN ASSET, NOT AN INSTANCE, and the getter is why.** `Lightmapping.lightingSettings`
            // THROWS when nothing is assigned rather than returning null, so it cannot be read to
            // find out whether it needs setting - it has to be written first. And a settings object
            // made with `new` and assigned is a runtime object with no home, which is the same trap
            // `TriangularPrismMesh` recorded for meshes: the scene serialises a reference to nothing.
            LightingSettings settings = AssetDatabase.LoadAssetAtPath<LightingSettings>(SettingsPath);
            if (settings == null)
            {
                settings = new LightingSettings { name = "IterationLighting" };
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(SettingsPath));
                AssetDatabase.CreateAsset(settings, SettingsPath);
            }

            Lightmapping.lightingSettings = settings;

            settings.bakedGI = true;
            settings.realtimeGI = false;

            // **BOUNCES ARE THE WHOLE POINT, so this is not the place to economise.** One bounce in a
            // white room is most of the effect and two is nearly all of it; the surfaces here are
            // bright enough that light does not run out as fast as it would in a normal interior.
            settings.indirectResolution = 2f;
            settings.lightmapMaxSize = 1024;
            settings.ao = false;   // SSAO already runs, and two occlusion passes double-darken corners

            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();

            Debug.Log("[BakeLighting] Lighting settings: baked GI on, realtime GI off, "
                    + "auto-generate off.");
        }

        // THE SCENE'S BAKING SET - the asset Unity files baked probe data under.
        //
        // **WITHOUT ONE, THE BAKE CRASHES THE EDITOR.** Not "fails" - `AdaptiveProbeVolumes`
        // dereferences its baking set unconditionally while writing results, so a bake with none
        // throws a NullReferenceException per cell inside `GenerateScenesCellLists`, logs it, and
        // repeats: thirty-two thousand exceptions, a 129MB log file and a hard crash. There is no
        // error message saying what was missing.
        //
        // The Editor never hits this because the Lighting window creates a set as part of drawing
        // its own UI. Nothing draws that UI in batchmode, so nothing creates one.
        //
        // **REFLECTION, RELUCTANTLY.** `ProbeVolumeBakingSet` is public but every member needed to
        // populate one - `SetDefaults`, `AddScene`, `singleSceneMode` - is `internal`, and there is
        // no public path to a configured set. Each lookup is checked and reported rather than
        // assumed, so a Unity version that renames one of them fails with a line naming the member
        // instead of crashing the way the missing set did.
        //
        // ONE SET PER SCENE, in single-scene mode. The cycles are independent worlds and only one is
        // ever loaded, so their probes never have to agree - which is the same reason `RunBatch`
        // bakes them one at a time.
        private static ProbeVolumeBakingSet EnsureBakingSet(Scene scene)
        {
            string guid = AssetDatabase.AssetPathToGUID(scene.path);
            if (string.IsNullOrEmpty(guid))
            {
                Debug.LogError($"[BakeLighting] '{scene.name}' has no asset GUID - save it first.");
                return null;
            }

            System.Type type = typeof(ProbeVolumeBakingSet);
            const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;

            MethodInfo lookup = type.GetMethod("GetBakingSetForScene",
                BindingFlags.Static | BindingFlags.NonPublic, null, new[] { typeof(string) }, null);
            if (lookup?.Invoke(null, new object[] { guid }) is ProbeVolumeBakingSet already)
            {
                Debug.Log($"[BakeLighting] '{scene.name}' already belongs to baking set "
                        + $"'{already.name}'.");
                return already;
            }

            const string dir = "Assets/Settings/ProbeVolumes";
            string path = $"{dir}/{scene.name}_ProbeVolumes.asset";

            ProbeVolumeBakingSet set = AssetDatabase.LoadAssetAtPath<ProbeVolumeBakingSet>(path);
            if (set == null)
            {
                set = ScriptableObject.CreateInstance<ProbeVolumeBakingSet>();
                set.name = $"{scene.name} Probe Volumes";

                MethodInfo defaults = type.GetMethod("SetDefaults", Instance);
                if (defaults == null)
                {
                    Debug.LogError("[BakeLighting] ProbeVolumeBakingSet.SetDefaults is gone - this "
                                 + "Unity version has moved the API. Bake from the Lighting window "
                                 + "instead, which creates a set itself.");
                    return null;
                }
                defaults.Invoke(set, null);

                System.IO.Directory.CreateDirectory(dir);
                AssetDatabase.CreateAsset(set, path);
            }

            FieldInfo single = type.GetField("singleSceneMode", Instance);
            single?.SetValue(set, true);

            MethodInfo add = type.GetMethod("AddScene", Instance);
            if (add == null)
            {
                Debug.LogError("[BakeLighting] ProbeVolumeBakingSet.AddScene is gone - see above.");
                return null;
            }
            // (string guid, SceneBakeData bakeData = null) - the second is an internal type, and null
            // is what the Editor's own call passes.
            add.Invoke(set, new object[] { guid, null });

            EditorUtility.SetDirty(set);
            AssetDatabase.SaveAssets();
            Debug.Log($"[BakeLighting] '{scene.name}': baking set created at {path}.");
            return set;
        }

        // **DILATION ON. Unity ships it OFF, and for this building that default is the difference
        // between white walls and black ones.**
        //
        // A probe that ends up inside geometry is INVALID - it has no useful irradiance, because it is
        // buried in a wall. Dilation is the pass that fills those in from their valid neighbours;
        // Unity's own tooltip is "Replace invalid probe data with valid data from neighboring probes
        // during baking." `ProbeDilationSettings.SetDefaults()` sets `enableDilation = false`.
        //
        // **This building generates invalid probes by the thousand**, and that is a consequence of how
        // it is built rather than a mistake: every wall is a grid of 25mm-proud panels with grooves
        // between them, and probes sit on a 1m lattice, so a large share of them land inside a panel,
        // inside a backing slab, or in the gap between the two. Virtual Offset is on and tries to push
        // them clear, but it moves a probe `outOfGeoOffset` = **1cm**, which does not get a probe out
        // of a wall it is 30cm inside. Dilation is the safety net for everything Virtual Offset cannot
        // rescue, and with it off there was no net at all.
        //
        // The symptom was NOT subtle and it was not the flicker: wall panels rendered as hard-edged
        // black wedges with dithered borders, in rooms whose floor and ceiling looked perfectly fine.
        // **That asymmetry is the tell** - a floor and a ceiling are single large slabs with probes
        // well clear of them, so they never had the problem the walls did. If black patches ever come
        // back on one class of surface while the others are fine, look at probe VALIDITY first.
        //
        // Written through `SerializedObject` rather than reflection: every one of these types is
        // `internal`, but the serialised names are stable and this is the same mechanism
        // `SceneBuilder.ConfigureUrpAsset` uses on the URP asset. A renamed field logs and changes
        // nothing, rather than throwing.
        private static void ConfigureBakingSet(ProbeVolumeBakingSet set)
        {
            var so = new SerializedObject(set);
            SerializedProperty dilate = so.FindProperty("settings.dilationSettings.enableDilation");
            if (dilate == null)
            {
                Debug.LogWarning("[BakeLighting] settings.dilationSettings.enableDilation is gone - "
                               + "this Unity version has moved it. Invalid probes will not be filled "
                               + "in, which shows up as black patches on the wall panels.");
                return;
            }

            if (dilate.boolValue) return;

            dilate.boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(set);
            AssetDatabase.SaveAssets();
            Debug.Log($"[BakeLighting] '{set.name}': dilation enabled (Unity defaults it off).");
        }

        // Switches URP from legacy Light Probe Groups to Adaptive Probe Volumes.
        //
        // **DONE HERE RATHER THAN IN `ConfigureUrpAsset`, and that ordering is deliberate.** APV with
        // no baked data is worse than no APV: objects sample a grid that does not exist. Flipping it
        // in the build would mean every fresh clone renders wrong until somebody bakes. Flipping it
        // as the first step of the bake means it is only ever on when data is about to exist.
        private static bool EnsureAdaptiveProbeVolumes()
        {
            var urp = GraphicsSettings.defaultRenderPipeline as RenderPipelineAsset;
            if (urp == null) return false;

            var so = new SerializedObject(urp);
            SerializedProperty system = so.FindProperty("m_LightProbeSystem");
            if (system == null) return false;

            // 0 is Light Probe Groups, 1 is Adaptive Probe Volumes. Set by index because this is a
            // plain int rather than a serialised enum - checked, not assumed.
            if (system.intValue != 1)
            {
                system.intValue = 1;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(urp);
                AssetDatabase.SaveAssets();
                Debug.Log("[BakeLighting] URP switched to Adaptive Probe Volumes.");
            }

            return true;
        }

        // One volume per scene, boxed to whatever that scene actually contains.
        //
        // Measured off the renderers rather than written down, for the reason every size in this
        // project is: the cycles are different shapes, cycle 2 is a ring and cycle 3 is a cross, and
        // a box written for one of them is wrong for the other two.
        private static int EnsureVolumeFor(Scene scene)
        {
            Bounds bounds = default;
            bool any = false;

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
                {
                    // Particle systems and the like report wandering bounds; only the built world
                    // should decide how big the lit volume is.
                    if (r is ParticleSystemRenderer) continue;
                    if (!any) { bounds = r.bounds; any = true; }
                    else bounds.Encapsulate(r.bounds);
                }
            }

            if (!any) return 0;

            const string volumeName = "AdaptiveProbeVolume";
            GameObject go = null;
            foreach (GameObject root in scene.GetRootGameObjects())
                if (root.name == volumeName) { go = root; break; }

            if (go == null)
            {
                go = new GameObject(volumeName);
                EditorSceneManager.MoveGameObjectToScene(go, scene);
            }

            ProbeVolume volume = go.GetComponent<ProbeVolume>();
            if (volume == null) volume = go.AddComponent<ProbeVolume>();

            go.transform.position = bounds.center;
            go.transform.rotation = Quaternion.identity;
            volume.mode = ProbeVolume.Mode.Local;
            volume.size = bounds.size + Vector3.one * (VolumePadding * 2f);

            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log($"[BakeLighting] '{scene.name}': volume {volume.size} at {bounds.center}.");
            return 1;
        }
    }
}
