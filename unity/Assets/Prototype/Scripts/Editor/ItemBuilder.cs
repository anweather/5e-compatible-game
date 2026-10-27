using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// The things to carry's prefabs (Carryable, rules/items.json), built by
// Prototype > Build Chemistry Yard, one per item in Generated/Things/: three
// glowing balls, a bullseye lantern, a spyglass, a Dancing Lights mote, a
// sending stone and a ball to kick about (Kicker). Where they lie is data, world/things.json (ThingSpawner
// spawns them; a pair of sending stones share a "link" there). Their light,
// colour and use come from the rules at runtime; these are only greybox shapes.
public static class ItemBuilder
{
    const string BouncyPath = PropBuilder.Generated + "/ItemBouncy.asset";
    static readonly Color Placeholder = new Color(0.3f, 0.3f, 0.3f);

    static readonly string[] Kinds =
    {
        ItemIds.GlowBallAmber, ItemIds.GlowBallAzure, ItemIds.GlowBallRose, ItemIds.BullseyeLantern,
        ItemIds.Spyglass, ItemIds.DancingLights, ItemIds.SendingStone, ItemIds.Ball,
    };

    /// <summary>Builds (or rebuilds) one prefab per item.</summary>
    public static List<ThingSpawner.KindPrefab> BuildPrefabs()
    {
        var built = new List<ThingSpawner.KindPrefab>();
        foreach (var id in Kinds)
        {
            var go = Shape(id);
            PropBuilder.Make(go, id);
            go.AddComponent<Carryable>();
            go.AddComponent<NetItem>();
            built.Add(PropBuilder.Save(go, id));
        }
        return built;
    }

    // Greybox shapes. Lit items get a placeholder emission (a black one would drop the
    // material's emission keyword when saved); ItemLight sets the rules' colour.
    static GameObject Shape(string id)
    {
        switch (id)
        {
            case ItemIds.BullseyeLantern:
            {
                var lantern = Solid(PrimitiveType.Cylinder, new Vector3(0.16f, 0.12f, 0.16f));
                PropBuilder.Paint(lantern, "ItemLantern", new Color(0.72f, 0.55f, 0.25f), 0.7f, Placeholder);
                return lantern;
            }
            case ItemIds.Spyglass:
            {
                var glass = Solid(PrimitiveType.Cylinder, new Vector3(0.05f, 0.16f, 0.05f));
                PropBuilder.Paint(glass, "ItemSpyglass", new Color(0.62f, 0.48f, 0.22f), 0.8f);
                return glass;
            }
            case ItemIds.SendingStone:
            {
                var stone = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                stone.transform.localScale = new Vector3(0.14f, 0.08f, 0.12f);
                PropBuilder.Paint(stone, "ItemSendingStone", new Color(0.36f, 0.42f, 0.5f), 0.5f, new Color(0.2f, 0.45f, 0.8f));
                return stone;
            }
            case ItemIds.Ball:
            {
                // A big kickball (70 cm): easy to see and to run into on the big court.
                var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                ball.transform.localScale = Vector3.one * 0.7f;
                ball.GetComponent<Collider>().sharedMaterial = Bouncy();
                PropBuilder.Paint(ball, "ItemBall", new Color(0.95f, 0.93f, 0.86f), 0.35f);
                return ball;
            }
            case ItemIds.DancingLights:
            {
                var mote = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                mote.transform.localScale = Vector3.one * 0.1f;
                PropBuilder.Paint(mote, "ItemMote", Color.white, 0.9f, Placeholder);
                return mote;
            }
            default:
            {
                var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                ball.transform.localScale = Vector3.one * 0.28f;
                ball.GetComponent<Collider>().sharedMaterial = Bouncy();
                PropBuilder.Paint(ball, "ItemGlowBall", new Color(0.9f, 0.9f, 0.88f), 0.6f, Placeholder);
                return ball;
            }
        }
    }

    // A cylinder's own collider is a capsule, which rolls; a convex mesh stands.
    static GameObject Solid(PrimitiveType type, Vector3 scale)
    {
        var go = GameObject.CreatePrimitive(type);
        go.transform.localScale = scale;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.AddComponent<MeshCollider>().convex = true;
        return go;
    }

    static PhysicsMaterial Bouncy()
    {
        EditorAssets.EnsureFolderFor(BouncyPath);
        var bouncy = EditorAssets.LoadOrCreate(BouncyPath, () => new PhysicsMaterial("ItemBouncy"));
        bouncy.bounciness = 0.75f;
        bouncy.bounceCombine = PhysicsMaterialCombine.Maximum;
        bouncy.dynamicFriction = 0.4f;
        bouncy.staticFriction = 0.5f;
        EditorUtility.SetDirty(bouncy);
        return bouncy;
    }
}
