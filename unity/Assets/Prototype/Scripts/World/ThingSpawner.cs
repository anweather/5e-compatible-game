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
// - The dragon's breath (ChemGrid.ChunkFreshened): the things of the chunk it
//   reset go home, solo or by the host, and those that broke come back. Only what
//   a wizard is carrying stays with it.
// - Mods add things and take them away (World.ThingsNow, docs/RULES_MCP.md "Things"):
//   at each switch of rules this spawns what's new and despawns what went, solo or
//   by the host, even out of a hand. A new kind has no prefab of its own: it spawns
//   as one of the two generic ones, dressed by ThingBodies from its look. Online
//   those say nothing of what they are, so a client finds the kind by the thing's
//   id, and one that arrives before this peer's rules have it waits for them.
// - Spells place things too, and they lapse (World.Placed, docs/design/events-and-placing.md
//   2a): the core makes a new ThingsNow table whenever its list changes, so each frame this
//   compares that table with the one it showed (a reference, nothing more on a quiet tick) and,
//   after a tick that changed it, spawns and despawns the difference as at a switch.
public class ThingSpawner : MonoBehaviour
{
    [Serializable]
    public struct KindPrefab
    {
        public string kind;
        public GameObject prefab;
    }

    public const string DressedProp = "dressed-prop", DressedItem = "dressed-item";

    public ChemGrid grid;
    [Tooltip("One prefab per kind; each is also a registered network prefab (ChemYardBuilder).")]
    public List<KindPrefab> prefabs = new();

    readonly Dictionary<ThingId, PhysicsProp> live = new();
    readonly HashSet<ThingId> gone = new();
    readonly HashSet<(int, int)> loaded = new();
    readonly HashSet<string> missingKinds = new();
    readonly List<NetProp> waiting = new();
    WorldThings shown;
    Rules lookRules;
    ThingBodies bodies;
    NetworkManager network;

    bool Client => network != null && network.IsListening && !network.IsServer;
    bool Hosting => network != null && network.IsListening && network.IsServer;
    WorldThings Things => grid.World.ThingsNow;

    public IEnumerable<(ThingId id, PhysicsProp prop)> Live
    {
        get
        {
            foreach (var pair in live)
                if (pair.Value != null) yield return (pair.Key, pair.Value);
        }
    }

    public PhysicsProp Find(ThingId id) => live.TryGetValue(id, out var prop) && prop != null ? prop : null;

    public bool IsLoaded(int cx, int cy) => loaded.Contains((cx, cy));

    static readonly Registry<ThingSpawner> registry = new();

    public static ThingSpawner InPlay => registry.First;

    void Awake()
    {
        registry.Join(this);
        bodies = gameObject.AddComponent<ThingBodies>();
    }

    void Start()
    {
        network = NetworkManager.Singleton;
        if (network != null)
        {
            network.OnServerStarted += SpawnOnline;
            network.OnClientStarted += DropSoloCopies;
        }
        SpawnActive();
        grid.WorldReset += Respawn;
        grid.ChunkFreshened += BringBack;
        grid.RulesChanged += ThingsChanged;
    }

    void SpawnActive()
    {
        var world = grid.World;
        shown = Things;
        lookRules = world.Rules;
        foreach (var (cx, cy) in shown.ChunkCoords)
            if (world.HasChunk(cx, cy) && world.IsActive(cx, cy)) SpawnChunk(cx, cy);
    }

    void Respawn()
    {
        if (!Hosting || !OnlineReset.Happened(Peers.Of(grid)))
        {
            ThingsChanged();
            return;
        }
        foreach (var (cx, cy) in new List<(int, int)>(loaded)) UnloadChunk(cx, cy);
        foreach (var (_, prop) in Live) Remove(prop);
        live.Clear();
        gone.Clear();
        loaded.Clear();
        SpawnActive();
    }

    void OnDestroy()
    {
        registry.Leave(this);
        if (grid != null)
        {
            grid.WorldReset -= Respawn;
            grid.ChunkFreshened -= BringBack;
            grid.RulesChanged -= ThingsChanged;
        }
        if (network == null) return;
        network.OnServerStarted -= SpawnOnline;
        network.OnClientStarted -= DropSoloCopies;
    }

