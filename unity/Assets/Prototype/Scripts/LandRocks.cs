// The rocks on stamped land: boulders set into its faces, big ones jutting at their lips, and loose stones at their feet.
// The ground has one height a point, so these are what make a wall read as a crag and give it an overhang. They're
// only drawn: no collider, nothing shared, and each peer works the same ones out from the land itself, so a late joiner
// and an undo need nothing of their own. A rock is placed a few a frame and rides its cell's ground as that eases. A map's
// rocks.json lays more by hand, where a hill wants them.
using System.Collections.Generic;
using System.IO;
using Chem;
using Newtonsoft.Json.Linq;
using UnityEngine;

public sealed class LandRocks
{
    const int FaceDrop = 12, FootReach = 2, ClumpCells = 5;
    // Room for a map's own stamps' rocks (the valley's cliff and canyon take about 600) and as many again for acts.
    const int PlacedPerFrame = 6, MostRocks = 1400;
    const float StillSeconds = 1f, StillMetres = 0.001f;

    enum Kind
    {
        None,
        Face,
        Lip,
        Scree,
        Laid,
    }

    sealed class Rock
    {
        public Kind Kind;
        public Transform Body;
        public Vector3 Spot;
        public float Over, Last;
    }

    readonly ChemGrid grid;
    Transform root;
    readonly Dictionary<int, Rock> rocks = new();
    readonly Queue<(int x, int y)> waiting = new();
    readonly HashSet<int> queued = new();
    readonly Dictionary<int, float> laid = new();
    float still;

    public LandRocks(ChemGrid grid)
    {
        this.grid = grid;
        MakeRoot();
    }

    void MakeRoot()
    {
        root = new GameObject("Land rocks").transform;
        root.SetParent(grid.transform, false);
    }

    // An online reset (ChemGrid.ResetWorld) destroys everything under the grid, these rocks with it (playtest 2026-11-08
    // L1: their bodies were still read): a new root, and every cell that had one looked at again on the new world's land.
    void Regrow()
    {
        MakeRoot();
        int w = grid.World.W;
        foreach (int key in rocks.Keys)
            if (queued.Add(key)) waiting.Enqueue((key % w, key / w));
        rocks.Clear();
        still = 0f;
    }

    /// <summary>How many rocks the land's faces have now; a map's own aren't counted.</summary>
    public int Count
    {
        get
        {
            int n = 0;
            foreach (var r in rocks.Values)
                if (r.Kind != Kind.Laid) n++;
            return n;
        }
    }

    /// <summary>How many of the land's own rocks stand within a square this far round a cell (a map's stamps have theirs elsewhere).</summary>
    public int Near(int x, int y, int reach)
    {
        int w = grid.World.W, n = 0;
        foreach (var kv in rocks)
            if (kv.Value.Kind != Kind.Laid && System.Math.Abs(kv.Key % w - x) <= reach && System.Math.Abs(kv.Key / w - y) <= reach) n++;
        return n;
    }

    /// <summary>The rocks a map's folder lays, each [x, y, metres across]: none without a rocks.json.</summary>
    public void Lay(string worldDir)
    {
        string path = Path.Combine(worldDir, "rocks.json");
        if (!File.Exists(path)) return;
        foreach (var r in (JArray)JObject.Parse(File.ReadAllText(path))["rocks"])
        {
            int x = (int)r[0], y = (int)r[1];
            laid[grid.World.Idx(x, y)] = (float)r[2];
            Moved(x, y, 0);
        }
    }

    /// <summary>The land moved round a cell: what stands there is looked at again.</summary>
    public void Moved(int x, int y, int reach)
    {
        var world = grid.World;
        for (int v = y - reach; v <= y + reach; v++)
            for (int u = x - reach; u <= x + reach; u++)
                if (world.InBounds(u, v) && queued.Add(world.Idx(u, v))) waiting.Enqueue((u, v));
        still = 0f;
    }

    /// <summary>Once a frame: a few more rocks, and the ones that stand ride the ground.</summary>
    public void Tick(float dt)
    {
        var world = grid.World;
        if (root == null) Regrow();
        for (int placed = 0; placed < PlacedPerFrame && waiting.Count > 0;)
        {
            var (x, y) = waiting.Dequeue();
            int key = world.Idx(x, y);
            queued.Remove(key);
            var kind = laid.ContainsKey(key) ? Kind.Laid : KindAt(world, x, y);
            rocks.TryGetValue(key, out var was);
            if ((was?.Kind ?? Kind.None) == kind) continue;
            if (was != null)
            {
                if (was.Body != null) Object.Destroy(was.Body.gameObject);
                rocks.Remove(key);
            }
            if (kind == Kind.None || rocks.Count >= MostRocks) continue;
            rocks[key] = Place(world, x, y, kind);
            placed++;
        }
        if (still > StillSeconds && waiting.Count == 0) return;
        bool moved = false;
        foreach (var rock in rocks.Values)
        {
            float ground = grid.GroundAt(rock.Spot);
            if (Mathf.Abs(ground - rock.Last) < StillMetres) continue;
            rock.Last = ground;
            var p = rock.Body.position;
            p.y = ground + rock.Over;
            rock.Body.position = p;
            moved = true;
        }
        still = moved ? 0f : still + dt;
    }

