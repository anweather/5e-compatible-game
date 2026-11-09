using System.Collections;
using System.Linq;
using UnityEngine;

// The probe's secondmap scenario: a map that isn't the valley starts and can be walked (docs/design/maps-as-data.md, phase
// 7), run on the map the open Editor is pointed at (Temp/map, ChemGrid.EditorMapFile: an id under world/maps or a map
// folder's path, test-fixtures/maps/coast-192 for its sea). The world is the map's size, its chunks are its own and drawn
// from its land, the wizard starts at its spawn on its ground and walks off, and its sea is drawn where its land is low:
// over the sea and a cove joined to the edge, and over a hollow walled off from it (a map that dresses no lake).
public class SecondMapProbe : ProbeScenario
{
    protected override IEnumerator Run()
    {
        var land = MapLand.Current;
        Check("not_the_valley", land != null && land.Map.Id != "valley", $"on {land?.Map.Id}: name another map in unity/{ChemGrid.EditorMapFile}");
        if (land == null) yield break;
        var map = land.Map;
        Check("world_is_the_maps", World.W == map.W && World.H == map.H, $"{World.W} x {World.H}, map {map.W} x {map.H}");
        var terrains = FindAnyObjectByType<MapTerrains>();
        int chunks = terrains != null ? terrains.Chunks.Length : 0;
        Check("chunks_are_the_maps", chunks == land.Layout.ChunksX * land.Layout.ChunksZ, $"{chunks} chunks");

        var spawn = grid.CellCenter(map.Spawn.x, map.Spawn.y);
        var at = wizard.transform.position;
        float off = new Vector2(at.x - spawn.x, at.z - spawn.z).magnitude, above = at.y - grid.GroundAt(at);
        Metric("spawn_off_m", off);
        Metric("spawn_above_ground_m", above);
        Check("starts_at_the_spawn", off < 3f && above > -0.5f && above < 1.5f, $"{off:0.0} m off the spawn, {above:0.00} m over the ground");

        Metric("sea_cells", land.Sea.SeaCells);
        Metric("basin_cells", land.Sea.BasinCells);
        var ocean = Ocean.Instance;
        var seaCell = Cell(c => land.Sea.IsSea(c.x, c.y));
        var basinCell = Cell(c => land.Sea.IsBasin(c.x, c.y));
        bool Covers(Vector2Int c)
        {
            var p = grid.CellCenter(c.x, c.y);
            return ocean != null && ocean.Covers(new Vector2(p.x, p.z));
        }
        Check("sea_drawn", land.Sea.SeaCells == 0 || (seaCell.x >= 0 && Covers(seaCell)), $"sea at {seaCell}");
        Check("basin_drawn", land.Sea.BasinCells == 0 || (basinCell.x >= 0 && Covers(basinCell)), $"basin at {basinCell}");
        // Far from any water (more than a beach's width): no sea drawn there, even under the ground.
        var far = Cell(c => !land.Sea.IsSea(c.x, c.y) && !land.Sea.IsBasin(c.x, c.y) && FarFromWater(land, c, 48));
        Check("dry_land_not_sea", far.x >= 0 && !Covers(far), $"dry at {far}");
        Check("water_level_is_the_maps", ocean == null || Mathf.Abs(ocean.level - land.WaterLevel) < 1e-4f, $"{ocean?.level}");

        yield return Wait(1f);
        Look(map.Spawn.x - 10, map.Spawn.y + 14, 12f, map.Spawn.x + 30, map.Spawn.y, 0f);
        yield return Wait(2f);
        Shot("land");
        yield return Wait(0.5f);
        if (seaCell.x >= 0)
        {
            Look(seaCell.x - 30, seaCell.y + 20, 14f, seaCell.x, seaCell.y, 0f);
            yield return Wait(2f);
            Shot("sea");
            yield return Wait(0.5f);
        }

        var from = wizard.transform.position;
        var yard = grid.yardCells;
        yield return WalkFor(map.Spawn.x + 20 - yard.x, map.Spawn.y - yard.y, 8f);
        float walked = Vector3.Distance(from, wizard.transform.position);
        Metric("walked_m", walked);
        Check("walks", walked > 5f, $"{walked:0.0} m in 8 s toward 20 cells east");
    }

    // Whether no sea or basin cell is within this many cells either way.
    static bool FarFromWater(MapLand land, Vector2Int c, int reach)
    {
        for (int y = c.y - reach; y <= c.y + reach; y += 4)
            for (int x = c.x - reach; x <= c.x + reach; x += 4)
                if (land.Sea.IsBasin(x, y) || (x >= 0 && y >= 0 && x < land.Map.W && y < land.Map.H && land.Sea.IsSea(x, y))) return false;
        return true;
    }

    // The map's first cell that's so, scanning from its middle row outward; (-1, -1) if none.
    Vector2Int Cell(System.Func<Vector2Int, bool> so)
    {
        int w = World.W, h = World.H;
        foreach (int dy in Enumerable.Range(0, h).Select(k => k % 2 == 0 ? k / 2 : -(k + 1) / 2))
        {
            int y = h / 2 + dy;
            if (y < 0 || y >= h) continue;
            for (int x = 0; x < w; x++)
                if (so(new Vector2Int(x, y))) return new Vector2Int(x, y);
        }
        return new Vector2Int(-1, -1);
    }
}
