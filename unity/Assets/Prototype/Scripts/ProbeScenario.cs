using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Chem;
using Unity.Profiling;
using UnityEngine;

// A scripted check for the fast loop (docs/FAST_LOOP.md): `unity command probe
// run` enters Play mode, adds one of these (ProbeScenarios), and writes its
// Result as JSON when it is Done. A scenario is a ShowcaseScript, so it places
// the wizard, casts and cuts the camera with the same helpers as the trailer
// showcases; it adds checks, numbers, the frame times, the chemistry ticks,
// console errors and screenshots.
public abstract class ProbeScenario : ShowcaseScript
{
    const float SettleSeconds = 1f;
    const int MaxErrorsKept = 20;
    /// <summary>Bytes a call that counts as none: half the smallest object there is. Stray allocations in a frame came to 0.6 at most.</summary>
    protected const float MostGarbage = 8f;
    // The long edge of a shot's review copy (.claude/skills/screenshot-budget).
    const int ReviewEdge = 480;

    public ProbeResult Result { get; } = new ProbeResult();
    /// <summary>A folder to save screenshots in, or null for none. Set before Start.</summary>
    public string ScreenshotDir { get; set; }
    /// <summary>How long a timed scenario (perf) runs. Set before Start.</summary>
    public float Seconds { get; set; } = 10f;
    /// <summary>chatprompts only: spend real prompts on the signed-in Claude. Off, a stand-in answers. Set before Start.</summary>
    public bool RealClaude { get; set; }
    /// <summary>chatprompts only: how many of the set to send; 0 for all of it, and never over its cap. Set before Start.</summary>
    public int Prompts { get; set; }
    /// <summary>chatprompts only: the folder for this run's records and raw streams. Set before Start.</summary>
    public string OutDir { get; set; }
    /// <summary>chatprompts only: the model to ask claude for; empty leaves it to the account, as the game does. Set before Start.</summary>
    public string Model { get; set; }
    /// <summary>chatprompts only: "bare" starts claude without the player's own skills, plugins and commands; empty, as the game does. Set before Start.</summary>
    public string Context { get; set; }
    /// <summary>chatprompts only: send just the prompt of this kind. Set before Start.</summary>
    public string OnlyKind { get; set; }
    /// <summary>chatprompts only: earlier runs' folders, comma-separated, to cast their spells from their records, spending nothing. Set before Start.</summary>
    public string Replay { get; set; }
    /// <summary>chatprompts only: more arguments for claude, split on semicolons, for this run alone (a short system prompt, an effort). Set before Start.</summary>
    public string Also { get; set; }
    /// <summary>chatprompts only: the prompts file in docs/telemetry to send in place of the fixed set. Set before Start.</summary>
    public string SetFile { get; set; }
    /// <summary>chatprompts only: "off" leaves notes.md out of the message; empty, as the game does. Set before Start.</summary>
    public string Notes { get; set; }

    /// <summary>chatprompts only: api answers with the game's own loop in place of Claude Code.</summary>
    public string Harness { get; set; }

    /// <summary>chatprompts only: the file holding the API's running total; real money won't start without one.</summary>
    public string BudgetFile { get; set; }

    /// <summary>chatprompts only: how long the API loop's prefix stays cached, 1h or 5m.</summary>
    public string PrefixTtl { get; set; }

    readonly List<float> frameMs = new();
    readonly Chem.FallWatch fall = new();
    readonly List<string> consoleLines = new();
    bool measuring;
    float measureStarted;
    int tickAtStart;

    /// <summary>The scenario itself, run once the scene has settled; frames are measured while it runs.</summary>
    protected abstract IEnumerator Run();

    void Awake() => Application.logMessageReceived += OnLog;

    protected override void OnDestroy()
    {
        Application.logMessageReceived -= OnLog;
        FixClock(false);
        base.OnDestroy();
    }

