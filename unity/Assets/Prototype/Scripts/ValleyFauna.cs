using System.Collections.Generic;
using System.Runtime.InteropServices;
using Chem;
using Punchy.Fauna;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;

// The valley's animals: deer, rabbits, foxes, turkeys, frogs by the water and
// the odd bear, kept around the camera. Built from Punchy's creatures
// (Zachary Franklin, ThirdParty/Punchy): each species is parts on a body,
// posed procedurally with feet placed on the ground, all drawn in one
// instanced call. Each lives its own day (Punchy's activities): grazing,
// walking to the water to drink, lying down to rest, sleeping at night (or by
// day, for night animals), playing chase with its own kind, and hunting what
// it hunts (wolves and foxes); prey runs from a hunter, and everything bolts
// from a wizard who comes too close, and from fire. They keep to dry, clear,
// unburnt cells.
// Presentation only, like the fireflies: each machine has its own animals,
// and nothing they do reaches the chemistry.
public class ValleyFauna : MonoBehaviour, ICreatureGround
{
    [System.Serializable]
    public class Entry
    {
        public CreatureSpecies species;
        /// <summary>Animals per hectare (10 000 m²).</summary>
        public float density = 0.5f;
        /// <summary>Only near water (frogs, herons).</summary>
        public bool nearWater;
    }

    public Material material;
    public List<Entry> species = new();
    /// <summary>Animals are kept within this distance of the camera.</summary>
    public float radius = 90f;
    /// <summary>New animals appear at least this far away.</summary>
    public float minSpawnDistance = 35f;
    /// <summary>Animals further than this are removed.</summary>
    public float despawnDistance = 130f;
    public int maxCreatures = 120;
    /// <summary>Spawn attempts per second.</summary>
    public float spawnRate = 6f;
    /// <summary>
    /// The species' densities times this round where the wizard starts, rising to wildDensity out in
    /// the wilds (between homeRadius and wildRadius from the start): a quiet start, more to find exploring.
    /// </summary>
    public float homeDensity = 0.3f, wildDensity = 1.4f;
    public float homeRadius = 60f, wildRadius = 300f;

    sealed class Creature
    {
        public int id;
        public int species;
        public int coat;
        public CreatureState state;
    }

    static readonly ProfilerMarker UpdateMarker = new("ValleyFauna.Update");
    static readonly int SegmentsId = Shader.PropertyToID("_Segments");

    readonly List<Creature> creatures = new();
    readonly List<CreatureBody> bodies = new();
    readonly List<Vector2> wizards = new(), fires = new();
    readonly CreatureAnimator animator = new();
    readonly CreatureBrain brain = new();
    readonly Herd herd = new();
    SkyLight sky;
    int[] counts;
    SegmentInstance[] instances = new SegmentInstance[256];
    GraphicsBuffer buffer;
    MaterialPropertyBlock props;
    Mesh capsule;
    ChemGrid grid;
    World world;
    float2? home;
    float spawnBudget, nextLook;
    int nextId;
    Unity.Mathematics.Random rng = new(0x9E3779B9u);

    /// <summary>How many animals are out, for the probe.</summary>
    public int Count => creatures.Count;

    /// <summary>The species built from the list, by index (for the probe).</summary>
    public int SpeciesIndex(string name) => species.FindIndex(e => e.species != null && e.species.name == name);

    /// <summary>An animal's state (position, speed, what it's doing), for the probe.</summary>
    public CreatureState State(int index) => creatures[index].state;

    /// <summary>An animal's id, the same for as long as it's out (Footprints follows each one by it).</summary>
    public int Id(int index) => creatures[index].id;

    /// <summary>Takes an animal under outside control: it keeps this heading and speed and ignores everything (the probe).</summary>
    public void Puppet(int index, float yaw, float speed)
    {
        var c = creatures[index];
        c.state.Mode = CreatureMode.Puppet;
        c.state.Yaw = yaw;
        c.state.Speed = speed;
    }

    void Start()
    {
        grid = FindFirstObjectByType<ChemGrid>();
        sky = FindFirstObjectByType<SkyLight>();
        BuildBodies();
        counts = new int[species.Count];
        capsule = CreatureCapsule.Build(12, 6, 4);
        props = new MaterialPropertyBlock();
    }

