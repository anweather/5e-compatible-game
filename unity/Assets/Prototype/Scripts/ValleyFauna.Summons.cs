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
    /// <summary>The most summoned animals out on a peer at once (the design's view cap).</summary>
    public const int MaxSummoned = 24;

    // The world the summons were read from, and the last one brought out (its N), so each comes out once.
    World summonWorld;
    int lastSummon = -1;

    /// <summary>How many animals spells brought out are out now, for the probe.</summary>
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

    /// <summary>Where the summoned animal with this N is now, and its species' id; false once it's gone (for the probe).</summary>
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

    /// <summary>
    /// Brings out what spells summoned since the last look (Update calls it each frame; public for the probe's cost). A new
    /// world (a reset, or joining a room) starts the count again, so a late joiner brings out what its replay summoned lately.
    /// </summary>
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
