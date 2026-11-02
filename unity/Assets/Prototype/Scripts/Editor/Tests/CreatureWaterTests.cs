using Chem;
using NUnit.Framework;
using Punchy.Fauna;
using Unity.Mathematics;

// Water life (upstream c52d9ff): swimmers and floaters keep to water deep enough for them, carried
// at their own height; swimmers leap.
public class CreatureWaterTests
{
    const float Dt = 1f / 30f;

    // A lake 40 m across and 2 m deep in dry land.
    sealed class Lake : ICreatureGround
    {
        public float Height(float2 xz) => InLake(xz) ? -2f : 0f;
        public bool Walkable(float2 xz) => !InLake(xz);
        public bool Water(float2 xz) => InLake(xz);
        public float WaterDepth(float2 xz) => InLake(xz) ? 2f : -1f;
        static bool InLake(float2 xz) => math.length(xz) < 20f;
    }

    static CreatureBody Body(string species)
    {
        var bodies = new CreatureBodies();
        bodies.Refresh(Rules.Load(ChemGrid.FindRulesDir()));
        return bodies[bodies.IndexOf(species)];
    }

    static CreatureState Run(CreatureBody b, float seconds, System.Action<CreatureState> each)
    {
        var s = new CreatureState { Position = new float3(0f, 0f, 0f), Scale = 1f, Mode = CreatureMode.Idle, Timer = 0.5f, Seed = 7u, Individual = 3u };
        var brain = new CreatureBrain();
        var herd = new Herd();
        var lake = new Lake();
        for (int f = 0; f < seconds / Dt; f++)
        {
            herd.Clear();
            herd.Add(1, 0, s, b);
            brain.Step(ref s, 1, 0, b, herd, lake, new float2(1e6f, 1e6f), false, 0f, Dt);
            each(s);
        }
        return s;
    }

    [TestCase("trout")]
    [TestCase("carp")]
    [TestCase("glow-eel")]
    public void ASwimmerStaysInTheWaterUnderTheSurface(string species)
    {
        var b = Body(species);
        Assert.That(b.Habitat, Is.EqualTo(Habitat.Swims));
        Run(b, 120f, s =>
        {
            Assert.That(math.length(s.Position.xz), Is.LessThan(20f), species + " left the lake");
            if (s.Air <= 0f) Assert.That(s.Position.y, Is.LessThan(0f).And.GreaterThan(-2f), species + " out of its depth");
        });
    }

    [Test]
    public void ADuckFloatsOnTheSurface()
    {
        var b = Body("duck");
        Assert.That(b.Habitat, Is.EqualTo(Habitat.Floats));
        float want = b.StandHeight * 0.35f;
        Run(b, 60f, s =>
        {
            Assert.That(math.length(s.Position.xz), Is.LessThan(20f));
            Assert.That(s.Position.y, Is.EqualTo(want).Within(0.1f * want + 0.5f));
        });
    }

    [Test]
    public void ALeaperLeapsOutOfTheWater()
    {
        float highest = -9f;
        Run(Body("trout"), 900f, s => highest = math.max(highest, s.Position.y));
        Assert.That(highest, Is.GreaterThan(0.1f));
    }

    [Test]
    public void ShallowWaterWontCarryASwimmer()
    {
        Assert.That(CreatureBrain.Suits(Habitat.Swims, 0.5f), Is.False);
        Assert.That(CreatureBrain.Suits(Habitat.Floats, 0.5f), Is.True);
        Assert.That(CreatureBrain.Suits(Habitat.Floats, -1f), Is.False);
    }

    [Test]
    public void TheWorldsLandAnimalsNeverAskAboutDepth()
    {
        Assert.That(Body("deer").Habitat, Is.EqualTo(Habitat.Land));
        Assert.That(Body("heron").Habitat, Is.EqualTo(Habitat.Land));
    }
}
