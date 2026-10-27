using System;
using System.Collections.Generic;
using Chem;
using Unity.Netcode;
using UnityEngine;

// Spawns the world's props and items from data (world/things.json, read by
// ChemGrid into Chem.WorldThings), chunk by chunk, instead of having them
// placed in the scene. Each thing is a prefab of its kind (built by
// PropBuilder and ItemBuilder) set at rest on its cell, or floating at its
// waterline, and carries its stable id (ThingId: chunk + index in the data)
// on its NetProp.
//
// - Solo, it spawns the things of every active chunk at Start.
// - Online, the host spawns the same objects as NetworkObjects when it starts
//   hosting (their ids travel with the spawn, to late joiners too). A client
//   throws its own solo copies away when it starts joining and adopts the
//   host's as they arrive (NetProp.OnNetworkSpawn), by id: it gives them the
//   grid and links each sending stone to its partner.
// - Per chunk: SpawnChunk and UnloadChunk. Nothing unloads chunks yet (no
//   streaming); a thing that broke is remembered as gone and not spawned
//   again when its chunk comes back.
// - An online reset (OnlineReset): the host takes every thing away and spawns
//   them all again from the data with their ids, the broken ones too.
public class ThingSpawner : MonoBehaviour
{
    /// <summary>The prefab for one kind (an id in rules/entities.json or rules/items.json).</summary>
    [Serializable]
    public struct KindPrefab
    {
        public string kind;
        public GameObject prefab;
    }

    public ChemGrid grid;
    [Tooltip("One prefab per kind; each is also a registered network prefab (ChemYardBuilder).")]
    public List<KindPrefab> prefabs = new();

    // What is in the world now, by id: spawned here, or (on a client) adopted from the host.
    readonly Dictionary<ThingId, PhysicsProp> live = new();
    // Things that broke: their chunk spawning again doesn't bring them back.
    readonly HashSet<ThingId> gone = new();
    readonly HashSet<(int, int)> loaded = new();
    readonly HashSet<string> missingKinds = new();
    NetworkManager network;

    bool Client => network != null && network.IsListening && !network.IsServer;
    bool Hosting => network != null && network.IsListening && network.IsServer;

    /// <summary>The things in the world now (not broken), by stable id.</summary>
    public IEnumerable<(ThingId id, PhysicsProp prop)> Live
    {
        get
        {
            foreach (var pair in live)
                if (pair.Value != null) yield return (pair.Key, pair.Value);
        }
    }

    /// <summary>The thing with this id, if it is in the world now.</summary>
    public PhysicsProp Find(ThingId id) => live.TryGetValue(id, out var prop) && prop != null ? prop : null;

    public bool IsLoaded(int cx, int cy) => loaded.Contains((cx, cy));

    void Start()
    {
        network = NetworkManager.Singleton;
        if (network != null)
        {
            network.OnServerStarted += SpawnOnline;
            network.OnClientStarted += DropSoloCopies;
        }
        SpawnActive();
        chem = grid.GetComponent<NetChem>();
        grid.WorldReset += Respawn;
    }

    NetChem chem;

    void SpawnActive()
    {
        var world = grid.World;
        foreach (var (cx, cy) in grid.Things.ChunkCoords)
            if (world.HasChunk(cx, cy) && world.IsActive(cx, cy)) SpawnChunk(cx, cy);
    }

    // The host, at an online reset: every thing away (for everyone) and back from the data, broken
    // ones included. A client's things go and come with the host's despawns and spawns.
    void Respawn()
    {
        if (!Hosting || !OnlineReset.Happened(chem)) return;
        foreach (var (cx, cy) in new List<(int, int)>(loaded)) UnloadChunk(cx, cy);
        foreach (var (_, prop) in Live)
        {
            var obj = prop.GetComponent<NetworkObject>();
            if (obj != null && obj.IsSpawned) obj.Despawn(true);
            else Destroy(prop.gameObject);
        }
        live.Clear();
        gone.Clear();
        loaded.Clear();
        SpawnActive();
    }

    void OnDestroy()
    {
        if (grid != null) grid.WorldReset -= Respawn;
        if (network == null) return;
        network.OnServerStarted -= SpawnOnline;
        network.OnClientStarted -= DropSoloCopies;
    }

    // --- per chunk -----------------------------------------------------------------

    /// <summary>
    /// Spawns a chunk's things (all but those that broke). On a client it only notes the chunk:
    /// the host's things arrive through Netcode.
    /// </summary>
    public void SpawnChunk(int cx, int cy)
    {
        if (!loaded.Add((cx, cy))) return;
        NoteGone();
        if (Client) return;
        foreach (var thing in grid.Things.InChunk(cx, cy))
        {
            if (gone.Contains(thing.Id) || Find(thing.Id) != null) continue;
            var prop = Place(thing);
            if (prop == null) continue;
            live[thing.Id] = prop;
            if (Hosting) prop.GetComponent<NetworkObject>().Spawn(true);
            Link(thing.Id);
        }
    }

