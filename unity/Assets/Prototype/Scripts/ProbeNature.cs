using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Chem;
using UnityEngine;

// The probe's nature scenario (docs/FAST_LOOP.md): a Fireball burns the
// meadow round a tree west of the yard's pond, Plant Growth (held to charge 2)
// regrows it, and the old tree's stump sprouts a sapling that grows into a
// tree; then Spike Growth lays thorns across the yard's south meadow, the
// wizard walks through them (slower than on open grass, and losing health),
// and a Fire Bolt burns the thorns. Shots of the burnt patch, the regrown one,
// the grown tree, the thorns and the thorns burning.
public class NatureProbe : ProbeScenario
{
    // The yard's tree at (5, 8); the Fireball lands two squares north of it, so its crater misses
    // the tree (shaping would topple it) and its flames reach it.
    const int BurnX = 5, BurnY = 6, TreeX = 5, TreeY = 8;
    // Spike Growth on the south meadow; the wizard walks east along row 13.
    const int SpikeX = 13, SpikeY = 13;

    protected override IEnumerator Run()
    {
        // Noon, clear and calm, so no rain puts the fire out or snow covers the grass.
        yield return Switch(At(12f) + ", 'weather': { 'calmPeriods': 0, 'rampMinutes': 0.02, " +
            "'weights': { 'clear': 1, 'cloudy': 0, 'windy': 0, 'fog': 0, 'rain': 0, 'storm': 0, 'snow': 0 } }");
        yield return Regrow();
        yield return Thorns();
    }

    // --- Plant Growth ------------------------------------------------------------------

    IEnumerator Regrow()
    {
        Place(11, 12);
        var at = Yard(BurnX, BurnY);
        at.y = grid.GroundAt(at);
        Cut(at + V(-2f, 9f, -11f), at);
        var target = grid.YardCell(BurnX, BurnY);
        var area = World.SpellCells(World.Rules.Spell(SpellIds.PlantGrowth), target.x, target.y, 1, 2);
        int grassBefore = Count(area, c => c.T == TerrainIds.Grass);

        yield return CastFromBar(SpellIds.Fireball, BurnX, BurnY, 1, 10f);
        PutOutFires();
        yield return Wait(0.5f);
        int burnt = Count(area, c => c.Cover == CoverIds.Ash);
        int grassBurnt = Count(area, c => c.T == TerrainIds.Grass);
        var tree = grid.YardCell(TreeX, TreeY);
        string treeAfterFire = World.At(tree.x, tree.y).T;
        Metric("grass_before", grassBefore);
        Metric("grass_after_fire", grassBurnt);
        Metric("burnt_squares", burnt);
        Check("fire_burns", burnt >= 15 && grassBurnt < grassBefore, $"{burnt} squares of ash, grass {grassBefore} -> {grassBurnt}");
        Check("tree_burns_to_stump", treeAfterFire == TerrainIds.Stump, $"the tree at ({TreeX}, {TreeY}) is now {treeAfterFire}");
        Shot("burnt");

        // Plant Growth, held to charge 2 (a radius of 5): green shoots ripen outward into grass.
        Check($"bar_has_{SpellIds.PlantGrowth}", caster.Select(SpellIds.PlantGrowth));
        puppet.Face(grid.CellCenter(target.x, target.y));
        caster.Cast(new CastCommand(SpellIds.PlantGrowth, target.x, target.y, 1, 2));
        yield return Wait(0.3f);
        int shoots = Count(area, c => c.T == TerrainIds.Sprouts);
        int ashLeft = Count(area, c => c.Cover == CoverIds.Ash);
        Metric("shoots", shoots);
        Check("ash_cleared", ashLeft == 0 && shoots > 0, $"{shoots} squares of green shoots, {ashLeft} of ash left");
        yield return Wait(6f);
        int grassAfter = Count(area, c => c.T == TerrainIds.Grass);
        int saplings = Count(area, c => c.T == TerrainIds.Sapling);
        Metric("grass_regrown", grassAfter);
        Metric("saplings", saplings);
        Check("grass_regrows", grassAfter > grassBurnt + burnt / 2 && Count(area, c => c.T == TerrainIds.Sprouts) == 0,
            $"grass {grassBurnt} -> {grassAfter} of {area.Count} squares, {saplings} saplings");
        string onStump = World.At(tree.x, tree.y).T;
        Check("stump_sprouts", onStump == TerrainIds.Sapling, $"the old tree's square is {onStump}");
        Shot("regrown");

        // The saplings grow into trees (90 to 240 ticks, plus the wave): the stump's tree is drawn again.
        yield return Wait(25f);
        string grown = World.At(tree.x, tree.y).T;
        int trees = Count(area, c => c.T == TerrainIds.Tree);
        bool drawn = grid.transform.Cast<Transform>().Any(t => t.name == $"{TerrainIds.Tree} {tree.x},{tree.y}" && t.childCount > 0);
        Metric("trees_grown", trees);
        Check("sapling_grows_into_tree", grown == TerrainIds.Tree && drawn, $"the old tree's square is {grown}, drawn {drawn}, {trees} trees in the patch");
        var trunk = grid.CellCenter(tree.x, tree.y);
        trunk.y = grid.GroundAt(trunk);
        Cut(trunk + V(-4f, 4f, -8f), trunk + V(0f, 2.5f, 0f));
        yield return Wait(0.3f);
        Shot("tree");
    }

