using Chem;
using NUnit.Framework;
using UnityEngine;

// Village buildings built from their plans (BuildingMesher, D12): every mix of
// walls, roof and cover makes a body and a roof, the same seed makes the same
// meshes, only windows and lamps glow, and the building stays on its footprint.
public class BuildingMesherTests
{
    static BuildingMesher.Spec Spec(WallKind walls, RoofKind roof, RoofCover cover, int storeys, BuildingKind kind = BuildingKind.House) => new()
    {
        kind = kind,
        walls = walls,
        roof = roof,
        cover = cover,
        storeys = storeys,
        chimney = true,
        seed = 1234,
        width = 7.5f,
        depth = 6f,
    };

    [Test]
    public void EveryKindOfBuildingHasABodyAndARoof()
    {
        foreach (WallKind walls in System.Enum.GetValues(typeof(WallKind)))
            foreach (RoofKind roof in System.Enum.GetValues(typeof(RoofKind)))
                foreach (RoofCover cover in System.Enum.GetValues(typeof(RoofCover)))
                    foreach (BuildingKind kind in System.Enum.GetValues(typeof(BuildingKind)))
                    {
                        var made = BuildingMesher.Make(Spec(walls, roof, cover, 2, kind));
                        Assert.That(made.body.vertexCount, Is.GreaterThan(100), $"{walls} {roof} {cover} {kind}");
                        Assert.That(made.roof.vertexCount, Is.GreaterThan(10), $"{walls} {roof} {cover} {kind}");
                        Assert.That(made.size.y, Is.GreaterThan(3f));
                        Object.DestroyImmediate(made.body);
                        Object.DestroyImmediate(made.roof);
                    }
    }

    [Test]
    public void TheSameSeedBuildsTheSameHouse()
    {
        var a = BuildingMesher.Make(Spec(WallKind.Stone, RoofKind.Gable, RoofCover.Slate, 2));
        var b = BuildingMesher.Make(Spec(WallKind.Stone, RoofKind.Gable, RoofCover.Slate, 2));
        Assert.That(b.body.vertices, Is.EqualTo(a.body.vertices));
        Assert.That(b.body.colors, Is.EqualTo(a.body.colors));
    }

    [Test]
    public void OnlyWindowsAndLampsGlow()
    {
        var made = BuildingMesher.Make(Spec(WallKind.Timber, RoofKind.Gable, RoofCover.Thatch, 2, BuildingKind.Inn));
        int lit = 0;
        foreach (var c in made.body.colors)
        {
            if (c.a <= 0f) continue;
            lit++;
            Assert.That(c.r, Is.GreaterThan(c.b), "a glowing face is warm");
        }
        Assert.That(lit, Is.GreaterThan(0));
        Assert.That(lit, Is.LessThan(made.body.vertexCount / 5));
        foreach (var c in made.roof.colors) Assert.That(c.a, Is.EqualTo(0f), "a roof never glows");
    }

    [Test]
    public void TheWallsStayOnTheFootprint()
    {
        var made = BuildingMesher.Make(Spec(WallKind.Log, RoofKind.Hip, RoofCover.Shingle, 1));
        var walls = made.body.bounds;
        // Logs' ends, the door step and the chimney may stand a little past it; nothing more.
        Assert.That(walls.extents.x, Is.LessThan(7.5f / 2f + 0.5f));
        Assert.That(walls.min.z, Is.GreaterThan(-6f / 2f - 1f));
        Assert.That(walls.min.y, Is.GreaterThanOrEqualTo(-0.01f));
    }
}
