using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Chem;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

// The frame-rate log (docs/FAST_LOOP.md, "The play log"): in every player build, on by default, so
// a player can play as usual and read the frame rate afterwards. Each frame only adds to running
// totals. Once a second one line goes to a CSV beside the player log, by PlayLogFile on a thread of
// its own: frame rate, the worst frame, frames over 33 and 50 ms, CPU and GPU times, what the world
// was doing. A frame over 100 ms adds its own line (in a development build, with its top markers).
// F8 ("that felt bad") adds a marked line and a small screenshot. Quitting adds a summary line. The
// Esc menu turns it off; the choice is kept. The Editor doesn't log, except for the probe.
// -no-play-log starts a player without it, to measure what it costs.
public class PlayLog : MonoBehaviour
{
    const string OnKey = "playLog.on";
    const string Header = "kind,seconds,clock,fps,worst_ms,over_33,over_50,cpu_main_ms,cpu_render_ms,gpu_ms,weather,chunks,players,mods,what";
    const float HitchMs = 100f, SlowMs = 33f, VerySlowMs = 50f;
    const float LoadingSeconds = 5f; // the summary leaves these out: the first frame alone can take seconds
    const int EventsASecond = 3, ShotMip = 2;
    const int SectionOrder = 495; // after Controls, before Mods
    static readonly CultureInfo Plain = CultureInfo.InvariantCulture;
    static readonly FrameTiming[] Timing = new FrameTiming[1];

    /// <summary>The log, which exists in every game; it may not be writing.</summary>
    public static PlayLog Instance { get; private set; }

    // This second's totals.
    int frames, over33, over50, timed, hitches, marks;
    float elapsedMs, worstMs, mainMs, renderMs, gpuMs;
    double nextSecond;
    readonly List<float> everySecond = new();

    PlayLogFile file;
    ChemGrid grid;
    RuleSwitcher rules;
    LogEntry seen;
    PerfMarkers markers;
    float startedAt;

    /// <summary>The file being written; null while it's off.</summary>
    public string FilePath => file?.Path;

