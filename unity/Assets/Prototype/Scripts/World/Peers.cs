using Chem.Lockstep;
using UnityEngine;

// Finds a grid's IPeers (IPeers.cs): the NetChem on its GameObject if it has one, else the one SoloPeers. Looked up
// at each use, as the view looked up NetChem, so a grid that gains or loses its NetChem is answered as it is now.
public static class Peers
{
    public static readonly IPeers Solo = new SoloPeers();

    public static IPeers Of(Component grid) => grid != null && grid.TryGetComponent<IPeers>(out var peers) ? peers : Solo;

    public static string NameOf(int slot) => LockstepChannels.PeerName(new PeerId(slot));

    public static string PlayerOf(int slot) => LockstepChannels.PlayerName(new PeerId(slot));

    public static IPeers InPlay
    {
        get
        {
            var current = ChemGrid.Current;
            return Of(current != null ? current.Grid : null);
        }
    }

    public static bool Online(this IPeers peers) => peers.Role != PeerRole.Solo;

    public static bool IsClient(this IPeers peers) => peers.Role == PeerRole.Client;
}
