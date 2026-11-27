using System.Collections.Generic;
using Chem;
using Punchy.Fauna;
using Unity.Mathematics;
using UnityEngine;

// The villages' folk going about their day (docs/WORLD_LIFE.md, "Villagers"):
// each walks between its village's doors, the well and the benches, stops a
// while at each and sits on the benches; the first few are workers at their
// posts (the barkeep at the bar, one tending the well, a lookout at the inn's
// door) and keep to them. At night and in heavy rain or snow they walk to the nearest door and go
// in, and come out of the doors again when it's over. A fire near them hands
// them back to the creature brain, which runs from it as far as their leash to
// the village lets them; they take up their day again once it has passed. Driven as Punchy's puppets (they keep the heading
// and pace they're given), steered round walls by the cells, which they only
// read. Presentation only, like the rest of the fauna.
public partial class ValleyFauna
{
    sealed class FolkPlaces
    {
        public readonly List<float2> doors = new();
        public readonly List<(float2 at, float yaw, bool lounge)> benches = new();
        public float2 well;
        public bool hasBar;
        public float2 bar;
        public float barYaw;
        public bool hasInn;
        public float2 inn;
        public float innYaw;
        public bool indoors;
    }

    enum FolkTask : byte { Pause, Walk, Sit, GoIn }

    readonly List<FolkPlaces> places = new();

    const float FolkFireFear = 16f;

    static FolkPlaces PlacesOf(Village v)
    {
        var p = new FolkPlaces();
        var square = v.transform.position + v.wellAt;
        p.well = new float2(square.x, square.z) + new float2(1.4f, 0.6f);
        foreach (var house in v.GetComponentsInChildren<VillageHouse>())
        {
            var t = house.transform;
            var outward = -t.forward;
            var door = t.position + outward * (house.depth / 2f + 0.8f);
            p.doors.Add(new float2(door.x, door.z));
            if (house.kind == BuildingKind.Shop && !p.hasBar)
            {
                p.hasBar = true;
                p.bar = new float2(door.x, door.z);
                p.barYaw = math.atan2(outward.x, outward.z);
            }
            if (house.kind == BuildingKind.Inn && !p.hasInn)
            {
                p.hasInn = true;
                p.inn = new float2(door.x, door.z);
                p.innYaw = math.atan2(outward.x, outward.z);
            }
        }
        float reach = v.wander + 12f;
        foreach (var seat in Seat.All)
        {
            var foot = seat.Foot;
            if (math.distance(new float2(foot.x, foot.z), new float2(square.x, square.z)) > reach) continue;
            var f = seat.Facing * Vector3.forward;
            p.benches.Add((new float2(foot.x, foot.z), math.atan2(f.x, f.z), seat.lounge));
        }
        return p;
    }

    static (CreatureTask job, float2 post, float yaw) PostOf(int n, FolkPlaces p)
    {
        if (n == 0 && p.hasBar) return (CreatureTask.Gesture, p.bar, p.barYaw);
        if (n == 1)
        {
            var well = p.well - new float2(1.4f, 0.6f);
            return (CreatureTask.Tend, p.well, math.atan2(well.x - p.well.x, well.y - p.well.y));
        }
        if (n == 2 && p.hasInn) return (CreatureTask.Watch, p.inn, p.innYaw);
        return (CreatureTask.None, default, 0f);
    }

    bool FolkGoIn(float night)
    {
        var weather = world.Weather;
        return night > 0.55f || weather.Rain > 30 || (weather.Kind == WeatherKind.Snow && weather.Strength > 50);
    }

    void RunFolk(float dt, float night)
    {
        bool goIn = FolkGoIn(night);
        if (!goIn)
            foreach (var p in places) p.indoors = false;
        for (int i = creatures.Count - 1; i >= 0; i--)
        {
            var c = creatures[i];
            if (c.village < 0 || c.village >= places.Count) continue;
            if (!StepFolk(c, places[c.village], goIn, dt))
            {
                creatures.RemoveAt(i);
                places[c.village].indoors = goIn;
            }
        }
    }

    bool StepFolk(Creature c, FolkPlaces p, bool goIn, float dt)
    {
        var s = c.state;
        if (FireNear(s.Position.xz))
        {
            if (s.Mode == CreatureMode.Puppet)
            {
                s.Mode = CreatureMode.Idle;
                s.Lie = 0f;
                s.Task = CreatureTask.None;
                c.task = FolkTask.Pause;
                c.wait = 0f;
            }
            c.state = s;
            return true;
        }
        if (s.Mode == CreatureMode.Flee) return true;
        s.Mode = CreatureMode.Puppet;

        if (goIn && c.task != FolkTask.GoIn && p.doors.Count > 0)
        {
            c.task = FolkTask.GoIn;
            c.goal = Nearest(p.doors, s.Position.xz);
            s.Lie = 0f;
            s.Task = CreatureTask.None;
        }
        var b = bodies[c.species];
        switch (c.task)
        {
            case FolkTask.GoIn:
                if (Steer(ref s, c.goal, b, dt)) return false;
                break;
            case FolkTask.Walk:
                if (Steer(ref s, c.goal, b, dt)) Arrive(c, ref s, p);
                break;
            case FolkTask.Sit:
            case FolkTask.Pause:
                s.Speed = 0f;
                c.wait -= dt;
                if (c.wait <= 0f && s.Task == CreatureTask.None) Next(c, ref s, p);
                break;
        }
        c.state = s;
        return true;
    }

