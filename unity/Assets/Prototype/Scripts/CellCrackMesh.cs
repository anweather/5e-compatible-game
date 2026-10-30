using System.Collections.Generic;
using Chem;
using UnityEngine;

// The decals on the ground: one mesh of quads drawn by Prototype/WindowGlass, a
// quad over each cracked cell (a spiderweb, CrackWeb.hlsl) and each brick cell
// (its courses). One object however many there are, rebuilt only when one changes.
// A pane's crack is on its own glass (WindowPanes). Only the show: Cell.Cracked is
// the truth.
public sealed class CellCrackMesh
{
    // How far the decal floats over its cell, and the dark of the lines.
    const float Lift = 0.035f, DepthBias = -60f;
    static readonly Color32 Line = new Color(0.05f, 0.07f, 0.1f, 0f), Mortar = new Color(0.5f, 0.43f, 0.36f, 0f);
    const float WebKind = 2f, BrickKind = 3f;

    readonly ChemGrid grid;
    readonly Dictionary<int, (float top, bool brick)> cracked = new();
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

    /// <summary>A cell was redrawn: its crack, at the height its top is drawn, is rebuilt at the next Rebuild.</summary>
    public void Changed(int x, int y, Cell c, float top)
    {
        int key = grid.World.Idx(x, y);
        bool brick = c.T == TerrainIds.Brick;
        bool now = brick || (c.Cracked != 0 && !grid.World.IsBlocking(c));
        bool had = cracked.TryGetValue(key, out var was);
        if (now) cracked[key] = (top, brick);
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
        foreach (var kv in cracked) Quad(kv.Key, kv.Value.top, kv.Value.brick);
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
        float half = grid.cellSize * 0.5f, kind = (brick ? BrickKind : WebKind) + Mathf.Repeat(key * 0.137f, 1f);
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

    public void Destroy()
    {
        Object.Destroy(shown);
        Object.Destroy(mesh);
        Object.Destroy(material);
    }
}
