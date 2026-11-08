using System.Collections.Generic;
using System.Linq;
using Chem;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

// Draws the buildings a mod added or edited, from their rows (RowMesher). What the world was made
// with stays as the valley drew it, so a village looks the same until a mod touches it. An edited
// building hides the scene's one and shows it again when reverted; an edited cabin keeps its own,
// since its row is cells and no walls, and only what the mod added is drawn beside it. It follows
// the rules in force, so a switch, a revert, a late join and a save all come the same way, and it
// moves this peer's player off a new wall (players aren't core state). RuleSwitcher adds it.
// A house follows its cells in play (RowRuin): one house is looked at a frame and redrawn if it must
// be, so a burning street spreads over frames, and a house's chimney smokes while it stands. Its
// windows glow at night from their rows' lamps (WindowPanes), and go dark as their walls burn or fall.
[RequireComponent(typeof(ChemGrid))]
public class RowHouses : MonoBehaviour
{
    const int FarthestNudge = 40;
    const float FindHouseMetres = 10f, DefaultWall = 2.7f, FootingMore = 0.5f;

    sealed class Drawn
    {
        public string json;
        public World on;
        public GameObject root;
        public Transform hidden;
        public RowRuin ruin;
        public BuildingRow row;
        public MeshCollider roofCollider, walkCollider, railCollider;
        public Mesh walkMesh, railMesh;
        public WindowPanes panes;
        public RowMesher.Stretch[] paneOn;
        public bool roofBurns, told;
        public System.Func<Vector2Int, RowRuin.CellNow> cells;
        public ParticleSystem[] smoke;
        public List<Vector2Int> walls;
    }

    readonly Dictionary<string, Drawn> drawn = new();
    ChemGrid grid;
    World hooked;
    Rules seen;
    Material material, glass;
    readonly List<Drawn> order = new();
    int nextLook;
    bool closing;
    readonly System.Diagnostics.Stopwatch timer = new();

    /// <summary>How long the longest redraw of a house has taken (ms), and how many redraws so far.</summary>
    public double WorstRedrawMs { get; private set; }
    public int Redraws { get; private set; }

    /// <summary>Each house and its stretches down, for the probe: two peers should say the same.</summary>
    public string Ruins => string.Join(" ", order.Select(d => $"{d.row.Id}:{d.ruin.Down}/{d.ruin.Stretches}"));

    /// <summary>A house's ruin, for the probe; null if it isn't drawn.</summary>
    public RowRuin Ruin(string id) => drawn.TryGetValue(id, out var d) ? d.ruin : null;

    /// <summary>How many buildings are drawn from their rows now.</summary>
    public int Count => drawn.Count;

    /// <summary>A building drawn from its row, for the probe; null if it isn't.</summary>
    public GameObject Root(string id) => drawn.TryGetValue(id, out var d) ? d.root : null;

    /// <summary>The scene's own building this row hid, for the probe; null if none.</summary>
    public Transform Hidden(string id) => drawn.TryGetValue(id, out var d) ? d.hidden : null;

    void Awake() => grid = GetComponent<ChemGrid>();

    void OnDestroy()
    {
        Hook(null);
        // The grid goes with this: it's no time to dress the cells again (a rejoin hit that).
        closing = true;
        foreach (var d in drawn.Values) Remove(d);
        drawn.Clear();
        order.Clear();
    }

    void Update()
    {
        var world = grid.World;
        if (world == null) return;
        if (world != hooked) Hook(world);
        if (world.Rules != seen)
        {
            seen = world.Rules;
            Sync(world);
        }
        LookAtOne();
    }

    // One house a frame: its cells read, and redrawn only if they show something new.
    void LookAtOne()
    {
        if (order.Count == 0) return;
        nextLook = (nextLook + 1) % order.Count;
        var d = order[nextLook];
        if (d.ruin.Stretches == 0) return;
        timer.Restart();
        if (!d.ruin.Look(d.cells, d.roofBurns)) return;
        Redraws++;
        WorstRedrawMs = System.Math.Max(WorstRedrawMs, timer.Elapsed.TotalMilliseconds);
        if (d.roofCollider != null) d.roofCollider.enabled = !d.ruin.RoofHurt;
        if (d.walkCollider != null) d.walkCollider.sharedMesh = d.walkMesh; // cooked again, as it now stands
        if (d.railCollider != null) d.railCollider.sharedMesh = d.railMesh;
        d.panes.Darken(i => d.ruin.Out(d.paneOn[i]));
        foreach (var puff in d.smoke) puff.gameObject.SetActive(!d.ruin.RoofHurt);
        if (d.told || d.ruin.Down * 2 < d.ruin.Stretches) return;
        d.told = true;
        EventLog.Add($"The {d.row.Id} falls");
    }