    // --- Spike Growth --------------------------------------------------------------------

    IEnumerator Thorns()
    {
        var surface = wizard.GetComponent<SurfaceEffects>();
        var vitals = wizard.GetComponent<Vitals>();
        var row = Yard(SpikeX, SpikeY);
        row.y = grid.GroundAt(row);
        Cut(row + V(0f, 8f, -12f), row);

        // On open grass first, for the speed to compare with.
        Place(1, SpikeY);
        yield return Timed(8, SpikeY, 1.6f);
        float open = lastSpeed;

        Place(SpikeX + 6, SpikeY - 4);
        yield return CastFromBar(SpellIds.SpikeGrowth, SpikeX, SpikeY, 1, 0.5f);
        var squares = SpellCells(SpellIds.SpikeGrowth, SpikeX, SpikeY, 1);
        int thorny = Count(squares, c => c.Cover == CoverIds.Thorns);
        int clumps = grid.transform.Cast<Transform>().Count(t => t.name.StartsWith("Thorns ", System.StringComparison.Ordinal));
        Metric("thorny_squares", thorny);
        Metric("thorn_clumps", clumps);
        Check("thorns_grow", thorny >= 60 && clumps >= thorny / 2, $"{thorny} of {squares.Count} squares thorny, {clumps} clumps drawn");
        Shot("thorns");

        Place(SpikeX - 4, SpikeY);
        yield return Wait(0.3f);
        float health = vitals != null ? vitals.Health : 0f;
        yield return Timed(SpikeX + 4, SpikeY, 1.6f);
        float slow = lastSpeed;
        bool felt = surface != null && surface.InThorns;
        float hurt = vitals != null ? health - vitals.Health : 0f;
        Metric("speed_open_mps", open);
        Metric("speed_thorns_mps", slow);
        Metric("health_lost", hurt);
        Check("thorns_slow", open > 0.5f && slow < open * 0.75f && felt, $"{open:0.00} m/s on grass, {slow:0.00} m/s through the thorns (in thorns: {felt})");
        Check("thorns_hurt", hurt >= 1f, $"{hurt:0.0} health lost walking {slow * 1.6f:0.0} m");

        // A Fire Bolt into the thorns: they catch, and burn to ash.
        Place(SpikeX - 8, SpikeY);
        yield return CastFromBar(SpellIds.FireBolt, SpikeX, SpikeY, 1, 1.5f);
        int burning = Count(squares, c => c.Fire > 0);
        Shot("thorns_burning");
        yield return Wait(6f);
        int ash = Count(squares, c => c.Cover == CoverIds.Ash);
        int left = Count(squares, c => c.Cover == CoverIds.Thorns);
        Metric("thorns_burning", burning);
        Metric("thorns_ash", ash);
        Check("thorns_burn", burning > 0 && ash >= 5 && left < thorny, $"{burning} squares alight, then {ash} of ash and {left} thorny of {thorny}");
        Shot("thorns_burnt");
    }

    // The ground the last timed walk covered a second (metres, flat).
    float lastSpeed;

    // Walks toward a yard cell for a while, timing it (lastSpeed).
    IEnumerator Timed(int x, int y, float seconds)
    {
        var from = wizard.transform.position;
        float start = Time.time;
        yield return WalkFor(x, y, seconds);
        var moved = wizard.transform.position - from;
        moved.y = 0f;
        lastSpeed = moved.magnitude / Mathf.Max(0.01f, Time.time - start);
    }

    int Count(List<(int x, int y)> cells, System.Func<Cell, bool> what) => cells.Count(s => what(World.At(s.x, s.y)));
}
