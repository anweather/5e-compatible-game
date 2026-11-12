// People with work to go to (world/maps/<id>/jobs.json, docs/WORLD_LIFE.md, "Jobs"): a job is stops a worker keeps going
// round, each with what it does there, and the walks between are found over the land by what they cost a person on foot, so
// the road is taken because it's quicker than the hill. Where a worker is comes from the clock alone, so it needs no
// saving, every peer sees the same and it holds with nobody near. Presentation only, like the rest of the fauna (D12).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Chem;
using Newtonsoft.Json.Linq;
using Punchy.Fauna;
using Unity.Mathematics;

public sealed class FolkJobs
{
    public const string FileName = "jobs.json";
    const float LandUnit = 0.125f, RoadPace = 1.15f, HillDrag = 5f;
    const int Smoothing = 2;

    /// <summary>How fast a person goes for ground this steep (rise over run), against their walking pace.</summary>
    public static float Pace(float steepness, bool road) => road ? RoadPace : 1f / (1f + HillDrag * math.abs(steepness));

    /// <summary>The ground a walk crosses: what a step costs, read from the world as it was made.</summary>
    public sealed class Land
    {
        readonly World world;
        readonly LandChunks units;
        readonly float cellMetres;

        public Land(World world, LandChunks units, float cellMetres)
        {
            this.world = world;
            this.units = units;
            this.cellMetres = cellMetres;
        }

        public float Metres(int x, int y) => (units.At(x, y) + world.LandMovedAt(x, y)) * LandUnit;

        public bool Road(int x, int y) => world.At(x, y)?.T == TerrainIds.Dirt;

        public bool Open(int x, int y)
        {
            var c = world.At(x, y);
            return c != null && c.Water == 0 && !world.IsBlocking(c) && c.T != TerrainIds.Tree;
        }

        /// <summary>The seconds, at a walking pace of one metre a second, from a cell to one beside it.</summary>
        public float Step(int x, int y, int u, int v)
        {
            float run = cellMetres * math.sqrt((x - u) * (x - u) + (y - v) * (y - v));
            return run / Pace((Metres(u, v) - Metres(x, y)) / run, Road(x, y) && Road(u, v));
        }

        public int Cells => world.W * world.H;
        public int Index(int x, int y) => world.Idx(x, y);
        public bool Inside(int x, int y) => world.InBounds(x, y);
        public float CellMetres => cellMetres;
    }

    /// <summary>The quickest way on foot between two cells, cell by cell; empty if there's none.</summary>
    public static List<(int x, int y)> Way(Land land, (int x, int y) from, (int x, int y) to)
    {
        // The cells reached so far, by index: their cost from the start, the cell they were reached from, and where they
        // are. Only those the search reaches, never an array a cell of the map (8 bytes a cell, 318 MB a walk on the
        // 12 x 10; J-086).
        var open = new SortedSet<(float rank, int key, int x, int y)>();
        int start = land.Index(from.x, from.y), goal = land.Index(to.x, to.y);
        open.Add((0f, start, from.x, from.y));
        var reached = new Dictionary<int, (float cost, int came, int x, int y)> { [start] = (0f, -1, from.x, from.y) };
        while (open.Count > 0)
        {
            var now = open.Min;
            open.Remove(now);
            if (now.key == goal) return Back(reached, goal);
            float here = reached[now.key].cost;
            for (int v = now.y - 1; v <= now.y + 1; v++)
                for (int u = now.x - 1; u <= now.x + 1; u++)
                {
                    if ((u == now.x && v == now.y) || !land.Inside(u, v) || !land.Open(u, v)) continue;
                    if (u != now.x && v != now.y && (!land.Open(u, now.y) || !land.Open(now.x, v))) continue;
                    int key = land.Index(u, v);
                    float through = here + land.Step(now.x, now.y, u, v);
                    bool seen = reached.TryGetValue(key, out var was);
                    if (seen && through >= was.cost) continue;
                    if (seen) open.Remove((was.cost + Left(land, u, v, to), key, u, v));
                    reached[key] = (through, now.key, u, v);
                    open.Add((through + Left(land, u, v, to), key, u, v));
                }
        }
        return new List<(int x, int y)>();
    }

