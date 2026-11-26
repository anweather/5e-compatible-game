using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Chem;
using UnityEngine;

// The probe's holdbodies scenario (hold-bodies): the Hold's raider kinds made from the valley's animals, and the folk off the
// raiders' road in a run. A run starts from the plate; the workers whose rounds go up the road walk to their stops for a run
// (jobs.json "inARun") and none is left near it; then a mixed wave (the new kinds among the old) comes up the road and
// strikes the crystal. Each kind is drawn with its rules row's body. With --shots it films the clip (frames, sound.wav,
// timeline.txt): the empty road, the wave on it, the wave at the crystal. The setup between is cut. The raiders are put down
// in the core directly, as only a solo probe may; nothing here is in the game.
public class HoldBodiesProbe : ProbeScenario
{
    const float MostWait = 15f, MostWalkHome = 300f, Hurry = 5f;
    const int Clear = 6;
    const string ClearSky = "{ 'clear': 1, 'cloudy': 0, 'windy': 0, 'fog': 0, 'rain': 0, 'storm': 0, 'snow': 0 }";

    static readonly string[] Mixed =
    {
        RaiderIds.StormBison, RaiderIds.BlightDeer, RaiderIds.GloomWolf, RaiderIds.BoneHound,
        RaiderIds.BlightDeer, RaiderIds.AshCrab, RaiderIds.GloomWolf, RaiderIds.BarrowBear,
    };

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

    // The raiders' road (the map's hold.json road) as cells, nearest the heart first.
    List<(int x, int y)> Road()
    {
        var site = Core.HoldSite;
        var (hx, hy) = site.Heart;
        return BuildingRaster.Cells(BuildingRow.Read(site.Castle, site.Row, Core.Rules.Buildings), Core.Rules.Buildings)
            .Where(c => c.Part == BuildingPart.Road && c.Terrain == site.RoadTerrain)
            .Select(c => (c.X, c.Y)).Distinct()
            .OrderBy(c => System.Math.Abs(c.X - hx) + System.Math.Abs(c.Y - hy)).ThenBy(c => c.Y).ThenBy(c => c.X).ToList();
    }

    // The nearest any worker is to the road now, in cells, wherever the clock and a run have put them.
    int NearestWorker(ValleyFauna fauna, HashSet<(int x, int y)> road, out string who)
    {
        int nearest = int.MaxValue;
        who = "";
        for (int i = 0; i < fauna.Jobs.Workers.Count; i++)
        {
            var at = fauna.WorkerNow(i).At;
            var cell = grid.CellAt(new Vector3(at.x, 0f, at.y));
            int apart = road.Min(c => Mathf.Max(Mathf.Abs(c.x - cell.x), Mathf.Abs(c.y - cell.y)));
            if (apart >= nearest) continue;
            nearest = apart;
            who = fauna.Jobs.Workers[i].Job;
        }
        return nearest;
    }

    // No wave walks in on its own while the probe stages its own, and the keep stands.
    void HoldTheWaves()
    {
        Core.Hold.ToSpawn = 0;
        Core.Hold.NextWave = Core.Tick + Core.Rules.Sim.Hold.WaveTicks;
        Core.Hold.Health = Core.Rules.Sim.Hold.Health;
    }

    protected override IEnumerator Run()
    {
        var fauna = FindAnyObjectByType<ValleyFauna>();
        var field = FindAnyObjectByType<DefenseField>();
        var site = Core.HoldSite;
        Check("found", fauna != null && fauna.Jobs != null && field != null && site != null);
        if (fauna == null || fauna.Jobs == null || field == null || site == null) yield break;
        yield return Switch(At(10f) + ", 'weather': { 'calmPeriods': 0, 'rampMinutes': 0.02, 'weights': " + ClearSky + " }");
        EventLog.HudVisible = true;
        var road = Road();
        var onRoad = road.ToHashSet();
        Check("a_road", road.Count > 20, $"{road.Count} cells");

        StandOn(site.Plate);
        for (float t = 0f; t < MostWait && !Core.HoldRunning; t += Time.deltaTime) yield return null;
        Check("a_run_starts", Core.HoldRunning);
        if (!Core.HoldRunning) yield break;
        HoldTheWaves();
        StandOn(site.Rise);

        var homed = Enumerable.Range(0, fauna.Jobs.Workers.Count).Where(i => fauna.Jobs.Workers[i].HasHome).ToList();
        yield return null;
        Check("the_workers_head_home", homed.Count > 0 && homed.All(fauna.KeptHome), $"{homed.Count(fauna.KeptHome)} of {homed.Count}");
        // They walk it, so let the clock run on quickly till they're off it.
        Time.timeScale = Hurry;
        float waited = 0f;
        string who;
        while (waited < MostWalkHome && NearestWorker(fauna, onRoad, out who) < Clear)
        {
            HoldTheWaves();
            waited += Time.deltaTime;
            yield return null;
        }
        Time.timeScale = 1f;
        int apart = NearestWorker(fauna, onRoad, out who);
        Check("the_road_empties", apart >= Clear, $"nearest worker {who} {apart} cells off, after {waited:0} s");
        Metric("walk_home_seconds", waited);

        if (!string.IsNullOrEmpty(ScreenshotDir)) film = new ClipFilm(this, ScreenshotDir, withHud: true) { Keeping = false };
        film?.Roll();
        yield return EmptyRoad(road);
        yield return TheWave(field, road, site);
        if (film != null)
        {
            yield return film.Stop();
            File.WriteAllLines(Path.Combine(ScreenshotDir, "timeline.txt"), timeline);
            Metric("seconds", film.Seconds);
            Check("short_enough", film.Seconds <= 30f, $"{film.Seconds:0.0} s");
        }
    }

