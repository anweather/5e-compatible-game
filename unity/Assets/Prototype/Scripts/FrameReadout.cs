using UnityEngine;
using UnityEngine.InputSystem;

// A small frame readout for playtests (#217): the last second's mean, 95th percentile and worst frame against the
// 60 fps budget (16.7 ms), in the top corner. Off unless asked for: F9, or Esc > Settings > Frame readout, remembered
// between sessions as the player's own setting (PlayerPrefs, never shared state). It reads only the frame times Unity
// gives each frame: a fixed ring of them, worked out four times a second into one line, nothing made per frame.
public sealed class FrameReadout : MonoBehaviour
{
    const string ShownKey = "frame-readout";
    const int SectionOrder = 506; // the Settings sub menu, after the play log's
    const float Budget = 1000f / 60f, RefreshSeconds = 0.25f;
    // Enough for a second at 500 fps; past it the oldest go.
    const int Ring = 512;

    /// <summary>The readout in this session, for the probe.</summary>
    public static FrameReadout Instance { get; private set; }

    readonly float[] times = new float[Ring], ages = new float[Ring], sorted = new float[Ring];
    int next, count;
    float nextRefresh;
    readonly GUIContent line = new();
    GUIStyle style;

    /// <summary>Whether the readout shows.</summary>
    public bool Shown { get; private set; }

    /// <summary>The last second's mean, 95th percentile and worst frame (ms), as last worked out.</summary>
    public float MeanMs { get; private set; }
    public float P95Ms { get; private set; }
    public float WorstMs { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void StartWithTheGame()
    {
        if (ProbeLink.InVirtualPlayer || Instance != null) return;
        var go = new GameObject("Frame readout");
        DontDestroyOnLoad(go);
        go.AddComponent<FrameReadout>();
    }

    void Awake()
    {
        Instance = this;
        Shown = PlayerPrefs.GetInt(ShownKey, 0) == 1;
    }

    void OnEnable() => PauseMenu.AddSection(DrawEntry, SectionOrder, PauseMenu.Submenu.Settings);

    void OnDisable() => PauseMenu.RemoveSection(DrawEntry);

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>Shows or hides the readout, and remembers it.</summary>
    public void Show(bool on)
    {
        Shown = on;
        PlayerPrefs.SetInt(ShownKey, on ? 1 : 0);
        PlayerPrefs.Save();
        nextRefresh = 0f;
    }

    void DrawEntry()
    {
        if (PauseMenu.Button($"Frame readout: {(Shown ? "On" : "Off")} (F9)", PauseMenu.ButtonStyle)) Show(!Shown);
    }

    static readonly Unity.Profiling.ProfilerMarker DIAG_UpdateMarker = new("FrameReadout.Update");

    void Update()
    {

        using var diagMarked = DIAG_UpdateMarker.Auto();
        var keyboard = Keyboard.current;
        if (keyboard != null && keyboard.f9Key.wasPressedThisFrame) Show(!Shown);
        if (!Shown) return;
        float now = Time.unscaledTime;
        times[next] = Time.unscaledDeltaTime * 1000f;
        ages[next] = now;
        next = (next + 1) % Ring;
        if (count < Ring) count++;
        if (now < nextRefresh) return;
        nextRefresh = now + RefreshSeconds;
        WorkOut(now);
    }

    // The frames of the last second: their mean, 95th percentile and worst, and the line that says them.
    void WorkOut(float now)
    {
        int n = 0;
        float sum = 0f;
        for (int i = 0; i < count; i++)
        {
            if (now - ages[i] > 1f) continue;
            sorted[n++] = times[i];
            sum += times[i];
        }
        if (n == 0) return;
        System.Array.Sort(sorted, 0, n);
        MeanMs = sum / n;
        P95Ms = sorted[Mathf.Min(n - 1, (int)(n * 0.95f))];
        WorstMs = sorted[n - 1];
        line.text = $"{MeanMs:0.0} ms mean  {P95Ms:0.0} p95  {WorstMs:0.0} worst  (16.7)";
    }

    // Green with the 95th percentile in budget, amber with only the mean in it, red past it.
    Color Colour()
    {
        if (P95Ms <= Budget) return new Color(0.6f, 1f, 0.6f);
        return MeanMs <= Budget ? new Color(1f, 0.85f, 0.4f) : new Color(1f, 0.5f, 0.45f);
    }

    void OnGUI()
    {
        if (!Shown || Event.current.type != EventType.Repaint) return;
        style ??= new GUIStyle(GUI.skin.label) { fontSize = 14, alignment = TextAnchor.UpperRight };
        style.normal.textColor = Colour();
        GUI.Label(new Rect(Screen.width - 420f, 6f, 410f, 24f), line, style);
    }
}
