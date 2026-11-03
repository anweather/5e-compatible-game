using System.Collections;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Chem;
using Newtonsoft.Json.Linq;
using Unity.Cinemachine;
using UnityEngine;

// The probe's rowburn scenario: the stucco house through the MCP, looked at from a wizard's height
// (the street, the door, inside once it's open, its details), walked into and against, then burnt by
// a fireball to a ruin, with what the fire costs in frames. Its stucco is laid over timber, so it
// burns: the plain fixture's only breaks. With --shots it films; without, the frame numbers are clean.
public class RowBurnProbe : RowHousesProbe
{
    const string Timbered = "{ \"name\": \"Stucco wall\", \"tags\": [\"blocking\", \"trellis\"], \"burn\": { \"catch\": 0.35, \"fuel\": 60, \"leaves\": \"rubble\", \"look\": \"wood\" }, \"break\": { \"leaves\": \"rubble\" }, \"look\": { \"family\": \"wall\", \"form\": \"built\", \"color\": [0.84, 0.71, 0.54] } }";
    const string Parts = "[{ \"kind\": \"chimney\", \"on\": \"east\", \"at\": 8 }, { \"kind\": \"viga\", \"on\": \"front-e\", \"every\": 0.9 }, { \"kind\": \"canale\", \"on\": \"front-w\", \"at\": 3 }, { \"kind\": \"porch\", \"on\": \"east\", \"at\": 4, \"width\": 3, \"depth\": 1.5 }]";
    const float Eye = 1.7f, Above = 11f, MostBurn = 60f, Quiet = 4f, FilmEvery = 0.25f, HitchMs = 50f, WalkSeconds = 3f, DoorBurn = 12f;
    const int Settle = 30, ClipWidth = 1280, ClipHeight = 720, ClipQuality = 85;

    Camera film;
    RenderTexture target;
    Texture2D page;
    int frame;

    protected override IEnumerator Run()
    {
        houses = grid.GetComponent<RowHouses>();
        Check("row_houses_here", houses != null && RulesMcpServer.Url != null, RulesMcpServer.Url);
        if (houses == null || RulesMcpServer.Url == null) yield break;
        yield return Tool("find_site", new JObject { ["width"] = 14, ["depth"] = 8, ["near"] = "lake", ["side"] = "north", ["within"] = 40 });
        var site = Regex.Matches(answer ?? "", @"\[(\d+), ?(\d+)\]").Cast<Match>().LastOrDefault() ?? Match.Empty; // on grass; the nearest stand on the shore's banks
        Check("a_site", site.Success, answer);
        if (!site.Success) yield break;
        int x = int.Parse(site.Groups[1].Value, CultureInfo.InvariantCulture), y = int.Parse(site.Groups[2].Value, CultureInfo.InvariantCulture);
        Stand(x + 7, y + 11);
        yield return null;
        yield return Tool("apply", new JObject { ["mod"] = Mod(x, y), ["title"] = Title });
        yield return Until(() => houses.Root(House) != null);
        Check("the_house_goes_in", houses.Root(House) != null, answer);
        if (houses.Root(House) == null) yield break;
        BeginFilm();
        yield return null;
        Still("street", Cell(x + 7, y + 13), Cell(x + 7, y + 4));
        Still("high", Cell(x + 7, y + 13) + Vector3.up * Above, Cell(x + 4, y + 2));
        Still("details", Cell(x + 17, y + 12), Cell(x + 12, y + 6));
        Still("porch", Cell(x + 18, y + 3), Cell(x + 14, y + 3));

        yield return Walks(x, y);
        yield return Burn(x, y);
        EndFilm();
        yield return RevertAll();
    }

    /// <summary>The fixture, of stucco that burns, with more details than the plain one for the frames.</summary>
    public static string Mod(int x, int y)
    {
        var json = JObject.Parse(ModJson(x, y, Timbered));
        json["world"]["buildings"][House]["parts"] = JArray.Parse(Parts);
        return json.ToString(Newtonsoft.Json.Formatting.None);
    }

