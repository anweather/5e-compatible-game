using System.Collections;
using System.Collections.Generic;
using Chem;
using UnityEngine;

// The probe's playtestlooks scenario, on the nine (unity/Temp/map = nine): the places of Andrew's playtest of 2026-11-10 (#220),
// each from a fixed eye, at 11 in clear weather, into --shots <folder>, so a fix has a before and an after from the same
// spot: a sparse wood at eye height (where the near trees turn to impostors), the coast's dock from the mountain by the
// falls at the near-layer cut and either side of it, the lake and the sea from over the falls, the far view to the four
// quarters and the map's edge from above, the valley's edge where the biome ground starts, and a brush patch beside it at
// eye height. Metrics: the cuts in force (the Default layer's, the trees', the haze's 98%), the brush dressing standing off
// its ground, and the butterflies' homes round the spawn and in the dry country. Checks: on the nine, each place found.
public class PlaytestLooksProbe : ProbeScenario
{
    const float Eye = 1.7f, Settle = 5f, Up = 30f, High = 120f;

    protected override IEnumerator Run()
    {
        yield return Switch(At(11f) + ", 'weather': { 'calmPeriods': 0, 'rampMinutes': 0.02, 'weights': " +
                            "{ 'clear': 1, 'cloudy': 0, 'windy': 0, 'fog': 0, 'rain': 0, 'storm': 0, 'snow': 0 } }");
        for (float t = 0f; t < 20f && (FarRing.Instance == null || !FarRing.Instance.IsReady); t += Time.deltaTime) yield return null;
        var far = FarRing.Instance;
        Check("on_the_nine", far != null && far.IsReady, far == null ? "no far ring (not the nine?)" : "far ring up");
        if (far == null) yield break;
        var cam = Camera.main;
        Metric("default_layer_cut_m", cam.layerCullDistances[0]);
        Metric("cut_spherical", cam.layerCullSpherical ? 1 : 0);
        Metric("haze_98_m", far.HazeMetres);
        Metric("tree_prefabs_m", TreeImpostors.Near);
        Metric("tree_impostors_m", TreeImpostors.Far);
        Metric("terrains_drawn_chunks", MapTerrains.Sight());

        var spawn = wizard.transform.position;
        yield return Trees(spawn);
        yield return Dock();
        yield return Water();
        yield return FarView();
        yield return Edge();
        Butterflies();
    }

    // Puts the wizard under the eye (the rings and tiles follow it), cuts to the view, waits for it to settle, shoots.
    IEnumerator View(string label, Vector3 eye, Vector3 at, float settle = Settle)
    {
        Teleport(new Vector3(eye.x, grid.GroundAt(eye) + 0.05f, eye.z));
        for (float t = 0f; t < settle; t += Time.deltaTime)
        {
            Cut(eye, at);
            yield return null;
        }
        Shot(label);
        yield return null;
        yield return null;
    }

    Vector3 OnGround(Vector3 p, float up) => new(p.x, grid.GroundAt(p) + up, p.z);

    // A sparse wood: of points round the spawn, the one with the fewest trees within 150 m but some past 60 m, and none
    // within 12 m; looked at toward those trees.
    IEnumerator Trees(Vector3 spawn)
    {
        Vector3 best = Vector3.zero, toward = Vector3.zero;
        int bestCount = int.MaxValue;
        for (int gz = -6; gz <= 6; gz++)
            for (int gx = -6; gx <= 6; gx++)
            {
                var c = spawn + new Vector3(gx * 48f, 0f, gz * 48f);
                var (near, ring, middle) = Wood(grid.CellAt(c));
                if (near > 0 || ring < 12 || ring >= bestCount) continue;
                bestCount = ring;
                best = c;
                toward = middle;
            }
        Check("sparse_wood_found", bestCount < int.MaxValue, bestCount < int.MaxValue ? $"{bestCount} tree cells sampled 60-150 m round {best:F0}" : "none");
        if (bestCount == int.MaxValue) yield break;
        Metric("sparse_wood_trees_sampled", bestCount);
        var eye = OnGround(best, Eye);
        var at = OnGround(toward, Eye);
        yield return View("trees", eye, at, 8f);
        yield return View("trees-left", eye, eye + Quaternion.Euler(0f, -50f, 0f) * (at - eye), 3f);
    }

