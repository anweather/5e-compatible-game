using Chem;
using NUnit.Framework;

// The game modes are Chem.Play's, above the core (docs/design/architecture-review.md step 9): the world the game
// starts with gets them as it is made (ChemGrid.StartWorld), so its Hold, defense field and course run.
[OnTheValley]
public class PlayModesTests
{
    [Test]
    public void TheGamesWorldHasItsModes()
    {
        var world = ChemGrid.StartWorld(Rules.Load(ChemGrid.Folders.Rules), 7, out _);
        Assert.That(world.HasModes, "ChemGrid makes its world through PlayModes.Install");
        Assert.That(world.Play().World, Is.SameAs(world));
        Assert.That(world.Play().Hold.Site, Is.SameAs(world.HoldSite()));
    }
}
