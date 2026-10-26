using System.Collections;
using UnityEngine;

// The probe's blast scenario (docs/FAST_LOOP.md): Fireball goes off with a bang
// (its "crater" and "push" in spells.json). Cast on the meadow from the bar,
// as a player casts it. Checks the crater is in the cells (two steps down in
// the middle, one round it), the terrain is drawn dug out under it, and the
// fire still takes round the edge.
public class BlastProbe : ProbeScenario
{
    protected override IEnumerator Run()
    {
        Place(4, 10);
        var at = Yard(12, 4);
        var cell = grid.CellAt(at);
        float before = TerrainGround(at);
        Cut(at + V(-4f, 7f, -7f), at);
        yield return Wait(0.5f);
        yield return CastFromBar(SpellIds.Fireball, 12, 4, 0, 3f);

        int middle = World.At(cell.x, cell.y).Height, ring = World.At(cell.x + 1, cell.y + 1).Height;
        Check("crater_middle", middle == -2, $"cell {cell}: height {middle}");
        Check("crater_ring", ring == -1, $"height {ring} beside it");
        float after = TerrainGround(at);
        Metric("ground_drop_m", before - after);
        Check("crater_drawn", before - after > 0.6f, $"the terrain {before:0.00} m before, {after:0.00} m after");
        int burning = 0;
        for (int dy = -2; dy <= 2; dy++)
            for (int dx = -2; dx <= 2; dx++)
                if (World.At(cell.x + dx, cell.y + dy).Fire > 0) burning++;
        Metric("burning_in_blast", burning);
        Check("edge_burns", burning > 0, $"{burning} of the blast's squares burning");
        Shot("crater");
    }
}
