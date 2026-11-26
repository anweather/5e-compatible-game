using System.Collections.Generic;
using Chem;
using Chem.Lockstep;
using Unity.Netcode;
using UnityEngine;

// Lockstep chemistry over the network (D7, D19): the core's LockstepSession over NGO (NgoLockstepTransport), with the
// grid as its world. Only commands travel; every machine steps its own Chem.World, and because the world is
// deterministic they all stay identical. The session holds the protocol (the host's clock, stamping, the tick batches,
// hash windows, joiners by snapshot, the reset; docs/design/core-package/multiplayer-ports.md); this is the adapter and
// the view's glue around it:
//
// - Every door the view calls (casts, prop effects, defense, combos, strikes, the room in NetChem.Room.cs, the dragon,
//   the reset) asks or stamps on the session's channel for it.
// - A cast's From (where its caster stood, the centre of its shove) is the view's, so it travels beside the request;
//   when the host takes the cast it tells everyone the tick it lands on (AnnounceCastRpc: the gesture early), and each
//   machine shoves after that tick runs (SpellForces): the host the props, each player its own wizard.
// - The host's physics props' effects, the chunk plan around every wizard (ChunkStreaming, in Stamping) and the
//   dragon's summons are the host's alone, stamped straight in. Strikes and swings keep their reach checks here.
// - A late joiner, and a client whose hash window differs from the host's, loads the host's world from a snapshot
//   (ChemGrid.LoadSnapshot) and plays on from it. A desync is logged as it was.
// - The host can reset the world for everyone (J-014, StampReset): every machine hears ResetComing at once (a
//   countdown), and at that tick rebuilds the world from its seed (ChemGrid.ResetWorld), the mods re-stamped.
[RequireComponent(typeof(ChemGrid))]
public partial class NetChem : NetworkBehaviour
{
    const int HashEvery = RollingHash.Every;
    const double CatchUpBudgetMs = 8.0;
    const int PlanEvery = 3;
    const int StrikeReachCells = 4;

    ChemGrid grid;
    LockstepSession session;
    NgoLockstepTransport transport;
    LockstepChannel<CastCommand> casts;
    LockstepChannel<EffectCommand> effects;
    LockstepChannel<ChunkSwitch> chunks;
    LockstepChannel<DefenseCommand> defense;
    LockstepChannel<ComboCommand> combos;
    LockstepChannel<DragonCommand> dragon;
    LockstepChannel<StrikeCommand> strikes;
    readonly System.Diagnostics.Stopwatch budget = new();
    System.Func<bool> over;

    readonly HashSet<(int cx, int cy)> plannedAwake = new();
    ChunkPlanner planner = new();
    int resetAt;
    readonly Dictionary<ulong, Queue<Vector3>> castFrom = new();
    Vector3 hostFrom;
    readonly Dictionary<int, List<(CastCommand cast, ulong caster, Vector3 from)>> shoves = new();
    int desyncs;

    public bool Online => IsSpawned && session != null;

    public event System.Action<int, string> TickApplied;

    public bool DesyncSeen => desyncs > 0;

    public int Desyncs => desyncs;

    public int NetTick => session != null ? session.NetTick : grid.World.Tick;

    public int KnownUpTo => session != null ? session.KnownUpTo : NetTick;

    public bool CaughtUp => session != null && session.IsCaughtUp;

    public int ReplayedUpTo { get; private set; }

    public const float ResetCountdownSeconds = 3f;

    public int ResetCountdownTicks => Mathf.RoundToInt(ResetCountdownSeconds * Mathf.Max(1, grid.World.Rules.Sim.TicksPerSecond));

    public event System.Action<int> ResetComing;

    public bool Resetting { get; private set; }

    public int LastResetTick => session != null ? session.LastResetTick : 0;

    void Awake()
    {
        grid = GetComponent<ChemGrid>();
        over = () => budget.Elapsed.TotalMilliseconds >= CatchUpBudgetMs;
    }