    RowRuin.CellNow CellOf(BuildingRow row, Vector2Int at)
    {
        var world = hooked;
        int x = row.X + at.x, y = row.Y + at.y;
        if (!world.InBounds(x, y)) return default;
        var c = world.At(x, y);
        return new RowRuin.CellNow { blocking = world.IsBlocking(c), burning = c.Fire > 0, ashen = c.Cover == CoverIds.Ash };
    }

    // The dragon's breath put a chunk back: its walls forget the fire.
    void PutBack(ChunkFresh fresh)
    {
        foreach (var d in order)
            d.ruin.Forget(at => Chunks.ChunkOf(d.row.X + at.x) == fresh.CX && Chunks.ChunkOf(d.row.Y + at.y) == fresh.CY);
    }

    void Hook(World world)
    {
        if (hooked != null) hooked.BuildingsChanged -= Moved;
        hooked = world;
        seen = null;
        if (hooked != null) hooked.BuildingsChanged += Moved;
        grid.ChunkFreshened -= PutBack;
        if (hooked != null) grid.ChunkFreshened += PutBack;
    }

    void Sync(World world)
    {
        var rows = world.RowsDrawn();
        foreach (string id in drawn.Keys.Where(id => rows.All(r => r.Id != id)).ToList())
        {
            Remove(drawn[id]);
            order.Remove(drawn[id]);
            drawn.Remove(id);
            Debug.Log($"Row houses: took down {id}, {drawn.Count} now");
        }
        foreach (var row in rows)
        {
            string json = row.Json.ToString(Formatting.None);
            if (drawn.TryGetValue(row.Id, out var old))
            {
                // A reset took what was drawn with the grid's children, and its blockers with it.
                if (old.json == json && old.on == world) continue;
                Remove(old);
                order.Remove(old);
            }
            var made = Draw(world, row, json);
            drawn[row.Id] = made;
            order.Add(made);
            Debug.Log($"Row houses: drew {row.Id}, {drawn.Count} now");
        }
    }

    Drawn Draw(World world, BuildingRow row, string json)
    {
        FindMaterials();
        var corner = grid.CellCenter(row.X, row.Y) + new Vector3(-grid.cellSize / 2f, 0f, grid.cellSize / 2f);
        var root = new GameObject("Row house " + row.Id);
        root.transform.SetParent(transform, false);
        bool level = (bool?)row.Json["level"] ?? false;
        float rootY = level ? Highest(world, row) : grid.GroundAt(Middle(corner, row));
        root.transform.position = new Vector3(corner.x, rootY, corner.z);
        var tables = new RowMesher.Tables
        {
            OpeningLook = kind => world.Rules.Buildings.Row("openings", kind)?["look"],
            TerrainColor = terrain => terrain == null ? Color.clear : CellDressing.Look(world.Rules, terrain).color,
            Tiled = terrain => terrain != null && CellDressing.Of(world.Rules, terrain)?.Form == "tile",
            Footing = Footing(world, row, root.transform.position.y),
            GroundAt = p => grid.GroundAt(corner + new Vector3(p.x, 0f, p.y)) - rootY,
        };
        var made = RowMesher.Make(row.Json, tables);
        Part(root, "Body", made.body, false);
        var roof = Part(root, "Roof", made.roof, true);
        var walkCollider = Walk(root, made.walk, "Walk", 0);
        var railCollider = Walk(root, made.rail, "Rail", CameraPassesLayer);
        var panes = root.AddComponent<WindowPanes>();
        panes.hollow = true;
        panes.Show(made.panes, glass);
        var valley = FindAnyObjectByType<ValleyChemistry>();
        if (valley != null) valley.BindPanes(panes);
        var laid = world.BuildingCellsOf(row);
        var cells = new HashSet<Vector2Int>(laid.Where(c => c.Part != BuildingPart.Fill).Select(c => new Vector2Int(c.X - row.X, c.Y - row.Y)));
        // What the scene's own box covers (a cabin's cells) keeps it; the rest needs a blocker to meet.
        var walls = laid.Where(c => c.Part != BuildingPart.Built && c.Part != BuildingPart.Road).Select(c => new Vector2Int(c.X, c.Y)).ToList();
        float tall = (float?)row.Json["wall"]?["height"] ?? DefaultWall;
        foreach (var c in walls) grid.BuiltWall(c.x, c.y, level ? TopOver(made, row, c, rootY) : tall);
        GreatTrees(root, row, corner);
        return new Drawn
        {
            json = json,
            on = world,
            root = root,
            hidden = HideBase(world, row, corner),
            row = row,
            ruin = new RowRuin(made, cells.Contains),
            roofCollider = roof,
            walkCollider = walkCollider,
            walkMesh = made.walk,
            railCollider = railCollider,
            railMesh = made.rail,
            panes = panes,
            paneOn = made.paneStretches,
            roofBurns = RoofBurns(world, row),
            cells = at => CellOf(row, at),
            smoke = Smoke(root, made.chimneys),
            walls = walls,
        };
    }

