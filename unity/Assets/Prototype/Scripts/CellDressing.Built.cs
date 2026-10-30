using UnityEngine;

// The shaped parts of a cell's dressing: a bridge's plank deck, an iron plate
// lying on the ground, the rocks of a raised wall. Each is seeded from its
// cell, so neighbours differ (turned, sized and shaded apart) and a strip of
// them reads as separate things, not a row of tiles.
public sealed partial class CellDressing
{
    // Each rock model's size and where its mesh sits, measured once.
    static readonly System.Collections.Generic.Dictionary<GameObject, Bounds> RockBounds = new();

    static readonly Color Iron = new(0.30f, 0.29f, 0.28f);
    static readonly Color Bronze = new(0.45f, 0.32f, 0.18f);
    static readonly Color Rusted = new(0.42f, 0.22f, 0.12f);

    // A bridge's deck: boards across the cell, uneven and with gaps, on two beams.
    void DressPlanks(Transform root, Vector3 surface, Color color, System.Random rng, int x, int y)
    {
        const int boards = 5;
        float depth = CellSize / boards;
        foreach (float side in new[] { -0.3f, 0.3f })
        {
            var beam = Part(root, PrimitiveType.Cube, surface + new Vector3(side * CellSize, 0.05f, 0f), new Vector3(0.16f, 0.1f, CellSize * 1.02f), "Planks", color);
            Paint(beam.GetComponent<Renderer>(), "Planks", color * 0.7f, 0.15f, x, y * 3 + (side < 0 ? 1 : 2));
        }
        for (int i = 0; i < boards; i++)
        {
            var at = surface + new Vector3(Jitter(rng, 0.04f), 0.12f + Jitter(rng, 0.012f), (i + 0.5f) * depth - CellSize / 2f);
            var board = Part(root, PrimitiveType.Cube, at, new Vector3(CellSize * (1.0f + Jitter(rng, 0.08f)), 0.05f, depth * (0.78f + Jitter(rng, 0.08f))), "Planks", color);
            board.transform.rotation = Quaternion.Euler(Jitter(rng, 1.5f), Jitter(rng, 4f), Jitter(rng, 1.5f));
            Paint(board.GetComponent<Renderer>(), "Planks", color, 0.15f, x * 5 + i, y);
        }
    }