    public override void OnNetworkSpawn()
    {
        grid.ExternalTicks = true;
        if (IsServer)
        {
            grid.PlanRoom(NetSession.RoomMode);
            grid.ResetWorld();
        }
        transport = new NgoLockstepTransport(IsServer ? LockstepRole.Host : LockstepRole.Client, bytes => ToHostRpc(bytes),
            (client, bytes) => ToClientRpc(bytes, RpcTarget.Single(client, RpcTargetUse.Temp)));
        session = new LockstepSession(transport, new GridWorld(this), NgoLockstepTransport.PeerOf(NetworkManager.LocalClientId))
        {
            MostOwedSeconds = ChemGrid.MostOwedSeconds,
        };
        Register();
        session.TickApplied += Applied;
        session.Desynced += Desynced;
        session.Refused += Refused;
        session.ResetComing += tick => ResetComing?.Invoke(tick);
        session.Failed += Failed;
        session.Joined += Joined;
        shoves.Clear();
        castFrom.Clear();
        desyncs = 0;
        ReplayedUpTo = 0;
        if (IsServer)
        {
            session.MapId = ChemGrid.MapId;
            session.Start["spawn"] = SpawnPoints.StartIn ?? "";
            session.Stamping += PlanChunks;
            session.Taken += Taken;
            session.ResetComing += tick => resetAt = tick;
            resetAt = 0;
            ResetPlan(false);
            NetworkManager.OnClientDisconnectCallback += transport.Left;
        }
        else
        {
            session.AutoJoin = false;
            session.HelloReceived += Greeted;
            ReadyRpc();
        }
    }

    public override void OnNetworkDespawn()
    {
        grid.ExternalTicks = false;
        if (NetworkManager != null && transport != null) NetworkManager.OnClientDisconnectCallback -= transport.Left;
        session = null;
        transport = null;
    }

    void Register()
    {
        casts = session.Register(LockstepChannels.Casts(LockstepChannels.PlayerName));
        effects = session.Register(LockstepChannels.Effects());
        chunks = session.Register(LockstepChannels.Chunks());
        defense = session.Register(LockstepChannels.Defense());
        combos = session.Register(LockstepChannels.Combos(LockstepChannels.PlayerName, HoldMoments.Offered));
        dragon = session.Register(LockstepChannels.Dragon());
        session.Register(LockstepChannels.Course(LockstepChannels.PlayerName));
        strikes = session.Register(LockstepChannels.Strikes(LockstepChannels.PlayerName));
        room = session.Register(LockstepChannels.Room(LockstepChannels.PeerName));
    }

    [Rpc(SendTo.Server)]
    void ReadyRpc(RpcParams rpc = default) => transport?.Ready(rpc.Receive.SenderClientId);

    [Rpc(SendTo.Server)]
    void ToHostRpc(byte[] bytes, RpcParams rpc = default) => transport?.Arrived(rpc.Receive.SenderClientId, bytes);

    [Rpc(SendTo.SpecifiedInParams)]
    void ToClientRpc(byte[] bytes, RpcParams rpc = default) => transport?.Arrived(NetworkManager.ServerClientId, bytes);

    void PeerJoined(ulong client) => session.PeerJoined(NgoLockstepTransport.PeerOf(client), PeerName(client));

    void PeerLeft(ulong client)
    {
        session.PeerLeft(NgoLockstepTransport.PeerOf(client));
        castFrom.Remove(client);
        session.Stamp(strikes, StrikeCommand.Leave(PlayerName(client)));
    }

    void Greeted(LockstepHello hello)
    {
        if (hello.Map != ChemGrid.MapId)
        {
            NetSession.JoinOnHostMap(hello.Map);
            return;
        }
        SpawnPoints.StartIn = hello.Start.TryGetValue("spawn", out var spawn) ? spawn : "";
        session.Join();
    }

    void Failed(string why)
    {
        if (!IsServer && session?.Hello != null && session.Hello.Map != ChemGrid.MapId && !session.Synced)
        {
            NetSession.JoinOnHostMap(session.Hello.Map);
            return;
        }
        EventLog.Add($"<color=#ff8a80>{why}</color>");
        Debug.LogWarning("NetChem: " + why);
    }

    void Joined(int tick)
    {
        if (desyncs > 0)
        {
            EventLog.Add($"Put right from the host's world at tick {tick}");
            return;
        }
        ReplayedUpTo = tick;
        EventLog.Add($"Joined the yard at tick {tick}; catching up");
    }

    void Desynced(LockstepDesync desync)
    {
        desyncs++;
        EventLog.Add($"<color=#ff8a80>{desync.Text}</color>");
        Debug.LogWarning($"NetChem {desync.Text}");
    }

