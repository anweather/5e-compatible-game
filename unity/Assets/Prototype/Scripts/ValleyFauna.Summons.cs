using Chem;
using Punchy.Fauna;
using Unity.Mathematics;
using UnityEngine;

// Animals a spell brings out (spells.json "place": { "animal": ... },
// docs/design/events-and-placing.md 2a). The core lists them (World.SummonedAfter)
// with a cell and an Individual, the same on every peer; each peer brings one out
// there, facing the way its Individual says, and from then on it lives as the
// valley's other animals do, on each peer's own (J-056: view only). At most
// MaxSummoned are out on a peer: the oldest goes first. A quiet frame compares one
// number (World.LastSummonN) and asks nothing else.
public partial class ValleyFauna
{
    public const int MaxSummoned = 24;

    World summonWorld;
    int lastSummon = -1;

    public int SummonedCount
    {
        get
        {
            int n = 0;
            foreach (var c in creatures)
                if (c.summon >= 0) n++;
            return n;
        }
    }

    public bool Summoned(int n, out Vector3 at, out string species)
    {
        foreach (var c in creatures)
        {
            if (c.summon != n) continue;
            at = c.state.Position;
            species = bodies[c.species]?.Id;
            return true;
        }
        at = default;
        species = null;
        return false;
    }

    public void BringOutSummons()
    {
        world = grid != null ? grid.World : null;
        if (world == null || bodies.Count == 0) return;
        if (!ReferenceEquals(summonWorld, world))
        {
            summonWorld = world;
            lastSummon = -1;
        }
        if (world.LastSummonN <= lastSummon) return;
        foreach (var s in world.SummonedAfter(lastSummon))
        {
            lastSummon = s.N;
            BringOut(s);
        }
    }

    void BringOut(Summon s)
    {
        int species = bodies.IndexOf(s.Species);
        if (species < 0 || bodies[species] == null) return;
        LetOldestGo();
        var centre = grid.CellCenter(s.X, s.Y);
        var p = new float2(centre.x, centre.z);
        float yaw = (CreatureBody.Unit(s.Individual, 7) * 2f - 1f) * math.PI;
        int at = Spawn(species, new float3(p.x, Height(p), p.y), yaw, s.Individual);
        creatures[at].summon = s.N;
    }

    void LetOldestGo()
    {
        int oldest = -1, count = 0;
        for (int i = 0; i < creatures.Count; i++)
        {
            if (creatures[i].summon < 0) continue;
            count++;
            if (oldest < 0 || creatures[i].summon < creatures[oldest].summon) oldest = i;
        }
        if (count >= MaxSummoned) creatures.RemoveAt(oldest);
    }
}
