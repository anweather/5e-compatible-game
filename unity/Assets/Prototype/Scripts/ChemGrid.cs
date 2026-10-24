using System.Collections.Generic;
using System.IO;
using Chem;
using UnityEngine;

// The 3D view of a Chem.World: one block per cell, plus water, ice, covers,
// fire, gas and charge drawn on top. The rules come from ../rules/*.json,
// the same files the web sandbox runs on. Cell (x, y) sits at world
// (x, 0, H-1-y) times cellSize, so north (y-1) is +z.
public class ChemGrid : MonoBehaviour
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

    static readonly Dictionary<string, (Color color, float height)> TerrainLook = new()
    {
        ["bedrock"] = (new Color(0.22f, 0.21f, 0.23f), 1.5f), ["stone"] = (new Color(0.50f, 0.50f, 0.52f), 1.2f),
        ["pillar"] = (new Color(0.55f, 0.45f, 0.35f), 1.2f), ["rubble"] = (new Color(0.45f, 0.43f, 0.40f), 0.05f),
        ["dirt"] = (new Color(0.55f, 0.42f, 0.28f), 0f), ["grass"] = (new Color(0.36f, 0.62f, 0.27f), 0f),
        ["brush"] = (new Color(0.22f, 0.42f, 0.17f), 0.6f), ["tree"] = (new Color(0.15f, 0.36f, 0.14f), 2.2f),
        ["stump"] = (new Color(0.40f, 0.28f, 0.16f), 0.15f), ["wood"] = (new Color(0.62f, 0.44f, 0.24f), 0f),
        ["pit"] = (new Color(0.30f, 0.24f, 0.18f), 0f), ["metal"] = (new Color(0.62f, 0.66f, 0.72f), 0.02f),
    };

    sealed class View
    {
        public GameObject tile, water, cover, fire, gas, spark;
        public string terrain;
        public int key = int.MinValue;
    }

    View[] views;
    readonly Dictionary<string, Material> materials = new();
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

        views = new View[w * h];
        for (int i = 0; i < views.Length; i++) views[i] = new View();
        Render();
    }

    void Update()
    {
        tickTimer += Time.deltaTime;
        float tick = 1f / Mathf.Max(1, World.Rules.Sim.TicksPerSecond);
        int guard = 4;
        while (tickTimer >= tick && guard-- > 0)
        {
            tickTimer -= tick;
            World.Step();
        }
        Render();
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

    // --- rendering ---------------------------------------------------------

    void Render()
    {
        for (int y = 0; y < World.H; y++)
        for (int x = 0; x < World.W; x++)
        {
            var c = World.At(x, y);
            var v = views[y * World.W + x];
            int key = Key(c);
            if (key == v.key && v.terrain == c.T) continue;
            v.key = key;

            if (v.terrain != c.T) BuildTile(v, c, x, y);
            float ground = SurfaceY(x, y);
            var center = CellCenter(x, y);

            // Water and ice: a slab from the ground up to the water surface. Ice is solid to walk on.
            if (c.Water > 0)
            {
                float top = (World.ElevationUnits(c) + c.Water) / (float)World.Rules.Sim.WaterUnitsPerStep * stepHeight;
                top = Mathf.Max(top, ground + 0.04f);
                var slab = Ensure(ref v.water, "Water", center);
                slab.transform.position = new Vector3(center.x, (ground + top) / 2f, center.z);
                slab.transform.localScale = new Vector3(cellSize, top - ground, cellSize);
                slab.GetComponent<Renderer>().sharedMaterial = c.Frozen ? Mat("Ice", new Color(0.78f, 0.92f, 1f), 0.95f) : Mat("Water", new Color(0.2f, 0.45f, 0.8f), 0.9f);
                slab.GetComponent<Collider>().enabled = c.Frozen;
                ground = top;
            }
            else Hide(v.water);

            if (c.Cover != null)
            {
                var col = c.Cover == "oil" ? new Color(0.08f, 0.07f, 0.06f) : c.Cover == "grease" ? new Color(0.85f, 0.80f, 0.45f) : new Color(0.55f, 0.55f, 0.55f);
                var film = Ensure(ref v.cover, "Cover", center);
                film.transform.position = new Vector3(center.x, ground + 0.02f, center.z);
                film.transform.localScale = new Vector3(0.96f * cellSize, 0.03f, 0.96f * cellSize);
                film.GetComponent<Renderer>().sharedMaterial = Mat("Cover-" + c.Cover, col, c.Cover == "ash" ? 0.1f : 0.85f);
            }
            else Hide(v.cover);

            if (c.Fire > 0)
            {
                var flame = Ensure(ref v.fire, "Fire", center);
                if (flame.GetComponent<Flicker>() == null) flame.AddComponent<Flicker>();
                float s = 0.25f + 0.12f * c.Fire;
                flame.transform.localScale = new Vector3(s, s * 1.3f, s);
                flame.transform.position = new Vector3(center.x, ground + s * 0.65f, center.z);
                flame.GetComponent<Renderer>().sharedMaterial = Mat("Fire", new Color(1f, 0.38f, 0.05f), 0.2f, new Color(1f, 0.3f, 0.02f) * 1.2f);
            }
            else Hide(v.fire);

            if (c.Gas != null && c.GasAmt > 0)
            {
                var puff = Ensure(ref v.gas, "Gas", center);
                float s = 0.25f + 0.1f * c.GasAmt;
                puff.transform.localScale = Vector3.one * s;
                puff.transform.position = new Vector3(center.x, ground + 1.2f, center.z);
                puff.GetComponent<Renderer>().sharedMaterial = c.Gas == "steam" ? Mat("Steam", new Color(0.95f, 0.97f, 1f), 0.3f) : Mat("Smoke", new Color(0.3f, 0.3f, 0.32f), 0.1f);
            }
            else Hide(v.gas);

            if (c.Charge > 0)
            {
                var spark = Ensure(ref v.spark, "Charge", center);
                spark.transform.position = new Vector3(center.x, ground + 0.08f, center.z);
                spark.transform.localScale = new Vector3(0.8f * cellSize, 0.05f, 0.8f * cellSize);
                spark.GetComponent<Renderer>().sharedMaterial = Mat("Charge", new Color(1f, 0.95f, 0.3f), 0.5f, new Color(1f, 0.9f, 0.2f) * 6f);
            }
            else Hide(v.spark);
        }
    }

    static int Key(Cell c)
    {
        unchecked
        {
            int k = c.Water;
            k = k * 31 + (c.Frozen ? 1 : 0);
            k = k * 31 + (c.Cover?.GetHashCode() ?? 0);
            k = k * 31 + c.Fire;
            k = k * 31 + (c.Gas?.GetHashCode() ?? 0);
            k = k * 31 + c.GasAmt;
            k = k * 31 + (c.Charge > 0 ? 1 : 0);
            return k;
        }
    }

    void BuildTile(View v, Cell c, int x, int y)
    {
        if (v.tile != null) Destroy(v.tile);
        v.terrain = c.T;
        var look = Look(c.T);
        float top = SurfaceY(x, y);
        float bottom = -1.5f;
        var tile = GameObject.CreatePrimitive(PrimitiveType.Cube);
        tile.name = $"{c.T} {x},{y}";
        tile.transform.SetParent(transform, false);
        var c0 = CellCenter(x, y);
        tile.transform.position = new Vector3(c0.x, (top + bottom) / 2f, c0.z);
        tile.transform.localScale = new Vector3(cellSize, top - bottom, cellSize);
        tile.GetComponent<Renderer>().sharedMaterial = Mat("T-" + c.T, look.color, c.T == "metal" ? 0.8f : 0.15f);
        v.tile = tile;
    }

    (Color color, float height) Look(string terrain) =>
        TerrainLook.TryGetValue(terrain, out var l) ? l : (Color.magenta, 0f);

    GameObject Ensure(ref GameObject go, string name, Vector3 at)
    {
        if (go == null)
        {
            go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(transform, false);
            go.transform.position = at;
            if (name != "Water") Destroy(go.GetComponent<Collider>());
        }
        go.SetActive(true);
        return go;
    }

    static void Hide(GameObject go)
    {
        if (go != null && go.activeSelf) go.SetActive(false);
    }

    Material Mat(string name, Color color, float smoothness, Color? emission = null)
    {
        if (materials.TryGetValue(name, out var m)) return m;
        m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
        m.SetColor("_BaseColor", color);
        m.SetFloat("_Smoothness", smoothness);
        if (emission.HasValue)
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", emission.Value);
        }
        materials[name] = m;
        return m;
    }

    void OnDestroy()
    {
        foreach (var m in materials.Values) Destroy(m);
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
