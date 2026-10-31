using System.Collections;
using System.Linq;
using Chem;
using UnityEngine;

// The probe's golf scenario (docs/BACKLOG.md 24): the hole in the valley played
// through the caster, the wizard behind the ball, Gust of Wind after Gust of Wind:
// the tee, the flag and the hole drawn, the HUD's stroke line, the ball drawn where
// the core has it, all the way into the hole. Then the coil puzzle beside it: its
// cells laid, no tree on its pond, a bolt into the pond opens the gate.
public class GolfProbe : ProbeScenario
{
    const int MostStrokes = 14;
    const int North = 0, East = 1, South = 2, West = 3;
    GolfCourse course;

    protected override IEnumerator Run()
    {
        var g = World.Golf;
        course = grid.GetComponentInChildren<GolfCourse>();
        Check("ball_on_the_tee", g != null && g.X == g.TeeX && g.Y == g.TeeY, g != null ? $"at {g.X}, {g.Y}" : "no hole");
        Check("course_drawn", course != null, "GolfCourse");
        if (g == null || course == null) yield break;
        EventLog.HudVisible = true;
        foreach (var part in new[] { "Tee", "Hole", "Flag", "Flag pole", "Golf ball" })
            Check("drawn_" + part.Replace(' ', '_').ToLowerInvariant(), course.transform.Find(part) != null, part);

        Behind(g, East);
        var tee = grid.CellCenter(g.TeeX, g.TeeY);
        tee.y = grid.GroundAt(tee);
        Cut(tee + V(-5f, 3f, -6f), tee + V(1f, 0.5f, 0f));
        yield return Wait(0.8f);
        Shot("tee");
        yield return null;
        yield return null;
        Check("hud_says_how", course.HudLine().Contains("Gust of Wind"), course.HudLine());

        Check("fairway_open", Enumerable.Range(g.TeeX, g.HoleX - g.TeeX + 1).All(x => !World.IsBlocking(World.At(x, g.Y))),
            string.Join(" ", Enumerable.Range(g.TeeX, g.HoleX - g.TeeX + 1).Where(x => World.IsBlocking(World.At(x, g.Y))).Select(x => $"{x}:{World.At(x, g.Y).T}:{World.At(x, g.Y).Height}")));
        yield return Swing(g);
        Check("a_stroke_counts", g.Strokes == 1, $"{g.Strokes} strokes, ball at {g.X}, {g.Y}");
        Check("ball_rolled", g.X > g.TeeX, $"from {g.TeeX} to {g.X}");
        Check("hud_counts", course.HudLine().Contains("1 stroke"), course.HudLine());
        float apart = Flat(course.BallAt, grid.CellCenter(g.X, g.Y));
        Metric("ball_off_its_cell_m", apart);
        Check("ball_drawn_at_its_cell", apart < grid.cellSize, $"{apart:0.00} m off");
        var ballAt = course.BallAt;
        Cut(ballAt + V(-4f, 2.5f, -5f), ballAt);
        yield return Wait(0.3f);
        Shot("after-a-stroke");
        yield return null;
        yield return null;

        while (!g.Holed && g.Strokes < MostStrokes) yield return Swing(g);
        Metric("strokes", g.Strokes);
        Check("holed", g.Holed, $"{g.Strokes} strokes, ball at {g.X}, {g.Y}, hole {g.HoleX}, {g.HoleY}");
        Check("hud_says_holed", g.Holed && course.HudLine().Contains("in the hole"), course.HudLine());
        var hole = grid.CellCenter(g.HoleX, g.HoleY);
        hole.y = grid.GroundAt(hole);
        Cut(hole + V(-4f, 3f, -5f), hole + V(0f, 1.2f, 0f));
        yield return Wait(0.5f);
        Shot("flag");
        yield return null;
        yield return null;
        yield return Coil();
    }

    // The wizard two cells behind the ball, on the side that sends it this way.
    void Behind(GolfState g, int dir)
    {
        var yard = grid.YardCell(0, 0);
        Place(g.X - World.DirX(dir) * 2 - yard.x, g.Y - World.DirY(dir) * 2 - yard.y);
    }

    IEnumerator Swing(GolfState g)
    {
        // Round by the hole's row first, then along it.
        int dir = g.Y != g.HoleY ? (g.Y < g.HoleY ? South : North) : g.X < g.HoleX ? East : West;
        int strokes = g.Strokes;
        Behind(g, dir);
        yield return Wait(0.3f);
        caster.Cast(SpellIds.GustOfWind, g.X - World.DirX(dir), g.Y - World.DirY(dir), dir, 1);
        for (float t = 0f; t < 2f && g.Strokes == strokes; t += Time.deltaTime) yield return null;
        for (float t = 0f; t < 6f && g.Moving; t += Time.deltaTime) yield return null;
        yield return Wait(0.5f);
    }

    IEnumerator Coil()
    {
        var pond = new RectInt(327, 236, 4, 5);
        int pit = 0, wet = 0, treeCells = 0;
        foreach (var at in pond.allPositionsWithin)
        {
            var c = World.At(at.x, at.y);
            if (c.T == TerrainIds.Pit) pit++;
            if (c.Water > 0) wet++;
            if (c.T == TerrainIds.Tree) treeCells++;
        }
        Metric("pond_cells_wet", wet);
        Check("pond_laid", pit == 20 && wet == 20, $"{pit} pit, {wet} wet of 20");
        Check("no_tree_cell_on_the_pond", treeCells == 0, $"{treeCells}");
        var edge = new RectInt(pond.x - 1, pond.y - 1, pond.width + 2, pond.height + 2);
        int standing = 0;
        foreach (var terrain in Terrain.activeTerrains)
        {
            var data = terrain.terrainData;
            standing += data.treeInstances.Count(t => edge.Contains(grid.CellAt(Vector3.Scale(t.position, data.size) + terrain.transform.position)));
        }
        Metric("trees_on_the_pond", standing);
        Check("no_tree_stands_on_the_pond", standing == 0, $"{standing} trees over the pond's cells");

        var coil = new Vector2Int(331, 238);
        var gate = new Vector2Int(336, 238);
        var yard = grid.YardCell(0, 0);
        Place(329 - yard.x, 233 - yard.y);
        var mid = grid.CellCenter(332, 238);
        mid.y = grid.GroundAt(mid);
        Cut(mid + V(-3f, 7f, -11f), mid);
        yield return Wait(1f);
        Check("coil_laid", World.At(coil.x, coil.y).T == "coil" && World.At(gate.x, gate.y).T == "gate", $"{World.At(coil.x, coil.y).T}, {World.At(gate.x, gate.y).T}");
        Shot("coil-gate-shut");
        yield return null;
        yield return null;
        Check("gate_starts_shut", World.IsBlocking(World.At(gate.x, gate.y)), World.At(gate.x, gate.y).T);
        caster.Cast(SpellIds.LightningBolt, 329, 238, South, 1);
        yield return Wait(1.5f);
        Check("bolt_opens_the_gate", !World.IsBlocking(World.At(gate.x, gate.y)), "the gate after a bolt into the pond");
        Shot("coil-gate-open");
        yield return null;
        yield return null;
    }

    static float Flat(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;
}
