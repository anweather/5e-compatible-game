using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Chem;
using Unity.Cinemachine;
using Unity.Netcode;
using UnityEngine;

// Placing in play, the view's side (docs/design/events-and-placing.md 2a, 2b): a mod's spells
// place things, a summoned animal, trees and brambles, and its world section lays trees and
// brambles, through an Open room with the virtual player in it. Both sides show the same things
// on the same cells, resting on the ground; the summoned child comes out on each; the new tree
// and bramble cells are dressed; things that lapse vanish on both; a late joiner gets the same.
// The spells are plain test rows: named content never goes in the code or rules (J-055).
public class PlaceViewProbe : RoomProbe
{
    const string ModId = "probe-place", Title = "Probe place";
    const string Barrels = "probe-barrels", Crates = "probe-crates", Child = "probe-child", Grove = "probe-grove", Brambles = "probe-brambles";
    const string ChildSpecies = "human-child";
    const int Each = 3, CratesLast = 48, Around = 9;
    const float Settle = 1f, CatchUp = 20f, ShotsBack = 18f, ShotsHigh = 15f;
    const int ClipWidth = 1280, ClipHeight = 720, ClipQuality = 85;

    protected override ChangeMode Mode => ChangeMode.Open;
    protected override bool StartsWithAMod => false;

