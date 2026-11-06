using System.Collections.Generic;
using Chem;
using UnityEngine;

// The Hold's barricades (docs/design/the-hold.md, phase 4), on every machine, from the cells: crossed stakes on each barricade cell,
// one combined mesh for the sound ones and one for those burning, charred, and gone with a burst of splinters when a raider breaks
// them. A wizard walks into them as into a built wall. Fire on them is the cells' own. The meshes are made again only when a cell
// changes. Only the show: the core raises and breaks them.
[RequireComponent(typeof(ChemGrid))]
public class HoldBarricades : MonoBehaviour
{
    const float Tall = 1.7f, Thick = 0.17f, Lean = 24f, Spread = 0.26f;
    const int StakesACell = 3;
    static readonly Color Wood = new(0.46f, 0.33f, 0.2f), Burnt = new(0.09f, 0.07f, 0.06f);

    sealed class Batch
    {
        public readonly List<CombineInstance> stakes = new();
        public int shown = -1;
        public Mesh mesh;
        public MeshRenderer renderer;
    }

    ChemGrid grid;
    World shownFor;
    Mesh stake;
    readonly Batch sound = new(), burning = new();
    int lookedAt = -1;
    readonly HashSet<int> standing = new();
    readonly HashSet<int> now = new();

    /// <summary>The cells drawn as barricades now, and those of them charred: for the probe.</summary>
    public int Drawn => Shown(sound) + Shown(burning);
    public int Charred => Shown(burning);

    static int Shown(Batch b) => b.renderer != null && b.renderer.enabled && b.mesh.vertexCount > 0 ? b.stakes.Count / StakesACell : 0;

    void Awake() => grid = GetComponent<ChemGrid>();

    void OnDestroy()
    {
        foreach (var b in new[] { sound, burning })
        {
            if (b.renderer != null) Destroy(b.renderer.gameObject);
            if (b.mesh != null) Destroy(b.mesh);
        }
    }

    void Update()
    {
        var world = grid.World;
        if (world?.HoldSite == null) return;
        if (world != shownFor)
        {
            shownFor = world;
            lookedAt = -1;
            standing.Clear();
        }
        if (world.Tick == lookedAt || world.TickInProgress) return;
        lookedAt = world.Tick;
        Look(world);
    }

    void Look(World world)
    {
        var rows = world.Rules.Sim.Hold;
        now.Clear();
        sound.stakes.Clear();
        burning.stakes.Clear();
        if (stake == null) Make();
        foreach (var (px, py) in world.HoldSite.Barricades)
            for (int y = py - rows.BarricadeRadius; y <= py + rows.BarricadeRadius; y++)
                for (int x = px - rows.BarricadeRadius; x <= px + rows.BarricadeRadius; x++)
                    if (world.InBounds(x, y) && world.At(x, y).T == rows.BarricadeTerrain && now.Add(world.Idx(x, y))) Stakes(x, y, world.At(x, y).Fire > 0 ? burning : sound);
        foreach (int cell in now)
            if (!standing.Contains(cell)) Wall(world, cell, Tall);
        foreach (int cell in standing)
            if (!now.Contains(cell)) Fell(world, cell);
        standing.Clear();
        standing.UnionWith(now);
        Show(sound);
        Show(burning);
    }

    void Wall(World world, int cell, float tall) => grid.BuiltWall(cell % world.W, cell / world.W, tall);

    void Fell(World world, int cell)
    {
        Wall(world, cell, 0f);
        int x = cell % world.W, y = cell / world.W;
        if (world.At(x, y).Fire > 0) return;
        var at = grid.CellCenter(x, y);
        at.y = grid.GroundAt(at) + 0.5f;
        ShardBurst.At(at, Wood, 10);
    }

    void Stakes(int x, int y, Batch into)
    {
        var at = grid.CellCenter(x, y);
        at.y = grid.GroundAt(at);
        uint seed = (uint)(x * 73856093) ^ (uint)(y * 19349663);
        for (int i = 0; i < StakesACell; i++)
        {
            seed = seed * 1664525u + 1013904223u;
            float yaw = (seed >> 8) % 360, along = ((seed >> 16) % 100 / 100f - 0.5f) * 2f * Spread;
            float lean = (i % 2 == 0 ? Lean : -Lean) + (seed >> 24) % 10;
            var turn = Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(0f, 0f, lean);
            var foot = at + Quaternion.Euler(0f, yaw, 0f) * new Vector3(along, 0f, 0f);
            into.stakes.Add(new CombineInstance { mesh = stake, transform = Matrix4x4.TRS(foot + turn * Vector3.up * (Tall / 2f), turn, new Vector3(Thick, Tall, Thick)) });
        }
    }

    void Show(Batch b)
    {
        b.renderer.enabled = b.stakes.Count > 0;
        if (b.stakes.Count == b.shown) return;
        b.shown = b.stakes.Count;
        b.mesh.Clear();
        if (b.stakes.Count > 0) b.mesh.CombineMeshes(b.stakes.ToArray(), true, true);
    }

    void Make()
    {
        var cube = Primitives.Visual(PrimitiveType.Cube, "Barricade stake");
        stake = cube.GetComponent<MeshFilter>().sharedMesh;
        Destroy(cube);
        Dress(sound, "Barricade", Wood);
        Dress(burning, "Barricade burnt", Burnt);
    }

    // Not under the grid: a reset takes everything there.
    static void Dress(Batch b, string name, Color color)
    {
        var go = new GameObject(name);
        b.mesh = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        go.AddComponent<MeshFilter>().sharedMesh = b.mesh;
        b.renderer = go.AddComponent<MeshRenderer>();
        b.renderer.sharedMaterial = UrpMaterials.NewLit(name, color, 0.1f);
    }
}
