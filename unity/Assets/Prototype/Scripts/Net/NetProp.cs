using Chem;
using Unity.Netcode;
using UnityEngine;

// A physics prop (or a carried item) in multiplayer: a NetworkObject with a
// server-authoritative NetworkTransform, spawned from world/things.json by
// ThingSpawner (its prefab built by PropBuilder or ItemBuilder).
//
// It carries the thing's stable id (ThingId: chunk + index in the data), set
// by the spawner and sent with its spawn, so every machine and a late joiner
// know it by the same id; a client's ThingSpawner adopts it by that id when
// it arrives. The lockstep effects it hands the chemistry name it by that id
// too. Solo it is never spawned, so the id is kept in a plain field and only
// copied into its NetworkVariable as the host spawns it.
//
// The host simulates it: its Rigidbody, the cell under it, burning, hit
// points, what it does to cells (stamped through NetChem, ChemEffects). On
// clients it is a puppet: a kinematic body moved by the NetworkTransform
// (interpolated), which shows the flames and the blast the host reports.
// Only what the eye needs is replicated: burning, levitating and hit points.
//
// When it breaks, the host despawns and destroys it, so it is gone on every
// machine, and a late joiner never gets it.
[RequireComponent(typeof(PhysicsProp), typeof(Rigidbody))]
public class NetProp : NetworkBehaviour
{
    readonly NetworkVariable<bool> burning = new();
    readonly NetworkVariable<bool> levitating = new();
    readonly NetworkVariable<int> hp = new();
    // The host writes it as it spawns (so it arrives with the spawn, late joiners included);
    // a NetworkVariable mustn't be written before then.
    readonly NetworkVariable<ulong> thing = new(ThingId.None.Value);
    ThingId assigned = ThingId.None;

    PhysicsProp prop;
    Rigidbody body;

    /// <summary>A client's copy: follows the host's, simulates nothing.</summary>
    public bool Puppet => IsSpawned && !IsServer;
    /// <summary>The host's copy of a spawned prop: the one that counts.</summary>
    public bool Hosting => IsSpawned && IsServer;

    public bool Burning => burning.Value;
    public bool Levitating => levitating.Value;
    public int Hp => hp.Value;
    /// <summary>Its stable id from world/things.json, the same on every machine (None if it wasn't spawned from data).</summary>
    public ThingId Id => assigned.IsNone ? new ThingId(thing.Value) : assigned;

    /// <summary>ThingSpawner, before it is spawned (or solo, where it never is): which thing of the world's data this is.</summary>
    public void AssignId(ThingId id) => assigned = id;

    void Awake()
    {
        prop = GetComponent<PhysicsProp>();
        body = GetComponent<Rigidbody>();
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            thing.Value = assigned.Value;
            Publish();
            return;
        }
        // A client's copy arrives from the host: the spawner gives it the grid and its partner.
        var spawner = FindFirstObjectByType<ThingSpawner>();
        if (spawner != null) spawner.Adopt(this);
        body.isKinematic = true;
        body.interpolation = RigidbodyInterpolation.None;
        burning.OnValueChanged += Burned;
        prop.ShowBurning(burning.Value, announce: false);
    }

    // A despawn while the session runs means it broke: gone on this machine too. (Leaving the
    // game also despawns everything, during shutdown; the scene is reloaded then anyway.)
    public override void OnNetworkDespawn()
    {
        burning.OnValueChanged -= Burned;
        var manager = NetworkManager != null ? NetworkManager : NetworkManager.Singleton;
        if (manager == null || manager.ShutdownInProgress || !manager.IsListening) return;
        prop.Vanish();
    }

    void Burned(bool was, bool now) => prop.ShowBurning(now, announce: true);

    void FixedUpdate()
    {
        if (Hosting) Publish();
    }

    // A NetworkVariable only sends when its value changes.
    void Publish()
    {
        var state = prop.State;
        if (state == null) return;
        burning.Value = state.Burning;
        levitating.Value = prop.grid != null && prop.grid.World != null && state.Levitating(prop.grid.World.Tick);
        hp.Value = state.Hp;
    }

    /// <summary>
    /// Host: the prop broke (or blew up). Everyone sees it go (unless <paramref name="quiet"/>: an item
    /// whose spill the chemistry already logs), then it is gone from every machine.
    /// </summary>
    public void Broke(bool exploded, bool quiet = false)
    {
        if (!Hosting) return;
        if (!quiet) BrokeRpc(exploded, transform.position);
        NetworkObject.Despawn(true);
    }

    [Rpc(SendTo.NotServer)]
    void BrokeRpc(bool exploded, Vector3 at)
    {
        transform.position = at;
        prop.ShowBreak(exploded);
    }
}
