using System.Collections;
using System.Linq;
using Chem;
using UnityEngine;

// The probe's roofs scenario (Andrew, 2026-10-28): walls and roofs fail
// separately. A timber house with a slate roof gets a Fire Bolt on its walls:
// they burn down and the slate falls onto what's left. A stone house with a
// thatch roof gets a fire beside its walls (grease, then a Fire Bolt): the
// thatch burns away and the stone stands, sooted round where it burned.
// Numbers and before/after pictures of both.
public class RoofsProbe : ProbeScenario
{
    protected override IEnumerator Run()
    {
        yield return Switch(At(12f) + ", 'weather': { 'calmPeriods': 0, 'rampMinutes': 0.02, 'weights': " +
            "{ 'clear': 1, 'cloudy': 0, 'windy': 0, 'fog': 0, 'rain': 0, 'storm': 0, 'snow': 0 } }");
        var houses = FindObjectsByType<VillageHouse>(FindObjectsSortMode.None);
        var timber = houses.FirstOrDefault(h => h.walls == WallKind.Timber && h.cover == RoofCover.Slate);
        var stone = houses.FirstOrDefault(h => h.walls == WallKind.Stone && h.cover == RoofCover.Thatch);
        Check("houses_found", timber != null && stone != null, $"timber with slate {timber != null}, stone with thatch {stone != null}");
        if (timber == null || stone == null) yield break;

        float roofBefore = Roof(timber).transform.position.y;
        var colorsBefore = BodyColors(stone);
        int stoneBefore = Cells(stone).Count(c => World.At(c.x, c.y).T == TerrainIds.Stone);
        Frame(timber);
        yield return Snap("timber-slate-before");
        Frame(stone);
        yield return Snap("stone-thatch-before");

        // The timber house: a Fire Bolt on a wall.
        var wall = Cells(timber).FirstOrDefault(c => World.At(c.x, c.y).T == TerrainIds.LogWall);
        Teleport(timber.transform.position - timber.transform.forward * 7f + Vector3.up * 0.1f);
        caster.Cast(SpellIds.FireBolt, wall.x, wall.y, 1);
        // The stone house: grease beside a wall, set alight.
        var beside = Beside(stone);
        Teleport(stone.transform.position - stone.transform.forward * 7f + Vector3.up * 0.1f);
        caster.Cast(SpellIds.Grease, beside.x, beside.y, 1);
        yield return Wait(0.5f);
        caster.Cast(SpellIds.FireBolt, beside.x, beside.y, 1);
        yield return Wait(50f);

        int logLeft = Cells(timber).Count(c => World.At(c.x, c.y).T == TerrainIds.LogWall);
        var slate = Roof(timber);
        float drop = roofBefore - slate.transform.position.y;
        int stoneLeft = Cells(stone).Count(c => World.At(c.x, c.y).T == TerrainIds.Stone);
        bool thatchGone = !Roof(stone).enabled;
        var colorsAfter = BodyColors(stone);
        float soot = colorsBefore.Length == 0 || colorsAfter.Length != colorsBefore.Length ? 0f
            : (float)Enumerable.Range(0, colorsBefore.Length).Count(v => colorsAfter[v].grayscale < colorsBefore[v].grayscale * 0.8f) / colorsBefore.Length;
        Metric("timber_log_walls_left", logLeft);
        Metric("slate_drop_m", drop);
        Metric("stone_walls_left", stoneLeft);
        Metric("stone_soot", soot);
        Check("timber_walls_burn_down", logLeft == 0, $"{logLeft} log wall cells left");
        Check("slate_falls_in", slate.enabled && drop > 1f, $"the slate roof is {(slate.enabled ? "there" : "gone")}, {drop:0.0} m lower");
        Check("thatch_burns_away", thatchGone, thatchGone ? "the thatch is gone" : "the thatch still stands");
        Check("stone_stands", stoneLeft > 0 && stoneLeft == stoneBefore, $"stone wall cells {stoneBefore} -> {stoneLeft}");
        Check("stone_sooted", soot > 0.02f, $"{soot:P0} of the stone house's vertices sooted");
        Frame(timber);
        yield return Snap("timber-slate-after");
        Frame(stone);
        yield return Snap("stone-thatch-after");
    }

    IEnumerator Snap(string label)
    {
        yield return Wait(0.4f);
        Shot(label);
        yield return null;
        yield return null;
    }

    static Renderer Roof(VillageHouse h) => h.GetComponentsInChildren<Renderer>(true).First(r => r.name == "Roof");

    // The body's vertex colours (CharNear darkens them round a fire).
    static Color[] BodyColors(VillageHouse h)
    {
        var body = h.GetComponentsInChildren<MeshFilter>(true).FirstOrDefault(f => f.name == "Body");
        return body != null ? body.sharedMesh.colors : System.Array.Empty<Color>();
    }

    // The house's footprint cells: wall cells under its box.
    System.Collections.Generic.List<Vector2Int> Cells(VillageHouse h)
    {
        var box = h.GetComponent<BoxCollider>();
        var list = new System.Collections.Generic.List<Vector2Int>();
        var centre = grid.CellAt(h.transform.position);
        for (int y = centre.y - 6; y <= centre.y + 6; y++)
            for (int x = centre.x - 6; x <= centre.x + 6; x++)
            {
                if (!World.InBounds(x, y)) continue;
                var q = h.transform.InverseTransformPoint(grid.CellCenter(x, y)) - box.center;
                if (Mathf.Abs(q.x) <= box.size.x / 2f + 0.3f && Mathf.Abs(q.z) <= box.size.z / 2f + 0.3f) list.Add(new Vector2Int(x, y));
            }
        return list;
    }

    // An open cell beside the house's front wall.
    Vector2Int Beside(VillageHouse h)
    {
        var walls = Cells(h);
        var front = h.transform.position - h.transform.forward * (h.depth / 2f + 0.9f);
        var at = grid.CellAt(front);
        return walls.Contains(at) ? at + Vector2Int.down : at;
    }

    void Frame(VillageHouse h)
    {
        var t = h.transform;
        // High over its street, so neighbouring houses aren't in the way.
        Cut(t.position - t.forward * 10f + t.right * 4f + Vector3.up * 11f, t.position + Vector3.up * 1f);
    }
}
