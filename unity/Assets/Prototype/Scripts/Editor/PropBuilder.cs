using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEngine;

// The yard's physics props (PhysicsProp), placed by Prototype > Build
// Chemistry Yard: oil barrels in and by the oil spill, crates on and beside the
// pond, a boulder at its edge, and crates by the brambles. Positions are yard
// cells, as in ChemYardBuilder's map. Each prop is placed at rest on the baked
// terrain (ValleyTerrain), or floating at its waterline on the pond, so nothing
// drops or bobs when Play starts.
//
// Each prop is also an in-scene NetworkObject (NetProp): online the host
// simulates it and clients follow its NetworkTransform. ChemYardBuilder gives
// every in-scene NetworkObject its id once the scene is saved.
public static class PropBuilder
{
    public const string Generated = "Assets/Prototype/Generated";

    static readonly (string kind, int x, int y)[] SetPieces =
    {
        // Oil barrels in the spill (light the oil and they go up) and one beside it.
        (EntityIds.Barrel, 18, 3), (EntityIds.Barrel, 20, 3), (EntityIds.Barrel, 17, 2),
        // Crates on the pond and at its edge, and a boulder beside it.
        (EntityIds.Crate, 19, 5), (EntityIds.Crate, 21, 6), (EntityIds.Crate, 16, 6),
        (EntityIds.Boulder, 23, 5),
        // Crates by the brambles.
        (EntityIds.Crate, 11, 2), (EntityIds.Crate, 11, 3), (EntityIds.Crate, 10, 11),
    };

    /// <summary>Places the set pieces for a yard <paramref name="yardHeight"/> cells tall.</summary>
    public static void Place(ChemGrid grid, int yardHeight)
    {
        world = ChemGrid.MapWorld(grid.LoadBaseRules(), grid.map, grid.seed, out _);
        var parent = new GameObject("Props").transform;
        var flames = EditorAssets.LoadPrefab(PackPaths.FireFx + "/TinyFlames.prefab");
        var blast = EditorAssets.LoadPrefab(PackPaths.FireFx + "/BigExplosion.prefab");
        int n = 0;
        foreach (var (kind, x, y) in SetPieces)
        {
            var prop = Drop(Shape(kind), $"{kind} {++n}", parent, grid, kind, x, yardHeight - 1 - y);
            prop.flamesFx = flames;
            prop.explosionFx = blast;
        }
    }

    // The starting world, for the water props float on (set by Place, which runs first).
    static Chem.World world;

    /// <summary>
    /// Makes <paramref name="go"/> a physics prop of <paramref name="kind"/> under <paramref name="parent"/>,
    /// at rest on yard cell (<paramref name="x"/>, <paramref name="z"/>) in world cells (z grows north).
    /// </summary>
    public static PhysicsProp Drop(GameObject go, string name, Transform parent, ChemGrid grid, string kind, int x, int z)
    {
        go.name = name;
        go.transform.SetParent(parent, false);
        var cell = new Vector3(x, 0f, z) * grid.cellSize;
        go.transform.position = cell + Vector3.up * RestHeight(go, grid, kind, cell);

        var body = go.AddComponent<Rigidbody>();
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        var prop = go.AddComponent<PhysicsProp>();
        prop.grid = grid;
        prop.kind = kind;
        Network(go);
        return prop;
    }

    // Where its centre rests: its lowest collider on the terrain, or, where it floats on water,
    // its waterline (PropState: a floater sinks until the part under water times its lift holds it up).
    static float RestHeight(GameObject go, ChemGrid grid, string kind, Vector3 at)
    {
        Physics.SyncTransforms();
        float below = go.transform.position.y;
        foreach (var c in go.GetComponents<Collider>()) below = Mathf.Min(below, c.bounds.min.y);
        float onGround = ValleyTerrain.GroundAt(at) + go.transform.position.y - below;

        var rules = world.Rules;
        var def = rules.Entity(kind) ?? rules.Item(kind)?.AsEntity();
        var cell = grid.CellAt(at);
        var c0 = world.At(cell.x, cell.y);
        if (def == null || c0 == null || c0.Water <= 0 || !new Chem.PropState(def).Floats) return onGround;
        float half = go.GetComponent<Renderer>().bounds.extents.y;
        float surface = ValleyTerrain.GroundAt(at) + c0.Water / (float)rules.Sim.WaterUnitsPerStep * grid.stepHeight;
        float afloat = surface - 2f * half / (float)new Chem.PropState(def).Lift + half;
        return Mathf.Max(onGround, afloat);
    }