    // Down the road from over the castle's gate: no one on it.
    IEnumerator EmptyRoad(List<(int x, int y)> road)
    {
        var near = Ground(road[Mathf.Min(18, road.Count - 1)]);
        var far = Ground(road[road.Count * 2 / 3]);
        var down = new Vector3(far.x - near.x, 0f, far.z - near.z).normalized;
        Cut(near + Vector3.up * 14f, far + Vector3.up * 1f);
        yield return Wait(0.6f);
        if (film != null) film.Keeping = true;
        Note("the road, empty in a run: the folk have gone home");
        yield return Wait(3.5f);
        if (film != null) film.Keeping = false;
    }

    // A mixed wave put on the road a way down, coming up it, then at the crystal.
    IEnumerator TheWave(DefenseField field, List<(int x, int y)> road, WorldHold site)
    {
        var put = new List<Raider>();
        int from = Mathf.Min(road.Count - 1, 34);
        for (int k = 0; k < Mixed.Length; k++)
        {
            // The slow bear starts nearest, the bison furthest back.
            int at = Mathf.Clamp(from - (Mixed[k] == RaiderIds.BarrowBear ? 9 : 0) + k * 2, 0, road.Count - 1);
            var cell = road[at];
            var r = Core.SpawnRaider(Mixed[k], cell.x, cell.y, Core.Rules.Entity(Mixed[k]).Hp * 40);
            if (r != null) put.Add(r);
        }
        Check("the_mixed_wave_is_out", put.Select(r => r.Kind).Distinct().Count() >= 6, string.Join(" ", put.Select(r => r.Kind)));
        yield return null;
        yield return null;
        Check("each_kind_has_its_body", field.DrawnRaiders >= put.Count, $"{field.DrawnRaiders} drawn of {put.Count}");

        var near = Ground(road[Mathf.Max(0, from - 22)]);
        var far = Ground(road[from + 4 < road.Count ? from + 4 : from]);
        var down = new Vector3(far.x - near.x, 0f, far.z - near.z).normalized;
        var side = Vector3.Cross(down, Vector3.up);
        Cut(near + side * 3f + Vector3.up * 6f, far + Vector3.up * 0.8f);
        yield return Wait(0.3f);
        if (film != null) film.Keeping = true;
        Note("a mixed wave up the road: bison, deer, wolves, hound, crab, bear");
        yield return Wait(9f);
        if (film != null) film.Keeping = false;

        var (hx, hy) = site.Heart;
        var heart = Ground((hx, hy));
        var plate = Ground(site.Plate);
        var toward = new Vector3(plate.x - heart.x, 0f, plate.z - heart.z).normalized;
        int before = Core.Hold.Health;
        for (float t = 0f; t < 60f && Core.Hold.Health == before; t += Time.deltaTime)
        {
            Core.Hold.NextWave = Core.Tick + Core.Rules.Sim.Hold.WaveTicks;
            yield return null;
        }
        Check("they_strike_the_crystal", Core.Hold.Health < before, $"keep {Core.Hold.Health}");
        // Most of them at it before the shot.
        for (float t = 0f; t < 40f && Core.Raiders.Count(r => System.Math.Abs(r.X - hx) + System.Math.Abs(r.Y - hy) <= 3) < 5; t += Time.deltaTime)
        {
            Core.Hold.NextWave = Core.Tick + Core.Rules.Sim.Hold.WaveTicks;
            yield return null;
        }
        Metric("at_the_crystal", Core.Raiders.Count(r => System.Math.Abs(r.X - hx) + System.Math.Abs(r.Y - hy) <= 3));
        // From behind the crystal, out at the way they come in: the keep's walls hide them from anywhere else.
        var crowd = Core.Raiders.Where(r => System.Math.Abs(r.X - hx) + System.Math.Abs(r.Y - hy) <= 8).Select(r => Ground((r.X, r.Y))).ToList();
        var mid = crowd.Count > 0 ? crowd.Aggregate(Vector3.zero, (a, b) => a + b) / crowd.Count : heart + toward;
        var inward = new Vector3(heart.x - mid.x, 0f, heart.z - mid.z).normalized;
        Cut(heart + inward * 4f + Vector3.up * 8.5f + Vector3.Cross(inward, Vector3.up) * 3f, (mid + heart) * 0.5f);
        yield return Wait(0.3f);
        if (film != null) film.Keeping = true;
        Note("at the crystal");
        yield return Wait(7f);
        var kinds = Core.Raiders.Select(r => Rules.PlainKind(r.Kind)).Distinct().ToList();
        Metric("kinds_at_the_end", kinds.Count);
        if (film != null) film.Keeping = false;
    }
}
