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
// Walls that burn or break in play stay drawn: only the cells change.
[RequireComponent(typeof(ChemGrid))]
public class RowHouses : MonoBehaviour
{
    const float FindHouseMetres = 10f;

    sealed class Drawn
    {
        public string json;
        public GameObject root;
        public Transform hidden;
    }

    readonly Dictionary<string, Drawn> drawn = new();
    ChemGrid grid;
    World hooked;
    Rules seen;
    Material material, glass;

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
        foreach (var d in drawn.Values) Remove(d);
        drawn.Clear();
    }

    void Update()
    {
        var world = grid.World;
        if (world == null) return;
        if (world != hooked) Hook(world);
        if (world.Rules == seen) return;
        seen = world.Rules;
        Sync(world);
    }

    void Hook(World world)
    {
        if (hooked != null) hooked.BuildingsChanged -= Moved;
        hooked = world;
        seen = null;
        if (hooked != null) hooked.BuildingsChanged += Moved;
    }

    void Sync(World world)
    {
        var rows = world.Rules.ModBuildings;
        foreach (string id in drawn.Keys.Where(id => rows.All(r => r.Id != id)).ToList())
        {
            Remove(drawn[id]);
            drawn.Remove(id);
            Debug.Log($"Row houses: took down {id}, {drawn.Count} now");
        }
        foreach (var row in rows)
        {
            string json = row.Json.ToString(Formatting.None);
            if (drawn.TryGetValue(row.Id, out var old))
            {
                if (old.json == json) continue;
                Remove(old);
            }
            drawn[row.Id] = Draw(world, row, json);
            Debug.Log($"Row houses: drew {row.Id}, {drawn.Count} now");
        }
    }

    Drawn Draw(World world, BuildingRow row, string json)
    {
        FindMaterials();
        var corner = grid.CellCenter(row.X, row.Y) + new Vector3(-grid.cellSize / 2f, 0f, grid.cellSize / 2f);
        var root = new GameObject("Row house " + row.Id);
        root.transform.SetParent(transform, false);
        root.transform.position = new Vector3(corner.x, grid.GroundAt(Middle(corner, row)), corner.z);
        var tables = new RowMesher.Tables
        {
            OpeningLook = kind => world.Rules.Buildings.Row("openings", kind)?["look"],
            TerrainColor = terrain => terrain == null ? Color.clear : CellDressing.Look(world.Rules, terrain).color,
            Tiled = terrain => terrain != null && CellDressing.Of(world.Rules, terrain)?.Form == "tile",
        };
        var made = RowMesher.Make(row.Json, tables);
        Part(root, "Body", made.body, false);
        Part(root, "Roof", made.roof, true);
        root.AddComponent<WindowPanes>().Show(made.panes, glass);
        return new Drawn { json = json, root = root, hidden = HideBase(world, row, corner) };
    }

    static Vector3 Middle(Vector3 corner, BuildingRow row)
    {
        if (!(row.Json["joins"] is JObject joins) || !joins.HasValues) return corner;
        float x = joins.Properties().Average(j => (int)j.Value[0] * 0.75f), z = joins.Properties().Average(j => (int)j.Value[1] * 0.75f);
        return corner + new Vector3(x, 0f, -z);
    }

    void Part(GameObject root, string name, Mesh mesh, bool solid)
    {
        var go = new GameObject(name);
        go.transform.SetParent(root.transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = material;
        if (solid && mesh.vertexCount > 0) go.AddComponent<MeshCollider>().sharedMesh = mesh;
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

    static void Remove(Drawn d)
    {
        if (d.hidden != null) d.hidden.gameObject.SetActive(true);
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

    // Raised after the cells are laid: this peer's own player moves off a new wall.
    void Moved(BuildingsChange change)
    {
        var caster = EventLog.TrackedCaster;
        if (caster == null) return;
        var wizard = caster.transform;
        var at = grid.CellAt(wizard.position);
        if (!change.Covers(at.x, at.y) || !(hooked.NearestFree(at.x, at.y) is { } free)) return;
        var to = grid.CellCenter(free.x, free.y);
        to.y = grid.GroundAt(to);
        bool hasBody = wizard.TryGetComponent<CharacterController>(out var body);
        if (hasBody) body.enabled = false; // a controller would undo a direct move
        if (wizard.TryGetComponent<NetWizard>(out var net) && net.IsSpawned) net.Teleport(to);
        else wizard.position = to;
        if (hasBody) body.enabled = true;
        EventLog.Add("A new wall nudges you aside");
    }
}
