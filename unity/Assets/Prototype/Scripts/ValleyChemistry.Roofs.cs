using System.Collections.Generic;
using System.Linq;
using Chem;
using UnityEngine;

// A building's walls and roof are of separate materials (world/villages.json
// "materials") and fail separately (Andrew, 2026-10-28):
//  - its walls stand on its cells: when they burn down, a roof that burns goes
//    with them, and one that doesn't (slate) falls onto what's left of the walls;
//  - its roof isn't a cell: a roof that burns (thatch, shingle) catches from a
//    fire on or beside its walls (sooner the more flammable it is), burns as long
//    as its fuel lasts, and is gone, leaving the walls standing, charred around
//    where the fire was. A hot roof (thatch, materials.json "heat") flares as it
//    goes and sets the walls nearest the fire alight: through the core, as an
//    effect command (ChemEffects), so every peer's walls burn alike.
// View only, like the rest of the burnt look: every peer sees the same cells burn.
public partial class ValleyChemistry
{
    // Each building's wall cells (world indices), how long fire has licked at its roof (s), and whether
    // its roof is gone.
    List<int>[] buildingCells;
    float[] roofFire;
    bool[] roofGone;
    // When (Time.time) each roof caught and was gone, -1 not yet: the probe's numbers.
    float[] roofCaughtAt, roofGoneAt;
    int[] lastFire;
    float roofTimer;
    BuildingMaterials buildingMaterials;

    // How often to look; a roof catches after this many seconds of fire beside it over its flammability;
    // a roof this hot (heat) or hotter lights the walls as it burns away, this many of them.
    const float RoofLook = 0.25f, RoofCatch = 1f, FlareHeat = 2f;
    const int FlareCells = 4;
    // A burnt-out building's walls: this much of their height is left.
    const float BurntWallsLeft = 0.35f;

    void BindRoofs(int buildings)
    {
        buildingCells = new List<int>[buildings];
        for (int k = 0; k < buildings; k++) buildingCells[k] = new List<int>();
        roofFire = new float[buildings];
        roofGone = new bool[buildings];
        roofCaughtAt = Enumerable.Repeat(-1f, buildings).ToArray();
        roofGoneAt = Enumerable.Repeat(-1f, buildings).ToArray();
        lastFire = new int[buildings];
        if (buildingMaterials == null)
        {
            var specs = VillageLayout.Load(ChemGrid.FindWorldDir());
            buildingMaterials = specs.Count > 0 ? specs[0].Materials : new BuildingMaterials();
        }
    }

    // A cabin's roof is shingle; a village building's comes from its cover.
    string RoofMaterialOf(int k) =>
        cabins[k] != null && cabins[k].TryGetComponent<VillageHouse>(out var house) ? buildingMaterials.RoofOf(house.cover) : TerrainIds.Shingle;

    TerrainDef RoofOf(int k) => grid.World.Rules.Terrain.TryGetValue(RoofMaterialOf(k), out var t) ? t : null;

    bool RoofBurns(int k) => RoofOf(k)?.Burns == true;

    /// <summary>For the probe: when a building's roof caught and when it was gone (Time.time), -1 for not yet.</summary>
    public (float caught, float gone) RoofTimes(Transform building)
    {
        int k = System.Array.IndexOf(cabins, building);
        return k < 0 || roofCaughtAt == null ? (-1f, -1f) : (roofCaughtAt[k], roofGoneAt[k]);
    }

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
            if (!roofGone[k] && buildingCells[k].Count > 0 && RoofBurns(k)) RoofStep(k, step);
        }
    }

    // A roof that burns: catching from fire beside it (a flammable one sooner: thatch in about a second,
    // shingle in several), then burning as long as its fuel lasts (ticks at the sim's rate).
    void RoofStep(int k, float step)
    {
        var roof = RoofOf(k);
        int at = FireAt(buildingCells[k]);
        if (roofCaughtAt[k] < 0)
        {
            if (at < 0 || cabinCharred[k]) return;
            roofFire[k] += step;
            if (roofFire[k] < RoofCatch / Mathf.Max(0.05f, (float)roof.Flammability)) return;
            roofCaughtAt[k] = Time.time;
            roofFire[k] = 0f;
            lastFire[k] = at;
            return;
        }
        if (at >= 0) lastFire[k] = at;
        roofFire[k] += step;
        if (roofFire[k] >= (roof.Fuel ?? 5) / (float)grid.World.Rules.Sim.TicksPerSecond) RoofBurnsAway(k, lastFire[k]);
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
        roofGoneAt[k] = Time.time;
        if (RoofOf(k).Heat >= FlareHeat && !cabinCharred[k]) Flare(k, fireCell);
        var building = cabins[k];
        HideRoof(building);
        var world = grid.World;
        var at = grid.CellCenter(fireCell % world.W, fireCell / world.W);
        foreach (var f in building.GetComponentsInChildren<MeshFilter>())
            if (f.name != "Roof") CharNear(f, at, 6f);
        EventLog.Add($"The roof of the {LabelOf(building)} burns away");
    }

    // A hot roof flaring as it goes sets the walls nearest the fire alight (they burn as long as wood does).
    void Flare(int k, int fireCell)
    {
        var world = grid.World;
        int fx = fireCell % world.W, fy = fireCell / world.W;
        foreach (int i in buildingCells[k].OrderBy(i => (i % world.W - fx) * (i % world.W - fx) + (i / world.W - fy) * (i / world.W - fy)).Take(FlareCells))
            ChemEffects.Request(grid, new EffectCommand(EffectKind.Burn, i % world.W, i / world.W, null));
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
