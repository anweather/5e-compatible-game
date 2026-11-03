using System.Linq;
using Chem;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;

// RowMesher's shapes, without a world; and that the rows the world is made with stand on the valley
// as it was baked, so drawing buildings from rows can't move a village or a cabin.
public class RowMesherTests
{
    const float Tolerance = 0.01f;
    const string YardScene = "Assets/Prototype/Scenes/ChemistryYard.unity";

    static readonly RowMesher.Tables Plain = new()
    {
        OpeningLook = kind => JObject.Parse(kind == "window" ? "{ \"width\": 0.9, \"height\": 1.2, \"sill\": 0.9 }" : "{ \"width\": 0.9, \"height\": 2.1, \"sill\": 0 }"),
        TerrainColor = _ => Color.grey,
        Tiled = _ => false,
    };

    // A box 6 × 4.5 m of 3 m walls.
    static JObject Box(string roof, string openings = "") => JObject.Parse(
        "{ \"wall\": { \"height\": 3, \"thickness\": 0.3 }, \"joins\": { \"a\": [0, 0], \"b\": [8, 0], \"c\": [8, 6], \"d\": [0, 6] }," +
        " \"walls\": { \"n\": { \"from\": \"a\", \"to\": \"b\"" + openings + " }, \"e\": { \"from\": \"b\", \"to\": \"c\" }, \"s\": { \"from\": \"c\", \"to\": \"d\" }, \"w\": { \"from\": \"d\", \"to\": \"a\" } }," +
        " \"roofs\": { \"main\": { \"over\": [\"a\", \"b\", \"c\", \"d\"], " + roof + " } } }");

    [Test]
    public void WallsStandInsideTheirLines()
    {
        var bounds = RowMesher.Make(Box("\"shape\": \"flat\""), Plain).body.bounds;
        Assert.AreEqual(0f, bounds.min.x, Tolerance);
        Assert.AreEqual(6f, bounds.max.x, Tolerance);
        Assert.AreEqual(0f, bounds.max.z, Tolerance);
        Assert.AreEqual(-4.5f, bounds.min.z, Tolerance);
    }

    [Test]
    public void AHipRisesByItsPitchToTheMiddle()
    {
        var bounds = RowMesher.Make(Box("\"shape\": \"hip\", \"pitch\": [6, 12]"), Plain).roof.bounds;
        Assert.AreEqual(3f + 2.25f * 0.5f, bounds.max.y, 0.2f);
    }

    [Test]
    public void AGableRisesAcrossTheShortSideAndEavesOverhang()
    {
        var bounds = RowMesher.Make(Box("\"shape\": \"gable\", \"pitch\": [6, 12], \"overhang\": 0.4"), Plain).roof.bounds;
        Assert.AreEqual(3f + 2.25f * 0.5f, bounds.max.y, 0.2f);
        Assert.Greater(bounds.max.x, 6.2f);
    }

    [Test]
    public void AShedRisesFromItsLowWall()
    {
        var bounds = RowMesher.Make(Box("\"shape\": \"shed\", \"low\": \"s\", \"pitch\": [4, 12]"), Plain).roof.bounds;
        Assert.AreEqual(3f + 4.5f / 3f, bounds.max.y, 0.1f);
    }

    [Test]
    public void AParapetRaisesTheWalls()
    {
        var bounds = RowMesher.Make(Box("\"shape\": \"flat\", \"parapet\": 0.6"), Plain).body.bounds;
        Assert.AreEqual(3.6f, bounds.max.y, Tolerance);
    }

    [Test]
    public void AWindowIsAPaneAndAHoleInTheWall()
    {
        var plain = RowMesher.Make(Box("\"shape\": \"flat\""), Plain);
        var made = RowMesher.Make(Box("\"shape\": \"flat\"", ", \"openings\": [{ \"kind\": \"window\", \"at\": 2 }]"), Plain);
        Assert.AreEqual(1, made.panes.Length);
        Assert.AreEqual(2.25f, made.panes[0].at.x, Tolerance);
        Assert.Greater(made.body.vertexCount, plain.body.vertexCount);
    }

