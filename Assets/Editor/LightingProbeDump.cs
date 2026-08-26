using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace IterationRoom.EditorTools
{
    // A READ-ONLY DUMP OF THE LIGHTING STATE A SCENE ACTUALLY HAS. It changes nothing; it prints
    // what the guesses are about. Run it from the command line:
    //
    //   Unity.exe -batchmode -projectPath . -executeMethod
    //             IterationRoom.EditorTools.LightingProbeDump.Run -quit -logFile dump.log
    //
    // **KEPT BECAUSE IT FOUND THE WORST BUG OF THE 2026-08 LIGHTING WORK IN ONE RUN.** Three rounds
    // of reasoning about dark walls had already failed when this printed `ambientMode: Skybox` and
    // an ambient colour nobody had ever written - which is how it came out that `RenderSettings` is
    // per scene and three of the four scenes had never received the project's own lighting.
    //
    // **What it can and cannot see.** The material, static-flag and renderer state is exact. The
    // probe sample at the bottom reads whatever probe system is live IN BATCH MODE, which is not
    // necessarily what a player sees - during the APV work it returned the ambient probe (the same
    // value at every point in the room) rather than baked data. Treat a constant field as "this tool
    // is not seeing the bake", not as "the bake is empty".
    public static class LightingProbeDump
    {
        public static void Run()
        {
            var sb = new StringBuilder();
            string path = "Assets/Scenes/Cycle1.unity";
            EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            Scene scene = SceneManager.GetActiveScene();
            sb.AppendLine($"=== scene: {scene.name} ===");

            sb.AppendLine($"ambientMode      : {RenderSettings.ambientMode}");
            sb.AppendLine($"ambient sky/eq/gr: {RenderSettings.ambientSkyColor.r:0.###} / "
                        + $"{RenderSettings.ambientEquatorColor.r:0.###} / "
                        + $"{RenderSettings.ambientGroundColor.r:0.###}");
            sb.AppendLine($"ambientIntensity : {RenderSettings.ambientIntensity}");
            sb.AppendLine($"defaultReflection: {RenderSettings.defaultReflectionMode}");
            sb.AppendLine($"reflectionInt.   : {RenderSettings.reflectionIntensity}");

            var urp = GraphicsSettings.defaultRenderPipeline;
            if (urp != null)
            {
                var so = new SerializedObject(urp);
                foreach (string p in new[] { "m_LightProbeSystem", "m_MixedLightingSupported",
                                             "m_AdditionalLightsRenderingMode",
                                             "m_AdditionalLightsPerObjectLimit",
                                             "m_ProbeVolumeSHBands" })
                {
                    SerializedProperty sp = so.FindProperty(p);
                    sb.AppendLine($"URP {p,-38}: {(sp == null ? "MISSING" : sp.intValue.ToString())}");
                }
            }

            // A wall panel, the floor and a ceiling slab - the three surfaces the argument is about.
            Report(sb, scene, "PANEL  ", r => r.transform.parent != null
                                           && r.transform.parent.name.EndsWith("_Panels"));
            Report(sb, scene, "FLOOR  ", r => r.sharedMaterial != null
                                           && r.sharedMaterial.name == "FloorWhite");
            Report(sb, scene, "CEILING", r => r.sharedMaterial != null
                                           && r.sharedMaterial.name == "CeilingWhite");

            Report(sb, scene, "FIXTURE", r => r.sharedMaterial != null
                                           && r.sharedMaterial.name == "CeilingFixture");

            // **WHAT THE BAKE ACTUALLY PUT IN THE AIR.** Everything above is configuration; this is
            // the result. `LightProbes.GetInterpolatedProbe` reads whatever probe system is live at
            // the point given, so it answers the only question that matters: is there any baked
            // indirect light where the walls are?
            foreach (var (label, pos) in new (string, Vector3)[]
            {
                ("room centre, chest high", new Vector3(0f, 1.3f, 0f)),
                ("30cm off the back wall ", new Vector3(0f, 2.7f, -5.0f)),
                ("30cm under the ceiling ", new Vector3(0f, 5.1f, 0f)),
                ("30cm above the floor   ", new Vector3(0f, 0.3f, 0f)),
            })
            {
                var probe = new SphericalHarmonicsL2();
                LightProbes.GetInterpolatedProbe(pos, null, out probe);
                // Evaluate the SH toward a few directions; [0] is the ambient/DC term.
                var dirs = new Vector3[] { Vector3.up, Vector3.forward, Vector3.right };
                var cols = new Color[3];
                probe.Evaluate(dirs, cols);
                sb.AppendLine($"PROBE {label} @{pos}: DC={probe[0, 0]:0.####}  "
                            + $"up={cols[0].r:0.###} fwd={cols[1].r:0.###} right={cols[2].r:0.###}");
            }

            int spots = 0, mixed = 0, casting = 0;
            foreach (Light l in Object.FindObjectsByType<Light>(FindObjectsInactive.Include))
            {
                if (l.type != LightType.Spot) continue;
                spots++;
                if (l.lightmapBakeType == LightmapBakeType.Mixed) mixed++;
                if (l.shadows != LightShadows.None) casting++;
            }
            sb.AppendLine($"spot lights: {spots}, Mixed: {mixed}, currently casting: {casting}");

            Debug.Log("[LightingProbeDump]\n" + sb);
        }

        private static void Report(StringBuilder sb, Scene scene, string label,
                                   System.Func<Renderer, bool> match)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
                {
                    if (!match(r)) continue;

                    Material m = r.sharedMaterial;
                    var flags = GameObjectUtility.GetStaticEditorFlags(r.gameObject);
                    var mr = r as MeshRenderer;
                    sb.AppendLine($"--- {label} '{r.name}' at {r.transform.position} ---");
                    sb.AppendLine($"    material        : {(m == null ? "null" : m.name)}");
                    if (m != null && m.HasProperty("_BaseColor"))
                        sb.AppendLine($"    _BaseColor      : {m.GetColor("_BaseColor")}");
                    if (m != null && m.HasProperty("_Smoothness"))
                        sb.AppendLine($"    _Smoothness     : {m.GetFloat("_Smoothness")}");
                    if (m != null)
                        sb.AppendLine($"    giFlags         : {m.globalIlluminationFlags}");
                    if (m != null && m.HasProperty("_EmissionColor"))
                        sb.AppendLine($"    _EmissionColor  : {m.GetColor("_EmissionColor")}  "
                                    + $"keyword={m.IsKeywordEnabled("_EMISSION")}");
                    sb.AppendLine($"    staticFlags     : {flags}");
                    sb.AppendLine($"    receiveGI       : {(mr == null ? "n/a" : mr.receiveGI.ToString())}");
                    sb.AppendLine($"    lightmapIndex   : {r.lightmapIndex}");
                    sb.AppendLine($"    receiveShadows  : {r.receiveShadows}");
                    sb.AppendLine($"    castShadows     : {r.shadowCastingMode}");
                    sb.AppendLine($"    lightProbeUsage : {r.lightProbeUsage}");
                    sb.AppendLine($"    reflProbeUsage  : {r.reflectionProbeUsage}");
                    sb.AppendLine($"    hasPropertyBlock: {r.HasPropertyBlock()}");
                    return;
                }
            }
            sb.AppendLine($"--- {label}: NONE FOUND ---");
        }
    }
}
