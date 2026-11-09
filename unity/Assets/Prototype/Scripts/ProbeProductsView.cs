using System.Collections;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Chem;
using Newtonsoft.Json.Linq;
using Unity.Cinemachine;
using Unity.Netcode;
using UnityEngine;

// The probe's productsview scenario (docs/design/primitives.md P3 and step 2, the view's half): what a material
// leaves, and a mod's own covers, drawn from the rules on two peers. This Editor hosts an Open room and the
// virtual player joins. First the base: a Fire Bolt on one grass lot leaves the base ash, charred paint and no
// film. Then a mod through the real MCP over HTTP, as Claude builds one: a cover "blue-soot" with its own look,
// and grass's burn.ash naming it; a Fire Bolt on a second lot leaves blue soot, charred and wearing its own
// colour as a film, while the base ash stays as it was. Then black ice (primitives §5's first test) cast in a
// slick the host's wizard walks onto, lapsing after LapseTicks: every slick goes, in every chunk, on both
// sides, and the view's rules and mods list follow the lapse. With --shots: stills of the second lot before and
// after it burns, and the black ice film, rendered off screen at 720p (no window needed).
public class ProductsViewProbe : RoomProbe
{
    const string Soot = "blue-soot", Ice = "black-ice", Glaze = "glaze", SootTitle = "Blue soot", IceTitle = "Black ice";
    /// <summary>The soot's look, and how close the film drawn on it must come (a colour channel).</summary>
    static readonly float[] SootColour = { 0.18f, 0.32f, 0.95f };
    const float SameColour = 0.02f;
    /// <summary>The black ice's lapse: 120 ticks (20 s) for filming, where §5's first test has 3600 (ten minutes).</summary>
    const int LapseTicks = 120;
    const int LotNear = 45, LotFar = 75, LotSide = 3;
    const float BurnOut = 50f, Settle = 1.5f, SweepWait = 20f;
    const int Width = 1280, Height = 720, Fps = 30, Quality = 88;

    static readonly HttpClient Http = new();
    string answer;
    int stills;

    protected override ChangeMode Mode => ChangeMode.Open;
    protected override bool StartsWithAMod => false;

    /// <summary>For each cover the probe watches: "id:cells in the world/cells drawn wearing its film".</summary>
    public static string Films(ChemGrid grid) =>
        string.Join(" ", new[] { Soot, Ice, CoverIds.Ash }.Select(id => $"{id}:{grid.World.Cells.Count(c => c.Cover == id)}/{grid.FilmCells(id)}"));

    bool Filming => !string.IsNullOrEmpty(ScreenshotDir);

    static GameObject Host => NetworkManager.Singleton.SpawnManager.GetLocalPlayerObject().gameObject;

    protected override IEnumerator Run()
    {
        if (RulesMcpServer.Url == null)
        {
            Check("mcp_here", false, "no RulesMcpServer");
            yield break;
        }
        yield return base.Run();
    }

    protected override IEnumerator Steps()
    {
        BaseDrawsAsBefore();
        var from = grid.CellAt(Host.transform.position);
        if (!FindLot(from, -1, out var a) || !FindLot(from, 1, out var b))
        {
            Check("two_grass_lots", false, $"none {LotNear} to {LotFar} cells either side of {from}");
            yield break;
        }
        Metric("lot_base_x", a.x);
        Metric("lot_mod_x", b.x);

        // The base: a fire on lot A leaves the base ash.
        yield return Burn(a);
        int baseAsh = World.Cells.Count(c => c.Cover == CoverIds.Ash);
        Metric("base_ash_cells", baseAsh);
        Check("base_fire_leaves_ash", baseAsh > 20, $"{baseAsh} ash cells");
        BaseAshDrawn("base_ash_drawn_as_before");

        // The mod, built through the MCP: a cover of its own, and grass leaving it.
        yield return Tool("add_material", new JObject { ["kind"] = "cover", ["id"] = Soot, ["name"] = SootTitle, ["verbs"] = new JObject { ["look"] = new JObject { ["color"] = new JArray(SootColour[0], SootColour[1], SootColour[2]) } }.ToString() });
        string draft = Regex.Match(answer ?? "", @"[Dd]raft:? ""?(\w[\w-]*)").Groups[1].Value;
        Check("add_material_drafts", draft.Length > 0, answer);
        yield return Tool("set_material", new JObject { ["id"] = TerrainIds.Grass, ["field"] = "burn.ash", ["value"] = $"\"{Soot}\"", ["draft"] = draft });
        Check("set_material_takes_burn_ash", answer != null && !answer.Contains("refused") && !answer.Contains("no field"), answer);
        yield return Tool("apply", new JObject { ["mod"] = draft, ["title"] = SootTitle });
        yield return Agree("soot_on", $"Player 1 switched on {SootTitle}");
        Check("soot_is_a_film", grid.IsFilm(Soot) && CellPaint.IsAsh(Soot, World.Rules), $"film {grid.IsFilm(Soot)}, ash {CellPaint.IsAsh(Soot, World.Rules)}");
        yield return Still("products-view-before", b);

        yield return Burn(b);
        yield return Still("products-view-after", b);
        SootDrawn(b);
        BaseAshDrawn("base_ash_unchanged_beside_the_mod");
        yield return Ask("report");
        if (Theirs == null) yield break;
        Check("client_draws_the_soot_alike", Theirs.films == Mine.films, $"{Theirs.films}, here {Mine.films}");
        CompareHashes("soot_in_step");

        // Black ice: cast, walked on, lapsed.
        yield return Tool("revert", new JObject { ["mod"] = World.Room.Mods.FirstOrDefault(m => m.Title == SootTitle)?.Id ?? draft });
        yield return Agree("soot_off", $"Player 1 switched off {SootTitle}");
        Check("the_soot_is_swept_with_its_mod", World.Cells.All(c => c.Cover != Soot), Films(grid));
        yield return BlackIce(from);
    }

