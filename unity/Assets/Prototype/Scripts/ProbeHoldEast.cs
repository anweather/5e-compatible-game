using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Chem;
using UnityEngine;

// The probe's holdeast scenario (hold-east-path, playtest 2026-11-08 C3 and B4): a run's raiders come by both roads, the foot's
// down the hill to the east gate and the east road from the lake to the south gate; the brook behind the castle is dry; and
// wading at the lake's edge slows the wizard a little, not a lot. With --shots it films the clip (frames, sound.wav,
// timeline.txt): the castle with a wave on both roads, the south gate, and a wade into the lake. The setup between is cut, and
// time hurries while the raiders walk. Also writes castle-map.png, the castle's surroundings a pixel a cell. Nothing here is
// in the game.
public class HoldEastProbe : ProbeScenario
{
    const float MostWait = 15f, MostMarch = 400f, Hurry = 6f;

    ClipFilm film;
    readonly List<string> timeline = new();

    World Core => grid.World;

    void Note(string what) => timeline.Add(string.Format(CultureInfo.InvariantCulture, "{0:0.000} {1}", film != null ? film.Seconds : 0f, what));

    Vector3 Ground((int x, int y) cell)
    {
        var p = grid.CellCenter(cell.x, cell.y);
        p.y = grid.GroundAt(p);
        return p;
    }

    // The castle's gates as their open cells, the east gate first, then the south.
    List<List<(int x, int y)>> Gates()
    {
        var site = Core.HoldSite;
        var open = BuildingRaster.Cells(BuildingRow.Read(site.Castle, site.Row, Core.Rules.Buildings), Core.Rules.Buildings)
            .Where(c => c.Terrain == "timber-gate-open").Select(c => (c.X, c.Y)).ToList();
        var gates = new List<List<(int x, int y)>>();
        foreach (var c in open)
        {
            var near = gates.FirstOrDefault(g => g.Any(o => Mathf.Abs(o.x - c.Item1) + Mathf.Abs(o.y - c.Item2) <= 2));
            if (near != null) near.Add(c);
            else gates.Add(new List<(int x, int y)> { c });
        }
        return gates.OrderByDescending(g => g.Max(c => c.x)).ToList();
    }

    static (int x, int y) Middle(List<(int x, int y)> cells) => cells[cells.Count / 2];