    // The tree cells round a dry cell, sampled every third: those within 12 m, those 60-150 m off and their middle
    // (near is 1 for a cell off the map or wet, so it's never picked).
    (int near, int ring, Vector3 middle) Wood(Vector2Int cell)
    {
        var world = World;
        if (!world.InBounds(cell.x, cell.y) || world.At(cell.x, cell.y) is not { Water: 0 }) return (1, 0, Vector3.zero);
        int near = 0, ring = 0;
        var sum = Vector3.zero;
        for (int dy = -100; dy <= 100; dy += 3)
            for (int dx = -100; dx <= 100; dx += 3)
            {
                int x = cell.x + dx, y = cell.y + dy;
                if (!world.InBounds(x, y) || world.At(x, y) is not { } t || t.T != TerrainIds.Tree) continue;
                float d = Mathf.Sqrt(dx * dx + dy * dy) * grid.cellSize;
                if (d < 12f) near++;
                else if (d > 60f && d < 150f)
                {
                    ring++;
                    sum += grid.CellCenter(x, y);
                }
            }
        return (near, ring, ring > 0 ? sum / ring : Vector3.zero);
    }

    // The coast's dock from the mountain by the falls: the eye on the line from the dock to the falls, at the Default
    // layer's cut, 15 m either side of it, and on the mountain itself, 120 m over the ground so the trees don't hide it.
    IEnumerator Dock()
    {
        var dock = GameObject.Find("Dock");
        var falls = GameObject.Find("Falls water");
        Check("dock_found", dock != null && falls != null, "the coast's Dock and the Falls water");
        if (dock == null || falls == null) yield break;
        var d = dock.transform.position;
        var away = falls.transform.position - d;
        away.y = 0f;
        float mountain = away.magnitude;
        away.Normalize();
        Metric("dock_to_falls_m", mountain);
        float cut = Camera.main.layerCullDistances[0];
        foreach (var (label, metres) in new[] { ("dock-in", cut - 15f), ("dock-out", cut + 15f), ("dock-490", 490f), ("dock-520", 520f), ("dock-mountain", mountain + 20f) })
        {
            var eye = OnGround(d + away * metres, High);
            yield return View(label, eye, d + Vector3.up * 2f, 3f);
        }
    }

    // The lake and the sea from over the falls, panned across.
    IEnumerator Water()
    {
        var falls = GameObject.Find("Falls water");
        var dock = GameObject.Find("Dock");
        if (falls == null || dock == null) yield break;
        var eye = Mountain(dock, falls);
        var ahead = dock.transform.position - eye;
        for (int i = 0; i < 3; i++)
            yield return View($"water-{i}", eye, eye + Quaternion.Euler(0f, -35f + 35f * i, 0f) * ahead, i == 0 ? Settle : 1.5f);
    }

    // The mountain by the falls: 20 m past the falls from the dock, 30 m over its ground.
    Vector3 Mountain(GameObject dock, GameObject falls)
    {
        var d = dock.transform.position;
        var away = falls.transform.position - d;
        away.y = 0f;
        return OnGround(d + away.normalized * (away.magnitude + 20f), Up);
    }

