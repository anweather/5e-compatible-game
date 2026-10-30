using UnityEngine;

// The dressings the new casts brought: a boulder (Rolling Boulder, rolling and
// at rest), a coil and the gate it powers (machines.json), a log wall worn thin
// by Sand Blast, and metal going over to rust, a step at a time. Simple pieces.
public sealed partial class CellDressing
{
    const float GateTall = 2.3f, GateRise = 2f, GateBarWidth = 0.09f, WornWallTall = 1.3f;
    static readonly Color Copper = new(0.62f, 0.36f, 0.18f);
    static readonly Color WornLog = new(0.52f, 0.42f, 0.3f);
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    /// <summary>A boulder round <paramref name="parent"/>'s middle, about <paramref name="size"/> across: a rock model, or a lump of spheres without the pack.</summary>
    public void Boulder(Transform parent, float size)
    {
        var rocks = grid.wallRocks;
        var rng = new System.Random(parent.GetInstanceID());
        if (rocks != null && rocks.Length > 0 && rocks[0] != null)
        {
            Rock(parent, rocks[rng.Next(rocks.Length)], parent.position + Vector3.down * (size / 2f), size, size, rng);
            return;
        }
        var color = new Color(0.42f, 0.4f, 0.37f);
        Part(parent, PrimitiveType.Sphere, parent.position, Vector3.one * size, "Stone", color);
        for (int i = 0; i < 5; i++)
        {
            var lump = Part(parent, PrimitiveType.Sphere, parent.position + Random.onUnitSphere * (size * 0.3f), Vector3.one * (size * 0.55f), "Stone", color * 0.9f);
            lump.transform.rotation = Random.rotationUniform;
        }
    }

    // A boulder that came to rest: half sunk on the cell's ground.
    void DressBoulder(Transform root, Vector3 surface, System.Random rng)
    {
        var middle = new GameObject("Boulder").transform;
        middle.SetParent(root, false);
        middle.SetPositionAndRotation(surface + Vector3.up * (CellSize * 0.42f), Quaternion.Euler(Jitter(rng, 12f), rng.Next(360), Jitter(rng, 12f)));
        Boulder(middle, CellSize);
    }

    // Rock a step up with water beside it: Wall of Stone over water.
    bool SteppingStone(Chem.Cell c, int x, int y)
    {
        if (c.T != TerrainIds.Rock || c.Height != 1) return false;
        var world = grid.World;
        for (int d = 0; d < 4; d++)
        {
            var side = world.InBounds(x + Chem.World.DirX(d), y + Chem.World.DirY(d)) ? world.At(x + Chem.World.DirX(d), y + Chem.World.DirY(d)) : null;
            if (side != null && side.Water > 0) return true;
        }
        return false;
    }

    // A broad, flat stone a step out of the water; rough without the pack's stones.
    void DressSteppingStone(Transform root, Vector3 surface, int x, int y)
    {
        var rng = new System.Random(x * 15485863 + y * 32452843);
        var at = surface + new Vector3(Jitter(rng, 0.1f), -0.45f, Jitter(rng, 0.1f)) * CellSize;
        var stones = grid.wallRocks;
        if (stones != null && stones.Length > 0 && stones[0] != null)
        {
            Rock(root, stones[rng.Next(stones.Length)], at, CellSize * 0.98f, 0.45f * CellSize + 0.06f, rng);
            return;
        }
        var slab = Part(root, PrimitiveType.Cylinder, surface, new Vector3(CellSize * 0.85f, 0.25f, CellSize * 0.85f), "Stone", Look(TerrainIds.Rock).color);
        Paint(slab.GetComponent<Renderer>(), "Stone", Look(TerrainIds.Rock).color, 0.1f, x, y);
    }

    // A lightning coil: a bronze post wound with copper on a plate, a knob on top.
    void DressCoil(Transform root, Vector3 surface, int x, int y)
    {
        var foot = Part(root, PrimitiveType.Cube, surface + Vector3.up * 0.05f, new Vector3(CellSize * 0.7f, 0.1f, CellSize * 0.7f), "Coil base", Iron);
        Paint(foot.GetComponent<Renderer>(), "Coil base", Iron, 0.3f, x, y);
        Part(root, PrimitiveType.Cylinder, surface + Vector3.up * 0.8f, new Vector3(0.14f, 0.75f, 0.14f), "Coil post", Bronze, 0.5f);
        for (int i = 0; i < 7; i++)
            Part(root, PrimitiveType.Cylinder, surface + Vector3.up * (0.3f + i * 0.17f), new Vector3(0.52f - i * 0.025f, 0.05f, 0.52f - i * 0.025f), "Coil wire", Copper, 0.6f);
        Part(root, PrimitiveType.Sphere, surface + Vector3.up * 1.6f, Vector3.one * 0.3f, "Coil post", Bronze, 0.5f);
    }