    // Base covers wear a film as they did (oil, grease, frost) and nothing else does; the colours are today's.
    void BaseDrawsAsBefore()
    {
        string[] films = { CoverIds.Oil, CoverIds.Grease, CoverIds.Frost }, none = { CoverIds.Ash, CoverIds.Snow, CoverIds.Snowbank, CoverIds.Vines, CoverIds.Thorns, CoverIds.Embers };
        Check("base_films_as_before", films.All(grid.IsFilm) && !none.Any(grid.IsFilm), string.Join(" ", World.Rules.Covers.Keys.Where(grid.IsFilm)));
        var ash = grid.CoverColor(CoverIds.Ash, Color.magenta);
        Check("base_ash_colour", Near(ash, 0.35f, 0.34f, 0.33f), $"{ash}");
        Check("base_snow_reads_the_rules", World.Rules.Sim.Weather.SnowCover == World.SnowCover && World.Rules.Sim.Weather.BankCover == World.BankCover && World.Rules.Sim.Fire.Ash == CoverIds.Ash);
    }

    // The base ash: charred paint, no film, its own colour.
    void BaseAshDrawn(string name)
    {
        var cells = Enumerable.Range(0, World.Cells.Length).Where(i => World.Cells[i].Cover == CoverIds.Ash).ToList();
        int filmed = cells.Count(i => grid.SlickColorAt(i % World.W, i / World.W) != null);
        int charred = cells.Count(i => Charred(World.Cells[i]));
        Metric(name + "_cells", cells.Count);
        Metric(name + "_filmed", filmed);
        Check(name, cells.Count > 0 && filmed == 0 && charred == cells.Count, $"{cells.Count} ash cells, {filmed} with a film, {charred} charred");
    }

    void SootDrawn(Vector2Int lot)
    {
        var cells = Enumerable.Range(0, World.Cells.Length).Where(i => World.Cells[i].Cover == Soot).ToList();
        Metric("soot_cells", cells.Count);
        Check("the_mod_fire_leaves_soot", cells.Count > 20, $"{cells.Count} soot cells round {lot}");
        if (cells.Count == 0) return;
        var colours = cells.Select(i => grid.SlickColorAt(i % World.W, i / World.W)).ToList();
        int filmed = colours.Count(c => c != null);
        var mean = colours.Where(c => c != null).Aggregate(Color.clear, (s, c) => s + c.Value) / Mathf.Max(1, filmed);
        Metric("soot_filmed", filmed);
        Metric("soot_film_r", mean.r);
        Metric("soot_film_g", mean.g);
        Metric("soot_film_b", mean.b);
        Metric("soot_film_a", mean.a);
        Check("soot_painted_its_own_colour", filmed == cells.Count && Near(mean, SootColour[0], SootColour[1], SootColour[2]), $"{filmed} of {cells.Count} filmed, mean {mean}");
        Check("soot_ground_charred", cells.All(i => Charred(World.Cells[i])));
        var ash = grid.CoverColor(CoverIds.Ash, Color.magenta);
        Check("soot_not_the_base_ash_colour", Mathf.Abs(mean.b - ash.b) > 0.3f, $"{mean} against {ash}");
    }

