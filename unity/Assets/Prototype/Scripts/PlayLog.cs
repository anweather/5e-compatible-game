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
// its own: frame rate, the worst frame and the 95th percentile, frames over 33 and 50 ms, CPU and GPU
// times, the world's speed and what it owes, what the world was doing. A frame over 100 ms adds its own line (in a development build, with its top markers).
// F8 ("that felt bad") adds a marked line and a small screenshot, as the playtest bot's finds do. Quitting adds a summary line. The
// Esc menu's Settings turns it off; the choice is kept. The Editor doesn't log, except for the probe.
// -no-play-log starts a player without it, to measure what it costs.
public class PlayLog : MonoBehaviour
{
    public const string OnKey = "playLog.on";
    const string Header = "kind,seconds,clock,fps,worst_ms,p95_ms,over_33,over_50,cpu_main_ms,cpu_render_ms,gpu_ms,sim_pct,tick_debt_s,weather,chunks,players,mods,what";
    const float HitchMs = 100f, SlowMs = 33f, VerySlowMs = 50f;
    const float LoadingSeconds = 5f; // the summary leaves these out: the first frame alone can take seconds
    const int EventsASecond = 3, ShotMip = 2;
    const int SectionOrder = 505; // the Settings sub menu: after Shake and rumble
    static readonly CultureInfo Plain = CultureInfo.InvariantCulture;
    static readonly FrameTiming[] Timing = new FrameTiming[1];

    /// <summary>The log, which exists in every game; it may not be writing.</summary>
    public static PlayLog Instance { get; private set; }

    // This second's totals.
    int frames, over33, over50, timed, hitches, marks;
    float elapsedMs, worstMs, mainMs, renderMs, gpuMs;
    double nextSecond;
    int ticksAtSecond;
    readonly List<float> everySecond = new(), frameMs = new();

    PlayLogFile file;
    ChemGrid grid;
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

    /// <summary>Whether a game opens with the log writing.</summary>
    public static bool StartsWriting(bool inEditor, bool wanted, string[] args) => !inEditor && wanted && !args.Contains("-no-play-log");

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

    void OnEnable() => PauseMenu.AddSection(DrawEntry, SectionOrder, PauseMenu.Submenu.Settings);

    void OnDisable() => PauseMenu.RemoveSection(DrawEntry);

    void Start()
    {
        if (StartsWriting(Application.isEditor, Wanted, Environment.GetCommandLineArgs())) Begin(Folder());
    }

    // Beside the player log, so one folder holds everything about a session.
    public static string Folder() => Folder(Application.consoleLogPath, () => ChemGrid.Folders);

    /// <summary>
    /// Beside the log, else the player's data folder; Unity's own when no rules can be found, as the play
    /// log is what tells of a broken start.
    /// </summary>
    public static string Folder(string log, Func<GameFolders> folders)
    {
        if (!string.IsNullOrEmpty(log)) return Path.GetDirectoryName(log);
        try
        {
            return folders().PlayerData;
        }
        catch (DirectoryNotFoundException)
        {
            return Application.persistentDataPath;
        }
    }

    void DrawEntry()
    {
        if (!PauseMenu.Button($"Play log: {(Wanted ? "On" : "Off")}", PauseMenu.ButtonStyle)) return;
        Wanted = !Wanted;
        if (Wanted) Begin(Folder());
        else End();
    }

    /// <summary>Starts a new file in this folder, keeping the last few (also for the probe).</summary>
    public void Begin(string folder)
    {
        End();
        file = new PlayLogFile(folder, $"play-log-{DateTime.Now.ToString("yyyy-MM-dd-HHmmss", Plain)}");
        headerOwed = true;
        early.Clear();
        startedAt = Time.unscaledTime;
        nextSecond = startedAt + 1f;
        everySecond.Clear();
        hitches = marks = 0;
        ClearSecond();
        if (Debug.isDebugBuild) markers = new PerfMarkers();
    }

    // The header waits for the first second's row: a player's window takes the size it was asked for only after its first
    // frames, and the header said 640 x 480 for a 1280 x 720 window (#217). Lines from before then wait with it.
    bool headerOwed;
    readonly List<string> early = new();

    void WriteHeader()
    {
        if (!headerOwed || file == null) return;
        headerOwed = false;
        file.Add($"# Spell Chemistry Valley {Application.version}, {SystemInfo.graphicsDeviceName}, {Screen.width}x{Screen.height}");
        file.Add(Header);
        foreach (string line in early) file.Add(line);
        early.Clear();
    }

