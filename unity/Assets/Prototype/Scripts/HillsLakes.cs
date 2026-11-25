using System.Collections.Generic;
using Chem;
using UnityEngine;
using UnityEngine.SceneManagement;

// The lakes on a hills map's rivers (docs/design/alpine.md, "Rivers"): each drawn as one flat surface at its level over
// the grid squares round its nodes, as the valley's lake is one surface (the scene's "Lake"), not as the cells' water:
// a lake is hundreds of thousands of cells, and the cells' water mesh (CellWaterMesh) works its levels out cell by cell.
// Its pit cells are lake (ValleyChemistry.LakeLevelOf), so the cells' water leaves them to it; land over its level stands
// over the surface. On a torus each lake stands at the copy of its place nearest the camera. All view: nothing shared
// reads it.
public sealed class HillsLakes : MonoBehaviour
{
    // A lake's surface, with its north-west corner as a map cell (unwrapped) and its size in cells.
    readonly List<(GameObject go, int x0, int y0, int w, int h)> lakes = new();
    MapLand land;
    float nextPlace;
    const float PlaceEvery = 0.25f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Attach()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        Begin();
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Begin();

    static void Begin()
    {
        if (ChemGrid.Current?.Grid == null || !PinnedValley.InGenerated || FindFirstObjectByType<HillsLakes>() != null) return;
        if (LoadedMaps.Get(ChemGrid.MapSource).Hills?.Rivers == null) return;
        new GameObject("Hills lakes").AddComponent<HillsLakes>();
    }

    void Start()
    {
        land = MapLand.Current;
        var rivers = LoadedMaps.Get(ChemGrid.MapSource).Hills.Rivers;
        if (land == null || rivers == null) return;
        var material = LakeMaterial();
        foreach (var lake in rivers.Lakes) lakes.Add(Make(lake, rivers, material));
        Debug.Log($"HILLS LAKES: {lakes.Count} lakes drawn as surfaces");
    }

    // The valley's lake's material where the scene has it, else plain water.
    static Material LakeMaterial()
    {
        var valleyLake = GameObject.Find("Lake");
        if (valleyLake != null && valleyLake.TryGetComponent<Renderer>(out var r) && r.sharedMaterial != null) return r.sharedMaterial;
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        var m = new Material(shader) { name = "Hills lake" };
        m.SetColor("_BaseColor", new Color(0.16f, 0.34f, 0.42f, 1f));
        m.SetFloat("_Smoothness", 0.9f);
        return m;
    }

    (GameObject, int, int, int, int) Make(HillsRivers.Lake lake, HillsRivers rivers, Material material)
    {
        const int g = HillsRivers.Grid;
        int gw = rivers.GridW, gh = rivers.GridH;
        var nodes = rivers.LakeNodes(lake.Id);
        // Its grid squares (a cell is lake where a corner of its square is a lake node), unwrapped round its first node.
        var (fi, fj) = nodes[0];
        var squares = new HashSet<(int, int)>();
        foreach (var (i, j) in nodes)
        {
            int ui = fi + Wrap(i - fi, gw), uj = fj + Wrap(j - fj, gh);
            // And a square more all round: land under the level just past the lake's cells reads as its shore's water, not
            // as a stair of the grid's squares (land over the level stands over the surface).
            for (int dj = -2; dj <= 1; dj++)
                for (int di = -2; di <= 1; di++) squares.Add((ui + di, uj + dj));
        }
        int i0 = int.MaxValue, j0 = int.MaxValue, i1 = int.MinValue, j1 = int.MinValue;
        foreach (var (i, j) in squares)
        {
            i0 = Mathf.Min(i0, i);
            j0 = Mathf.Min(j0, j);
            i1 = Mathf.Max(i1, i);
            j1 = Mathf.Max(j1, j);
        }
        float cell = land.Layout.CellSize, y = lake.Level * (float)MapFile.LandUnitMetres, side = g * cell;
        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        foreach (var (i, j) in squares)
        {
            float x = (i - i0) * side, z = -(j - j0) * side;
            int at = vertices.Count;
            vertices.Add(new Vector3(x, y, z));
            vertices.Add(new Vector3(x + side, y, z));
            vertices.Add(new Vector3(x, y, z - side));
            vertices.Add(new Vector3(x + side, y, z - side));
            triangles.AddRange(new[] { at, at + 1, at + 2, at + 1, at + 3, at + 2 });
        }
        var mesh = new Mesh { name = $"Hills lake {lake.Id}" };
        if (vertices.Count > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        var go = new GameObject(mesh.name);
        go.transform.SetParent(transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = material;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        int x0 = i0 * g, y0 = j0 * g;
        Place(go, x0, y0, (i1 - i0 + 1) * g, (j1 - j0 + 1) * g);
        return (go, x0, y0, (i1 - i0 + 1) * g, (j1 - j0 + 1) * g);
    }

    static int Wrap(int d, int n)
    {
        d %= n;
        if (d < -n / 2) d += n;
        if (d >= n / 2) d -= n;
        return d;
    }

    // At the copy of its place nearest the camera: its north-west corner's world x, z as a cell's line (as the far ring's).
    void Place(GameObject go, int x0, int y0, int w, int h)
    {
        var l = land.Layout;
        float wx = l.OriginX + (x0 + l.U0) * l.CellSize, wz = l.OriginZ + (l.V0 + 1 - y0) * l.CellSize;
        float hx = w * l.CellSize * 0.5f, hz = h * l.CellSize * 0.5f;
        var to = new Vector3(ViewWrap.NearX(wx + hx) - hx, 0f, ViewWrap.NearZ(wz - hz) + hz);
        if ((go.transform.position - to).sqrMagnitude > 1f) go.transform.position = to;
    }

    void Update()
    {
        if (Time.unscaledTime < nextPlace) return;
        nextPlace = Time.unscaledTime + PlaceEvery;
        foreach (var (go, x0, y0, w, h) in lakes) Place(go, x0, y0, w, h);
    }
}
