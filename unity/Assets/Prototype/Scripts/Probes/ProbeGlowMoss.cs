using System.Collections;
using System.Globalization;
using System.IO;
using System.Linq;
using Chem;
using Newtonsoft.Json.Linq;
using UnityEngine;

// The probe's glowmoss scenario (docs/design/primitives.md P4, the view's half of glow, over P2's spreading
// covers): primitives §5's glowing moss that creeps over the river stones, on two peers. This Editor hosts an
// Open room and the virtual player joins. The base rules glow nowhere. Through the game's own MCP the clock goes
// to dusk and the moss mod goes on: the valley's river runs between grass banks with no stone, so the mod lays its
// own river stones (a strip of rock on the bank nearest the wizard) and a cover with look.glow 0.3 that spreads
// onto stone and rock, on two of them; it creeps on, and every cell it reaches glows in its colour over its film,
// and nothing else does. At midnight a moss cell's glow is well above the bank's beside it (none) and a stone's it
// hasn't reached (none), at noon next to nothing; the other
// peer has the same moss and the same glowing cells; reverted, the glow goes with the moss. With --shots: the
// moss creeping at dusk, filmed off screen at 720p (glow-moss/ frames).
public class GlowMossProbe : RoomProbe
{
    const string Moss = "river-moss", Title = "Glowing moss";
    static readonly float[] MossColour = { 0.3f, 0.85f, 0.55f };
    const float Glow = 0.3f;
    const int Every = 30, MossReach = 12, LapseTicks = 3600;
    // How far from the host's wizard to look for river stones, and how long to let the moss creep.
    const int Search = 90;
    // The river stones the mod lays: this many bank cells within BankReach of the river cell nearest the wizard.
    const int Stones = 40, BankReach = 8;
    const float Creep = 18f, LightWait = 12f, GlowSettle = 5f;
    // A moss cell's glow at midnight must pass this (its brightest channel); by day it stays under DayMost.
    const float NightLeast = 0.15f, DayMost = 0.03f, SameColour = 0.02f;

    string answer;

    protected override ChangeMode Mode => ChangeMode.Open;
    protected override bool StartsWithAMod => false;

    bool Filming => !string.IsNullOrEmpty(ScreenshotDir);

    /// <summary>"moss cells/glowing cells", for both peers to compare.</summary>
    // Moss cells "/" glowing cells, as Glows writes them: every moss cell glows.
    static bool AllGlow(string glows) => glows != null && glows.Split('/') is var p && p.Length == 2 && p[0] == p[1];

    public static string Glows(ChemGrid grid) => $"{grid.World.AllCells().Count(c => c != null && c.Cover == Moss)}/{grid.GlowCells}";

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
        Check("the_base_glows_nowhere", grid.GlowCells == 0, $"{grid.GlowCells} glowing cells");
        var host = Unity.Netcode.NetworkManager.Singleton.SpawnManager.GetLocalPlayerObject().gameObject;
        var from = grid.CellAt(host.transform.position);
        if (!RiverBank(from, out var stones, out var bare))
        {
            Check("a_river_bank_near", false, $"no bare grass or dirt by the river within {Search} cells of {from}");
            yield break;
        }
        var a = stones[0];
        Metric("stones", stones.Count);
        Metric("stone_x", a.x);
        Metric("stone_y", a.y);

        yield return SetTime("dusk", 0.3f, true);
        yield return Tool("apply", new JObject { ["mod"] = Patch(stones), ["title"] = Title });
        yield return Agree("moss_on", $"Player 1 switched on {Title}");
        int laid = MossCells();
        Metric("moss_laid", laid);
        // The two laid cells are moss: the count can be past two by now, as the moss spreads every 30 ticks and the switch is
        // agreed some ticks after it went on.
        int laidOn = stones.Take(2).Count(c => World.At(c.x, c.y).Cover == Moss);
        Check("the_stones_and_moss_are_laid", laidOn == 2 && stones.Skip(2).All(c => World.At(c.x, c.y).T == TerrainIds.Rock), $"{laidOn} of 2 laid moss cells ({laid} in all, tick {World.Tick}), {stones.Count(c => World.At(c.x, c.y).T == TerrainIds.Rock)} of {stones.Count - 2} stones");