    bool Charred(Cell c) => CellPaint.Mix(World, c)[CellPaint.Layers.ToList().IndexOf(CellPaint.Charred)] > 0.5f;

    static bool Near(Color c, float r, float g, float b) => Mathf.Abs(c.r - r) <= SameColour && Mathf.Abs(c.g - g) <= SameColour && Mathf.Abs(c.b - b) <= SameColour;

    // A square of grass LotSide cells round its middle, between LotNear and LotFar cells to one side, in an active chunk.
    bool FindLot(Vector2Int from, int side, out Vector2Int lot)
    {
        for (int d = LotNear; d <= LotFar; d++)
            for (int dy = -20; dy <= 20; dy++)
            {
                lot = new Vector2Int(from.x + side * d, from.y + dy);
                if (World.IsActiveAt(lot.x, lot.y) && IsGrassLot(lot)) return true;
            }
        lot = default;
        return false;
    }

    bool IsGrassLot(Vector2Int lot)
    {
        for (int y = -LotSide; y <= LotSide; y++)
            for (int x = -LotSide; x <= LotSide; x++)
            {
                var c = World.At(lot.x + x, lot.y + y);
                if (c == null || c.T != TerrainIds.Grass || c.Cover != null || c.Water != 0) return false;
            }
        return true;
    }

    // A Fire Bolt from the host at a lot, and the fire waited out (or BurnOut seconds).
    IEnumerator Burn(Vector2Int lot)
    {
        Host.GetComponent<SpellCaster>().Cast(SpellIds.FireBolt, lot.x, lot.y, 0);
        for (float t = 0f; t < 8f && BurningCells() == 0; t += Time.unscaledDeltaTime) yield return null;
        for (float t = 0f; t < BurnOut && BurningCells() > 0; t += Time.unscaledDeltaTime) yield return null;
        Metric($"burning_left_{lot.x}", BurningCells());
        yield return Wait(Settle);
    }

    RoomMod iceMod;
    int lapses, redrawsWere;
    Lens lens;

    IEnumerator BlackIce(Vector2Int from)
    {
        yield return IceOn();
        if (iceMod == null) yield break;
        lens = new Lens(grid, Filming ? Path.Combine(ScreenshotDir, "black-ice") : null, new Vector2Int(from.x, from.y + 9), new Vector3(8f, 10f, -12f), true);
        yield return Glazes(from);
        yield return Ask("report");
        if (Theirs == null) yield break;
        Check("client_draws_the_ice", Theirs.films == Mine.films, $"{Theirs.films}, here {Mine.films}");
        var feet = Host.GetComponent<SurfaceEffects>();
        int skids = feet != null ? feet.Skids : 0;
        yield return Skid(from);
        Check("and_skids", feet != null && feet.Skids > skids, $"{(feet != null ? feet.Skids - skids : -1)} skids");
        // Up to the lapse, and a little after.
        yield return lens.Roll(40f, () => World.Tick >= lapses);
        yield return lens.Roll(2.5f, null);
        Metric("ice_frames", lens.Close());
        yield return AfterLapse();
    }

    IEnumerator IceOn()
    {
        int start = World.Tick;
        yield return Tool("apply", new JObject { ["mod"] = IcePatch(), ["title"] = IceTitle });
        yield return Agree("ice_on", $"Player 1 switched on {IceTitle}");
        iceMod = World.Room.Mods.FirstOrDefault(m => m.Title == IceTitle);
        lapses = iceMod != null ? World.LapsesAt(iceMod) : -1;
        redrawsWere = grid.EveryChunkRedraws;
        Metric("ice_lapses_at", lapses);
        Check("ice_lapses", lapses > start, $"{lapses}, now {World.Tick}");
        Check("the_panel_says_time_left", (grid.GetComponent<RuleSwitcher>().LapseNote(iceMod?.Id ?? "") ?? "").StartsWith("lapses in", System.StringComparison.Ordinal));
    }

