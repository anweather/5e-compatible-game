using System.Collections.Generic;
using Chem;
using UnityEngine;

// A building's window glass: one small mesh of quads (Prototype/WindowGlass, a
// see-through tint with the sky's reflection), so the lit windows behind glow
// through it. Each pane stands on a `pane` cell (world/villages.json, a
// cabin's map cell): when the cell shatters (a blast, the ground shaken, its
// wall gone) the quad turns to the dark of an open gap, in place, with a
// spray of shards (ShardBurst) and the break sound. A storey above stands on
// the same cell and goes with it. A heavy thing that hits hard (PhysicsProp)
// asks the core to shatter the pane nearest it (Struck, through ChemEffects as
// a prop's blast does: the host decides, every machine applies it); the thing
// reports the hit because a building's colliders are parts with no Rigidbody,
// which never pass a collision up to this component. ValleyChemistry binds it
// to the cells for each new world. Only the show: the cells are the truth.
[ExecuteAlways]
public class WindowPanes : MonoBehaviour
{
    [System.Serializable]
    public struct Pane
    {
        /// <summary>Its middle, in the building's space.</summary>
        public Vector3 at;
        /// <summary>Half its width and half its height, as directions.</summary>
        public Vector3 along, up;
        /// <summary>The way it faces.</summary>
        public Vector3 facing;
    }

    // The tint and clearness of glass (alpha: how much it hides), and of the gap a shattered pane leaves.
    static readonly Color32 Glass = new Color(0.62f, 0.76f, 0.82f, 0.22f), Gap = new Color(0.025f, 0.022f, 0.02f, 1f);
    static readonly Color ShardTint = new(0.78f, 0.9f, 0.96f);
    // How hard a thing's hit (N s) breaks the nearest pane, and how near it must be (m). Measured: a
    // crate at 2.5 m/s hits at about 70, at 8 m/s about 165 (a nudge leaves the glass, a hard shove breaks it).
    const float HeavyImpulse = 120f, Reach = 2.2f;
    const int ShardsPerPane = 24;

    public Pane[] panes = System.Array.Empty<Pane>();
    public Material material;

    Mesh mesh;
    GameObject shown;
    Color32[] colors;
    // Each pane's cell (a world index), or -1 where no pane cell stands, and whether it's a gap now. The
    // mesh may come after the binding (a new world can load before a house has drawn itself).
    int[] cellOf = System.Array.Empty<int>();
    bool[] open = System.Array.Empty<bool>();

    void OnEnable() => Show();

    void OnDisable() => Hide();

    /// <summary>Draws these panes (a VillageHouse's, from its plan).</summary>
    public void Show(Pane[] made, Material glass)
    {
        panes = made;
        if (glass != null) material = glass;
        Show();
    }

