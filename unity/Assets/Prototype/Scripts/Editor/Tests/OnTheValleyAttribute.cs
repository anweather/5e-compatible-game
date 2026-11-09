using System;
using Chem;
using NUnit.Framework;
using NUnit.Framework.Interfaces;

// On a fixture that reads the valley's own data (its cells, land, ground, rows, coast): the fixture runs on the valley
// (ChemGrid.OnMap) whatever unity/Temp/map points the Editor at, so EditMode stays green with a probe on another map.
[AttributeUsage(AttributeTargets.Class)]
public sealed class OnTheValleyAttribute : Attribute, ITestAction
{
    IDisposable scope;

    public ActionTargets Targets => ActionTargets.Suite;

    public void BeforeTest(ITest test) => scope = ChemGrid.OnMap(GameFolders.DefaultMap);

    public void AfterTest(ITest test)
    {
        scope?.Dispose();
        scope = null;
    }
}