    // Sets the wizard down and walks it north as far as it'll go.
    IEnumerator WalkNorth(int x, int fromY, int toY)
    {
        Stand(x, fromY);
        yield return null;
        var walk = puppet.WalkTo(grid.CellCenter(x, toY));
        for (float t = 0f; t < WalkSeconds && walk.MoveNext(); t += Time.deltaTime) yield return walk.Current;
        puppet.Move(Vector2.zero);
    }

    // A wall and a shut door stop a wizard where they're drawn; a door that's gone lets it in.
    IEnumerator Walks(int x, int y)
    {
        var world = grid.World;
        var door = world.BuildingCellsOf(world.BuildingRowOf(House)).First(c => c.Terrain == "door" && c.X >= x + 7 && c.Y >= y + 5);
        yield return WalkNorth(x + 13, y + 11, y + 4);
        var at = caster.transform.position;
        Metric("wall_gap_m", at.z - (grid.CellCenter(x + 13, y + 7).z - grid.cellSize / 2f));
        Check("a_wall_stops_a_wizard", grid.CellAt(at).y >= y + 8, $"at {grid.CellAt(at)}, {at.z - (grid.CellCenter(x + 13, y + 7).z - grid.cellSize / 2f):0.00} m off the wall's face; cell {world.At(x + 13, y + 7).T}");
        yield return WalkNorth(door.X, y + 11, y + 3);
        Check("a_shut_door_stops_a_wizard", grid.CellAt(caster.transform.position).y > door.Y, $"at {grid.CellAt(caster.transform.position)}, door {door.X},{door.Y}");
        Still("at-the-door", caster.transform.position + Vector3.up * Eye, Cell(door.X, door.Y - 2));
        yield return Pane(y);
        ChemEffects.Request(grid, new EffectCommand(EffectKind.Burn, door.X, door.Y, null));
        yield return Until(() => !world.IsBlocking(world.At(door.X, door.Y)) || BurningCells() == 0);
        for (float t = 0f; t < DoorBurn && world.IsBlocking(world.At(door.X, door.Y)); t += Time.deltaTime) yield return null;
        Check("the_door_goes", !world.IsBlocking(world.At(door.X, door.Y)), world.At(door.X, door.Y).T);
        yield return WalkNorth(door.X, y + 11, y + 3);
        Check("a_door_gap_lets_a_wizard_in", grid.CellAt(caster.transform.position).y < door.Y, $"at {grid.CellAt(caster.transform.position)}, door {door.X},{door.Y}");
        yield return null;
        Still("door-gap", Cell(door.X, y + 10), Cell(door.X, y + 2));
        Still("inside", caster.transform.position + Vector3.up * Eye, Cell(x + 13, y + 3));
        Stand(x + 7, y + 11);
    }

    // A shattered pane leaves a hole to see through, in a wall that stands.
    IEnumerator Pane(int y)
    {
        var world = grid.World;
        var pane = world.BuildingCellsOf(world.BuildingRowOf(House)).First(c => c.Terrain == TerrainIds.Pane && c.Y >= y + 6);
        var glass = houses.Root(House).GetComponent<WindowPanes>();
        int lit = glass.LampsLit;
        int folded() => houses.Root(House).GetComponentsInChildren<MeshFilter>().Where(f => f.name == "Panes").Sum(f => f.sharedMesh.vertices.Where((v, i) => i % 4 == 0 && v == f.sharedMesh.vertices[i + 2]).Count());
        ChemEffects.Request(grid, new EffectCommand(EffectKind.Shatter, pane.X, pane.Y, "", ThingId.None)); // the first hit only cracks it
        yield return Wait(1f);
        ChemEffects.Request(grid, new EffectCommand(EffectKind.Shatter, pane.X, pane.Y, "", ThingId.None));
        yield return Until(() => folded() > 0);
        Check("a_shattered_pane_opens", folded() > 0, $"{world.At(pane.X, pane.Y).T}, {folded()} folded");
        Check("a_shattered_pane_goes_dark", glass.LampsLit < lit, $"{glass.LampsLit} of {lit} lamps lit");
        for (int i = 0; i < Settle; i++) yield return null;
        Check("the_wall_round_it_stands", houses.Ruin(House).Down == 0, $"{houses.Ruin(House).Down} down");
        Still("pane-gone", Cell(pane.X, pane.Y + 4), Cell(pane.X, pane.Y));
    }