    // Three Glazes in a line north of the host, a slick to cross; drawn as a film in its own colour.
    IEnumerator Glazes(Vector2Int from)
    {
        var caster = Host.GetComponent<SpellCaster>();
        for (int i = 0; i < 3; i++)
        {
            caster.Cast(Glaze, from.x, from.y + 5 + 4 * i, 0);
            yield return lens.Roll(1.2f, null);
        }
        int laid = World.Cells.Count(c => c.Cover == Ice);
        Metric("ice_cells_laid", laid);
        Check("glaze_lays_black_ice", laid > 20, $"{laid} cells");
        yield return lens.Roll(1f, null);
        Check("the_ice_is_drawn_as_a_film", grid.FilmCells(Ice) == laid, $"{grid.FilmCells(Ice)} drawn of {laid}");
        var drawn = grid.SlickColorAt(from.x, from.y + 9);
        Check("in_its_own_colour", drawn.HasValue && Near(drawn.Value, 0.08f, 0.12f, 0.35f), $"{drawn}");
    }

    // The wizard runs from the dry grass onto the slick and across it.
    IEnumerator Skid(Vector2Int from)
    {
        var puppet = new WizardPuppet(Host);
        var walk = puppet.WalkTo(grid.CellCenter(from.x, from.y + 18));
        bool onIce = false;
        for (float t = 0f; t < 5f && walk.MoveNext(); t += Time.deltaTime)
        {
            var at = grid.CellAt(Host.transform.position);
            onIce |= World.At(at.x, at.y)?.Cover == Ice;
            lens.Shoot();
            yield return walk.Current;
        }
        puppet.Move(Vector2.zero);
        Check("the_wizard_crosses_the_ice", onIce);
    }

    // The lapse: no ice in any chunk on either side, the view's rules and the mods list without it.
    IEnumerator AfterLapse()
    {
        Check("the_ice_lapsed", World.Tick >= lapses && World.Room.Mods.All(m => m.Title != IceTitle), $"tick {World.Tick}, {World.RoomText()}");
        for (float t = 0f; t < SweepWait && grid.SweptChunksLeft > 0; t += Time.unscaledDeltaTime) yield return null;
        yield return Wait(Settle);
        Metric("swept_chunks_drawn", grid.SweptChunksDrawn);
        Check("the_lapse_redraws_every_chunk_once", grid.EveryChunkRedraws - redrawsWere == 1, $"{grid.EveryChunkRedraws - redrawsWere} sweeps");
        Check("no_ice_left_in_any_chunk", !World.Cells.Any(c => c.Cover == Ice) && grid.FilmCells(Ice) == 0, Films(grid));
        Check("the_view_follows_the_lapse", grid.CurrentRules.Mods.Count == World.Room.Mods.Count && !grid.CurrentRules.IsOn(iceMod.Id) && !grid.IsFilm(Ice), $"view {string.Join(",", grid.CurrentRules.Mods.Select(m => m.Id))}, room {World.RoomText()}");
        Check("the_panel_drops_it", grid.GetComponent<RuleSwitcher>().LapseNote(iceMod.Id) == null);
        yield return Reach("ice_gone", Net.NetTick);
        for (float t = 0f; t < SweepWait && Theirs != null && Theirs.sweptLeft > 0; t += 2f)
        {
            yield return Wait(2f);
            yield return Ask("report");
        }
        if (Theirs == null) yield break;
        Check("client_lapsed_too", Theirs.roomText == World.RoomText() && Theirs.lines.LastOrDefault() == Mine.lines.LastOrDefault(), $"{Theirs.lines.LastOrDefault()}; {Theirs.roomText}");
        Check("client_has_no_ice_left", Theirs.films == Mine.films && Theirs.films.Contains($"{Ice}:0/0"), $"{Theirs.films}, here {Mine.films}");
        Check("client_view_follows_the_lapse", Theirs.viewMods == Mine.viewMods, $"{Theirs.viewMods}, here {Mine.viewMods}");
    }

    // A fixed camera at 720p, rendered off screen (the brain off): the black ice's film a frame at a time on a 30 fps
    // clock, or one still. With no folder it films nothing. glowmoss films with it too.
    internal sealed class Lens
    {
        readonly Camera cam;
        readonly CinemachineBrain brain;
        readonly RenderTexture target;
        readonly Texture2D page;
        readonly string folder;
        readonly Vector3 aim, from;
        int frame;

        public Lens(ChemGrid grid, string folder, Vector2Int cell, Vector3 from, bool clock)
        {
            this.folder = folder;
            this.from = from;
            if (folder == null) return;
            Directory.CreateDirectory(folder);
            cam = Camera.main;
            if (cam.TryGetComponent(out brain)) brain.enabled = false;
            target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            page = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            aim = grid.CellCenter(cell.x, cell.y);
            aim.y = grid.GroundAt(aim);
            if (clock) Time.captureDeltaTime = 1f / Fps;
        }

