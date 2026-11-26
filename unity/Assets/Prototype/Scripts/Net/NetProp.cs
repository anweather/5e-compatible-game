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
    readonly NetworkVariable<ulong> thing = new(ThingId.None.Value);
    ThingId assigned = ThingId.None;

    PhysicsProp prop;
    Rigidbody body;

    public bool Puppet => IsSpawned && !IsServer;
    public bool Hosting => IsSpawned && IsServer;

    public bool Burning => burning.Value;
    public bool Levitating => levitating.Value;
    public int Hp => hp.Value;
    public ThingId Id => assigned.IsNone ? new ThingId(thing.Value) : assigned;

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
        var spawner = FindAnyObjectByType<ThingSpawner>();
        if (spawner != null) spawner.Adopt(this);
        body.isKinematic = true;
        body.interpolation = RigidbodyInterpolation.None;
        burning.OnValueChanged += Burned;
        prop.ShowBurning(burning.Value, announce: false);
    }

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

    void Publish()
    {
        var state = prop.State;
        if (state == null) return;
        burning.Value = state.Burning;
        levitating.Value = prop.grid != null && prop.grid.World != null && state.Levitating(prop.grid.World.Tick);
        hp.Value = state.Hp;
    }

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

    public void Teleport(Vector3 to)
    {
        if (!Hosting) return;
        if (TryGetComponent<Unity.Netcode.Components.NetworkTransform>(out var follow) && follow.CanCommitToTransform)
            follow.Teleport(to, transform.rotation, transform.localScale);
        else transform.position = to;
    }

    public const float MaxKick = 3f * Forces.ImpulsePerPush;

    public void Kick(Vector3 impulse)
    {
        if (IsServer) KickHere(impulse);
        else KickRpc(impulse);
    }

    [Rpc(SendTo.Server)]
    void KickRpc(Vector3 impulse) => KickHere(impulse);

    void KickHere(Vector3 impulse)
    {
        if (TryGetComponent<Rigidbody>(out var rb) && !rb.isKinematic) rb.AddForce(Vector3.ClampMagnitude(impulse, MaxKick), ForceMode.Impulse);
    }
}
