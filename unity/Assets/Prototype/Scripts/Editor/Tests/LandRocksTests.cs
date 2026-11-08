using System.IO;
using System.Linq;
using Chem;
using NUnit.Framework;
using UnityEngine;

// The land's rocks across an online reset (playtest 2026-11-08 L1): ChemGrid.ResetWorld destroys everything under the
// grid, the rocks' root with it, and the next time the ground moved LandRocks read a destroyed rock's body, every frame
// the land eased (147 NullReferenceExceptions a run). They grow again on a new root instead, and ride the ground as before.
public class LandRocksTests
{
    GameObject go;
    string dir;

    [TearDown]
    public void TearDown()
    {
        if (go != null) Object.DestroyImmediate(go);
        if (dir != null && Directory.Exists(dir)) Directory.Delete(dir, true);
    }

    (ChemGrid grid, LandRocks rocks) Laid(int x, int y)
    {
        go = new GameObject("ChemGrid under test");
        var grid = go.AddComponent<ChemGrid>();
        var world = new World(Rules.Load(ChemGrid.Folders.Rules), 16, 16, 1u);
        typeof(ChemGrid).GetProperty(nameof(ChemGrid.World)).SetValue(grid, world);
        dir = Path.Combine(Path.GetTempPath(), "land-rocks-test-" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "rocks.json"), $"{{ \"rocks\": [[{x}, {y}, 1.5]] }}");
        var rocks = new LandRocks(grid);
        rocks.Lay(dir);
        rocks.Tick(0.02f);
        return (grid, rocks);
    }

    static Transform Root(ChemGrid grid) => grid.transform.Cast<Transform>().FirstOrDefault(t => t.name == "Land rocks");

    [Test]
    public void RocksGrowAgainAfterTheGridsChildrenAreDestroyedAndRideTheGround()
    {
        var (grid, rocks) = Laid(5, 6);
        Assert.That(Root(grid).childCount, Is.EqualTo(1), "the map's rock is placed");

        // ChemGrid.ResetWorld: every child of the grid goes.
        foreach (var child in grid.transform.Cast<Transform>().ToArray()) Object.DestroyImmediate(child.gameObject);
        // The new world's land eases under the rock's cell.
        grid.World.At(5, 6).Height += 2;

        Assert.DoesNotThrow(() => rocks.Tick(0.02f), "a destroyed rock's body isn't read");
        var root = Root(grid);
        Assert.That(root, Is.Not.Null, "a new root for the rocks");
        Assert.That(root.childCount, Is.EqualTo(1), "the rock is placed again");
        float ground = grid.GroundAt(grid.CellCenter(5, 6));
        Assert.That(root.GetChild(0).position.y, Is.GreaterThan(ground - 1.5f), "on the ground as it is now");

        grid.World.At(5, 6).Height += 2;
        Assert.DoesNotThrow(() => rocks.Tick(0.02f));
        Assert.That(root.GetChild(0).position.y, Is.GreaterThan(grid.GroundAt(grid.CellCenter(5, 6)) - 1.5f), "and rides it");
    }
}
