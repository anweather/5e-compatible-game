using System.Collections.Generic;
using Chem;
using UnityEngine;

// The decals on the ground: one mesh of quads drawn by Prototype/WindowGlass, a
// quad over each cracked cell (a spiderweb, CrackWeb.hlsl) and each brick cell
// (its courses). A crack in ice is a small grid of quads that follows the ice's own
// surface, over a mound too. One object however many there are, rebuilt only when one changes.
// A pane's crack is on its own glass (WindowPanes). Only the show: Cell.Cracked is
// the truth.
public sealed class CellCrackMesh
{
    // How far the decal floats over its cell, and the dark of the lines.
    const float Lift = 0.035f, IceLift = 0.05f, DepthBias = -60f;
    static readonly Color32 Line = new Color(0.05f, 0.07f, 0.1f, 0f), Mortar = new Color(0.5f, 0.43f, 0.36f, 0f);
    const float WebKind = 2f, BrickKind = 3f;
    // Quads to a side of a crack on ice.
    const int IceGrid = 6;

    readonly ChemGrid grid;
    readonly Dictionary<int, (float top, bool brick, bool ice)> cracked = new();
    readonly GameObject shown;
    readonly Mesh mesh;
    readonly Material material;
    readonly List<Vector3> vertices = new(), uvs = new(), normals = new();
    readonly List<Color32> colors = new();
    readonly List<int> triangles = new();
    bool dirty;

    public CellCrackMesh(ChemGrid grid)
    {
        this.grid = grid;
        var shader = Shader.Find("Prototype/WindowGlass");
        if (shader == null) return;
        material = new Material(shader);
        material.SetFloat("_DepthBias", DepthBias);
        mesh = new Mesh { name = "Cell cracks", hideFlags = HideFlags.DontSave, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        shown = new GameObject("Cracks");
        shown.transform.SetParent(grid.transform, false);
        shown.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = shown.AddComponent<MeshRenderer>();
        r.sharedMaterial = material;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    /// <summary>A web's seed, 0 to 1, in the middle of one of the shader's 64 steps: on the edge of one, it speckled from step to step.</summary>
    public static float WebSeed(float any) => (Mathf.Floor(Mathf.Repeat(any, 1f) * 64f) + 0.5f) / 64f;

    /// <summary>A cell was redrawn: its crack, at the height its top is drawn, is rebuilt at the next Rebuild.</summary>
    public void Changed(int x, int y, Cell c, float top)
    {
        int key = grid.World.Idx(x, y);
        bool brick = c.T == TerrainIds.Brick;
        bool now = brick || (c.Cracked != 0 && !grid.World.IsBlocking(c));
        bool had = cracked.TryGetValue(key, out var was);
        if (now) cracked[key] = (top, brick, c.Frozen);
        else cracked.Remove(key);
        dirty |= had != now || (now && (was.brick != brick || !Mathf.Approximately(was.top, top)));
    }

    /// <summary>Rebuilds the quads if a cell's crack changed since the last time.</summary>
    public void Rebuild()
    {
        if (!dirty || mesh == null) return;
        dirty = false;
        vertices.Clear();
        uvs.Clear();
        normals.Clear();
        colors.Clear();
        triangles.Clear();
        foreach (var kv in cracked)
        {
            if (kv.Value.ice) IceQuads(kv.Key, kv.Value.top);
            else Quad(kv.Key, kv.Value.top, kv.Value.brick);
        }
        mesh.Clear();
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetColors(colors);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
    }

    void Quad(int key, float top, bool brick)
    {
        int w = grid.World.W, v = vertices.Count;
        var c = grid.CellCenter(key % w, key / w);
        c = grid.transform.InverseTransformPoint(new Vector3(c.x, top + Lift, c.z));
        float half = grid.cellSize * 0.5f, kind = (brick ? BrickKind : WebKind) + WebSeed(key * 0.137f);
        for (int i = 0; i < 4; i++)
        {
            float u = i == 1 || i == 2 ? 1f : 0f, t = i >= 2 ? 1f : 0f;
            vertices.Add(c + new Vector3((u * 2f - 1f) * half, 0f, (t * 2f - 1f) * half));
            uvs.Add(new Vector3(u, t, kind));
            normals.Add(Vector3.up);
            colors.Add(brick ? Mortar : Line);
        }
        triangles.AddRange(new[] { v, v + 2, v + 1, v, v + 3, v + 2 });
    }

    // A grid of quads over the cell, each corner on the ice as it's drawn there.
    void IceQuads(int key, float top)
    {
        int w = grid.World.W, x = key % w, y = key / w, first = vertices.Count, side = IceGrid + 1;
        var middle = grid.CellCenter(x, y);
        float half = grid.cellSize * 0.5f, kind = WebKind + WebSeed(key * 0.137f);
        for (int j = 0; j < side; j++)
            for (int i = 0; i < side; i++)
            {
                float u = i / (float)IceGrid, t = j / (float)IceGrid;
                // The cell's +x is the world's, and its +y runs the world's -z (ContourSurface.Flat).
                float gx = x + u, gy = y + 1f - t;
                var at = new Vector3(middle.x + (u * 2f - 1f) * half, grid.DrawnWaterTopAt(gx, gy, top) + IceLift, middle.z + (t * 2f - 1f) * half);
                vertices.Add(grid.transform.InverseTransformPoint(at));
                uvs.Add(new Vector3(u, t, kind));
                normals.Add(Vector3.up);
                colors.Add(Line);
            }
        for (int j = 0; j < IceGrid; j++)
            for (int i = 0; i < IceGrid; i++)
            {
                int a = first + j * side + i, b = a + 1, c = a + side, d = c + 1;
                triangles.AddRange(new[] { a, c, d, a, d, b });
            }
    }

    public void Destroy()
    {
        Object.Destroy(shown);
        Object.Destroy(mesh);
        Object.Destroy(material);
    }
}