    void Add(string line)
    {
        if (headerOwed) early.Add(line);
        else file.Add(line);
    }

    /// <summary>Stops with the summary, and waits for the file to be written.</summary>
    public void End()
    {
        if (file == null) return;
        string summary = Summary();
        Add(Line("summary", 0f, 0, 0, summary));
        WriteHeader();
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
        frameMs.Add(ms);
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
        WriteHeader();
        Add(Line("second", fps, over33, over50, Happening()));
        ClearSecond();
    }

    void ClearSecond()
    {
        frames = over33 = over50 = timed = 0;
        frameMs.Clear();
        ticksAtSecond = WorldNow()?.Tick ?? 0;
        elapsedMs = worstMs = mainMs = renderMs = gpuMs = 0f;
    }

    void Hitch(float ms)
    {
        hitches++;
        string what = Happening(false);
        if (markers != null) what = (what + " " + markers.Breakdown()).Trim();
        Add(Line("hitch", 1000f / ms, 1, 1, what, ms));
    }

    /// <summary>A line of what's going on, to tie a hitch to it: not a mark, which says something felt bad.</summary>
    public void Note(string what)
    {
        if (file == null) return;
        Add(Line("note", frames * 1000f / Mathf.Max(1f, elapsedMs), over33, over50, what));
    }

    /// <summary>"That felt bad": a marked line, and a small picture of the moment. The playtest bot marks what it finds.</summary>
    public void Mark(string why = "felt bad")
    {
        if (file == null) return;
        marks++;
        string shot = $"{file.Stem}-mark-{marks}.jpg";
        Add(Line("mark", frames * 1000f / Mathf.Max(1f, elapsedMs), over33, over50, $"{why} {shot} {Happening(false)}".Trim()));
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
        sb.Append(Num(fps)).Append(',').Append(Num(worst >= 0f ? worst : worstMs)).Append(',').Append(worst >= 0f ? "" : Num(P95())).Append(',').Append(slow).Append(',').Append(verySlow).Append(',');
        sb.Append(Average(mainMs)).Append(',').Append(Average(renderMs)).Append(',').Append(Average(gpuMs)).Append(',');
        sb.Append(SimSpeed(world)).Append(',').Append(grid != null ? grid.OwedSeconds.ToString("0.00", Plain) : "").Append(',');
        sb.Append(world != null ? world.Weather.Kind.ToString() : "").Append(',').Append(world != null ? world.ActiveChunks.Count : 0).Append(',');
        sb.Append(Mathf.Max(1, Knockback.All.Count)).Append(',').Append(Quoted(Mods())).Append(',').Append(Quoted(what));
        return sb.ToString();
    }

    // The frame 95% of this second's beat. Sorting the list a second doesn't make garbage.
    float P95()
    {
        if (frameMs.Count == 0) return 0f;
        frameMs.Sort();
        return frameMs[(frameMs.Count - 1) * 95 / 100];
    }

    // The world's speed this second: the ticks it ran against the ticks due.
    string SimSpeed(World world)
    {
        float seconds = elapsedMs / 1000f;
        if (world == null || seconds <= 0f) return "";
        return Mathf.RoundToInt((world.Tick - ticksAtSecond) * 100f / (seconds * world.Rules.Sim.TicksPerSecond)).ToString(Plain);
    }

    string Average(float sum) => timed > 0 && sum > 0f ? Num(sum / timed) : "";

    static string Num(float value) => value.ToString("0.0", Plain);

    static string Quoted(string text) => $"\"{text.Replace("\"", "\"\"")}\"";

    World WorldNow()
    {
        if (grid == null) grid = ChemGrid.Current?.Grid;
        return grid != null ? grid.World : null;
    }

    /// <summary>The mods on, by the titles the room gives them, a mod Claude wrote too.</summary>
    public string Mods()
    {
        var world = WorldNow();
        return world?.Room == null ? "" : string.Join("; ", world.Room.Mods.Select(m => m.Title));
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
        if (sorted.Count == 0) return $"under {LoadingSeconds:0} s; {file.Path}";
        float low = sorted[Mathf.Min(sorted.Count - 1, sorted.Count / 100)];
        return $"average {Num(sorted.Average())} fps, 1% low {Num(low)}, worst second {Num(sorted[0])}, over {sorted.Count} s after loading; {hitches} hitches, {marks} marks; {file.Path}";
    }
}
