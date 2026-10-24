using UnityEditor;
using UnityEditor.Recorder;
using UnityEditor.Recorder.Encoder;
using UnityEditor.Recorder.Input;
using UnityEngine;

// Prototype > Record Demo Clip: plays the Chemistry Yard with the scripted
// DemoDirector and records the Game view to Recordings/ChemistryYard.mp4.
// Recording runs at a fixed 30 fps of game time, so the clip is smooth
// even when the Editor renders slower than real time.
public static class DemoRecorder
{
    const string Flag = "Prototype.RecordDemo";
    static RecorderController controller;

    [MenuItem("Prototype/Record Demo Clip")]
    public static void Record()
    {
        if (EditorApplication.isPlaying) return;
        SessionState.SetBool(Flag, true);
        EditorApplication.isPlaying = true;
    }

    [InitializeOnLoadMethod]
    static void Hook()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Flag, false))
            {
                SessionState.SetBool(Flag, false);
                Begin();
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

    static void Begin()
    {
        SetGameViewGizmos(false);
        var director = new GameObject("DemoDirector").AddComponent<DemoDirector>();

        var movie = ScriptableObject.CreateInstance<MovieRecorderSettings>();
        movie.name = "Demo";
        movie.Enabled = true;
        movie.EncoderSettings = new CoreEncoderSettings
        {
            Codec = CoreEncoderSettings.OutputCodec.MP4,
            EncodingQuality = CoreEncoderSettings.VideoEncodingQuality.High,
        };
        movie.ImageInputSettings = new GameViewInputSettings { OutputWidth = 1280, OutputHeight = 720 };
        movie.AudioInputSettings.PreserveAudio = false;
        movie.OutputFile = "Recordings/ChemistryYard";

        var settings = ScriptableObject.CreateInstance<RecorderControllerSettings>();
        settings.AddRecorderSettings(movie);
        settings.SetRecordModeToManual();
        settings.FrameRate = 30f;
        settings.CapFrameRate = true;

        controller = new RecorderController(settings);
        controller.PrepareRecording();
        controller.StartRecording();
        Debug.Log("Recording demo clip...");

        void Tick()
        {
            if (director != null && !director.Done) return;
            EditorApplication.update -= Tick;
            EditorApplication.isPlaying = false;
        }
        EditorApplication.update += Tick;
    }

    static void Finish()
    {
        if (controller == null || !controller.IsRecording()) return;
        controller.StopRecording();
        if (gizmosWere.HasValue) SetGameViewGizmos(gizmosWere.Value);
        gizmosWere = null;
        controller = null;
        Debug.Log("Saved Recordings/ChemistryYard.mp4");
    }
}