    // The least the rest of the way could take.
    static float Left(Land land, int x, int y, (int x, int y) to) =>
        land.CellMetres * math.sqrt((x - to.x) * (x - to.x) + (y - to.y) * (y - to.y)) / RoadPace;

    static List<(int x, int y)> Back(Dictionary<int, (float cost, int came, int x, int y)> reached, int goal)
    {
        var way = new List<(int x, int y)>();
        for (int k = goal; k >= 0; k = reached[k].came) way.Add((reached[k].x, reached[k].y));
        way.Reverse();
        return way;
    }

    /// <summary>The seconds a way takes at a walking pace of one metre a second.</summary>
    public static float Seconds(Land land, List<(int x, int y)> way)
    {
        float sum = 0f;
        for (int k = 0; k + 1 < way.Count; k++) sum += land.Step(way[k].x, way[k].y, way[k + 1].x, way[k + 1].y);
        return sum;
    }

    /// <summary>Where a worker is at a moment.</summary>
    public struct Now
    {
        public float2 At;
        public float Yaw, Pace;
        public CreatureTask Task;
        public bool OnDeck;
    }

    internal sealed class Leg
    {
        public float Seconds;
        // A stop: where it stands. A walk: its points, and when it reaches each, from the leg's start.
        public float2[] Points;
        public float[] Times;
        public float Yaw;
        public CreatureTask Task;
        public bool Deck, Walk;
    }

    /// <summary>One person with a job: the same round, and the same person, on every peer.</summary>
    public sealed class Worker
    {
        public string Job;
        public uint Individual;
        public bool NightShift;
        internal readonly List<Leg> legs = new();
        internal float period, start;
        // The stop it keeps to while a Hold run is on (a job's "inARun"), as a leg of its round, and the cell its walk there ends on.
        internal int homeLeg = -1;
        internal (int x, int y) homeCell;

        /// <summary>Whether it has a stop to keep to while a Hold run is on (a job's "inARun").</summary>
        public bool HasHome => homeLeg >= 0;

        /// <summary>Standing at that stop, at its work.</summary>
        public Now Home => Stand(legs[homeLeg]);

        /// <summary>Whether its round has it at that stop now: where it rejoins the round after a run.</summary>
        public bool AtHome(double seconds) => homeLeg >= 0 && LegAt(seconds) == homeLeg;

        int LegAt(double seconds)
        {
            double t = (seconds + start) % period;
            for (int i = 0; i < legs.Count; i++)
            {
                if (t < legs[i].Seconds) return i;
                t -= legs[i].Seconds;
            }
            return 0;
        }

        static Now Stand(Leg leg) => new Now { At = leg.Points[0], Yaw = leg.Yaw, Task = leg.Task, OnDeck = leg.Deck };

        public Now At(double seconds)
        {
            double t = (seconds + start) % period;
            foreach (var leg in legs)
            {
                if (t >= leg.Seconds)
                {
                    t -= leg.Seconds;
                    continue;
                }
                return leg.Walk ? Walking(leg, (float)t) : Stand(leg);
            }
            return Stand(legs[0]);
        }

        internal static Now Walking(Leg leg, float t)
        {
            int k = Array.BinarySearch(leg.Times, t);
            if (k < 0) k = ~k;
            k = math.clamp(k, 1, leg.Points.Length - 1);
            float gap = math.max(1e-4f, leg.Times[k] - leg.Times[k - 1]);
            var from = leg.Points[k - 1];
            var to = leg.Points[k];
            var step = to - from;
            return new Now
            {
                At = math.lerp(from, to, math.saturate((t - leg.Times[k - 1]) / gap)),
                Yaw = math.atan2(step.x, step.y),
                Pace = math.length(step) / gap,
                OnDeck = leg.Deck,
            };
        }
    }

