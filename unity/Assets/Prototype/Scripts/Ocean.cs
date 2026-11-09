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
// swimmer (SurfaceEffects) rides the waves they see.
[ExecuteAlways]
public class Ocean : MonoBehaviour
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

    // The map changed (a host's map): its own sea.
    void Update()
    {
        if (MapLand.Current != builtFor) Build();
    }

    void OnDisable()
    {
        if (Instance == this) Instance = null;
        Clear();
    }

    void Clear()
    {
        if (surface != null) DestroyNow(surface);
        if (mesh != null) DestroyNow(mesh);
    }

    /// <summary>Builds the sea's mesh again (after the terrain under it changed shape, or the map did).</summary>
    public void Build()
    {
        Clear();
        terrains = null;
        builtFor = MapLand.Current;
        if (builtFor != null) FromMap(builtFor);
        var out_ = seaward.normalized;
        var along = Vector2.Perpendicular(out_);
        var (farAlong, farInland) = MapSpan(out_, along);
        float fineSide = Mathf.Max(fineAlong, farAlong), back = Mathf.Max(inland, farInland);
        var us = Stops(-alongHalf, alongHalf, -fineSide, fineSide);
        var vs = Stops(-back, outFar, -back, fineOut);
        var points = new Vector2[us.Count * vs.Count];
        var wet = new bool[points.Length];
        int n = 0;
        foreach (float v in vs)
            foreach (float u in us)
            {
                points[n] = shoreOrigin + along * u + out_ * v;
                wet[n] = Wet(points[n]);
                n++;
            }
        // Only the squares over the water: a square with a wet corner, its points numbered as they're first used.
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
            water.Add(new Vector2(Depth(xz, vs[k / us.Count]), 0f));
            return index[k];
        }
        for (int j = 0; j + 1 < vs.Count; j++)
            for (int i = 0; i + 1 < us.Count; i++)
            {
                int a = j * us.Count + i, b = a + 1, c = a + us.Count, d = c + 1;
                if (!(wet[a] || wet[b] || wet[c] || wet[d])) continue;
                int ia = Use(a), ib = Use(b), ic = Use(c), id = Use(d);
                triangles.Add(ia);
                triangles.Add(ib);
                triangles.Add(ic);
                triangles.Add(ib);
                triangles.Add(id);
                triangles.Add(ic);
            }
        if (triangles.Count == 0) return;
        mesh = new Mesh { name = "Ocean", indexFormat = IndexFormat.UInt32, hideFlags = HideFlags.DontSave };
        mesh.SetVertices(positions);
        mesh.SetUVs(1, water);
        mesh.SetTriangles(triangles, 0);
        var up = new Vector3[positions.Count];
        System.Array.Fill(up, Vector3.up);
        mesh.normals = up;
        // The swell lifts the surface: keep it from being culled at the edges of the view.
        var bounds = mesh.bounds;
        bounds.Expand(new Vector3(4f, 4f, 4f));
        mesh.bounds = bounds;

        surface = new GameObject("Sea surface") { hideFlags = HideFlags.DontSave | HideFlags.NotEditable };
        surface.transform.SetParent(transform, false);
        surface.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = surface.AddComponent<MeshRenderer>();
        r.sharedMaterial = material;
        r.shadowCastingMode = ShadowCastingMode.Off;
        if (shoreMap != null)
        {
            var block = new MaterialPropertyBlock();
            block.SetTexture(ShoreMapId, shoreMap.texture);
            block.SetVector(ShoreMapRectId, shoreMap.Rect);
            block.SetVector(ShoreDirId, ShoreDir);
            r.SetPropertyBlock(block);
        }
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
        bool lake = System.Array.IndexOf(map.Map.Dresses, "lake") >= 0;
        wetCells = map.Sea.Near(Mathf.CeilToInt(inland / map.Layout.CellSize), basins: !lake);
    }

    bool[] wetCells;

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
    (float along, float inland) MapSpan(Vector2 out_, Vector2 along)
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
        // On fine steps from the shore, as the near sea's are.
        return (Mathf.Ceil(most / fineStep) * fineStep, Mathf.Ceil(back / fineStep) * fineStep);
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
    // shelving away from the coast.
    float Depth(Vector2 xz, float outward)
    {
        var p = new Vector3(xz.x, 0f, xz.y);
        var terrain = (terrains ??= new TerrainTiles()).At(p);
        if (terrain != null) return level - (terrain.SampleHeight(p) + terrain.transform.position.y);
        return Mathf.Clamp(3f + outward * 0.12f, 3f, 60f);
    }

    TerrainTiles terrains;

    // The terrains by where they stand, read once per build: the map's chunks are a grid of same-sized tiles, so a point
    // finds its own in one step rather than asking each (the nine has 729, and the sea's mesh asks for every vertex).
    sealed class TerrainTiles
    {
        readonly Terrain[] all = Terrain.activeTerrains;
        readonly Dictionary<(int, int), Terrain> byTile = new();
        readonly float x0, z0, w, d;
        readonly bool tiled;

        public TerrainTiles()
        {
            if (all.Length == 0) return;
            var size = all[0].terrainData.size;
            w = size.x;
            d = size.z;
            x0 = float.MaxValue;
            z0 = float.MaxValue;
            foreach (var t in all)
            {
                x0 = Mathf.Min(x0, t.transform.position.x);
                z0 = Mathf.Min(z0, t.transform.position.z);
            }
            tiled = w > 0f && d > 0f;
            foreach (var t in all)
            {
                var s = t.terrainData.size;
                var p = t.transform.position;
                float i = (p.x - x0) / w, j = (p.z - z0) / d;
                if (!Mathf.Approximately(s.x, w) || !Mathf.Approximately(s.z, d) || Mathf.Abs(i - Mathf.Round(i)) > 0.01f || Mathf.Abs(j - Mathf.Round(j)) > 0.01f)
                    tiled = false;
                byTile.TryAdd((Mathf.RoundToInt(i), Mathf.RoundToInt(j)), t);
            }
        }

        // The first terrain holding the point, as asking each in turn would find it (a shared edge goes to either).
        public Terrain At(Vector3 p)
        {
            if (!tiled) return First(all, p);
            int i = Mathf.FloorToInt((p.x - x0) / w), j = Mathf.FloorToInt((p.z - z0) / d);
            for (int dj = 0; dj >= -1; dj--)
                for (int di = 0; di >= -1; di--)
                    if (byTile.TryGetValue((i + di, j + dj), out var t) && t != null && Holds(t, p)) return t;
            return null;
        }

        static Terrain First(Terrain[] terrains, Vector3 p)
        {
            foreach (var t in terrains)
                if (t != null && Holds(t, p)) return t;
            return null;
        }

        static bool Holds(Terrain t, Vector3 p)
        {
            var size = t.terrainData.size;
            var local = p - t.transform.position;
            return local.x >= 0f && local.z >= 0f && local.x <= size.x && local.z <= size.z;
        }
    }

    // Coordinates from lo to hi: fineStep apart between fineLo and fineHi, growing by a sixth each
    // step beyond them.
    List<float> Stops(float lo, float hi, float fineLo, float fineHi)
    {
        var stops = new List<float>();
        float x = Mathf.Max(lo, fineLo);
        var below = new List<float>();
        for (float step = fineStep, y = x - step; y > lo; step *= 1.17f, y -= step) below.Add(y);
        below.Add(lo);
        for (int i = below.Count - 1; i >= 0; i--) stops.Add(below[i]);
        for (; x < Mathf.Min(hi, fineHi); x += fineStep) stops.Add(x);
        for (float step = fineStep; x < hi; step *= 1.17f, x += step) stops.Add(x);
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
