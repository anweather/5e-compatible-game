using System.Reflection;
using Chem;
using NUnit.Framework;
using UnityEngine;

// ChemGrid.Current, the grid in play: never a grid that didn't wake or is destroyed, the newest
// grid kept when an older one goes, and its narrow face reading through to the grid. Edit mode runs
// no Awake or OnDestroy, so the tests take and let go the way those do, by name.
public class ChemGridCurrentTests
{
    readonly System.Collections.Generic.List<GameObject> made = new();

    [TearDown]
    public void TearDown()
    {
        foreach (var go in made)
        {
            if (go != null)
            {
                Call(go.GetComponent<ChemGrid>(), "LetGoCurrent");
                Object.DestroyImmediate(go);
            }
        }
        made.Clear();
    }

    ChemGrid NewGrid()
    {
        var go = new GameObject("ChemGrid under test");
        made.Add(go);
        return go.AddComponent<ChemGrid>();
    }

    static void Call(ChemGrid grid, string name) =>
        typeof(ChemGrid).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(grid, null);

    [Test]
    public void AGridThatNeverWokeIsNotCurrent()
    {
        NewGrid();
        Assert.That(ChemGrid.Current, Is.Null, "in Edit mode a grid in the scene never ran its Awake");
    }

    [Test]
    public void AWokenGridIsCurrentAndItsFaceReadsThrough()
    {
        var grid = NewGrid();
        Call(grid, "TakeCurrent");
        Assert.That(ChemGrid.Current, Is.Not.Null);
        Assert.That(ChemGrid.Current.Grid, Is.SameAs(grid));
        Assert.That(ChemGrid.Current.World, Is.Null, "no world before BuildWorld");
        Assert.That(ChemGrid.Current.Rules, Is.Null);
        Assert.DoesNotThrow(() => ChemGrid.Current.Request(new EffectCommand(EffectKind.Shatter, 0, 0, "", ThingId.None)),
            "an effect with no world is dropped, as ChemEffects.Request does");
        Assert.That(ChemGrid.Current, Is.SameAs(ChemGrid.Current), "one face per grid");
    }

    [Test]
    public void ADestroyedGridIsNeverCurrent()
    {
        var grid = NewGrid();
        Call(grid, "TakeCurrent");
        var face = ChemGrid.Current;
        Object.DestroyImmediate(grid.gameObject);
        Assert.That(ChemGrid.Current, Is.Null, "gone even if OnDestroy didn't run");
        Assert.That(face.World, Is.Null, "a face kept from before reads no world");
    }

    [Test]
    public void AnOldGridGoingKeepsTheNewOne()
    {
        var old = NewGrid();
        Call(old, "TakeCurrent");
        var fresh = NewGrid();
        Call(fresh, "TakeCurrent");
        Call(old, "LetGoCurrent");
        Assert.That(ChemGrid.Current.Grid, Is.SameAs(fresh), "a reloaded scene's grid woke before the old one was destroyed");
        Call(fresh, "LetGoCurrent");
        Assert.That(ChemGrid.Current, Is.Null);
    }
}
