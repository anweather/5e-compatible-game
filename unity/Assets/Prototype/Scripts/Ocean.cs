using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// The map's sea (docs/WORLD_LIFE.md): one mesh at the map's water level from
// the far side of the map out to the horizon, finely spaced over the map and the
// near sea and coarser far out, drawn with the Ocean shader, and kept only over
// the water and a beach's width round it: the sea, where the map's land is low and
// reaches its edge, and the basins too (a cove or a hollow cut into the land) on a
// map that doesn't dress a lake of its own (MapLand, SeaMask). The level, the way
// out to sea and the shore map come from the map, so a second map has its own sea. Each vertex
// carries the water's depth there, sampled from the terrain when the mesh is
// built (and a guess past the terrain's edge), so the shader can grow the
// swell over the shoals, break it into foam and wash it up the sand. The swell
// runs toward the nearest land, along the shore map (ShoreMap) the shader
// samples. Built when the scene loads, in the Editor too; only its settings are saved.
// Looks only: the sea's cells are the lake's kind of water (ValleyBuilder),
// and the currents that push swimmers live in WaterCurrents. WaveOffset works
// out the swell's rise and fall at a point the same way the shader does, so a
// swimmer (SurfaceEffects) rides the waves they see. On a generated map the fine sea is laid in tiles
// round the drawn tier and a coarse sea past them (Ocean.Tiles.cs, #214), so its cost never follows the map's size.
[ExecuteAlways]
public partial class Ocean : MonoBehaviour
{
    public Material material;
    [Tooltip("The sea's surface height, from the map (MapLand): the lake's, so the cells' water is one level.")]
    public float level = -0.6f;
    [Tooltip("The way to the nearest land over the sea, and how far off it is.")]
    public ShoreMap shoreMap;
    [Tooltip("A point on the coastline (x, z).")]
    public Vector2 shoreOrigin;
    [Tooltip("Out to sea, square to the coast (a direction on the ground plane).")]
    public Vector2 seaward = new(0.707f, -0.707f);
    [Tooltip("How far along the coast the sea reaches each way, how far out, and how far inland of the shore a beach's water runs (m).")]
    public float alongHalf = 12000f, outFar = 12000f, inland = 45f;
    [Tooltip("Spacing over the beaches and the near sea (m), and how far each way it holds.")]
    public float fineStep = 2.5f, fineAlong = 430f, fineOut = 330f;

    /// <summary>The sea in the scene, if there is one.</summary>
    public static Ocean Instance { get; private set; }

    /// <summary>The rules switched: the sea's colours, tinted from its art (ChemGrid.WorldLook.cs).</summary>
    public void Recolour(Color tint)
    {
        this.tint = tint;
        foreach (var r in GetComponentsInChildren<MeshRenderer>())
        {
            var block = new MaterialPropertyBlock();
            r.GetPropertyBlock(block);
            block.SetColor(DeepId, r.sharedMaterial.GetColor(DeepId) * tint);
            block.SetColor(ShallowId, r.sharedMaterial.GetColor(ShallowId) * tint);
            r.SetPropertyBlock(block);
        }
    }

    static readonly int DeepId = Shader.PropertyToID("_Deep"), ShallowId = Shader.PropertyToID("_Shallow");

    static readonly int ShoreDirId = Shader.PropertyToID("_ShoreDir"), SwellId = Shader.PropertyToID("_Swell"),
        WavelengthId = Shader.PropertyToID("_Wavelength"), SpeedId = Shader.PropertyToID("_Speed");

    Mesh mesh;
    GameObject surface;
    MapLand builtFor;

    void OnEnable()
    {
        Instance = this;
        Build();
    }

    // The map changed (a host's map): its own sea. On a generated map the tiles follow the drawn tier.
    static readonly Unity.Profiling.ProfilerMarker DIAG_UpdateMarker = new("Ocean.Update");
    void Update()
    {
        using var diagMarked = DIAG_UpdateMarker.Auto();
        if (MapLand.Current != builtFor) Build();
        else if (tiled) StreamTiles();
    }

    void OnDisable()
    {
        if (Instance == this) Instance = null;
        Clear();
    }

    void Clear()
    {
        if (surface != null) DestroyNow(surface);
        surface = null;
        ReleaseTiles(destroy: true);
        ClearCoarse();
    }

