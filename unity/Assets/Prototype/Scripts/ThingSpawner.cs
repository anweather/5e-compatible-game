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
    /// <summary>The prefab for one kind (an id in rules/entities.json or rules/items.json).</summary>
    [Serializable]
    public struct KindPrefab
    {
        public string kind;
        public GameObject prefab;
    }

    /// <summary>The generic prefabs a mod's new kind spawns as: its "kind" in <see cref="prefabs"/>, and on the prefab until it's dressed.</summary>
    public const string DressedProp = "dressed-prop", DressedItem = "dressed-item";

    public ChemGrid grid;
    [Tooltip("One prefab per kind; each is also a registered network prefab (ChemYardBuilder).")]
    public List<KindPrefab> prefabs = new();

    // What is in the world now, by id: spawned here, or (on a client) adopted from the host.
    readonly Dictionary<ThingId, PhysicsProp> live = new();
    // Things that broke: their chunk spawning again doesn't bring them back.
    readonly HashSet<ThingId> gone = new();
    readonly HashSet<(int, int)> loaded = new();
    readonly HashSet<string> missingKinds = new();
    // A client: dressed things that arrived before this peer's rules had them.
    readonly List<NetProp> waiting = new();
    // The things as they stood at the last switch or change, to tell what changed.
    WorldThings shown;
    // The rules the dressed things last read their looks from.
    Rules lookRules;
    ThingBodies bodies;
    NetworkManager network;

    bool Client => network != null && network.IsListening && !network.IsServer;
    bool Hosting => network != null && network.IsListening && network.IsServer;
    WorldThings Things => grid.World.ThingsNow;

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

    void Awake() => bodies = gameObject.AddComponent<ThingBodies>();

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
        grid.ChunkFreshened += BringBack;
        grid.RulesChanged += ThingsChanged;
    }

    NetChem chem;

    void SpawnActive()
    {
        var world = grid.World;
        shown = Things;
        lookRules = world.Rules;
        foreach (var (cx, cy) in shown.ChunkCoords)
            if (world.HasChunk(cx, cy) && world.IsActive(cx, cy)) SpawnChunk(cx, cy);
    }

    // The host, at an online reset: every thing away (for everyone) and back from the data, broken
    // ones included. A client's things go and come with the host's despawns and spawns. Any other
    // reset is a new world, which may hold other things by its seed's chances.
    void Respawn()
    {
        if (!Hosting || !OnlineReset.Happened(chem))
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

    // --- per chunk -----------------------------------------------------------------

    static readonly Unity.Profiling.ProfilerMarker SpawnMarker = new("ThingSpawner.SpawnChunk"), UnloadMarker = new("ThingSpawner.UnloadChunk"),
        BringBackMarker = new("ThingSpawner.BringBack");

    /// <summary>
    /// Spawns a chunk's things (all but those that broke). On a client it only notes the chunk:
    /// the host's things arrive through Netcode.
    /// </summary>
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

    // The dragon's breath reset a chunk: what broke there comes back, and its things go home unburnt. A chunk that
    // isn't loaded only forgets what broke, for when it is. On a client the host's things do this for it.
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

    /// <summary>Removes a chunk's things (the host despawns them for everyone); SpawnChunk brings them back.</summary>
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

    // Out of the world: the host despawns it for everyone.
    static void Remove(PhysicsProp prop)
    {
        var net = prop.GetComponent<NetworkObject>();
        if (net != null && net.IsSpawned) net.Despawn(true);
        else Destroy(prop.gameObject);
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

    // --- a switch of rules ---------------------------------------------------------------

    static readonly Unity.Profiling.ProfilerMarker PlacedMarker = new("ThingSpawner.Placed");

    /// <summary>Milliseconds the last change of things between switches took to show (a spell placed, or a placed thing lapsed), for the probe.</summary>
    public double PlacedMs { get; private set; }

    void Update() => ShowPlaced();

    /// <summary>
    /// After a tick that placed things or let them lapse (the core's table is a new one), shows the difference; a switch
    /// not heard yet reads the rules too. A quiet frame compares one reference. True if anything changed (for the probe).
    /// </summary>
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

    // The rules changed, or the world did: what a mod added comes in, what it took with it goes, and
    // the dressed things read their looks again. On a client the host's spawns and despawns do the rest.
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

    // A mod's thing stands in any chunk of the world, active or not: it sleeps there until the chunk activates.
    void Bring(ThingSpawn thing)
    {
        if (!grid.World.HasChunk(thing.ChunkX, thing.ChunkY)) return;
        if (!IsLoaded(thing.ChunkX, thing.ChunkY)) SpawnChunk(thing.ChunkX, thing.ChunkY);
        else if (Find(thing.Id) == null) Spawn(thing);
    }

    // A despawned thing a player holds is theirs to lose.
    void TakeAway(ThingId id)
    {
        var prop = Find(id);
        live.Remove(id);
        gone.Remove(id);
        if (prop == null) return;
        if (prop.TryGetComponent<Carryable>(out var item)) item.Lost();
        Remove(prop);
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

    // Sending stones: each knows the other (the data's "link"), once both are in the world. Links are
    // the file's, which a client has before it has a room.
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

    // The host's rules got here ahead of this peer's. It doesn't Start, and so reads no rules, until enabled.
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
        if (net.TryGetComponent<Carryable>(out var item)) item.enabled = on;
    }
}