    /// <summary>A walk off the round: to a worker's stop for a Hold run, from wherever it was.</summary>
    public sealed class Trip
    {
        internal Leg leg;
        internal Now end;

        public float Seconds => leg.Seconds;

        /// <summary>Where it is this long after setting off; at the stop once there.</summary>
        public Now At(float seconds) => seconds >= leg.Seconds || leg.Points.Length < 2 ? end : Worker.Walking(leg, seconds);
    }

    public readonly List<Worker> Workers = new();
    Land land;
    Func<int, int, float2> centre;
    float walkSpeed;

    /// <summary>The walk from a cell to a worker's stop for a Hold run, over the land; null if it has none or there's no way.</summary>
    public Trip HomeFrom(Worker worker, (int x, int y) from)
    {
        if (!worker.HasHome || land == null) return null;
        var way = Way(land, OnLand(from), worker.homeCell);
        if (way.Count == 0) return null;
        var points = Smoothed(way, centre).ToList();
        if (way[0] != from) points.Insert(0, centre(from.x, from.y)); // off the dock first
        points.Add(worker.Home.At);
        return new Trip { leg = Timed(points, way, land, walkSpeed), end = worker.Home };
    }

    /// <summary>The jobs of a map's folder, walked over its land; none without a jobs.json.</summary>
    public static FolkJobs Load(string worldDir, Land land, Func<int, int, float2> centre, float walkSpeed)
    {
        var jobs = new FolkJobs { land = land, centre = centre, walkSpeed = walkSpeed };
        string path = Path.Combine(worldDir, FileName);
        if (!File.Exists(path)) return jobs;
        var file = JObject.Parse(File.ReadAllText(path));
        var spots = (JObject)file["spots"];
        var ways = new Dictionary<(string, string), List<(int x, int y)>>();
        foreach (var job in ((JArray)file["jobs"]).OfType<JObject>())
            for (int n = 0; n < (int)job["people"]; n++)
                jobs.Workers.Add(Round(job, n, spots, ways, land, centre, walkSpeed));
        return jobs;
    }

    static Worker Round(JObject job, int n, JObject spots, Dictionary<(string, string), List<(int x, int y)>> ways, Land land, Func<int, int, float2> centre, float walkSpeed)
    {
        string id = (string)job["id"];
        uint individual = math.hash(new int2(id.Aggregate(17, (h, c) => h * 31 + c), n)) | 1u;
        var worker = new Worker { Job = id, Individual = individual, NightShift = (bool?)job["night"] ?? false };
        var stops = ((JArray)job["stops"]).OfType<JObject>().ToList();
        for (int k = 0; k < stops.Count; k++)
        {
            var stop = stops[k];
            var next = stops[(k + 1) % stops.Count];
            var spot = (JObject)spots[(string)stop["at"]];
            var there = centre((int)spot["at"][0], (int)spot["at"][1]) + Offset(individual, k);
            var stay = (JArray)stop["stay"];
            float seconds = math.lerp((float)stay[0], (float)stay[1], CreatureBody.Unit(individual, 20 + k));
            worker.legs.Add(new Leg { Seconds = seconds, Points = new[] { there }, Yaw = math.radians((float?)spot["facing"] ?? 0f), Task = TaskOf((string)stop["do"]), Deck = spot["out"] != null });
            worker.legs.Add(Walk(Spot(spots, stop), Spot(spots, next), ways, land, centre, walkSpeed));
        }
        if (job["inARun"] is JToken home)
        {
            int k = stops.FindIndex(st => (string)st["at"] == (string)home);
            if (k < 0) throw new InvalidDataException($"{FileName}: job {id}'s inARun {home} is none of its stops");
            worker.homeLeg = k * 2;
            worker.homeCell = Foot((JObject)spots[(string)home]);
        }
        worker.period = worker.legs.Sum(l => l.Seconds);
        worker.start = CreatureBody.Unit(individual, 30) * worker.period;
        return worker;
    }

