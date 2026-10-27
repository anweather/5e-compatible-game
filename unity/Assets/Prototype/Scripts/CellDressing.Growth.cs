using UnityEngine;

// What Plant Growth stands on a cell: a sapling, and the tree it grows into.
// Both are the valley's own tree models (ValleyChemistry.TreeModel: the tree
// that stood there before it burnt, else one of its chunk's kinds), a sapling
// at a quarter of its size, springing up and then filling out (GrowIn). The
// valley's baked trees are the terrain's own and aren't dressed; only a tree
// that grew in this world is (ValleyChemistry.Grown). Without a valley, a
// stem and a crown stand in.
public sealed partial class CellDressing
{
    const float SaplingScale = 0.25f, SpringSeconds = 1.2f, FillOutSeconds = 3f;

    // A tree that grew from a sapling here; the terrain's own trees aren't dressed.
    bool IsGrownTree(string terrain, int x, int y) =>
        terrain == TerrainIds.Tree && grid.valley != null && grid.valley.Grown(x, y);

    // A sapling (small) or a grown tree, at its trunk's spot when the valley drew a tree in this cell.
    void DressTree(Transform root, Vector3 surface, System.Random rng, int x, int y, bool sapling)
    {
        var foot = surface;
        if (grid.valley != null && grid.valley.TrunkAt(x, y, out var trunk)) foot = new Vector3(trunk.x, surface.y, trunk.z);
        var turn = Quaternion.Euler(0f, rng.Next(360), 0f);
        if (grid.valley != null && grid.valley.TreeModel(x, y, out var prefab, out var scale))
        {
            var tree = Object.Instantiate(prefab, root);
            tree.name = (sapling ? "Sapling " : "Grown ") + prefab.name;
            tree.transform.SetPositionAndRotation(foot, turn);
            foreach (var col in tree.GetComponentsInChildren<Collider>()) Object.Destroy(col);
            var full = Vector3.Scale(prefab.transform.localScale, scale);
            if (sapling) GrowIn.Play(tree, Vector3.zero, full * SaplingScale, SpringSeconds);
            else GrowIn.Play(tree, full * SaplingScale, full, FillOutSeconds);
            return;
        }
        var stand = new GameObject(sapling ? "Sapling" : "Tree").transform;
        stand.SetParent(root, false);
        stand.SetPositionAndRotation(foot, turn);
        var bark = new Color(0.36f, 0.25f, 0.16f);
        var leaves = new Color(0.22f, 0.45f, 0.18f);
        var stem = Part(stand, PrimitiveType.Cylinder, foot + Vector3.up * 1.5f, new Vector3(0.3f, 1.5f, 0.3f), "Bark", bark);
        Paint(stem.GetComponent<Renderer>(), "Bark", bark, 0.1f, x, y);
        var crown = Part(stand, PrimitiveType.Sphere, foot + Vector3.up * 3.6f, new Vector3(2.4f, 2.6f, 2.4f), "Leaves", leaves);
        Paint(crown.GetComponent<Renderer>(), "Leaves", leaves, 0.1f, x, y);
        if (sapling) GrowIn.Play(stand.gameObject, Vector3.zero, Vector3.one * SaplingScale, SpringSeconds);
        else GrowIn.Play(stand.gameObject, Vector3.one * SaplingScale, Vector3.one, FillOutSeconds);
    }
}
