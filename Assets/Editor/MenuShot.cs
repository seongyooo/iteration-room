using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace IterationRoom.EditorTools
{
    // A PHOTOGRAPH OF THE TITLE SCREEN, SO IT CAN BE LOOKED AT WITHOUT PLAYING. Read-only: it opens
    // `MainMenu.unity`, renders one frame and never saves.
    //
    //   Unity.exe -batchmode -projectPath . -executeMethod
    //             IterationRoom.EditorTools.MenuShot.Run -quit -logFile shot.log
    //
    // **WHY IT EXISTS.** The title screen is authored entirely in `SceneBuilder.Menu.cs` - every
    // colour, margin and anchor is a number in code - and the only other way to see the result is to
    // press Play. During the 2026-09-04 rebuild to `docs/mainmenu_dark.png` that made every layout
    // question a round trip through the editor; this turned each one into a build and a look.
    //
    // **THE CANVAS HAS TO BE SWITCHED FOR THE FRAME.** The menu draws in `ScreenSpaceOverlay`, which
    // composites after every camera and so is invisible to `Camera.Render()` - the first run of this
    // came back as the background photograph with no interface on it at all. It is put into
    // `ScreenSpaceCamera` for the shot; the scene is not saved, so the shipped menu keeps Overlay.
    //
    // **WHAT IT CANNOT SHOW, AND THIS MATTERS MORE THAN WHAT IT CAN.** Nothing runs. `MainMenu.Start`
    // never fills the labels it fills at runtime, and `MenuRowHover.Update` never runs, so anything
    // that only exists while the pointer is on a row - the hover rule, the ink and weight change,
    // CONTINUE's cycle line - is absent from every frame this takes. It is a LAYOUT check, not a
    // behaviour one, and a row that looks right here can still be misaligned when it lights up.
    public static class MenuShot
    {
        // Overridable so a caller can drop the frame somewhere scratch rather than in the project.
        private static string OutDir =>
            System.Environment.GetEnvironmentVariable("MENU_SHOT_DIR") ?? "MenuShot";

        public static void Run()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                Debug.LogWarning("[MenuShot] No graphics device (-nographics): nothing rendered.");
                return;
            }

            EditorSceneManager.OpenScene(MenuScenePathForShot, OpenSceneMode.Single);
            Scene scene = SceneManager.GetActiveScene();

            Canvas canvas = null;
            Camera cam = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Canvas c in root.GetComponentsInChildren<Canvas>(true))
                    if (c.isRootCanvas && canvas == null) canvas = c;
                foreach (Camera c in root.GetComponentsInChildren<Camera>(true))
                    if (cam == null) cam = c;
            }

            if (canvas == null)
            {
                Debug.LogError("[MenuShot] No root canvas in the menu scene - nothing to photograph.");
                return;
            }

            if (cam == null)
            {
                cam = new GameObject("MenuShotCam").AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Color.black;
            }

            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = 1f;
            // The canvas has just changed how it lays out; without this the frame is taken against
            // the previous pass's rects.
            Canvas.ForceUpdateCanvases();

            var rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt;
            cam.Render();

            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            cam.targetTexture = null;

            Directory.CreateDirectory(OutDir);
            string path = Path.Combine(OutDir, "titlescreen.png");
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Debug.Log($"[MenuShot] wrote {path} - LAYOUT ONLY: no hover state, no runtime labels.");

            Object.DestroyImmediate(tex);
            rt.Release();
            Object.DestroyImmediate(rt);
        }

        // Named here rather than taken from `SceneBuilder`, so this tool keeps working if it is ever
        // lifted out of this project.
        private const string MenuScenePathForShot = "Assets/Scenes/MainMenu.unity";
    }
}