    // An iron (now and then bronze) plate lying on the ground: sized, shifted, turned and tilted a little, a
    // raised face on a thicker rim, and a rivet at each corner. A rusted one (metal left wet, materials.json
    // rust) is the same plate gone orange-brown, flaking and dull.
    void DressPlate(Transform root, Vector3 surface, System.Random rng, int x, int y, bool rusted = false)
    {
        var color = rng.NextDouble() < 0.25 ? Bronze : Iron;
        if (rusted) color = Rusted;
        var plate = new GameObject("Plate").transform;
        plate.SetParent(root, false);
        // Shifted off the cell's centre too, so a run of plates is a scatter, not a tiled path.
        var shift = new Vector3(Jitter(rng, 0.1f), 0f, Jitter(rng, 0.1f)) * CellSize;
        plate.SetPositionAndRotation(surface + shift + Vector3.up * 0.03f, Quaternion.Euler(Jitter(rng, 2f), Jitter(rng, 20f), Jitter(rng, 2f)));
        float w = CellSize * (0.7f + 0.2f * (float)rng.NextDouble()), d = CellSize * (0.66f + 0.22f * (float)rng.NextDouble());
        var rim = PlatePart(plate, PrimitiveType.Cube, Vector3.zero, new Vector3(w, 0.05f, d));
        var face = PlatePart(plate, PrimitiveType.Cube, Vector3.up * 0.02f, new Vector3(w - 0.12f, 0.03f, d - 0.12f));
        PaintMetal(rim.GetComponent<Renderer>(), color * 0.8f, x, y, rusted);
        PaintMetal(face.GetComponent<Renderer>(), color, x, y, rusted);
        foreach (var (cx, cz) in new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) })
        {
            var rivet = PlatePart(plate, PrimitiveType.Sphere, new Vector3(cx * (w / 2f - 0.07f), 0.03f, cz * (d / 2f - 0.07f)), new Vector3(0.07f, 0.035f, 0.07f));
            PaintMetal(rivet.GetComponent<Renderer>(), color * 0.65f, x + cx, y + cz, rusted);
        }
    }

    /// <summary>
    /// Hot metal's glow (Cell.Heat, the fire-heat reaction): 0 cold to 1 just heated, from dull red to
    /// orange, on the plate's own material through its emission (no new objects). Rusted plates glow too.
    /// </summary>
    public void Glow(GameObject dressing, float heat)
    {
        if (dressing == null) return;
        var glow = heat <= 0f ? Color.black : Color.Lerp(new Color(0.5f, 0.05f, 0.02f), new Color(1f, 0.3f, 0.05f), heat) * (0.5f + 0.5f * heat * heat);
        foreach (var r in dressing.GetComponentsInChildren<Renderer>())
        {
            r.GetPropertyBlock(tint);
            tint.SetColor(EmissionColorId, glow);
            r.SetPropertyBlock(tint);
        }
    }

    static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    // A part of a plate, placed and sized in the plate's own (turned) frame.
    static GameObject PlatePart(Transform plate, PrimitiveType type, Vector3 local, Vector3 scale)
    {
        var go = Primitives.Visual(type);
        go.transform.SetParent(plate, false);
        go.transform.localPosition = local;
        go.transform.localScale = scale;
        return go;
    }

    // Worn metal: one shared metallic material (another, rough and barely metallic, for rust), each
    // part's shade (some rust in it) per cell. Both take hot metal's glow (Glow): emission on, black.
    void PaintMetal(Renderer r, Color color, int x, int y, bool rusted = false)
    {
        var m = rusted ? materials.Metal("Rusted plate", Color.white, 0.12f, 0.2f) : materials.Metal("Plate", Color.white, 0.45f, 0.75f);
        if (!m.IsKeywordEnabled("_EMISSION")) UrpMaterials.Configure(m, Color.white, null, Color.black);
        r.sharedMaterial = m;
        float n = Mathf.PerlinNoise(x * 0.61f + 0.3f, y * 0.61f + 0.7f);
        UrpMaterials.SetColor(r, tint, Color.Lerp(color, Rusted, (rusted ? 0.6f : 0.35f) * n) * (0.85f + 0.3f * n));
    }

    // A raised wall: a rock model per square, big enough to cover the square's face from below the
    // wall's foot to just over its top (the terrain's own steep sides stretch their texture), and
    // now and then a smaller stone on top, each turned and sized per cell so a run of them reads as
    // one rocky ridge. Rough shapes stand in without the terrain pack's rocks.
    void DressWall(Transform root, Vector3 surface, int steps, int x, int y)
    {
        var rocks = grid.wallRocks;
        if (rocks == null || rocks.Length == 0 || rocks[0] == null)
        {
            DressWallShapes(root, surface, Look(TerrainIds.Rock).color, x, y);
            return;
        }
        var rng = new System.Random(x * 15485863 + y * 32452843);
        float tall = Mathf.Max(1, steps) * grid.stepHeight;
        var foot = new Vector3(surface.x + Jitter(rng, 0.08f) * CellSize, surface.y - tall - 0.25f, surface.z + Jitter(rng, 0.08f) * CellSize);
        Rock(root, rocks[rng.Next(rocks.Length)], foot, CellSize * (1.3f + Jitter(rng, 0.08f)), tall + 0.25f + 0.3f * (float)rng.NextDouble(), rng);
        var stones = grid.wallStones;
        if (stones == null || stones.Length == 0 || rng.NextDouble() < 0.45) return;
        var top = surface + new Vector3(Jitter(rng, 0.25f), 0f, Jitter(rng, 0.25f)) * CellSize + Vector3.up * 0.05f;
        Rock(root, stones[rng.Next(stones.Length)], top, CellSize * (0.5f + 0.2f * (float)rng.NextDouble()), 0.3f + 0.25f * (float)rng.NextDouble(), rng);
    }

    // A rock model stood on <paramref name="foot"/>, turned about the vertical, scaled to about
    // <paramref name="width"/> across and <paramref name="height"/> high.
    static void Rock(Transform root, GameObject prefab, Vector3 foot, float width, float height, System.Random rng)
    {
        if (prefab == null) return;
        var b = BoundsOf(prefab);
        var rock = Object.Instantiate(prefab, root);
        var turn = Quaternion.Euler(0f, rng.Next(360), 0f);
        // The bounds are as the prefab stands (its root's own scale in them), so the root's scale is multiplied, not replaced.
        var fit = new Vector3(width / Mathf.Max(0.01f, b.size.x), height / Mathf.Max(0.01f, b.size.y), width / Mathf.Max(0.01f, b.size.z));
        rock.transform.localScale = Vector3.Scale(prefab.transform.localScale, fit);
        rock.transform.SetPositionAndRotation(foot - turn * Vector3.Scale(new Vector3(b.center.x, b.min.y, b.center.z), fit), turn);
    }

    static Bounds BoundsOf(GameObject prefab)
    {
        if (RockBounds.TryGetValue(prefab, out var known)) return known;
        var b = new Bounds();
        bool first = true;
        foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>())
        {
            if (mf.sharedMesh == null) continue;
            var m = mf.sharedMesh.bounds;
            var w = new Bounds(mf.transform.TransformPoint(m.center), Vector3.Scale(m.size, mf.transform.lossyScale));
            if (first) b = w;
            else b.Encapsulate(w);
            first = false;
        }
        if (first) b = new Bounds(Vector3.up * 0.5f, Vector3.one);
        RockBounds[prefab] = b;
        return b;
    }

    // Without the pack's rocks: rough crags and boulders over the wall's top and down its sides.
    void DressWallShapes(Transform root, Vector3 surface, Color color, int x, int y)
    {
        var rng = new System.Random(x * 15485863 + y * 32452843);
        // Weathered grey, darker than the rock's own colour, so the crags read as stone and not as pale boxes.
        color = Color.Lerp(color, new Color(0.36f, 0.35f, 0.33f), 0.6f);
        int count = 4 + rng.Next(3);
        for (int i = 0; i < count; i++)
        {
            var off = new Vector3(Jitter(rng, 0.62f), 0f, Jitter(rng, 0.62f)) * CellSize;
            // Toward the cell's edge a crag sits lower, over the slope.
            float edge = Mathf.Max(Mathf.Abs(off.x), Mathf.Abs(off.z)) / (CellSize * 0.62f);
            float size = 0.4f + 0.55f * (float)rng.NextDouble();
            var shape = rng.NextDouble() < 0.6 ? PrimitiveType.Sphere : PrimitiveType.Cube;
            // Half sunk into the wall's top, lower still toward its edge.
            var at = surface + off + Vector3.up * (size * 0.05f - 0.45f * edge * edge);
            var crag = Part(root, shape, at, new Vector3(size, size * (0.5f + 0.4f * (float)rng.NextDouble()), size * (0.7f + 0.5f * (float)rng.NextDouble())), "Stone", color);
            crag.transform.rotation = Quaternion.Euler(Jitter(rng, 30f), rng.Next(360), Jitter(rng, 30f));
            Paint(crag.GetComponent<Renderer>(), "Stone", color * (0.8f + 0.25f * (float)rng.NextDouble()), 0.1f, x + i * 11, y);
        }
    }

    // A fallen tree: the valley's own tree that stood here (ValleyChemistry.FelledTree), laid on
    // its side with its torn-up root ball at its foot, fallen a different way per cell; a trunk,
    // a root ball and a few clumps of leaves without one.
    void DressDeadfall(Transform root, Vector3 surface, System.Random rng, Color color, int x, int y)
    {
        var fall = Quaternion.Euler(0f, rng.Next(360), 0f);
        var roots = Part(root, PrimitiveType.Sphere, surface + Vector3.up * 0.35f, new Vector3(1.1f, 0.9f, 0.45f), "Roots", color * 0.7f);
        roots.transform.rotation = fall;
        Paint(roots.GetComponent<Renderer>(), "Roots", color * 0.7f, 0.05f, x, y);
        if (grid.valley != null && grid.valley.FelledTree(x, y, out var prefab, out var scale))
        {
            var tree = Object.Instantiate(prefab, root);
            tree.name = "Fallen " + prefab.name;
            // Tipped over toward its fall, lying a little above the ground on its branches.
            tree.transform.SetPositionAndRotation(surface + Vector3.up * 0.3f, fall * Quaternion.Euler(86f, 0f, 0f));
            tree.transform.localScale = Vector3.Scale(prefab.transform.localScale, scale);
            Untip(tree);
            return;
        }
        var along = fall * Vector3.forward;
        var trunk = Part(root, PrimitiveType.Cylinder, surface + Vector3.up * 0.25f + along * 2.5f, new Vector3(0.4f, 2.5f, 0.4f), "Bark", color);
        trunk.transform.rotation = Quaternion.FromToRotation(Vector3.up, along);
        Paint(trunk.GetComponent<Renderer>(), "Bark", color, 0.1f, x, y);
        var leaves = new Color(0.2f, 0.34f, 0.14f);
        for (int i = 0; i < 3; i++)
        {
            float r = 0.9f + 0.4f * (float)rng.NextDouble();
            var clump = Part(root, PrimitiveType.Sphere, surface + along * (3.5f + i * 0.8f) + Vector3.up * (r * 0.3f), new Vector3(r * 1.3f, r * 0.6f, r), "Leaves", leaves);
            Paint(clump.GetComponent<Renderer>(), "Leaves", leaves, 0.1f, x + i * 13, y);
        }
    }

    // A tree model on its side: no colliders, and not the far LOD (a picture of a standing tree,
    // which would stand up again in the distance).
    static void Untip(GameObject tree)
    {
        foreach (var col in tree.GetComponentsInChildren<Collider>()) Object.Destroy(col);
        if (!tree.TryGetComponent<LODGroup>(out var lod) || lod.lodCount < 2) return;
        var lods = lod.GetLODs();
        foreach (var r in lods[lods.Length - 1].renderers)
            if (r != null) r.enabled = false;
        var kept = new LOD[lods.Length - 1];
        System.Array.Copy(lods, kept, kept.Length);
        kept[kept.Length - 1].screenRelativeTransitionHeight = 0.01f;
        lod.SetLODs(kept);
    }

    // A random value in -range..range.
    static float Jitter(System.Random rng, float range) => ((float)rng.NextDouble() * 2f - 1f) * range;
}
