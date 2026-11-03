using System.Collections.Generic;
using System.Linq;
using Chem;
using Unity.Netcode;

// Live rule changes over the network (docs/RULES_MCP.md "Change modes", World.Room.cs).
// Anyone asks with the mod's patch (RoomIntent); the host names who asked, may refuse at
// once by the room's change mode, and stamps the command just before the next tick's
// batch, as a defense command goes (both on a CommandChannel). Every peer then decides it
// itself at that tick. A late joiner gets the room as its history starts, then the commands
// since. An online reset keeps the room, so the host notes it at each one.
public partial class NetChem
{
    // A patch is rules, not a world: past this the host refuses it before it costs anyone anything.
    const int MaxPatchChars = 64 * 1024;

    // The room commands on their way (made in Awake, with the other channels).
    CommandChannel<RoomIntent> roomCommands;

    // Host only: the room's mods where the kept history starts, and who's waiting for that history.
    RoomIntent[] roomStart = System.Array.Empty<RoomIntent>();
    readonly List<ulong> joiners = new();

    /// <summary>This peer's name in the room.</summary>
    public string LocalPeer => PeerName(NetworkManager.LocalClientId);

    /// <summary>A peer's name in the room, as the log shows it. The first (the host, or solo) is Player 1.</summary>
    public static string PeerName(ulong clientId) => $"Player {clientId + 1}";

    /// <summary>Asks the host for a room change; the peer it names is overwritten with whoever sends it.</summary>
    public void RequestRoom(RoomCommand command) => RequestRoomRpc(RoomIntent.From(command, 0));

    /// <summary>
    /// Host: someone was granted a thing a mod added. Stamped straight in, past RequestRoomRpc: finding
    /// something changes no rule, so a Locked room's RoomPolicy has no say.
    /// </summary>
    public void StampFound(string peer, ThingId thing) => roomCommands.Request(RoomIntent.From(RoomCommand.Found(peer, thing), 0));

    void ClearRoom()
    {
        roomCommands.Clear();
        joiners.Clear();
        roomStart = System.Array.Empty<RoomIntent>();
    }

    // The room's mods as the world just built has them: where a joiner's replay starts from.
    void NoteRoomStart() => roomStart = grid.World.Room.Mods.Select(RoomIntent.From).ToArray();

    // A joiner waits while a reset is stamped but hasn't reached this machine's world, whose room it needs.
    void ServeJoiners()
    {
        if (joiners.Count == 0 || NetTick + 1 < historyFrom) return;
        foreach (var client in joiners) SendHistory(client);
        joiners.Clear();
    }

    // The sender is who's asking, whatever it says.
    [Rpc(SendTo.Server)]
    void RequestRoomRpc(RoomIntent intent, RpcParams rpc = default)
    {
        intent.Peer = PeerName(rpc.Receive.SenderClientId);
        var room = grid.World.Room;
        string why = RoomPolicy.Refusal(room.Mode, room.Host, intent.Peer);
        if (why == null && intent.Json.Length > MaxPatchChars) why = "that patch is too long for a live change";
        if (why == null) roomCommands.Request(intent);
        else RoomRefusedRpc(intent, why);
    }

    [Rpc(SendTo.ClientsAndHost)]
    void RoomRefusedRpc(RoomIntent intent, string why)
    {
        var command = intent.ToCommand();
        grid.NoteRefused(command, new RoomVerdict(false, why, RoomPolicy.RefusedLine(command.Change, why)));
    }

    void StampRoom()
    {
        var stamped = roomCommands.Stamp(hostTick);
        if (stamped.Length > 0) RoomStampedRpc(hostTick, stamped);
    }

    [Rpc(SendTo.ClientsAndHost)]
    void RoomStampedRpc(int tick, RoomIntent[] commands) => roomCommands.Receive(tick, commands);

    [Rpc(SendTo.SpecifiedInParams)]
    void SendRoomHistoryRpc(byte mode, string host, RoomIntent[] start, RoomIntent[] past, RpcParams rpc = default)
    {
        roomCommands.Joining(past);
        grid.JoinRoom((ChangeMode)mode, host, start.Select(m => m.ToMod()).ToList());
    }
}

/// <summary>
/// A room command (Chem.RoomCommand) on its way, stamped with the sim tick it takes effect on; or
/// a mod on in a room, with the tick it went on.
/// </summary>
public struct RoomIntent : INetworkSerializable, ITickStamped
{
    public int Tick;
    public RoomChange Change;
    public string Peer, Mod, Json, Title;

    int ITickStamped.Tick => Tick;

    void ITickStamped.StampFor(int tick) => Tick = tick;

    public static RoomIntent From(RoomCommand c, int tick) => new RoomIntent
    {
        Tick = tick,
        Change = c.Change,
        Peer = c.Peer,
        Mod = c.Mod,
        Json = c.Json,
        Title = c.Title ?? "",
    };

    public static RoomIntent From(RoomMod m) => new RoomIntent { Tick = m.Tick, Peer = m.By, Mod = m.Id, Json = m.Json, Title = m.Title };

    public RoomCommand ToCommand() => new RoomCommand(Change, Peer, Mod, Json, Title);

    public RoomMod ToMod() => new RoomMod { Id = Mod, Title = Title, By = Peer, Tick = Tick, Json = Json };

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref Tick);
        serializer.SerializeValue(ref Change);
        serializer.SerializeValue(ref Peer);
        serializer.SerializeValue(ref Mod);
        serializer.SerializeValue(ref Json);
        serializer.SerializeValue(ref Title);
    }
}