    void Arrive(Creature c, ref CreatureState s, FolkPlaces p)
    {
        s.Speed = 0f;
        c.task = FolkTask.Pause;
        c.wait = rng.NextFloat(4f, 12f);
        if (c.job != CreatureTask.None)
        {
            s.Yaw = c.postYaw;
            s.Task = c.job;
            return;
        }
        foreach (var (at, yaw, lounge) in p.benches)
        {
            if (math.distancesq(at, c.goal) > 0.01f) continue;
            s.Position = new float3(at.x, s.Position.y, at.y);
            s.Yaw = yaw;
            s.Lie = lounge ? 1f : 0.35f;
            c.task = FolkTask.Sit;
            c.wait = rng.NextFloat(12f, 30f);
            return;
        }
    }

    void Next(Creature c, ref CreatureState s, FolkPlaces p)
    {
        s.Lie = 0f;
        c.task = FolkTask.Walk;
        if (c.job != CreatureTask.None)
        {
            c.goal = c.post;
            return;
        }
        float roll = rng.NextFloat();
        if (roll < 0.35f && FreeSeat(c, p, out var seat)) c.goal = seat;
        else if (roll < 0.65f || p.doors.Count == 0) c.goal = p.well + rng.NextFloat2Direction() * 0.8f;
        else c.goal = p.doors[rng.NextInt(p.doors.Count)];
    }

    bool FreeSeat(Creature c, FolkPlaces p, out float2 seat)
    {
        seat = default;
        int free = 0;
        foreach (var (at, _, _) in p.benches) if (!SeatTaken(c, at)) free++;
        if (free == 0) return false;
        int pick = rng.NextInt(free);
        foreach (var (at, _, _) in p.benches)
        {
            if (SeatTaken(c, at)) continue;
            if (pick-- == 0)
            {
                seat = at;
                return true;
            }
        }
        return false;
    }

    bool SeatTaken(Creature self, float2 at)
    {
        foreach (var other in creatures)
            if (other != self && other.village >= 0 && (other.task == FolkTask.Sit || other.task == FolkTask.Walk)
                && math.distancesq(other.goal, at) < 0.01f)
                return true;
        return false;
    }

    bool Steer(ref CreatureState s, float2 goal, CreatureBody b, float dt)
    {
        var to = goal - s.Position.xz;
        float d = math.length(to);
        if (d < 0.6f)
        {
            s.Speed = 0f;
            s.YawRate = 0f;
            return true;
        }
        float east = to.x, north = to.y;
        float want = math.atan2(east, north);
        float yaw = s.Yaw + math.clamp(Wrap(want - s.Yaw), -3f * dt, 3f * dt);
        for (int k = 0; k < 5; k++)
        {
            float side = (k & 1) == 1 ? 1f : -1f;
            float tryYaw = yaw + side * 0.6f * ((k + 1) / 2);
            var ahead = s.Position.xz + new float2(math.sin(tryYaw), math.cos(tryYaw)) * 1.2f;
            if (!Walkable(ahead)) continue;
            yaw = tryYaw;
            break;
        }
        s.YawRate = Wrap(yaw - s.Yaw) / math.max(dt, 1e-4f);
        s.Yaw = Wrap(yaw);
        s.Speed = b.WalkSpeed * CreatureBrain.Pace(s.Scale) * (1f + CreatureBody.Trait(s.Individual, 5) * 0.08f);
        return false;
    }

    static float Wrap(float a) => a - 2f * math.PI * math.floor((a + math.PI) / (2f * math.PI));

    static float2 Nearest(List<float2> spots, float2 from)
    {
        float2 best = spots[0];
        float bestSq = float.MaxValue;
        foreach (var p in spots)
        {
            float d = math.distancesq(p, from);
            if (d >= bestSq) continue;
            bestSq = d;
            best = p;
        }
        return best;
    }

    public List<Vector3> FolkOf(Village village, out int sitting)
    {
        var at = new List<Vector3>();
        sitting = 0;
        int v = folk.FindIndex(f => f.village == village);
        foreach (var c in creatures)
        {
            if (v < 0 || c.village != v) continue;
            at.Add(c.state.Position);
            if (c.task == FolkTask.Sit) sitting++;
        }
        return at;
    }

    public List<string> FolkKinds(Village village, out int working)
    {
        var kinds = new List<string>();
        working = 0;
        int v = folk.FindIndex(f => f.village == village);
        foreach (var c in creatures)
        {
            if (v < 0 || c.village != v) continue;
            if (c.state.Task != CreatureTask.None) working++;
            kinds.Add(c.job == CreatureTask.None ? bodies[c.species].Id : $"{bodies[c.species].Id} ({c.job})");
        }
        return kinds;
    }

    public bool FirstSitter(Village village, out Vector3 at, out float yaw)
    {
        int v = folk.FindIndex(f => f.village == village);
        foreach (var c in creatures)
        {
            if (v < 0 || c.village != v || c.task != FolkTask.Sit) continue;
            at = c.state.Position;
            yaw = c.state.Yaw;
            return true;
        }
        at = default;
        yaw = 0f;
        return false;
    }

    bool FireNear(float2 at)
    {
        foreach (var f in fires)
            if (math.distancesq(at, (float2)f) < FolkFireFear * FolkFireFear) return true;
        return false;
    }
}
