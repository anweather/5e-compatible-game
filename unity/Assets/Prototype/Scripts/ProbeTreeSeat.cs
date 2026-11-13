using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// The probe's treeseat scenario, on any map (unity/Temp/map names one): every tree drawn now, of every kind, against the
// ground drawn under it. The terrains' trees (their kind's lowest point at the instance's scale, against the terrain's own
// surface at the trunk), and every tree model standing in the scene (the near trees' prefabs, great trees, grown trees and
// saplings: the bottom of its renderers' bounds, against the drawn ground at its root). On the island (2026-11-11) the
// generated chunks' trees stood 9 to 17 m under the ground (PinnedGround moved the heights into the pinned valley's range
// and not the trees): a check on the arrays, before that move, saw every tree within 0.38 m.
public class TreeSeatProbe : ProbeScenario
{
    /// <summary>How far a tree's lowest point may sit under the ground at its trunk, or its root over it, in metres.</summary>
    public const float Sunk = 1f, Floating = 0.75f;

    protected override IEnumerator Run()
    {
        var valley = FindAnyObjectByType<ValleyChemistry>();
        // The ground eases into any stamp laid at the start: measured once it's still.
        for (float t = 0f; t < 20f && (valley == null || valley.GroundMoving); t += Time.deltaTime) yield return null;
        yield return Wait(2f);
        var terrains = Terrain.activeTerrains;
        Check("terrains_drawn", terrains.Length > 0, $"{terrains.Length} terrains");

        var lowest = new Dictionary<GameObject, float>();
        int trees = 0, sunk = 0, floating = 0;
        float worstSunk = 0f, worstFloat = 0f;
        string worstSunkAt = "-", worstFloatAt = "-";
        void Measure(float off, string what)
        {
            trees++;
            if (off < -Sunk) sunk++;
            if (off > Floating) floating++;
            if (off < worstSunk) (worstSunk, worstSunkAt) = (off, what);
            if (off > worstFloat) (worstFloat, worstFloatAt) = (off, what);
        }

        foreach (var t in terrains)
        {
            var data = t.terrainData;
            var o = t.transform.position;
            var protos = data.treePrototypes;
            foreach (var ti in data.treeInstances)
            {
                if (ti.prototypeIndex < 0 || ti.prototypeIndex >= protos.Length || protos[ti.prototypeIndex].prefab == null) continue;
                var prefab = protos[ti.prototypeIndex].prefab;
                if (!lowest.TryGetValue(prefab, out float low)) lowest[prefab] = low = Lowest(prefab);
                var at = o + Vector3.Scale(ti.position, data.size);
                float ground = o.y + t.SampleHeight(at);
                Measure(Mathf.Min(at.y + low * ti.heightScale, at.y) - ground, $"{prefab.name} in {t.name} at cell {grid.CellAt(at)}");
            }
        }
        int terrainTrees = trees;

        // The tree models standing in the scene: anything made of a terrain tree kind's prefab (a LOD group named for it).
        var kinds = new HashSet<string>();
        foreach (var t in terrains)
            foreach (var p in t.terrainData.treePrototypes)
                if (p.prefab != null) kinds.Add(p.prefab.name);
        foreach (var lod in FindObjectsByType<LODGroup>(FindObjectsSortMode.None))
        {
            string name = lod.name;
            if (name.StartsWith("Fallen ", System.StringComparison.Ordinal)) continue;
            bool tree = name == "Great tree" || kinds.Contains(name) || name.StartsWith("Grown ", System.StringComparison.Ordinal);
            if (!tree || !lod.gameObject.activeInHierarchy) continue;
            var rs = lod.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) continue;
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            var root = lod.transform.position;
            float ground = grid.GroundAt(root);
            Measure(Mathf.Min(b.min.y, root.y) - ground, $"{name} (model) at cell {grid.CellAt(root)}");
        }

        Metric("trees_measured", trees);
        Metric("terrain_trees", terrainTrees);
        Metric("tree_models", trees - terrainTrees);
        Metric("trees_sunk", sunk);
        Metric("trees_floating", floating);
        Metric("worst_sunk_m", worstSunk);
        Metric("worst_floating_m", worstFloat);
        Check("trees_measured_some", trees > 0, $"{trees} trees ({terrainTrees} terrain trees, {trees - terrainTrees} models)");
        Check("no_tree_sunk", sunk == 0, $"{sunk} more than {Sunk} m under; worst {worstSunk:F2} m: {worstSunkAt}");
        Check("no_tree_floating", floating == 0, $"{floating} more than {Floating} m over; worst {worstFloat:F2} m: {worstFloatAt}");
    }

    // A tree kind's lowest point under its root, in its own metres (its first LOD's meshes, at the prefab's own scale).
    static float Lowest(GameObject prefab)
    {
        float low = 0f;
        var root = prefab.transform.worldToLocalMatrix;
        var scale = prefab.transform.localScale.y;
        foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null) continue;
            var m = root * mf.transform.localToWorldMatrix;
            var bounds = mf.sharedMesh.bounds;
            for (int c = 0; c < 8; c++)
            {
                var corner = bounds.center + Vector3.Scale(bounds.extents, new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1));
                low = Mathf.Min(low, m.MultiplyPoint3x4(corner).y * scale);
            }
        }
        return low;
    }
}