    // Each species' body, with what it hunts and whether anything hunts it (a listed species' variants count).
    void BuildBodies()
    {
        var hunted = new bool[species.Count];
        var prey = new List<int>[species.Count];
        for (int i = 0; i < species.Count; i++)
        {
            prey[i] = new List<int>();
            var hunter = species[i].species;
            if (hunter == null) continue;
            for (int j = 0; j < species.Count; j++)
            {
                var quarry = species[j].species;
                if (quarry == null || j == i || !hunter.Hunts.Exists(h => h != null && h.Body == quarry.Body)) continue;
                prey[i].Add(j);
                hunted[j] = true;
            }
        }
        for (int i = 0; i < species.Count; i++)
            bodies.Add(species[i].species != null ? CreatureBody.Build(species[i].species, prey[i], hunted[i]) : null);
    }

    void OnDestroy()
    {
        buffer?.Release();
        if (capsule != null) Destroy(capsule);
    }

    void Update()
    {
        world = grid != null ? grid.World : null;
        var cam = Camera.main;
        if (world == null || cam == null || material == null || bodies.Count == 0) return;
        using var _ = UpdateMarker.Auto();
        var centre = new float2(cam.transform.position.x, cam.transform.position.z);
        home ??= centre;
        if (Time.time >= nextLook) LookAround(centre);

        Despawn(centre);
        spawnBudget = math.min(spawnBudget + spawnRate * Time.deltaTime, 3f);
        while (spawnBudget >= 1f)
        {
            spawnBudget -= 1f;
            TrySpawn(centre);
        }

        float dt = math.min(Time.deltaTime, 0.1f);
        float night = sky != null ? 1f - sky.Daylight : 0f;
        // Everyone as they were at the start of the frame, for seeing each other.
        herd.Clear();
        foreach (var c in creatures) herd.Add(c.id, c.species, c.state, bodies[c.species]);
        foreach (var c in creatures)
        {
            var (threat, isFire) = Nearest(c.state.Position.xz);
            brain.Step(ref c.state, c.id, c.species, bodies[c.species], herd, this, threat, isFire, night, dt);
        }
        Draw(cam);
    }

    // --- the ground (ICreatureGround) -----------------------------------------

    public float Height(float2 xz) => grid.GroundAt(new Vector3(xz.x, 0f, xz.y));

    /// <summary>Water to drink from: a wet, unfrozen cell.</summary>
    public bool Water(float2 xz)
    {
        var cell = grid.CellAt(new Vector3(xz.x, 0f, xz.y));
        var c = world.At(cell.x, cell.y);
        return c != null && c.Water > 0 && !c.Frozen;
    }

    /// <summary>Dry, clear of trees and walls, and not burning.</summary>
    public bool Walkable(float2 xz)
    {
        var cell = grid.CellAt(new Vector3(xz.x, 0f, xz.y));
        var c = world.At(cell.x, cell.y);
        if (c == null || c.Water > 0 || c.Fire > 0) return false;
        return c.T != TerrainIds.Tree && c.T != TerrainIds.LogWall && c.T != TerrainIds.Stone && c.T != TerrainIds.Rock && c.T != TerrainIds.Metal;
    }

    bool NearWater(float2 xz)
    {
        var cell = grid.CellAt(new Vector3(xz.x, 0f, xz.y));
        for (int dy = -4; dy <= 4; dy++)
            for (int dx = -4; dx <= 4; dx++)
            {
                var c = world.At(cell.x + dx, cell.y + dy);
                if (c != null && c.Water > 0) return true;
            }
        return false;
    }

    // --- what animals are wary of ----------------------------------------------

    // Twice a second: where the wizards are, and the fires near the camera.
    void LookAround(float2 centre)
    {
        nextLook = Time.time + 0.5f;
        wizards.Clear();
        foreach (var w in FindObjectsByType<SpellCaster>(FindObjectsSortMode.None))
            wizards.Add(new Vector2(w.transform.position.x, w.transform.position.z));
        fires.Clear();
        var mid = grid.CellAt(new Vector3(centre.x, 0f, centre.y));
        int reach = Mathf.CeilToInt(despawnDistance / grid.cellSize);
        for (int y = mid.y - reach; y <= mid.y + reach; y += 2)
            for (int x = mid.x - reach; x <= mid.x + reach; x += 2)
            {
                var c = world.At(x, y);
                if (c != null && c.Fire > 0)
                {
                    var p = grid.CellCenter(x, y);
                    fires.Add(new Vector2(p.x, p.z));
                }
            }
    }

    (float2 at, bool fire) Nearest(float2 from)
    {
        float2 best = new(1e9f, 1e9f);
        float bestSq = float.MaxValue;
        bool fire = false;
        foreach (var w in wizards) Consider(w, false);
        foreach (var f in fires) Consider(f, true);
        return (best, fire);

        void Consider(Vector2 p, bool isFire)
        {
            float d = math.distancesq(from, (float2)p);
            if (d >= bestSq) return;
            bestSq = d;
            best = p;
            fire = isFire;
        }
    }