    /// <summary>Builds the sea's mesh again (after the terrain under it changed shape, or the map did).</summary>
    public void Build()
    {
        var map = MapLand.Current;
        bool wantTiles = WantsTiles();
        // A generated map's coarse sea reads no terrain: it stays, and only the tiles are measured again.
        if (!(wantTiles && tiled && map == coarseFor && coarse != null)) Clear();
        terrains = null;
        builtFor = map;
        tiled = wantTiles;
        if (builtFor != null) FromMap(builtFor);
        if (tiled)
        {
            BuildTiled();
            return;
        }
        var (us, vs) = Grid(fineStep);
        mesh = Lay(us, vs, Depth, "Ocean");
        if (mesh != null) surface = Surface("Sea surface", mesh, material, swell: true);
    }

    // The sea's grid, along the coast and out from it: `step` apart over the map and the near sea, growing past them.
    (List<float> us, List<float> vs) Grid(float step)
    {
        var out_ = seaward.normalized;
        var along = Vector2.Perpendicular(out_);
        var (farAlong, farInland) = MapSpan(out_, along, step);
        float fineSide = Mathf.Max(fineAlong, farAlong), back = Mathf.Max(inland, farInland);
        return (Stops(-alongHalf, alongHalf, -fineSide, fineSide, step), Stops(-back, outFar, -back, fineOut, step));
    }

    // A mesh over a grid's squares with a wet corner (us along the coast and vs out from it; with `square`, x and z), its
    // points numbered as they're first used, each carrying the water's depth there; null where no square is wet. Laid
    // into `reuse` when given.
    Mesh Lay(List<float> us, List<float> vs, System.Func<Vector2, float, float> depth, string name, Mesh reuse = null, bool square = false)
    {
        var out_ = seaward.normalized;
        var along = Vector2.Perpendicular(out_);
        var points = new Vector2[us.Count * vs.Count];
        var wet = new bool[points.Length];
        int n = 0;
        foreach (float v in vs)
            foreach (float u in us)
            {
                points[n] = square ? new Vector2(u, v) : shoreOrigin + along * u + out_ * v;
                wet[n] = Wet(points[n]);
                n++;
            }
        var index = new int[points.Length];
        System.Array.Fill(index, -1);
        var positions = new List<Vector3>();
        var water = new List<Vector2>();
        var triangles = new List<int>();
        int Use(int k)
        {
            if (index[k] >= 0) return index[k];
            var xz = points[k];
            index[k] = positions.Count;
            positions.Add(new Vector3(xz.x, level, xz.y) - transform.position);
            water.Add(new Vector2(depth(xz, Vector2.Dot(xz - shoreOrigin, out_)), 0f));
            return index[k];
        }
        for (int j = 0; j + 1 < vs.Count; j++)
            for (int i = 0; i + 1 < us.Count; i++)
            {
                int a = j * us.Count + i, b = a + 1, c = a + us.Count, d = c + 1;
                if (!(wet[a] || wet[b] || wet[c] || wet[d])) continue;
                int ia = Use(a), ib = Use(b), ic = Use(c), id = Use(d);
                // Wound to face up either way: along and out turn the other way round from x and z.
                if (square)
                {
                    (ib, ic) = (ic, ib);
                }
                triangles.Add(ia);
                triangles.Add(ib);
                triangles.Add(ic);
                triangles.Add(ib);
                triangles.Add(id);
                triangles.Add(ic);
            }
        if (triangles.Count == 0) return null;
        var m = reuse != null ? reuse : new Mesh { indexFormat = IndexFormat.UInt32, hideFlags = HideFlags.DontSave };
        m.name = name;
        m.SetVertices(positions);
        m.SetUVs(1, water);
        m.SetTriangles(triangles, 0);
        var up = new Vector3[positions.Count];
        System.Array.Fill(up, Vector3.up);
        m.normals = up;
        // The swell lifts the surface: keep it from being culled at the edges of the view.
        var bounds = m.bounds;
        bounds.Expand(new Vector3(4f, 4f, 4f));
        m.bounds = bounds;
        return m;
    }

