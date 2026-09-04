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
    // THE PROJECT'S OWN SETTINGS, rather than any object in a room: asset folders, the balloon
    // layer, URP's renderer features, post-processing, the lighting pipeline and per-scene
    // environment. `ApplyEnvironment` writes to the ACTIVE scene and is called once per scene,
    // because `RenderSettings` is per scene and this build makes five (CLAUDE.md 3).
    //
    // Split out of `SceneBuilder.cs` (2026-09-02), which had reached 25,000 lines. One class,
    // many files - see the note over the `partial` keyword there. Everything private in any part
    // is reachable from every other part, so nothing about the build changed when this moved.
    public static partial class SceneBuilder
    {

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

            // **APV'S SAMPLING NOISE IS A TAA FEATURE, AND THIS PROJECT HAS NO TAA.**
            //
            // Adaptive Probe Volumes dither the probe sampling position to hide the seams between
            // subdivision levels, and Unity's own tooltip on `animateSamplingNoise` says what it is
            // for: *"Whether to animate the noise WHEN TAA IS ENABLED, smoothing potentially out the
            // noise pattern introduced."* It defaults to **on**, with 0.1 of noise.
            //
            // The player camera runs **SMAA** (`BuildPlayer`), which resolves one frame at a time and
            // cannot smooth anything across frames. So the dither was simply re-rolled every frame
            // with nothing to average it out, and every wall panel in the building visibly pulsed the
            // instant APV was turned on (2026-08-25, reported from play as "밝기가 튀는" - brightness
            // popping). Nothing was wrong with the bake; this is the sampler.
            //
            // **Noise to zero as well, not just un-animated.** A static dither is a fixed grain, and
            // this project has already learnt once what fine grain does to a white room - the SSAO
            // radius note calls 0.12 a halo that "on white walls reads as *dirt*". Clinical evenness
            // is the look. **If seams appear between subdivision levels** - a visible step in
            // brightness partway along a wall, which is what the noise exists to hide - raise this
            // toward 0.1 rather than turning the animation back on.
            //
            // `leakReductionMode` is left at Unity's default (`Quality`), which is already the good
            // one; it is named here only so the next person knows it was considered.
            ProbeVolumesOptions probes = GetOrAddOverride<ProbeVolumesOptions>(profile);
            probes.animateSamplingNoise.overrideState = true;
            probes.animateSamplingNoise.value = false;
            probes.samplingNoise.overrideState = true;
            probes.samplingNoise.value = 0f;

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
            // **4096, HOLDING EXACTLY FOUR MAPS OF 2048** (2026-08-25, by request, after play).
            //
            // The atlas, `ShadowBudget.maxCasters` and the tier below are ONE decision and must move
            // together. A 2048 atlas is 4.2M texels, which buys either four maps of 1024 or one of
            // 2048 - and play rejected BOTH: four of 1024 gave 22.7mm texels and a 20cm key threw a
            // 10cm smudge, while one of 2048 was sharp but made the shadow JUMP as the player crossed
            // the room, because the single caster is whichever fixture is nearest and that changes.
            //
            // 4096 is 16.8M and takes four maps of 2048: sharp AND stable, plus the soft overlap a
            // ceiling of four panels genuinely produces. **The cost is four times the shadow pixels
            // this game drew before**, and this number was 4096 once before and came down after play
            // reported lag - so it is the first thing to put back if the frame rate suffers. The
            // suspected cause then was full-screen passes rather than this, but that was never
            // measured.
            // **If the console ever prints "Reduced additional punctual light shadows resolution ...
            // to make N shadow maps fit" again, either `maxCasters` went up or something is casting
            // that this does not know about.** That line is the only warning URP gives before it
            // silently halves every map in the building.
            //
            // **2048 PER LIGHT.** A 130-degree cone from 5.41m spreads its map over 23.2m of floor, so
            // this is what sets the texel: 1024 gave 22.7mm and a key's shank is thinner than one of
            // those, which is how a 20cm key came out as a 10cm smudge. 2048 halves it to 11.3mm.
            //
            // **Paired with `maxCasters` 4 and a 4096 atlas above** - raise either without the other
            // and URP silently halves every map in the building, warning once.
            SetEnumByName(so, "m_AdditionalLightsShadowmapResolution", "_4096");
            // The per-light tiers URP picks from. `UniversalAdditionalLightData` defaults a light to
            // the HIGH one, so this is what actually reaches the fixtures.
            SetIfPresent(so, "m_AdditionalLightsShadowResolutionTierHigh", 2048);
            SetIfPresent(so, "m_SoftShadowsSupported", true);

            // **THE PROBE SYSTEM FOLLOWS THE DATA - it is not a constant, and it used to be.**
            //
            // `BakeLighting` switches this to APV as the first step of a bake, because APV is the only
            // GI this project can use (nothing here has lightmap UVs; see that file). But **APV with
            // no baked data is worse than no APV**: every renderer marked `ReceiveGI.LightProbes` -
            // which is all 3,432 of them - samples a grid that does not exist.
            //
            // So this was pinned to `LegacyLightProbes` on every build, as a safety catch, with a note
            // saying that IF A BAKE WERE EVER MADE TO STICK THE TWO HALVES WOULD HAVE TO MOVE
            // TOGETHER. **They now do** (2026-08-25): the bake works, so pinning it off would silently
            // throw away every cell of it on the next rebuild - a bake nobody could see the result of,
            // which is a worse failure than the crash was, because it does not announce itself.
            //
            // **The catch is kept, only asked as a QUESTION rather than assumed.** No baked data
            // anywhere - a fresh clone, or a bake that has not been run yet - still lands on legacy,
            // which is the state that renders correctly without one.
            //
            // BY NAME, because this is a serialised enum and `SetIfPresent(int)` refuses those on
            // purpose - the same guard that silently ignored the first attempt at `m_MSAA`. The
            // values are `LegacyLightProbes = 0` and `ProbeVolumes = 1`.
            bool haveBakedProbes = AnyBakedProbeVolumes();
            SetEnumByName(so, "m_LightProbeSystem",
                haveBakedProbes ? "ProbeVolumes" : "LegacyLightProbes");
            Debug.Log(haveBakedProbes
                ? "[SceneBuilder] Baked probe volumes found - URP left on Adaptive Probe Volumes."
                : "[SceneBuilder] URP on LegacyLightProbes - Adaptive Probe Volumes are OFF by "
                + "choice, not for want of a bake. See SceneBuilder.AnyBakedProbeVolumes.");

            // **MSAA 4 AGAIN, 2026-09-01, and this deliberately re-opens a decision.**
            //
            // It was 4, went to 2 on 2026-08-25 after play reported lag, and is back at 4 because
            // play reported the thing 2 was not enough for: stair-stepping on the black panel
            // grooves. Both reports are real and they pull opposite ways, so what settles it is that
            // this is the RIGHT TOOL for that particular artefact and the alternatives are worse
            // trades:
            //
            //   - The groove is a GEOMETRIC edge between 0.04 and 0.85 albedo - the highest-contrast
            //     edge in the game, and there are thousands of them. MSAA is coverage sampling, which
            //     is exactly what a geometric edge needs; 2 samples give three levels of coverage
            //     (0, half, full) and 4 give five. On an edge this contrasty that difference is
            //     visible.
            //   - SMAA is already running at High (`BuildPlayer`) and cannot fix it: it is a
            //     morphological filter on the RESOLVED image, so the information MSAA would have kept
            //     is gone before it sees the frame.
            //   - Render scale 1.25 also works and costs more - every pixel of the whole pipeline,
            //     post-processing included, against MSAA's extra samples at edges only.
            //   - `GrooveDark` 0.04 -> 0.12 was measured at about a 20% reduction in displayed
            //     contrast. That is a mitigation, not a fix, and it repaints the whole building.
            //
            // **THE GROOVE DEPTH WENT FIRST, AND IT IS WHY THIS IS WORTH SPENDING.** 25mm -> 15mm
            // (see `GrooveDepth`) fixed the seams BREAKING UP at grazing angles, which was the larger
            // and uglier half and cost nothing. What is left is ordinary edge aliasing, which is the
            // half that has to be bought.
            //
            // **IF IT LAGS AGAIN, THIS IS THE FIRST THING TO PUT BACK TO `_2x`** - one word - and the
            // ENDING is where to measure it: four cycles awake at once and a 766k-triangle cable car
            // is the heaviest frame in the game by a distance, and an ordinary room says nothing
            // about it. "Disabled" is the bigger saving after that.
            //
            // BY NAME, because `m_MSAA` is an enum and `SetIfPresent(int)` refuses those on purpose:
            // MsaaQuality's values are 1/2/4/8 and its INDICES are 0/1/2/3, so assigning the number
            // you want picks a different mode. The first attempt at this line did exactly that and
            // was silently ignored by the guard, which is the guard working.
            SetEnumByName(so, "m_MSAA", "_4x");

            // **RENDER SCALE 1.25, 2026-09-01 - SUPERSAMPLING, AND THE LAST LEVER THERE IS.**
            //
            // Authored here rather than left in the asset for the reason the MSAA above is: it sat at
            // 1 with nothing anywhere saying so, which is the state that note calls "a number nobody
            // has examined".
            //
            // WHY IT IS BEING SPENT. The panel grooves still stair-step at distance and at the edge
            // of the frame, in a STILL frame. Everything cheaper has been tried and the ladder is
            // worth writing down, because the next person will be tempted to climb it again:
            //
            //   - `GrooveDepth` 25 -> 15 -> 10mm and `PanelChamfer` 6 -> 3mm. These fixed the seams
            //     breaking up at grazing angles and narrowed the soft transition band. Free, and they
            //     are the reason this is now ordinary edge aliasing rather than three problems at once.
            //   - MSAA 2 -> 4. Helped, and it is the right tool for a geometric edge.
            //   - TAA was considered and RULED OUT: the artefact is visible in a still frame, and TAA
            //     accumulates across frames. It would buy ghosting and fix nothing here.
            //
            // What is left is a thin, very high contrast line (albedo 0.04 against 0.85) that falls
            // below a pixel at distance. No spatial filter recovers detail finer than its own
            // sampling rate - the only thing that does is sampling finer, which is this.
            //
            // ~~1.25 rather than 1.5~~ **BACK TO 1.0, 2026-09-01, by request: cycle 2 drops frames.**
            //
            // It was the most expensive change in the rendering setup and it went first, exactly as
            // the note above said it should. Unlike MSAA it costs every pixel of every pass,
            // post-processing included - 1.25 is 1.56x the pixels, so this is 36% of all pixel work
            // back, and it takes the MSAA resolve down with it because that scales with the buffer.
            //
            // **MSAA STAYS AT 4**, deliberately. It is the cheaper of the two and it is the right
            // tool for the problem this pair was raised to fix - a thin, very high contrast groove
            // line (albedo 0.04 against 0.85) stair-stepping at grazing angles. Giving up the
            // supersample loses the sub-pixel detail; giving up MSAA as well would put the stepping
            // straight back.
            //
            // **AND IT MAY NOT BE THE RIGHT LEVER, WHICH IS WORTH RECORDING.** Fill cost is uniform
            // and this complaint is not: cycle 2 alone is slow, and cycle 2 alone has twenty-odd
            // ghosts (skinned, afterimage-shaded, animation-scrubbed, growing with the iteration
            // count), 210 pool balls and a water surface. If the frame rate still falls as the
            // iterations pile up, the cost is the ghosts and no rendering number will touch it.
            SetIfPresent(so, "m_RenderScale", 1.0f);

            // **DEPTH BIAS 0.5, NOT URP'S DEFAULT 1** (2026-08-24, by request). Authored here rather
            // than left to the asset for the reason every tuned value is (CLAUDE.md §2) - the asset is
            // rewritten on every build, so a number only an inspector knows is a number that survives
            // by luck.
            //
            // What it trades: a shadow map stores one depth per texel, so a surface whose depth varies
            // within a texel shadows ITSELF in stripes - acne. Depth bias pushes the stored depth away
            // from the light to stop that, and the side effect is that the whole shadow shifts, which
            // reads as the object floating above its own shadow (peter-panning). **Too low is acne,
            // too high is a detached shadow, and both are visible on the floor of any room here.**
            //
            // Halved because detachment is the fault this building actually reported and 2048 maps
            // over a 130-degree cone need less bias than 1024 did. Normal bias is left at 1: it buys
            // the same protection by moving the SAMPLE along the surface normal instead, which costs
            // thin shadows rather than contact, so it is the wrong one to spend first.
            //
            // **Neither number is verified by eye.** If the floor develops stripes, this is what did
            // it. See docs/rendering-notes.md.
            SetFloatIfPresent(so, "m_ShadowDepthBias", 0.5f);
            SetFloatIfPresent(so, "m_ShadowNormalBias", 1f);

            // OFF, because there is no main light. URP's main light is the brightest DIRECTIONAL
            // light and this project deletes the scene's - the rooms are sealed boxes with a
            // ceiling slab, so a sun has no way in. Keeping its shadows enabled reserved a 2048 map
            // and a shadow pass for a light that does not exist.
            SetIfPresent(so, "m_MainLightShadowsSupported", false);

            // **30, RAISED BACK FROM 15** (2026-08-25, after play: "a shadow only appears once you
            // get close, even though the object is already on screen"). That is precisely what this
            // number does - URP draws shadows within it OF THE CAMERA and fades them out before the
            // edge, so past it an object on screen simply has none.
            //
            // **The argument for 15 was that halving it doubles the effective texel density, and that
            // argument is FALSE HERE.** Cascade resolution is a DIRECTIONAL-light property, and this
            // project has no directional light: it is deleted at build, `m_MainLightShadowsSupported`
            // is off and the cascade count is 1. Every shadow in the game comes from a spot, whose
            // map is sized by its cone angle and its resolution tier and does not know this number
            // exists. So the "quality gain" half of that trade was never being collected - 15 was
            // buying nothing and paying for it in pop-in.
            //
            // The other half of the old reasoning has also expired: it was written when **only Room1
            // cast**, so "30m reaches two rooms past anything worth shadowing" was true. Every room
            // casts now.
            //
            // What it still genuinely does is stop a whole corridor of lit rooms rendering shadow
            // maps at once, and that is the thing to watch if the frame rate moves.
            SetFloatIfPresent(so, "m_ShadowDistance", 30f);

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

            // **RENDERING LAYERS, ON FOR EXACTLY ONE THING: keeping the ending's sun out of the room
            // the player is standing in** (2026-09-03). `FacilityExterior.LightTheOutside` enables a
            // directional light with `shadows = None`, and a shadowless directional lights every
            // surface in the scene - walls and ceilings do not stop it. The `occupied` guard that
            // protects that room from the repaint and the un-bake cannot help, because those are per
            // renderer and this is global.
            //
            // Defaults are unchanged by switching this on: a renderer's mask and a light's mask are
            // both bit 0 out of the box, so everything keeps lighting everything until something
            // deliberately moves off that bit. `FacilityExterior` is the only thing that does.
            SetIfPresent(so, "m_SupportsLightLayers", true);

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

        private static void SetIfPresent(SerializedObject so, string path, float value)
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
            // **AMBIENT IS BACK, AND ZERO WAS TRIED AND FAILED** (2026-08-25/26).
            //
            // A sealed windowless room physically HAS no ambient light: there is no sky, and every
            // photon leaves a ceiling panel. So once the bake worked and the bounce count was raised
            // to eight, this was taken to zero on exactly that argument.
            //
            // **It produced a black building.** Measured off play screenshots: walls at 4-8 out of
            // 255 - not dark, BLACK - with the floor still lit at 67 and the emissive fixtures at 255.
            // The floor is the one surface the downlights reach directly; everything else in this
            // building lives on bounce, and the bake does not deliver enough of it to stand alone.
            // The ceiling is the extreme case, with no direct light at all.
            //
            // Raising `CeilingLightIntensity` to 17 did not help and could not: it took the floor from
            // 39% to 65.7% of its pixels CLIPPED while the walls stayed black, because 2.4 times
            // almost nothing is still almost nothing.
            //
            // **So the constant stays, and it is still a fudge - just an honest one now.** The bake
            // carries the floor's share (this is why the sky band is 0.028 and not 0.155), and this
            // carries the walls and ceiling that the bake cannot reach. Why it cannot is a real open
            // question - probe validity against 25mm panels, reflection probes capturing an unlit
            // room, or simply four downlights not making much wall bounce in a 8.75 x 10.5m room -
            // and it is worth answering, but not while the game is unusable. `TODO.md`.
            ApplyEnvironment();

            // The default Directional Light is deleted, not dimmed. These rooms are sealed boxes
            // with a ceiling slab over them - a sun has no way in, so it contributed exactly
            // nothing while still costing a shadow pass. (It was even less use once the corner
            // seams and the door pocket were closed.)
            foreach (Light light in Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude))
                if (light.type == LightType.Directional) Object.DestroyImmediate(light.gameObject);
        }

        // **THE PROBE VOLUME IS PART OF THE SCENE, SO THE BUILD OWNS IT** (2026-08-26).
        //
        // It used to be created by `BakeLighting` just before baking, and that quietly broke the
        // whole feature: `SceneBuilder.Build()` rebuilds a scene from nothing, so the next build
        // after a bake DELETED the volume and the `ProbeVolumePerSceneData` that Unity attaches
        // beside it. The baked cell data stayed on disk, tens of megabytes of it, with nothing in the
        // scene left to load it.
        //
        // **The workflow made it invisible.** Every run was build -> bake -> build, so the last step
        // always threw the result away. The bake logged success, the files were there, and the game
        // was lit entirely by the flat ambient constant - which is exactly what the measurements
        // showed and nobody could explain: `LightProbes.GetInterpolatedProbe` returned the SAME value
        // 30cm under the ceiling as 30cm above the floor (DC 0.3748 at every point in the room),
        // because it was reading the ambient probe rather than any baked field. A real irradiance
        // field cannot be constant in space.
        //
        // Sized off the renderers rather than written down, for the reason every size here is: the
        // cycles are different shapes, and a box written for one is wrong for the others.
        private const float ProbeVolumePadding = 4f;

        // Boxes everything in the scene. The core
        // scene is built with the entire building still in it - the cycles are moved out afterwards -
        // so measuring every renderer there gives a 48 x 52 x 193m box, **25.6 times the volume of a
        // cycle's**, for one 8.75 x 10.5m room. APV spreads a fixed cell budget over whatever box it
        // is given, so that room got a far coarser field than the identical rooms in the cycles, and
        // play saw it: Room1 read grey while the calibration room blew out to pure white, from one
        // global ambient value that cannot do both.
        private static void EnsureProbeVolume(Scene scene)
        {
            Bounds bounds = default;
            bool any = false;

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
                {
                    // Particle systems report wandering bounds; only the built world should decide
                    // how big the lit volume is.
                    if (r is ParticleSystemRenderer) continue;
                    if (!any) { bounds = r.bounds; any = true; }
                    else bounds.Encapsulate(r.bounds);
                }
            }

            if (!any) return;

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
            volume.size = bounds.size + Vector3.one * (ProbeVolumePadding * 2f);

            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log($"[SceneBuilder] '{scene.name}': probe volume {volume.size} at {bounds.center}.");
        }

        // **THE LIGHTING ENVIRONMENT, AND IT HAS TO BE APPLIED TO EVERY SCENE SEPARATELY.**
        //
        // `RenderSettings` is PER SCENE. `SetupLighting` writes it while the core scene is open, and
        // `SplitCyclesIntoScenes` then makes each cycle with `NewScene(EmptyScene)` - which arrives
        // with Unity's DEFAULTS and no way to inherit anything. Nothing copied them across, so from
        // the day the cycles were split until 2026-08-26 **every tuned lighting value in this project
        // existed in `IterationRoom.unity` alone**, and Cycle1/2/3 ran on skybox ambient at
        // (0.212, 0.227, 0.259) that nobody chose.
        //
        // **It hid for months because it looks like lighting, not like a bug**, and it invalidated
        // every attempt to tune the room:
        //
        // - `m_DefaultReflectionMode` defaulted to Skybox in those scenes, so walls at smoothness
        //   0.85 were mirroring the PROCEDURAL SKY - a constant no ceiling light can move. That is
        //   the anomaly that finally gave it away: the menu render's floor tracked fixture intensity
        //   exactly (140 / 173 / 211 for 7 / 10.5 / 17) while the wall beside it did not move at all
        //   (115 / 117 / 122).
        // - Everything measured came out BLUE - B greater than G greater than R in every single
        //   sample, in a white room lit by white panels. That was the sky, and it was on screen the
        //   whole time.
        // - Setting ambient to zero blacked out the calibration room (which lives in the core scene
        //   and does get these values) while the cycle rooms carried on, which made the result look
        //   inconsistent and sent three separate diagnoses in the wrong direction.
        //
        // **Anything else added to the environment must go in HERE, not in `SetupLighting`.**
        private static void ApplyEnvironment()
        {
            // **THE SKY IS SET HERE, IN EVERY SCENE, AND IT IS SET AT BUILD TIME ON PURPOSE.**
            //
            // Nothing in this game has ever looked at the sky - every room is sealed - so the field
            // was left at Unity's default, which is the blue procedural one with a sun in it. That
            // stopped being invisible the moment the ending opened a shaft with a hole in the top:
            // play's report was that the map "still shows what you get when no background is set",
            // which is exactly what it was.
            //
            // The ending sets this at runtime too, in `FacilityExterior.LightTheOutside`. This is the
            // belt to that pair of braces: a runtime assignment that does not run leaves the default
            // showing, and there is no version of this game where the default is the right answer.
            // Ambient is Trilight below, so the skybox feeds nothing and this cannot move a single
            // tuned lighting value.
            RenderSettings.skybox = EndingSkybox();

            RenderSettings.ambientMode = AmbientMode.Trilight;

            // **AMBIENT IS A FUDGE THIS BUILDING CANNOT DO WITHOUT, AND IT WAS TESTED TWICE.**
            //
            // A sealed windowless room physically has no ambient light: no sky, and every photon
            // leaves a ceiling panel. Zero was tried on that argument, gave a black building, and was
            // tried AGAIN after `ApplyEnvironment` fixed the three cycle scenes that had been running
            // on skybox ambient - because the first attempt had measured an inconsistency rather than
            // the idea. On the corrected baseline it still gives black walls and a black ceiling.
            //
            // **The measurement that settles it**: with ambient at zero the walls read 13 and the
            // ceiling 9 out of 255, against 149 and 169 with it on. Direct light from the fixtures
            // barely reaches a wall at all - the spots point DOWN, so the floor takes 100 and the
            // vertical surfaces get the tail of the cone - and eight bounces of that tail is not
            // enough to light a room. This constant is doing almost all of the wall and ceiling
            // lighting, and it has been all along.
            //
            // Raising the fixtures does not substitute for it either: 10.5 -> 17 took the floor from
            // 39% to 65.7% of its pixels CLIPPED while the walls stayed dark. The light is not
            // missing, it is pointing somewhere else.
            //
            // **If this is ever to go, the fixtures have to stop being downlights** - a source that
            // actually faces the walls, or an emissive panel that contributes to the bake (these are
            // `RealtimeEmissive`, so their glow lights nothing). `TODO.md`.
            // **HALVED AGAIN 2026-08-26, THE MOMENT THE BAKE STARTED ACTUALLY APPLYING.**
            //
            // Until the probe volume survived a rebuild (see `EnsureProbeVolume`) the baked bounce
            // reached nothing, and these numbers were tuned to carry the whole room by themselves.
            // Now the bounce arrives as well and the two are ADDED, which showed up first on the
            // things that take all their light from probes: play reported the bed as blown out, and
            // it measured 255,255,255 across its whole frame - 100% clipped - with the sheet at 92%
            // while the walls beside it sat at 100 and were called too dark.
            //
            // Static surfaces and dynamic objects were being lit by different amounts of the same
            // double count, so no single brightness fixed both. Taking ambient down is what removes
            // the duplication rather than trading one complaint for the other.
            //
            // Was 0.028 / 0.659 / 0.843, itself already halved from the pre-bake 0.155 / 0.644 /
            // 0.719. **If the room now reads flat rather than dim, this is still too high** - the
            // whole point is for the bake to be what lights the walls.
            // **FOUND ON THE DIAL, 2026-08-26, IN TWO PASSES - AND THE SECOND PASS IS THE IMPORTANT
            // ONE.** The first got the room properly white (0.036 / 0.836 / 0.729) and play's verdict
            // was that it HURT TO LOOK AT. These are the numbers that came back from making it
            // comfortable instead of maximally white.
            //
            // The shape of that second correction: the FLOOR band came up tenfold (0.036 -> 0.356)
            // while the ceiling came down (0.729 -> 0.521) and the walls eased slightly. The three
            // bands are now much closer together, which is the whole point - the eye reads a room by
            // the RATIOS between its surfaces, and pushing every one of them toward white removes the
            // ratios and leaves a glare.
            //
            // **The lesson for anyone retuning this: brightness is not the target, comfort is.** Two
            // days went into trying to replace this constant with something physically derived - baked
            // bounce, then wall washers - and both failed on the same point. They add brightness where
            // light already is; what this room wants is EVENNESS, at a level that can be looked at.
            // That is an eye's judgement and it was only ever going to be found by looking.
            // **B WAS A HAIR ABOVE R IN ALL THREE BANDS (0.356/0.356/0.361 etc) UNTIL 2026-09-03,
            // AND IT WAS INVISIBLE UNTIL THE FLOOR ACTUALLY REFLECTED.** A diffuse wall is lit
            // mostly by its own white albedo plus direct light, so a ~1-2% blue bias in the ambient
            // term was lost in that sum. The floor is different now that `ProbeBoxMargin` puts it
            // properly inside its room's reflection probe (see `docs/gotchas.md`): a probe bake
            // captures the room fully lit, ambient included, so this same small bias came out on
            // EVERY face of the baked cubemap (checked directly, not eyeballed - all six faces read
            // B > G > R by 2-5%) and showed up as a visibly blue floor next to the neutral flat
            // reflection the title screen substitutes in. R and G are untouched, so the room's
            // overall level and the comfort tuning above are unchanged - only the tiny cast is gone.
            RenderSettings.ambientSkyColor     = new Color(0.356f, 0.356f, 0.356f);
            RenderSettings.ambientEquatorColor = new Color(0.763f, 0.763f, 0.763f);
            RenderSettings.ambientGroundColor  = new Color(0.521f, 0.521f, 0.521f);
            RenderSettings.ambientIntensity = 1f;
            // Assigning the colours does NOT rebuild the ambient probe. Without this they are
            // stored and never reach a shader, and every tweak looks like it did nothing.
            DynamicGI.UpdateEnvironment();

            // Reflections come from per-room probes that see the actual white room. **This being
            // left at Unity's default in the cycle scenes is what painted the sky onto the walls**,
            // so it is set here rather than assumed.
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            RenderSettings.reflectionIntensity = 1f;
        }

        // Recessed ceiling fixtures - the actual light in the room.
        //
        // Spots rather than points, aimed straight down, because a downlight's cone is what gives
        // the floor bright pools and lets the walls fall off toward the corners. That gradient is
        // most of what separates a room from a whitebox; a point light just washes everything.
        //
        // castShadows is TRUE for every room now, and the flag is kept rather than removed: it is the
        // per-room switch for the one thing here that costs real frames, so a room that turns out to
        // be too heavy can opt out without touching the mechanism. It was off everywhere but Room1
        // until 2026-08-24 - see the shadow block below for what that did to the player's own shadow.
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
                    light.spotAngle = CeilingSpotAngle;
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
                    light.intensity = CeilingLightIntensity;
                    // Barely off white - clinical rather than domestic, without tinting the room.
                    light.color = new Color(0.99f, 0.99f, 1f);
                    // **ALL FOUR CAST** (2026-08-24, after play called the player's shadow detached -
                    // "not something coming off my body"). It was ONE per room before, and that is
                    // what the complaint was: one caster is one shadow pointing one way, and this
                    // grid's first fixture is a CORNER of it, so a player at the room's centre was
                    // lit for shadow purposes from 3.13m to one side and threw a 1.8m body into a
                    // shadow whose head landed a metre away - while the room LOOKS evenly lit from
                    // four panels overhead. A shadow at that angle reads as a separate object lying
                    // on the floor beside you.
                    //
                    // **THE CUE THAT WAS MISSING IS A CONTACT SHADOW, AND ONLY SEVERAL LIGHTS MAKE
                    // ONE.** With four, each fixture carries about a quarter of the light: directly
                    // under the feet all four are blocked and it goes properly dark, while each
                    // single radiating shadow loses only its own quarter and stays faint. Dark at the
                    // feet fading outward is what the eye reads as "this is standing here" - a
                    // better-aimed single shadow cannot produce it at all.
                    //
                    // The old reasoning here was that four overlapping shadows "read as a smear, not
                    // as a figure". That is true of four EQUALLY dark ones; the gradient is the point.
                    //
                    // **0.45, DOWN FROM 0.75** (2026-08-25, after play: four scattered shadows under a
                    // held object read as wrong). They are in the RIGHT PLACE - a point light at
                    // (+-1.75, 5.41, +-2.6) throws an object held at 1.3m onto the floor 0.99m from
                    // its own footprint, so the four sit on a 1.11 x 1.65m rectangle, which is exactly
                    // what the screen shows. What is wrong is the EDGE: these stand in for 1.4m
                    // emissive panels, and a real source that size would blur each shadow over 0.44m
                    // at that height - half their separation - so the four would melt into one soft
                    // pool. A point light gives four crisp copies instead, and URP has no realtime
                    // area light to fix that with.
                    //
                    // So the lever is CONTRAST, not geometry. Each fixture carries about a quarter of
                    // the light, so one shadow alone removes strength/4: at 0.75 that was 19% and read
                    // as a distinct copy, at 0.45 it is 11% and reads as a smudge. Under the object
                    // all four still stack to 45%, which is the contact cue this is really for. The other half of the old argument -
                    // that four casters a room is a budget nobody has - was written for the WebGL
                    // build. The target is Steam, and six shadowed fixtures is a configuration this
                    // project has already shipped (see the atlas note in `ConfigureUrpAsset`).
                    //
                    // Cost is bounded by CULLING, not by room count: a light outside the frustum is
                    // not in `visibleLights` and costs nothing, so this is four casters in the room
                    // you are standing in and up to eight seen through an open door - never fourteen
                    // rooms' worth.
                    // **MIXED, SO THE BOUNCE CAN BE BAKED WITHOUT GIVING UP THE DIRECT LIGHT**
                    // (2026-08-25). Left at the default Realtime these contribute NOTHING to a GI
                    // bake - the bake log said so in as many words, "0 lights" - so a building lit
                    // entirely by them bakes pitch black indirect and the whole exercise is wasted.
                    //
                    // Mixed rather than Baked because the direct half has to stay live: `ShadowBudget`
                    // switches these fixtures' shadows on and off as the player moves, and a fully
                    // baked light has no runtime shadow to switch. Mixed keeps the direct light and
                    // its shadow realtime and bakes only what bounces, which is exactly the split
                    // this room wants.
                    light.lightmapBakeType = LightmapBakeType.Mixed;
                    light.shadows = castShadows ? LightShadows.Soft : LightShadows.None;
                    light.shadowStrength = 0.45f;
                    light.renderMode = LightRenderMode.ForcePixel;
                    built.Add(light);
                }
            }

            return (built.ToArray(), panels.ToArray());
        }
    }
}