    static uint Hash(int x, int y, int salt) => WorldHash.Hash4(salt, 4177, x, y);

    // Rocks come in clumps, so a long face doesn't wear an even row of them.
    static bool InClump(int x, int y) => Hash(x / ClumpCells, y / ClumpCells, 1) % 5 < 3;

    static Kind KindAt(World world, int x, int y)
    {
        int drop = world.LandDrop(x, y);
        // A short face is a bump's bank, not a crag: no boulders on it, nor at its foot (#148).
        if (drop == 0 || world.LandFace(x, y) < World.LandCrag) return Kind.None;
        uint roll = Hash(x, y, 2);
        if (drop >= FaceDrop)
        {
            if (!InClump(x, y)) return Kind.None;
            if (IsLip(world, x, y)) return roll % 3 == 0 ? Kind.Lip : Kind.None;
            return roll % 4 == 0 ? Kind.Face : Kind.None;
        }
        return drop < World.LandSteep && roll % 4 == 0 && IsFoot(world, x, y) ? Kind.Scree : Kind.None;
    }

    // At the top of a face: every cell higher than this one beside it is no longer steep.
    static bool IsLip(World world, int x, int y)
    {
        int here = world.LandMovedAt(x, y);
        for (int v = -1; v <= 1; v++)
            for (int u = -1; u <= 1; u++)
                if (world.LandMovedAt(x + u, y + v) > here && world.LandDrop(x + u, y + v) >= FaceDrop) return false;
        return true;
    }

    // Low ground with a face rising close by.
    static bool IsFoot(World world, int x, int y)
    {
        int here = world.LandMovedAt(x, y);
        for (int v = -FootReach; v <= FootReach; v++)
            for (int u = -FootReach; u <= FootReach; u++)
                if (world.LandDrop(x + u, y + v) >= FaceDrop && world.LandMovedAt(x + u, y + v) > here) return true;
        return false;
    }

    // The way down the land from a cell, flat, in the world.
    Vector3 Down(World world, int x, int y)
    {
        var east = (grid.CellCenter(x + 1, y) - grid.CellCenter(x, y)).normalized;
        var south = (grid.CellCenter(x, y + 1) - grid.CellCenter(x, y)).normalized;
        var down = east * (world.LandMovedAt(x - 1, y) - world.LandMovedAt(x + 1, y)) + south * (world.LandMovedAt(x, y - 1) - world.LandMovedAt(x, y + 1));
        down.y = 0f;
        return down.sqrMagnitude > 0f ? down.normalized : Vector3.zero;
    }

    Rock Place(World world, int x, int y, Kind kind)
    {
        var rng = new System.Random((int)Hash(x, y, 3));
        var spot = grid.CellCenter(x, y);
        var down = Down(world, x, y);
        var models = kind == Kind.Scree ? grid.wallStones : grid.wallRocks;
        float wide, tall, over;
        Vector3 outward;
        switch (kind)
        {
            case Kind.Lip:
                wide = 5f + 5f * (float)rng.NextDouble();
                tall = wide * 0.8f;
                over = -1.5f - 2f * (float)rng.NextDouble();
                outward = down * 1.2f;
                break;
            case Kind.Face:
                wide = 3.5f + 3.5f * (float)rng.NextDouble();
                tall = wide * 1.4f;
                over = -2f;
                outward = -down * 0.8f;
                break;
            case Kind.Laid:
                wide = laid[world.Idx(x, y)];
                tall = wide * (0.6f + 0.4f * (float)rng.NextDouble());
                over = -wide * 0.2f;
                outward = Vector3.zero;
                break;
            default:
                wide = 0.7f + 1.6f * (float)rng.NextDouble();
                tall = wide * 0.7f;
                over = -0.1f;
                outward = Vector3.zero;
                break;
        }
        float ground = grid.GroundAt(spot);
        var foot = new Vector3(spot.x + outward.x, ground + over, spot.z + outward.z);
        var body = models != null && models.Length > 0 ? CellDressing.Rock(root, models[rng.Next(models.Length)], foot, wide, tall, rng) : null;
        var made = body != null ? body : Lump(foot, wide, tall);
        foreach (var collider in made.GetComponentsInChildren<Collider>())
        {
            if (Application.isPlaying) Object.Destroy(collider);
            else Object.DestroyImmediate(collider); // an Edit mode test's
        }
        return new Rock { Kind = kind, Body = made.transform, Spot = spot, Over = made.transform.position.y - ground, Last = ground };
    }

    // Without the pack's rocks: a rough cube.
    GameObject Lump(Vector3 foot, float wide, float tall)
    {
        var lump = GameObject.CreatePrimitive(PrimitiveType.Cube);
        lump.transform.SetParent(root, false);
        lump.transform.localScale = new Vector3(wide, tall, wide);
        lump.transform.position = foot + Vector3.up * (tall * 0.5f);
        return lump;
    }
}
