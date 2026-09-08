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

            // **`-bakeScenes A,B` BAKES ONLY THOSE**, because tuning light is a loop and the loop is
            // one room. A full run is four scenes and about four minutes at two bounces, and more at
            // sixteen; the core scene holds no rooms at all and bakes in seconds, which makes
            // it the right place to answer "did that setting do anything" before paying for the rest.
            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] != "-bakeScenes") continue;
                scenes = args[i + 1].Split(',');
                Debug.Log($"[BakeLighting] -bakeScenes: {string.Join(", ", scenes)}");
                break;
            }

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
            foreach (Cycle cycle in Object.FindObjectsByType<Cycle>(FindObjectsInactive.Include))
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

            // **THE VOLUME IS THE BUILD'S, NOT THIS TOOL'S** (2026-08-26). Creating it here is what
            // broke the feature for days: `SceneBuilder.Build()` rebuilds a scene from nothing, so
            // the next build after a bake deleted the volume and the `ProbeVolumePerSceneData` beside
            // it, leaving tens of megabytes of baked cells on disk with nothing to load them. See
            // `SceneBuilder.EnsureProbeVolume`.
            if (Object.FindAnyObjectByType<ProbeVolume>() == null)
            {
                Debug.LogError($"[BakeLighting] '{scene.name}' has no ProbeVolume. Run "
                             + "'Iteration Room/Build Whitebox Scene' first - the build creates it.");
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

            // **SAY SO IF THE RESULT WILL BE IGNORED.** APV is off (`SceneBuilder.AnyBakedProbeVolumes`),
            // and a bake takes four minutes across the four scenes. Without this line the menu item
            // runs, reports success, writes tens of megabytes - and changes nothing on screen, which
            // is a very expensive way to learn where the switch is.
            var urp = GraphicsSettings.defaultRenderPipeline;
            if (urp != null && new SerializedObject(urp).FindProperty("m_LightProbeSystem")?.intValue == 0)
                Debug.LogWarning("[BakeLighting] URP is on LegacyLightProbes, so NOTHING BAKED HERE "
                               + "WILL BE VISIBLE. Adaptive Probe Volumes are switched off in "
                               + "SceneBuilder.AnyBakedProbeVolumes - see the comment there for why, "
                               + "and docs/gotchas.md for what was tried. Baking anyway.");

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
            // **EIGHT BOUNCES, AGAINST UNITY'S DEFAULT OF TWO** (2026-08-25).
            //
            // Two is a sane default and it is badly wrong for THIS building. Light loses almost
            // nothing per bounce here: the walls and ceiling are albedo 1.0 and the floor 0.85, so
            // the indirect is a geometric series that barely decays. Against the infinite sum, two
            // bounces captures about **19%** of the real indirect light in this room; four gets 34%,
            // eight 57%, sixteen 81%. **Sixteen was tried first and play called it over-bright**, so
            // eight is not a compromise for speed - it is where the room wanted to sit.
            //
            // **That is most of why the bake looked like it was not working.** Its first outing left
            // the walls and ceiling so dark that the ambient fudge standing in for them had to go UP
            // (equator 0.644 -> 0.659, ground 0.719 -> 0.843) even as the floor's came down 82%. The
            // floor is lit directly and bounces once; the walls and ceiling live entirely on the tail
            // of that series, which is exactly the part two bounces throws away.
            //
            // **It costs less bake time than expected**: `IterationRoom` went 5.3s at two bounces to
            // 6.8s at sixteen, so this is not the binding cost on a small scene. Watch Cycle2, which
            // is the big one, before assuming that holds everywhere.
            //
            // Written through `SerializedObject` rather than a property: the serialised name is known
            // from the asset (`m_PVRBounces`) and the C# one is not documented in this version's XML,
            // so this is the spelling that can actually be checked. Same mechanism, and the same
            // report-rather-than-assume, as `ConfigureBakingSet` and `SceneBuilder.ConfigureUrpAsset`.
            var so = new SerializedObject(settings);

            // **BAKED INDIRECT, NOT SHADOWMASK - because Shadowmask was eating the direct light off
            // every wall in the building** (2026-08-26).
            //
            // Unity defaults a Mixed light to Shadowmask. In that mode a lightmap-static renderer -
            // which is every surface here, since `MarkReflectionProbeStatic` sets `ContributeGI` on
            // all of them - does not simply receive a Mixed light's direct contribution: it receives
            // it multiplied by an OCCLUSION MASK, and with Adaptive Probe Volumes that mask comes out
            // of the probe occlusion data rather than a lightmap.
            //
            // **The measurement that found it.** With ambient at zero and APV switched off entirely,
            // so that realtime direct light was the only thing left, the editor's own render of the
            // room put the walls at **117-155 of 255** - lit, and exactly where the cosine falloff
            // from a 130-degree cone at 5.41m says they should be. In PLAY, with APV on, the same
            // walls measured **4-8**. More light sources available, and less light arriving: the
            // direct term was being multiplied away.
            //
            // That is also why the room could never be lit without a flat ambient constant, and why
            // raising the fixtures to 17 only clipped the floor - the walls were not short of light,
            // they were being denied the light they already had.
            //
            // `IndirectOnly` is the mode that bakes only what bounces and leaves direct light fully
            // realtime, with no mask over it. It is what this project's Mixed lights were always
            // meant to be doing: `SceneBuilder` chose Mixed so `ShadowBudget` could keep switching a
            // REAL shadow at runtime, which is a statement about direct light staying live.
            //
            // MixedLightingMode: 0 = IndirectOnly, 1 = Subtractive, 2 = Shadowmask.
            SerializedProperty mixed = so.FindProperty("m_MixedBakeMode");
            if (mixed == null)
                Debug.LogWarning("[BakeLighting] m_MixedBakeMode is gone - this Unity version has "
                               + "moved it. If the walls go dark, that is this.");
            else
                mixed.intValue = 0;

            SerializedProperty bounces = so.FindProperty("m_PVRBounces");
            if (bounces == null)
            {
                Debug.LogWarning("[BakeLighting] m_PVRBounces is gone - this Unity version has moved "
                               + "it. The bake will run at Unity's default of 2, which in a room this "
                               + "white captures about a fifth of the real indirect light.");
            }
            else
            {
                bounces.intValue = 8;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

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

            // **VIRTUAL OFFSET HAS TO REACH PAST A PANEL, AND UNITY'S DEFAULT REACHES 1cm**
            // (2026-08-26). A wall here is not a flat surface: it is a 25mm panel standing proud of a
            // backing slab with a groove between them, so the gap behind each panel is a SEALED
            // CAVITY. A probe that lands in one correctly receives no light - and a wall surface
            // sampling that probe comes out black.
            //
            // Virtual Offset exists to push such a probe back out into the room, but
            // `outOfGeoOffset` defaults to **0.01m**, which cannot clear a 25mm panel, and
            // `searchMultiplier` 0.2 barely looks for a way out. Dilation then fills from neighbours
            // that are often in the same cavity.
            //
            // **The measurement that motivates this**: converted to linear, the walls sit at 4.6% of
            // the floor's brightness. Form-factor maths for a room this shape says a wall should
            // receive 25-30% of what the floor does. About six times the light is going missing, and
            // "downlights point at the floor" does not account for a factor of six.
            //
            // 0.05m clears the panel and the groove; 1.0 searches a full probe spacing for open air.
            SerializedProperty geoOffset = so.FindProperty("settings.virtualOffsetSettings.outOfGeoOffset");
            SerializedProperty search = so.FindProperty("settings.virtualOffsetSettings.searchMultiplier");
            if (geoOffset != null) geoOffset.floatValue = 0.05f;
            if (search != null) search.floatValue = 1.0f;
            if (geoOffset == null || search == null)
                Debug.LogWarning("[BakeLighting] virtualOffsetSettings fields have moved - probes "
                               + "stuck inside wall panels will not be rescued.");

            if (dilate.boolValue && geoOffset == null) return;

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
