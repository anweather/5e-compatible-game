using Chem;
using Chem.Lockstep;

// Live rule changes over the network (docs/RULES_MCP.md "Change modes", World.Room.cs), on the session's room channel.
// Anyone asks with the mod's patch; the host names who asked and may refuse at once by the room's change mode
// (LockstepChannels.Room: RoomPolicy, and a patch too long), every peer hearing the refusal (NetChem.Refused); else it
// stamps the command for the next tick and every peer decides it there itself. A late joiner gets the room in the
// host's snapshot. An online reset keeps the room.
public partial class NetChem
{
    LockstepChannel<RoomCommand> room;

    public string LocalPeer => PeerName(NetworkManager.LocalClientId);

    public static string PeerName(ulong clientId) => LockstepChannels.PeerName(NgoLockstepTransport.PeerOf(clientId));

    public void RequestRoom(RoomCommand command) => session?.Request(room, command);

    public void StampFound(string peer, ThingId thing)
    {
        if (IsServer) session?.Stamp(room, RoomCommand.Found(peer, thing));
    }
}
