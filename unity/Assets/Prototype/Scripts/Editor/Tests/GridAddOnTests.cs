using System.Linq;
using NUnit.Framework;

// Grid add-ons (GridAddOn.cs): the components features mark [GridAddOn] are the ones the grid and its rule
// switcher used to add by name, at the same stage and in the same order, so the grid builds as it did.
public class GridAddOnTests
{
    [Test]
    public void TheRuleSwitcherAddsModsRowHousesAndTheWatcherInThatOrder()
    {
        var names = GridAddOns.At(GridStage.Rules).Select(t => t.Name).ToArray();
        Assert.That(names, Is.EqualTo(new[] { "ModsPanel", "RowHouses", "RulesWatcher" }));
    }

    [Test]
    public void TheGridAddsTheHoldProgressAndWaystonesInThatOrder()
    {
        var names = GridAddOns.At(GridStage.Grid).Select(t => t.Name).ToArray();
        Assert.That(names, Is.EqualTo(new[] { "HoldShow", "ProgressKeeper", "HoldBarricades", "HoldMoments", "Waystones" }));
    }

    [Test]
    public void TheEventLogAddsTheWizardHud()
    {
        var names = GridAddOns.At(GridStage.Hud).Select(t => t.Name).ToArray();
        Assert.That(names, Is.EqualTo(new[] { "WizardHud" }));
    }
}
