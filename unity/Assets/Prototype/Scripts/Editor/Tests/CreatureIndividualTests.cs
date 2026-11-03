using System.Linq;
using Chem;
using NUnit.Framework;
using Punchy.Fauna;
using Unity.Mathematics;

// Punchy's individuals (upstream f3141f6): from CreatureState.Individual each animal shows one
// option of each style group, in a hair colour of its own, and a worker poses its arms for its task.
public class CreatureIndividualTests
{
    sealed class Flat : ICreatureGround
    {
        public float Height(float2 xz) => 0f;
        public bool Walkable(float2 xz) => true;
        public bool Water(float2 xz) => false;
    }

    static Rules Base() => Rules.Load(ChemGrid.Folders.Rules);

    static SegmentInstance[] Pose(CreatureBody b, uint individual, CreatureTask task = CreatureTask.None, float clock = 0f)
    {
        var state = new CreatureState { Scale = 1f, Mode = CreatureMode.Puppet, Seed = 1u, Individual = individual, Task = task, Clock = clock };
        var output = new SegmentInstance[b.Segments.Length];
        int shown = new CreatureAnimator().Evaluate(b, state, 0, new Flat(), output, 0);
        return output.Take(shown).ToArray();
    }

    // The segments an individual shows: its parts but for unchosen options and what hangs from them.
    static int Showing(CreatureBody b, uint individual)
    {
        var hidden = new bool[b.Parts.Length];
        int shown = 1;
        for (int i = 0; i < b.Parts.Length; i++)
        {
            var p = b.Parts[i];
            hidden[i] = p.Parent >= 0 && hidden[p.Parent] || p.StyleGroup > 0 && CreatureBody.Style(individual, p.StyleGroup, p.StyleCount) != p.StyleIndex;
            if (!hidden[i]) shown += p.SegmentCount;
        }
        return shown;
    }

    [TestCase("human")]
    [TestCase("dwarf")]
    [TestCase("elf")]
    [TestCase("goblin")]
    [TestCase("ogre")]
    public void EachIndividualDrawsOneOptionOfEachStyleGroupAndNoneOfTheRest(string species)
    {
        var b = CreatureBody.Build(Base().Fauna, species);
        Assert.That(b.Parts.Any(p => p.StyleGroup > 0), Is.True, species);
        for (uint individual = 1; individual < 40; individual++)
        {
            var pose = Pose(b, individual);
            Assert.That(pose, Has.Length.EqualTo(Showing(b, individual)), $"{species} {individual}");
            Assert.That(pose.Length, Is.LessThan(b.Segments.Length), $"{species} {individual}");
            Assert.That(pose.All(s => s.RadiusStart > 0f), Is.True, $"{species} {individual}: nothing of zero size in the buffer");
        }
    }

    [Test]
    public void IndividualsDiffer()
    {
        var b = CreatureBody.Build(Base().Fauna, "human");
        var styles = Enumerable.Range(1, 200).Select(i => CreatureBody.Style((uint)i, 1, 6)).Distinct().Count();
        Assert.That(styles, Is.EqualTo(6));
        var hair = Enumerable.Range(1, 200).Select(i => b.HairColor((uint)i, default).x).Distinct().Count();
        Assert.That(hair, Is.GreaterThan(3));
    }

    [Test]
    public void AChildTakesItsHairFromTheFolkItIsAVariantOf()
    {
        var fauna = Base().Fauna;
        Assert.That(CreatureBody.Build(fauna, "human-child").HairColors, Is.EqualTo(CreatureBody.Build(fauna, "human").HairColors));
    }

    [Test]
    public void AnimalsOfASpeciesDifferInColourAndBuild()
    {
        var b = CreatureBody.Build(Base().Fauna, "deer");
        var colours = Enumerable.Range(1, 50).Select(i => b.CoatOf(0, (uint)i).Base.x).Distinct().Count();
        Assert.That(colours, Is.GreaterThan(45));
        var girths = Enumerable.Range(1, 50).Select(i => b.GirthOf((uint)i)).ToList();
        Assert.That(girths.Min(), Is.GreaterThan(1f - b.Girth - 1e-4f));
        Assert.That(girths.Max(), Is.LessThan(1f + b.Girth + 1e-4f));
        Assert.That(girths.Max() - girths.Min(), Is.GreaterThan(b.Girth));
        Assert.That(b.CoatOf(0, 7u).Base, Is.EqualTo(b.CoatOf(0, 7u).Base), "the same animal, the same every frame");
    }

    [Test]
    public void IndividualZeroIsTheRowItself()
    {
        var b = CreatureBody.Build(Base().Fauna, "deer");
        Assert.That(b.CoatOf(0, 0u).Base, Is.EqualTo(b.Coats[0].Base));
        Assert.That(b.GirthOf(0u), Is.EqualTo(1f));
    }

    [Test]
    public void FolkPickTheirSkinApartFromTheirClothes()
    {
        var b = CreatureBody.Build(Base().Fauna, "human");
        var plain = b.Coats.Select(c => c.Base.x).ToList();
        var skins = Enumerable.Range(1, 200).Select(i => (uint)i).Where(i => b.PickCoat(CreatureBody.Unit(i, 0)) == 0)
            .Select(i => CreatureBody.Style(i, 9967, b.Coats.Length)).Distinct().Count();
        Assert.That(skins, Is.EqualTo(b.Coats.Length), "a blue tunic comes with any skin");
        Assert.That(plain, Is.Not.Empty);
    }

    [Test]
    public void AWorkerAtItsPostHoldsItsArmsUp()
    {
        var b = CreatureBody.Build(Base().Fauna, "human");
        var idle = Pose(b, 3u);
        var tending = Pose(b, 3u, CreatureTask.Tend);
        Assert.That(tending, Has.Length.EqualTo(idle.Length));
        Assert.That(Enumerable.Range(0, idle.Length).Min(i => math.abs(math.dot(idle[i].Rotation.value, tending[i].Rotation.value))), Is.LessThan(0.99f));
    }

    [Test]
    public void AVillageHasTheFolkTheRowsSayComeOutAmongIt()
    {
        var bodies = new CreatureBodies();
        bodies.Refresh(Base());
        int human = bodies.IndexOf("human"), child = bodies.IndexOf("human-child"), dwarf = bodies.IndexOf("dwarf");
        Assert.That(bodies.FolkAt(human, 0.1f), Is.EqualTo(child).Or.EqualTo(dwarf));
        Assert.That(bodies.FolkAt(human, 0.95f), Is.EqualTo(human));
        Assert.That(bodies.FolkAt(bodies.IndexOf("elf"), 0.1f), Is.EqualTo(bodies.IndexOf("elf")));
        var shares = Enumerable.Range(0, 1000).Select(i => bodies.FolkAt(human, i / 1000f)).ToList();
        Assert.That(shares.Count(k => k == child), Is.EqualTo(300).Within(2));
        Assert.That(shares.Count(k => k == dwarf), Is.EqualTo(120).Within(2));
    }
}
