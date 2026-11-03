using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Chem;
using Chem.Mcp;
using Newtonsoft.Json.Linq;
using UnityEngine;

// The probe's scopelooks scenario (docs/design/world-tools.md, "Scoped and mixed looks"): what a
// player asks of Claude, each through the real tools (Workbench, GameHere) and the room as a mod.
// The fire round the village blue; the fire near me green, walking through a burning strip with
// the fire changing back behind me; a blue fire meeting a yellow one, green between them; three
// colours as a patchwork; and a scope on gases, covers, ground and water. With --shots, a film of each.
public class ScopeLooksProbe : LooksModProbe
{
    const string Blue = "[0.25, 0.45, 1]", Green = "[0.2, 0.9, 0.3]", Yellow = "[1, 0.85, 0.1]";
    const string Mod = "scope-look";
    const int Strip = 34, Rows = 3, Near = 7, Past = 16;
    const float Settle = 2f, Show = 3f, WalkSeconds = 7f, FogEvery = 0.5f;

    Workbench bench;
    readonly GameHere here = new();
    RuleSwitcher switcher;
    Vector2Int at;
    string sky;
    bool on;

    protected override IEnumerator Run()
    {
        switcher = grid.GetComponent<RuleSwitcher>();
        bench = new Workbench(ChemGrid.Folders.Rules) { Here = here };
        here.Follow();
        Check("here_is_the_real_player", here.Asking() is { } spot && !here.StandIn && spot.Player == switcher.Me && World.InBounds(spot.X, spot.Y), bench.DescribeHere());
        if (!FindMeadow(out at))
        {
            Check("meadow_found", false, "no open grass near the yard");
            yield break;
        }
        sky = At(12f);
        yield return Switch(sky + ", 'weather': { 'calmPeriods': 0, 'rampMinutes': 0.02, 'weights': { 'clear': 1, 'cloudy': 0, 'windy': 0, 'fog': 0, 'rain': 0, 'storm': 0, 'snow': 0 } }");
        yield return Take("village", Village());
        yield return Take("nearme", NearMe());
        yield return Take("meeting", Meeting());
        yield return Take("patchwork", Patchwork());
        yield return Take("scoped", Everything());
        yield return Off();
        Check("no_scope_on_at_the_end", !grid.LooksScoped);
    }

    IEnumerator Take(string name, IEnumerator scene)
    {
        ClipFilm film = null;
        if (Filming)
        {
            film = new ClipFilm(this, Path.Combine(ScreenshotDir, name));
            film.Roll();
        }
        yield return scene;
        if (film == null) yield break;
        yield return film.Stop();
        Metric($"{name}_seconds", film.Seconds);
    }

    // A draft from set_look, into the room as a mod, as apply does.
    IEnumerator Apply(Draft draft)
    {
        yield return Off();
        string mod = new JObject { ["title"] = Mod, ["patch"] = draft.Patch }.ToString();
        string refused = switcher.Ask(RoomCommand.Apply(switcher.Me, Mod, mod, Mod));
        on = true;
        yield return Wait(0.6f);
        Check("mod_on_" + draft.Id, World.Room == null || World.Room.Mods.Any(m => m.Id == Mod), refused);
    }

    IEnumerator Off()
    {
        if (!on) yield break;
        switcher.Ask(RoomCommand.Revert(switcher.Me, Mod));
        on = false;
        yield return Wait(0.4f);
    }

    Draft Look(string id, string param, string value, string table, string where = null, string by = null, string draft = null) =>
        bench.SetLook(draft, id, param, value, table, out _, (where, by));

    void Light(int x, int y)
    {
        var c = World.At(x, y);
        if (c == null) return;
        c.Fire = 3;
        c.Fuel = 200;
    }

