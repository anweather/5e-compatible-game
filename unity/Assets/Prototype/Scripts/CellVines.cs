using System.Collections.Generic;
using Chem;
using UnityEngine;

// Plant Growth's vines (the vines cover on trellis terrain): stems and leaves hung
// over what they climb, down to its foot, or a woven mat across a pit or stream
// that holds the wizard up. A few shared meshes, one object a cell, like
// CellThorns; a wall's is stretched to its height. Walking into them climbs them
// (SurfaceEffects.Climb). Only the show: where they are is the core's.
public sealed class CellVines
{
    const int Variants = 3;
    const float MatDrop = 0.04f, MatThickness = 0.14f, ShortestDrape = 0.2f;
    static readonly Color Vine = new(0.2f, 0.4f, 0.14f);

    readonly ChemGrid grid;
    readonly MaterialCache materials;
    readonly List<Mesh> drapes = new(), mats = new();

    public CellVines(ChemGrid grid, MaterialCache materials)
    {
        this.grid = grid;
        this.materials = materials;
    }

    /// <summary>Vines on a cell, under the grid: a mat over low ground, else hung over what stands there.</summary>
    public GameObject Build(int x, int y, Cell c, Vector3 center)
    {
        if (drapes.Count == 0) MakeMeshes();
        int pick = (int)((uint)(x * 73856093 ^ y * 19349663) % Variants);
        bool low = grid.World.TerrainOf(c).Has("low");
        var go = new GameObject($"Vines {x},{y}");
        go.transform.SetParent(grid.transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = (low ? mats : drapes)[pick];
        go.AddComponent<MeshRenderer>().sharedMaterial = materials.Lit("Vines", Vine, 0.2f);
        float size = grid.cellSize / 1.5f, top = Top(x, y, c, low);
        float turn = pick * 90f;
        go.transform.SetPositionAndRotation(new Vector3(center.x, top, center.z), Quaternion.Euler(0f, turn, 0f));
        go.transform.localScale = new Vector3(size, low ? 1f : Mathf.Max(ShortestDrape, top - Foot(x, y, c)), size);
        if (low)
        {
            var mat = go.AddComponent<BoxCollider>();
            mat.center = Vector3.down * (MatThickness / 2f);
            mat.size = new Vector3(1.5f, MatThickness, 1.5f);
        }
        return go;
    }

    // A wall's top as drawn, or for a mat the highest ground beside it.
    float Top(int x, int y, Cell c, bool low)
    {
        float ground = grid.GroundTop(x, y, c);
        if (!low) return ground + Mathf.Max(CellDressing.Look(c.T).height, ValleyChemistry.FireHeight(c.T));
        float rim = ground;
        foreach (var (dx, dy) in Sides)
            if (grid.World.InBounds(x + dx, y + dy)) rim = Mathf.Max(rim, grid.GroundTop(x + dx, y + dy, grid.World.At(x + dx, y + dy)));
        return rim - MatDrop;
    }

    // The lowest ground beside a cell, its own too: how far down they hang.
    float Foot(int x, int y, Cell c)
    {
        float foot = grid.GroundTop(x, y, c);
        foreach (var (dx, dy) in Sides)
            if (grid.World.InBounds(x + dx, y + dy)) foot = Mathf.Min(foot, grid.GroundTop(x + dx, y + dy, grid.World.At(x + dx, y + dy)));
        return foot;
    }

    static readonly (int dx, int dy)[] Sides = { (1, 0), (-1, 0), (0, 1), (0, -1) };

    void MakeMeshes()
    {
        var kit = new MeshKit { Occlusion = 0f };
        for (int v = 0; v < Variants; v++)
        {
            var rng = new Unity.Mathematics.Random((uint)(5151 + v * 37));
            kit.Clear();
            for (int side = 0; side < 4; side++) Side(kit, ref rng, Quaternion.Euler(0f, side * 90f, 0f));
            Strands(kit, ref rng, 3, 0.01f);
            drapes.Add(kit.ToMesh($"Vines {v}"));
            kit.Clear();
            Strands(kit, ref rng, 9, -0.05f);
            mats.Add(kit.ToMesh($"Vine mat {v}"));
        }
    }

    // Stems down one face of a 1.5 m cell from its top (y 0) to its foot (y -1), wandering side to side, leaves along them.
    static void Side(MeshKit kit, ref Unity.Mathematics.Random rng, Quaternion turn)
    {
        const int Segments = 6;
        for (int strand = 0; strand < 6; strand++)
        {
            var at = new Vector3(-0.7f + strand * 0.28f + rng.NextFloat(-0.08f, 0.08f), 0.02f, 0.78f);
            int length = rng.NextInt(3, Segments + 1);
            for (int s = 0; s < length; s++)
            {
                var next = at + new Vector3(rng.NextFloat(-0.14f, 0.14f), -1f / Segments, rng.NextFloat(0f, 0.02f));
                kit.Cylinder(turn * at, turn * next, 0.022f, 4, Vine, false);
                Leaf(kit, ref rng, turn * at, turn * Vector3.forward);
                if (rng.NextFloat() < 0.6f) Leaf(kit, ref rng, turn * next, turn * Vector3.forward);
                at = next;
            }
        }
    }

    // A quarter turn about the cell's middle.
    static Vector3 Turned(Vector3 p) => Quaternion.Euler(0f, 90f, 0f) * p;

    // Stems across the cell at a height, crossing, with leaves.
    static void Strands(MeshKit kit, ref Unity.Mathematics.Random rng, int count, float y)
    {
        for (int i = 0; i < count; i++)
        {
            var a = new Vector3(rng.NextFloat(-0.78f, 0.78f), y, -0.78f);
            var b = new Vector3(rng.NextFloat(-0.78f, 0.78f), y, 0.78f);
            if (i % 2 == 1)
            {
                a = Turned(a);
                b = Turned(b);
            }
            var last = a;
            for (int s = 1; s <= 5; s++)
            {
                var next = Vector3.Lerp(a, b, s / 5f) + new Vector3(rng.NextFloat(-0.08f, 0.08f), rng.NextFloat(0f, 0.03f), rng.NextFloat(-0.08f, 0.08f));
                kit.Cylinder(last, next, 0.03f, 4, Vine, false);
                Leaf(kit, ref rng, next, Vector3.up);
                Leaf(kit, ref rng, last, Vector3.up);
                last = next;
            }
        }
    }

    // One leaf: a pointed sheet off a stem, lying near its surface.
    static void Leaf(MeshKit kit, ref Unity.Mathematics.Random rng, Vector3 at, Vector3 outward)
    {
        var along = Vector3.Cross(outward, Random3(ref rng)).normalized;
        var across = Vector3.Cross(outward, along);
        float size = rng.NextFloat(0.06f, 0.12f);
        var tip = at + along * (size * 2f) + outward * rng.NextFloat(0f, 0.05f);
        var middle = at + along * size;
        kit.Sheet(at, middle + across * (size * 0.6f), tip, middle - across * (size * 0.6f), Vine);
    }

    static Vector3 Random3(ref Unity.Mathematics.Random rng) => new(rng.NextFloat(-1f, 1f), rng.NextFloat(-1f, 1f), rng.NextFloat(-1f, 1f));

    /// <summary>Frees the meshes.</summary>
    public void Destroy()
    {
        foreach (var m in drapes) Object.Destroy(m);
        foreach (var m in mats) Object.Destroy(m);
        drapes.Clear();
        mats.Clear();
    }
}
