using System.Collections;
using System.Linq;
using Chem;
using UnityEngine;

// The probe's frost scenario (docs/FAST_LOOP.md): Ray of Frost from the lake's
// shore out over the water, as a player casts it. The wizard stands on the
// dry square nearest the yard with lake water south of it and casts south.
// Checks that the water froze, that the ice is drawn above the lake's own
// surface (not hidden under it) and that it can be stood on.
public class FrostProbe : ProbeScenario
{
    protected override IEnumerator Run()
    {
        if (!FindShore(out var shore))
        {
            Check("lake_shore_found", false, "no dry square with lake water south of it");
            yield break;
        }
        Check("lake_shore_found", true, $"cell {shore}");
        var yard = grid.yardCells;
        Place(shore.x - yard.x, shore.y - yard.y);
        var target = new Vector2Int(shore.x, shore.y + 3);
        var at = grid.CellCenter(target.x, target.y);
        Cut(at + V(9f, 7f, 9f), at);
        yield return Wait(0.5f);
        Check("bar_has_ray_of_frost", caster.Select(SpellIds.RayOfFrost));
        puppet.Face(at);
        caster.Cast(SpellIds.RayOfFrost, shore.x, shore.y + 1, 2);
        yield return Wait(2f);

        var line = World.SpellCells(World.Rules.Spell(SpellIds.RayOfFrost), shore.x, shore.y + 1, 2);
        int wet = line.Count(c => World.At(c.x, c.y).Water > 0);
        int frozen = line.Count(c => World.At(c.x, c.y).Frozen && World.At(c.x, c.y).Water > 0);
        Metric("line_cells_wet", wet);
        Metric("line_cells_frozen", frozen);
        Check("water_froze", wet > 0 && frozen == wet, $"{frozen}/{wet} wet squares in the beam frozen");

        // The ice: a collider at the frozen squares, at or above the lake's drawn surface.
        var lake = GameObject.Find("Lake");
        float lakeY = lake != null ? lake.transform.position.y : float.NegativeInfinity;
        var ice = line.FirstOrDefault(c => World.At(c.x, c.y).Frozen);
        var top = grid.CellCenter(ice.x, ice.y) + Vector3.up * 30f;
        bool hit = Physics.Raycast(top, Vector3.down, out var h, 60f, ~0, QueryTriggerInteraction.Ignore);
        Metric("ice_y", hit ? h.point.y : -99f);
        Metric("lake_y", lakeY);
        Check("ice_stands_on_lake", hit && h.collider.name == "Ice" && h.point.y >= lakeY - 0.02f,
            hit ? $"ray hits '{h.collider.name}' at {h.point.y:0.00} m, the lake's surface at {lakeY:0.00} m" : "nothing under the ray");
        Shot("ice");

        // Sleet Storm out on the open water: a wide circle of ice.
        var far = new Vector2Int(shore.x, shore.y + 9);
        Check("bar_has_sleet_storm", caster.Select(SpellIds.SleetStorm));
        caster.Cast(SpellIds.SleetStorm, far.x, far.y, 2);
        yield return Wait(2f);
        var circle = World.SpellCells(World.Rules.Spell(SpellIds.SleetStorm), far.x, far.y, 2);
        int iced = circle.Count(c => World.At(c.x, c.y).Frozen && World.At(c.x, c.y).Water > 0);
        Metric("sleet_cells_frozen", iced);
        Check("sleet_storm_freezes_a_circle", iced >= 40, $"{iced} of {circle.Count} squares frozen");
        Cut(grid.CellCenter(far.x, far.y) + V(14f, 12f, 14f), grid.CellCenter(far.x, far.y));
        yield return Wait(0.5f);
        Shot("sleet");
    }

    // The dry, open square outside the yard nearest its middle with lake water two squares south.
    bool FindShore(out Vector2Int shore)
    {
        var yard = grid.yardCells;
        var middle = new Vector2Int(yard.x + yard.width / 2, yard.y + yard.height / 2);
        shore = default;
        float best = float.MaxValue;
        for (int y = 0; y < World.H - 3; y++)
            for (int x = 0; x < World.W; x++)
            {
                var c = World.At(x, y);
                if (yard.Contains(new Vector2Int(x, y)) || c.Water > 0 || World.IsBlocking(c) || World.At(x, y + 2).Water == 0) continue;
                float d = (new Vector2Int(x, y) - middle).sqrMagnitude;
                if (y > middle.y && d < best)
                {
                    best = d;
                    shore = new Vector2Int(x, y);
                }
            }
        return best < float.MaxValue;
    }
}