    void Refused(LockstepRefusal refusal)
    {
        if (refusal.Channel == room)
        {
            var command = (RoomCommand)refusal.Command;
            grid.NoteRefused(command, new RoomVerdict(false, refusal.Why, RoomPolicy.RefusedLine(command.Change, refusal.Why)));
            return;
        }
        Debug.Log($"NET: the host refused a command on {refusal.Channel.Name} from {refusal.Peer}: {refusal.Why}");
    }

    public void RequestCast(CastCommand command, int delayTicks, Vector3 from)
    {
        if (session == null) return;
        if (IsServer) hostFrom = from;
        else CastFromRpc(from);
        session.Request(casts, command, delayTicks);
    }

    [Rpc(SendTo.Server)]
    void CastFromRpc(Vector3 from, RpcParams rpc = default)
    {
        ulong caster = rpc.Receive.SenderClientId;
        if (!castFrom.TryGetValue(caster, out var q)) castFrom[caster] = q = new Queue<Vector3>();
        q.Enqueue(from);
    }

    public void StampEffect(EffectCommand effect)
    {
        if (IsServer && session != null) session.Stamp(effects, effect);
    }

    public void RequestStrike(Vector2Int cell) => RequestStrikeRpc(cell.x, cell.y);

    [Rpc(SendTo.Server)]
    void RequestStrikeRpc(int x, int y, RpcParams rpc = default)
    {
        var wizard = NetworkManager.SpawnManager.GetPlayerNetworkObject(rpc.Receive.SenderClientId);
        if (wizard == null || !grid.World.InBounds(x, y) || grid.World.At(x, y).T != TerrainIds.Pane) return;
        var at = grid.CellAt(wizard.transform.position);
        if (Mathf.Max(Mathf.Abs(at.x - x), Mathf.Abs(at.y - y)) <= StrikeReachCells) StampEffect(new EffectCommand(EffectKind.Shatter, x, y, "", ThingId.None));
    }

    public void RequestSwing(Vector2Int cell) => RequestSwingRpc(cell.x, cell.y);

    [Rpc(SendTo.Server)]
    void RequestSwingRpc(int x, int y, RpcParams rpc = default)
    {
        var wizard = NetworkManager.SpawnManager.GetPlayerNetworkObject(rpc.Receive.SenderClientId);
        if (wizard == null || !grid.World.InBounds(x, y) || grid.World.TerrainOf(grid.World.At(x, y)).SwingsTo == null) return;
        var at = grid.CellAt(wizard.transform.position);
        if (Mathf.Max(Mathf.Abs(at.x - x), Mathf.Abs(at.y - y)) <= ChemEffects.SwingReachCells) StampEffect(new EffectCommand(EffectKind.Swing, x, y, "", ThingId.None));
    }

    public void RequestDefense(DefenseCommand command) => session?.Request(defense, command);

    public string LocalPlayer => PlayerName(NetworkManager.LocalClientId);

    public static string PlayerName(ulong clientId) => LockstepChannels.PlayerName(NgoLockstepTransport.PeerOf(clientId));

    public void RequestCombo(ComboCommand command) => session?.Request(combos, command);

    public void RequestStrikeCommand(StrikeCommand command) => session?.Request(strikes, command);

    public void SummonDragon()
    {
        if (IsServer && session != null) session.Stamp(dragon, new DragonCommand());
    }

    public void StampReset(int delayTicks)
    {
        if (IsServer) session?.StampReset(delayTicks);
    }

    void Update()
    {
        if (!IsSpawned || session == null) return;
        long from = System.Diagnostics.Stopwatch.GetTimestamp();
        transport.Deliver(session, PeerJoined, PeerLeft);
        if (session == null) return;
        budget.Restart();
        session.Update(Time.deltaTime, over);
        grid.StepMs = (System.Diagnostics.Stopwatch.GetTimestamp() - from) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        transport.Flush();
    }

    void PlanChunks(int tick)
    {
        if (tick == resetAt) ResetPlan(true);
        if (!grid.streamChunks || tick % PlanEvery != 1) return;
        var plan = planner.Plan(grid.World, (cx, cy) => plannedAwake.Contains((cx, cy)), grid.PlayerCells(), plannedAwake);
        foreach (var s in plan)
        {
            if (s.Active) plannedAwake.Add((s.CX, s.CY));
            else plannedAwake.Remove((s.CX, s.CY));
            session.Stamp(chunks, s);
        }
    }

