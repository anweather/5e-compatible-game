using UnityEditor;
using UnityEngine;

// The yard's things to carry (Carryable, rules/items.json), placed by
// Prototype > Build Chemistry Yard: three glowing balls on the grass near the
// start, a bullseye lantern at the yard's south edge, a spyglass, a Dancing
// Lights mote, and a pair of sending stones, one by the start and one in the
// far north-west corner. Positions are yard cells, as in ChemYardBuilder's map.
// Their light, colour and use come from the rules at runtime; these are only
// greybox shapes.
public static class ItemBuilder
{
    const string BouncyPath = PropBuilder.Generated + "/ItemBouncy.asset";
    static readonly Color Placeholder = new Color(0.3f, 0.3f, 0.3f);

    static readonly (string id, int x, int y)[] SetPieces =
    {
        (ItemIds.GlowBallAmber, 11, 12), (ItemIds.GlowBallAzure, 15, 12), (ItemIds.GlowBallRose, 14, 14),
        (ItemIds.BullseyeLantern, 12, 15),
        (ItemIds.Spyglass, 16, 14),
        (ItemIds.DancingLights, 10, 14),
        (ItemIds.SendingStone, 13, 11), (ItemIds.SendingStone, 2, 1),
    };

    /// <summary>Places the items for a yard <paramref name="yardHeight"/> cells tall.</summary>
    public static void Place(ChemGrid grid, int yardHeight)
    {
        var parent = new GameObject("Items").transform;
        Carryable lastStone = null;
        int n = 0;
        foreach (var (id, x, y) in SetPieces)
        {
            var prop = PropBuilder.Drop(Shape(id), $"{id} {++n}", parent, grid, id, x, yardHeight - 1 - y);
            var item = prop.gameObject.AddComponent<Carryable>();
            if (id != ItemIds.SendingStone) continue;
            // Stones come in pairs: each one knows the other.
            if (lastStone == null)
            {
                lastStone = item;
                continue;
            }
            item.partner = lastStone;
            lastStone.partner = item;
            lastStone = null;
        }
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
