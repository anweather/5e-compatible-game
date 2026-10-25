using Unity.Netcode;
using UnityEngine;

// A physics prop (or a carried item) in multiplayer: an in-scene NetworkObject
// with a server-authoritative NetworkTransform, placed by PropBuilder.
//
// The host simulates it: its Rigidbody, the cell under it, burning, hit
// points, what it does to cells (stamped through NetChem, ChemEffects). On
// clients it is a puppet: a kinematic body moved by the NetworkTransform
// (interpolated), which shows the flames and the blast the host reports.
// Only what the eye needs is replicated: burning, levitating and hit points.
//
// When it breaks, the host despawns it without destroying it (Netcode's way
// for in-scene objects) and every machine hides it; a late joiner is told it
// is despawned while joining and hides it too.
[RequireComponent(typeof(PhysicsProp), typeof(Rigidbody))]
public class NetProp : NetworkBehaviour
{
    readonly NetworkVariable<bool> burning = new();
    readonly NetworkVariable<bool> levitating = new();
    readonly NetworkVariable<int> hp = new();

    PhysicsProp prop;
    Rigidbody body;

    /// <summary>A client's copy: follows the host's, simulates nothing.</summary>
    public bool Puppet => IsSpawned && !IsServer;
    /// <summary>The host's copy of a spawned prop: the one that counts.</summary>
    public bool Hosting => IsSpawned && IsServer;

    public bool Burning => burning.Value;
    public bool Levitating => levitating.Value;
    public int Hp => hp.Value;

    void Awake()
    {
        prop = GetComponent<PhysicsProp>();
        body = GetComponent<Rigidbody>();
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            Publish();
            return;
        }
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
        NetworkObject.Despawn(false);
    }

    [Rpc(SendTo.NotServer)]
    void BrokeRpc(bool exploded, Vector3 at)
    {
        transform.position = at;
        prop.ShowBreak(exploded);
    }
}
