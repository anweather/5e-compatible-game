using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEngine;

// The physics props' prefabs (PhysicsProp), built by Prototype > Build
// Chemistry Yard: an oil barrel, a crate and a boulder, one prefab per kind in
// Generated/Things/. Where they stand is data, world/things.json, and
// ThingSpawner spawns them there at runtime (at rest on the ground, floaters
// at their waterline), so nothing is placed in the scene.
//
// Each prefab is also a NetworkObject (NetProp, with the thing's stable id):
// online the host spawns it and clients follow its NetworkTransform.
// ChemYardBuilder registers every prefab with the NetworkManager.
public static class PropBuilder
{
    public const string Generated = "Assets/Prototype/Generated";
    const string ThingsFolder = Generated + "/Things";

    static readonly string[] Kinds = { EntityIds.Barrel, EntityIds.Crate, EntityIds.Boulder };

    /// <summary>Builds (or rebuilds) one prefab per prop kind.</summary>
    public static List<ThingSpawner.KindPrefab> BuildPrefabs()
    {
        var flames = EditorAssets.LoadPrefab(PackPaths.FireFx + "/TinyFlames.prefab");
        var blast = EditorAssets.LoadPrefab(PackPaths.FireFx + "/BigExplosion.prefab");
        var built = new List<ThingSpawner.KindPrefab>();
        foreach (var kind in Kinds)
        {
            var go = Shape(kind);
            var prop = Make(go, kind);
            prop.flamesFx = flames;
            prop.explosionFx = blast;
            built.Add(Save(go, kind));
        }
        return built;
    }

    /// <summary>Makes <paramref name="go"/> a physics prop of <paramref name="kind"/> (rules/entities.json or rules/items.json), networked.</summary>
    public static PhysicsProp Make(GameObject go, string kind)
    {
        go.name = kind;
        var body = go.AddComponent<Rigidbody>();
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        var prop = go.AddComponent<PhysicsProp>();
        prop.kind = kind;
        Network(go);
        return prop;
    }

    /// <summary>Saves <paramref name="go"/> as the prefab for <paramref name="kind"/> and removes it from the scene.</summary>
    public static ThingSpawner.KindPrefab Save(GameObject go, string kind)
    {
        var path = $"{ThingsFolder}/{kind}.prefab";
        EditorAssets.EnsureFolderFor(path);
        var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
        Object.DestroyImmediate(go);
        StampNetworkHash(prefab);
        return new ThingSpawner.KindPrefab { kind = kind, prefab = prefab };
    }

    // A network prefab is known by its GlobalObjectIdHash, which Netcode works out in OnValidate.
    // That doesn't run for a prefab saved from code, so run it and save the hash into the asset,
    // so every Editor (Multiplayer Play Mode's virtual players too) reads the same hash.
    static void StampNetworkHash(GameObject prefab)
    {
        var networkObject = prefab.GetComponent<NetworkObject>();
        var onValidate = typeof(NetworkObject).GetMethod("OnValidate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        onValidate?.Invoke(networkObject, null);
        EditorUtility.SetDirty(prefab);
        AssetDatabase.SaveAssetIfDirty(prefab);
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