    static (string id, JObject row) Spot(JObject spots, JObject stop) => ((string)stop["at"], (JObject)spots[(string)stop["at"]]);

    static float2 Offset(uint individual, int salt) =>
        new float2(CreatureBody.Unit(individual, 40 + salt) - 0.5f, CreatureBody.Unit(individual, 50 + salt) - 0.5f) * 2.4f;

    static CreatureTask TaskOf(string name)
    {
        switch (name)
        {
            case "watch": return CreatureTask.Watch;
            case "tend": return CreatureTask.Tend;
            case "gesture": return CreatureTask.Gesture;
            default: return CreatureTask.None;
        }
    }

    // A spot out over the water is reached from the shore: "out" is the land its walk ends at.
    static (int x, int y) Foot(JObject spot)
    {
        var at = (JArray)(spot["out"] ?? spot["at"]);
        return ((int)at[0], (int)at[1]);
    }

    // The nearest cell a walk can start on: off a dock it's the shore.
    (int x, int y) OnLand((int x, int y) at)
    {
        const int Most = 24;
        for (int r = 0; r <= Most; r++)
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                    if ((System.Math.Abs(dx) == r || System.Math.Abs(dy) == r) && land.Inside(at.x + dx, at.y + dy) && land.Open(at.x + dx, at.y + dy))
                        return (at.x + dx, at.y + dy);
        return at;
    }

    static Leg Walk((string id, JObject row) from, (string id, JObject row) to, Dictionary<(string, string), List<(int x, int y)>> ways, Land land, Func<int, int, float2> centre, float walkSpeed)
    {
        if (!ways.TryGetValue((from.id, to.id), out var way)) ways[(from.id, to.id)] = way = Way(land, Foot(from.row), Foot(to.row));
        if (way.Count == 0) throw new InvalidDataException($"{FileName}: there is no way on foot from {from.id} to {to.id}");
        var points = new List<float2>();
        if (from.row["out"] != null) points.Add(centre((int)from.row["at"][0], (int)from.row["at"][1]));
        points.AddRange(Smoothed(way, centre));
        if (to.row["out"] != null) points.Add(centre((int)to.row["at"][0], (int)to.row["at"][1]));
        return Timed(points, way, land, walkSpeed);
    }

    // When a walk reaches each of its points, at the walker's pace over the land under it.
    static Leg Timed(List<float2> points, List<(int x, int y)> way, Land land, float walkSpeed)
    {
        var times = new float[points.Count];
        for (int k = 1; k < points.Count; k++)
        {
            int a = math.min(k - 1, way.Count - 1), b = math.min(k, way.Count - 1);
            float pace = a == b || way.Count < 2 ? 1f : land.CellMetres * math.sqrt((way[a].x - way[b].x) * (way[a].x - way[b].x) + (way[a].y - way[b].y) * (way[a].y - way[b].y)) / land.Step(way[a].x, way[a].y, way[b].x, way[b].y);
            times[k] = times[k - 1] + math.distance(points[k - 1], points[k]) / (walkSpeed * math.max(0.05f, pace));
        }
        return new Leg { Walk = true, Points = points.ToArray(), Times = times, Seconds = times[^1] };
    }

    // The cells' corners rounded off, so a walk bends and doesn't zigzag.
    static IEnumerable<float2> Smoothed(List<(int x, int y)> way, Func<int, int, float2> centre)
    {
        var points = way.Select(c => centre(c.x, c.y)).ToList();
        for (int k = 0; k < points.Count; k++)
        {
            int from = math.max(0, k - Smoothing), to = math.min(points.Count - 1, k + Smoothing);
            var sum = float2.zero;
            for (int i = from; i <= to; i++) sum += points[i];
            yield return sum / (to - from + 1);
        }
    }
}