    IEnumerator Burn(int x, int y)
    {
        var ruin = houses.Ruin(House);
        int redraws = houses.Redraws;
        Metric("stretches", ruin.Stretches);
        var glass = houses.Root(House).GetComponent<WindowPanes>();
        int lit = glass.LampsLit;
        caster.Cast(SpellIds.Fireball, x + 8, y + 7, 0, 3);
        float burning = 0f, sinceShot = 0f, calm = 0f, worst = 0f, sum = 0f;
        int frames = 0, hitches = 0, shot = 0;
        while (burning < MostBurn && calm < Quiet)
        {
            yield return null;
            float dt = Time.unscaledDeltaTime;
            burning += dt;
            sinceShot += dt;
            calm = BurningCells() > 0 ? 0f : calm + dt;
            frames++;
            sum += dt;
            worst = Mathf.Max(worst, dt);
            if (dt * 1000f > HitchMs) hitches++;
            if (sinceShot < FilmEvery) continue;
            sinceShot = 0f;
            shot++;
            Still("burn", Cell(x + 7, y + 13), Cell(x + 7, y + 5));
        }
        Metric("burn_seconds", burning);
        Metric("burn_frame_ms", sum / Mathf.Max(1, frames) * 1000f);
        Metric("burn_worst_frame_ms", worst * 1000f);
        Metric("burn_hitches", hitches);
        Metric("redraws", houses.Redraws - redraws);
        Metric("worst_redraw_ms", houses.WorstRedrawMs);
        Metric("stretches_down", ruin.Down);
        Metric("film_shots", shot);
        Check("the_fire_takes_walls_down", ruin.Down > 0, $"{ruin.Down} of {ruin.Stretches} stretches down in {burning:0.0} s");
        Check("a_ruin_has_no_roof_where_walls_fell", ruin.Down == 0 || ruin.RoofHurt);
        Check("burnt_windows_go_dark", glass.LampsLit < lit && (ruin.Down * 2 < ruin.Stretches || glass.LampsLit == 0), $"{glass.LampsLit} of {lit} lamps still lit, {ruin.Down} of {ruin.Stretches} stretches down");
        Check("no_redraw_hitch", houses.WorstRedrawMs < 4f, $"{houses.WorstRedrawMs:0.00} ms");
        yield return null;
        Still("ruin", Cell(x + 7, y + 13), Cell(x + 7, y + 4));
        Still("ruin-inside", Cell(x + 9, y + 5), Cell(x + 15, y + 3));
        Still("ruin-chimney", Cell(x + 20, y + 10), Cell(x + 13, y + 4));
    }

    Vector3 Cell(int x, int y)
    {
        var at = grid.CellCenter(x, y);
        at.y = grid.GroundAt(at) + Eye;
        return at;
    }

    void BeginFilm()
    {
        if (string.IsNullOrEmpty(ScreenshotDir)) return;
        film = Camera.main;
        if (film.TryGetComponent<CinemachineBrain>(out var flown)) flown.enabled = false;
        target = new RenderTexture(ClipWidth, ClipHeight, 24);
        page = new Texture2D(ClipWidth, ClipHeight, TextureFormat.RGB24, false);
    }

    void EndFilm()
    {
        if (film == null) return;
        if (film.TryGetComponent<CinemachineBrain>(out var flown)) flown.enabled = true;
        Destroy(page);
        Destroy(target);
        film = null;
    }

    // One frame from a wizard's height, to <ScreenshotDir>/<label>/.
    void Still(string label, Vector3 eye, Vector3 aim)
    {
        if (film == null) return;
        string folder = Path.Combine(ScreenshotDir, label);
        Directory.CreateDirectory(folder);
        film.transform.position = eye;
        film.transform.LookAt(aim);
        film.targetTexture = target;
        film.Render();
        film.targetTexture = null;
        RenderTexture.active = target;
        page.ReadPixels(new Rect(0, 0, ClipWidth, ClipHeight), 0, 0);
        RenderTexture.active = null;
        File.WriteAllBytes(Path.Combine(folder, $"frame_{frame++:00000}.jpg"), page.EncodeToJPG(ClipQuality));
    }
}
