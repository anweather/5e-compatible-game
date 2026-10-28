using System.Collections.Generic;
using System.Linq;
using Chem;
using UnityEngine;

// A building's walls and roof are of separate materials (world/villages.json
// "materials") and fail separately (Andrew, 2026-10-28):
//  - its walls stand on its cells: when they burn down, a roof that burns goes
//    with them, and one that doesn't (slate) falls onto what's left of the walls;
//  - its roof isn't a cell: a roof that burns (thatch, shingle) catches from a
//    fire on or beside its walls and burns away after a while, leaving the walls
//    standing, charred around where the fire was.
// View only, like the rest of the burnt look: every peer sees the same cells burn.
public partial class ValleyChemistry
{
    // Each building's wall cells (world indices), how long fire has licked at its roof (s), and whether
    // its roof is gone.
    List<int>[] buildingCells;
    float[] roofFire;
    bool[] roofGone;
    float roofTimer;
    BuildingMaterials buildingMaterials;

    // Seconds of fire at its walls before a roof that burns catches and burns away; how often to look.
    const float RoofBurnSeconds = 4f, RoofLook = 0.25f;
    // A burnt-out building's walls: this much of their height is left.
    const float BurntWallsLeft = 0.35f;

    void BindRoofs(int buildings)
    {
        buildingCells = new List<int>[buildings];
        for (int k = 0; k < buildings; k++) buildingCells[k] = new List<int>();
        roofFire = new float[buildings];
        roofGone = new bool[buildings];
        if (buildingMaterials == null)
        {
            var specs = VillageLayout.Load(ChemGrid.FindWorldDir());
            buildingMaterials = specs.Count > 0 ? specs[0].Materials : new BuildingMaterials();
        }
    }

    // A cabin's roof is shingle; a village building's comes from its cover.
    string RoofMaterialOf(int k) =>
        cabins[k] != null && cabins[k].TryGetComponent<VillageHouse>(out var house) ? buildingMaterials.RoofOf(house.cover) : TerrainIds.Shingle;

    bool RoofBurns(int k) =>
        grid.World.Rules.Terrain.TryGetValue(RoofMaterialOf(k), out var t) && t.Has("flammable");

    // A few times a second: fire on or beside a building's walls sets a roof that burns going.
    void WatchRoofs(float dt)
    {
        if (buildingCells == null || grid.World == null) return;
        roofTimer += dt;
        if (roofTimer < RoofLook) return;
        float step = roofTimer;
        roofTimer = 0f;
        for (int k = 0; k < buildingCells.Length; k++)
        {
            if (roofGone[k] || cabinCharred[k] || buildingCells[k].Count == 0 || !RoofBurns(k)) continue;
            int at = FireAt(buildingCells[k]);
            if (at < 0) continue;
            roofFire[k] += step;
            if (roofFire[k] >= RoofBurnSeconds) RoofBurnsAway(k, at);
        }
    }

    // The cell itself, then north, east, south and west.
    static readonly int[] AroundX = { 0, 0, 1, 0, -1 }, AroundY = { 0, -1, 0, 1, 0 };

    // A burning cell among the cells or beside them (world index), or -1.
    int FireAt(List<int> cells)
    {
        var world = grid.World;
        foreach (int i in cells)
        {
            int x = i % world.W, y = i / world.W;
            for (int d = 0; d < 5; d++)
            {
                int nx = x + AroundX[d], ny = y + AroundY[d];
                if (world.InBounds(nx, ny) && world.At(nx, ny).Fire > 0) return world.Idx(nx, ny);
            }
        }
        return -1;
    }

    void RoofBurnsAway(int k, int fireCell)
    {
        roofGone[k] = true;
        var building = cabins[k];
        HideRoof(building);
        var world = grid.World;
        var at = grid.CellCenter(fireCell % world.W, fireCell / world.W);
        foreach (var f in building.GetComponentsInChildren<MeshFilter>())
            if (f.name != "Roof") CharNear(f, at, 6f);
        EventLog.Add($"The roof of the {LabelOf(building)} burns away");
    }

    static void HideRoof(Transform building)
    {
        foreach (var r in building.GetComponentsInChildren<Renderer>())
            if (r.name == "Roof") r.enabled = false;
        foreach (var c in building.GetComponentsInChildren<Collider>())
            if (c.name == "Roof") c.enabled = false;
    }

    // Walls burnt down: the roof goes too if it burns, else it falls onto what's left of the walls.
    void WallsBurnDown(int k)
    {
        var building = cabins[k];
        if (!building.TryGetComponent<VillageHouse>(out _))
        {
            HideRoof(building);
            return;
        }
        var body = building.GetComponentsInChildren<Renderer>().FirstOrDefault(r => r.name == "Body");
        float top = body != null ? body.bounds.max.y : building.position.y + 3f, bottom = body != null ? body.bounds.min.y : building.position.y;
        if (body != null)
        {
            var s = body.transform.localScale;
            body.transform.localScale = new Vector3(s.x, s.y * BurntWallsLeft, s.z);
        }
        if (roofGone[k] || RoofBurns(k))
        {
            HideRoof(building);
            return;
        }
        // Slate doesn't burn: it drops onto the stubs of the walls, askew.
        float drop = (top - bottom) * (1f - BurntWallsLeft);
        foreach (var r in building.GetComponentsInChildren<Renderer>().Where(r => r.name == "Roof"))
            r.transform.SetPositionAndRotation(r.transform.position + Vector3.down * drop, r.transform.rotation * Quaternion.Euler(6f, 0f, -4f));
        EventLog.Add($"The {LabelOf(building)}'s slate roof falls in");
    }

    // Darkens a mesh's vertex colours near a point (VillageLit draws them): soot, heaviest at the point.
    static void CharNear(MeshFilter filter, Vector3 at, float radius)
    {
        var mesh = filter.mesh;
        var colors = mesh.colors;
        if (colors.Length == 0) return;
        var vertices = mesh.vertices;
        var local = filter.transform.InverseTransformPoint(at);
        float scale = filter.transform.lossyScale.x;
        for (int v = 0; v < vertices.Length; v++)
        {
            float d = Vector3.Distance(vertices[v], local) * scale;
            float soot = Mathf.Sqrt(Mathf.Clamp01(1f - d / radius)) * 0.9f;
            var c = colors[v];
            colors[v] = new Color(c.r * (1f - soot), c.g * (1f - soot), c.b * (1f - soot), c.a);
        }
        mesh.colors = colors;
    }
}
