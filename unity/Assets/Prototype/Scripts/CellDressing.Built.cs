using UnityEngine;

// The shaped parts of a cell's dressing: a bridge's plank deck, an iron plate
// lying on the ground, the crags of a raised wall. Each is seeded from its
// cell, so neighbours differ (turned, sized and shaded apart) and a strip of
// them reads as separate things, not a row of tiles.
public sealed partial class CellDressing
{
    static readonly Color Iron = new(0.30f, 0.29f, 0.28f);
    static readonly Color Bronze = new(0.45f, 0.32f, 0.18f);

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
    // raised face on a thicker rim, and a rivet at each corner.
    void DressPlate(Transform root, Vector3 surface, System.Random rng, int x, int y)
    {
        var color = rng.NextDouble() < 0.25 ? Bronze : Iron;
        var plate = new GameObject("Plate").transform;
        plate.SetParent(root, false);
        // Shifted off the cell's centre too, so a run of plates is a scatter, not a tiled path.
        var shift = new Vector3(Jitter(rng, 0.1f), 0f, Jitter(rng, 0.1f)) * CellSize;
        plate.SetPositionAndRotation(surface + shift + Vector3.up * 0.03f, Quaternion.Euler(Jitter(rng, 2f), Jitter(rng, 20f), Jitter(rng, 2f)));
        float w = CellSize * (0.7f + 0.2f * (float)rng.NextDouble()), d = CellSize * (0.66f + 0.22f * (float)rng.NextDouble());
        var rim = PlatePart(plate, PrimitiveType.Cube, Vector3.zero, new Vector3(w, 0.05f, d));
        var face = PlatePart(plate, PrimitiveType.Cube, Vector3.up * 0.02f, new Vector3(w - 0.12f, 0.03f, d - 0.12f));
        PaintMetal(rim.GetComponent<Renderer>(), color * 0.8f, x, y);
        PaintMetal(face.GetComponent<Renderer>(), color, x, y);
        foreach (var (cx, cz) in new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) })
        {
            var rivet = PlatePart(plate, PrimitiveType.Sphere, new Vector3(cx * (w / 2f - 0.07f), 0.03f, cz * (d / 2f - 0.07f)), new Vector3(0.07f, 0.035f, 0.07f));
            PaintMetal(rivet.GetComponent<Renderer>(), color * 0.65f, x + cx, y + cz);
        }
    }

    // A part of a plate, placed and sized in the plate's own (turned) frame.
    static GameObject PlatePart(Transform plate, PrimitiveType type, Vector3 local, Vector3 scale)
    {
        var go = Primitives.Visual(type);
        go.transform.SetParent(plate, false);
        go.transform.localPosition = local;
        go.transform.localScale = scale;
        return go;
    }

    // Worn metal: one shared metallic material, each part's shade (some rust in it) per cell.
    void PaintMetal(Renderer r, Color color, int x, int y)
    {
        r.sharedMaterial = materials.Metal("Plate", Color.white, 0.45f, 0.75f);
        float n = Mathf.PerlinNoise(x * 0.61f + 0.3f, y * 0.61f + 0.7f);
        var rust = new Color(0.42f, 0.22f, 0.12f);
        UrpMaterials.SetColor(r, tint, Color.Lerp(color, rust, 0.35f * n) * (0.85f + 0.3f * n));
    }

    // A raised wall: rough crags and boulders over its top and down its sides, placed, sized and
    // turned per cell, so a run of wall cells reads as one rocky ridge.
    void DressWall(Transform root, Vector3 surface, Color color, int x, int y)
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

    // A random value in -range..range.
    static float Jitter(System.Random rng, float range) => ((float)rng.NextDouble() * 2f - 1f) * range;
}
