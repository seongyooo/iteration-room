using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace IterationRoom.Dev
{
    // A LIVE DIAL FOR THE NUMBERS THAT CAN ONLY BE FOUND BY LOOKING.
    //
    // A rebuild-look-guess cycle is four minutes a step, and a bake is four minutes more. This panel
    // exists so the ambient bands, the fixture intensity, the shadow biases and the six surface
    // numbers can be moved with the room in front of you, and typed into `SceneBuilder` once.
    //
    // **THIS IS THE SECOND ONE. The first found the wall/floor/ceiling numbers in 2026-08-10 and was
    // deleted once they settled** - a development panel on the first screen every player sees is not
    // something to ship. It is rebuilt here because the bake landing (2026-08-25) invalidated the
    // balance those numbers sat on. `docs/rendering-notes.md` records both the technique and the four
    // rules below, which are what made the first one work.
    //
    // **WHAT THIS PANEL LIES ABOUT, AND IT MATTERS.** The ceiling fixtures are **Mixed**: their
    // direct light is realtime, their BOUNCE is baked. So every slider here that feeds the bake -
    // ambient, intensity, albedo - shows you its direct half instantly and its indirect half frozen
    // at whatever the last bake computed. What you are looking at is a hybrid no build reproduces.
    // Use it to get to the right neighbourhood, then **type the numbers in, rebuild, RE-BAKE, and
    // look again**. The one group that is completely honest is the shadow biases, which have no
    // baked component at all.
    //
    // The rules, all four of them load-bearing:
    //
    // 1. **Material writes go through a `MaterialPropertyBlock`**, which is per-renderer. The
    //    calibration room's walls are the same `PanelWhite` asset as every wall in the building, so
    //    writing the material directly would repaint the entire game and dirty the asset on disk.
    // 2. **Nothing is saved.** Numbers are read off the panel and typed into `SceneBuilder`, which
    //    owns them (CLAUDE.md §2). A tuner that persisted would be a second source of truth for a
    //    value the builder already answers.
    // 3. **Controls are seeded from the LIVE SCENE, never from constants.** The first tuner seeded
    //    from hardcoded defaults, so opening it silently overwrote the room's real lighting - which
    //    reads exactly like "my change never went in".
    // 4. **`ControlEnabled` is held false every frame** while the panel is up, and the cursor is
    //    released. That room keeps the pointer captured because capturing it is what the sensitivity
    //    step measures, and a slider needs it loose.
    public class LightingTuner : MonoBehaviour
    {
        [Tooltip("The calibration room's ceiling fixtures. Intensity and cone are driven on these " +
                 "only - this panel never touches another room's lights.")]
        public Light[] fixtures;

        [Tooltip("Calibration-room renderers, split by surface. Driven through property blocks, so " +
                 "the shared materials every other room uses are untouched.")]
        public Renderer[] wallPanels;
        public Renderer[] floors;
        public Renderer[] ceilings;

        public FirstPersonController controller;

        // **A DEVELOPMENT PANEL ON THE FIRST SCREEN EVERY PLAYER SEES IS WHY THE LAST ONE WAS
        // DELETED.** This one is disarmed in a release player instead, which is `CaptureRig`'s
        // bargain and for the same reason: the technique stays available without ever shipping.
        // Disarmed means TAB does nothing and `OnGUI` never draws - the component is inert, not
        // absent, because the scene is built once and serves both.
        private bool armed;

        private bool open;
        private bool seeded;
        private Vector2 scroll;

        private void Awake() => armed = Application.isEditor || Debug.isDebugBuild;

        // Ambient: the slider drives the r/g level and the blue keeps the tint it was authored with,
        // so what comes out is the same shape of number `SetupLighting` writes.
        private float ambSky, ambEquator, ambGround;
        private float ambSkyBlue, ambEquatorBlue, ambGroundBlue;

        private float intensity, spotAngle;
        private float depthBias, normalBias;
        private float depthBiasSeed, normalBiasSeed;
        private float shadowStrength;

        private SurfaceDials wall, floor, ceiling;

        private MaterialPropertyBlock block;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");
        private static readonly int BumpScaleId = Shader.PropertyToID("_BumpScale");

        // One surface's three numbers. `albedo` is the grey level written into `_BaseColor`.
        private struct SurfaceDials
        {
            public float albedo, smoothness, bump;
        }

        private void Update()
        {
            if (!armed) return;

            if (Input.GetKeyDown(KeyCode.Tab))
            {
                open = !open;
                if (open) Seed();
                else Restore();
            }

            // EVERY FRAME, not once on toggle - `FirstPersonController` takes the lock back the
            // moment it sees a click, so a one-shot release is undone by the first slider drag.
            if (!open) return;
            if (controller != null) controller.ControlEnabled = false;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        // Rule 3: read the world, do not assume it.
        private void Seed()
        {
            if (controller == null) controller = FindAnyObjectByType<FirstPersonController>();

            Color sky = RenderSettings.ambientSkyColor;
            Color eq = RenderSettings.ambientEquatorColor;
            Color gr = RenderSettings.ambientGroundColor;
            ambSky = sky.r; ambSkyBlue = sky.b - sky.r;
            ambEquator = eq.r; ambEquatorBlue = eq.b - eq.r;
            ambGround = gr.r; ambGroundBlue = gr.b - gr.r;

            if (fixtures != null && fixtures.Length > 0 && fixtures[0] != null)
            {
                intensity = fixtures[0].intensity;
                spotAngle = fixtures[0].spotAngle;
                shadowStrength = fixtures[0].shadowStrength;
            }

            if (GraphicsSettings.defaultRenderPipeline is UniversalRenderPipelineAsset urp)
            {
                depthBias = depthBiasSeed = urp.shadowDepthBias;
                normalBias = normalBiasSeed = urp.shadowNormalBias;
            }

            wall = SeedSurface(wallPanels);
            floor = SeedSurface(floors);
            ceiling = SeedSurface(ceilings);
            seeded = true;

            // **SAY SO WHEN THE WIRING IS GONE, because the failure is otherwise INVISIBLE.** Every
            // group here falls back to a plausible default when its array is empty, so a panel with
            // nothing attached does not look broken - it looks like a room whose walls happen to be
            // albedo 1, smoothness 0.5, bump 1. Numbers were read off it in that state once
            // (2026-08-25) and typed up as though they meant something.
            //
            // The way it happens is a scene boundary: this component holds references to objects in
            // the calibration room, and if it is ever built into a cycle's scene instead of beside
            // the room, Unity nulls every one of them on save. `cross-scene-report.txt`, written by
            // every build, lists them - but only if somebody reads it, so this shouts as well.
            if (Empty(fixtures) || Empty(wallPanels) || Empty(floors) || Empty(ceilings))
                Debug.LogWarning("[LightingTuner] Wiring is missing - "
                    + $"{Count(fixtures)} fixture(s), {Count(wallPanels)} wall, {Count(floors)} floor, "
                    + $"{Count(ceilings)} ceiling. Those sliders are driving NOTHING and the numbers "
                    + "they show are fallback defaults, not this room's values. Check "
                    + "cross-scene-report.txt: this panel must live in the same scene as the room.");
        }

        private static bool Empty<T>(T[] a) where T : Object
        {
            if (a == null || a.Length == 0) return true;
            foreach (T t in a) if (t != null) return false;
            return true;
        }

        private static int Count<T>(T[] a) where T : Object
        {
            if (a == null) return 0;
            int n = 0;
            foreach (T t in a) if (t != null) n++;
            return n;
        }

        private SurfaceDials SeedSurface(Renderer[] group)
        {
            var dials = new SurfaceDials { albedo = 1f, smoothness = 0.5f, bump = 1f };
            if (group == null || group.Length == 0 || group[0] == null) return dials;

            Material m = group[0].sharedMaterial;
            if (m == null) return dials;

            if (m.HasProperty(BaseColorId)) dials.albedo = m.GetColor(BaseColorId).r;
            if (m.HasProperty(SmoothnessId)) dials.smoothness = m.GetFloat(SmoothnessId);
            if (m.HasProperty(BumpScaleId)) dials.bump = m.GetFloat(BumpScaleId);
            return dials;
        }

        // Rule 6's half of the bargain: the URP asset is a PROJECT asset and the only thing here that
        // is not per-instance, so what this panel changed it puts back. A rebuild would restore it
        // anyway (`ConfigureUrpAsset` authors both every time), but relying on that would mean the
        // Editor sat on a dirty asset until somebody happened to build.
        private void Restore()
        {
            if (controller != null) controller.ControlEnabled = true;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;

            if (GraphicsSettings.defaultRenderPipeline is UniversalRenderPipelineAsset urp)
            {
                urp.shadowDepthBias = depthBiasSeed;
                urp.shadowNormalBias = normalBiasSeed;
            }
        }

        private void OnDisable()
        {
            if (open) Restore();
            open = false;
        }

        private void OnGUI()
        {
            if (!armed || !open || !seeded) return;

            const float w = 430f;
            GUILayout.BeginArea(new Rect(12f, 12f, w, Screen.height - 24f), GUI.skin.box);
            scroll = GUILayout.BeginScrollView(scroll);

            GUILayout.Label("LIGHTING TUNER  ·  TAB to close");
            GUILayout.Label("Bounce is BAKED and stale while you drag. Type the numbers into "
                          + "SceneBuilder, rebuild, then RE-BAKE before believing any of it. "
                          + "Only the shadow biases are honest live.");

            GUILayout.Space(6f);
            GUILayout.Label("— AMBIENT (global; SetupLighting) —");
            ambSky = Row("sky → floor", ambSky, 0f, 1f);
            ambEquator = Row("equator → walls", ambEquator, 0f, 1f);
            ambGround = Row("ground → ceiling", ambGround, 0f, 1f);
            ApplyAmbient();

            GUILayout.Space(6f);
            GUILayout.Label("— FIXTURES (this room only; BuildCeilingLights) —");
            intensity = Row("intensity", intensity, 0f, 25f);
            spotAngle = Row("spotAngle", spotAngle, 20f, 179f);
            ApplyFixtures();

            GUILayout.Space(6f);
            GUILayout.Label("— SHADOWS (global, URP asset; honest live) —");
            depthBias = Row("depth bias", depthBias, 0f, 3f);
            normalBias = Row("normal bias", normalBias, 0f, 3f);
            // **THE LEVER FOR "FOUR SHADOWS UNDER ONE OBJECT".** Four fixtures put four shadows on the
            // floor in geometrically correct places, and no bias or resolution changes that. What
            // changes is how much each ONE removes: a quarter of the light times this. Low enough and
            // a single shadow is a smudge while the four still stack to a dark contact point.
            shadowStrength = Row("shadow strength", shadowStrength, 0f, 1f);
            ApplyShadows();

            GUILayout.Space(6f);
            GUILayout.Label("— SURFACES (this room only; property blocks) —");
            wall = SurfaceRows("wall", wall, wallPanels);
            floor = SurfaceRows("floor", floor, floors);
            ceiling = SurfaceRows("ceiling", ceiling, ceilings);

            GUILayout.Space(8f);
            GUILayout.Label("— PASTE INTO SceneBuilder —");
            GUILayout.TextArea(Readout());

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private static float Row(string label, float value, float min, float max)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(130f));
            value = GUILayout.HorizontalSlider(value, min, max, GUILayout.Width(190f));
            GUILayout.Label(value.ToString("0.###"), GUILayout.Width(60f));
            GUILayout.EndHorizontal();
            return value;
        }

        private SurfaceDials SurfaceRows(string name, SurfaceDials d, Renderer[] group)
        {
            GUILayout.Label($"  {name}");
            d.albedo = Row("  albedo", d.albedo, 0f, 1f);
            d.smoothness = Row("  smoothness", d.smoothness, 0f, 1f);
            // 0..3 rather than 0..1: central differences over a smooth noise field give tiny
            // gradients, so this map is genuinely shallow and the useful range runs past 1.
            d.bump = Row("  bumpScale", d.bump, 0f, 3f);
            ApplySurface(d, group);
            return d;
        }

        private void ApplyAmbient()
        {
            RenderSettings.ambientSkyColor = Grey(ambSky, ambSkyBlue);
            RenderSettings.ambientEquatorColor = Grey(ambEquator, ambEquatorBlue);
            RenderSettings.ambientGroundColor = Grey(ambGround, ambGroundBlue);
            // Rule 5: assigning the colours does NOT rebuild the ambient probe. Without this they
            // are stored and never reach a shader, and every drag looks like it did nothing. This
            // cost a full tuning pass once already.
            DynamicGI.UpdateEnvironment();
        }

        private static Color Grey(float level, float blueOffset) =>
            new Color(level, level, level + blueOffset);

        private void ApplyFixtures()
        {
            if (fixtures == null) return;
            foreach (Light l in fixtures)
            {
                if (l == null) continue;
                l.intensity = intensity;
                l.spotAngle = spotAngle;
                l.shadowStrength = shadowStrength;
            }
        }

        private void ApplyShadows()
        {
            if (GraphicsSettings.defaultRenderPipeline is UniversalRenderPipelineAsset urp)
            {
                urp.shadowDepthBias = depthBias;
                urp.shadowNormalBias = normalBias;
            }
        }

        // Rule 1: per-renderer, so the shared asset - and therefore every other room - is untouched.
        //
        // `_BumpScale` is drivable from a block ONLY because `_NORMALMAP` is already compiled into
        // these materials by `ApplySurfaceDetail`. A property block cannot turn a shader keyword on.
        private void ApplySurface(SurfaceDials d, Renderer[] group)
        {
            if (group == null) return;
            block ??= new MaterialPropertyBlock();

            foreach (Renderer r in group)
            {
                if (r == null) continue;
                r.GetPropertyBlock(block);
                block.SetColor(BaseColorId, new Color(d.albedo, d.albedo, d.albedo, 1f));
                block.SetFloat(SmoothnessId, d.smoothness);
                block.SetFloat(BumpScaleId, d.bump);
                r.SetPropertyBlock(block);
            }
        }

        private string Readout() =>
$@"// SetupLighting
ambientSkyColor     = new Color({ambSky:0.###}f, {ambSky:0.###}f, {ambSky + ambSkyBlue:0.###}f);
ambientEquatorColor = new Color({ambEquator:0.###}f, {ambEquator:0.###}f, {ambEquator + ambEquatorBlue:0.###}f);
ambientGroundColor  = new Color({ambGround:0.###}f, {ambGround:0.###}f, {ambGround + ambGroundBlue:0.###}f);
// BuildCeilingLights   intensity {intensity:0.###}   spotAngle {spotAngle:0.###}   shadowStrength {shadowStrength:0.###}
// ConfigureUrpAsset    m_ShadowDepthBias {depthBias:0.###}   m_ShadowNormalBias {normalBias:0.###}
// wall     albedo {wall.albedo:0.###}  smoothness {wall.smoothness:0.###}  bump {wall.bump:0.###}
// floor    albedo {floor.albedo:0.###}  smoothness {floor.smoothness:0.###}  bump {floor.bump:0.###}
// ceiling  albedo {ceiling.albedo:0.###}  smoothness {ceiling.smoothness:0.###}  bump {ceiling.bump:0.###}";
    }
}