        // It creeps, filmed or not, at dusk.
        var lens = new ProductsViewProbe.Lens(grid, Filming ? Path.Combine(ScreenshotDir, "glow-moss") : null, a, new Vector3(3f, 7f, -9f), true);
        // The host's aim marker follows the Editor's cursor: its caster rests while the moss is filmed.
        var caster = host.GetComponent<SpellCaster>();
        caster.Marker?.Hide();
        caster.enabled = !Filming;
        if (Filming) yield return lens.Roll(Creep, null);
        else yield return Wait(Creep);
        caster.enabled = true;
        Metric("frames", lens.Close());
        int crept = MossCells();
        Metric("moss_crept", crept);
        Check("the_moss_creeps", crept > laid, $"{laid} laid, {crept} after {Creep:0} s");
        Check("every_moss_cell_glows_and_nothing_else", grid.GlowCells == crept, Glows(grid));
        var film = grid.SlickColorAt(a.x, a.y);
        Check("its_film_is_its_colour_as_before", film.HasValue && Near(film.Value), $"{film}");
        var untouched = stones.Where(c => World.At(c.x, c.y).Cover == null).ToList();
        Metric("stones_not_reached", untouched.Count);

        yield return SetTime("midnight", 0.95f, true);
        var night = MeanGlow();
        Metric("glow_night_r", night.r);
        Metric("glow_night_g", night.g);
        Metric("glow_night_b", night.b);
        var bank = grid.GlowAt(bare.x, bare.y);
        Metric("bank_glow", bank.maxColorComponent);
        float stoneGlow = untouched.Where(c => World.At(c.x, c.y).Cover == null).Select(c => grid.GlowAt(c.x, c.y).maxColorComponent).DefaultIfEmpty(0f).Max();
        Metric("bare_stone_glow", stoneGlow);
        Check("a_moss_cell_glows_at_night", night.maxColorComponent > NightLeast && night.maxColorComponent > bank.maxColorComponent + NightLeast, $"moss {night}, the bank beside {bank}");
        Check("the_bank_and_bare_stones_do_not", bank.maxColorComponent == 0f && stoneGlow == 0f && World.At(bare.x, bare.y).Cover != Moss, $"bank {bank} at {bare}, stones {stoneGlow}");
        Check("in_the_moss_colour", Mathf.Abs(night.g / Mathf.Max(night.r, 1e-4f) - MossColour[1] / MossColour[0]) < 0.05f, $"{night}");
        // Every moss cell glows there too. The two sides report at different ticks and the moss spreads meanwhile, so
        // the client's glow is held to its own moss (moss_in_step holds the cells the same), once it has drawn them.
        yield return Reach("night_report", Net.NetTick);
        for (float t = 0f; t < GlowSettle && Theirs != null && !AllGlow(Theirs.glows); t += 0.5f)
        {
            yield return Wait(0.5f);
            yield return Ask("report");
        }
        if (Theirs == null) yield break;
        Check("client_glows_alike", AllGlow(Theirs.glows), $"{Theirs.glows} there (moss/glowing), here {Mine.glows}");
        CompareHashes("moss_in_step");

        yield return SetTime("noon", 0.01f, false);
        var day = MeanGlow();
        Metric("glow_day_max", day.maxColorComponent);
        Check("next_to_nothing_by_day", day.maxColorComponent < DayMost, $"{day}");

