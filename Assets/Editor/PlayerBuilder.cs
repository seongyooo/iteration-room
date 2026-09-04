using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
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
        private const string WindowsOutput = "Build/Windows";
        private const string MacOutput = "Build/Mac";
        private const string LinuxOutput = "Build/Linux";
        private const string AppIconPath = "Assets/Textures/AppIcon.png";
        // THE NAME IS NOT DECORATION - IT IS AN ADDRESS. `companyName` and `productName` together
        // decide where a built player keeps its data and its `PlayerPrefs`: the registry key
        // `HKCU\Software\<company>\<product>` on Windows, `~/Library/Preferences/<bundle id>` on
        // macOS. **Changing either one abandons every setting and key binding an existing player
        // has saved** - which is why this was set before anything shipped to anybody rather than
        // after, and why it should not be edited again without meaning to reset those.
        private const string Company = "seonline";
        // The bundle identifier is derived from these two when it is left empty, which would have
        // put `com.DefaultCompany.Iteration` on a Mac app. Stated instead, so it cannot follow a
        // later rename by accident.
        private const string BundleId = "com.seonline.iteration";

        // **THE VERSION IS AUTHORED HERE, next to the rest of the identity, rather than typed into
        // Player Settings by hand** (2026-09-05). It is the one piece of branding that changes
        // often, and it was the only one this method read without writing - so a build could be cut
        // with a version nobody had chosen, which is exactly what happened to v1.0.
        //
        // Unlike `Company` and `BundleId` above, changing this is SAFE: it is stamped on the
        // executable and reported in the build log, and nothing keys player data off it.
        //
        // 1.1, from 1.0: occlusion culling, END on the touch controls, and the cycle 2 floor hatch,
        // cycle 3 gate and ghost mirror-reach fixes.
        private const string Version = "1.1";

        [MenuItem("Iteration Room/Build WebGL Player")]
        public static void BuildWebGL()
        {
            string[] scenes = EnabledScenes();
            if (scenes == null)
                return;

            ApplyBrand();

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

        // THE THREE DESKTOP PLAYERS. One menu item each rather than one that builds all three,
        // because switching the active build target reimports the whole project - 89MB of glb -
        // and three in a row costs three reimports whether they are one click or three. Separate
        // items at least let a failed one be redone on its own.
        //
        // Unity cross-compiles all three from a Windows editor; the only thing needed is the
        // module, which the Hub adds to an existing install (Installs > gear > Add modules).

        [MenuItem("Iteration Room/Build Windows Player")]
        public static void BuildWindows()
            => BuildStandalone(BuildTarget.StandaloneWindows64, WindowsOutput, "Iteration.exe");

        [MenuItem("Iteration Room/Build macOS Player")]
        public static void BuildMac()
            => BuildStandalone(BuildTarget.StandaloneOSX, MacOutput, "Iteration.app");

        [MenuItem("Iteration Room/Build Linux Player")]
        public static void BuildLinux()
            => BuildStandalone(BuildTarget.StandaloneLinux64, LinuxOutput, "Iteration.x86_64");

        private static void BuildStandalone(BuildTarget target, string outputDir, string executable)
        {
            string[] scenes = EnabledScenes();
            if (scenes == null)
                return;

            ApplyBrand();

            // BUILDING A MAC PLAYER WITH IL2CPP REQUIRES A MAC - it runs Apple's toolchain, which
            // does not exist here. Mono is the standalone default and is what makes the cross-build
            // possible at all; the cost is that the .app is x86_64 only, so Apple Silicon machines
            // run it under Rosetta 2. Checked rather than set, because the scripting backend is one
            // setting shared by all three desktop targets and this method has no business changing
            // what the Windows player is built with.
            if (target == BuildTarget.StandaloneOSX
                && PlayerSettings.GetScriptingBackend(NamedBuildTarget.Standalone) == ScriptingImplementation.IL2CPP)
            {
                Debug.LogError("[PlayerBuilder] macOS + IL2CPP cannot be cross-built from Windows. "
                             + "Set Player Settings > Configuration > Scripting Backend to Mono, or "
                             + "build it on a Mac. Nothing was built.");
                return;
            }

            // THE SWITCH IS ALSO THE "IS THE MODULE INSTALLED" TEST, and it must be treated as one.
            // It returns false for a target this install cannot build, and the failure that matters
            // is the one where that is ignored: BuildPlayer then builds for whatever target was
            // already active, so a Windows player lands in Build/Mac and reads as a success right up
            // until a tester downloads it. Same shape as the WebGL path above.
            if (EditorUserBuildSettings.activeBuildTarget != target)
            {
                Debug.Log($"[PlayerBuilder] Switching active build target to {target} (this reimports assets)...");
                if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, target))
                {
                    Debug.LogError($"[PlayerBuilder] Could not switch to {target} - the module is "
                                 + "probably not installed. Unity Hub > Installs > 6000.5.7f1 (gear) > "
                                 + "Add modules > Mac Build Support (Mono) / Linux Build Support (Mono). "
                                 + "Nothing was built.");
                    return;
                }
            }

            // NOT A DEVELOPMENT BUILD, AND SAID TWICE ON PURPOSE. `CaptureRig` arms itself on
            // `Application.isEditor || Debug.isDebugBuild`, so a development build hands a tester
            // K, L and J - and the first of those wipes the HUD. `BuildOptions` is what BuildPlayer
            // actually reads; the EditorUserBuildSettings flags are what the Build Profiles window
            // shows, and leaving the two disagreeing is how this comes back.
            EditorUserBuildSettings.development = false;
            EditorUserBuildSettings.connectProfiler = false;
            EditorUserBuildSettings.allowDebugging = false;

            Directory.CreateDirectory(outputDir);

            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = Path.Combine(outputDir, executable),
                target = target,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None,
            });

            BuildSummary summary = report.summary;
            if (summary.result != BuildResult.Succeeded)
            {
                Debug.LogError($"[PlayerBuilder] {target} build {summary.result} "
                             + $"({summary.totalErrors} errors) after {summary.totalTime.TotalMinutes:F1} min");
                return;
            }

            Debug.Log($"[PlayerBuilder] {target} build SUCCEEDED in {summary.totalTime.TotalMinutes:F1} min, "
                    + $"{summary.totalSize / (1024f * 1024f):F1} MB at {Path.GetFullPath(outputDir)}");

            // WHAT TO DO WITH IT, SAID AT THE MOMENT IT EXISTS. Packaging is where these two targets
            // go wrong, and they go wrong on the tester's machine rather than in this log.
            if (target == BuildTarget.StandaloneOSX)
                Debug.LogWarning("[PlayerBuilder] macOS: a zip made on Windows DROPS THE EXECUTABLE BIT "
                               + "and the .app then will not open at all. Upload with butler, which "
                               + "preserves permissions, and tell testers it is unsigned: right-click > "
                               + "Open, or `xattr -dr com.apple.quarantine Iteration.app`.");
            else if (target == BuildTarget.StandaloneLinux64)
                Debug.LogWarning("[PlayerBuilder] Linux: a zip made on Windows DROPS THE EXECUTABLE BIT. "
                               + "Upload with butler, or tell testers to `chmod +x Iteration.x86_64`.");
        }

        // WHAT THE THING IS CALLED AND WHAT IT LOOKS LIKE BEFORE IT DRAWS ANYTHING. Written here at
        // build time rather than left sitting in ProjectSettings, for the same reason the WebGL
        // compression settings are: an Editor round-trip can lose a setting nobody is watching, and
        // these three are only ever noticed by somebody who is not in the room.
        private static void ApplyBrand()
        {
            PlayerSettings.bundleVersion = Version;

            // THE ICON IS A BUILD PRODUCT. `SceneBuilder.CaptureAppIcon` renders it from the room on
            // every scene build, so it can never drift from what the game looks like - and it is
            // missing on a fresh clone until the scenes are built, exactly like the scenes.
            Texture2D icon = AssetDatabase.LoadAssetAtPath<Texture2D>(AppIconPath);
            if (icon == null)
            {
                Debug.LogWarning($"[PlayerBuilder] No icon at {AppIconPath} - the player will wear Unity's "
                               + "default. Run the scene build first (it renders one).");
            }
            else
            {
                // One image for every size Unity asks for; it downscales. The array LENGTH is what
                // SetIcons validates against, so it has to be filled rather than passed as a single.
                int[] sizes = PlayerSettings.GetIconSizes(NamedBuildTarget.Standalone, IconKind.Any);
                Texture2D[] icons = new Texture2D[sizes.Length];
                for (int i = 0; i < icons.Length; i++)
                    icons[i] = icon;
                PlayerSettings.SetIcons(NamedBuildTarget.Standalone, icons, IconKind.Any);
                Debug.Log($"[PlayerBuilder] Icon set from {AppIconPath} for {sizes.Length} size(s).");
            }

            // NO SPLASH SCREEN. Unity 6 lets a Personal licence turn this off, which earlier versions
            // did not - so this is a setting that USED to be impossible and now is not, and it is
            // written rather than assumed for that reason.
            //
            // **The licence gets the last word, and it takes it silently.** If this install is not
            // entitled to hide the logo, Unity forces these back on during the build and nothing in
            // the console says so. So they are read back and logged: if the line below reports the
            // splash still showing, the setting is not the problem and there is nothing to fix here.
            PlayerSettings.SplashScreen.show = false;
            PlayerSettings.SplashScreen.showUnityLogo = false;
            Debug.Log($"[PlayerBuilder] Splash: show={PlayerSettings.SplashScreen.show}, "
                    + $"unityLogo={PlayerSettings.SplashScreen.showUnityLogo} (a licence can force these "
                    + "back on at build time - check the built player, not this line).");

            PlayerSettings.companyName = Company;
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, BundleId);

            // The window title bar, and the folder the player's data and PlayerPrefs live in.
            Debug.Log($"[PlayerBuilder] {PlayerSettings.companyName} / {PlayerSettings.productName} "
                    + $"v{PlayerSettings.bundleVersion}, bundle id {BundleId}");
        }

        // Whatever SceneBuilder last put in the build settings, in its order - MainMenu first, so
        // the player opens on the title screen. Read rather than hardcoded, so the two can never
        // disagree about which scenes exist. Null means "the reason is already logged, stop".
        private static string[] EnabledScenes()
        {
            string[] scenes = EditorBuildSettings.scenes
                .Where(s => s.enabled)
                .Select(s => s.path)
                .ToArray();

            if (scenes.Length == 0)
            {
                Debug.LogError("[PlayerBuilder] No enabled scenes in the build settings. Run SceneBuilder.Build first.");
                return null;
            }

            Debug.Log($"[PlayerBuilder] Scenes: {string.Join(", ", scenes)}");
            return scenes;
        }
    }
}
