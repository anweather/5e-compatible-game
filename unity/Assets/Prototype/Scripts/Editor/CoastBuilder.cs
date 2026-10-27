using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Dresses the coast ValleyCoast shapes (docs/WORLD_LIFE.md): the sea (Ocean,
// out to the horizon), palms and glowbulbs (GeneratedProp, on the squares
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
        var root = new GameObject("Coast").transform;
        BuildSea(root);
        var rng = new System.Random(77);
        var plants = new GameObject("Plants").transform;
        plants.SetParent(root, false);
        foreach (var plant in ValleyCoast.Plants)
            Plant(plants, plant, vertexLit);

        var beach = new GameObject("Beach").transform;
        beach.SetParent(root, false);
        // Sets of loungers by the path's end on the mainland, facing the sea.
        foreach (float along in new[] { -34f, -16f, 24f, 40f })
            BeachSet(beach, OnBeach(along, -9f), Seaward, rng, vertexLit);
        // And round the island's beach, facing out from it.
        foreach (float angle in new[] { 120f, 165f, 210f, 300f })
        {
            var dir = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad));
            BeachSet(beach, OnIsland(dir, 7f), dir, rng, vertexLit);
        }
        Scatter(beach, rng, vertexLit);
        BuildDock(root, vertexLit);
    }

    // --- the sea ----------------------------------------------------------------------

    static void BuildSea(Transform root)
    {
        var material = EditorAssets.LoadOrCreate($"{Generated}/Ocean.mat", () => new Material(Shader.Find("Prototype/Ocean")));
        material.SetVector("_ShoreDir", new Vector4(-Seaward.x, -Seaward.y, 0f, 0f));
        material.SetFloat("_Swell", 0.6f);
        EditorUtility.SetDirty(material);
        var sea = new GameObject("Sea");
        sea.transform.SetParent(root, false);
        var ocean = sea.AddComponent<Ocean>();
        ocean.material = material;
        ocean.level = ValleyCoast.SeaLevel;
        ocean.shoreOrigin = ValleyCoast.ShoreOrigin;
        ocean.seaward = Seaward;
        ocean.inland = 60f;
        ocean.Build();
    }

    // --- plants -----------------------------------------------------------------------

    static void Plant(Transform parent, ValleyCoast.Plant plant, Material material)
    {
        var kind = plant.Palm ? GeneratedProp.Kind.Palm : GeneratedProp.Kind.Glowbulb;
        var prop = Prop(parent, kind, plant.Seed, Ground(plant.At) - 0.1f, plant.At, Quaternion.Euler(0f, plant.Seed % 360, 0f), material);
        if (!plant.Palm) return;
        Object.DestroyImmediate(GeneratedProp.Make(kind, plant.Seed, 0f, out var crown));
        // The trunk is solid where you'd bump into it: a capsule up its lower half, where it hardly leans.
        var trunk = prop.gameObject.AddComponent<CapsuleCollider>();
        trunk.center = crown * 0.25f;
        trunk.height = crown.y * 0.5f + 0.4f;
        trunk.radius = 0.28f;
        trunk.direction = 1;
    }

    // --- the beach ------------------------------------------------------------------------

    // A spot on the mainland beach, along the coast from the path's end, at a distance out (negative: up the beach).
    static Vector2 OnBeach(float along, float outward)
    {
        var end = ValleyCoast.BeachPath[ValleyCoast.BeachPath.Count - 1];
        var p = end + Along * along;
        return p + Seaward * (outward - ValleyCoast.Out(p));
    }

    // A spot on the island's beach in a direction from its middle, a distance in from the water.
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

    // An umbrella with two loungers under it facing the water, and a towel beside them.
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
        }
        var towel = at + side * 2.6f + facing * 0.6f;
        Prop(parent, GeneratedProp.Kind.Towel, rng.Next(), Ground(towel) + 0.02f, towel, face * Quaternion.Euler(0f, rng.Next(-20, 20), 0f), material);
    }

    // Driftwood up the beach and starfish down by the water, on the mainland and round the island.
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

    // --- the dock ---------------------------------------------------------------------

    /// <summary>The dock's foot on the island (x, z), and the way it runs out over the water.</summary>
    public static Vector2 DockFoot { get; private set; }
    public static Vector2 DockOut { get; private set; }

    const float DockLength = 22f;

    // Off the island's beach toward the mainland: a deck on posts with a walkway to stand on and a
    // ramp up from the sand.
    static void BuildDock(Transform root, Material material)
    {
        DockOut = (ValleyCoast.ShoreOrigin - ValleyCoast.IslandAt).normalized;
        DockFoot = OnIsland(DockOut, 1.5f);
        float foot = Ground(DockFoot);
        var dock = Prop(root, GeneratedProp.Kind.Dock, 5, foot, DockFoot, Quaternion.LookRotation(new Vector3(DockOut.x, 0f, DockOut.y)), material);
        dock.length = DockLength;
        dock.Rebuild();
        dock.name = "Dock";
        // The deck sits PropMesher.DockDeck above the foot; walk on an unseen box, up a ramp from the sand.
        var deck = new GameObject("Walkway").AddComponent<BoxCollider>();
        deck.transform.SetParent(dock.transform, false);
        deck.center = new Vector3(0f, PropMesher.DockDeck - 0.25f, DockLength / 2f + 1f);
        deck.size = new Vector3(PropMesher.DockWidth, 0.5f, DockLength - 2f);
        var ramp = new GameObject("Ramp").AddComponent<BoxCollider>();
        ramp.transform.SetParent(dock.transform, false);
        const float run = 4f;
        ramp.transform.SetLocalPositionAndRotation(new Vector3(0f, PropMesher.DockDeck / 2f - 0.25f, 0f), Quaternion.Euler(-Mathf.Atan2(PropMesher.DockDeck, run) * Mathf.Rad2Deg, 0f, 0f));
        ramp.size = new Vector3(PropMesher.DockWidth, 0.5f, Mathf.Sqrt(run * run + PropMesher.DockDeck * PropMesher.DockDeck));
    }

    // --- helpers ----------------------------------------------------------------------

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

    static float Ground(Vector2 at) => ValleyTerrain.GroundAt(new Vector3(at.x, 0f, at.y));

    // Inside the world's edge walls.
    static bool Inside(Vector2 p) => p.x < 438f && p.y > -362f;

    /// <summary>Every palm, by the cell it stands on, for marking those cells trees (WireChemistry).</summary>
    public static IEnumerable<Vector2> PalmSpots()
    {
        foreach (var plant in ValleyCoast.Plants)
            if (plant.Palm) yield return plant.At;
    }
}