    // Host-simulated online: server-authoritative transform, interpolated on clients. It never
    // changes parent through Netcode (a carried item rides in a hand by our own code, solo too).
    static void Network(GameObject go)
    {
        var networkObject = go.AddComponent<NetworkObject>();
        networkObject.AutoObjectParentSync = false;
        var netTransform = go.AddComponent<NetworkTransform>();
        netTransform.AuthorityMode = NetworkTransform.AuthorityModes.Server;
        netTransform.SyncScaleX = netTransform.SyncScaleY = netTransform.SyncScaleZ = false;
        netTransform.InLocalSpace = false;
        netTransform.Interpolate = true;
        go.AddComponent<NetProp>();
    }

    // Greybox shapes: a wooden crate, an upright oil drum, a round grey boulder.
    static GameObject Shape(string kind)
    {
        switch (kind)
        {
            case EntityIds.Barrel:
            {
                var drum = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                drum.transform.localScale = new Vector3(0.7f, 0.5f, 0.7f);
                // The cylinder's own collider is a capsule, which won't stand up; a convex mesh will.
                Object.DestroyImmediate(drum.GetComponent<Collider>());
                drum.AddComponent<MeshCollider>().convex = true;
                AddFeet(drum, new Vector3(0.2f, 0.5f - FootRadius + FootProud, 0.2f));
                Paint(drum, "PropBarrel", new Color(0.55f, 0.12f, 0.08f), 0.55f);
                return drum;
            }
            case EntityIds.Boulder:
            {
                var rock = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                rock.transform.localScale = new Vector3(1.1f, 0.95f, 1.05f);
                Paint(rock, "PropBoulder", new Color(0.46f, 0.45f, 0.43f), 0.1f);
                return rock;
            }
            default:
            {
                var crate = GameObject.CreatePrimitive(PrimitiveType.Cube);
                crate.transform.localScale = Vector3.one * 0.8f;
                AddFeet(crate, Vector3.one * (0.4f - FootRadius + FootProud));
                Paint(crate, "PropCrate", new Color(0.62f, 0.44f, 0.24f), 0.15f);
                return crate;
            }
        }
    }

    // PhysX can catch a flat face sliding over an edge between triangles or colliders (an
    // "internal edge"), stopping a sliding crate dead. So a crate or barrel stands on small
    // spheres at its corners, just proud of its faces: spheres glide over edges.
    const float FootRadius = 0.06f, FootProud = 0.02f;

    /// <summary>Eight sphere colliders at (±x, ±y, ±z) from the centre, in metres.</summary>
    static void AddFeet(GameObject go, Vector3 corner)
    {
        var scale = go.transform.localScale;
        float largest = Mathf.Max(scale.x, Mathf.Max(scale.y, scale.z));
        for (int i = 0; i < 8; i++)
        {
            var sign = new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f);
            var foot = go.AddComponent<SphereCollider>();
            // Sphere colliders scale their radius by the largest axis of the object's scale.
            foot.radius = FootRadius / largest;
            foot.center = new Vector3(sign.x * corner.x / scale.x, sign.y * corner.y / scale.y, sign.z * corner.z / scale.z);
        }
    }

    public static void Paint(GameObject go, string material, Color color, float smoothness, Color? emission = null) =>
        go.GetComponent<Renderer>().sharedMaterial = EditorAssets.LitMaterial($"{Generated}/{material}.mat", color, smoothness, emission);
}
