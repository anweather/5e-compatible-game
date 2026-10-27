using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Chem;
using UnityEngine;

// The probe's defense scenario (docs/FAST_LOOP.md): the defense field north
// of the yard (DefenseFieldBuilder, DefenseField, World.Defense.cs). The
// wizard stands on the start plate; the first raiders come through the gate
// and walk the path toward the flag; a Fireball lands on the leader; a Wall of
// Stone goes up across the path ahead of the next; the raiders go round it,
// and one reaching the flag costs a life. Shots of the field, mid-wave and of
// the wall.
public class DefenseProbe : ProbeScenario
{
    DefenseField defense;

    protected override IEnumerator Run()
    {
        defense = FindAnyObjectByType<DefenseField>();
        Check("field_built", defense != null && defense.gate != null && defense.flag != null && defense.banner != null, "a Defense field with its gate, flag and banner");
        if (defense == null || defense.gate == null || defense.flag == null) yield break;
        var gate = defense.GateCell;
        var flag = defense.FlagCell;
        Metric("path_squares", Mathf.Abs(flag.x - gate.x) + Mathf.Abs(flag.y - gate.y));
        Check("path_length", Mathf.Abs(flag.x - gate.x) >= 33 && Mathf.Abs(flag.x - gate.x) <= 47 && flag.y == gate.y, $"gate {gate}, flag {flag}: one row, 50 to 70 m");
        Check("path_is_dirt", Enumerable.Range(gate.x, flag.x - gate.x + 1).Count(x => World.At(x, gate.y)?.T == "dirt") >= (flag.x - gate.x) * 0.8f, "the path's squares are dirt");
        Overview();
        yield return Wait(1f);
        Shot("field");

        yield return StartWaves();
        if (!defense.Running) yield break;
        yield return Walking(flag);
        yield return Fireball(flag);
        yield return Wall(flag);
        yield return LifeLost();
    }

    Vector3 Middle => (defense.gate.position + defense.flag.position) / 2f;

    // From the south, high, looking over the whole path.
    void Overview() => Cut(Middle + V(0f, 26f, -34f), Middle);

    IEnumerator StartWaves()
    {
        Teleport(defense.startPlate + Vector3.up * 0.1f);
        int mark = ConsoleMark;
        yield return Wait(2f);
        Check("plate_starts_waves", defense.Running, "standing on the start plate started the waves");
        Check("start_logged", ConsoleSaid("raiders are coming", mark) || LogSince(0).Any(l => l.Contains("Wave 1 of")), "the start in the log");
        // Off the plate, beside the flag, to watch.
        var watch = defense.flag.position + V(-4f, 0f, -9f);
        Teleport(new Vector3(watch.x, TerrainGround(watch) + 0.05f, watch.z));
    }

    // The first raider comes through the gate and gets nearer the flag.
    IEnumerator Walking(Vector2Int flag)
    {
        float waited = 0f;
        while (World.Raiders.Count == 0 && waited < 10f)
        {
            waited += Time.deltaTime;
            yield return null;
        }
        Metric("first_raider_s", waited);
        Check("raiders_spawn", World.Raiders.Count > 0, $"a raider through the gate after {waited:0.0} s");
        if (World.Raiders.Count == 0) yield break;
        var first = World.Raiders[0];
        int before = Mathf.Abs(flag.x - first.X) + Mathf.Abs(flag.y - first.Y);
        yield return Wait(3f);
        int after = first.Alive ? Mathf.Abs(flag.x - first.X) + Mathf.Abs(flag.y - first.Y) : -1;
        Metric("raider_squares_walked_3s", before - after);
        Check("raiders_walk_to_flag", after >= 0 && after < before - 2, $"{before} squares from the flag, then {after} three seconds on");
        Cut(defense.gate.position + V(6f, 9f, -14f), defense.gate.position + V(14f, 0f, 0f));
        yield return Wait(0.6f);
        Shot("mid-wave");
        Check("raiders_drawn", defense.DrawnRaiders > 0, $"{defense.DrawnRaiders} raiders drawn as creatures");
    }

    // A Fireball a square ahead of the leader, where it will be when the bead lands.
    IEnumerator Fireball(Vector2Int flag)
    {
        if (World.Raiders.Count == 0) yield break;
        var lead = World.Raiders[0];
        int alive = World.Raiders.Count, puffs = defense.Puffs;
        int x = lead.X + System.Math.Sign(flag.x - lead.X);
        puppet.Face(grid.CellCenter(x, lead.Y));
        caster.Cast(SpellIds.Fireball, x, lead.Y, 1);
        yield return Wait(2.5f);
        Metric("fireball_kills", defense.Puffs - puffs);
        Check("fireball_kills", defense.Puffs > puffs, $"{defense.Puffs - puffs} raider(s) beaten ({alive} on the board before, {World.Raiders.Count} after)");
    }

    // A Wall of Stone across the path, eight squares short of the flag: the raiders go round its ends.
    IEnumerator Wall(Vector2Int flag)
    {
        int column = flag.x - 8;
        int top = flag.y - 3;
        int costBefore = World.PathCostAt(column - 1, flag.y);
        caster.Cast(SpellIds.WallOfStone, column, top, 2);
        yield return Wait(2f);
        var wallRows = Enumerable.Range(top, 6).Where(y => World.IsRaisedWall(World.At(column, y))).ToList();
        Check("wall_raised", wallRows.Count >= 4, $"{wallRows.Count} wall squares across the path at x {column}");
        Cut(grid.CellCenter(column, flag.y) + V(-10f, 14f, -16f), grid.CellCenter(column, flag.y));
        yield return Wait(0.5f);
        Shot("wall");
        int costAfter = World.PathCostAt(column - 1, flag.y);
        Metric("path_cost_before_wall", costBefore);
        Metric("path_cost_after_wall", costAfter);
        var seen = new HashSet<int>();
        for (float t = 0f; t < 45f && seen.Count == 0; t += Time.deltaTime)
        {
            foreach (var r in World.Raiders)
                if (r.X == column) seen.Add(r.Y);
            yield return null;
        }
        Check("raiders_reroute", seen.Count > 0 && seen.All(y => !wallRows.Contains(y)) && costAfter > costBefore,
            $"passed the wall's column on rows {string.Join(", ", seen)} (wall on {string.Join(", ", wallRows)}); way's cost {costBefore} then {costAfter}");
    }

    // Sooner or later one gets through: a life lost, on the banner too.
    IEnumerator LifeLost()
    {
        int lives = World.Rules.Sim.Defense.Lives;
        float waited = 0f;
        while (World.Defense.Lives == lives && waited < 60f)
        {
            waited += Time.deltaTime;
            yield return null;
        }
        Metric("lives_left", World.Defense.Lives);
        Check("flag_costs_a_life", World.Defense.Lives < lives, $"lives {World.Defense.Lives}/{lives} after {waited:0.0} s more");
        yield return Wait(0.2f);
        Check("banner_shows_lives", defense.banner.text.Contains($"LIVES {World.Defense.Lives}"), $"the banner reads \"{defense.banner.text.Replace('\n', ' ')}\"");
        Check("hud_line", defense.HudLine().StartsWith("Wave ", System.StringComparison.Ordinal), defense.HudLine());
        Overview();
        yield return Wait(0.5f);
        Shot("end");
    }
}