    static int Apart((int x, int y) a, (int x, int y) b) => Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);

    protected override IEnumerator Run()
    {
        var site = Core.HoldSite;
        Check("a_hold", site != null);
        if (site == null) yield break;
        yield return Switch(At(10f) + ", 'weather': { 'calmPeriods': 0, 'rampMinutes': 0.02, 'weights': { 'clear': 1, 'cloudy': 0, 'windy': 0, 'fog': 0, 'rain': 0, 'storm': 0, 'snow': 0 } }");
        EventLog.HudVisible = true;
        if (!string.IsNullOrEmpty(ScreenshotDir)) WriteMap(Path.Combine(ScreenshotDir, "castle-map.png"), 40, 260, 340, 500, 3);

        var gates = Gates();
        Check("two_gates", gates.Count == 2, $"{gates.Count}");
        Check("two_spawners", site.Spawners.Count == 2, string.Join(",", site.Spawners.Select(s => s.Id)));
        Check("the_brook_is_dry", Enumerable.Range(250, 220).All(y => Enumerable.Range(30, 100).All(x => Core.At(x, y)?.T != "river")), "no river cells behind the castle");
        if (gates.Count != 2 || site.Spawners.Count != 2) yield break;
        // The castle drawn whole before the run: no stretch of wall shown down by the new gate.
        var houses = FindAnyObjectByType<RowHouses>();
        var ruin = houses != null ? houses.Ruin(site.Castle) : null;
        Check("the_castle_stands_whole", ruin != null && ruin.Down == 0, ruin == null ? "no ruin" : $"{ruin.Down} down at {string.Join(" ", ruin.DownPlaces())}, the row at {site.Row?["at"]}");

        StandOn(site.Plate);
        for (float t = 0f; t < MostWait && !Core.HoldRunning; t += Time.deltaTime) yield return null;
        Check("a_run_starts", Core.HoldRunning);
        if (!Core.HoldRunning) yield break;
        StandOn(site.Rise);
        // A fuller wave than the first, with a few kinds in it, as the probe puts it straight in.
        Core.Hold.Wave = 4;
        Core.Hold.InWave = 0;
        Core.Hold.ToSpawn = World.HoldWaveSize(Core.Rules.Sim.Hold, 4, Core.Hold.Peers);
        HoldTheRun();

        yield return BothRoads(site, gates);
        yield return Wade();
        if (film != null)
        {
            yield return film.Stop();
            File.WriteAllLines(Path.Combine(ScreenshotDir, "timeline.txt"), timeline);
            Metric("seconds", film.Seconds);
            Check("short_enough", film.Seconds <= 30f, $"{film.Seconds:0.0} s");
        }
    }

    // The run held in its first wave with the keep whole, so no offer or fall cuts in while the probe watches.
    void HoldTheRun()
    {
        Core.Hold.Health = Core.Rules.Sim.Hold.Health;
        Core.Hold.NextWave = Core.Tick + Core.Rules.Sim.Hold.WaveTicks;
    }

    // Who has come in at which gate so far.
    void Through(List<List<(int x, int y)>> gates, ref bool east, ref bool south)
    {
        HoldTheRun();
        foreach (var r in Core.Raiders)
        {
            if (gates[1].Contains((r.X, r.Y))) south = true;
            if (gates[0].Contains((r.X, r.Y))) east = true;
        }
    }

    // The wave comes out from both spawners, walks both roads, and some of it is in at the south gate.
    IEnumerator BothRoads(WorldHold site, List<List<(int x, int y)>> gates)
    {
        var east = Middle(gates[0]);
        var south = Middle(gates[1]);
        var from = new Dictionary<int, (int x, int y)>();
        bool throughSouth = false, throughEast = false;
        Time.timeScale = Hurry;
        // Till a few are close to each gate, outside it.
        for (float t = 0f; t < MostMarch; t += Time.deltaTime)
        {
            Through(gates, ref throughEast, ref throughSouth);
            foreach (var r in Core.Raiders)
                if (!from.ContainsKey(r.Id)) from[r.Id] = (r.X, r.Y);
            int nearSouth = Core.Raiders.Count(r => Apart((r.X, r.Y), south) <= 30 && r.Y > south.y);
            int nearEast = Core.Raiders.Count(r => Apart((r.X, r.Y), east) <= 30 && r.X > east.x);
            if (nearSouth >= 3 && nearEast >= 3) break;
            yield return null;
        }
        Time.timeScale = 1f;
        bool By(HoldSpawner s, (int x, int y) c) => s.Cells.Any(o => Apart(o, c) <= 2);
        int byFoot = from.Values.Count(c => By(site.Spawners[0], c)), byEast = from.Values.Count(c => By(site.Spawners[1], c));
        Check("the_wave_comes_from_both_spawners", byFoot > 0 && byEast > 0, $"foot {byFoot}, east {byEast}");
        Metric("raiders_by_the_foot", byFoot);
        Metric("raiders_by_the_east_road", byEast);

        if (!string.IsNullOrEmpty(ScreenshotDir)) film = new ClipFilm(this, ScreenshotDir, withHud: true) { Keeping = false };
        film?.Roll();
        // Over the castle's south-east corner: the east gate's road on the right, the south gate's below.
        var heart = Ground(site.Heart);
        var corner = Ground((south.x + 10, south.y + 8));
        Cut(corner + new Vector3(22f, 26f, -30f), (heart + corner) * 0.5f);
        yield return Wait(0.4f);
        if (film != null) film.Keeping = true;
        Note("the new south gate, a wave coming up the east road to it");
        for (float t = 0f; t < 6f; t += Time.deltaTime)
        {
            Through(gates, ref throughEast, ref throughSouth);
            yield return null;
        }
        if (film != null) film.Keeping = false;

        // The east gate, the foot's road coming up to it.
        var gateEast = Ground(east);
        Cut(gateEast + new Vector3(17f, 8f, 5f), gateEast + Vector3.up * 1.5f);
        yield return Wait(0.3f);
        if (film != null) film.Keeping = true;
        Note("the east gate, the foot's road");
        for (float t = 0f; t < 4.5f; t += Time.deltaTime)
        {
            Through(gates, ref throughEast, ref throughSouth);
            yield return null;
        }
        if (film != null) film.Keeping = false;

        // At the south gate, from outside, looking in.
        var gate = Ground(south);
        Cut(gate + new Vector3(4f, 7f, -16f), gate + Vector3.up * 1.5f);
        yield return Wait(0.3f);
        if (film != null) film.Keeping = true;
        Note("in at the south gate");
        for (float t = 0f; t < 7f; t += Time.deltaTime)
        {
            Through(gates, ref throughEast, ref throughSouth);
            yield return null;
        }
        if (film != null) film.Keeping = false;
        // The rest of the wave, hurried, to see who got in where.
        Time.timeScale = Hurry;
        for (float t = 0f; t < 120f && !(throughSouth && throughEast); t += Time.deltaTime)
        {
            Through(gates, ref throughEast, ref throughSouth);
            yield return null;
        }
        Time.timeScale = 1f;
        Check("raiders_come_in_at_the_south_gate", throughSouth);
        Check("and_at_the_east_gate", throughEast);
    }

    // The wizard walks off the lake's west shore into the shallows: wading slows it a little.
    IEnumerator Wade()
    {
        var effects = wizard.GetComponent<SurfaceEffects>();
        Check("the_wizard_wades", effects != null);
        if (effects == null) yield break;
        Check("the_edge_slows_a_little", effects.WadingFactor(0.15f) >= 0.88f, $"{effects.WadingFactor(0.15f):0.00}");
        Check("waist_deep_slows_more_not_a_lot", effects.WadingFactor(1.1f) >= 0.75f && effects.WadingFactor(1.1f) < effects.WadingFactor(0.15f), $"{effects.WadingFactor(1.1f):0.00}");

        // A dry cell with the lake's water a few cells east of it, south of the lake house, on the ground (not a roof).
        (int x, int y) shore = default;
        bool found = false;
        for (int y = 396; y < 430 && !found; y++)
            for (int x = 230; x < 262 && !found; x++)
            {
                var c = Core.At(x, y);
                if (c == null || c.Water > 0 || Core.IsBlocking(c)) continue;
                if (Ground((x, y)).y - grid.WaterSurfaceY(x + 5, y) > 1.5f) continue;
                if (Enumerable.Range(4, 3).All(d => Core.At(x + d, y)?.Water >= effects.wadingDepth) && Core.At(x - 6, y)?.Water == 0)
                {
                    shore = (x - 6, y);
                    found = true;
                }
            }
        Check("a_shore", found, $"{shore}");
        if (!found) yield break;
        StandOn(shore);
        wizard.transform.rotation = Quaternion.LookRotation(Vector3.right);
        yield return Wait(0.6f);
        var start = Ground(shore);
        Cut(start + new Vector3(-3f, 4f, -9f), start + new Vector3(8f, 0.5f, 0f));
        yield return Wait(0.4f);
        if (film != null) film.Keeping = true;
        Note("a wade into the lake's shallows");
        float dryRun = 0f, wetRun = 0f, dryTime = 0f, wetTime = 0f, deepest = 0f;
        var last = wizard.transform.position;
        var walk = puppet.WalkTo(Ground((shore.x + 13, shore.y)));
        for (float t = 0f; t < 6f && walk.MoveNext(); t += Time.deltaTime)
        {
            // The camera follows, behind and to the side.
            var at = wizard.transform.position;
            Cut(at + new Vector3(-4.5f, 2.6f, -5.5f), at + new Vector3(2.5f, 0.6f, 0f));
            yield return walk.Current;
            var now = wizard.transform.position;
            float moved = new Vector2(now.x - last.x, now.z - last.z).magnitude;
            last = now;
            if (t < 0.5f || effects.Swimming) continue;
            if (effects.WadeDepth > 0f)
            {
                wetRun += moved;
                wetTime += Time.deltaTime;
                deepest = Mathf.Max(deepest, effects.WadeDepth);
            }
            else
            {
                dryRun += moved;
                dryTime += Time.deltaTime;
            }
        }
        puppet.Move(Vector2.zero);
        if (film != null) film.Keeping = false;
        float dry = dryTime > 0f ? dryRun / dryTime : 0f, wet = wetTime > 0f ? wetRun / wetTime : 0f;
        Metric("dry_speed", dry);
        Metric("wading_speed", wet);
        Metric("wading_deepest_m", deepest);
        Check("it_wades_a_while", wetTime > 0.8f && dryTime > 0.5f, $"dry {dryTime:0.0} s, wading {wetTime:0.0} s");
        Check("wading_is_only_a_little_slower", dry > 0f && wet / dry >= 0.78f, $"{wet:0.00} m/s against {dry:0.00} on land");
    }

    // The castle's surroundings a pixel a cell (scaled): water blue, the castle's roads orange, stone grey, the spawners red.
    void WriteMap(string path, int x0, int y0, int x1, int y1, int scale)
    {
        var site = Core.HoldSite;
        int w = x1 - x0, h = y1 - y0;
        var tex = new Texture2D(w * scale, h * scale, TextureFormat.RGBA32, false);
        var road = BuildingRaster.Cells(BuildingRow.Read(site.Castle, site.Row, Core.Rules.Buildings), Core.Rules.Buildings)
            .Where(c => c.Part == BuildingPart.Road && c.Terrain == site.RoadTerrain).Select(c => (c.X, c.Y)).ToHashSet();
        var spawn = site.Spawners.SelectMany(s => s.Cells).ToHashSet();
        for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
            {
                var c = Core.At(x, y);
                Color col = c == null ? Color.black : c.T switch
                {
                    "stone" => new Color(0.55f, 0.55f, 0.58f),
                    "tree" => new Color(0.1f, 0.35f, 0.1f),
                    "dirt" => new Color(0.55f, 0.45f, 0.3f),
                    "river" => new Color(0f, 0.8f, 0.8f),
                    _ => new Color(0.45f, 0.7f, 0.35f),
                };
                if (c != null && c.Water > 0) col = new Color(0.3f, 0.55f, 1f);
                if (road.Contains((x, y))) col = new Color(0.75f, 0.45f, 0.15f);
                if (spawn.Contains((x, y))) col = Color.red;
                if ((x, y) == site.Heart) col = Color.yellow;
                for (int i = 0; i < scale; i++)
                    for (int j = 0; j < scale; j++)
                        tex.SetPixel((x - x0) * scale + i, (y1 - 1 - y) * scale + j, col);
            }
        tex.Apply();
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.Destroy(tex);
    }
}
