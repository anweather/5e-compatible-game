using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Chem;
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

    public ProbeResult Result { get; } = new ProbeResult();
    /// <summary>A folder to save screenshots in, or null for none. Set before Start.</summary>
    public string ScreenshotDir { get; set; }
    /// <summary>How long a timed scenario (perf) runs. Set before Start.</summary>
    public float Seconds { get; set; } = 10f;

    readonly List<float> frameMs = new();
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
        Summarise();
        // A screenshot is written at the end of the frame it was asked for.
        yield return null;
        yield return null;
    }

    void Update()
    {
        if (measuring) frameMs.Add(Time.unscaledDeltaTime * 1000f);
    }

    void OnLog(string message, string stack, LogType type)
    {
        consoleLines.Add(message);
        if (type == LogType.Warning) Result.Console.WarningCount++;
        if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
        Result.Console.ErrorCount++;
        if (Result.Console.Errors.Count >= MaxErrorsKept) return;
        var firstFrame = stack?.Split('\n').FirstOrDefault(l => l.Length > 0);
        Result.Console.Errors.Add(firstFrame != null ? $"{type}: {message} (at {firstFrame})" : $"{type}: {message}");
    }

    // --- for scenarios -------------------------------------------------------------

    protected void Check(string name, bool pass, string detail = null) =>
        Result.Checks.Add(new ProbeCheck { Name = name, Pass = pass, Detail = detail });

    protected void Metric(string name, double value) => Result.Metrics[name] = System.Math.Round(value, 3);

    /// <summary>Saves a screenshot of the Game view as &lt;ScreenshotDir&gt;/&lt;scenario&gt;-&lt;label&gt;.png, if asked for.</summary>
    protected void Shot(string label)
    {
        if (string.IsNullOrEmpty(ScreenshotDir)) return;
        System.IO.Directory.CreateDirectory(ScreenshotDir);
        var file = System.IO.Path.Combine(ScreenshotDir, $"{Result.Scenario}-{label}.png");
        ScreenCapture.CaptureScreenshot(file);
        Result.Screenshots.Add(file);
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
    public List<string> Errors { get; } = new();
}
