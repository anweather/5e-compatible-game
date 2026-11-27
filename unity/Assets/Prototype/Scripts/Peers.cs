using Chem.Lockstep;
using UnityEngine;

// Finds a grid's IPeers (IPeers.cs): the NetChem on its GameObject if it has one, else the one SoloPeers. Looked up
// at each use, as the view looked up NetChem, so a grid that gains or loses its NetChem is answered as it is now.
public static class Peers
{
    /// <summary>The adapter for no other peers.</summary>
    public static readonly IPeers Solo = new SoloPeers();

    /// <summary>The peers of the grid on <paramref name="grid"/>'s GameObject; Solo for null or a grid with no NetChem.</summary>
    public static IPeers Of(Component grid) => grid != null && grid.TryGetComponent<IPeers>(out var peers) ? peers : Solo;

    /// <summary>A peer's name in the room by its slot (a client id online); the first, the host or solo, is "Player 1".</summary>
    public static string NameOf(int slot) => LockstepChannels.PeerName(new PeerId(slot));

    /// <summary>Hosting or a client: the session is up.</summary>
    public static bool Online(this IPeers peers) => peers.Role != PeerRole.Solo;

    /// <summary>Online and not the host: shared state (props, effects, rule files) is the host's to decide (D4).</summary>
    public static bool IsClient(this IPeers peers) => peers.Role == PeerRole.Client;
}
