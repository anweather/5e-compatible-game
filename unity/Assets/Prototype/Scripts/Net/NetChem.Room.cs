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

    /// <summary>This peer's name in the room.</summary>
    public string LocalPeer => PeerName(NetworkManager.LocalClientId);

    /// <summary>A peer's name in the room, as the log shows it. The first (the host, or solo) is Player 1.</summary>
    public static string PeerName(ulong clientId) => LockstepChannels.PeerName(NgoLockstepTransport.PeerOf(clientId));

    /// <summary>Asks the host for a room change; the peer it names is overwritten with whoever sends it.</summary>
    public void RequestRoom(RoomCommand command) => session?.Request(room, command);

    /// <summary>
    /// Host: someone was granted a thing a mod added. Stamped straight in: finding something changes no rule, so a
    /// Locked room's RoomPolicy has no say.
    /// </summary>
    public void StampFound(string peer, ThingId thing)
    {
        if (IsServer) session?.Stamp(room, RoomCommand.Found(peer, thing));
    }
}
