using System.IO;
using Chem;
using NUnit.Framework;
using UnityEngine;

// The view across the seams of a world that wraps (ViewWrap, docs/design/hills.md): cells fold into the world, and a
// place goes at the copy nearest the camera, so the land over a seam is drawn where it meets. The hills (a torus) are
// played from their rows alone like the Wide World.
public class ViewWrapTests
{
    [TearDown]
    public void NoWrap()
    {
        ViewWrap.Set(0, 0, 1f);
        ViewWrap.EyeAt = null;
    }

    [Test]
    public void APlaceGoesToTheCopyNearestTheCamera()
    {
        ViewWrap.Set(6912, 5760, 1.5f);
        ViewWrap.EyeAt = new Vector3(10360f, 20f, -10f); // just west of the east seam, just south of the south one
        Assert.That(ViewWrap.PeriodX, Is.EqualTo(10368f));
        Assert.That(ViewWrap.NearX(10f), Is.EqualTo(10378f), "the west edge's copy is just east of the seam");
        Assert.That(ViewWrap.NearX(10300f), Is.EqualTo(10300f), "a place near the camera stays");
        Assert.That(ViewWrap.NearZ(8630f), Is.EqualTo(-10f), "the north edge's copy is just south");
        Assert.That(ViewWrap.FoldX(6913), Is.EqualTo(1));
        Assert.That(ViewWrap.FoldY(-1), Is.EqualTo(5759));
        Assert.That(ViewWrap.FoldChunk((108, -1), 108, 90), Is.EqualTo((0, 89)));
        Assert.That(ViewWrap.ChunkDistance(107, 0, 108, true), Is.EqualTo(1));
        Assert.That(ViewWrap.ChunkDistance(89, 0, 90, false), Is.EqualTo(1));
    }

    [Test]
    public void WithNoWrapNothingMoves()
    {
        ViewWrap.Set(0, 0, 1.5f);
        ViewWrap.EyeAt = new Vector3(10360f, 20f, -10f);
        Assert.That(ViewWrap.NearX(10f), Is.EqualTo(10f));
        Assert.That(ViewWrap.FoldX(6913), Is.EqualTo(6913));
        Assert.That(ViewWrap.FoldChunk((108, -1), 108, 90), Is.EqualTo((108, -1)));
        Assert.That(ViewWrap.ChunkDistance(107, 0, 108, true), Is.EqualTo(107));
    }

    [Test]
    public void TheHillsArePlayedFromTheirRowsAlone()
    {
        string folder = ChemGrid.PlayFolder("hills-12x10", playing: true);
        Assert.That(GeneratedMap.IsRowsOnly(folder), Is.True, folder);
        Assert.That(File.Exists(Path.Combine(folder, MapFile.CellsName)), Is.False, "no cells written");
        Assert.That(MapCheck.Errors(folder), Is.Empty);
        var map = MapFile.Load(folder);
        Assert.That((map.WrapX, map.WrapY), Is.EqualTo((true, true)));
        Assert.That(map.IsOff("fauna"), Is.True);
    }
}
