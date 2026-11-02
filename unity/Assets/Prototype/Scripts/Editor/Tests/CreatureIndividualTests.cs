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

    static Rules Base() => Rules.Load(ChemGrid.FindRulesDir());

    static SegmentInstance[] Pose(CreatureBody b, uint individual, CreatureTask task = CreatureTask.None, float clock = 0f)
    {
        var state = new CreatureState { Scale = 1f, Mode = CreatureMode.Puppet, Seed = 1u, Individual = individual, Task = task, Clock = clock };
        var output = new SegmentInstance[b.Segments.Length];
        new CreatureAnimator().Evaluate(b, state, 0, new Flat(), output, 0);
        return output;
    }

    [TestCase("human")]
    [TestCase("dwarf")]
    [TestCase("elf")]
    [TestCase("goblin")]
    [TestCase("ogre")]
    public void EachIndividualShowsOneOptionOfEachStyleGroup(string species)
    {
        var b = CreatureBody.Build(Base().Fauna, species);
        var groups = b.Parts.Where(p => p.StyleGroup > 0).Select(p => p.StyleGroup).Distinct().ToList();
        Assert.That(groups, Is.Not.Empty, species);
        for (uint individual = 1; individual < 40; individual++)
        {
            var pose = Pose(b, individual);
            foreach (int group in groups)
            {
                var shown = b.Parts.Where(p => p.StyleGroup == group && p.SegmentCount > 0 && pose[p.FirstSegment].RadiusStart > 0f).Select(p => p.StyleIndex).Distinct().ToList();
                Assert.That(shown, Has.Count.EqualTo(1), $"{species} {individual} group {group}");
            }
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
    public void AWorkerAtItsPostHoldsItsArmsUp()
    {
        var b = CreatureBody.Build(Base().Fauna, "human");
        var arm = b.Parts.First(p => p.Role == LimbRole.Arm);
        var idle = Pose(b, 3u)[arm.FirstSegment].Rotation;
        var tending = Pose(b, 3u, CreatureTask.Tend)[arm.FirstSegment].Rotation;
        Assert.That(math.abs(math.dot(idle.value, tending.value)), Is.LessThan(0.99f));
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
