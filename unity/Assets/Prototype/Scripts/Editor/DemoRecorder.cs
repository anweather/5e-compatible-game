using UnityEditor;
using UnityEditor.Recorder;
using UnityEditor.Recorder.Encoder;
using UnityEditor.Recorder.Input;
using UnityEngine;

// Prototype > Record Demo Clip: plays the Chemistry Yard with the scripted
// DemoDirector and records the Game view to Recordings/ChemistryYard.mp4.
// Prototype > Record Overnight Showcase does the same with OvernightShowcase
// (physics, earth, items and live rules) at 1080p, as JPEG frames in
// Recordings/OvernightShowcase/ for ffmpeg: the movie encoder can't keep up
// with 1080p and queues frames in memory until the Editor runs out.
// Prototype > Record Valley Showcase records ValleyShowcase (the chunked
// valley) the same way, into Recordings/ValleyShowcase/.
// RecordShowcase("EarthShowcase") records any ShowcaseScript by its class name the same
// way (the recap films: EarthShowcase, SkyFireShowcase, WaterShowcase,
// ValleyTourShowcase), from script eval or the CLI.
// RecordUntil records any running session, such as one player's view of
// the 4-player demo.
public static class DemoRecorder
{
    const string Flag = "Prototype.RecordDemo";
    const string Demo = "demo", Overnight = "overnight", Valley = "valley";
    static RecorderController controller;

    [MenuItem("Prototype/Record Demo Clip")]
    public static void Record() => PlayAndRecord(Demo);

    [MenuItem("Prototype/Record Overnight Showcase")]
    public static void RecordOvernightShowcase() => PlayAndRecord(Overnight);

    [MenuItem("Prototype/Record Valley Showcase")]
    public static void RecordValleyShowcase() => PlayAndRecord(Valley);

    const string Named = "showcase:";

    public static void RecordShowcase(string typeName) => PlayAndRecord(Named + typeName);

    static void PlayAndRecord(string which)
    {
        if (EditorApplication.isPlaying) return;
        SessionState.SetString(Flag, which);
        EditorApplication.isPlaying = true;
    }

    [InitializeOnLoadMethod]
    static void Hook()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            string which = SessionState.GetString(Flag, "");
            if (state == PlayModeStateChange.EnteredPlayMode && which != "")
            {
                SessionState.EraseString(Flag);
                Begin(which);
            }
            else if (state == PlayModeStateChange.ExitingPlayMode) Finish();
        };
    }

    static bool? gizmosWere;

    internal static void HideGizmos() => SetGameViewGizmos(false);

    internal static void RestoreGizmos()
    {
        if (gizmosWere.HasValue) SetGameViewGizmos(gizmosWere.Value);
        gizmosWere = null;
    }

    static void SetGameViewGizmos(bool on)
    {
        var type = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
        var field = type?.GetField("m_Gizmos", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        if (field == null) return;
        foreach (var view in Resources.FindObjectsOfTypeAll(type))
        {
            gizmosWere ??= (bool)field.GetValue(view);
            field.SetValue(view, on);
        }
    }

    static void Begin(string which)
    {
        void Stop() => EditorApplication.isPlaying = false;
        if (which == Overnight) { RecordShowcase<OvernightShowcase>(Stop); return; }
        if (which == Valley) { RecordShowcase<ValleyShowcase>(Stop); return; }
        if (which.StartsWith(Named, System.StringComparison.Ordinal))
        {
            var type = typeof(ShowcaseScript).Assembly.GetType(which.Substring(Named.Length));
            if (type == null || !typeof(ShowcaseScript).IsAssignableFrom(type)) { Debug.LogError($"No showcase {which}"); Stop(); return; }
            var show = (ShowcaseScript)new GameObject(type.Name).AddComponent(type);
            RecordUntil($"Recordings/{type.Name}", () => show == null || show.Done, realTime: false, Stop, 1920, 1080, frames: true);
            return;
        }
        var director = new GameObject("DemoDirector").AddComponent<DemoDirector>();
        RecordUntil("Recordings/ChemistryYard", () => director == null || director.Done, realTime: false, Stop);
    }

    static void RecordShowcase<T>(System.Action stop) where T : ShowcaseScript
    {
        var name = typeof(T).Name;
        var show = new GameObject(name).AddComponent<T>();
        RecordUntil($"Recordings/{name}", () => show == null || show.Done, realTime: false, stop, 1920, 1080, frames: true);
    }

    public static void RecordUntil(string file, System.Func<bool> done, bool realTime, System.Action finished = null,
        int width = 1280, int height = 720, bool frames = false)
    {
        HideGizmos();
        var input = new GameViewInputSettings { OutputWidth = width, OutputHeight = height };
        RecorderSettings recorder;
        if (frames)
        {
            recorder = ScriptableObject.CreateInstance<ImageRecorderSettings>();
            var images = (ImageRecorderSettings)recorder;
            images.OutputFormat = ImageRecorderSettings.ImageRecorderOutputFormat.JPEG;
            images.JpegQuality = 95;
            images.imageInputSettings = input;
            images.OutputFile = $"{file}/frame_{DefaultWildcard.Frame}";
        }
        else
        {
            var movie = ScriptableObject.CreateInstance<MovieRecorderSettings>();
            movie.EncoderSettings = new CoreEncoderSettings
            {
                Codec = CoreEncoderSettings.OutputCodec.MP4,
                EncodingQuality = CoreEncoderSettings.VideoEncodingQuality.High,
            };
            movie.ImageInputSettings = input;
            movie.AudioInputSettings.PreserveAudio = false;
            movie.OutputFile = file;
            recorder = movie;
        }
        recorder.name = "Demo";
        recorder.Enabled = true;

        var settings = ScriptableObject.CreateInstance<RecorderControllerSettings>();
        settings.AddRecorderSettings(recorder);
        settings.SetRecordModeToManual();
        settings.FrameRate = 30f;
        settings.FrameRatePlayback = realTime ? FrameRatePlayback.Variable : FrameRatePlayback.Constant;
        settings.CapFrameRate = !realTime;

        outputFile = file;
        controller = new RecorderController(settings);
        controller.PrepareRecording();
        controller.StartRecording();
        Debug.Log(frames ? $"Recording {file}/frame_*.jpg..." : $"Recording {file}.mp4...");

        void Tick()
        {
            if (!done()) return;
            EditorApplication.update -= Tick;
            Finish();
            finished?.Invoke();
        }
        EditorApplication.update += Tick;
    }

    static string outputFile;

    static void Finish()
    {
        if (controller == null || !controller.IsRecording()) return;
        controller.StopRecording();
        RestoreGizmos();
        controller = null;
        Debug.Log($"Saved {outputFile}");
    }
}
