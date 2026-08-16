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

        // THE OTHER SETTING WORTH PERSISTING. Not for the same reason as sensitivity - there is no
        // browser quirk behind it - but because this game runs a PA, a clock, four running taps and a
        // building that shakes, and "too loud" is the one complaint a player cannot work around from
        // inside the game.
        //
        // Applied through `AudioListener.volume`, which is global and survives a scene load, so the
        // title screen setting it is enough for the run that follows.
        public const float DefaultMasterVolume = 0.8f;

        private const string MouseSensitivityKey = "iteration.mouseSensitivity";
        private const string MasterVolumeKey = "iteration.masterVolume";

        private static float masterVolume = -1f;

        public static float MasterVolume
        {
            get
            {
                if (masterVolume < 0f)
                    masterVolume = Mathf.Clamp01(
                        PlayerPrefs.GetFloat(MasterVolumeKey, DefaultMasterVolume));
                return masterVolume;
            }
            // Applied the instant it changes so a drag is audible while it is being dragged, and
            // committed to disk with everything else in Save().
            set
            {
                masterVolume = Mathf.Clamp01(value);
                AudioListener.volume = masterVolume;
            }
        }

        // HOW HARD THE DEVICE IS ASKED TO WORK, which is not a player setting and is not audio -
        // it is here because this is already the one place that applies engine-wide state at
        // startup, and a second file to hold one number would be worse.
        //
        // **UNCAPPED IS THE PROBLEM, NOT THE SCENE.** Play on a phone reported no lag at all and a
        // warm device, which is what a game rendering as many frames as the panel will take looks
        // like: a 120Hz phone was drawing 120 frames a second of a game whose clock is sixty
        // seconds long. Nothing in this project benefits from the second sixty - the loop is timed
        // in seconds, `PlayerRecorder` samples on a fixed interval and `GhostReplayer` scrubs by
        // elapsed time, so none of it is frame-coupled.
        //
        // Halving the frames halves the CPU side outright - two hundred-odd polling components,
        // culling and draw submission - which is where the heat is, and costs nothing visible.
        public const int BrowserFrameCap = 60;

        // **THE BROWSER, NOT THE PHONE.** This started as a mobile-only cap and play found the half
        // it was missing: with the phone capped and its pixels budgeted, the DESKTOP browser was the
        // laggier of the two. A desktop canvas is several times a phone's area and was running
        // uncapped on top of it, so the machine with more power was being asked for far more work.
        //
        // Applied to the whole WebGL player rather than to mobile, and NOT to a standalone build:
        // Steam is the platform where somebody may genuinely want their monitor's refresh, and it is
        // not the one where the frame budget is being fought over.
        public static void ApplyFrameCap()
        {
            if (Application.platform != RuntimePlatform.WebGLPlayer) return;
            Application.targetFrameRate = BrowserFrameCap;
        }

        // Everything the engine has to be told at startup, in every scene, without anyone having to
        // remember to call it. `AudioListener.volume` and `targetFrameRate` are both globals that
        // nothing resets for us, and a run started straight from the Editor never passes through the
        // title screen where they used to be applied.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ApplyOnLoad()
        {
            ApplyAudio();
            ApplyFrameCap();
        }

        // Pushes the stored value at the engine. Called wherever audio starts mattering - the title
        // screen and the pause menu - because `AudioListener.volume` is a global that nothing resets
        // for us, and a run started straight from the Editor never passes through the menu.
        public static void ApplyAudio() => AudioListener.volume = MasterVolume;

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
            bool any = false;

            if (mouseSensitivity >= 0f)
            {
                PlayerPrefs.SetFloat(MouseSensitivityKey, mouseSensitivity);
                any = true;
            }

            if (masterVolume >= 0f)
            {
                PlayerPrefs.SetFloat(MasterVolumeKey, masterVolume);
                any = true;
            }

            // One flush for both, for the reason the setter does not write: on WebGL every Save is a
            // storage round trip.
            if (any) PlayerPrefs.Save();
        }

        private static float Clamp(float value) =>
            Mathf.Clamp(value, MinMouseSensitivity, MaxMouseSensitivity);
    }
}
