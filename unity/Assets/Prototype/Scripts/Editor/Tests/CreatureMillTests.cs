using NUnit.Framework;
using Punchy.Fauna;
using Unity.Mathematics;
using UnityEditor;

// Animals with nowhere left to run (CreatureBrain's Flee): cornered against what they can't walk
// on, or at the end of a leash, they mill about; they don't stand frozen, jitter on the spot, or
// run through a leash. MillWatch tells which, as in the fauna probe.
public class CreatureMillTests
{
    const float Dt = 1f / 60f;
    const float Settle = 3f, Watched = 15f;

    /// <summary>Flat land, walkable where <see cref="open"/> says.</summary>
    sealed class Land : ICreatureGround
    {
        readonly System.Func<float2, bool> open;
        public Land(System.Func<float2, bool> open) => this.open = open;
        public float Height(float2 xz) => 0f;
        public bool Walkable(float2 xz) => open(xz);
        public bool Water(float2 xz) => false;
    }

    static CreatureBody Body(string species) =>
        CreatureBody.Build(AssetDatabase.LoadAssetAtPath<CreatureSpecies>($"Assets/Prototype/ThirdParty/Punchy/Fauna/{species}.asset"));

    static CreatureBody Deer() => Body("Deer");

    static CreatureState At(float x, float z, uint seed) => new CreatureState
    {
        Position = new float3(x, 0f, z),
        Scale = 1f,
        Mode = CreatureMode.Idle,
        Timer = 1f,
        Seed = seed | 1u,
    };

    // Steps an animal with a threat stood still at a place; the herd is only itself.
    static MillWatch Run(ref CreatureState s, CreatureBody body, ICreatureGround land, float2 threat, bool fire = false, System.Action<CreatureState> eachFrame = null)
    {
        var brain = new CreatureBrain();
        var herd = new Herd();
        var watch = new MillWatch();
        int frames = (int)((Settle + Watched) / Dt);
        for (int f = 0; f < frames; f++)
        {
            herd.Clear();
            herd.Add(1, 0, s, body);
            brain.Step(ref s, 1, 0, body, herd, land, threat, fire, 0f, Dt);
            if (f * Dt >= Settle) watch.Sample(s, Dt);
            eachFrame?.Invoke(s);
        }
        return watch;
    }

    // A pen 8 m wide and 10 deep with no way out, the danger at its south end.
    static bool Pen(float2 p) => math.abs(p.x) < 4f && math.abs(p.y) < 5f;

    [TestCase("Deer", 1u)]
    [TestCase("Deer", 7u)]
    [TestCase("Deer", 1234u)]
    [TestCase("Turkey", 7u)]
    [TestCase("BlackBear", 7u)]
    [TestCase("Human", 7u)]
    public void CorneredItMillsAbout(string species, uint seed)
    {
        var body = Body(species);
        var s = At(0f, 2f, seed);
        var land = new Land(Pen);
        var watch = Run(ref s, body, land, new float2(0f, -3f), true, state => Assert.That(land.Walkable(state.Position.xz), Is.True, "walked out of the land"));
        Assert.That(watch.Mills, Is.True, watch.Summary);
        Assert.That(s.Mode, Is.EqualTo(CreatureMode.Flee), "still afraid");
    }

    [TestCase(1u)]
    [TestCase(7u)]
    [TestCase(1234u)]
    public void AtTheEndOfALeashItMillsAboutInReach(uint seed)
    {
        const float leash = 7f;
        var body = Deer();
        var s = At(2f, 0f, seed);
        s.Home = float2.zero;
        s.Leash = leash;
        var furthest = 0f;
        var watch = Run(ref s, body, new Land(_ => true), float2.zero, false, state => furthest = math.max(furthest, math.length(state.Position.xz)));
        Assert.That(furthest, Is.LessThan(leash + 2f), "ran through its leash");
        Assert.That(watch.Mills, Is.True, watch.Summary);
    }

    // Only a true corner is one: an animal running at a long wall goes along it and gets away.
    [TestCase(1u)]
    [TestCase(7u)]
    [TestCase(1234u)]
    public void AWallWithAWayRoundIsNoCorner(uint seed)
    {
        var body = Deer();
        var s = At(0f, 0f, seed);
        Run(ref s, body, new Land(p => p.y < 6f), new float2(0f, -6f));
        Assert.That(math.distance(s.Position.xz, new float2(0f, -6f)), Is.GreaterThan(body.FleeDistance), "still by the threat");
    }

    [Test]
    public void MillingAllocatesNothing()
    {
        var body = Deer();
        var s = At(0f, 2f, 7u);
        var land = new Land(Pen);
        var threat = new float2(0f, -6f);
        var brain = new CreatureBrain();
        var herd = new Herd();
        herd.Add(1, 0, s, body);
        for (int f = 0; f < 300; f++) brain.Step(ref s, 1, 0, body, herd, land, threat, false, 0f, Dt);
        long before = System.GC.GetAllocatedBytesForCurrentThread();
        for (int f = 0; f < 600; f++) brain.Step(ref s, 1, 0, body, herd, land, threat, false, 0f, Dt);
        Assert.That(System.GC.GetAllocatedBytesForCurrentThread() - before, Is.Zero);
    }
}