    // --- population --------------------------------------------------------------

    void Despawn(float2 centre)
    {
        System.Array.Clear(counts, 0, counts.Length);
        for (int i = creatures.Count - 1; i >= 0; i--)
        {
            float d = math.distance(creatures[i].state.Position.xz, centre);
            if (d > despawnDistance) creatures.RemoveAt(i);
            else if (d <= radius) counts[creatures[i].species]++;
        }
    }

    // How full of animals the land is at a point, by its distance from where the wizard started.
    float Wildness(float2 at) =>
        math.lerp(homeDensity, wildDensity, math.smoothstep(homeRadius, wildRadius, math.distance(at, home ?? at)));

    // A random spot in the ring round the camera: whichever species is furthest below its density gets a group there.
    void TrySpawn(float2 centre)
    {
        if (creatures.Count >= maxCreatures) return;
        float angle = rng.NextFloat(math.PI * 2f);
        float distance = math.sqrt(rng.NextFloat(minSpawnDistance * minSpawnDistance, radius * radius));
        var at = centre + new float2(math.cos(angle), math.sin(angle)) * distance;
        if (!Walkable(at)) return;
        float hectares = math.PI * radius * radius / 10000f * Wildness(at);
        bool wet = NearWater(at);
        int best = -1;
        float bestShortfall = 0f;
        for (int i = 0; i < species.Count; i++)
        {
            if (bodies[i] == null || species[i].nearWater && !wet) continue;
            float shortfall = species[i].density * hectares - counts[i];
            if (shortfall > bestShortfall)
            {
                bestShortfall = shortfall;
                best = i;
            }
        }
        // Rare species (less than one expected nearby) turn up only some of the time.
        if (best < 0 || bestShortfall < 1f && rng.NextFloat() > bestShortfall) return;
        var group = species[best].species.Body.GroupSize;
        int n = rng.NextInt(math.max(1, group.x), math.max(group.x, group.y) + 1);
        for (int i = 0; i < n; i++)
        {
            var p = at + rng.NextFloat2Direction() * rng.NextFloat(0f, 2f + n * 1.5f);
            if (Walkable(p)) Spawn(best, new float3(p.x, Height(p), p.y));
        }
        counts[best] += n;
    }

    /// <summary>Puts one animal of a species (by index in the list) at a point. Also for the probe.</summary>
    public int Spawn(int speciesIndex, float3 position, float yaw = float.NaN)
    {
        var b = bodies[speciesIndex];
        creatures.Add(new Creature
        {
            id = ++nextId,
            species = speciesIndex,
            coat = b.PickCoat(rng.NextFloat()),
            state = new CreatureState
            {
                Position = position,
                Yaw = float.IsNaN(yaw) ? rng.NextFloat(-math.PI, math.PI) : yaw,
                Phase = rng.NextFloat(),
                Scale = rng.NextFloat(b.Size.x, math.max(b.Size.x, b.Size.y)),
                Mode = CreatureMode.Idle,
                Timer = rng.NextFloat(0.5f, 4f),
                Seed = rng.NextUInt() | 1u,
            },
        });
        return creatures.Count - 1;
    }

    // --- drawing -------------------------------------------------------------------

    // Poses every animal into one list of segments and draws them all as instances of a unit capsule.
    void Draw(Camera cam)
    {
        int total = 0;
        foreach (var c in creatures) total += bodies[c.species].Segments.Length;
        if (total == 0) return;
        if (instances.Length < total) instances = new SegmentInstance[Mathf.NextPowerOfTwo(total)];
        int offset = 0;
        foreach (var c in creatures)
        {
            var b = bodies[c.species];
            animator.Evaluate(b, c.state, c.coat, this, instances, offset);
            offset += b.Segments.Length;
        }
        if (buffer == null || buffer.count < total)
        {
            buffer?.Release();
            buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, instances.Length, Marshal.SizeOf<SegmentInstance>());
        }
        buffer.SetData(instances, 0, 0, total);
        props.SetBuffer(SegmentsId, buffer);
        var rp = new RenderParams(material)
        {
            worldBounds = new Bounds(cam.transform.position, Vector3.one * 2000f),
            matProps = props,
            shadowCastingMode = ShadowCastingMode.On,
            receiveShadows = true,
        };
        Graphics.RenderMeshPrimitives(rp, capsule, 0, total);
    }
}