        public void Shoot(string name = null)
        {
            if (folder == null) return;
            cam.transform.position = aim + from;
            cam.transform.LookAt(aim);
            cam.targetTexture = target;
            cam.Render();
            cam.targetTexture = null;
            RenderTexture.active = target;
            page.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            RenderTexture.active = null;
            File.WriteAllBytes(Path.Combine(folder, (name ?? $"frame_{frame++:00000}") + ".jpg"), page.EncodeToJPG(Quality));
        }

        /// <summary>Films for <paramref name="seconds"/> of game time, or until <paramref name="done"/>.</summary>
        public IEnumerator Roll(float seconds, System.Func<bool> done)
        {
            for (float t = 0f; t < seconds && (done == null || !done()); t += Time.deltaTime)
            {
                Shoot();
                yield return null;
            }
        }

        public int Close()
        {
            if (folder == null) return 0;
            Time.captureDeltaTime = 0f;
            if (brain != null) brain.enabled = true;
            Object.Destroy(page);
            Object.Destroy(target);
            return frame;
        }
    }

    static string IcePatch() =>
        "{ \"title\": \"" + IceTitle + "\", \"idea\": \"A spell that lays a slick of black ice, gone again soon.\", \"patch\": {" +
        " \"covers\": { \"" + Ice + "\": { \"name\": \"Black ice\", \"tags\": [\"slippery\"], \"lasts\": 360, \"witherLog\": \"The black ice thaws away\"," +
        " \"melt\": { \"water\": 1 }, \"look\": { \"color\": [0.08, 0.12, 0.35] }, \"lapse\": { \"after\": " + LapseTicks.ToString(CultureInfo.InvariantCulture) + " } } }," +
        " \"reactions\": { \"add\": [ { \"id\": \"cold-black-ice\", \"element\": \"cold\", \"if\": { \"notTerrainTag\": \"blocking\", \"water\": false, \"frozen\": false, \"power\": 5 }," +
        " \"do\": [{ \"setCover\": \"" + Ice + "\" }], \"log\": \"Black ice glazes the ground\", \"stop\": true } ] }," +
        " \"spells\": [ { \"id\": \"" + Glaze + "\", \"group\": \"water\", \"name\": \"Glaze\", \"look\": { \"family\": \"motes\", \"color\": [0.08, 0.12, 0.35] }," +
        " \"level\": 2, \"shape\": \"burst\", \"radius\": 2, \"element\": \"cold\", \"power\": 5, \"blurb\": \"Level 2. A slick of black ice.\" } ] } }";

    // A still of a lot at 720p, off screen: the camera south of it, looking down.
    IEnumerator Still(string name, Vector2Int lot)
    {
        if (!Filming) yield break;
        yield return Wait(0.5f);
        var still = new Lens(grid, ScreenshotDir, lot, new Vector3(0f, 16f, -17f), false);
        still.Shoot(name);
        still.Close();
        Metric("stills", ++stills);
    }

    // The MCP over HTTP, as a client outside calls it, off the main thread so the server can answer.
    IEnumerator Tool(string name, JObject arguments)
    {
        yield return Call(name, arguments, said => answer = said);
        Metric($"mcp_{name}_chars", answer?.Length ?? 0);
    }

    /// <summary>One tool of the game's own rules MCP over HTTP, its answer (or the error) handed to <paramref name="said"/>.</summary>
    internal static IEnumerator Call(string name, JObject arguments, System.Action<string> said)
    {
        string url = RulesMcpServer.Url;
        string body = new JObject { ["jsonrpc"] = "2.0", ["id"] = 1, ["method"] = "tools/call", ["params"] = new JObject { ["name"] = name, ["arguments"] = arguments } }.ToString();
        var call = Task.Run(() => Http.PostAsync(url, new StringContent(body, Encoding.UTF8, "application/json")).Result.Content.ReadAsStringAsync().Result);
        while (!call.IsCompleted) yield return null;
        if (call.IsFaulted)
        {
            said(call.Exception?.GetBaseException().Message);
            yield break;
        }
        var json = JObject.Parse(call.Result);
        said(json["error"]?.ToString() ?? string.Join("\n", (json["result"]?["content"] as JArray ?? new JArray()).Select(c => (string)c["text"])));
    }
}
