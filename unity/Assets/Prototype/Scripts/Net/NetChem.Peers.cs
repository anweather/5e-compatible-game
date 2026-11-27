// NetChem as the view's IPeers (IPeers.cs): the NGO adapter. Until its lockstep session is up (Online) it answers as
// SoloPeers does: Solo, caught up, the first peer's name. The sends are NetChem's own doors (RequestCast, RequestCombo,
// RequestRoom, RequestStrike, ...), which already match the port, so a send through it is the same call it always was.
public partial class NetChem : IPeers
{
    PeerRole IPeers.Role
    {
        get
        {
            if (!Online) return PeerRole.Solo;
            return IsServer ? PeerRole.Host : PeerRole.Client;
        }
    }

    string IPeers.Me => Online ? LocalPeer : PeerName(0);

    string IPeers.Player => Online ? LocalPlayer : SpellCaster.SoloPlayer;

    bool IPeers.CaughtUp => !Online || CaughtUp;
}
