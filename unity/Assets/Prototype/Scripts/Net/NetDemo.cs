using Unity.Netcode;
using UnityEngine;

// Starts the scripted 4-player demo on every machine at once: the host
// picks a start tick a few seconds ahead and tells everyone, and each
// player's NetDemoDirector plays its own part from that tick.
[RequireComponent(typeof(ChemGrid))]
public class NetDemo : NetworkBehaviour
{
    const int LeadTicks = 18;

    public void StartDemo()
    {
        if (!IsServer) return;
        StartDemoRpc(GetComponent<ChemGrid>().World.Tick + LeadTicks);
    }

    [Rpc(SendTo.ClientsAndHost)]
    void StartDemoRpc(int startTick)
    {
        var wizard = NetworkManager.SpawnManager.GetLocalPlayerObject();
        if (wizard == null) return;
        var director = wizard.gameObject.AddComponent<NetDemoDirector>();
        director.Begin(GetComponent<ChemGrid>(), startTick, NetworkManager.LocalClientId);
    }
}
