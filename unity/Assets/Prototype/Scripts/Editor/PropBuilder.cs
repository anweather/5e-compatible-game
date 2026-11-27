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
// ChemYardBuilder registers every prefab with the NetworkManager, and
// StableNetworkIds gives each the same network id on every machine.
public static class PropBuilder
{
    public const string Generated = "Assets/Prototype/Generated";
    const string ThingsFolder = Generated + "/Things";

    static readonly string[] Kinds = { EntityIds.Barrel, EntityIds.Crate, EntityIds.Boulder };

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
        var dressed = Dressed(ThingSpawner.DressedProp);
        dressed.flamesFx = flames;
        dressed.explosionFx = blast;
        built.Add(Save(dressed.gameObject, ThingSpawner.DressedProp));
        return built;
    }

    public static PhysicsProp Dressed(string kind)
    {
        var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.GetComponent<Renderer>().enabled = false;
        return Make(box, kind);
    }

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

    public static ThingSpawner.KindPrefab Save(GameObject go, string kind)
    {
        var path = $"{ThingsFolder}/{kind}.prefab";
        EditorAssets.EnsureFolderFor(path);
        StableNetworkIds.PrepareAsset(path);
        PrefabUtility.SaveAsPrefabAsset(go, path);
        Object.DestroyImmediate(go);
        return new ThingSpawner.KindPrefab { kind = kind, prefab = StableNetworkIds.PinPrefab(path) };
    }

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

    static GameObject Shape(string kind)
    {
        switch (kind)
        {
            case EntityIds.Barrel:
            {
                var drum = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                drum.transform.localScale = new Vector3(0.7f, 0.5f, 0.7f);
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

    const float FootRadius = 0.06f, FootProud = 0.02f;

    static void AddFeet(GameObject go, Vector3 corner)
    {
        var scale = go.transform.localScale;
        float largest = Mathf.Max(scale.x, Mathf.Max(scale.y, scale.z));
        for (int i = 0; i < 8; i++)
        {
            var sign = new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f);
            var foot = go.AddComponent<SphereCollider>();
            foot.radius = FootRadius / largest;
            foot.center = new Vector3(sign.x * corner.x / scale.x, sign.y * corner.y / scale.y, sign.z * corner.z / scale.z);
        }
    }

    public static void Paint(GameObject go, string material, Color color, float smoothness, Color? emission = null) =>
        go.GetComponent<Renderer>().sharedMaterial = EditorAssets.LitMaterial($"{Generated}/{material}.mat", color, smoothness, emission);
}
