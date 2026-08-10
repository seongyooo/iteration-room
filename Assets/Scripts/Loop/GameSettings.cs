using UnityEngine;

namespace IterationRoom
{
    // The one persisted setting in the project, and it exists because mouse sensitivity is not a
    // number that can be chosen once and be right.
    //
    // Unity's WebGL build hands the engine the browser's pointer-lock movementX completely
    // unscaled - the shipped framework.js does literally `HEAP32[idx+8] = e["movementX"]`, with no
    // devicePixelRatio and no canvas-size correction. That value has already been through the OS
    // pointer-speed slider and its acceleration curve on the way out of the browser, and Chrome,
    // Firefox and Safari do not agree on it. The Editor's input path never goes near any of that,
    // which is exactly why the room felt fine here and far too twitchy on itch.io.
    //
    // So the fix is not a better constant. The right value differs per tester, per browser and per
    // pointer setting, and the only thing that can resolve that is asking the player - hence the
    // slider in the pause menu. The default below is a starting point, not an answer.
    //
    // Note this is NOT frame-rate related, which is the usual first guess: GetAxis("Mouse X") is
    // already the delta accumulated since the last frame, so the per-frame values shrink as the
    // frame rate rises and the total rotation over a given sweep comes out the same. That is also
    // why FirstPersonController.HandleLook correctly uses no deltaTime.
    public static class GameSettings
    {
        public const float DefaultMouseSensitivity = 1.1f;
        public const float MinMouseSensitivity = 0.2f;
        public const float MaxMouseSensitivity = 4f;

        private const string MouseSensitivityKey = "iteration.mouseSensitivity";

        // Negative means "not read from disk yet". A nullable float would say the same thing, but
        // this is read once per frame by HandleLook and a plain float avoids the boxing.
        private static float mouseSensitivity = -1f;

        public static float MouseSensitivity
        {
            get
            {
                // Read lazily rather than from a static constructor: PlayerPrefs is a file (an
                // IndexedDB round trip on WebGL), and the first frame of this scene is already
                // doing plenty.
                if (mouseSensitivity < 0f) mouseSensitivity = Clamp(
                    PlayerPrefs.GetFloat(MouseSensitivityKey, DefaultMouseSensitivity));
                return mouseSensitivity;
            }
            // Deliberately does not write to disk. A uGUI Slider raises onValueChanged on every
            // frame of a drag, and on WebGL each PlayerPrefs.Save is a storage flush - so the value
            // is applied live and committed once, on Save() below.
            set => mouseSensitivity = Clamp(value);
        }

        // Called when the pause menu closes, by any route out of it. Nothing relies on
        // OnApplicationQuit: a browser tab is closed rather than quit, and Unity's WebGL shutdown
        // path is not guaranteed to run at all.
        public static void Save()
        {
            if (mouseSensitivity < 0f) return;
            PlayerPrefs.SetFloat(MouseSensitivityKey, mouseSensitivity);
            PlayerPrefs.Save();
        }

        private static float Clamp(float value) =>
            Mathf.Clamp(value, MinMouseSensitivity, MaxMouseSensitivity);
    }
}
