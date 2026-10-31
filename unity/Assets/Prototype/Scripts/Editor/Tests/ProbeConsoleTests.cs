using NUnit.Framework;

/// <summary>What the probes' no_console_errors leaves out: Unity's own QuickSearch, and nothing of ours.</summary>
public class ProbeConsoleTests
{
    // As Application.logMessageReceived gives it, from an Editor log of 2026-10-31.
    const string QuickSearch = "System.Collections.Generic.List`1[T].get_Item (System.Int32 index)\n"
        + "UnityEditor.Search.SearchDatabase+<EnumerateAll>d__83.MoveNext () (at /Users/bokken/build/output/unity/unity/Modules/QuickSearch/Editor/Indexing/SearchDatabase.cs:453)\n"
        + "UnityEditor.Search.SearchInit.IndexationOnStartup () (at /Users/bokken/build/output/unity/unity/Modules/QuickSearch/Editor/SearchInit.cs:109)\n";

    [Test]
    public void UnitysQuickSearchIsLeftOut() => Assert.That(ProbeScenario.IsUnitysQuickSearch(QuickSearch), Is.True);

    [Test]
    public void AnErrorOfOursStaysEvenThroughSearch()
    {
        Assert.That(ProbeScenario.IsUnitysQuickSearch("ChemGrid.Update () (at Assets/Prototype/Scripts/ChemGrid.cs:10)\n"), Is.False);
        Assert.That(ProbeScenario.IsUnitysQuickSearch(QuickSearch + "ChemGrid.Find () (at Assets/Prototype/Scripts/ChemGrid.cs:10)\n"), Is.False);
    }

    [Test]
    public void NoStackStays()
    {
        Assert.That(ProbeScenario.IsUnitysQuickSearch(null), Is.False);
        Assert.That(ProbeScenario.IsUnitysQuickSearch(""), Is.False);
    }
}