    static readonly Unity.Profiling.ProfilerMarker SpawnMarker = new("ThingSpawner.SpawnChunk"), UnloadMarker = new("ThingSpawner.UnloadChunk"),
        BringBackMarker = new("ThingSpawner.BringBack");

    public void SpawnChunk(int cx, int cy)
    {
        if (!loaded.Add((cx, cy))) return;
        using var marked = SpawnMarker.Auto();
        NoteGone();
        if (Client) return;
        foreach (var thing in Things.InChunk(cx, cy))
        {
            if (gone.Contains(thing.Id) || Find(thing.Id) != null) continue;
            Spawn(thing);
        }
    }

    void Spawn(ThingSpawn thing)
    {
        var prop = Place(thing);
        if (prop == null) return;
        live[thing.Id] = prop;
        if (Hosting) prop.GetComponent<NetworkObject>().Spawn(true);
        Link(thing.Id);
    }

    void BringBack(ChunkFresh fresh)
    {
        if (Client) return;
        using var marked = BringBackMarker.Auto();
        NoteGone();
        foreach (var thing in Things.InChunk(fresh.CX, fresh.CY))
        {
            gone.Remove(thing.Id);
            if (!IsLoaded(fresh.CX, fresh.CY)) continue;
            var prop = Find(thing.Id);
            if (prop == null) Spawn(thing);
            else if (!prop.Held) SendHome(prop, thing);
        }
    }

    void SendHome(PhysicsProp prop, ThingSpawn thing)
    {
        var cell = new Vector2Int(thing.CellX, thing.CellY);
        var at = grid.CellCenter(cell.x, cell.y);
        var turn = Quaternion.Euler(0f, thing.Rotation, 0f);
        prop.BackAsBuilt(at + Vector3.up * SpawnAbove, turn);
        prop.BackAsBuilt(at + Vector3.up * RestHeight(prop.gameObject, thing.Kind, cell, at), turn);
    }

    public void UnloadChunk(int cx, int cy)
    {
        if (!loaded.Remove((cx, cy))) return;
        using var marked = UnloadMarker.Auto();
        NoteGone();
        foreach (var thing in Things.InChunk(cx, cy))
        {
            var prop = Find(thing.Id);
            live.Remove(thing.Id);
            if (prop != null && !Client) Remove(prop);
        }
    }

    static void Remove(PhysicsProp prop)
    {
        var net = prop.GetComponent<NetworkObject>();
        if (net != null && net.IsSpawned) net.Despawn(true);
        else Destroy(prop.gameObject);
    }

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

    static readonly Unity.Profiling.ProfilerMarker PlacedMarker = new("ThingSpawner.Placed");

    public double PlacedMs { get; private set; }

    void Update() => ShowPlaced();

    public bool ShowPlaced()
    {
        if (grid == null || grid.World == null || ReferenceEquals(Things, shown)) return false;
        using var marked = PlacedMarker.Auto();
        long began = System.Diagnostics.Stopwatch.GetTimestamp();
        if (ReferenceEquals(lookRules, grid.World.Rules)) ShowChanges();
        else ThingsChanged();
        PlacedMs = (System.Diagnostics.Stopwatch.GetTimestamp() - began) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        return true;
    }

    void ThingsChanged()
    {
        lookRules = grid.World.Rules;
        bodies.ReadRules(lookRules);
        ShowChanges();
    }

    void ShowChanges()
    {
        var (added, removed) = Things.ChangesFrom(shown);
        shown = Things;
        if (Client)
        {
            AdoptWaiting();
            return;
        }
        NoteGone();
        foreach (var id in removed) TakeAway(id);
        foreach (var thing in added) Bring(thing);
    }

    void Bring(ThingSpawn thing)
    {
        if (!grid.World.HasChunk(thing.ChunkX, thing.ChunkY)) return;
        if (!IsLoaded(thing.ChunkX, thing.ChunkY)) SpawnChunk(thing.ChunkX, thing.ChunkY);
        else if (Find(thing.Id) == null) Spawn(thing);
    }

    void TakeAway(ThingId id)
    {
        var prop = Find(id);
        live.Remove(id);
        gone.Remove(id);
        if (prop == null) return;
        if (prop.TryGetComponent<ItemFace>(out var item)) item.Lost();
        Remove(prop);
    }

