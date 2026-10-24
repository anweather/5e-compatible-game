using System.Collections.Generic;
using System.IO;
using Chem;
using UnityEngine;

// The 3D view of a Chem.World. The rules come from ../rules/*.json, the
// same files the web sandbox runs on; this side only ticks the world and
// draws it (see ChemGrid.Visuals.cs). Cell (x, y) sits at world
// (x, 0, H-1-y) times cellSize, so north (y-1) is +z.
public partial class ChemGrid : MonoBehaviour
{
    [TextArea(8, 20)] public string map;
    public uint seed = 1;
    [Tooltip("World height of one step of elevation (a pit is one step deep).")]
    public float stepHeight = 0.5f;
    [Tooltip("World width of one cell. A 5 ft square is about 1.5 m.")]
    public float cellSize = 1.5f;

    public World World { get; private set; }
    public Vector2Int PlayerStart { get; private set; }

    // One character per cell, the same legend as the web sandbox's scenarios.
    static readonly Dictionary<char, (string t, int water, bool frozen, string cover)> Legend = new()
    {
        ['%'] = ("bedrock", 0, false, null), ['#'] = ("stone", 0, false, null),
        ['.'] = ("dirt", 0, false, null), [','] = ("grass", 0, false, null),
        ['"'] = ("brush", 0, false, null), ['T'] = ("tree", 0, false, null),
        ['='] = ("wood", 0, false, null), ['_'] = ("pit", 0, false, null),
        ['w'] = ("pit", 4, false, null), ['*'] = ("pit", 4, true, null),
        ['~'] = ("dirt", 2, false, null), ['m'] = ("metal", 0, false, null),
        ['o'] = ("dirt", 0, false, "oil"), ['@'] = ("grass", 0, false, null),
    };

    float tickTimer;
    LogEntry lastLog;
    int lastLogCount;

    void Awake()
    {
        var rulesDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../rules"));
        var rules = Rules.Load(rulesDir);

        var rows = map.Replace("\r", "").Trim('\n').Split('\n');
        int h = rows.Length, w = 0;
        foreach (var r in rows) w = Mathf.Max(w, r.Length);
        World = new World(rules, w, h, seed);
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            char ch = x < rows[y].Length ? rows[y][x] : '.';
            if (!Legend.TryGetValue(ch, out var spec)) spec = Legend['.'];
            if (ch == '@') PlayerStart = new Vector2Int(x, y);
            World.Paint(x, y, spec.t);
            if (spec.water > 0) World.AddWater(x, y, spec.water);
            if (spec.frozen) World.At(x, y).Frozen = true;
            if (spec.cover != null) World.SetCover(x, y, spec.cover);
        }

        InitVisuals();
        Render();
    }

    void Update()
    {
        tickTimer += Time.deltaTime;
        float tick = 1f / Mathf.Max(1, World.Rules.Sim.TicksPerSecond);
        int guard = 4;
        bool stepped = false;
        while (tickTimer >= tick && guard-- > 0)
        {
            tickTimer -= tick;
            World.Step();
            stepped = true;
        }
        Render();
        if (stepped) UpdateFireLights();
        FlickerFireLights();
        PumpLog();
    }

    // --- coordinates -----------------------------------------------------

    public Vector3 CellCenter(int x, int y) => new Vector3(x * cellSize, 0f, (World.H - 1 - y) * cellSize);

    public Vector2Int CellAt(Vector3 p) =>
        new Vector2Int(Mathf.RoundToInt(p.x / cellSize), World.H - 1 - Mathf.RoundToInt(p.z / cellSize));

    public float SurfaceY(int x, int y)
    {
        var c = World.At(x, y);
        return c == null ? 0f : World.ElevationUnits(c) / (float)World.Rules.Sim.WaterUnitsPerStep * stepHeight + Look(c.T).height;
    }

    public bool IsSlipperyAt(Vector3 p)
    {
        var cell = CellAt(p);
        var c = World.At(cell.x, cell.y);
        return c != null && World.IsSlippery(c);
    }

    // --- log ---------------------------------------------------------------

    // Forward new entries from the world's log to the on-screen log.
    void PumpLog()
    {
        var log = World.Log;
        if (log.Count == 0) return;
        int start = 0;
        if (lastLog != null)
        {
            int at = -1;
            for (int i = log.Count - 1; i >= 0; i--)
                if (ReferenceEquals(log[i], lastLog)) { at = i; break; }
            if (at >= 0)
            {
                if (lastLog.Count != lastLogCount) EventLog.Add($"{lastLog.Text} (×{lastLog.Count})", lastLog.Text);
                start = at + 1;
            }
        }
        for (int i = start; i < log.Count; i++) EventLog.Add(log[i].Count > 1 ? $"{log[i].Text} (×{log[i].Count})" : log[i].Text, log[i].Text);
        lastLog = log[log.Count - 1];
        lastLogCount = lastLog.Count;
    }
}