    void ResetPlan(bool reset)
    {
        plannedAwake.Clear();
        planner = new ChunkPlanner();
        if (!reset)
        {
            foreach (var c in grid.World.ActiveChunks) plannedAwake.Add(c);
            return;
        }
        foreach (var c in grid.World.ChunkCoords) plannedAwake.Add(c);
        if (grid.streamChunks)
            foreach (var s in ChunkStreaming.StartPlan(grid.World.ChunkCoords, (_, _) => true,
                new[] { (grid.PlayerStart.x, grid.PlayerStart.y) }, grid.World.Rules.Sim.Streaming.Reach))
                plannedAwake.Remove((s.CX, s.CY));
    }

    void Taken(LockstepTaken taken)
    {
        if (taken.Channel != casts) return;
        var c = (CastCommand)taken.Command;
        ulong caster = NgoLockstepTransport.ClientOf(taken.From);
        var from = caster == NetworkManager.LocalClientId ? hostFrom
            : castFrom.TryGetValue(caster, out var q) && q.Count > 0 ? q.Dequeue() : Vector3.zero;
        var wizard = NetworkManager.SpawnManager.GetPlayerNetworkObject(caster);
        AnnounceCastRpc(new Unity.Collections.FixedString32Bytes(c.Spell), c.X, c.Y, c.Dir | (c.Charge << 8), c.AimX, c.AimY,
            wizard != null ? wizard.NetworkObjectId : ulong.MaxValue, caster, taken.Tick, from);
    }

    [Rpc(SendTo.ClientsAndHost)]
    void AnnounceCastRpc(Unity.Collections.FixedString32Bytes spell, int x, int y, int dirAndCharge, int aimX, int aimY, ulong casterObject, ulong caster, int tick, Vector3 from)
    {
        var cast = new CastCommand(spell.ToString(), x, y, dirAndCharge & 0xff, (byte)(dirAndCharge >> 8), aimX, aimY, PlayerName(caster));
        if (NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(casterObject, out var player) && player.TryGetComponent<SpellCaster>(out var spellCaster))
            spellCaster.PlayCastEffect(cast);
        if (session == null || tick <= NetTick) return;
        if (!shoves.TryGetValue(tick, out var list)) shoves[tick] = list = new List<(CastCommand, ulong, Vector3)>();
        list.Add((cast, caster, from));
    }

    void Applied(TickBatch batch)
    {
        int tick = session.NetTick;
        if (shoves.Count > 0)
        {
            if (shoves.Remove(tick, out var list)) Shove(list);
            foreach (int t in new List<int>(shoves.Keys))
                if (t < tick) shoves.Remove(t);
        }
        grid.NoteTickDone();
        TickApplied?.Invoke(tick, tick % HashEvery == 0 ? session.Checksum() : null);
    }

    void Shove(List<(CastCommand cast, ulong caster, Vector3 from)> casts)
    {
        var mine = NetworkManager.SpawnManager.GetLocalPlayerObject();
        var myWizard = mine != null ? mine.GetComponent<Knockback>() : null;
        foreach (var (cast, caster, from) in casts)
        {
            bool castHere = caster == NetworkManager.LocalClientId;
            SpellForces.ApplyOnline(grid, cast, from, IsServer, castHere ? null : myWizard, castHere && mine != null ? mine.transform : null);
        }
    }

    void Rebuild()
    {
        Resetting = true;
        try
        {
            grid.ResetWorld();
        }
        finally
        {
            Resetting = false;
        }
        EventLog.Add("The world resets");
    }

    public string CurrentChecksum => string.Join("/", JoinSnapshot.ChecksumOf(grid.World));

    sealed class GridWorld : ILockstepWorld
    {
        readonly NetChem net;

        public GridWorld(NetChem net) => this.net = net;

        public World World => net.grid.World;
        public RuleSet Rules => net.grid.CurrentRules;
        public void Prepare(TickBatch batch) => net.grid.PrepareBatch(batch);
        public void Rebuild() => net.Rebuild();
        public JoinSnapshot Snapshot() => JoinSnapshot.Take(World, World.Source, Rules);
        public void Load(JoinSnapshot snapshot) => net.grid.LoadSnapshot(snapshot);
    }
}