    // Far off: the four quarters from the mountain by the falls, a little down; then the nine's edge from 250 m up.
    IEnumerator FarView()
    {
        var falls = GameObject.Find("Falls water");
        var dock = GameObject.Find("Dock");
        if (falls == null || dock == null) yield break;
        var eye = Mountain(dock, falls);
        string[] names = { "north", "east", "south", "west" };
        for (int i = 0; i < 4; i++)
        {
            var dir = Quaternion.Euler(0f, 90f * i, 0f) * Vector3.forward;
            yield return View($"far-{names[i]}", eye, eye + dir * 1000f + Vector3.down * 60f, i == 0 ? Settle : 2f);
        }
        var high = eye + Vector3.up * 250f;
        yield return View("far-edge-high", high, high + new Vector3(-1000f, -300f, -1000f), 3f);
    }

    // The valley's edge where the biome ground starts: the first cell east of the spawn whose biome isn't the pinned
    // square's, from 30 m up and 60 m back; then a brush patch there at eye height. And the brush dressing off its ground.
    IEnumerator Edge()
    {
        var biomes = BiomeGround.Last;
        Check("biomes_read", biomes != null, biomes == null ? "no biome ground" : "read");
        if (biomes == null) yield break;
        var edge = FirstBiomeCell(biomes, grid.CellAt(wizard.transform.position));
        Check("edge_found", edge.x >= 0, edge.x >= 0 ? $"cell {edge} ({biomes.BiomeAt(edge.x, edge.y)})" : "none");
        if (edge.x < 0) yield break;
        var at = OnGround(grid.CellCenter(edge.x, edge.y), 0f);
        var back = OnGround(grid.CellCenter(edge.x - 40, edge.y + 15), 30f);
        yield return View("edge", back, at, Settle);
        var brush = NearestBrush(edge, 120);
        if (brush.x >= 0)
        {
            var b = OnGround(grid.CellCenter(brush.x, brush.y), 0f);
            yield return View("bushes", OnGround(grid.CellCenter(brush.x - 6, brush.y + 3), Eye), b, 3f);
        }
        Floating();
    }

    // The first cell with a biome of its own (out of the pinned square) going east of a cell, else north, west, south.
    Vector2Int FirstBiomeCell(BiomeGround biomes, Vector2Int from)
    {
        foreach (var step in new[] { new Vector2Int(1, 0), new Vector2Int(0, -1), new Vector2Int(-1, 0), new Vector2Int(0, 1) })
            for (int k = 0; k < 900; k++)
            {
                var c = from + step * k;
                if (!World.InBounds(c.x, c.y)) break;
                if (biomes.BiomeAt(c.x, c.y) != null) return c;
            }
        return new Vector2Int(-1, -1);
    }

    Vector2Int NearestBrush(Vector2Int from, int reach)
    {
        for (int r = 0; r < reach; r++)
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != r) continue;
                    int x = from.x + dx, y = from.y + dy;
                    if (World.InBounds(x, y) && World.At(x, y) is { } c && c.T == TerrainIds.Brush) return new Vector2Int(x, y);
                }
        return new Vector2Int(-1, -1);
    }

    // Each brush cell's dressing against the drawn terrain under its middle: how many stand 0.25 m or more off it.
    void Floating()
    {
        int all = 0, off = 0;
        float worst = 0f;
        var terrains = MapTerrains.Instance;
        string prefix = TerrainIds.Brush + " ";
        foreach (Transform t in grid.transform)
        {
            if (!t.name.StartsWith(prefix, System.StringComparison.Ordinal)) continue;
            var drawn = terrains != null ? terrains.HeightAt(t.position) : null;
            if (drawn == null) continue;
            all++;
            float gap = t.position.y - drawn.Value;
            if (Mathf.Abs(gap) >= 0.25f) off++;
            if (Mathf.Abs(gap) > Mathf.Abs(worst)) worst = gap;
        }
        Metric("brush_dressings", all);
        Metric("brush_off_ground", off);
        Metric("brush_worst_gap_m", worst);
    }

    // The butterflies' homes in the tiles drawn round the last view.
    void Butterflies()
    {
        var critters = FindAnyObjectByType<AmbientCritters>();
        if (critters == null) return;
        Metric("butterfly_homes_last_view", critters.Homes().butterflies);
    }
}
