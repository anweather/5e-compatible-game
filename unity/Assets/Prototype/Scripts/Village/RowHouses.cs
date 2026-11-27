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
[GridAddOn(GridStage.Rules, 20)]
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
        public Vector2Int[] chunks;
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

    public double WorstRedrawMs { get; private set; }
    public int Redraws { get; private set; }

    public string Ruins => string.Join(" ", order.Select(d => $"{d.row.Id}:{d.ruin.Down}/{d.ruin.Stretches}"));

    public RowRuin Ruin(string id) => drawn.TryGetValue(id, out var d) ? d.ruin : null;

    public int Count => drawn.Count;

    public GameObject Root(string id) => drawn.TryGetValue(id, out var d) ? d.root : null;

    public Transform Hidden(string id) => drawn.TryGetValue(id, out var d) ? d.hidden : null;

    void Awake()
    {
        grid = GetComponent<ChemGrid>();
        grid.WorldReset += Dropped;
    }

    void Dropped()
    {
        foreach (var d in drawn.Values) Remove(d);
        drawn.Clear();
        order.Clear();
        Hook(null);
    }

    void OnDestroy()
    {
        if (grid != null) grid.WorldReset -= Dropped;
        Hook(null);
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
        if (world.Rules != seen || NearMoved(world))
        {
            seen = world.Rules;
            Sync(world);
        }
        LookAtOne();
    }

    void LookAtOne()
    {
        if (order.Count == 0) return;
        nextLook = (nextLook + 1) % order.Count;
        var d = order[nextLook];
        if (d.ruin.Stretches == 0 || AllPacked(d)) return;
        timer.Restart();
        if (!d.ruin.Look(d.cells, d.roofBurns)) return;
        Redraws++;
        WorstRedrawMs = System.Math.Max(WorstRedrawMs, timer.Elapsed.TotalMilliseconds);
        if (d.roofCollider != null) d.roofCollider.enabled = !d.ruin.RoofHurt;
        if (d.walkCollider != null) d.walkCollider.sharedMesh = d.walkMesh;
        if (d.railCollider != null) d.railCollider.sharedMesh = d.railMesh;
        d.panes.Darken(i => d.ruin.Out(d.paneOn[i]));
        foreach (var puff in d.smoke) puff.gameObject.SetActive(!d.ruin.RoofHurt);
        if (d.told || d.ruin.Down * 2 < d.ruin.Stretches) return;
        d.told = true;
        EventLog.Add($"The {d.row.Id} falls");
    }

    bool AllPacked(Drawn d)
    {
        var world = hooked;
        if (world == null || d.chunks == null || d.chunks.Length == 0) return false;
        foreach (var c in d.chunks)
            if (!world.InBounds(c.x << Chunks.Shift, c.y << Chunks.Shift) || !world.IsPacked(c.x, c.y)) return false;
        return true;
    }

    RowRuin.CellNow CellOf(BuildingRow row, Vector2Int at)
    {
        var world = hooked;
        int x = row.X + at.x, y = row.Y + at.y;
        if (!world.InBounds(x, y)) return default;
        var c = world.At(x, y);
        return new RowRuin.CellNow { blocking = world.IsBlocking(c), burning = c.Fire > 0, ashen = c.Cover == CoverIds.Ash };
    }

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

    const int NearCells = 640, NearChunkMoved = 2;
    const float NearEvery = 1f;
    float nextNear;
    readonly List<(int x, int y)> nearFrom = new();

    static bool DrawsNearOnly(World world) => world.Source is IGeneratedSource;

    bool NearMoved(World world)
    {
        if (!DrawsNearOnly(world) || Time.unscaledTime < nextNear) return false;
        nextNear = Time.unscaledTime + NearEvery;
        var now = grid.PlayerCells();
        if (now.Count == 0) return false;
        bool moved = now.Count != nearFrom.Count;
        for (int n = 0; !moved && n < now.Count; n++)
            moved = System.Math.Max(System.Math.Abs(now[n].x - nearFrom[n].x), System.Math.Abs(now[n].y - nearFrom[n].y)) >= NearChunkMoved * Chunks.Size;
        return moved;
    }

    bool Near(BuildingRow row)
    {
        foreach (var (x, y) in nearFrom)
            if (System.Math.Max(System.Math.Abs(row.X - x), System.Math.Abs(row.Y - y)) <= NearCells) return true;
        return false;
    }

    void Sync(World world)
    {
        var rows = world.RowsDrawn();
        if (DrawsNearOnly(world))
        {
            nearFrom.Clear();
            nearFrom.AddRange(grid.PlayerCells());
            if (nearFrom.Count == 0) nearFrom.Add((grid.PlayerStart.x, grid.PlayerStart.y));
            rows = rows.Where(Near).ToList();
        }
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
        var corner = Corner(row);
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
        var valley = ValleyChemistry.InPlay;
        if (valley != null) valley.BindPanes(panes);
        var laid = world.BuildingCellsOf(row);
        var cells = new HashSet<Vector2Int>(laid.Where(c => c.Part != BuildingPart.Fill).Select(c => new Vector2Int(c.X - row.X, c.Y - row.Y)));
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
            chunks = laid.Select(c => new Vector2Int(Chunks.ChunkOf(c.X), Chunks.ChunkOf(c.Y))).Distinct().ToArray(),
        };
    }

    void GreatTrees(GameObject root, BuildingRow row, Vector3 corner)
    {
        if (!(row.Json["parts"] is JArray parts) || !(row.Json["joins"] is JObject joins)) return;
        var valley = ValleyChemistry.InPlay;
        foreach (var part in parts.OfType<JObject>().Where(p => (string)p["kind"] == GreatTree))
        {
            if (valley == null || !(joins[(string)part["join"] ?? ""] is JArray j)) continue;
            int jx = (int)j[0], jy = (int)j[1];
            int cx = row.X + Mathf.FloorToInt(jx / 2f), cy = row.Y + Mathf.FloorToInt(jy / 2f);
            if (!valley.TreeModel(cx, cy, (string)part["tree"], out var prefab, out var size)) continue;
            float half = grid.cellSize / 2f;
            var at = corner + new Vector3(jx * half, 0f, -jy * half);
            at.y = TerrainY(at) ?? grid.GroundAt(at);
            var tree = Instantiate(prefab, at, Quaternion.Euler(0f, (cx * 37 + cy * 11) % 360, 0f), root.transform);
            tree.name = "Great tree";
            tree.AddComponent<GroundedTree>();
            float own = Height(tree), tallest = valley.TreeKinds(cx, cy).Select(Measured).DefaultIfEmpty(own).Max();
            tree.transform.localScale = (((float?)part["scale"] ?? 3f) * tallest / Mathf.Max(0.1f, own)) * size;
        }
    }

    const string GreatTree = "great-tree";

    static float? TerrainY(Vector3 p)
    {
        var map = Object.FindAnyObjectByType<MapTerrains>();
        if (map != null && map.Lazy) return map.HeightAt(p);
        foreach (var t in map != null ? map.Chunks : Terrain.activeTerrains)
        {
            if (t == null) continue;
            var o = t.transform.position;
            var size = t.terrainData.size;
            if (p.x >= o.x && p.x <= o.x + size.x && p.z >= o.z && p.z <= o.z + size.z) return o.y + t.SampleHeight(p);
        }
        return null;
    }

    static float Height(GameObject tree)
    {
        var all = tree.GetComponentsInChildren<Renderer>();
        if (all.Length == 0) return 0f;
        var bounds = all[0].bounds;
        foreach (var r in all) bounds.Encapsulate(r.bounds);
        return bounds.size.y;
    }

    static float Measured(GameObject prefab)
    {
        var copy = Instantiate(prefab, new Vector3(0f, -10000f, 0f), Quaternion.identity);
        float h = Height(copy);
        Destroy(copy);
        return h;
    }

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

    float TopOver(RowMesher.Result made, BuildingRow row, Vector2Int c, float rootY)
    {
        float top = made.tops.TryGetValue(new Vector2Int(c.x - row.X, c.y - row.Y), out float wall) ? wall : RowMesher.WalkTop(row.Json);
        return rootY + top - grid.GroundTop(c.x, c.y, grid.World.At(c.x, c.y));
    }

    const int CameraPassesLayer = 1;

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

    static Transform HideBase(World world, BuildingRow row, Vector3 corner)
    {
        var house = SceneHouse(world, row, corner);
        if (house != null) house.gameObject.SetActive(false);
        return house;
    }

    public Transform SceneHouseOf(BuildingRow row) => SceneHouse(grid.World, row, Corner(row));

    Vector3 Corner(BuildingRow row) => grid.CellCenter(row.X, row.Y) + new Vector3(-grid.cellSize / 2f, 0f, grid.cellSize / 2f);

    static Transform SceneHouse(World world, BuildingRow row, Vector3 corner)
    {
        if (row.Json["built"] != null || !world.IsBaseBuilding(row.Id)) return null;
        var valley = ValleyChemistry.InPlay;
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
        return house;
    }

    void Remove(Drawn d)
    {
        if (!closing)
            foreach (var c in d.walls) grid.BuiltWall(c.x, c.y, 0f);
        if (d.hidden != null) d.hidden.gameObject.SetActive(true);
        var valley = ValleyChemistry.InPlay;
        if (valley != null && d.panes != null) valley.UnbindPanes(d.panes);
        if (d.root == null) return;
        foreach (var filter in d.root.GetComponentsInChildren<MeshFilter>())
            if (filter.sharedMesh != null && filter.sharedMesh.GetInstanceID() < 0) Destroy(filter.sharedMesh);
        Destroy(d.root);
    }

    void FindMaterials()
    {
        if (material != null) return;
        var house = FindAnyObjectByType<VillageHouse>(FindObjectsInactive.Include);
        material = house != null ? house.material : null;
        glass = house != null ? house.glass : null;
    }

    Vector2Int? Outside(BuildingsChange change, Vector2Int at)
    {
        for (int ring = 1; ring <= FarthestNudge; ring++)
            for (int dy = -ring; dy <= ring; dy++)
                for (int dx = -ring; dx <= ring; dx++)
                    if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) == ring && Open(change, at.x + dx, at.y + dy)) return new Vector2Int(at.x + dx, at.y + dy);
        return null;
    }

    bool Open(BuildingsChange change, int x, int y) => hooked.InBounds(x, y) && !change.Covers(x, y) && !hooked.IsBlocking(hooked.At(x, y));

    void Moved(BuildingsChange change)
    {
        var caster = EventLog.TrackedWizard;
        if (caster == null) return;
        var wizard = caster.transform;
        var at = grid.CellAt(wizard.position);
        if (!change.Covers(at.x, at.y) || !(Outside(change, at) is { } free)) return;
        var to = grid.CellCenter(free.x, free.y);
        to.y = grid.GroundAt(to);
        bool hasBody = wizard.TryGetComponent<CharacterController>(out var body);
        if (hasBody) body.enabled = false;
        if (caster is Caster mover) mover.MoveTo(to);
        else wizard.position = to;
        if (hasBody) body.enabled = true;
    }
}