    [Test]
    public void ARowOfNoWallsDrawsNoRoof()
    {
        var made = RowMesher.Make(JObject.Parse("{ \"built\": { }, \"roofs\": { \"main\": { \"shape\": \"gable\" } } }"), Plain);
        Assert.AreEqual(0, made.roof.vertexCount);
    }

    [Test]
    public void AnOverhangIsItsOwnLengthNotTheGrids()
    {
        var bounds = RowMesher.Make(Box("\"shape\": \"hip\", \"pitch\": [4, 12], \"overhang\": 0.8"), Plain).roof.bounds;
        Assert.AreEqual(6.8f, bounds.max.x, Tolerance);
        Assert.AreEqual(-5.3f, bounds.min.z, Tolerance);
    }

    [Test]
    public void NothingARowDrawsGlows()
    {
        var made = RowMesher.Make(Box("\"shape\": \"hip\", \"pitch\": [4, 12]", ", \"openings\": [{ \"kind\": \"door\", \"at\": 2 }]"), Plain);
        Assert.AreEqual(0f, made.body.colors.Concat(made.roof.colors).Max(c => c.a), "alpha is glow to VillageLit");
    }

    [Test]
    public void AWallIsCutOntoTheCellsItStandsOn()
    {
        var made = RowMesher.Make(Box("\"shape\": \"flat\""), Plain);
        CollectionAssert.AreEquivalent(new[] { new Vector2Int(0, 0), new Vector2Int(1, 0), new Vector2Int(2, 0), new Vector2Int(3, 0) },
            made.stretches.Where(s => Mathf.Approximately(s.Middle.z, -0.15f)).Select(s => s.Place).ToArray());
        Assert.AreEqual(2 * (4 + 3), made.stretches.Length, "each wall has one stretch per cell along it");
    }

    [Test]
    public void AWallFollowsItsCellsAndTheRoofGoesWithIt()
    {
        var made = RowMesher.Make(Box("\"shape\": \"hip\", \"pitch\": [4, 12]"), Plain);
        var ruin = new RowRuin(made, _ => true);
        int whole = Drawn(made.body), roofWhole = Drawn(made.roof);
        var lost = new Vector2Int(1, 0);
        Assert.IsTrue(ruin.Look(at => new RowRuin.CellNow { blocking = at != lost }, false));
        Assert.AreEqual(1, ruin.Down);
        Assert.IsTrue(ruin.RoofHurt);
        Assert.Less(Drawn(made.roof), roofWhole);
        Assert.AreNotEqual(whole, Drawn(made.body));
        Assert.IsFalse(ruin.Look(at => new RowRuin.CellNow { blocking = at != lost }, false), "nothing new shows, so nothing is written");
    }

    [Test]
    public void HalfTheWallsDownTakesTheRoofAndCellsPutBackBringItBack()
    {
        var made = RowMesher.Make(Box("\"shape\": \"hip\", \"pitch\": [4, 12]"), Plain);
        var ruin = new RowRuin(made, _ => true);
        int whole = Drawn(made.body), roofWhole = Drawn(made.roof);
        ruin.Look(at => new RowRuin.CellNow { blocking = at.y > 1 }, false);
        Assert.AreEqual(0, Drawn(made.roof));
        ruin.Look(_ => new RowRuin.CellNow { blocking = true }, false);
        Assert.AreEqual(roofWhole, Drawn(made.roof));
        Assert.AreEqual(whole, Drawn(made.body));
    }

