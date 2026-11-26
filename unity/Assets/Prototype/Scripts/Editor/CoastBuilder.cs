using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Dresses the coast ValleyCoast shapes (docs/WORLD_LIFE.md): the sea (Ocean,
// out to the horizon, a root of its own that every map keeps: its water is the
// map's, from its land), palms and glowbulbs (GeneratedProp, on the squares
// ValleyCoast.Plants marks), sets of loungers, umbrellas and towels on the
// beach by the path's end and round the island, driftwood and starfish along
// the water's edge, and a dock off the island toward the mainland. The island's
// plaza, beach bar and cottages are a village (world/villages.json). Every
// prop builds its own mesh when the scene loads; only its kind and seed are saved.
public static class CoastBuilder
{
    const string Generated = "Assets/Prototype/Generated";

    static Vector2 Seaward => ValleyCoast.Seaward;
    static Vector2 Along => Vector2.Perpendicular(Seaward);

    public static void Build(Material vertexLit)
    {
        BuildSea();
        var root = new GameObject("Coast").transform;
        var rng = new System.Random(77);
        var plants = new GameObject("Plants").transform;
        plants.SetParent(root, false);
        foreach (var plant in ValleyCoast.Plants)
            Plant(plants, plant, vertexLit);

        var beach = new GameObject("Beach").transform;
        beach.SetParent(root, false);
        foreach (float along in new[] { -34f, -16f, 24f, 40f })
            BeachSet(beach, OnBeach(along, -9f), Seaward, rng, vertexLit);
        foreach (float angle in new[] { 120f, 165f, 210f, 300f })
        {
            var dir = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad));
            BeachSet(beach, OnIsland(dir, 7f), dir, rng, vertexLit);
        }
        Scatter(beach, rng, vertexLit);
        BuildLife(root, vertexLit);
        foreach (float along in new[] { -6f, 6f })
            Bench(beach, OnBeach(along, -24f), Seaward, rng.Next(), vertexLit);
        BuildDock(root, vertexLit);
    }

    static void BuildSea()
    {
        AssetDatabase.DeleteAsset($"{Generated}/ShoreMap.asset");
        var material = EditorAssets.LoadOrCreate($"{Generated}/Ocean.mat", () => new Material(Shader.Find("Prototype/Ocean")));
        material.SetTexture("_ShoreMap", null);
        material.SetFloat("_Swell", 0.6f);
        EditorUtility.SetDirty(material);
        var ocean = new GameObject("Sea").AddComponent<Ocean>();
        ocean.material = material;
        ocean.inland = 60f;
        ocean.Build();
    }

    static void Plant(Transform parent, ValleyCoast.Plant plant, Material material)
    {
        var kind = plant.Palm ? GeneratedProp.Kind.Palm : GeneratedProp.Kind.Glowbulb;
        var prop = Prop(parent, kind, plant.Seed, Ground(plant.At) - 0.1f, plant.At, Quaternion.Euler(0f, plant.Seed % 360, 0f), material);
        if (!plant.Palm) return;
        Object.DestroyImmediate(GeneratedProp.Make(kind, plant.Seed, 0f, out var crown));
        var trunk = prop.gameObject.AddComponent<CapsuleCollider>();
        trunk.center = crown * 0.25f;
        trunk.height = crown.y * 0.5f + 0.4f;
        trunk.radius = 0.28f;
        trunk.direction = 1;
    }

    static Vector2 OnBeach(float along, float outward)
    {
        var end = ValleyCoast.BeachPath[ValleyCoast.BeachPath.Count - 1];
        var p = end + Along * along;
        return p + Seaward * (outward - ValleyCoast.Out(p));
    }

    static Vector2 OnIsland(Vector2 dir, float inside)
    {
        var p = ValleyCoast.IslandAt;
        for (float r = 0f; r < 120f; r += 0.5f)
        {
            p = ValleyCoast.IslandAt + dir * r;
            if (ValleyCoast.InIsland(p) <= inside) break;
        }
        return p;
    }

    static void BeachSet(Transform parent, Vector2 at, Vector2 facing, System.Random rng, Material material)
    {
        var face = Quaternion.LookRotation(new Vector3(facing.x, 0f, facing.y));
        var side = -Vector2.Perpendicular(facing);
        Prop(parent, GeneratedProp.Kind.Umbrella, rng.Next(), Ground(at), at, face, material);
        for (int s = -1; s <= 1; s += 2)
        {
            var spot = at + side * (s * 0.9f) - facing * 0.4f;
            var lounger = Prop(parent, GeneratedProp.Kind.Lounger, rng.Next(), Ground(spot), spot, face, material);
            var box = lounger.gameObject.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 0.25f, 0.1f);
            box.size = new Vector3(0.65f, 0.5f, 1.9f);
            var seat = lounger.gameObject.AddComponent<Seat>();
            seat.seatAt = PropMesher.LoungerSeat;
            seat.lounge = true;
        }
        var towel = at + side * 2.6f + facing * 0.6f;
        Prop(parent, GeneratedProp.Kind.Towel, rng.Next(), Ground(towel) + 0.02f, towel, face * Quaternion.Euler(0f, rng.Next(-20, 20), 0f), material);
    }

    static void Scatter(Transform parent, System.Random rng, Material material)
    {
        for (int i = 0; i < 14; i++)
        {
            var p = OnBeach((float)(rng.NextDouble() - 0.5) * 300f, -3f - (float)rng.NextDouble() * 6f);
            if (Inside(p)) Prop(parent, GeneratedProp.Kind.Driftwood, rng.Next(), Ground(p) - 0.05f, p, Quaternion.Euler(0f, rng.Next(360), 0f), material);
        }
        for (int i = 0; i < 30; i++)
        {
            var p = OnBeach((float)(rng.NextDouble() - 0.5) * 300f, -0.6f - (float)rng.NextDouble() * 1.6f);
            if (Inside(p)) Prop(parent, GeneratedProp.Kind.Starfish, rng.Next(), Ground(p), p, Quaternion.Euler(0f, rng.Next(360), 0f), material);
        }
        for (int i = 0; i < 18; i++)
        {
            float a = (float)(rng.NextDouble() * Mathf.PI * 2.0);
            var p = OnIsland(new Vector2(Mathf.Cos(a), Mathf.Sin(a)), i % 3 == 0 ? 4f : 1.2f);
            var kind = i % 3 == 0 ? GeneratedProp.Kind.Driftwood : GeneratedProp.Kind.Starfish;
            if (Inside(p)) Prop(parent, kind, rng.Next(), Ground(p) - 0.03f, p, Quaternion.Euler(0f, rng.Next(360), 0f), material);
        }
    }

    static void BuildLife(Transform root, Material material)
    {
        material.enableInstancing = true;
        EditorUtility.SetDirty(material);
        var life = new GameObject("Coast life").AddComponent<AmbientLife>();
        life.material = material;
        var rng = new System.Random(91);
        life.gullAnchors = GullAnchors();
        life.crabSpots = CrabSpots(rng);
        life.shoals = Shoals(rng);
    }

    static Vector3[] GullAnchors()
    {
        float sea = ValleyCoast.SeaLevel;
        var gulls = new List<Vector3>();
        for (float along = -120f; along <= 120f; along += 40f)
        {
            var p = OnBeach(along, 6f);
            if (Inside(p)) gulls.Add(new Vector3(p.x, sea, p.y));
        }
        gulls.Add(new Vector3(ValleyCoast.IslandAt.x, sea, ValleyCoast.IslandAt.y));
        return gulls.ToArray();
    }

    static Vector3[] CrabSpots(System.Random rng)
    {
        var crabs = new List<Vector3>();
        for (int i = 0; i < 40; i++)
        {
            var p = i < 30
                ? OnBeach((float)(rng.NextDouble() - 0.5) * 300f, -1.2f - (float)rng.NextDouble() * 2.5f)
                : OnIsland(Angle(rng), 1.5f + (float)rng.NextDouble() * 2f);
            if (Inside(p) && Ground(p) > ValleyCoast.SeaLevel + 0.05f) crabs.Add(new Vector3(p.x, Ground(p), p.y));
        }
        return crabs.ToArray();
    }

    static Vector4[] Shoals(System.Random rng)
    {
        float sea = ValleyCoast.SeaLevel;
        var shoals = new List<Vector4>();
        for (int i = 0; i < 400 && shoals.Count < 26; i++)
        {
            var p = ShoalSpot(i % 3, rng);
            float depth = sea - ValleyShape.HeightAt(p);
            if (Inside(p) && depth > 1.4f && depth < 12f) shoals.Add(new Vector4(p.x, sea, p.y, depth));
        }
        return shoals.ToArray();
    }

    static Vector2 ShoalSpot(int where, System.Random rng)
    {
        if (where == 0) return ValleyShape.LakeCentre + new Vector2((float)(rng.NextDouble() - 0.5) * 120f, (float)(rng.NextDouble() - 0.5) * 70f);
        if (where == 1) return OnBeach((float)(rng.NextDouble() - 0.5) * 300f, 8f + (float)rng.NextDouble() * 45f);
        return OnIsland(Angle(rng), -6f - (float)rng.NextDouble() * 18f);
    }

    static Vector2 Angle(System.Random rng)
    {
        float a = (float)(rng.NextDouble() * Mathf.PI * 2.0);
        return new Vector2(Mathf.Cos(a), Mathf.Sin(a));
    }

    public static Vector2 DockFoot { get; private set; }
    public static Vector2 DockOut { get; private set; }

    const float DockLength = 22f;

    static void BuildDock(Transform root, Material material)
    {
        DockOut = (ValleyCoast.ShoreOrigin - ValleyCoast.IslandAt).normalized;
        DockFoot = OnIsland(DockOut, 1.5f);
        Pier(root, material, "Dock", DockFoot, DockOut, DockLength, 5);
        var south = new Vector2(0f, -1f);
        Pier(root, material, "South pier", OnIsland(south, 1.5f), south, PierLength, 9);
    }

    const float PierLength = 16f;

    static void Pier(Transform root, Material material, string name, Vector2 footAt, Vector2 outward, float length, int seed)
    {
        float foot = Ground(footAt);
        var dock = Prop(root, GeneratedProp.Kind.Dock, seed, foot, footAt, Quaternion.LookRotation(new Vector3(outward.x, 0f, outward.y)), material);
        dock.length = length;
        dock.Rebuild();
        dock.name = name;
        var deck = new GameObject("Walkway").AddComponent<BoxCollider>();
        deck.transform.SetParent(dock.transform, false);
        deck.center = new Vector3(0f, PropMesher.DockDeck - 0.25f, length / 2f + 1f);
        deck.size = new Vector3(PropMesher.DockWidth, 0.5f, length - 2f);
        var ramp = new GameObject("Ramp").AddComponent<BoxCollider>();
        ramp.transform.SetParent(dock.transform, false);
        const float run = 4f;
        ramp.transform.SetLocalPositionAndRotation(new Vector3(0f, PropMesher.DockDeck / 2f - 0.25f, 0f), Quaternion.Euler(-Mathf.Atan2(PropMesher.DockDeck, run) * Mathf.Rad2Deg, 0f, 0f));
        ramp.size = new Vector3(PropMesher.DockWidth, 0.5f, Mathf.Sqrt(run * run + PropMesher.DockDeck * PropMesher.DockDeck));
    }

    public static GeneratedProp Bench(Transform parent, Vector2 at, Vector2 facing, int seed, Material material)
    {
        var turn = Quaternion.LookRotation(new Vector3(facing.x, 0f, facing.y));
        var bench = Prop(parent, GeneratedProp.Kind.Bench, seed, Ground(at), at, turn, material);
        var box = bench.gameObject.AddComponent<BoxCollider>();
        box.center = new Vector3(0f, 0.3f, -0.05f);
        box.size = new Vector3(1.6f, 0.6f, 0.5f);
        var seat = bench.gameObject.AddComponent<Seat>();
        seat.seatAt = PropMesher.BenchSeat;
        return bench;
    }

    static GeneratedProp Prop(Transform parent, GeneratedProp.Kind kind, int seed, float y, Vector2 at, Quaternion turn, Material material)
    {
        var go = new GameObject(kind.ToString());
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(new Vector3(at.x, y, at.y), turn);
        var prop = go.AddComponent<GeneratedProp>();
        prop.kind = kind;
        prop.seed = seed;
        prop.material = material;
        prop.Rebuild();
        return prop;
    }

    static float Ground(Vector2 at) => EditorGround.GroundAt(new Vector3(at.x, 0f, at.y));

    static bool Inside(Vector2 p) => p.x < 438f && p.y > -362f;

    public static IEnumerable<Vector2> PalmSpots()
    {
        foreach (var plant in ValleyCoast.Plants)
            if (plant.Palm) yield return plant.At;
    }
}
