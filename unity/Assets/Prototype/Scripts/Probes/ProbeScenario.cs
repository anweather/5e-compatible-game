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
    const float SettleSeconds = 1f, MostDrawWaitSeconds = 30f;
    const int MaxErrorsKept = 20;
    protected const float MostGarbage = 8f;
    const int ReviewEdge = 480;

    public ProbeResult Result { get; } = new ProbeResult();
    public string ScreenshotDir { get; set; }
    public float Seconds { get; set; } = 10f;
    public bool RealClaude { get; set; }
    public int Prompts { get; set; }
    public string OutDir { get; set; }
    public string Model { get; set; }
    public string Context { get; set; }
    public string OnlyKind { get; set; }
    public string Replay { get; set; }
    public string Also { get; set; }
    public string SetFile { get; set; }
    public string Notes { get; set; }

    public string Harness { get; set; }

    public string BudgetFile { get; set; }

    public string PrefixTtl { get; set; }

    readonly List<float> frameMs = new();
    readonly Chem.FallWatch fall = new();
    readonly List<string> consoleLines = new();
    bool measuring;
    float measureStarted;
    int tickAtStart;

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
        for (float waited = 0f; grid.ChunksLeftToDraw > 0 && waited < MostDrawWaitSeconds; waited += Time.unscaledDeltaTime) yield return null;
        tickAtStart = grid.World.Tick;
        measureStarted = Time.realtimeSinceStartup;
        measuring = true;
        yield return Run();
        measuring = false;
        Check("fell", !fall.Fell, $"deepest under the ground {fall.Deepest:0.0} m");
        Summarise();
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

    public static bool IsUnitysQuickSearch(string stack) =>
        stack != null
        && stack.Contains("UnityEditor.Search.", System.StringComparison.Ordinal)
        && !stack.Contains("Assets/Prototype/", System.StringComparison.Ordinal);

    protected void Check(string name, bool pass, string detail = null) =>
        Result.Checks.Add(new ProbeCheck { Name = name, Pass = pass, Detail = detail });

    protected void Metric(string name, double value) => Result.Metrics[name] = System.Math.Round(value, 3);

    protected void StandOn((int x, int y) cell)
    {
        var p = grid.CellCenter(cell.x, cell.y);
        p.y = grid.GroundAt(p);
        Teleport(p + Vector3.up * 0.05f);
    }

    protected bool LoadElement(string element, bool held = false)
    {
        var command = ComboCommand.Load(caster.CastPlayer, element, held);
        var net = grid.GetComponent<NetChem>();
        if (net == null || !net.Online) return World.ApplyCombo(command);
        net.RequestCombo(command);
        return true;
    }

    protected void Shot(string label)
    {
        if (string.IsNullOrEmpty(ScreenshotDir)) return;
        System.IO.Directory.CreateDirectory(ScreenshotDir);
        var file = System.IO.Path.Combine(ScreenshotDir, $"{Result.Scenario}-{label}.png");
        Result.Screenshots.Add(file);
        StartCoroutine(Capture(file));
    }

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

    protected static bool TreeColliderAt(Vector3 at)
    {
        foreach (var hit in Physics.RaycastAll(at + Vector3.up * 120f, Vector3.down, 200f))
            if (hit.collider is TerrainCollider terrain && hit.point.y > terrain.GetComponent<Terrain>().SampleHeight(hit.point) + terrain.transform.position.y + 1f) return true;
        return false;
    }

    protected static void FixClock(bool on) => Time.captureDeltaTime = on ? 1f / 60f : 0f;

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

    protected void RadialPad() => wizard.GetComponent<PlayerControls>().ElementPad = false;

    protected int BurningCells() => World.AllCells().Count(c => c.Fire > 0);

    protected int ConsoleMark => consoleLines.Count;

    protected IEnumerable<string> ConsoleSince(int mark) => consoleLines.Skip(mark);

    protected bool ConsoleSaid(string text, int mark) =>
        consoleLines.Skip(mark).Any(l => l.Contains(text, System.StringComparison.Ordinal));

    protected IEnumerable<string> LogSince(int tick) =>
        World.Log.Where(e => e.Tick >= tick).Select(e => e.Text);

    protected IEnumerator CastFromBar(string spellId, int x, int y, int dir, float wait)
    {
        Check($"bar_has_{spellId}", caster.Select(spellId));
        var cell = grid.YardCell(x, y);
        puppet.Face(grid.CellCenter(cell.x, cell.y));
        caster.Cast(caster.SelectedId, cell.x, cell.y, dir);
        yield return Wait(wait);
    }

    protected void Look(int x, int y, float up, int atX, int atY, float atUp)
    {
        var from = grid.CellCenter(x, y);
        var to = grid.CellCenter(atX, atY);
        Cut(new Vector3(from.x, grid.GroundAt(from) + up, from.z), new Vector3(to.x, grid.GroundAt(to) + atUp, to.z));
    }

    protected IEnumerator WalkFor(int x, int y, float seconds)
    {
        var walk = puppet.WalkTo(Yard(x, y));
        for (float t = 0f; t < seconds && walk.MoveNext(); t += Time.deltaTime) yield return walk.Current;
        puppet.Move(Vector2.zero);
    }

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
    public int LogLines { get; set; }
}

public sealed class ProbeConsole
{
    public int ErrorCount { get; set; }
    public int WarningCount { get; set; }
    public int IgnoredCount { get; set; }
    public List<string> Errors { get; } = new();
}
