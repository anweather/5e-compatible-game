using Unity.Netcode;
using UnityEngine;

// The ball court's score, online (BallCourt): the host's physics moves the
// ball, so only the host decides goals and puts the ball back on the centre
// spot. It keeps the goals at each end in network variables, so every
// machine's scoreboard shows the same numbers (a late joiner's too), and a
// goal is announced to everyone. Solo it is never spawned and BallCourt keeps
// its own score.
[RequireComponent(typeof(NetworkObject))]
public class NetCourt : NetworkBehaviour
{
    readonly NetworkVariable<int> west = new();
    readonly NetworkVariable<int> east = new();

    /// <summary>The spawned one while a game is online; null solo.</summary>
    public static NetCourt Live { get; private set; }

    /// <summary>Goals at the west end and at the east end, the same on every machine.</summary>
    public int West => west.Value;
    public int East => east.Value;

    ChemGrid grid;
    NetChem net;

    public override void OnNetworkSpawn()
    {
        Live = this;
        if (!IsServer) return;
        grid = FindAnyObjectByType<ChemGrid>();
        net = grid != null ? grid.GetComponent<NetChem>() : null;
        if (grid != null) grid.WorldReset += Nil;
    }

    public override void OnNetworkDespawn()
    {
        if (Live == this) Live = null;
        if (grid != null) grid.WorldReset -= Nil;
    }

    // Host, at an online reset (OnlineReset): 0-0 for everyone. The ball is a thing, back on its spot.
    void Nil()
    {
        if (!OnlineReset.Happened(net)) return;
        west.Value = 0;
        east.Value = 0;
    }

    /// <summary>Host: a goal at the east end (or the west), counted and announced to everyone.</summary>
    public void Scored(bool eastEnd)
    {
        if (!IsServer) return;
        if (eastEnd) east.Value++;
        else west.Value++;
        GoalRpc(eastEnd, west.Value, east.Value);
    }

    [Rpc(SendTo.ClientsAndHost)]
    void GoalRpc(bool eastEnd, int westGoals, int eastGoals)
    {
        // The counts travel with the call: the network variables may not have arrived yet.
        if (IsSpawned) BallCourt.Announce(eastEnd, westGoals, eastGoals);
    }
}