    protected override IEnumerator Play()
    {
        yield return Wait(SettleSeconds);
        tickAtStart = grid.World.Tick;
        measureStarted = Time.realtimeSinceStartup;
        measuring = true;
        yield return Run();
        measuring = false;
        Check("fell", !fall.Fell, $"deepest under the ground {fall.Deepest:0.0} m");
        Summarise();
        // A screenshot is written at the end of the frame it was asked for.
        yield return null;
        yield return null;
    }

    void Update()
    {
        if (!measuring) return;
        frameMs.Add(Time.unscaledDeltaTime * 1000f);
        if (wizard != null) fall.Feed(grid.GroundAt(wizard.transform.position) - wizard.transform.position.y, Time.deltaTime);
    }

    void OnLog(string message, string stack, LogType type)
    {
        consoleLines.Add(message);
        if (type == LogType.Warning) Result.Console.WarningCount++;
        if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
        if (IsUnitysQuickSearch(stack))
        {
            Result.Console.IgnoredCount++;
            return;
        }
        Result.Console.ErrorCount++;
        if (Result.Console.Errors.Count >= MaxErrorsKept) return;
        var firstFrame = stack?.Split('\n').FirstOrDefault(l => l.Length > 0);
        Result.Console.Errors.Add(firstFrame != null ? $"{type}: {message} (at {firstFrame})" : $"{type}: {message}");
    }

    /// <summary>
    /// Unity's own QuickSearch throwing as it indexes after a start or a reload (docs/perf/notes-2026-10-31-mac.md):
    /// its stack runs through UnityEditor.Search with no frame of ours, so it says nothing about the game.
    /// </summary>
    public static bool IsUnitysQuickSearch(string stack) =>
        stack != null
        && stack.Contains("UnityEditor.Search.", System.StringComparison.Ordinal)
        && !stack.Contains("Assets/Prototype/", System.StringComparison.Ordinal);

    // --- for scenarios -------------------------------------------------------------

    protected void Check(string name, bool pass, string detail = null) =>
        Result.Checks.Add(new ProbeCheck { Name = name, Pass = pass, Detail = detail });

    protected void Metric(string name, double value) => Result.Metrics[name] = System.Math.Round(value, 3);

    /// <summary>Saves a screenshot of the Game view as &lt;ScreenshotDir&gt;/&lt;scenario&gt;-&lt;label&gt;.png, if asked for, and a small copy beside it as .480.png.</summary>
    protected void Shot(string label)
    {
        if (string.IsNullOrEmpty(ScreenshotDir)) return;
        System.IO.Directory.CreateDirectory(ScreenshotDir);
        var file = System.IO.Path.Combine(ScreenshotDir, $"{Result.Scenario}-{label}.png");
        Result.Screenshots.Add(file);
        StartCoroutine(Capture(file));
    }

    // At the frame's end, the Game view: the shot, and beside it the copy with ReviewEdge on its long
    // edge, the one the worker opens to review. Both from one picture: ScreenCapture.CaptureScreenshot
    // wrote black alongside CaptureScreenshotAsTexture in the same frame.
    static IEnumerator Capture(string file)
    {
        yield return new WaitForEndOfFrame();
        var shown = ScreenCapture.CaptureScreenshotAsTexture();
        int width = shown.width, height = shown.height;
        var from = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        Graphics.Blit(shown, from);
        Destroy(shown);
        SavePicture(from, file);
        float shrink = (float)ReviewEdge / Mathf.Max(width, height);
        int smallWidth = Mathf.Max(1, Mathf.RoundToInt(width * shrink)), smallHeight = Mathf.Max(1, Mathf.RoundToInt(height * shrink));
        // Halved in steps: one bilinear blit of more than half skips pixels, and thin things break up.
        while (width > smallWidth || height > smallHeight)
        {
            (width, height) = (Mathf.Max(width / 2, smallWidth), Mathf.Max(height / 2, smallHeight));
            var half = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(from, half);
            RenderTexture.ReleaseTemporary(from);
            from = half;
        }
        SavePicture(from, System.IO.Path.ChangeExtension(file, $"{ReviewEdge}.png"));
        RenderTexture.ReleaseTemporary(from);
    }

