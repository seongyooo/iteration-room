using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Recorder;
using UnityEditor.Recorder.Encoder;
using UnityEditor.Recorder.Input;
using UnityEngine;

namespace IterationRoom.EditorTools
{
    // Configures Unity Recorder for capturing this game, so the Recorder window opens ready to
    // record rather than ready to be configured.
    //
    // WHY RECORDER AND NOT A SCREEN CAPTURE. Windows' own recorder writes whatever the game actually
    // managed to draw. This game ACCUMULATES: a practised clear takes about ten iterations and a first
    // one more, so by the end there are nine or more ghosts on screen, each with an animator and the
    // afterimage shader. **The moment the game is most worth showing is the moment it runs slowest**,
    // so a screen capture is at its worst exactly where a trailer needs it to be at its best.
    //
    // `FrameRatePlayback.Constant` is the whole answer: Recorder drives `Time.captureFramerate`, so
    // the game advances a fixed 1/60s per rendered frame no matter how long that frame took in real
    // time. Play slows down; the file comes out smooth. Nothing else in the setup matters as much.
    //
    // Values live here rather than being clicked into the window, for the reason every other value in
    // this project lives in a script: a setting nobody wrote down is a setting that will be wrong
    // again next time.
    public static class RecorderSetup
    {
        private const string MovieName = "IterationRoom Gameplay 1080p60";
        private const string OutputDir = "Recordings";   // already git-ignored

        [MenuItem("Iteration Room/Set Up Recorder")]
        public static void Configure()
        {
            // The window's own settings object, so this configures what actually opens rather than a
            // preset somebody then has to remember to load.
            RecorderControllerSettings controller = RecorderControllerSettings.GetGlobalSettings();

            // Idempotent: re-running replaces our recorder instead of stacking a second copy beside
            // it. Anything the user added by hand is left alone.
            foreach (RecorderSettings existing in new List<RecorderSettings>(controller.RecorderSettings))
                if (existing != null && existing.name == MovieName)
                    controller.RemoveRecorder(existing);

            controller.FrameRate = 60f;
            // THE ONE THAT MATTERS - see the note at the top of this file.
            controller.FrameRatePlayback = FrameRatePlayback.Constant;
            // Do not let play run FASTER than the target either, so what is captured is paced the way
            // a player would experience it.
            controller.CapFrameRate = true;
            // Manual, not a frame or time interval: gameplay footage is started and stopped by hand,
            // because which iteration turns out to be worth keeping is not knowable in advance.
            controller.SetRecordModeToManual();

            var movie = ScriptableObject.CreateInstance<MovieRecorderSettings>();
            movie.name = MovieName;
            movie.Enabled = true;

            // 1920x1080 REGARDLESS OF THE GAME VIEW, which is the other thing a screen capture cannot
            // do. This project's Game view sits at 2.03:1, so a screen capture bakes that letterbox
            // into every frame; Recorder renders the size it is told to.
            movie.ImageInputSettings = new GameViewInputSettings
            {
                OutputWidth = 1920,
                OutputHeight = 1080,
            };

            movie.EncoderSettings = new CoreEncoderSettings
            {
                Codec = CoreEncoderSettings.OutputCodec.MP4,
                EncodingQuality = CoreEncoderSettings.VideoEncodingQuality.High,
            };

            // The PA announcer is half of what the facility is, and a silent clip of a white room is
            // a screensaver. Recorder captures the same mix the player hears.
            movie.CaptureAudio = true;
            movie.AudioInputSettings.PreserveAudio = true;

            // **AN ABSOLUTE PATH, AND THE FOLDER MADE BEFORE WE ASK FOR IT.** This was
            // `$"{OutputDir}/iteration_<Take>"` and it cost a recording session: a RELATIVE path is
            // not resolved against the project folder here, so `Recordings/iteration_<Take>` came out
            // as `C:\iteration_002.mp4` - the root of the system drive, which Windows does not let a
            // normal process write to. What the console then showed was three errors deep and none of
            // them said "path":
            //
            //     WindowsVideoMedia error 0x80070005 while reading C:\iteration_002.mp4
            //     VideoMediaEncoder::Create got unsupported format.
            //     Recording failed. Unable to create encoder
            //
            // 0x80070005 is E_ACCESSDENIED. The "unsupported format" line is a consequence of the
            // encoder never getting a file to open, not a codec problem - so anyone debugging this
            // from the last line up goes looking at H.264 settings that were never wrong.
            //
            // `CreateDirectory` because Recorder does not make the folder either, and a missing
            // directory fails exactly as unhelpfully as an unwritable one.
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string outputDir = Path.Combine(projectRoot, OutputDir);
            Directory.CreateDirectory(outputDir);

            // `Take` auto-increments, so a second recording never silently overwrites the first -
            // which is the failure mode that costs you the good run.
            // **CONCATENATED, NOT `Path.Combine`.** `<Take>` is a Recorder wildcard, and `<` and `>`
            // are illegal path characters - `Path.Combine` validates its arguments and throws
            // `ArgumentException: Illegal characters in path` before Recorder ever sees the pattern.
            // The wildcard is only a real path after Recorder expands it, so the join has to be dumb.
            //
            // Forward slashes: Recorder stores this as a string and a backslash in it reads as an
            // escape on the way back out.
            movie.OutputFile = outputDir.Replace('\\', '/').TrimEnd('/') + "/iteration_<Take>";

            controller.AddRecorderSettings(movie);
            controller.Save();
            AssetDatabase.SaveAssets();

            Debug.Log($"[RecorderSetup] '{MovieName}' ready: 1920x1080, 60 fps constant, H.264 MP4 "
                    + $"with audio, manual start/stop, writing to {outputDir}. "
                    + "Open Window > General > Recorder and press START RECORDING.");
        }
    }
}
