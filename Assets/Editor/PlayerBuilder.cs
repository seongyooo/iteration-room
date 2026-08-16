using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace IterationRoom.EditorTools
{
    // Standalone/WebGL player builds. Separate from SceneBuilder on purpose: that one assembles
    // the scenes and runs on every code change, where this one is slow, rare, and produces
    // something for other people.
    public static class PlayerBuilder
    {
        private const string WebGLOutput = "Build/WebGL";

        [MenuItem("Iteration Room/Build WebGL Player")]
        public static void BuildWebGL()
        {
            // Whatever SceneBuilder last put in the build settings, in its order - MainMenu first,
            // so the player opens on the title screen. Read rather than hardcoded, so the two can
            // never disagree about which scenes exist.
            string[] scenes = EditorBuildSettings.scenes
                .Where(s => s.enabled)
                .Select(s => s.path)
                .ToArray();

            if (scenes.Length == 0)
            {
                Debug.LogError("[PlayerBuilder] No enabled scenes in the build settings. Run SceneBuilder.Build first.");
                return;
            }

            Debug.Log($"[PlayerBuilder] Scenes: {string.Join(", ", scenes)}");

            // Switching the active target is what forces the reimport, and it is by far the
            // slowest part of a first WebGL build - 89MB of glb has to be re-crunched. Done
            // explicitly rather than left to BuildPlayer so the log says when it started.
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.WebGL)
            {
                Debug.Log("[PlayerBuilder] Switching active build target to WebGL (this reimports assets)...");
                if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.WebGL, BuildTarget.WebGL))
                {
                    Debug.LogError("[PlayerBuilder] Could not switch to WebGL. Is the WebGL module installed?");
                    return;
                }
            }

            // Brotli for size, WITH the JavaScript fallback. The fallback costs a little loader
            // weight and a slower first load on a server that sends no Content-Encoding header -
            // and it is worth it, because without it such a server serves a blank page instead.
            // This build is going to strangers on hosts we do not control.
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Brotli;
            PlayerSettings.WebGL.decompressionFallback = true;
            // Only what the code throws. Full exception support is a large size and speed cost for
            // stack traces nobody testing the design is going to read.
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;
            // The room drops seventy rigidbodies at once; a heap that cannot grow is how that ends
            // as an out-of-memory abort on someone else's machine.
            PlayerSettings.WebGL.memoryGrowthMode = WebGLMemoryGrowthMode.Geometric;

            // THE PROJECT'S OWN TEMPLATE, which is Unity's Default with ONE line uncommented:
            // `config.devicePixelRatio = 1`. A phone reports a ratio of 2 or 3, and honouring it
            // renders four to nine times the pixels - so the same build that is comfortable on a
            // desktop is a slideshow on the device it was built for, for reasons that have nothing
            // to do with the scene.
            //
            // Set here rather than left in ProjectSettings so it cannot be lost to an Editor
            // round-trip, the same reasoning as the compression settings above.
            PlayerSettings.WebGL.template = "PROJECT:IterationMobile";

            // NOTE: EditorUserBuildSettings.overrideMaxTextureSize was tried here and DOES NOT
            // WORK for this project's problem. Textures were 97.3% of the first WebGL build
            // (612.8 MB against 11.0 MB of mesh - the 306k-triangle bed was never the issue), and
            // all of it is the 31 maps embedded in the two glb models. glTFast creates those as
            // sub-assets in code, so they never pass through TextureImporter and the override has
            // nothing to act on: a full rebuild with it set changed the total by 0.7 MB.
            //
            // Worse, it is not harmless - it would also have capped MenuBackground.png, which is
            // authored at 1920x1080 and drawn full-screen.
            //
            // The maps were shrunk inside the glb files instead, with Tools/shrink_glb_textures.py.
            // Nothing here needs to do anything about texture size.
            EditorUserBuildSettings.overrideMaxTextureSize = 0;

            Directory.CreateDirectory(WebGLOutput);

            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = WebGLOutput,
                target = BuildTarget.WebGL,
                targetGroup = BuildTargetGroup.WebGL,
                options = BuildOptions.None,
            });

            BuildSummary summary = report.summary;
            if (summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"[PlayerBuilder] WebGL build SUCCEEDED in {summary.totalTime.TotalMinutes:F1} min, "
                        + $"{summary.totalSize / (1024f * 1024f):F1} MB at {Path.GetFullPath(WebGLOutput)}");
            }
            else
            {
                Debug.LogError($"[PlayerBuilder] WebGL build {summary.result} "
                             + $"({summary.totalErrors} errors) after {summary.totalTime.TotalMinutes:F1} min");
            }
        }
    }
}
