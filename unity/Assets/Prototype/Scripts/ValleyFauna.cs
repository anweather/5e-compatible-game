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
// from a wizard who comes too close, and from fire, milling about if it's
// cornered. A wizard in an animal's
// shape (Shapeshift) is one of the animals to them: its own kind and the
// rest let it be, and what that animal hunts runs from it. They keep to dry,
// clear, unburnt cells.
// Presentation only, like the fireflies: each machine has its own animals,
// and nothing they do reaches the chemistry.
public partial class ValleyFauna : FaunaFace, ICreatureGround
{
    public Material material;
    public float radius = 90f;
    public float minSpawnDistance = 35f;
    public float despawnDistance = 130f;
    public int maxCreatures = 120;
    public float spawnRate = 6f;
    public float homeDensity = 0.3f, wildDensity = 1.4f;
    public float homeRadius = 60f, wildRadius = 300f;

    sealed class Creature
    {
        public int id;
        public int species;
        public int coat;
        public CreatureState state;
        public CreatureTraits traits;
        public float size;
        public int village = -1;
        public FolkTask task;
        public float2 goal;
        public float wait;
        public CreatureTask job;
        public float2 post;
        public float postYaw;
        public int summon = -1;
    }

    static readonly ProfilerMarker UpdateMarker = new("ValleyFauna.Update"), ReadRowsMarker = new("ValleyFauna.ReadRows");

    readonly List<Creature> creatures = new();
    readonly CreatureBodies bodies = new();
    readonly List<Vector2> fires = new();
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

    public override int Count => creatures.Count;

    public int AtLens { get; private set; }

    public int SpeciesCount => bodies.Count;

    public int Rebuilt => bodies.Rebuilt;
    public double ReadRowsMs { get; private set; }

    public int SpeciesIndex(string id) => bodies.IndexOf(id);

    public override CreatureState State(int index) => creatures[index].state;

    public override int Id(int index) => creatures[index].id;

    public int IndexOf(int id) => creatures.FindIndex(c => c.id == id);

    public void Puppet(int index, float yaw, float speed)
    {
        var c = creatures[index];
        c.state.Mode = CreatureMode.Puppet;
        c.state.Yaw = yaw;
        c.state.Speed = speed;
    }

    public void Leash(int index, float2 home, float leash)
    {
        var c = creatures[index];
        c.state.Home = home;
        c.state.Leash = leash;
    }

    public override int SpeciesOf(int index) => creatures[index].species;

    public float RowSize(int index) => creatures[index].size;

    public override int Coat(int index) => creatures[index].coat;

    public CreatureTraits Traits(int index) => creatures[index].traits;

    public override CreatureBody Body(int speciesIndex) => bodies[speciesIndex];

    public override string SpeciesName(int speciesIndex) => bodies[speciesIndex]?.Name;