    static void SavePicture(RenderTexture picture, string file)
    {
        var active = RenderTexture.active;
        RenderTexture.active = picture;
        var copy = new Texture2D(picture.width, picture.height, TextureFormat.RGB24, false);
        copy.ReadPixels(new Rect(0, 0, picture.width, picture.height), 0, 0);
        RenderTexture.active = active;
        System.IO.File.WriteAllBytes(file, copy.EncodeToPNG());
        Destroy(copy);
    }

    /// <summary>Runs game time at 60 frames a second however long a frame really takes, so a beat or a stop counts the same frames every run; off, the real clock.</summary>
    protected static void FixClock(bool on) => Time.captureDeltaTime = on ? 1f / 60f : 0f;

    /// <summary>
    /// The managed bytes a call makes, by the profiler's count of a frame's allocations: the least of some frames with the
    /// calls, less the least of some without, over the calls. Negative if the count isn't working. GC.GetAllocatedBytesForCurrentThread
    /// reads 0 in Unity, and the heap's size moves in blocks: Unity's threads move it at any moment, and 7 KB made in a round doesn't.
    /// </summary>
    protected static IEnumerator GarbagePerCall(System.Action call, int times, System.Action<float> measured)
    {
        const int Frames = 8, Calibration = 16 * 1024;
        using var allocated = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
        yield return null;
        yield return null;
        var made = new byte[Calibration];
        yield return null;
        bool counting = allocated.LastValue >= made.Length;
        long without = long.MaxValue, with = long.MaxValue;
        for (int frame = 0; frame < Frames; frame++)
        {
            yield return null;
            without = System.Math.Min(without, allocated.LastValue);
            for (int i = 0; i < times; i++) call();
            yield return null;
            with = System.Math.Min(with, allocated.LastValue);
        }
        measured(counting ? (float)System.Math.Max(0L, with - without) / times : -1f);
    }

    /// <summary>The controller's debug layout, for a scenario that drives the radial's buttons.</summary>
    protected void RadialPad() => wizard.GetComponent<PlayerControls>().ElementPad = false;

    protected int BurningCells() => World.Cells.Count(c => c.Fire > 0);

    /// <summary>How many console lines there have been so far, to pass to ConsoleSaid later.</summary>
    protected int ConsoleMark => consoleLines.Count;

    /// <summary>The console lines since <paramref name="mark"/>.</summary>
    protected IEnumerable<string> ConsoleSince(int mark) => consoleLines.Skip(mark);

    /// <summary>Whether a console line since <paramref name="mark"/> contains <paramref name="text"/> (EventLog writes there).</summary>
    protected bool ConsoleSaid(string text, int mark) =>
        consoleLines.Skip(mark).Any(l => l.Contains(text, System.StringComparison.Ordinal));

    /// <summary>The chemistry log's lines (World.Log) written or repeated since a tick.</summary>
    protected IEnumerable<string> LogSince(int tick) =>
        World.Log.Where(e => e.Tick >= tick).Select(e => e.Text);

    /// <summary>Selects the spell on the bar (SpellCaster.Select) and casts it at a yard cell, then waits.</summary>
    protected IEnumerator CastFromBar(string spellId, int x, int y, int dir, float wait)
    {
        Check($"bar_has_{spellId}", caster.Select(spellId));
        var cell = grid.YardCell(x, y);
        puppet.Face(grid.CellCenter(cell.x, cell.y));
        caster.Cast(caster.SelectedId, cell.x, cell.y, dir);
        yield return Wait(wait);
    }

    /// <summary>Walks the wizard toward a yard cell for at most <paramref name="seconds"/> of game time.</summary>
    protected IEnumerator WalkFor(int x, int y, float seconds)
    {
        var walk = puppet.WalkTo(Yard(x, y));
        for (float t = 0f; t < seconds && walk.MoveNext(); t += Time.deltaTime) yield return walk.Current;
        puppet.Move(Vector2.zero);
    }