    // A surface of the sea: a mesh drawn with a material, the shore map and the swell (or none).
    GameObject Surface(string name, Mesh m, Material with, bool swell)
    {
        var go = new GameObject(name) { hideFlags = HideFlags.DontSave | HideFlags.NotEditable };
        go.transform.SetParent(transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = m;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = with;
        r.shadowCastingMode = ShadowCastingMode.Off;
        Look(r, swell);
        return go;
    }

    // A surface's shore map, its swell (none on the coarse sea) and the rules' tint.
    void Look(MeshRenderer r, bool swell)
    {
        if (shoreMap == null && swell && tint == Color.white) return;
        var block = new MaterialPropertyBlock();
        if (shoreMap != null)
        {
            block.SetTexture(ShoreMapId, shoreMap.texture);
            block.SetVector(ShoreMapRectId, shoreMap.Rect);
            block.SetVector(ShoreDirId, ShoreDir);
        }
        if (!swell) block.SetFloat(SwellId, 0f);
        if (tint != Color.white)
        {
            block.SetColor(DeepId, r.sharedMaterial.GetColor(DeepId) * tint);
            block.SetColor(ShallowId, r.sharedMaterial.GetColor(ShallowId) * tint);
        }
        r.SetPropertyBlock(block);
    }

    static readonly int ShoreMapId = Shader.PropertyToID("_ShoreMap"), ShoreMapRectId = Shader.PropertyToID("_ShoreMapRect");

    // The swell's way: in from the sea.
    Vector4 ShoreDir => new(-seaward.normalized.x, -seaward.normalized.y, 0f, 0f);

    // The map's level, the way its swell comes in, its shore map, and which of its cells the surface spans.
    void FromMap(MapLand map)
    {
        level = map.WaterLevel;
        shoreOrigin = map.Shore;
        seaward = map.Seaward;
        shoreMap = map.ShoreMap;
        // Once a map (it's a pass over every cell), kept a bit a cell: on the Scale Island 40 million cells.
        if (wetFor == map && wetInland == inland) return;
        wetCells = new System.Collections.BitArray(map.Sea.Near(Mathf.CeilToInt(inland / map.Layout.CellSize), basins: DrawsBasins(map)));
        wetFor = map;
        wetInland = inland;
    }

    // Whether the sea also spans the map's basins (hollows below its level, walled off from the sea): only on a map that
    // neither dresses a lake of its own nor is generated. A generated map's cells hold its lakes' and ponds' water, which
    // the cells' own still surface draws; the sea over them too made a pond rise and fall with the swell (the Scale
    // Island's playtest, 2026-11-11).
    static bool DrawsBasins(MapLand map) =>
        System.Array.IndexOf(map.Map.Dresses, "lake") < 0 && !Chem.GeneratedMap.IsGenerated(ChemGrid.MapSource);

    System.Collections.BitArray wetCells;
    MapLand wetFor;
    float wetInland;

    // Whether the surface spans a point: over the map, near its water; off it, where the map's edge is sea.
    bool Wet(Vector2 xz)
    {
        var map = builtFor;
        if (map == null || wetCells == null) return Vector2.Dot(xz - shoreOrigin, seaward.normalized) > -inland;
        var c = map.CellOf(xz);
        if (c.x < 0 || c.y < 0 || c.x >= map.Map.W || c.y >= map.Map.H) return map.Sea.SeaCells > 0;
        return wetCells[c.y * map.Map.W + c.x];
    }

    // How far along the coast either way, and how far inland of it, the map's corners are (m): the fine mesh spans them all.
    (float along, float inland) MapSpan(Vector2 out_, Vector2 along, float step)
    {
        if (builtFor == null) return (0f, 0f);
        var layout = builtFor.Layout;
        float most = 0f, back = 0f;
        for (int k = 0; k < 4; k++)
        {
            var (x, z) = layout.ChunkCorner(k % 2 == 0 ? 0 : layout.ChunksX, k < 2 ? 0 : layout.ChunksZ);
            var d = new Vector2(x, z) - shoreOrigin;
            most = Mathf.Max(most, Mathf.Abs(Vector2.Dot(d, along)));
            back = Mathf.Max(back, -Vector2.Dot(d, out_));
        }
        // On whole steps from the shore, as the near sea's are.
        return (Mathf.Ceil(most / step) * step, Mathf.Ceil(back / step) * step);
    }

    /// <summary>Whether a point is under the sea's mesh: over the map's water or a beach's width from it, or off the map by the sea.</summary>
    public bool Covers(Vector2 xz) => mesh != null && Wet(xz);

    /// <summary>
    /// How far the swell lifts (or drops) the sea's surface above its level at a point, now: the
    /// shader's four Gerstner waves (Ocean.shader, Vert), on the same clock (the time since the
    /// scene loaded), from the material's settings and the shore map. Their sideways sway is left out, and so are
    /// the swash and the breakers, which only rise over the last metre or two of the beach.
    /// </summary>
    public float WaveOffset(Vector2 xz)
    {
        if (material == null || shoreMap == null) return 0f;
        float depth = Depth(xz, Vector2.Dot(xz - shoreOrigin, seaward.normalized));
        Vector4 dir = ShoreDir;
        float across = Vector2.Dot(Vector2.Perpendicular(new Vector2(dir.x, dir.y).normalized), xz);
        var (_, distance, reach) = shoreMap.At(xz);
        float swell = material.GetFloat(SwellId), wavelength = material.GetFloat(WavelengthId), speed = material.GetFloat(SpeedId);
        float t = Time.timeSinceLevelLoad;
        float shoal = Mathf.Lerp(1f, 2.4f, Mathf.Clamp01((5.5f - depth) / 3.5f)) * Mathf.Clamp01(depth / 0.5f + 0.1f);
        float shoreward = -distance;
        return reach * (Wave(shoreward, wavelength, swell * shoal, speed, t)
            + Wave(shoreward * 0.944f + across * 0.33f, wavelength * 0.61f, swell * 0.45f * shoal, speed * 0.8f, t)
            + Wave(shoreward * 0.894f - across * 0.447f, wavelength * 0.37f, swell * 0.22f, speed * 0.6f, t)
            + Wave(across * 0.981f + shoreward * 0.196f, wavelength * 2.3f, swell * 0.35f * Mathf.Clamp01(distance / 60f), speed * 1.3f, t));
    }

    // One Gerstner wave's rise at a point, given how far along the wave's way the point is (the
    // shader's Gerstner, height only).
    static float Wave(float along, float wavelength, float height, float speed, float t)
    {
        float k = 2f * Mathf.PI / wavelength;
        return height * 0.5f * Mathf.Sin(k * (along - speed * t));
    }

    // The water's depth at a point: the sea level less the terrain there; past the terrain, a bed
    // shelving away from the coast. Tiled, the ground drawn there (MapTerrains.HeightAt: its terrain's, or its land's
    // where none is made), as the tile under it was measured.
    float Depth(Vector2 xz, float outward)
    {
        var p = new Vector3(xz.x, 0f, xz.y);
        if (tiled)
        {
            var ground = MapTerrains.Instance != null ? MapTerrains.Instance.HeightAt(p) : null;
            return ground is { } g ? level - g : Shelf(outward);
        }
        var terrain = (terrains ??= new TerrainTiles()).At(p);
        if (terrain != null) return level - (terrain.SampleHeight(p) + terrain.transform.position.y);
        return Shelf(outward);
    }

    // A sea bed shelving away from the coast, `outward` metres out to sea of it.
    static float Shelf(float outward) => Mathf.Clamp(3f + outward * 0.12f, 3f, 60f);

    TerrainTiles terrains;

    // Coordinates from lo to hi: `fine` apart between fineLo and fineHi, growing by a sixth each
    // step beyond them.
    static List<float> Stops(float lo, float hi, float fineLo, float fineHi, float fine)
    {
        var stops = new List<float>();
        float x = Mathf.Max(lo, fineLo);
        var below = new List<float>();
        for (float step = fine, y = x - step; y > lo; step *= 1.17f, y -= step) below.Add(y);
        below.Add(lo);
        for (int i = below.Count - 1; i >= 0; i--) stops.Add(below[i]);
        for (; x < Mathf.Min(hi, fineHi); x += fine) stops.Add(x);
        for (float step = fine; x < hi; step *= 1.17f, x += step) stops.Add(x);
        stops.Add(hi);
        // No two the same (where a fine run starts at an end).
        stops.Sort();
        var distinct = new List<float> { stops[0] };
        foreach (float s in stops)
            if (s > distinct[distinct.Count - 1] + 0.01f) distinct.Add(s);
        return distinct;
    }

    static void DestroyNow(Object o)
    {
        if (Application.isPlaying) Destroy(o);
        else DestroyImmediate(o);
    }
}
