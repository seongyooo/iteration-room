using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace IterationRoom
{
    // A temporary in-game panel for finding the room's brightness by eye, in play mode, instead of
    // by rebuilding the scene between guesses.
    //
    // THIS IS SCAFFOLDING. Every value it touches is hardcoded in SceneBuilder, which is the source
    // of truth - this only moves the live scene. When the numbers are settled, press Copy, paste
    // them into SceneBuilder.SetupLighting / BuildCeilingLights, rebuild, and delete this script
    // along with the line in SceneBuilder that adds it.
    //
    // Deliberately IMGUI rather than uGUI: a dev overlay that gets deleted next week should not add
    // a canvas, five slider prefabs and an EventSystem dependency to the built scene. OnGUI draws
    // over everything and costs one component.
    public class LightingTuner : MonoBehaviour
    {
        public KeyCode toggleKey = KeyCode.F1;

        // The values SceneBuilder builds with. Reset puts these back, and Copy diffs against them
        // so it is obvious what actually moved.
        public const float DefaultIntensity = 15f;
        public const float DefaultSky = 0.22f;
        public const float DefaultEquator = 0.40f;
        public const float DefaultGround = 0.88f;
        public const float DefaultExposure = 0f;

        // The blue lift the three ambient colours carry (0.22, 0.22, 0.24). Kept as an offset so a
        // slider stays one number - dragging three channels to tune brightness is not the job.
        private const float AmbientBlueLift = 0.02f;

        private float intensity = DefaultIntensity;
        private float sky = DefaultSky;
        private float equator = DefaultEquator;
        private float ground = DefaultGround;
        private float exposure = DefaultExposure;

        private bool open;
        private Light[] fixtures;
        private ColorAdjustments colorAdjustments;
        private FirstPersonController player;
        private string status = "";

        private void Start()
        {
            // Only the ceiling fixtures. Anything else that turns up in the scene later (a lamp on
            // the nightstand, say) is not what this slider means.
            var found = new System.Collections.Generic.List<Light>();
            foreach (Light light in FindObjectsByType<Light>(FindObjectsInactive.Exclude))
                if (light.type == LightType.Spot) found.Add(light);
            fixtures = found.ToArray();

            player = FindAnyObjectByType<FirstPersonController>();

            Volume volume = FindAnyObjectByType<Volume>();
            if (volume != null)
            {
                // `.profile`, NOT `.sharedProfile`: the getter hands back a runtime clone, so
                // dragging the exposure slider in play mode cannot dirty IterationVolume.asset on
                // disk. Tuning must not quietly edit the project.
                if (volume.profile.TryGet(out ColorAdjustments adjustments))
                {
                    colorAdjustments = adjustments;
                    colorAdjustments.postExposure.overrideState = true;
                    exposure = colorAdjustments.postExposure.value;
                }
            }
        }

        private void Update()
        {
            if (Input.GetKeyDown(toggleKey))
            {
                open = !open;
                if (!open) ReleaseCursor();
            }

            // Re-asserted every frame rather than once on open. The loop hands control back at the
            // end of each wake-up regardless of what this panel wants, so a one-shot would let the
            // view start swinging around under the mouse mid-drag at the top of an iteration.
            if (open)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                if (player != null) player.ControlEnabled = false;
            }
        }

        private void ReleaseCursor()
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            // Only if the clock is actually running. During the wake-up the sequence owns the
            // camera, and handing control back here would let the player walk out of the animation.
            if (player != null && LoopManager.Instance != null && LoopManager.Instance.IterationRunning)
                player.ControlEnabled = true;
        }

        private void OnGUI()
        {
            if (!open)
            {
                GUI.Label(new Rect(10f, 10f, 300f, 20f), $"[{toggleKey}] lighting");
                return;
            }

            // The panel is laid out in 1080p units and scaled to the window, so it stays readable
            // on a 4K display instead of shrinking to a stamp.
            float scale = Mathf.Max(1f, Screen.height / 1080f);
            Matrix4x4 previous = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one * scale);

            const float w = 340f;
            GUILayout.BeginArea(new Rect(16f, 16f, w, 340f), GUI.skin.box);
            GUILayout.Label($"LIGHTING  —  [{toggleKey}] to close");
            GUILayout.Space(4f);

            intensity = Row("Fixture intensity", intensity, 0f, 30f, "F1");
            GUILayout.Space(2f);
            GUILayout.Label("Ambient (stands in for bounce):");
            ground = Row("  ground → ceiling", ground, 0f, 1.5f, "F2");
            equator = Row("  equator → walls", equator, 0f, 1.5f, "F2");
            sky = Row("  sky → floor", sky, 0f, 1.5f, "F2");
            GUILayout.Space(2f);
            exposure = Row("Post exposure (EV)", exposure, -3f, 1.5f, "F2");

            GUILayout.Space(6f);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Copy C# to clipboard")) Copy();
            if (GUILayout.Button("Reset", GUILayout.Width(70f))) Reset();
            GUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(status)) GUILayout.Label(status);

            GUILayout.EndArea();
            GUI.matrix = previous;

            Apply();
        }

        private static float Row(string label, float value, float min, float max, string format)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{label}  {value.ToString(format)}", GUILayout.Width(190f));
            float result = GUILayout.HorizontalSlider(value, min, max);
            GUILayout.EndHorizontal();
            return result;
        }

        private void Apply()
        {
            if (fixtures != null)
                foreach (Light light in fixtures)
                    if (light != null) light.intensity = intensity;

            Color skyColor = Ambient(sky);
            Color equatorColor = Ambient(equator);
            Color groundColor = Ambient(ground);

            // Guarded because UpdateEnvironment rebuilds the ambient probe, and running that every
            // frame for no change is waste - OnGUI ticks several times a frame.
            bool changed = RenderSettings.ambientSkyColor != skyColor
                        || RenderSettings.ambientEquatorColor != equatorColor
                        || RenderSettings.ambientGroundColor != groundColor;

            if (changed)
            {
                RenderSettings.ambientSkyColor = skyColor;
                RenderSettings.ambientEquatorColor = equatorColor;
                RenderSettings.ambientGroundColor = groundColor;
                // Assigning the colours does not rebuild the probe. Without this every drag looks
                // like it did nothing - the same trap SceneBuilder.SetupLighting documents.
                DynamicGI.UpdateEnvironment();
            }

            if (colorAdjustments != null) colorAdjustments.postExposure.value = exposure;
        }

        private static Color Ambient(float level) => new Color(level, level, level + AmbientBlueLift);

        private void Reset()
        {
            intensity = DefaultIntensity;
            sky = DefaultSky;
            equator = DefaultEquator;
            ground = DefaultGround;
            exposure = DefaultExposure;
            status = "reset to built values";
        }

        // Emits the exact lines to paste back into SceneBuilder, because the point of this panel is
        // to end with a number in the source file. Reading values off a slider by eye and retyping
        // them is how a tuning session gets lost.
        private void Copy()
        {
            var sb = new StringBuilder();
            sb.AppendLine("// SceneBuilder.BuildCeilingLights:");
            sb.AppendLine($"light.intensity = {intensity.ToString("0.##")}f;");
            sb.AppendLine("// SceneBuilder.SetupLighting:");
            sb.AppendLine($"RenderSettings.ambientSkyColor     = new Color({sky:0.###}f, {sky:0.###}f, {sky + AmbientBlueLift:0.###}f);");
            sb.AppendLine($"RenderSettings.ambientEquatorColor = new Color({equator:0.###}f, {equator:0.###}f, {equator + AmbientBlueLift:0.###}f);");
            sb.AppendLine($"RenderSettings.ambientGroundColor  = new Color({ground:0.###}f, {ground:0.###}f, {ground + AmbientBlueLift:0.###}f);");

            if (Mathf.Abs(exposure - DefaultExposure) > 0.001f)
            {
                sb.AppendLine("// SceneBuilder.BuildPostProcessing - this override does not exist yet:");
                sb.AppendLine("color.postExposure.overrideState = true;");
                sb.AppendLine($"color.postExposure.value = {exposure.ToString("0.##")}f;");
            }

            GUIUtility.systemCopyBuffer = sb.ToString();
            // Logged as well as copied: the clipboard is easy to lose to the next thing you copy,
            // and the Console keeps it.
            Debug.Log("[LightingTuner] values\n" + sb);
            status = "copied — paste into SceneBuilder, rebuild, delete this panel";
        }
    }
}