    void Clear(Vector2Int from, int w, int h)
    {
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var c = World.At(from.x + x, from.y + y);
                if (c == null) continue;
                (c.Fire, c.Fuel, c.Gas, c.GasAmt) = (0, 0, null, 0);
            }
    }

    void Stand(Vector2Int cell, Vector3 back)
    {
        PlaceCell(cell.x, cell.y);
        var mid = grid.CellCenter(cell.x, cell.y);
        mid.y = grid.GroundAt(mid);
        Cut(mid + back, mid);
    }

    // A row of fire from well inside the village out past it.
    IEnumerator Village()
    {
        var village = World.Rules == null ? null : bench.Places.Find("hollowmere");
        Check("village_found", village != null);
        if (village == null) yield break;
        var (mx, my) = village.Middle;
        int edge = village.Bounds.Right + 8;
        Stand(new Vector2Int(edge - 6, my + 6), V(4f, 46f, -30f));
        for (int x = mx; x < edge + 14; x++)
            for (int dy = 0; dy < 2; dy++) Light(x, my + 6 + dy);
        yield return Wait(Settle);
        yield return Apply(Look("fire", "color", Blue, "elements", "{\"place\": \"hollowmere\", \"within\": 4}"));
        yield return Wait(Show);
        var inside = grid.FireTint(mx + 2, my + 6);
        var outside = grid.FireTint(edge + 12, my + 6);
        Check("village_fire_blue", inside.Any, $"turn {inside.Turn:0.00}");
        Check("past_the_village_plain", !outside.Any, $"turn {outside.Turn:0.00}");
        Clear(new Vector2Int(mx - 2, my + 4), edge + 20 - mx, 8);
    }

    // The scope follows the wizard along a burning strip; what they've left changes back.
    IEnumerator NearMe()
    {
        yield return Off();
        Stand(at + new Vector2Int(1, -3), V(0f, 18f, -30f));
        var mid = grid.CellCenter(at.x + Strip / 2, at.y);
        mid.y = grid.GroundAt(mid);
        Cut(mid + V(0f, 20f, -34f), mid);
        for (int y = 0; y < Rows; y++)
            for (int x = 0; x < Strip; x++) Light(at.x + x, at.y + y);
        yield return Wait(1f);
        here.Follow();
        yield return Apply(Look("fire", "color", Green, "elements", "{\"around\": \"me\", \"within\": 6}"));
        yield return Wait(1f);
        var first = at + new Vector2Int(1, 1);
        Check("near_me_green", grid.FireTint(first.x, first.y).Any, $"turn {grid.FireTint(first.x, first.y).Turn:0.00}");
        float greenTurn = grid.FireTint(first.x, first.y).Turn;
        for (float t = 0f; t < WalkSeconds; t += Time.deltaTime)
        {
            puppet.Move(Vector2.right);
            yield return null;
        }
        puppet.Move(Vector2.zero);
        yield return Wait(1f);
        var stand = grid.CellAt(wizard.transform.position);
        Metric("near_me_walked_cells", stand.x - (at.x + 1));
        Check("walked_along_it", stand.x - (at.x + 1) >= Past, $"{stand.x - (at.x + 1)} cells");
        Check("behind_me_changed_back", !grid.FireTint(first.x, first.y).Any, $"turn {grid.FireTint(first.x, first.y).Turn:0.00} at {first}, wizard at {stand}");
        Check("fire_flame_changed_back", grid.Flames.FlamesOn(World.Idx(first.x, first.y)).All(f => Mathf.Approximately(f.turn, 0f)), "flames at the first cell");
        int x1 = Mathf.Clamp(stand.x, at.x, at.x + Strip - 1);
        Check("ahead_turned_green", Mathf.Approximately(grid.FireTint(x1, at.y + 1).Turn, greenTurn), $"{grid.FireTint(x1, at.y + 1).Turn:0.00} at {x1}");
        Clear(at - Vector2Int.one * 3, Strip + 6, Rows + 8);
    }

    // Blue from one side, yellow from the other: a fire between them, caught from both, burns green.
    IEnumerator Meeting()
    {
        yield return Off();
        var row = at + new Vector2Int(0, 10);
        Stand(row + new Vector2Int(8, -7), V(0f, 14f, -24f));
        var draft = Look(SpellIds.FireBolt, "color", Blue, "spells");
        Look(SpellIds.Fireball, "color", Yellow, "spells", draft: draft.Id);
        yield return Apply(draft);
        var bolt = World.Rules.Spell(SpellIds.FireBolt);
        var ball = World.Rules.Spell(SpellIds.Fireball);
        // A cast's note reaches its scatter too, so the two are placed to leave one cell between what each noted.
        var left = new Vector2Int(row.x + 2, row.y);
        grid.LightInLook(caster, bolt, caster.LookOf(bolt.Id), new CastCommand(bolt.Id, left.x, left.y, 0));
        int boltEnd = Reach(bolt, left, +1);
        var between = new Vector2Int(boltEnd + 1, left.y);
        int ballAt = between.x + 1 + (between.x + 1 - Reach(ball, between + Vector2Int.right, -1));
        grid.LightInLook(caster, ball, caster.LookOf(ball.Id), new CastCommand(ball.Id, ballAt, left.y, 0));
        var right = new Vector2Int(between.x + 1, left.y);
        Light(boltEnd, left.y);
        Light(right.x, right.y);
        yield return Wait(0.5f);
        Check("a_cell_between_them_clear", World.At(between.x, between.y).Fire == 0, $"fire {World.At(between.x, between.y).Fire}");
        Light(between.x, between.y);
        yield return Wait(1f);
        var want = LookTint.Between(new Color(0.95f, 0.42f, 0.12f), Mixed(Blue, Yellow));
        var got = grid.FireTint(between.x, between.y);
        Check("blue_and_yellow_burn_green", World.At(between.x, between.y).Fire > 0 && Mathf.Abs(Mathf.DeltaAngle(got.Turn * Mathf.Rad2Deg, want.Turn * Mathf.Rad2Deg)) < 12f,
            $"turn {got.Turn:0.00}, wanted {want.Turn:0.00}; blue {grid.FireTint(boltEnd, left.y).Turn:0.00}, yellow {grid.FireTint(right.x, right.y).Turn:0.00}");
        yield return Wait(Show);
        Clear(row - Vector2Int.one * 8, 34, 20);
    }

    // How far along x a spell cast at a cell notes, its scatter counted: the far end the way asked.
    int Reach(SpellDef spell, Vector2Int cast, int way)
    {
        int scatter = spell.Scatter?.Reach ?? 0;
        var cells = World.SpellCells(spell, cast.x, cast.y, 0, 1, 0, 0).Select(c => c.Item1).ToList();
        return way > 0 ? cells.Max() + scatter : cells.Min() - scatter;
    }

    static Color Mixed(string a, string b)
    {
        static LookColour Of(string json)
        {
            var c = JArray.Parse(json);
            return new LookColour((float)c[0], (float)c[1], (float)c[2]);
        }
        var mix = Colors.Mix(Of(a), Of(b));
        return new Color(mix.R, mix.G, mix.B);
    }

    IEnumerator Patchwork()
    {
        yield return Off();
        Stand(at + new Vector2Int(Strip / 2, -3), V(0f, 16f, -26f));
        yield return Apply(Look("fire", "mix", "[\"#ff4020\", \"#2050ff\", \"#30d040\"]", "elements"));
        for (int y = 0; y < Rows + 2; y++)
            for (int x = 0; x < Strip; x++) Light(at.x + x, at.y + y);
        yield return Wait(Show);
        var turns = new HashSet<int>();
        for (int x = 0; x < Strip; x++) turns.Add(Mathf.RoundToInt(grid.FireTint(at.x + x, at.y + 1).Turn * 100f));
        Metric("patchwork_colours", turns.Count);
        Check("three_colours_as_a_patchwork", turns.Count == 3, string.Join(" ", turns));
        Check("same_cell_same_colour", Mathf.Approximately(grid.FireTint(at.x + 5, at.y).Turn, grid.FireTint(at.x + 5, at.y).Turn));
        Clear(at - Vector2Int.one * 3, Strip + 6, Rows + 8);
        yield return Wait(0.5f);
    }

    // One scope on everything else the world's colours reach: inside it the look, outside it plain.
    IEnumerator Everything()
    {
        yield return Off();
        var spot = at + new Vector2Int(6, 16);
        Stand(spot + new Vector2Int(0, -6), V(0f, 16f, -26f));
        string where = $"{{\"at\": [{spot.x}, {spot.y}], \"within\": 6}}";
        var draft = Look(GasIds.Fog, "color", "[1, 0.45, 0.75]", "gases", where);
        Look(CoverIds.Oil, "color", "[0.55, 0.2, 0.8]", "covers", where, draft: draft.Id);
        Look(TerrainIds.Grass, "color", Blue, "terrain", where, draft: draft.Id);
        Look(ElementIds.Water, "color", "[1, 0.45, 0.75]", "elements", where, draft: draft.Id);
        yield return Apply(draft);
        var inside = spot;
        var outside = spot + new Vector2Int(22, 0);
        foreach (var c in new[] { inside, outside }) World.At(c.x + 1, c.y).Cover = CoverIds.Oil;
        var water = spot + new Vector2Int(-3, 3);
        World.At(water.x, water.y).Water = 4;
        for (float t = 0f; t < Show; t += FogEvery)
        {
            foreach (var c in new[] { inside, outside })
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        var cell = World.At(c.x + dx, c.y + dy);
                        cell.Gas = GasIds.Fog;
                        cell.GasAmt = 6;
                    }
            yield return Wait(FogEvery);
        }
        var fog = grid.Fog.ColorAt(World.Idx(inside.x, inside.y));
        var fogOut = grid.Fog.ColorAt(World.Idx(outside.x, outside.y));
        Check("fog_in_the_scope", fog is { } a && a.r > 0.95f && a.g < 0.6f, fog?.ToString());
        Check("fog_out_of_it_plain", fogOut is { } b && Mathf.Abs(b.r - 0.86f) < 0.02f, fogOut?.ToString());
        Check("oil_in_the_scope", grid.SlickColorAt(inside.x + 1, inside.y) is { } o && o.b > 0.7f, grid.SlickColorAt(inside.x + 1, inside.y)?.ToString());
        Check("oil_out_of_it_plain", grid.SlickColorAt(outside.x + 1, outside.y) is { } p && p.b < 0.1f, grid.SlickColorAt(outside.x + 1, outside.y)?.ToString());
        var ground = spot + new Vector2Int(2, -2);
        Check("grass_film_in_the_scope", World.At(ground.x, ground.y).T != TerrainIds.Grass || grid.SlickColorAt(ground.x, ground.y) is { a: > 0.2f }, $"{World.At(ground.x, ground.y).T}: {grid.SlickColorAt(ground.x, ground.y)}");
        Check("no_film_past_it", grid.SlickColorAt(outside.x + 3, outside.y) == null, grid.SlickColorAt(outside.x + 3, outside.y)?.ToString());
        Check("water_film_in_the_scope", grid.SlickColorAt(water.x, water.y) is { a: > 0.2f }, grid.SlickColorAt(water.x, water.y)?.ToString());
        Clear(spot - Vector2Int.one * 2, 30, 5);
        foreach (var c in new[] { inside, outside }) World.At(c.x + 1, c.y).Cover = null;
        World.At(water.x, water.y).Water = 0;
    }
}