    /// <summary>The player's choice, kept between sessions.</summary>
    public static bool Wanted
    {
        get => PlayerPrefs.GetInt(OnKey, 1) == 1;
        set
        {
            PlayerPrefs.SetInt(OnKey, value ? 1 : 0);
            PlayerPrefs.Save();
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void StartWithTheGame()
    {
        if (ProbeLink.InVirtualPlayer || Instance != null) return;
        var go = new GameObject("Play log");
        DontDestroyOnLoad(go);
        go.AddComponent<PlayLog>();
    }

    void Awake()
    {
        Instance = this;
        QuickExit.BeforeExit += End; // a Windows player has no OnApplicationQuit
    }

    void OnEnable() => PauseMenu.AddSection(DrawEntry, SectionOrder);

    void OnDisable() => PauseMenu.RemoveSection(DrawEntry);

    void Start()
    {
        if (Application.isEditor || !Wanted || Environment.GetCommandLineArgs().Contains("-no-play-log")) return;
        Begin(Folder());
    }

    // Beside the player log, so one folder holds everything about a session.
    static string Folder()
    {
        string log = Application.consoleLogPath;
        return string.IsNullOrEmpty(log) ? Application.persistentDataPath : Path.GetDirectoryName(log);
    }

    void DrawEntry()
    {
        if (!PauseMenu.Button($"Frame-rate log: {(Wanted ? "On" : "Off")}", PauseMenu.ButtonStyle)) return;
        Wanted = !Wanted;
        if (Wanted) Begin(Folder());
        else End();
    }

    /// <summary>Starts a new file in this folder, keeping the last few (also for the probe).</summary>
    public void Begin(string folder)
    {
        End();
        file = new PlayLogFile(folder, $"play-log-{DateTime.Now.ToString("yyyy-MM-dd-HHmmss", Plain)}");
        file.Add($"# Spell Chemistry Valley {Application.version}, {SystemInfo.graphicsDeviceName}, {Screen.width}x{Screen.height}");
        file.Add(Header);
        startedAt = Time.unscaledTime;
        nextSecond = startedAt + 1f;
        everySecond.Clear();
        hitches = marks = 0;
        ClearSecond();
        if (Debug.isDebugBuild) markers = new PerfMarkers();
    }

    /// <summary>Stops with the summary, and waits for the file to be written.</summary>
    public void End()
    {
        if (file == null) return;
        string summary = Summary();
        file.Add(Line("summary", 0f, 0, 0, summary));
        file.Close();
        Debug.Log("PLAY LOG: " + summary);
        file = null;
        markers?.Dispose();
        markers = null;
    }

    void OnApplicationQuit() => End();

    void OnDestroy()
    {
        QuickExit.BeforeExit -= End;
        End();
        if (Instance == this) Instance = null;
    }

    void Update()
    {
        if (file == null) return;
        Frame(Time.unscaledDeltaTime * 1000f);
        ReadTiming();
        markers?.Sample();
        if (Keyboard.current != null && Keyboard.current.f8Key.wasPressedThisFrame) Mark();
        if (Time.unscaledTimeAsDouble >= nextSecond) Second();
    }

    /// <summary>A frame's time, into the totals. Makes no garbage unless the frame is a hitch.</summary>
    public void Frame(float ms)
    {
        frames++;
        elapsedMs += ms;
        if (ms > worstMs) worstMs = ms;
        if (ms > SlowMs) over33++;
        if (ms > VerySlowMs) over50++;
        if (ms > HitchMs) Hitch(ms);
    }

    void ReadTiming()
    {
        FrameTimingManager.CaptureFrameTimings();
        if (FrameTimingManager.GetLatestTimings(1, Timing) < 1) return;
        var t = Timing[0];
        timed++;
        mainMs += (float)t.cpuMainThreadFrameTime;
        renderMs += (float)t.cpuRenderThreadFrameTime;
        gpuMs += (float)t.gpuFrameTime;
    }

    void Second()
    {
        nextSecond = Time.unscaledTimeAsDouble + 1.0;
        float fps = elapsedMs > 0f ? frames * 1000f / elapsedMs : 0f;
        if (Time.unscaledTime - startedAt >= LoadingSeconds) everySecond.Add(fps);
        file.Add(Line("second", fps, over33, over50, Happening()));
        ClearSecond();
    }

    void ClearSecond()
    {
        frames = over33 = over50 = timed = 0;
        elapsedMs = worstMs = mainMs = renderMs = gpuMs = 0f;
    }

    void Hitch(float ms)
    {
        hitches++;
        string what = Happening(false);
        if (markers != null) what = (what + " " + markers.Breakdown()).Trim();
        file.Add(Line("hitch", 1000f / ms, 1, 1, what, ms));
    }

    /// <summary>"That felt bad": a marked line, and a small picture of the moment.</summary>
    public void Mark()
    {
        if (file == null) return;
        marks++;
        string shot = $"{file.Stem}-mark-{marks}.jpg";
        file.Add(Line("mark", frames * 1000f / Mathf.Max(1f, elapsedMs), over33, over50, ("felt bad " + shot + " " + Happening(false)).Trim()));
        StartCoroutine(Shoot(Path.Combine(file.Folder, shot)));
        EventLog.Add("Noted");
    }

    // The frame as it's shown, read back without holding the game up, a quarter the size.
    System.Collections.IEnumerator Shoot(string path)
    {
        yield return new WaitForEndOfFrame();
        var picture = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { useMipMap = true, autoGenerateMips = false };
        ScreenCapture.CaptureScreenshotIntoRenderTexture(picture);
        picture.GenerateMips();
        int width = Mathf.Max(1, picture.width >> ShotMip), height = Mathf.Max(1, picture.height >> ShotMip);
        var writing = file;
        AsyncGPUReadback.Request(picture, ShotMip, TextureFormat.RGBA32, request =>
        {
            if (!request.hasError && writing != null) writing.AddPicture(path, request.GetData<byte>().ToArray(), width, height, SystemInfo.graphicsUVStartsAtTop);
            picture.Release();
            Destroy(picture);
        });
    }

    string Line(string kind, float fps, int slow, int verySlow, string what, float worst = -1f)
    {
        var world = WorldNow();
        var sb = new StringBuilder(160);
        sb.Append(kind).Append(',').Append(Num(Time.unscaledTime - startedAt)).Append(',').Append(DateTime.Now.ToString("HH:mm:ss", Plain)).Append(',');
        sb.Append(Num(fps)).Append(',').Append(Num(worst >= 0f ? worst : worstMs)).Append(',').Append(slow).Append(',').Append(verySlow).Append(',');
        sb.Append(Average(mainMs)).Append(',').Append(Average(renderMs)).Append(',').Append(Average(gpuMs)).Append(',');
        sb.Append(world != null ? world.Weather.Kind.ToString() : "").Append(',').Append(world != null ? world.ActiveChunks.Count : 0).Append(',');
        sb.Append(Mathf.Max(1, Knockback.All.Count)).Append(',').Append(Quoted(Mods())).Append(',').Append(Quoted(what));
        return sb.ToString();
    }

    string Average(float sum) => timed > 0 && sum > 0f ? Num(sum / timed) : "";

    static string Num(float value) => value.ToString("0.0", Plain);

    static string Quoted(string text) => $"\"{text.Replace("\"", "\"\"")}\"";

    World WorldNow()
    {
        if (grid == null)
        {
            grid = FindAnyObjectByType<ChemGrid>();
            rules = grid != null ? grid.GetComponent<RuleSwitcher>() : null;
        }
        return grid != null ? grid.World : null;
    }

    string Mods()
    {
        if (rules == null) return "";
        return string.Join("; ", rules.Rules.Mods.Select(m => rules.Catalog.Find(m.Id)?.Title ?? m.Id));
    }

    // What the world has logged since the last line, the newest few, and the dragon's phase.
    string Happening(bool moveOn = true)
    {
        var world = WorldNow();
        if (world == null) return "";
        var said = new List<string>();
        var log = world.Log;
        for (int i = log.Count - 1; i >= 0 && said.Count < EventsASecond && !ReferenceEquals(log[i], seen); i--) said.Add(log[i].Text);
        if (moveOn && log.Count > 0) seen = log[log.Count - 1];
        if (world.Dragon != null) said.Add($"dragon {world.Dragon.Phase}");
        return string.Join("; ", said);
    }

    string Summary()
    {
        var sorted = everySecond.OrderBy(f => f).ToList();
        if (sorted.Count == 0) return $"no full second; {file.Path}";
        float low = sorted[Mathf.Min(sorted.Count - 1, sorted.Count / 100)];
        return $"average {Num(sorted.Average())} fps, 1% low {Num(low)}, worst second {Num(sorted[0])}, over {sorted.Count} s after loading; {hitches} hitches, {marks} marks; {file.Path}";
    }
}
