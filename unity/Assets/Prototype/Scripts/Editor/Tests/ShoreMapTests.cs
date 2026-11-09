using NUnit.Framework;
using UnityEngine;

// The shore map (ShoreMap): the field's distance and smoothing on made-up land
// (ShoreField), then the map of the valley's coast worked out from its land (MapLand): off the island the way to land is the
// way to the island, off the mainland the way to the mainland.
[OnTheValley]
public class ShoreMapTests
{
    static bool[] Land(int n, System.Func<int, int, bool> isLand)
    {
        var land = new bool[n * n];
        for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++) land[z * n + x] = isLand(x, z);
        return land;
    }

    [Test]
    public void TheDistanceIsSignedAndMeasuredFromTheShore()
    {
        const int N = 32;
        // Land on the western half: the shore is the line between x = 15 and x = 16.
        var d = ShoreField.SignedDistance(Land(N, (x, _) => x < 16), N, 2f);
        Assert.AreEqual(1f, d[8 * N + 16], 1e-4f);
        Assert.AreEqual(-1f, d[8 * N + 15], 1e-4f);
        Assert.AreEqual(21f, d[8 * N + 26], 1e-4f);
        Assert.AreEqual(-21f, d[8 * N + 5], 1e-4f);
    }

    [Test]
    public void TheDistanceGoesAroundCornersLikeARuler()
    {
        const int N = 32;
        var d = ShoreField.SignedDistance(Land(N, (x, z) => x == 4 && z == 4), N, 1f);
        Assert.AreEqual(Mathf.Sqrt(9f * 9f + 12f * 12f) - 0.5f, d[16 * N + 13], 1e-3f);
    }

    [Test]
    public void TheWayToLandPointsAtTheNearestShore()
    {
        const int N = 64;
        // Shores to the west and the east, the western one nearer most of the way.
        var pixels = Map(Land(N, (x, _) => x < 8 || x >= 48), N);
        Assert.Less(pixels[32 * N + 16].r, -0.95f, "near the west shore");
        Assert.Greater(pixels[32 * N + 40].r, 0.95f, "near the east shore");
    }

    [Test]
    public void WhereTwoShoresAreEquallyNearTheWayDoesNotSnap()
    {
        const int N = 64;
        var pixels = Map(Land(N, (x, _) => x < 8 || x >= 56), N);
        // Midway the way to land dies away, and no step across the crease is a jump: unblurred it
        // would flip from -1 to 1 between two texels.
        Assert.Less(Mathf.Abs(pixels[32 * N + 31].r + pixels[32 * N + 32].r), 0.05f, "midway");
        Assert.Less(Mathf.Abs(pixels[32 * N + 31].r), 0.2f, "midway");
        for (int x = 16; x < 47; x++)
            Assert.Less(Mathf.Abs(pixels[32 * N + x + 1].r - pixels[32 * N + x].r), 0.3f, $"between texels {x} and {x + 1}");
    }

    static Color[] Map(bool[] land, int n) =>
        ShoreField.Pixels(ShoreField.Blur(ShoreField.SignedDistance(land, n, ShoreField.TexelMetres), n, ShoreField.BlurTexels), n, ShoreField.TexelMetres);

    static ShoreMap BakedMap()
    {
        var land = MapLand.Current;
        Assert.IsNotNull(land, "the map's folder reads");
        return land.ShoreMap;
    }

    [Test]
    public void TheMapSamplesAtItsTexelCentres()
    {
        var map = BakedMap();
        float texel = map.size / map.texture.width;
        var at = map.origin + new Vector2(200.5f, 300.5f) * texel;
        Assert.AreEqual(map.texture.GetPixel(200, 300).b, map.At(at).distance, 1e-3f);
    }

    [Test]
    public void OffTheIslandTheWayToLandIsTheWayToTheIsland()
    {
        var map = BakedMap();
        // All the way round, 25 m off its rough shore. The mainland is over 100 m away everywhere.
        for (int degrees = 0; degrees < 360; degrees += 30)
        {
            float a = degrees * Mathf.Deg2Rad;
            var p = ValleyCoast.IslandAt + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 83f;
            var (toLand, distance, _) = map.At(p);
            var toIsland = (ValleyCoast.IslandAt - p).normalized;
            Assert.Greater(Vector2.Dot(toLand, toIsland), 0.8f, $"{degrees} degrees round the island: {toLand}");
            Assert.That(distance, Is.InRange(5f, 50f), $"{degrees} degrees round the island");
        }
    }

    [Test]
    public void OffTheMainlandTheWayToLandIsTheWayToTheMainland()
    {
        var map = BakedMap();
        for (float along = -200f; along <= 200f; along += 50f)
        {
            var p = ValleyCoast.ShoreOrigin + Vector2.Perpendicular(ValleyCoast.Seaward) * along;
            p += ValleyCoast.Seaward * (30f - ValleyCoast.Out(p));
            var (toLand, distance, _) = map.At(p);
            Assert.Greater(Vector2.Dot(toLand, -ValleyCoast.Seaward), 0.75f, $"{along} m along the coast: {toLand}");
            Assert.That(distance, Is.InRange(15f, 45f), $"{along} m along the coast");
        }
    }

    // Every map keeps its sea: the Ocean isn't in a place a map can leave out (PlaceRoot), and it takes its shore map from
    // the map as it builds. A yard built before the sea moved out of the coast says rebuild it.
    [Test]
    public void TheYardsSeaIsTheMapsOwn()
    {
        WizardPartsTests.WithYard(scene =>
        {
            var oceans = new System.Collections.Generic.List<Ocean>();
            foreach (var root in scene.GetRootGameObjects()) oceans.AddRange(root.GetComponentsInChildren<Ocean>(true));
            Assert.AreEqual(1, oceans.Count, "one sea");
            Assert.IsNull(oceans[0].GetComponentInParent<PlaceRoot>(true), "the sea isn't the coast's to leave out: rebuild the yard");
            oceans[0].Build();
            Assert.AreEqual(MapLand.Current.ShoreMap, oceans[0].shoreMap, "the map's shore map");
            Assert.AreEqual(MapLand.Current.WaterLevel, oceans[0].level, "at the map's level");
        });
    }

    [Test]
    public void TheWorldIsWellInsideTheMap()
    {
        var map = BakedMap();
        var layout = EditorGround.Layout;
        var (x1, _) = layout.ChunkCorner(layout.ChunksX, 0);
        var (_, z0) = layout.ChunkCorner(0, 0);
        Assert.AreEqual(1f, map.At(new Vector2(x1, z0)).reach, 1e-3f, "waves at full strength at the world's south-east corner");
        Assert.AreEqual(0f, map.At(map.origin).reach, 1e-3f, "and none at the map's corner");
    }
}