    PhysicsProp Place(ThingSpawn thing)
    {
        var prefab = PrefabFor(thing.Kind);
        if (prefab == null) return null;
        var cell = new Vector2Int(thing.CellX, thing.CellY);
        var at = grid.CellCenter(cell.x, cell.y);
        var go = Instantiate(prefab, at + Vector3.up * SpawnAbove, Quaternion.Euler(0f, thing.Rotation, 0f));
        go.name = NameOf(thing.Kind, thing.Id);
        var prop = go.GetComponent<PhysicsProp>();
        prop.grid = grid;
        if (IsDressed(prop)) Dress(prop, thing.Kind);
        if (go.TryGetComponent<NetProp>(out var net)) net.AssignId(thing.Id);
        var rest = at + Vector3.up * RestHeight(go, thing.Kind, cell, at);
        go.transform.position = rest;
        if (go.TryGetComponent<Rigidbody>(out var body)) body.position = rest;
        Physics.SyncTransforms();
        return prop;
    }

    const float SpawnAbove = 5f;

    static string NameOf(string kind, ThingId id) => $"{kind} {id}";

    static bool IsDressed(PhysicsProp prop) => prop.kind == DressedProp || prop.kind == DressedItem;

    void Dress(PhysicsProp prop, string kind)
    {
        prop.kind = kind;
        bodies.Dress(prop);
    }

    GameObject Baked(string kind)
    {
        foreach (var p in prefabs)
            if (p.kind == kind && p.prefab != null) return p.prefab;
        return null;
    }

    GameObject PrefabFor(string kind)
    {
        var baked = Baked(kind);
        if (baked != null) return baked;
        var rules = grid.World.Rules;
        if (ThingBodies.LookOf(rules, kind) != null && Baked(rules.Item(kind) != null ? DressedItem : DressedProp) is { } dressed) return dressed;
        if (missingKinds.Add(kind)) Debug.LogWarning($"ThingSpawner: no prefab for '{kind}' (rebuild the yard: Prototype > Build Chemistry Yard)");
        return null;
    }

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

    void Link(ThingId id)
    {
        var thing = grid.Things.Find(id);
        var mine = Find(id);
        if (thing == null || mine == null || !mine.TryGetComponent<ItemFace>(out var a)) return;
        foreach (var other in grid.Things.LinkedTo(thing))
        {
            var theirs = Find(other.Id);
            if (theirs == null || !theirs.TryGetComponent<ItemFace>(out var b)) continue;
            a.PairWith(b);
            b.PairWith(a);
        }
    }

    void SpawnOnline()
    {
        NoteGone();
        foreach (var (_, prop) in Live)
        {
            var net = prop.GetComponent<NetworkObject>();
            if (net != null && !net.IsSpawned) net.Spawn(true);
        }
    }

    void DropSoloCopies()
    {
        if (network.IsServer) return;
        foreach (var (_, prop) in Live) Destroy(prop.gameObject);
        live.Clear();
        gone.Clear();
    }

    public void Adopt(NetProp net)
    {
        var prop = net.GetComponent<PhysicsProp>();
        prop.grid = grid;
        if (net.Id.IsNone) return;
        if (!IsDressed(prop) || KindKnown(net)) Take(net);
        else Wait(net);
    }

    void Take(NetProp net)
    {
        var prop = net.GetComponent<PhysicsProp>();
        var id = net.Id;
        if (IsDressed(prop)) Dress(prop, Things.Find(id).Kind);
        net.name = NameOf(prop.kind, id);
        live[id] = prop;
        Link(id);
    }

    bool KindKnown(NetProp net) => Things.Find(net.Id) != null;

    void Wait(NetProp net)
    {
        Enable(net, false);
        waiting.Add(net);
    }

    void AdoptWaiting()
    {
        waiting.RemoveAll(net => net == null);
        foreach (var net in waiting.FindAll(KindKnown))
        {
            waiting.Remove(net);
            Take(net);
            Enable(net, true);
        }
    }

    static void Enable(NetProp net, bool on)
    {
        net.GetComponent<PhysicsProp>().enabled = on;
        if (net.TryGetComponent<ItemFace>(out var item)) item.enabled = on;
    }
}