    [Test]
    public void TheHeadOverAGoneDoorStandsWhileTheWallBesideItDoes()
    {
        var made = RowMesher.Make(Box("\"shape\": \"flat\"", ", \"openings\": [{ \"kind\": \"door\", \"at\": 2 }]"), Plain);
        var ruin = new RowRuin(made, _ => true);
        var door = new Vector2Int(1, 0);
        ruin.Look(at => new RowRuin.CellNow { blocking = at != door }, false);
        Assert.AreEqual(0, ruin.Down, "the door's gone but its head has the wall beside it");
        ruin.Look(at => new RowRuin.CellNow { blocking = at.y != 0 }, false);
        Assert.AreEqual(6, ruin.Down, "with the wall gone it falls too: the four along it and the two corners' cells");
    }

    [Test]
    public void AWholeWallOfArchStandsOnTheWallThatMeetsIt()
    {
        var row = JObject.Parse("{ \"wall\": { \"height\": 3, \"thickness\": 0.3 }, \"joins\": { \"a\": [0, 0], \"b\": [4, 0], \"c\": [0, 4] }," +
            " \"walls\": { \"arch\": { \"from\": \"a\", \"to\": \"b\", \"openings\": [{ \"kind\": \"arch\", \"at\": 0, \"width\": 3 }] }, \"side\": { \"from\": \"a\", \"to\": \"c\" } } }");
        var made = RowMesher.Make(row, Plain);
        var ruin = new RowRuin(made, at => at.y < 0 || at.x < 0);
        ruin.Look(_ => new RowRuin.CellNow { blocking = true }, false);
        Assert.AreEqual(0, ruin.Down, "its cells are open, not lost");
        ruin.Look(at => new RowRuin.CellNow { blocking = false }, false);
        Assert.AreEqual(ruin.Stretches, ruin.Down);
    }

    [Test]
    public void AFireLeavesSootEvenWhereTheWallStands()
    {
        var made = RowMesher.Make(Box("\"shape\": \"flat\""), Plain);
        var ruin = new RowRuin(made, _ => true);
        var was = made.body.colors32;
        Assert.IsTrue(ruin.Look(at => new RowRuin.CellNow { blocking = true, burning = at == Vector2Int.zero }, false));
        Assert.AreEqual(0, ruin.Down);
        CollectionAssert.AreNotEqual(was, made.body.colors32);
    }

    static int Drawn(Mesh mesh) => mesh.triangles.Count(i => i != 0);

    [Test]
    public void TheCabinsAndVillagesStandWhereTheyAlwaysDid()
    {
        var scene = EditorSceneManager.OpenScene(YardScene, OpenSceneMode.Additive);
        try
        {
            var grid = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<ChemGrid>()).First();
            var yard = ChemYardBuilder.Map.Split((char)10);
            ValleyShape.Plan(yard[0].Length, yard.Length, grid.cellSize);
            CollectionAssert.AreEqual(new[] { new Vector2(-16.75f, 15.25f), new Vector2(-30f, -40f), new Vector2(155f, 62f), new Vector2(-95f, 140f) }, ValleyShape.Cabins.Select(c => c.At).ToArray());
            var world = ChemGrid.MapWorld(grid.LoadBaseRules(), grid.map, grid.seed, out _);
            var file = WorldBuildings.Load(ChemGrid.Folders.World);
            foreach (var plan in ValleyShape.Villages)
            {
                var laid = WorldBuildings.LaidPlan(file.Villages.First(v => v.Name == plan.Spec.Name), (x, y) => world.InBounds(x, y) ? world.At(x, y).T : null);
                Assert.AreEqual(plan.Digest(), laid.Digest(), plan.Spec.Name);
            }
        }
        finally
        {
            EditorSceneManager.CloseScene(scene, true);
        }
    }

    [Test]
    public void EveryRowInTheFileHasASite()
    {
        var file = WorldBuildings.Load(ChemGrid.Folders.World);
        Assert.IsNotEmpty(file.FileRows);
        foreach (var row in file.FileRows) Assert.IsNotNull(row["site"], (string)row["id"]);
    }
}
