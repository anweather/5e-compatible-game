using System.Collections.Generic;
using UnityEngine;

// Spike Growth's thorns (the "thorns" cover): a clump of hard, leaning spikes
// and a few low woody stems on each thorny cell. The clumps are a few shared
// meshes (MeshKit cones and cylinders, made once), one object per cell turned
// and sized by its position, so a big patch is a few hundred renderers of the
// same mesh and material. The cell's fire, ash and withering are the core's;
// this only draws the cover while it's there.
public sealed class CellThorns
{
    const int Variants = 3;
    static readonly Color Thorn = new(0.30f, 0.33f, 0.16f);

    readonly ChemGrid grid;
    readonly MaterialCache materials;
    readonly List<Mesh> meshes = new();

    public CellThorns(ChemGrid grid, MaterialCache materials)
    {
        this.grid = grid;
        this.materials = materials;
    }

    /// <summary>A clump of thorns on a cell's ground, under the grid.</summary>
    public GameObject Build(int x, int y, Vector3 center, float ground)
    {
        if (meshes.Count == 0) MakeMeshes();
        int pick = (int)((uint)(x * 73856093 ^ y * 19349663) % Variants);
        var go = new GameObject($"Thorns {x},{y}");
        go.transform.SetParent(grid.transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = meshes[pick];
        go.AddComponent<MeshRenderer>().sharedMaterial = materials.Lit("Thorns", Thorn, 0.15f);
        float size = grid.cellSize / 1.5f;
        go.transform.SetPositionAndRotation(new Vector3(center.x, ground, center.z), Quaternion.Euler(0f, (x * 37 + y * 101) % 360, 0f));
        go.transform.localScale = Vector3.one * size;
        return go;
    }

    /// <summary>Moves a clump with its cell's ground.</summary>
    public static void Place(GameObject clump, Vector3 center, float ground) =>
        clump.transform.position = new Vector3(center.x, ground, center.z);

    // A few clumps for a 1.5 m square: leaning spikes over woody stems lying in the grass.
    void MakeMeshes()
    {
        var kit = new MeshKit { Occlusion = 0f };
        for (int v = 0; v < Variants; v++)
        {
            kit.Clear();
            var rng = new Unity.Mathematics.Random((uint)(4242 + v * 31));
            float Range(float a, float b) => rng.NextFloat(a, b);
            for (int i = 0; i < 4; i++)
            {
                var a = new Vector3(Range(-0.65f, 0.65f), 0.04f, Range(-0.65f, 0.65f));
                var b = a + Quaternion.Euler(0f, Range(0f, 360f), 0f) * Vector3.forward * Range(0.5f, 0.9f) + Vector3.up * Range(0f, 0.12f);
                kit.Cylinder(a, b, 0.035f, 5, Thorn);
            }
            for (int i = 0; i < 16; i++)
            {
                var foot = new Vector3(Range(-0.68f, 0.68f), 0f, Range(-0.68f, 0.68f));
                kit.Frame = Matrix4x4.TRS(foot, Quaternion.Euler(Range(-35f, 35f), Range(0f, 360f), Range(-35f, 35f)), Vector3.one);
                kit.Cone(Vector3.zero, Range(0.025f, 0.05f), Range(0.22f, 0.55f), 5, Thorn);
            }
            kit.Frame = Matrix4x4.identity;
            meshes.Add(kit.ToMesh($"Thorns {v}"));
        }
    }

    /// <summary>Frees the meshes.</summary>
    public void Destroy()
    {
        foreach (var m in meshes) Object.Destroy(m);
        meshes.Clear();
    }
}