    public override int Aimed(Ray ray, float maxDistance)
    {
        int best = -1;
        float bestAlong = maxDistance;
        for (int i = 0; i < creatures.Count; i++)
        {
            var b = bodies[creatures[i].species];
            if (b.Habitat != Habitat.Land) continue;
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

    static readonly Registry<ValleyFauna> registry = new();

    public static new ValleyFauna InPlay => registry.First;

    public override Material CreatureMaterial => material;

    void Awake()
    {
        registry.Join(this);
        JoinFauna();
    }

    void Start()
    {
        if (MapFile.Load(ChemGrid.Folders.World).IsOff("fauna"))
        {
            enabled = false;
            return;
        }
        grid = ChemGrid.Current?.Grid;
        sky = SkyLight.InPlay;
        drawer = new CreatureDrawer(material);
        if (grid != null) grid.RulesChanged += ReadRows;
        ReadRows();
    }

    void ReadRows()
    {
        if (grid == null || grid.CurrentRules == null) return;
        bool first = bodies.Count == 0;
        long began = System.Diagnostics.Stopwatch.GetTimestamp();
        using (ReadRowsMarker.Auto()) bodies.Refresh(grid.CurrentRules.Effective);
        ReadRowsMs = (System.Diagnostics.Stopwatch.GetTimestamp() - began) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        if (bodies.Unchanged) return;
        counts = new int[bodies.Count];
        creatures.RemoveAll(c => bodies[c.species] == null);
        foreach (var c in creatures)
        {
            c.traits = bodies[c.species].TraitsOf(c.coat, c.state.Individual);
            c.size = RowScale(bodies[c.species], c.state.Individual);
        }
        if (first) FindVillages();
    }

    void OnDestroy()
    {
        registry.Leave(this);
        LeaveFauna();
        if (grid != null) grid.RulesChanged -= ReadRows;
        drawer?.Dispose();
    }

    void Update()
    {
        world = grid != null ? grid.World : null;
        if (bodies.Count == 0) ReadRows();
        var cam = Camera.main;
        if (world == null || cam == null || material == null || bodies.Count == 0) return;
        using var _ = UpdateMarker.Auto();
        var centre = new float2(cam.transform.position.x, cam.transform.position.z);
        home ??= centre;
        if (Time.time >= nextLook) LookAround(centre);

        Despawn(centre);
        BringOutSummons();
        float night = sky != null ? 1f - sky.Daylight : 0f;
        BringOutFolk(centre, FolkGoIn(night));
        spawnBudget = math.min(spawnBudget + spawnRate * Time.deltaTime, 3f);
        while (spawnBudget >= 1f)
        {
            spawnBudget -= 1f;
            TrySpawn(centre);
        }

        float dt = math.min(Time.deltaTime, 0.1f);
        Grow(dt);
        RunFolk(dt, night);
        RunJobs(centre, dt, FolkGoIn(night));
        herd.Clear();
        foreach (var c in creatures) herd.Add(c.id, c.species, c.state, bodies[c.species]);
        using (BrainMarker.Auto())
        {
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

    void Grow(float dt)
    {
        float k = 1f - math.exp(-GrowRate * dt);
        foreach (var c in creatures)
        {
            float gap = c.size - c.state.Scale;
            c.state.Scale = math.abs(gap) < 0.002f ? c.size : c.state.Scale + gap * k;
        }
    }

    const float GrowRate = 3f;

    static float RowScale(CreatureBody b, uint individual) =>
        math.lerp(b.Size.x, math.max(b.Size.x, b.Size.y), CreatureBody.Unit(individual, 14));

    const float FarThinkingSq = 40f * 40f;
    static readonly ProfilerMarker BrainMarker = new("ValleyFauna.Think"), DrawMarker = new("ValleyFauna.Draw"),
        PoseMarker = new("ValleyFauna.Pose"), SendMarker = new("ValleyFauna.Send");

    public float Height(float2 xz) => grid.GroundAt(new Vector3(xz.x, 0f, xz.y));

    public bool Water(float2 xz)
    {
        var cell = grid.CellAt(new Vector3(xz.x, 0f, xz.y));
        var c = world.At(cell.x, cell.y);
        return c != null && c.Water > 0 && !c.Frozen;
    }

    public bool Walkable(float2 xz)
    {
        var cell = grid.CellAt(new Vector3(xz.x, 0f, xz.y));
        var c = world.At(cell.x, cell.y);
        if (c == null || c.Water > 0 || c.Fire > 0 || world.IsSpiky(c) || OnWildCoast(xz)) return false;
        return c.T != TerrainIds.Tree && c.T != TerrainIds.LogWall && c.T != TerrainIds.Pane && c.T != TerrainIds.Stone && c.T != TerrainIds.Rock && c.T != TerrainIds.Metal;
    }

    bool OnWildCoast(float2 xz)
    {
        var ocean = Ocean.Instance;
        if (ocean == null || !ocean.Covers(new Vector2(xz.x, xz.y))) return false;
        foreach (var (v, _, well) in folk)
            if (math.distance(well, xz) <= v.wander + 12f) return false;
        return true;
    }

    public float WaterDepth(float2 xz)
    {
        var at = new Vector3(xz.x, 0f, xz.y);
        var cell = grid.CellAt(at);
        var c = world.At(cell.x, cell.y);
        return c == null || c.Water <= 0 || c.Frozen ? -1f : grid.WaterSurfaceY(cell.x, cell.y) - grid.GroundAt(at);
    }

    bool Fits(CreatureBody b, float2 xz) => b.Habitat == Habitat.Land ? Walkable(xz) : CreatureBrain.Suits(b.Habitat, WaterDepth(xz));

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

    void LookAround(float2 centre)
    {
        nextLook = Time.time + 0.5f;
        wizards.Clear();
        foreach (var w in SpellCaster.All)
        {
            int form = w.TryGetComponent<Shapeshift>(out var shape) && shape.Shifted ? shape.Species : -1;
            wizards.Add((new Vector2(w.transform.position.x, w.transform.position.z), form));
        }
        fires.Clear();
        var mid = grid.CellAt(new Vector3(centre.x, 0f, centre.y));
        int reach = Mathf.CeilToInt(despawnDistance / grid.cellSize);
        bool skipPacked = world.Source is IGeneratedSource;
        for (int y = mid.y - reach; y <= mid.y + reach; y += 2)
            for (int x = mid.x - reach; x <= mid.x + reach; x += 2)
            {
                if (skipPacked && world.InBounds(x, y) && world.IsPacked(x >> Chunks.Shift, y >> Chunks.Shift)) continue;
                var c = world.At(x, y);
                if (c != null && c.Fire > 0)
                {
                    var p = grid.CellCenter(x, y);
                    fires.Add(new Vector2(p.x, p.z));
                }
            }
    }

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

    public bool Hunts(int hunter, int quarry)
    {
        var b = Body(hunter);
        return b != null && System.Array.IndexOf(b.Prey, quarry) >= 0;
    }

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

    float Wildness(float2 at) =>
        math.lerp(homeDensity, wildDensity, math.smoothstep(homeRadius, wildRadius, math.distance(at, home ?? at)));

    void TrySpawn(float2 centre)
    {
        if (creatures.Count >= maxCreatures) return;
        float angle = rng.NextFloat(math.PI * 2f);
        float distance = math.sqrt(rng.NextFloat(minSpawnDistance * minSpawnDistance, radius * radius));
        var at = centre + new float2(math.cos(angle), math.sin(angle)) * distance;
        float hectares = math.PI * radius * radius / 10000f * Wildness(at);
        bool wet = NearWater(at);
        int best = -1;
        float bestShortfall = 0f;
        for (int i = 0; i < bodies.Count; i++)
        {
            var b = bodies[i];
            if (b == null || b.NearWater && !wet || !Fits(b, at)) continue;
            float shortfall = b.Density * hectares - counts[i];
            if (shortfall > bestShortfall)
            {
                bestShortfall = shortfall;
                best = i;
            }
        }
        if (best < 0 || bestShortfall < 1f && rng.NextFloat() > bestShortfall) return;
        var group = bodies[best].Group;
        int n = rng.NextInt(math.max(1, group.x), math.max(group.x, group.y) + 1);
        for (int i = 0; i < n; i++)
        {
            var p = at + rng.NextFloat2Direction() * rng.NextFloat(0f, 2f + n * 1.5f);
            if (Fits(bodies[best], p)) Spawn(best, new float3(p.x, Height(p), p.y));
        }
        counts[best] += n;
    }

    readonly List<(Village village, int species, float2 home)> folk = new();

    void FindVillages()
    {
        foreach (var v in Village.All)
        {
            int i = bodies.IndexOf(v.residents);
            if (i < 0 || v.residentCount <= 0) continue;
            var p = v.transform.position + v.wellAt;
            folk.Add((v, i, new float2(p.x, p.z)));
            places.Add(PlacesOf(v));
        }
    }

    void BringOutFolk(float2 centre, bool goIn)
    {
        for (int v = 0; v < folk.Count; v++)
        {
            var (village, kind, square) = folk[v];
            if (village == null || bodies[kind] == null || goIn || places[v].indoors || math.distance(square, centre) > radius) continue;
            if (!FolkAbout(v)) ComeOut(v);
        }
    }

    bool FolkAbout(int v)
    {
        foreach (var c in creatures)
            if (c.village == v) return true;
        return false;
    }

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
            var (job, post, yaw) = PostOf(n, places[v]);
            uint individual = math.hash(new int2(village.seed, n)) | 1u;
            int who = job != CreatureTask.None ? kind : bodies.FolkAt(kind, CreatureBody.Unit(individual, 15));
            int at = Spawn(who, new float3(p.x, Height(p), p.y), float.NaN, individual);
            var c = creatures[at];
            var s = c.state;
            s.Home = square;
            s.Leash = village.wander;
            s.Mode = CreatureMode.Puppet;
            c.state = s;
            c.village = v;
            c.task = FolkTask.Pause;
            c.wait = rng.NextFloat(0f, 3f);
            c.job = job;
            c.post = post;
            c.postYaw = yaw;
        }
    }

    public int Spawn(int speciesIndex, float3 position, float yaw = float.NaN, uint individual = 0u)
    {
        var b = bodies[speciesIndex];
        if (individual == 0u) individual = rng.NextUInt() | 1u;
        var added = new Creature
        {
            id = ++nextId,
            species = speciesIndex,
            coat = b.PickCoat(CreatureBody.Unit(individual, 0)),
            state = new CreatureState
            {
                Position = position,
                Yaw = float.IsNaN(yaw) ? rng.NextFloat(-math.PI, math.PI) : yaw,
                Phase = rng.NextFloat(),
                Scale = RowScale(b, individual),
                Mode = CreatureMode.Idle,
                Timer = rng.NextFloat(0.5f, 4f),
                Seed = rng.NextUInt() | 1u,
                Individual = individual,
            },
        };
        added.traits = b.TraitsOf(added.coat, individual);
        added.size = added.state.Scale;
        creatures.Add(added);
        return creatures.Count - 1;
    }

    void Draw(Camera cam)
    {
        int total = 0;
        foreach (var c in creatures) total += bodies[c.species].Segments.Length;
        if (total == 0) return;
        if (instances.Length < total) instances = new SegmentInstance[Mathf.NextPowerOfTwo(total)];
        int offset = 0, posed = 0;
        AtLens = 0;
        using (PoseMarker.Auto())
        {
            view.From(cam, sky);
            foreach (var c in creatures)
            {
                var b = bodies[c.species];
                if (!view.Shows(c.state.Position, b, c.state.Scale)) continue;
                posed++;
                int shown = animator.Evaluate(b, c.state, in c.traits, this, instances, offset);
                if (CreatureDrawer.HideAtLens(instances, offset, shown, cam)) AtLens++;
                offset += shown;
            }
        }
        Posed = posed;
        PerfCounts.Add("fauna.creatures", creatures.Count);
        PerfCounts.Add("fauna.posed", posed);
        PerfCounts.Add("fauna.segments", offset);
        using (SendMarker.Auto()) drawer.Draw(instances, offset, cam.transform.position);
    }

    readonly CreatureView view = new();

    public int Posed { get; private set; }
}