        var mod = World.Room.Mods.FirstOrDefault(m => m.Title == Title);
        yield return Tool("revert", new JObject { ["mod"] = mod?.Id ?? "" });
        yield return Agree("moss_off", $"Player 1 switched off {Title}");
        for (float t = 0f; t < 20f && grid.SweptChunksLeft > 0; t += UnityEngine.Time.unscaledDeltaTime) yield return null;
        yield return Wait(1.5f);
        Check("the_glow_goes_with_the_moss", MossCells() == 0 && grid.GlowCells == 0, Glows(grid));
    }

    int MossCells() => World.AllCells().Count(c => c != null && c.Cover == Moss);

    // The mean glow drawn now over the moss cells.
    Color MeanGlow()
    {
        var sum = Color.clear;
        int n = 0;
        for (int i = 0; i < World.W * World.H; i++)
        {
            if (World.CellAt(i)?.Cover != Moss) continue;
            sum += grid.GlowAt(i % World.W, i / World.W);
            n++;
        }
        return n == 0 ? Color.clear : sum / n;
    }

    static bool Near(Color c) => Mathf.Abs(c.r - MossColour[0]) <= SameColour && Mathf.Abs(c.g - MossColour[1]) <= SameColour && Mathf.Abs(c.b - MossColour[2]) <= SameColour;

    // set_time through the MCP, then the lamps waited for: up past (or down under) a level.
    IEnumerator SetTime(string when, float lamps, bool up)
    {
        yield return Tool("set_time", new JObject { ["time"] = when });
        Check($"set_time_{when}", answer != null && !answer.Contains("refused"), answer);
        for (float t = 0f; t < LightWait && (up ? Nightfall.Lamps(World) < lamps : Nightfall.Lamps(World) > lamps); t += UnityEngine.Time.unscaledDeltaTime) yield return null;
        Metric($"lamps_{when}", Nightfall.Lamps(World));
    }

    // The bank nearest the wizard: up to Stones bare, dry grass or dirt cells within two of the river's water, in
    // active chunks, round the river cell nearest the wizard, nearest it first (the first two side by side, where
    // the moss is laid); and a cell of that bank beside them that stays grass (the moss spreads only onto rock).
    bool RiverBank(Vector2Int from, out System.Collections.Generic.List<Vector2Int> stones, out Vector2Int bare)
    {
        stones = null;
        bare = default;
        var river = Enumerable.Range(0, World.W * World.H)
            .Select(i => new Vector2Int(i % World.W, i / World.W))
            .Where(p => Mathf.Abs(p.x - from.x) + Mathf.Abs(p.y - from.y) <= Search && World.At(p.x, p.y) is Cell c && c.T == TerrainIds.River && c.Water > 0 && World.IsActiveAt(p.x, p.y))
            .OrderBy(p => (p - from).sqrMagnitude).Take(1).ToList();
        if (river.Count == 0) return false;
        var r = river[0];
        var bank = new System.Collections.Generic.List<Vector2Int>();
        for (int y = r.y - BankReach; y <= r.y + BankReach; y++)
            for (int x = r.x - BankReach; x <= r.x + BankReach; x++)
                if (IsBank(new Vector2Int(x, y))) bank.Add(new Vector2Int(x, y));
        bank = bank.OrderBy(p => (p - from).sqrMagnitude).ToList();
        var first = bank.FirstOrDefault(p => bank.Contains(p + Vector2Int.up) || bank.Contains(p + Vector2Int.right));
        if (bank.Count < 8 || first == default) return false;
        var second = bank.Contains(first + Vector2Int.up) ? first + Vector2Int.up : first + Vector2Int.right;
        stones = new[] { first, second }.Concat(bank.Where(p => p != first && p != second).OrderBy(p => (p - first).sqrMagnitude).Take(Stones - 2)).ToList();
        var laid = stones;
        bare = bank.Where(p => !laid.Contains(p)).OrderBy(p => (p - first).sqrMagnitude).FirstOrDefault();
        return bare != default;
    }

    bool IsBank(Vector2Int p)
    {
        var c = World.At(p.x, p.y);
        if (c == null || c.Cover != null || c.Fire > 0 || c.Water > 0 || !World.IsActiveAt(p.x, p.y)) return false;
        if (c.T != TerrainIds.Grass && c.T != TerrainIds.Dirt) return false;
        for (int dy = -2; dy <= 2; dy++)
            for (int dx = -2; dx <= 2; dx++)
                if (World.At(p.x + dx, p.y + dy) is Cell w && w.Water > 0 && !w.Frozen) return true;
        return false;
    }

    // primitives §5's glowing moss as Claude would write it (SpreadTests.Moss), with P4's glow: a temporary cover
    // that creeps onto stone and rock and nothing else; and the river stones for it, laid on the bank, and the moss
    // on two cells among them (a lay row's cell holds one thing, so those two stay the bank's ground, mossed).
    static string Patch(System.Collections.Generic.IReadOnlyList<Vector2Int> stones)
    {
        string N(float f) => f.ToString(CultureInfo.InvariantCulture);
        string Cells(System.Collections.Generic.IEnumerable<Vector2Int> cells) => string.Join(", ", cells.Select(c => $"[{c.x}, {c.y}]"));
        return "{ \"title\": \"" + Title + "\", \"idea\": \"Glowing moss that creeps over the river stones.\", \"patch\": {" +
            " \"covers\": { \"" + Moss + "\": { \"name\": \"Glowing moss\", \"tags\": [], \"look\": { \"color\": [" + string.Join(", ", MossColour.Select(N)) + "], \"glow\": " + N(Glow) + " }," +
            " \"spread\": { \"every\": " + Every + ", \"chance\": 0.5, \"onto\": [\"stone\", \"rock\"], \"reach\": " + MossReach + " }," +
            " \"lapse\": { \"after\": " + LapseTicks + " } } }," +
            " \"world\": { \"lay\": [ { \"terrain\": \"rock\", \"cells\": [" + Cells(stones) + "] }," +
            " { \"cover\": \"" + Moss + "\", \"cells\": [" + Cells(stones.Take(2)) + "] } ] } } }";
    }

    IEnumerator Tool(string name, JObject arguments)
    {
        yield return ProductsViewProbe.Call(name, arguments, said => answer = said);
        Metric($"mcp_{name}_chars", answer?.Length ?? 0);
    }
}
