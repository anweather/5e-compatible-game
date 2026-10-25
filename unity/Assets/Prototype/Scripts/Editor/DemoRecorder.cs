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
// RecordUntil records any running session, such as one player's view of
// the 4-player demo.
public static class DemoRecorder
{
    // Which recording to start once Play mode is up (empty: none).
    const string Flag = "Prototype.RecordDemo";
    const string Demo = "demo", Overnight = "overnight", Valley = "valley";
    static RecorderController controller;

    [MenuItem("Prototype/Record Demo Clip")]
    public static void Record() => PlayAndRecord(Demo);

    [MenuItem("Prototype/Record Overnight Showcase")]
    public static void RecordOvernightShowcase() => PlayAndRecord(Overnight);

    [MenuItem("Prototype/Record Valley Showcase")]
    public static void RecordValleyShowcase() => PlayAndRecord(Valley);

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

    // The Game view's Gizmos toggle (light icons and the like) shows up in recordings,
    // so it is switched off while recording and put back afterwards.
    static bool? gizmosWere;

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
        var director = new GameObject("DemoDirector").AddComponent<DemoDirector>();
        RecordUntil("Recordings/ChemistryYard", () => director == null || director.Done, realTime: false, Stop);
    }

    // A scripted showcase at 1080p, as JPEG frames in Recordings/<its class name>/.
    static void RecordShowcase<T>(System.Action stop) where T : ShowcaseScript
    {
        var name = typeof(T).Name;
        var show = new GameObject(name).AddComponent<T>();
        RecordUntil($"Recordings/{name}", () => show == null || show.Done, realTime: false, stop, 1920, 1080, frames: true);
    }

    /// <summary>
    /// Records the Game view (already in Play mode) to <paramref name="file"/>.mp4 until
    /// <paramref name="done"/> returns true. Fixed-rate recording runs game time at 30 fps,
    /// smooth even when the Editor renders slowly; real-time recording keeps pace with the
    /// wall clock, which a networked session needs because other machines don't wait.
    /// With <paramref name="frames"/> it writes numbered JPEGs into the folder <paramref name="file"/> instead.
    /// </summary>
    public static void RecordUntil(string file, System.Func<bool> done, bool realTime, System.Action finished = null,
        int width = 1280, int height = 720, bool frames = false)
    {
        SetGameViewGizmos(false);
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
        if (gizmosWere.HasValue) SetGameViewGizmos(gizmosWere.Value);
        gizmosWere = null;
        controller = null;
        Debug.Log($"Saved {outputFile}");
    }
}