    void Show()
    {
        Hide();
        if (panes == null || panes.Length == 0) return;
        var used = material != null ? material : new Material(Shader.Find("Prototype/WindowGlass"));
        mesh = Quads();
        shown = new GameObject("Panes") { hideFlags = HideFlags.DontSave | HideFlags.NotEditable };
        shown.transform.SetParent(transform, false);
        shown.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = shown.AddComponent<MeshRenderer>();
        r.sharedMaterial = used;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    /// <summary>Stops drawing them.</summary>
    public void Hide()
    {
        DestroyNow(shown);
        DestroyNow(mesh);
        shown = null;
        mesh = null;
    }

    Mesh Quads()
    {
        int n = panes.Length;
        var vertices = new Vector3[n * 4];
        var normals = new Vector3[n * 4];
        var triangles = new int[n * 6];
        colors = new Color32[n * 4];
        for (int i = 0; i < n; i++)
        {
            var p = panes[i];
            int v = i * 4, t = i * 6;
            vertices[v] = p.at - p.along - p.up;
            vertices[v + 1] = p.at + p.along - p.up;
            vertices[v + 2] = p.at + p.along + p.up;
            vertices[v + 3] = p.at - p.along + p.up;
            for (int k = 0; k < 4; k++)
            {
                normals[v + k] = p.facing;
                colors[v + k] = i < open.Length && open[i] ? Gap : Glass;
            }
            int[] corners = { v, v + 1, v + 2, v, v + 2, v + 3 };
            System.Array.Copy(corners, 0, triangles, t, 6);
        }
        var m = new Mesh { name = "Window panes", hideFlags = HideFlags.DontSave, vertices = vertices, normals = normals, triangles = triangles, colors32 = colors };
        return m;
    }

    /// <summary>
    /// Ties each pane to the pane cell it stands on and adds them to <paramref name="byCell"/>, for a new
    /// world: all glass again, but for those whose cell is already open (a saved game's).
    /// </summary>
    public void Bind(ChemGrid chemGrid, Dictionary<int, WindowPanes> byCell)
    {
        var grid = chemGrid;
        var world = grid.World;
        cellOf = new int[panes.Length];
        open = new bool[panes.Length];
        for (int i = 0; i < panes.Length; i++)
        {
            var at = grid.CellAt(transform.TransformPoint(panes[i].at));
            int cell = PaneCellAround(world, at);
            cellOf[i] = cell;
            if (cell >= 0) byCell[cell] = this;
            else open[i] = world.InBounds(at.x, at.y) && !world.IsBlocking(world.At(at.x, at.y));
        }
        Repaint();
    }

    // The pane cell a pane stands on: its own, or beside it (the wall's line runs near a cell's edge).
    static int PaneCellAround(World world, Vector2Int at)
    {
        for (int dy = 0; dy <= 2; dy++)
            for (int dx = 0; dx <= 2; dx++)
            {
                int x = at.x + (dx + 1) % 3 - 1, y = at.y + (dy + 1) % 3 - 1;
                if (world.InBounds(x, y) && world.At(x, y).T == TerrainIds.Pane) return world.Idx(x, y);
            }
        return -1;
    }

    /// <summary>A pane cell shattered: its panes (a column, on a taller building) open, with shards and the break sound.</summary>
    public void Shatter(int cell)
    {
        bool sounded = false;
        for (int i = 0; i < cellOf.Length; i++)
        {
            if (cellOf[i] != cell) continue;
            open[i] = true;
            var at = transform.TransformPoint(panes[i].at);
            ShardBurst.At(at, ShardTint, ShardsPerPane);
            if (!sounded) GameSounds.Play(SoundIds.Break, at);
            sounded = true;
        }
        Repaint();
    }

    void Repaint()
    {
        if (mesh == null) return;
        for (int i = 0; i < open.Length; i++)
            for (int k = 0; k < 4; k++) colors[i * 4 + k] = open[i] ? Gap : Glass;
        mesh.colors32 = colors;
    }

    /// <summary>The hardest hit by a thing so far (N s), for the probe's measure of HeavyImpulse.</summary>
    public static float HardestHit { get; set; }

    /// <summary>
    /// A thing hit something: if it hit hard, the pane cell nearest the hit, if one is near, shatters.
    /// Only the sideways part of the hit counts, so a thing landing on the ground beside a wall doesn't.
    /// </summary>
    public static void Struck(ChemGrid grid, Collision hit)
    {
        float impulse = Vector3.ProjectOnPlane(hit.impulse, Vector3.up).magnitude;
        HardestHit = Mathf.Max(HardestHit, impulse);
        if (impulse < HeavyImpulse || hit.contactCount == 0 || grid == null || grid.World == null) return;
        var world = grid.World;
        var point = hit.GetContact(0).point;
        var at = grid.CellAt(point);
        int reach = Mathf.CeilToInt(Reach / grid.cellSize), best = -1;
        float nearest = Reach * Reach;
        for (int y = at.y - reach; y <= at.y + reach; y++)
            for (int x = at.x - reach; x <= at.x + reach; x++)
            {
                if (!world.InBounds(x, y) || world.At(x, y).T != TerrainIds.Pane) continue;
                var c = grid.CellCenter(x, y);
                float d = new Vector2(c.x - point.x, c.z - point.z).sqrMagnitude;
                if (d >= nearest) continue;
                nearest = d;
                best = world.Idx(x, y);
            }
        if (best >= 0) ChemEffects.Request(grid, new EffectCommand(EffectKind.Shatter, best % world.W, best / world.W, "", ThingId.None));
    }

    static void DestroyNow(Object o)
    {
        if (o == null) return;
        if (Application.isPlaying) Destroy(o);
        else DestroyImmediate(o);
    }
}
