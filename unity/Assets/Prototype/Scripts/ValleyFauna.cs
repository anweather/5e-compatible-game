using System.Collections.Generic;
using Chem;
using Punchy.Fauna;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;

// The valley's animals: deer, rabbits, foxes, turkeys, frogs by the water and
// the odd bear, kept around the camera. Built from Punchy's creatures
// (Zachary Franklin, ThirdParty/Punchy): each species is parts on a body,
// posed procedurally with feet placed on the ground, all drawn in one
// instanced call. Each lives its own day (Punchy's activities): grazing,
// walking to the water to drink, lying down to rest, sleeping at night (or by
// day, for night animals), playing chase with its own kind, and hunting what
// it hunts (wolves and foxes); prey runs from a hunter, and everything bolts
// from a wizard who comes too close, and from fire. A wizard in an animal's
// shape (Shapeshift) is one of the animals to them: its own kind and the
// rest let it be, and what that animal hunts runs from it. They keep to dry,
// clear, unburnt cells.
// Presentation only, like the fireflies: each machine has its own animals,
// and nothing they do reaches the chemistry.
public partial class ValleyFauna : MonoBehaviour, ICreatureGround
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
        // One of a village's folk (ValleyFauna.Folk): its village (index in folk; -1 for an animal), what
        // it's doing, where it's going and how long it stays; the barkeep keeps to the bar.
        public int village = -1;
        public FolkTask task;
        public float2 goal;
        public float wait;
        public bool barkeep;
    }

    static readonly ProfilerMarker UpdateMarker = new("ValleyFauna.Update");

    readonly List<Creature> creatures = new();
    readonly List<CreatureBody> bodies = new();
    readonly List<Vector2> fires = new();
    // The wizards, and the species each has the shape of (-1: itself).
    readonly List<(Vector2 at, int form)> wizards = new();
    readonly CreatureAnimator animator = new();
    readonly CreatureBrain brain = new();
    readonly Herd herd = new();
    SkyLight sky;
    int[] counts;
    SegmentInstance[] instances = new SegmentInstance[256];
    CreatureDrawer drawer;
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

    /// <summary>An animal's species, as its index in the list.</summary>
    public int SpeciesOf(int index) => creatures[index].species;

    /// <summary>An animal's colouring (one of its species' coats).</summary>
    public int Coat(int index) => creatures[index].coat;

    /// <summary>A species' body (by index in the list), or null; built in Start.</summary>
    public CreatureBody Body(int speciesIndex) =>
        speciesIndex >= 0 && speciesIndex < bodies.Count ? bodies[speciesIndex] : null;

    /// <summary>A species' name as its asset has it ("RedFox"), or null.</summary>
    public string SpeciesName(int speciesIndex) =>
        speciesIndex >= 0 && speciesIndex < species.Count && species[speciesIndex].species != null ? species[speciesIndex].species.name : null;

    /// <summary>
    /// The animal <paramref name="ray"/> points at, the nearest along it within
    /// <paramref name="maxDistance"/>, or -1. Animals have no colliders, so it is a near miss of
    /// the ray past each body's middle, by about the body's size.
    /// </summary>
    public int Aimed(Ray ray, float maxDistance)
    {
        int best = -1;
        float bestAlong = maxDistance;
        for (int i = 0; i < creatures.Count; i++)
        {
            var b = bodies[creatures[i].species];
            var s = creatures[i].state;
            var middle = new Vector3(s.Position.x, s.Position.y + b.StandHeight * s.Scale, s.Position.z);
            float along = Vector3.Dot(middle - ray.origin, ray.direction);
            if (along <= 0f || along >= bestAlong) continue;
            float reach = math.max(0.45f, b.BodyLength * s.Scale * 0.6f);
            if ((ray.GetPoint(along) - middle).sqrMagnitude > reach * reach) continue;
            best = i;
            bestAlong = along;
        }
        return best;
    }

    void Start()
    {
        grid = FindAnyObjectByType<ChemGrid>();
        sky = FindAnyObjectByType<SkyLight>();
        FindVillages();
        BuildBodies();
        counts = new int[species.Count];
        drawer = new CreatureDrawer(material);
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

    void OnDestroy() => drawer?.Dispose();

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
        float night = sky != null ? 1f - sky.Daylight : 0f;
        BringOutFolk(centre, FolkGoIn(night));
        spawnBudget = math.min(spawnBudget + spawnRate * Time.deltaTime, 3f);
        while (spawnBudget >= 1f)
        {
            spawnBudget -= 1f;
            TrySpawn(centre);
        }

        float dt = math.min(Time.deltaTime, 0.1f);
        RunFolk(dt, night);
        // Everyone as they were at the start of the frame, for seeing each other.
        herd.Clear();
        foreach (var c in creatures) herd.Add(c.id, c.species, c.state, bodies[c.species]);
        using (BrainMarker.Auto())
        {
            // Animals far off think every other frame (half of them each frame) over twice the time:
            // nobody sees the difference out there, and it halves most of the herd's thinking.
            bool odd = (Time.frameCount & 1) == 1;
            foreach (var c in creatures)
            {
                float step = dt;
                if (math.distancesq(c.state.Position.xz, centre) > FarThinkingSq)
                {
                    if (((c.id & 1) == 1) != odd) continue;
                    step = math.min(2f * dt, 0.1f);
                }
                var (threat, isFire) = Nearest(c.state.Position.xz, c.species);
                brain.Step(ref c.state, c.id, c.species, bodies[c.species], herd, this, threat, isFire, night, step);
            }
        }
        using (DrawMarker.Auto()) Draw(cam);
    }

    // Beyond this (40 m, squared) from the camera an animal thinks every other frame.
    const float FarThinkingSq = 40f * 40f;
    static readonly ProfilerMarker BrainMarker = new("ValleyFauna.Think"), DrawMarker = new("ValleyFauna.Draw");

    // --- the ground (ICreatureGround) -----------------------------------------

    public float Height(float2 xz) => grid.GroundAt(new Vector3(xz.x, 0f, xz.y));

    /// <summary>Water to drink from: a wet, unfrozen cell.</summary>
    public bool Water(float2 xz)
    {
        var cell = grid.CellAt(new Vector3(xz.x, 0f, xz.y));
        var c = world.At(cell.x, cell.y);
        return c != null && c.Water > 0 && !c.Frozen;
    }

    /// <summary>Dry, clear of trees, walls and thorns, and not burning.</summary>
    public bool Walkable(float2 xz)
    {
        var cell = grid.CellAt(new Vector3(xz.x, 0f, xz.y));
        var c = world.At(cell.x, cell.y);
        if (c == null || c.Water > 0 || c.Fire > 0 || world.IsSpiky(c) || OnWildCoast(xz)) return false;
        return c.T != TerrainIds.Tree && c.T != TerrainIds.LogWall && c.T != TerrainIds.Pane && c.T != TerrainIds.Stone && c.T != TerrainIds.Rock && c.T != TerrainIds.Metal;
    }

    // The beach and the coast (the sea and the strip inland of its shore, Ocean.Covers) are no
    // place for the valley's wild animals (Andrew's playtest #8: deer on the beach); but a village
    // there is its folk's, out to where they wander (as FolkPlaces reaches).
    bool OnWildCoast(float2 xz)
    {
        var ocean = Ocean.Instance;
        if (ocean == null || !ocean.Covers(new Vector2(xz.x, xz.y))) return false;
        foreach (var (v, _, well) in folk)
            if (math.distance(well, xz) <= v.wander + 12f) return false;
        return true;
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

    // Twice a second: where the wizards are (and what shape they're in), and the fires near the camera.
    void LookAround(float2 centre)
    {
        nextLook = Time.time + 0.5f;
        wizards.Clear();
        foreach (var w in FindObjectsByType<SpellCaster>(FindObjectsSortMode.None))
        {
            int form = w.TryGetComponent<Shapeshift>(out var shape) && shape.Shifted ? shape.Species : -1;
            wizards.Add((new Vector2(w.transform.position.x, w.transform.position.z), form));
        }
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

    // The nearest danger to an animal of a species: a fire, a wizard, or a wizard in the shape of
    // something that hunts it (a wizard in any other animal's shape is no danger).
    (float2 at, bool fire) Nearest(float2 from, int speciesIndex)
    {
        float2 best = new(1e9f, 1e9f);
        float bestSq = float.MaxValue;
        bool fire = false;
        foreach (var (at, form) in wizards)
            if (form < 0 || Hunts(form, speciesIndex)) Consider(at, false);
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

    /// <summary>Whether the species <paramref name="hunter"/> hunts the species <paramref name="quarry"/> (indices in the list).</summary>
    public bool Hunts(int hunter, int quarry)
    {
        var b = Body(hunter);
        return b != null && System.Array.IndexOf(b.Prey, quarry) >= 0;
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

    // --- the villages' folk --------------------------------------------------------------

    // Each village's residents (Village.residents): their species in the list (density 0, so the
    // wilds never spawn them), where their square is, how far they wander.
    readonly List<(Village village, int species, float2 home)> folk = new();

    void FindVillages()
    {
        foreach (var v in FindObjectsByType<Village>(FindObjectsSortMode.None))
        {
            if (v.residents == null || v.residentCount <= 0) continue;
            int i = species.FindIndex(e => e.species == v.residents);
            if (i < 0)
            {
                species.Add(new Entry { species = v.residents, density = 0f });
                i = species.Count - 1;
            }
            var p = v.transform.position + v.wellAt;
            folk.Add((v, i, new float2(p.x, p.z)));
            places.Add(PlacesOf(v));
        }
    }

    // A village's folk come out (at home, leashed to it) when someone's near and none are about,
    // out of their doors (ValleyFauna.Folk), but not at night or in heavy rain; like the animals,
    // they go when everyone has gone far off.
    void BringOutFolk(float2 centre, bool goIn)
    {
        for (int v = 0; v < folk.Count; v++)
        {
            var (village, kind, square) = folk[v];
            if (village == null || goIn || places[v].indoors || math.distance(square, centre) > radius) continue;
            if (!FolkAbout(kind, square)) ComeOut(v);
        }
    }

    bool FolkAbout(int kind, float2 square)
    {
        foreach (var c in creatures)
            if (c.species == kind && c.state.Leash > 0f && math.distance(c.state.Home, square) < 1f) return true;
        return false;
    }

    // A village's folk out of its doors (or round its square if it has none), each with a plan for the day.
    void ComeOut(int v)
    {
        var (village, kind, square) = folk[v];
        var doors = places[v].doors;
        for (int n = 0; n < village.residentCount; n++)
        {
            var p = doors.Count > 0
                ? doors[rng.NextInt(doors.Count)] + rng.NextFloat2Direction() * 0.5f
                : square + rng.NextFloat2Direction() * rng.NextFloat(2f, village.wander * 0.6f);
            if (!Walkable(p)) continue;
            int at = Spawn(kind, new float3(p.x, Height(p), p.y));
            var c = creatures[at];
            var s = c.state;
            s.Home = square;
            s.Leash = village.wander;
            s.Mode = CreatureMode.Puppet;
            c.state = s;
            c.village = v;
            c.task = FolkTask.Pause;
            c.wait = rng.NextFloat(0f, 3f);
            c.barkeep = n == 0 && places[v].hasBar;
        }
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
        drawer.Draw(instances, total, cam.transform.position);
    }
}
