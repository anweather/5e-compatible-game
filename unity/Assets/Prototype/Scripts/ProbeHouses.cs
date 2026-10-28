using System.Collections;
using System.Linq;
using Chem;
using UnityEngine;

// The probe's houses scenario (Andrew, 2026-10-28, the materials step): a Fire
// Bolt on one of Hollowmere's timber houses and on one of its stone houses,
// on a clear noon. A building's walls are cells of its wall material
// (world/villages.json "materials"): log walls burn down to rubble, stone
// walls stand. Numbers: each house's wall cells before and after, and the
// timber house before and after in pictures.
public class HousesProbe : ProbeScenario
{
    protected override IEnumerator Run()
    {
        yield return Switch(At(12f) + ", 'weather': { 'calmPeriods': 0, 'rampMinutes': 0.02, 'weights': " +
            "{ 'clear': 1, 'cloudy': 0, 'windy': 0, 'fog': 0, 'rain': 0, 'storm': 0, 'snow': 0 } }");
        var village = FindObjectsByType<Village>(FindObjectsSortMode.None).FirstOrDefault(v => v.villageName == "Hollowmere");
        if (village == null)
        {
            Check("village_found", false, "no Hollowmere");
            yield break;
        }
        var houses = village.GetComponentsInChildren<VillageHouse>();
        var timber = houses.FirstOrDefault(h => h.walls == WallKind.Timber);
        var stone = houses.FirstOrDefault(h => h.walls == WallKind.Stone);
        Check("houses_found", timber != null && stone != null, $"timber {timber != null}, stone {stone != null}");
        if (timber == null || stone == null) yield break;

        int timberBefore = Walls(timber, TerrainIds.LogWall), stoneBefore = Walls(stone, TerrainIds.Stone);
        Frame(timber);
        yield return Wait(0.5f);
        Shot("timber-before");
        yield return null;
        yield return null;
        Bolt(timber, TerrainIds.LogWall);
        yield return Wait(0.5f);
        Bolt(stone, TerrainIds.Stone);
        yield return Wait(45f);
        int timberAfter = Walls(timber, TerrainIds.LogWall), stoneAfter = Walls(stone, TerrainIds.Stone);
        Frame(timber);
        yield return Wait(0.5f);
        Shot("timber-after");
        yield return null;
        yield return null;
        Frame(stone);
        yield return Wait(0.5f);
        Shot("stone-after");
        yield return null;
        yield return null;

        Metric("timber_walls_before", timberBefore);
        Metric("timber_walls_after", timberAfter);
        Metric("stone_walls_before", stoneBefore);
        Metric("stone_walls_after", stoneAfter);
        Check("timber_has_wall_cells", timberBefore > 0, $"{timberBefore} log wall cells");
        Check("stone_has_wall_cells", stoneBefore > 0, $"{stoneBefore} stone wall cells");
        Check("timber_burns", timberAfter < timberBefore, $"log walls {timberBefore} -> {timberAfter}");
        Check("stone_stands", stoneAfter == stoneBefore, $"stone walls {stoneBefore} -> {stoneAfter}");
    }

    // A house's wall cells of a terrain: those under its box (a little grown), by world cell.
    int Walls(VillageHouse house, string terrain) => Under(house).Count(c => World.At(c.x, c.y).T == terrain);

    System.Collections.Generic.IEnumerable<Vector2Int> Under(VillageHouse house)
    {
        var centre = grid.CellAt(house.transform.position);
        int reach = Mathf.CeilToInt(Mathf.Max(house.width, house.depth) / grid.cellSize / 2f) + 1;
        for (int y = centre.y - reach; y <= centre.y + reach; y++)
            for (int x = centre.x - reach; x <= centre.x + reach; x++)
                if (World.InBounds(x, y)) yield return new Vector2Int(x, y);
    }

    // A Fire Bolt at the house's wall cell nearest its middle.
    void Bolt(VillageHouse house, string terrain)
    {
        var centre = grid.CellAt(house.transform.position);
        var wall = Under(house).Where(c => World.At(c.x, c.y).T == terrain).OrderBy(c => (c - centre).sqrMagnitude).FirstOrDefault();
        Teleport(house.transform.position - house.transform.forward * 7f + Vector3.up * 0.1f);
        caster.Cast(SpellIds.FireBolt, wall.x, wall.y, 1);
    }

    void Frame(VillageHouse house)
    {
        var t = house.transform;
        var front = -t.forward;
        Teleport(t.position + front * 12f + Vector3.up * 0.1f);
        Cut(t.position + front * 11f + t.right * 4f + Vector3.up * 5f, t.position + Vector3.up * 1.5f);
    }
}
