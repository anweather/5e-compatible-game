using System.Collections.Generic;
using System.IO;
using Chem;
using Punchy.Fauna;
using Unity.Mathematics;
using UnityEngine;

// The folk who have work to go to (FolkJobs): a worker stands in the world only while someone's near, and stands wherever
// the clock says it has got to, so it's met on the road or at its post however long nobody looked. Fire hands it to
// the creature brain, as it does the villages' folk, and it takes up its round again once that has passed.
public partial class ValleyFauna
{
    const string WorkerSpecies = "human";
    const float DeckFeel = 6f;

    FolkJobs jobs;
    Creature[] working;
    World jobsWorld;
    double jobClock = -1;

    /// <summary>For the probe: the workers, and where each is on its round now.</summary>
    public FolkJobs Jobs => jobs;
    public double JobClock => jobClock;

    /// <summary>For the probe: what finding the walks took.</summary>
    public double JobsBuiltMs { get; private set; }

    /// <summary>For the probe: whether a worker stands in the world, and how far the nearest is from a point.</summary>
    public bool NearestWorker(Vector3 from, out float apart)
    {
        apart = float.MaxValue;
        if (working == null) return false;
        foreach (var c in working)
            if (c != null) apart = math.min(apart, math.distance(c.state.Position.xz, new float2(from.x, from.z)));
        return apart < float.MaxValue;
    }

    /// <summary>For the probe: how many workers stand in the world.</summary>
    public int WorkersOut
    {
        get
        {
            int n = 0;
            if (working != null)
                foreach (var c in working)
                    if (c != null) n++;
            return n;
        }
    }

    void MakeJobs()
    {
        jobsWorld = world;
        int human = bodies.IndexOf(WorkerSpecies);
        string landFile = Path.Combine(ChemGrid.Folders.World, MapFile.LandName);
        if (human < 0 || !File.Exists(landFile)) return;
        var units = LandFile.Read(File.ReadAllText(landFile), world.W, world.H);
        if (units.Length == 0) return;
        float2 Centre(int x, int y)
        {
            var p = grid.CellCenter(x, y);
            return new float2(p.x, p.z);
        }
        var land = new FolkJobs.Land(world, units, grid.cellSize);
        var timer = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            jobs = FolkJobs.Load(ChemGrid.Folders.World, land, Centre, bodies[human].WalkSpeed);
            working = new Creature[jobs.Workers.Count];
        }
        catch (System.Exception e) when (e is InvalidDataException || e is Newtonsoft.Json.JsonException)
        {
            jobs = null;
            Debug.LogError($"{FolkJobs.FileName} can't be used, so no one has a job: {e.Message}");
        }
        JobsBuiltMs = timer.Elapsed.TotalMilliseconds;
    }

    // The world's clock, run on smoothly between its ticks; every peer's agrees to within the glide.
    double JobSeconds(float dt)
    {
        double target = world.Tick / (double)Mathf.Max(1, world.Rules.Sim.TicksPerSecond);
        jobClock = jobClock < 0 || System.Math.Abs(target - jobClock) > 2.0 ? target : jobClock + dt + (target - jobClock) * math.min(1f, dt);
        return jobClock;
    }

    void RunJobs(float2 centre, float dt, bool goIn)
    {
        if (world != jobsWorld) MakeJobs();
        if (jobs == null) return;
        double seconds = JobSeconds(dt);
        for (int i = 0; i < jobs.Workers.Count; i++)
        {
            var worker = jobs.Workers[i];
            var now = worker.At(seconds);
            bool near = math.distance(now.At, centre) <= radius;
            var c = working[i];
            if (c != null && !creatures.Contains(c)) c = working[i] = null;
            if (c == null)
            {
                if (near && (!goIn || worker.NightShift)) working[i] = Bring(worker, now);
                continue;
            }
            if (!near)
            {
                creatures.Remove(c);
                working[i] = null;
                continue;
            }
            Keep(c, now);
        }
    }

    Creature Bring(FolkJobs.Worker worker, FolkJobs.Now now)
    {
        int at = Spawn(bodies.IndexOf(WorkerSpecies), new float3(now.At.x, GroundOf(now), now.At.y), now.Yaw, worker.Individual);
        var c = creatures[at];
        Keep(c, now);
        return c;
    }

    void Keep(Creature c, FolkJobs.Now now)
    {
        var s = c.state;
        if (FireNear(s.Position.xz) || s.Mode == CreatureMode.Flee)
        {
            if (s.Mode == CreatureMode.Puppet) s.Mode = CreatureMode.Idle;
            c.state = s;
            return;
        }
        s.Mode = CreatureMode.Puppet;
        s.Position = new float3(now.At.x, GroundOf(now), now.At.y);
        s.Yaw = now.Yaw;
        s.YawRate = 0f;
        s.Speed = now.Pace;
        s.Task = now.Task;
        s.Lie = 0f;
        c.state = s;
    }

    // On a dock the deck is what's underfoot, not the lake bed.
    float GroundOf(FolkJobs.Now now)
    {
        float ground = Height(now.At);
        if (!now.OnDeck) return ground;
        var above = new Vector3(now.At.x, ground + DeckFeel, now.At.y);
        return Physics.Raycast(above, Vector3.down, out var hit, DeckFeel * 2f, 1) ? hit.point.y : ground;
    }
}