    // A row's great trees (its "great-tree" details, rules/buildings.json): the valley's own tree for that cell, scaled up and
    // stood at a join, a stand-in until a great tree is modelled. The row's cells (its trunk, its roots) are what blocks.
    void GreatTrees(GameObject root, BuildingRow row, Vector3 corner)
    {
        if (!(row.Json["parts"] is JArray parts) || !(row.Json["joins"] is JObject joins)) return;
        var valley = FindAnyObjectByType<ValleyChemistry>();
        foreach (var part in parts.OfType<JObject>().Where(p => (string)p["kind"] == GreatTree))
        {
            if (valley == null || !(joins[(string)part["join"] ?? ""] is JArray j)) continue;
            int jx = (int)j[0], jy = (int)j[1];
            int cx = row.X + Mathf.FloorToInt(jx / 2f), cy = row.Y + Mathf.FloorToInt(jy / 2f);
            if (!valley.TreeModel(cx, cy, out var prefab, out var size)) continue;
            float half = grid.cellSize / 2f;
            var at = corner + new Vector3(jx * half, 0f, -jy * half);
            at.y = grid.GroundAt(at);
            var tree = Instantiate(prefab, at, Quaternion.Euler(0f, (cx * 37 + cy * 11) % 360, 0f), root.transform);
            tree.name = "Great tree";
            tree.transform.localScale = size * ((float?)part["scale"] ?? 7f);
        }
    }

    const string GreatTree = "great-tree";

    // A chimney smokes as a village's does: the same puff, copied from one, at each chimney's top.
    static ParticleSystem[] Smoke(GameObject root, Vector3[] chimneys)
    {
        var from = FindObjectsByType<VillageHouse>(FindObjectsInactive.Include, FindObjectsSortMode.None).Select(h => h.GetComponentInChildren<ParticleSystem>(true)).FirstOrDefault(p => p != null);
        if (from == null) return System.Array.Empty<ParticleSystem>();
        return chimneys.Select(top =>
        {
            var puff = Instantiate(from, root.transform);
            puff.gameObject.SetActive(true);
            puff.transform.localPosition = top;
            return puff;
        }).ToArray();
    }

    // A level row's blocker stops at the wall's top, however far down its cell's ground lies.
    float TopOver(RowMesher.Result made, BuildingRow row, Vector2Int c, float rootY)
    {
        float top = made.tops.TryGetValue(new Vector2Int(c.x - row.X, c.y - row.Y), out float wall) ? wall : RowMesher.WalkTop(row.Json);
        return rootY + top - grid.GroundTop(c.x, c.y, grid.World.At(c.x, c.y));
    }

    // TransparentFX: a wizard meets it, and the follow camera's obstacle check, which looks at Default alone, doesn't.
    const int CameraPassesLayer = 1;