    /// <summary>The dry, open cell outside the yard nearest its middle with lake water two cells south.</summary>
    protected bool FindShore(out Vector2Int shore)
    {
        var yard = grid.yardCells;
        var middle = new Vector2Int(yard.x + yard.width / 2, yard.y + yard.height / 2);
        shore = default;
        float best = float.MaxValue;
        for (int y = 0; y < World.H - 3; y++)
            for (int x = 0; x < World.W; x++)
            {
                var c = World.At(x, y);
                if (yard.Contains(new Vector2Int(x, y)) || c.Water > 0 || World.IsBlocking(c) || World.At(x, y + 2).Water == 0) continue;
                float d = (new Vector2Int(x, y) - middle).sqrMagnitude;
                if (y > middle.y && d < best)
                {
                    best = d;
                    shore = new Vector2Int(x, y);
                }
            }
        return best < float.MaxValue;
    }

    /// <summary>The cells a spell would touch, from yard coordinates, in world cells.</summary>
    protected List<(int x, int y)> SpellCells(string spellId, int x, int y, int dir)
    {
        var cell = grid.YardCell(x, y);
        return World.SpellCells(World.Rules.Spell(spellId), cell.x, cell.y, dir);
    }

    void Summarise()
    {
        var stats = FrameStats.Of(frameMs);
        Result.Frames = new ProbeFrames
        {
            Count = stats.Count,
            Seconds = (float)System.Math.Round(Time.realtimeSinceStartup - measureStarted, 2),
            MeanMs = (float)System.Math.Round(stats.MeanMs, 2),
            P95Ms = (float)System.Math.Round(stats.P95Ms, 2),
            WorstMs = (float)System.Math.Round(stats.WorstMs, 2),
        };
        Result.Chem = new ProbeChem
        {
            TickStart = tickAtStart,
            TickEnd = World.Tick,
            BurningCells = BurningCells(),
            LogLines = World.LogAppended,
        };
        Check("no_console_errors", Result.Console.ErrorCount == 0, $"{Result.Console.ErrorCount} errors");
        Result.Pass = Result.Checks.All(c => c.Pass);
    }
}

/// <summary>What a probe scenario found. The editor writes it as JSON (snake_case keys).</summary>
public sealed class ProbeResult
{
    public string Scenario { get; set; }
    public bool Pass { get; set; }
    public List<ProbeCheck> Checks { get; } = new();
    public SortedDictionary<string, double> Metrics { get; } = new();
    public ProbeFrames Frames { get; set; }
    public ProbeChem Chem { get; set; }
    public ProbeConsole Console { get; } = new();
    public List<string> Screenshots { get; } = new();
}

public sealed class ProbeCheck
{
    public string Name { get; set; }
    public bool Pass { get; set; }
    public string Detail { get; set; }
}

/// <summary>Frame times over the scenario, in Editor Play mode (not a player build: the Editor's own drawing is in them).</summary>
public sealed class ProbeFrames
{
    public string Source { get; } = "editor_play_mode";
    public int Count { get; set; }
    public float Seconds { get; set; }
    public float MeanMs { get; set; }
    public float P95Ms { get; set; }
    public float WorstMs { get; set; }
}

public sealed class ProbeChem
{
    public int TickStart { get; set; }
    public int TickEnd { get; set; }
    public int Ticks => TickEnd - TickStart;
    public int BurningCells { get; set; }
    /// <summary>World.LogAppended at the end: chemistry log lines since the world was built.</summary>
    public int LogLines { get; set; }
}

public sealed class ProbeConsole
{
    public int ErrorCount { get; set; }
    public int WarningCount { get; set; }
    /// <summary>Errors of Unity's own QuickSearch, left out of ErrorCount.</summary>
    public int IgnoredCount { get; set; }
    public List<string> Errors { get; } = new();
}