    static string Spell(string id, string name, string place) =>
        $@"{{ ""id"": ""{id}"", ""group"": ""nature"", ""name"": ""{name}"", ""level"": 1, ""shape"": ""burst"", ""radius"": 1,
            ""look"": {{ ""family"": ""blast"", ""color"": [0.4, 0.8, 0.3] }}, ""place"": {place} }}";

    static string Cells(IEnumerable<Vector2Int> cells) => string.Join(", ", cells.Select(c => $"[{c.x}, {c.y}]"));

    static string Mod(List<Vector2Int> trees, List<Vector2Int> brush) => $@"{{ ""title"": ""{Title}"", ""patch"": {{
        ""spells"": [
            {Spell(Barrels, "Probe barrels", $@"{{ ""thing"": ""barrel"", ""count"": {Each}, ""within"": 3, ""spacing"": 1 }}")},
            {Spell(Crates, "Probe crates", $@"{{ ""thing"": ""crate"", ""count"": {Each}, ""within"": 3, ""spacing"": 1, ""lasts"": {CratesLast} }}")},
            {Spell(Child, "Probe child", $@"{{ ""animal"": ""{ChildSpecies}"", ""count"": 1, ""within"": 0 }}")},
            {Spell(Grove, "Probe grove", $@"{{ ""terrain"": ""tree"", ""count"": {Each}, ""within"": 3, ""spacing"": 1 }}")},
            {Spell(Brambles, "Probe brambles", $@"{{ ""terrain"": ""brush"", ""count"": {Each}, ""within"": 3, ""spacing"": 1 }}")} ],
        ""world"": {{ ""lay"": [
            {{ ""terrain"": ""tree"", ""cells"": [{Cells(trees)}] }},
            {{ ""terrain"": ""brush"", ""cells"": [{Cells(brush)}] }} ] }} }} }}";

    ThingSpawner spawner;
    ValleyFauna fauna;
    float caughtUp;
    // Filming: where the camera looks, and the frames so far.
    Vector3? filmAt;
    int filmFrame;
    RenderTexture filmTarget;
    Texture2D filmPage;

    protected override IEnumerator Steps()
    {
        spawner = FindAnyObjectByType<ThingSpawner>();
        fauna = FindAnyObjectByType<ValleyFauna>();
        var caster = NetworkManager.Singleton.SpawnManager.GetLocalPlayerObject().GetComponent<SpellCaster>();
        var site = Site(grid.CellAt(caster.transform.position));
        Metric("site_x", site.x);
        Metric("site_y", site.y);
        var (laidTrees, laidBrush) = LayCells(site);
        var before = PlantCells(site);
        Metric("baked_tree_cells_dressed_before", DressedCount(grid, before.Where(c => World.At(c.x, c.y).T == TerrainIds.Tree)));

        StartFilm(site);
        Switcher.Ask(RoomCommand.Apply(Switcher.Me, ModId, Mod(laidTrees, laidBrush), Title));
        yield return Agree("mod_on", $"Player 1 switched on {Title}");
        if (Theirs == null) yield break;
        var laid = laidTrees.Concat(laidBrush).Where(c => World.At(c.x, c.y).T is TerrainIds.Tree or TerrainIds.Brush).ToList();
        Metric("laid_cells", laid.Count);
        Check("laid_cells_laid", laid.Count == laidTrees.Count + laidBrush.Count, $"{laid.Count} of {laidTrees.Count + laidBrush.Count}");
        Check("laid_cells_dressed_here", laid.Count > 0 && DressedCount(grid, laid) == laid.Count, $"{DressedCount(grid, laid)} of {laid.Count}");
        yield return Ask("dressed", CellText(laid));
        Check("laid_cells_dressed_there", Theirs != null && Theirs.cellsDressed == laid.Count, $"{Theirs?.cellsDressed} of {laid.Count}");

        // The casts: things west and east of the site, the child on it, a grove north and brambles south.
        int placedBefore = World.Placed.Count;
        caster.Cast(Barrels, site.x - 4, site.y, 0);
        yield return Wait(0.4f);
        caster.Cast(Crates, site.x + 4, site.y, 0);
        yield return Wait(0.4f);
        caster.Cast(Child, site.x, site.y, 0);
        yield return Wait(0.4f);
        caster.Cast(Grove, site.x, site.y - 4, 0);
        yield return Wait(0.4f);
        caster.Cast(Brambles, site.x, site.y + 4, 0);
        yield return Within(() => World.Placed.Count >= placedBefore + 2 * Each, 6f);
        yield return Wait(1.5f);
        Metric("place_frame_ms", spawner.PlacedMs);

        // Here: each placed thing is drawn on its cell, at rest; the child is out; the new plants are dressed.
        var (placed, gap) = PlacedOn(grid, spawner);
        Metric("placed_count", World.Placed.Count);
        Metric("placed_gap_most_m", gap);
        Check("things_placed", World.Placed.Count == 2 * Each, $"{World.Placed.Count} placed: {placed}");
        Check("each_drawn_on_its_cell", Entries(placed) == World.Placed.Count && World.Placed.All(t => placed.Contains($"{t.Id}:{t.Kind}@{t.CellX},{t.CellY}")), placed);
        Check("each_rests_on_the_ground", gap < 0.25f, $"{gap:0.00} m");
        string summons = SummonsOn(grid);
        Check("the_child_is_out", summons.Contains(ChildSpecies), summons);
        var childAt = World.SummonedAfter(-1).FirstOrDefault();
        if (fauna.Summoned(childAt.N, out var child, out _))
        {
            var cell = grid.CellCenter(childAt.X, childAt.Y);
            float off = Vector2.Distance(new Vector2(child.x, child.z), new Vector2(cell.x, cell.z));
            Metric("child_from_its_cell_m", off);
            Check("the_child_came_out_on_its_cell", off < 3f, $"{off:0.0} m, cell ({childAt.X}, {childAt.Y})");
        }
        var grown = PlantCells(site).Except(before).Except(laid).ToList();
        int trees = grown.Count(c => World.At(c.x, c.y).T == TerrainIds.Tree), brush = grown.Count - trees;
        Metric("new_tree_cells", trees);
        Metric("new_brush_cells", brush);
        Check("trees_and_brambles_made", trees == Each && brush == Each, $"{trees} trees, {brush} brambles");
        Check("new_plants_dressed_here", DressedCount(grid, grown) == grown.Count, $"{DressedCount(grid, grown)} of {grown.Count}");

        // There: the same.
        if (filmAt != null) Metric("film_frames_at_casts", filmFrame);
        yield return Agree2("casts", grown, placed, summons);

        // The crates lapse: gone on both.
        yield return Within(() => World.Placed.Count == Each, CratesLast / 6f + 6f);
        yield return Wait(1f);
        (placed, _) = PlacedOn(grid, spawner);
        Check("lapsed_ones_vanish_here", World.Placed.Count == Each && !placed.Contains(":crate@") && Entries(placed) == Each, placed);
        yield return Wait(1f);
        yield return InStep(Net.NetTick);
        Check("lapsed_ones_vanish_there", Theirs != null && Theirs.placed == placed, $"{Theirs?.placed}; here {placed}");
        yield return StopFilm();
        CompareHashes("checksums_before_the_late_join");

        // A late joiner: the barrels, the dressing and the child.
        HashesFrom = Net.NetTick;
        yield return Ask("rejoin");
        yield return Wait(Settle);
        yield return InStep(Net.NetTick);
        if (Theirs == null) yield break;
        Check("a_late_joiner_has_the_things", Theirs.placed == placed && Theirs.placedGap < 0.25f, $"{Theirs.placed} ({Theirs.placedGap:0.00} m); here {placed}");
        Check("a_late_joiner_has_the_child", Theirs.summons == SummonsOn(grid), $"{Theirs.summons}; here {SummonsOn(grid)}");
        var dressed = grown.Concat(laid).ToList();
        yield return Ask("dressed", CellText(dressed));
        Check("a_late_joiner_dresses_the_plants", Theirs != null && Theirs.cellsDressed == dressed.Count, $"{Theirs?.cellsDressed} of {dressed.Count}");

        yield return QuietCost();

        // Off again: the laid cells go back, undressed where they were bare.
        Switcher.Ask(RoomCommand.Revert(Switcher.Me, ModId));
        yield return Agree("mod_off", $"Player 1 switched off {Title}");
        Check("laid_cells_back", laid.All(c => World.At(c.x, c.y).T is not (TerrainIds.Tree or TerrainIds.Brush)), CellText(laid));
    }

    // Both sides hold the same placed things on the same cells, the same summons, and the new plants dressed.
    IEnumerator Agree2(string name, List<Vector2Int> grown, string placed, string summons)
    {
        yield return InStep(Net.NetTick);
        if (Theirs == null) yield break;
        Check(name + "_same_things_there", Theirs.placed == placed, $"{Theirs.placed}; here {placed}");
        Check(name + "_at_rest_there", Theirs.placedGap < 0.25f, $"{Theirs.placedGap:0.00} m");
        Check(name + "_the_child_out_there", Theirs.summons == summons, $"{Theirs.summons}; here {summons}");
        yield return Ask("dressed", CellText(grown));
        Check(name + "_plants_dressed_there", Theirs != null && Theirs.cellsDressed == grown.Count, $"{Theirs?.cellsDressed} of {grown.Count}");
    }

    // The virtual player in the background runs behind: waits until it has run this tick and holds this room.
    IEnumerator InStep(int tick)
    {
        for (caughtUp = 0f; caughtUp < CatchUp; caughtUp += Settle)
        {
            yield return Ask("report", tick.ToString(CultureInfo.InvariantCulture), 10f);
            if (Theirs == null || (Theirs.netTick >= tick && Theirs.roomText == World.RoomText() && Theirs.rules == World.Rules.Fingerprint)) yield break;
            yield return Wait(Settle);
        }
    }

    static IEnumerator Within(System.Func<bool> done, float seconds)
    {
        for (float t = 0f; t < seconds && !done(); t += Time.unscaledDeltaTime) yield return null;
    }

    // What a quiet frame costs: the spawner's look at the core's table and the animals' look at the summons.
    IEnumerator QuietCost()
    {
        const int Calls = 10000;
        Check("quiet_frame_shows_nothing", !spawner.ShowPlaced());
        var watch = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < Calls; i++) spawner.ShowPlaced();
        Metric("quiet_things_ns", watch.Elapsed.TotalMilliseconds * 1e6 / Calls);
        watch.Restart();
        for (int i = 0; i < Calls; i++) fauna.BringOutSummons();
        Metric("quiet_summons_ns", watch.Elapsed.TotalMilliseconds * 1e6 / Calls);
        float things = -1f, animals = -1f;
        yield return GarbagePerCall(() => spawner.ShowPlaced(), 20000, b => things = b);
        yield return GarbagePerCall(() => fauna.BringOutSummons(), 20000, b => animals = b);
        Metric("quiet_things_bytes", things);
        Metric("quiet_summons_bytes", animals);
        // A call that made anything would make 20 bytes or more each time: a fraction of a byte is another frame's noise.
        Check("quiet_frame_allocates_nothing", things is >= 0f and < 1f && animals is >= 0f and < 1f, $"{things} and {animals} bytes a call");
    }

    // --- the site -------------------------------------------------------------------------

    // A cell some way from the wizard with the most open ground round it, for every cast to find room.
    Vector2Int Site(Vector2Int from)
    {
        var open = World.PlaceOpen(null);
        Vector2Int best = from;
        int most = -1;
        var tries = Enumerable.Range(0, 6).Select(i => 10 + 4 * i)
            .SelectMany(r => new[] { new Vector2Int(r, 0), new Vector2Int(-r, 0), new Vector2Int(0, r), new Vector2Int(0, -r) });
        foreach (var c in tries.Select(d => from + d).Where(c => open(c.x, c.y)))
        {
            int n = OpenAround(open, c);
            if (n <= most) continue;
            most = n;
            best = c;
        }
        Metric("site_score", most);
        return best;
    }

    // Open cells round a cell, less a lot for each tree the land draws there: one hides what's placed from the film.
    int OpenAround(System.Func<int, int, bool> open, Vector2Int c)
    {
        int n = 0;
        for (int y = -Around; y <= Around; y++)
            for (int x = -Around; x <= Around; x++)
            {
                if (open(c.x + x, c.y + y)) n++;
                if (grid.valley != null && grid.valley.TreeStands(c.x + x, c.y + y)) n -= TreePenalty;
            }
        return n;
    }

    const int TreePenalty = 40;

    // Three trees and two brambles for the mod to lay, on open cells in a row by the site.
    (List<Vector2Int> trees, List<Vector2Int> brush) LayCells(Vector2Int site)
    {
        var open = World.PlaceOpen(null, false);
        var row = Enumerable.Range(-6, 13).Select(dx => new Vector2Int(site.x + dx, site.y - 7)).Where(c => open(c.x, c.y)).ToList();
        var spaced = new List<Vector2Int>();
        foreach (var c in row)
            if (spaced.Count == 0 || c.x - spaced[spaced.Count - 1].x >= 2) spaced.Add(c);
        return (spaced.Take(3).ToList(), spaced.Skip(3).Take(2).ToList());
    }

    // The tree and bramble cells round the site now.
    HashSet<Vector2Int> PlantCells(Vector2Int site)
    {
        var cells = new HashSet<Vector2Int>();
        for (int y = -Around; y <= Around; y++)
            for (int x = -Around; x <= Around; x++)
            {
                var c = World.At(site.x + x, site.y + y);
                if (c != null && (c.T == TerrainIds.Tree || c.T == TerrainIds.Brush)) cells.Add(new Vector2Int(site.x + x, site.y + y));
            }
        return cells;
    }

    // --- what each side reports (RoomProbeRecord) --------------------------------------------

    /// <summary>The things spells placed that this side draws (id:kind@cell, sorted), and the most any rests off the ground.</summary>
    public static (string placed, float gap) PlacedOn(ChemGrid grid, ThingSpawner spawner)
    {
        float gap = 0f;
        var all = new List<string>();
        foreach (var (id, prop) in spawner.Live)
        {
            if (!id.IsPlaced) continue;
            var cell = grid.CellAt(prop.transform.position);
            all.Add($"{id}:{prop.kind}@{cell.x},{cell.y}");
            if (prop.TryGetComponent<Collider>(out var body)) gap = Mathf.Max(gap, Mathf.Abs(body.bounds.min.y - grid.GroundAt(prop.transform.position)));
        }
        all.Sort(System.StringComparer.Ordinal);
        return (string.Join(Between, all), gap);
    }

    /// <summary>The animals spells brought out that are out on this side (n:species, sorted).</summary>
    public static string SummonsOn(ChemGrid grid)
    {
        var fauna = FindAnyObjectByType<ValleyFauna>();
        if (fauna == null || grid.World == null) return "";
        var all = new List<string>();
        foreach (var s in grid.World.SummonedAfter(-1))
            if (fauna.Summoned(s.N, out _, out var species)) all.Add($"{s.N}:{species}");
        all.Sort(System.StringComparer.Ordinal);
        return string.Join(" ", all);
    }

    /// <summary>How many of these cells stand dressed (CellDressing drew something on them).</summary>
    public static int DressedCount(ChemGrid grid, IEnumerable<Vector2Int> cells) =>
        cells.Count(c => grid.DressingAt(c.x, c.y) is { } d && d.GetComponentsInChildren<Renderer>().Length > 0);

    // Between two placed things in a report: an id has spaces in it.
    const string Between = "; ";

    static int Entries(string placed) => placed.Length == 0 ? 0 : placed.Split(Between).Length;

    static string CellText(IEnumerable<Vector2Int> cells) => string.Join(";", cells.Select(c => $"{c.x},{c.y}"));

    /// <summary>Cells written x,y;x,y.</summary>
    public static List<Vector2Int> ParseCells(string text) =>
        (text ?? "").Split(';', System.StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split(','))
            .Select(p => new Vector2Int(int.Parse(p[0], CultureInfo.InvariantCulture), int.Parse(p[1], CultureInfo.InvariantCulture)))
            .ToList();

    // --- film --------------------------------------------------------------------------------

    // With --shots, every frame from the mod going on to the crates lapsing, at 30 a second of game time, the camera
    // high on the south side (the trees are north, beyond the site) swinging slowly: it renders off screen, so it needs no window.
    void StartFilm(Vector2Int site)
    {
        if (string.IsNullOrEmpty(ScreenshotDir)) return;
        Directory.CreateDirectory(Path.Combine(ScreenshotDir, "film"));
        var at = grid.CellCenter(site.x, site.y);
        at.y = grid.GroundAt(at);
        filmAt = at;
        filmTarget = new RenderTexture(ClipWidth, ClipHeight, 24);
        filmPage = new Texture2D(ClipWidth, ClipHeight, TextureFormat.RGB24, false);
        Time.captureDeltaTime = 1f / 30f;
    }

    IEnumerator StopFilm()
    {
        if (filmAt == null) yield break;
        filmAt = null;
        yield return null;
        Time.captureDeltaTime = 0f;
        if (Camera.main.TryGetComponent<CinemachineBrain>(out var brain)) brain.enabled = true;
        Destroy(filmPage);
        filmTarget.Release();
        Metric("film_frames", filmFrame);
    }

    void LateUpdate()
    {
        if (filmAt is not { } aim) return;
        var cam = Camera.main;
        if (cam.TryGetComponent<CinemachineBrain>(out var brain) && brain.enabled) brain.enabled = false;
        float turn = -15f + filmFrame * 0.08f;
        cam.transform.position = aim + Quaternion.Euler(0f, turn, 0f) * new Vector3(0f, ShotsHigh, -ShotsBack);
        cam.transform.LookAt(aim + new Vector3(0f, 1.5f, 4f));
        cam.targetTexture = filmTarget;
        cam.Render();
        cam.targetTexture = null;
        RenderTexture.active = filmTarget;
        filmPage.ReadPixels(new Rect(0, 0, ClipWidth, ClipHeight), 0, 0);
        RenderTexture.active = null;
        File.WriteAllBytes(Path.Combine(ScreenshotDir, "film", $"frame_{filmFrame++:00000}.jpg"), filmPage.EncodeToJPG(ClipQuality));
    }

    protected override void OnDestroy()
    {
        if (filmAt != null) Time.captureDeltaTime = 0f;
        base.OnDestroy();
    }
}