    // A gate: iron bars and rails across the cell, its way along a run of gates. Opening, the bars rise through the lintel (DressingSlide).
    void DressGate(Transform root, Vector3 surface, int x, int y, bool open)
    {
        var world = grid.World;
        bool alongZ = (world.InBounds(x, y + 1) && IsGate(world.At(x, y + 1))) || (world.InBounds(x, y - 1) && IsGate(world.At(x, y - 1)));
        var across = alongZ ? Vector3.forward : Vector3.right;
        var lintel = Part(root, PrimitiveType.Cube, surface + Vector3.up * (GateTall + 0.15f), Vector3.one, "Gate", Iron * 0.8f);
        lintel.transform.localScale = alongZ ? new Vector3(0.34f, 0.3f, CellSize) : new Vector3(CellSize, 0.3f, 0.34f);
        var bars = new GameObject("Bars").transform;
        bars.SetParent(root, false);
        bars.position = surface;
        const int Count = 5;
        for (int i = 0; i < Count; i++)
        {
            var at = surface + across * (((i + 0.5f) / Count - 0.5f) * CellSize) + Vector3.up * (GateTall / 2f);
            Part(bars, PrimitiveType.Cylinder, at, new Vector3(GateBarWidth, GateTall / 2f, GateBarWidth), "Gate", Iron);
        }
        foreach (float height in new[] { 0.5f, 1.3f, 2.1f })
        {
            var rail = Part(bars, PrimitiveType.Cube, surface + Vector3.up * height, Vector3.one, "Gate", Iron * 0.8f);
            rail.transform.localScale = alongZ ? new Vector3(0.08f, 0.14f, CellSize) : new Vector3(CellSize, 0.14f, 0.08f);
        }
        DressingSlide.Start(bars, open ? Vector3.zero : Vector3.up * GateRise, open ? Vector3.up * GateRise : Vector3.zero);
    }

    static bool IsGate(Chem.Cell c) => c != null && (c.T == TerrainIds.Gate || c.T == TerrainIds.GateOpen);

    // A log wall worn thin: a few pale, gapped logs on their posts, the way the wall runs.
    void DressWornLogs(Transform root, Vector3 surface, System.Random rng, int x, int y)
    {
        var world = grid.World;
        bool alongZ = (world.InBounds(x, y + 1) && IsLogs(world.At(x, y + 1))) || (world.InBounds(x, y - 1) && IsLogs(world.At(x, y - 1)));
        var lay = Quaternion.Euler(alongZ ? 90f : 0f, 0f, alongZ ? 0f : 90f);
        foreach (float height in new[] { 0.25f, 0.62f, 1.1f })
        {
            float girth = 0.13f + 0.05f * (float)rng.NextDouble();
            var log = Part(root, PrimitiveType.Cylinder, surface + Vector3.up * height, new Vector3(girth, CellSize * 0.52f, girth), "Worn logs", WornLog);
            log.transform.rotation = lay * Quaternion.Euler(0f, rng.Next(360), 0f);
            Paint(log.GetComponent<Renderer>(), "Worn logs", WornLog, 0.1f, x + (int)(height * 10f), y);
        }
        var post = Part(root, PrimitiveType.Cylinder, surface + Vector3.up * (WornWallTall / 2f), new Vector3(0.16f, WornWallTall / 2f, 0.16f), "Worn logs", WornLog);
        Paint(post.GetComponent<Renderer>(), "Worn logs", WornLog * 0.75f, 0.1f, x, y + 3);
    }

    static bool IsLogs(Chem.Cell c) => c != null && (c.T == TerrainIds.LogWall || c.T == TerrainIds.WornLogWall);

    /// <summary>A plate a step further to rust (Cell.Rust on the way to rust.every): its parts' shades go that much of the way to rust.</summary>
    public void Tarnish(GameObject dressing, float amount)
    {
        if (dressing == null) return;
        foreach (var r in dressing.GetComponentsInChildren<Renderer>())
        {
            r.GetPropertyBlock(tint);
            tint.SetColor(BaseColorId, Color.Lerp(tint.GetColor(BaseColorId), Rusted, amount));
            r.SetPropertyBlock(tint);
        }
    }
}
