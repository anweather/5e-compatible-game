using System.Collections;
using System.Linq;
using UnityEngine;

// The probe's soak scenario (Andrew, 2026-10-28): Create Water on open ground
// in the yard on a clear day, then how much is left a while later. Thin water
// soaks in and dries up (materials.json soakEvery, World.Soak); the lake and
// the river keep theirs. Numbers: the water poured, what's left of it, and the
// river's and lake's water before and after.
public class SoakProbe : ProbeScenario
{
    protected override IEnumerator Run()
    {
        yield return Switch(At(12f) + ", 'weather': { 'calmPeriods': 0, 'rampMinutes': 0.02, 'weights': " +
            "{ 'clear': 1, 'cloudy': 0, 'windy': 0, 'fog': 0, 'rain': 0, 'storm': 0, 'snow': 0 } }");
        Place(30, 30);
        var cell = grid.YardCell(32, 30);
        var box = new RectInt(cell.x - 10, cell.y - 10, 21, 21);
        int Poured() => Units(box);
        int bodies = BodyWater();
        int before = Poured();
        yield return Cast(SpellIds.CreateWater, 32, 30, 1, 2f);
        int poured = Poured() - before;
        Cut(grid.CellCenter(cell.x, cell.y) + new Vector3(-6f, 6f, -6f), grid.CellCenter(cell.x, cell.y));
        yield return Wait(60f);
        int left = Poured() - before;
        Metric("poured_units", poured);
        Metric("left_units", left);
        Metric("body_units_before", bodies);
        Metric("body_units_after", BodyWater());
        Check("water_poured", poured > 0, $"{poured} units poured");
        Check("thin_water_soaks_in", left < poured / 2, $"{left} of {poured} units left after a minute on dry ground");
        Check("water_bodies_stay", BodyWater() >= bodies * 0.99f, $"the river and lake: {bodies} units, then {BodyWater()}");
        Shot("soaked");
        yield return null;
        yield return null;
    }

    int Units(RectInt r)
    {
        int sum = 0;
        for (int y = r.yMin; y < r.yMax; y++)
            for (int x = r.xMin; x < r.xMax; x++)
                if (World.InBounds(x, y)) sum += World.At(x, y).Water;
        return sum;
    }

    // Deep water's units (cells over sim.water.thin): the lake, the river, the sea.
    int BodyWater() => World.Cells.Where(c => c != null && c.Water > World.Rules.Sim.WaterUnitsPerStep / 2).Sum(c => c.Water);
}
