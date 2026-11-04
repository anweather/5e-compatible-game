using System.Collections;
using Chem;
using UnityEngine;

// The probe's cup scenario (docs/design/world-acts.md): a ball and a cup placed through the real MCP server beside the
// wizard, as Claude would. The course's parts move to them, Fire Bolt (no force of its own) moves the ball, and when it
// goes in the fireworks go off over the cup.
public class CupProbe : ProbeScenario
{
    const float Settle = 1.5f;
    const int Apart = 6, MostShots = 8, Tries = 6;

    string said;

    IEnumerator Say(string tool, string args)
    {
        var call = RoomMcpProbe.Post(RoomMcpProbe.Tool(tool, args));
        yield return RoomMcpProbe.Until(call);
        said = call.Result.body ?? "";
        Check(tool, call.Result.status == 200 && !said.Contains("\"isError\":true"), said.Length > 240 ? said.Substring(0, 240) : said);
    }

    static float Flat(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

    protected override IEnumerator Run()
    {
        var server = RulesMcpServer.Instance;
        if (!server.Serving) server.Serving = true;
        yield return Wait(Settle);
        var me = grid.CellAt(WizardRig.FindLocal().transform.position);
        yield return Say("act", $"{{\"what\":\"cup\",\"near\":\"[{me.x}, {me.y}]\",\"within\":10}}");
        yield return Wait(Settle);
        var g = World.Golf;
        // On the cup's row, as a player lines up a shot: the first open cell back from it.
        for (int i = 0; i < Tries && g != null && !said.Contains("golf-ball (act"); i++)
        {
            yield return Say("act", $"{{\"what\":\"golf-ball\",\"near\":\"[{g.HoleX - Apart - i}, {g.HoleY}]\",\"within\":0}}");
            yield return Wait(Settle);
        }
        Check("the_round_is_placed", g != null && g.Placed && g.InPlay && !g.NoCup, g != null ? $"ball {g.X}, {g.Y}, cup {g.HoleX}, {g.HoleY}" : "no round");
        if (g == null || g.NoCup) yield break;
        var course = grid.GetComponentInChildren<GolfCourse>();
        Check("the_course_is_drawn", course != null, "GolfCourse");
        if (course == null) yield break;
        Check("the_ball_is_drawn_there", Flat(course.BallAt, grid.CellCenter(g.X, g.Y)) < grid.cellSize, $"{Flat(course.BallAt, grid.CellCenter(g.X, g.Y)):0.00} m off");
        Check("the_flag_stands_there", Flat(course.transform.Find("Flag pole").position, grid.CellCenter(g.HoleX, g.HoleY)) < grid.cellSize, "flag pole");
        Cut(course.BallAt + V(-3f, 3f, -7f), course.BallAt + V(3f, 0.5f, 0f));
        yield return Wait(0.3f);
        Shot("placed");

        int from = g.X;
        for (int i = 0; i < MostShots && !g.Holed; i++)
        {
            World.Cast(SpellIds.FireBolt, g.X, g.Y, g.X < g.HoleX ? 1 : 3);
            yield return Wait(Settle);
        }
        Check("a_bolt_moved_it", g.Strokes > 0, $"{g.Strokes} strokes, from {from} to {g.X}");
        Check("holed", g.Holed, $"{g.Strokes} strokes, ball at {g.X}, {g.Y}");
        Check("fireworks_go_off", FindAnyObjectByType<TowerFireworks>() != null, "fireworks over the cup");
        // From high up, clear of the rocks: they burst six metres and more over the cup.
        var over = grid.CellCenter(g.HoleX, g.HoleY) + Vector3.up * 8f;
        Cut(over + V(-12f, 22f, -12f), over);
        yield return Wait(1.2f);
        Shot("fireworks");
    }
}