    // Collides, and nothing draws it.
    static MeshCollider Walk(GameObject root, Mesh mesh, string name, int layer)
    {
        if (mesh.vertexCount == 0) return null;
        var go = new GameObject(name) { layer = layer };
        go.transform.SetParent(root.transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var collider = go.AddComponent<MeshCollider>();
        collider.sharedMesh = mesh;
        return collider;
    }

    float Highest(World world, BuildingRow row) =>
        world.BuildingCellsOf(row).Where(c => c.Part != BuildingPart.Road).Max(c => grid.GroundAt(grid.CellCenter(c.X, c.Y)));

    static bool RoofBurns(World world, BuildingRow row) =>
        (row.Json["roofs"] as JObject)?.Properties().Any(r => world.Rules.Terrain.TryGetValue((string)r.Value["cover"] ?? "", out var cover) && cover.Burns) ?? false;

    // How far down the lowest ground under the walls lies from the house's floor, and a little more.
    float Footing(World world, BuildingRow row, float floor)
    {
        float lowest = floor;
        foreach (var c in world.BuildingCellsOf(row).Where(c => c.Part != BuildingPart.Road)) lowest = Mathf.Min(lowest, grid.GroundAt(grid.CellCenter(c.X, c.Y)));
        return floor - lowest + FootingMore;
    }

    static Vector3 Middle(Vector3 corner, BuildingRow row)
    {
        if (!(row.Json["joins"] is JObject joins) || !joins.HasValues) return corner;
        float x = joins.Properties().Average(j => (int)j.Value[0] * 0.75f), z = joins.Properties().Average(j => (int)j.Value[1] * 0.75f);
        return corner + new Vector3(x, 0f, -z);
    }

    MeshCollider Part(GameObject root, string name, Mesh mesh, bool solid)
    {
        var go = new GameObject(name);
        go.transform.SetParent(root.transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = material;
        if (!solid || mesh.vertexCount == 0) return null;
        var collider = go.AddComponent<MeshCollider>();
        collider.sharedMesh = mesh;
        return collider;
    }

    // An edited building with no baked cells replaces the scene's; a new one, or a cabin's, stands beside.
    static Transform HideBase(World world, BuildingRow row, Vector3 corner)
    {
        if (row.Json["built"] != null || !world.IsBaseBuilding(row.Id)) return null;
        var valley = FindAnyObjectByType<ValleyChemistry>();
        if (valley == null || valley.cabins == null) return null;
        var near = Middle(corner, row);
        near.y = 0f;
        Transform house = null;
        float best = FindHouseMetres * FindHouseMetres;
        foreach (var candidate in valley.cabins)
        {
            if (candidate == null) continue;
            var at = candidate.position;
            at.y = 0f;
            float d = (at - near).sqrMagnitude;
            if (d >= best) continue;
            best = d;
            house = candidate;
        }
        if (house != null) house.gameObject.SetActive(false);
        return house;
    }

    void Remove(Drawn d)
    {
        if (!closing)
            foreach (var c in d.walls) grid.BuiltWall(c.x, c.y, 0f);
        if (d.hidden != null) d.hidden.gameObject.SetActive(true);
        var valley = FindAnyObjectByType<ValleyChemistry>();
        if (valley != null && d.panes != null) valley.UnbindPanes(d.panes);
        if (d.root == null) return;
        foreach (var filter in d.root.GetComponentsInChildren<MeshFilter>()) Destroy(filter.sharedMesh);
        Destroy(d.root);
    }

    // The valley's own materials, so a drawn house sits in the same light as a village's.
    void FindMaterials()
    {
        if (material != null) return;
        var house = FindAnyObjectByType<VillageHouse>(FindObjectsInactive.Include);
        material = house != null ? house.material : null;
        glass = house != null ? house.glass : null;
    }

    // The nearest cell to stand on past what was laid: inside a hollow house would shut a player in.
    Vector2Int? Outside(BuildingsChange change, Vector2Int at)
    {
        for (int ring = 1; ring <= FarthestNudge; ring++)
            for (int dy = -ring; dy <= ring; dy++)
                for (int dx = -ring; dx <= ring; dx++)
                    if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) == ring && Open(change, at.x + dx, at.y + dy)) return new Vector2Int(at.x + dx, at.y + dy);
        return null;
    }

    bool Open(BuildingsChange change, int x, int y) => hooked.InBounds(x, y) && !change.Covers(x, y) && !hooked.IsBlocking(hooked.At(x, y));

    // Raised after the cells are laid: this peer's own player moves off a new wall.
    void Moved(BuildingsChange change)
    {
        var caster = EventLog.TrackedCaster;
        if (caster == null) return;
        var wizard = caster.transform;
        var at = grid.CellAt(wizard.position);
        if (!change.Covers(at.x, at.y) || !(Outside(change, at) is { } free)) return;
        var to = grid.CellCenter(free.x, free.y);
        to.y = grid.GroundAt(to);
        bool hasBody = wizard.TryGetComponent<CharacterController>(out var body);
        if (hasBody) body.enabled = false; // a controller would undo a direct move
        if (wizard.TryGetComponent<NetWizard>(out var net) && net.IsSpawned) net.Teleport(to);
        else wizard.position = to;
        if (hasBody) body.enabled = true;
    }
}