    /// <summary>Removes a chunk's things (the host despawns them for everyone); SpawnChunk brings them back.</summary>
    public void UnloadChunk(int cx, int cy)
    {
        if (!loaded.Remove((cx, cy))) return;
        NoteGone();
        foreach (var thing in grid.Things.InChunk(cx, cy))
        {
            var prop = Find(thing.Id);
            live.Remove(thing.Id);
            if (prop == null || Client) continue;
            var net = prop.GetComponent<NetworkObject>();
            if (net != null && net.IsSpawned) net.Despawn(true);
            else Destroy(prop.gameObject);
        }
    }

    // A thing destroyed without being unloaded broke (or blew up): it stays gone.
    void NoteGone()
    {
        var broken = new List<ThingId>();
        foreach (var pair in live)
            if (pair.Value == null) broken.Add(pair.Key);
        foreach (var id in broken)
        {
            live.Remove(id);
            gone.Add(id);
        }
    }

    // --- one thing ---------------------------------------------------------------------

    PhysicsProp Place(ThingSpawn thing)
    {
        var prefab = PrefabFor(thing.Kind);
        if (prefab == null) return null;
        var cell = new Vector2Int(thing.CellX, thing.CellY);
        var at = grid.CellCenter(cell.x, cell.y);
        // Made above the ground, never in it: a body that starts half sunk into a terrain can fall through it.
        var go = Instantiate(prefab, at + Vector3.up * SpawnAbove, Quaternion.Euler(0f, thing.Rotation, 0f));
        go.name = NameOf(thing.Kind, thing.Id);
        var prop = go.GetComponent<PhysicsProp>();
        prop.grid = grid;
        if (go.TryGetComponent<NetProp>(out var net)) net.AssignId(thing.Id);
        var rest = at + Vector3.up * RestHeight(go, thing.Kind, cell, at);
        go.transform.position = rest;
        if (go.TryGetComponent<Rigidbody>(out var body)) body.position = rest;
        Physics.SyncTransforms();
        return prop;
    }

    const float SpawnAbove = 5f;

    static string NameOf(string kind, ThingId id) => $"{kind} {id}";

    GameObject PrefabFor(string kind)
    {
        foreach (var p in prefabs)
            if (p.kind == kind && p.prefab != null) return p.prefab;
        if (missingKinds.Add(kind)) Debug.LogWarning($"ThingSpawner: no prefab for '{kind}' (rebuild the yard: Prototype > Build Chemistry Yard)");
        return null;
    }

    // Where its centre rests: its lowest collider on the ground, or, where it floats on water, its
    // waterline (PropState: a floater sinks until the part under water times its lift holds it up).
    float RestHeight(GameObject go, string kind, Vector2Int cell, Vector3 at)
    {
        Physics.SyncTransforms();
        float below = go.transform.position.y;
        foreach (var c in go.GetComponents<Collider>()) below = Mathf.Min(below, c.bounds.min.y);
        float onGround = grid.GroundAt(at) + go.transform.position.y - below;

        var rules = grid.World.Rules;
        var def = rules.Entity(kind) ?? rules.Item(kind)?.AsEntity();
        var c0 = grid.World.At(cell.x, cell.y);
        if (def == null || c0 == null || c0.Water <= 0) return onGround;
        var state = new PropState(def);
        if (!state.Floats) return onGround;
        float half = go.GetComponent<Renderer>().bounds.extents.y;
        float afloat = grid.WaterSurfaceY(cell.x, cell.y) - 2f * half / (float)state.Lift + half;
        return Mathf.Max(onGround, afloat);
    }

    // Sending stones: each knows the other (the data's "link"), once both are in the world.
    void Link(ThingId id)
    {
        var thing = grid.Things.Find(id);
        var mine = Find(id);
        if (thing == null || mine == null || !mine.TryGetComponent<Carryable>(out var a)) return;
        foreach (var other in grid.Things.LinkedTo(thing))
        {
            var theirs = Find(other.Id);
            if (theirs == null || !theirs.TryGetComponent<Carryable>(out var b)) continue;
            a.partner = b;
            b.partner = a;
        }
    }

    // --- online ------------------------------------------------------------------------

    // The host: the things already in the world become NetworkObjects, with their ids.
    void SpawnOnline()
    {
        NoteGone();
        foreach (var (_, prop) in Live)
        {
            var net = prop.GetComponent<NetworkObject>();
            if (net != null && !net.IsSpawned) net.Spawn(true);
        }
    }

    // A client: its solo copies go; the host's arrive through Netcode (Adopt).
    void DropSoloCopies()
    {
        if (network.IsServer) return;
        foreach (var (_, prop) in Live) Destroy(prop.gameObject);
        live.Clear();
        gone.Clear();
    }

    /// <summary>A client: one of the host's things arrived. It gets the grid, its name and its partner, by its id.</summary>
    public void Adopt(NetProp net)
    {
        var prop = net.GetComponent<PhysicsProp>();
        prop.grid = grid;
        var id = net.Id;
        if (id.IsNone) return;
        net.name = NameOf(prop.kind, id);
        live[id] = prop;
        Link(id);
    }
}
