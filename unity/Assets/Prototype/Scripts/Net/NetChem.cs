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
    // A joiner's held batches, or a busy frame's: no frame spends more than this stepping.
    const double CatchUpBudgetMs = 8.0;
    // Chunks are planned every few ticks: players move a chunk in seconds, not ticks.
    const int PlanEvery = 3;
    // How far from a client's wizard a punch may land, in cells (ChemEffects.Strike).
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

    // Host only: the chunks active once every stamped switch has run, the planner, and the tick a reset is stamped for.
    readonly HashSet<(int cx, int cy)> plannedAwake = new();
    ChunkPlanner planner = new();
    int resetAt;
    // Host only: where each client's casts were cast from, in the order sent, and the host's own cast's.
    readonly Dictionary<ulong, Queue<Vector3>> castFrom = new();
    Vector3 hostFrom;
    // Everyone: the shoves of the casts announced, by the tick they land on.
    readonly Dictionary<int, List<(CastCommand cast, ulong caster, Vector3 from)>> shoves = new();
    int desyncs;

    public bool Online => IsSpawned && session != null;

    /// <summary>
    /// Raised on every machine after a tick is applied, with the tick; at every HashEvery'th tick
    /// also with its checksum (null otherwise). The probe's online scenario compares these.
    /// </summary>
    public event System.Action<int, string> TickApplied;

    /// <summary>True once this client has found a checksum that differs from the host's (it stays true after a snapshot puts it right).</summary>
    public bool DesyncSeen => desyncs > 0;

    /// <summary>How many hash windows this client has found differing from the host's, each put right by a snapshot.</summary>
    public int Desyncs => desyncs;

    /// <summary>The network's tick: the world's own, plus the ticks before the last online reset.</summary>
    public int NetTick => session != null ? session.NetTick : grid.World.Tick;

    /// <summary>The last tick this machine holds the batch of: NetTick plus the ticks it knows of and hasn't run.</summary>
    public int KnownUpTo => session != null ? session.KnownUpTo : NetTick;

    /// <summary>
    /// Whether this machine is playing live: the host always; a client once it has loaded the host's snapshot and run
    /// the batches the host had stamped past it. A probe or a scenario waits on this before it acts.
    /// </summary>
    public bool CaughtUp => session != null && session.IsCaughtUp;

    /// <summary>The tick a late joiner's snapshot was taken at: it ran the ticks after it; 0 for the host.</summary>
    public int ReplayedUpTo { get; private set; }

    /// <summary>The countdown to an online reset, in seconds (J-014).</summary>
    public const float ResetCountdownSeconds = 3f;

    /// <summary>The countdown in ticks at the rules' tick rate (18 at 6 a second): the delay to pass StampReset.</summary>
    public int ResetCountdownTicks => Mathf.RoundToInt(ResetCountdownSeconds * Mathf.Max(1, grid.World.Rules.Sim.TicksPerSecond));

    /// <summary>
    /// A reset is coming at this network tick: raised on every machine as soon as the host stamps
    /// it, for a countdown. At that tick the world is rebuilt (ChemGrid.WorldReset fires).
    /// </summary>
    public event System.Action<int> ResetComing;

    /// <summary>True while ChemGrid.WorldReset fires for an online reset happening now (not a snapshot loading).</summary>
    public bool Resetting { get; private set; }

    /// <summary>The network tick of the last online reset (0 for none).</summary>
    public int LastResetTick => session != null ? session.LastResetTick : 0;

    void Awake()
    {
        grid = GetComponent<ChemGrid>();
        over = () => budget.Elapsed.TotalMilliseconds >= CatchUpBudgetMs;
    }

    public override void OnNetworkSpawn()
    {
        grid.ExternalTicks = true;
        // The host keeps the mods it had on solo, as the room's starting mods; a client loads the host's world.
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
            // The map the host is on and the spawn it started in (SpawnPoints) go in the hello: joiners start beside it.
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

    // The same channels in the same order on every peer (the hello checks them).
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

    // --- the transport's RPCs ------------------------------------------------------

    // A client's NetChem has spawned: the host sends it the hello (before, an RPC could meet an object not spawned yet).
    [Rpc(SendTo.Server)]
    void ReadyRpc(RpcParams rpc = default) => transport?.Ready(rpc.Receive.SenderClientId);

    [Rpc(SendTo.Server)]
    void ToHostRpc(byte[] bytes, RpcParams rpc = default) => transport?.Arrived(rpc.Receive.SenderClientId, bytes);

    [Rpc(SendTo.SpecifiedInParams)]
    void ToClientRpc(byte[] bytes, RpcParams rpc = default) => transport?.Arrived(NetworkManager.ServerClientId, bytes);

    // Host: a client is a peer; it hears the hello.
    void PeerJoined(ulong client) => session.PeerJoined(NgoLockstepTransport.PeerOf(client), PeerName(client));

    // Host: the core forgets a wizard whose player has gone.
    void PeerLeft(ulong client)
    {
        session.PeerLeft(NgoLockstepTransport.PeerOf(client));
        castFrom.Remove(client);
        session.Stamp(strikes, StrikeCommand.Leave(PlayerName(client)));
    }

    // Client: the host's hello. On another map than the host's, a joiner loads the host's (the scene is built for the
    // map it starts on) and joins again; else it starts where the host did and asks for the host's world.
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
        // A host on another map may run other rules: go there first.
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

    // --- the doors ---------------------------------------------------------------

    /// <summary>
    /// Asks the host to cast a spell. It takes effect on every machine at the same tick, after
    /// <paramref name="delayTicks"/>; <paramref name="from"/> is where the caster stands (a burst's centre).
    /// </summary>
    public void RequestCast(CastCommand command, int delayTicks, Vector3 from)
    {
        if (session == null) return;
        if (IsServer) hostFrom = from;
        else CastFromRpc(from);
        session.Request(casts, command, delayTicks);
    }

    // Beside the cast that follows it on the same reliable channel, so the host takes them in the same order.
    [Rpc(SendTo.Server)]
    void CastFromRpc(Vector3 from, RpcParams rpc = default)
    {
        ulong caster = rpc.Receive.SenderClientId;
        if (!castFrom.TryGetValue(caster, out var q)) castFrom[caster] = q = new Queue<Vector3>();
        q.Enqueue(from);
    }

    /// <summary>Host only: stamps a physics prop's effect on cells into the next tick's batch, for every machine.</summary>
    public void StampEffect(EffectCommand effect)
    {
        if (IsServer && session != null) session.Stamp(effects, effect);
    }

    /// <summary>Client: asks the host to hit a pane cell for this client's wizard (ChemEffects.Strike).</summary>
    public void RequestStrike(Vector2Int cell) => RequestStrikeRpc(cell.x, cell.y);

    [Rpc(SendTo.Server)]
    void RequestStrikeRpc(int x, int y, RpcParams rpc = default)
    {
        var wizard = NetworkManager.SpawnManager.GetPlayerNetworkObject(rpc.Receive.SenderClientId);
        if (wizard == null || !grid.World.InBounds(x, y) || grid.World.At(x, y).T != TerrainIds.Pane) return;
        var at = grid.CellAt(wizard.transform.position);
        if (Mathf.Max(Mathf.Abs(at.x - x), Mathf.Abs(at.y - y)) <= StrikeReachCells) StampEffect(new EffectCommand(EffectKind.Shatter, x, y, "", ThingId.None));
    }

    /// <summary>Client: asks the host to work a gate for this client's wizard (ChemEffects.SwingNear).</summary>
    public void RequestSwing(Vector2Int cell) => RequestSwingRpc(cell.x, cell.y);

    [Rpc(SendTo.Server)]
    void RequestSwingRpc(int x, int y, RpcParams rpc = default)
    {
        var wizard = NetworkManager.SpawnManager.GetPlayerNetworkObject(rpc.Receive.SenderClientId);
        if (wizard == null || !grid.World.InBounds(x, y) || grid.World.TerrainOf(grid.World.At(x, y)).SwingsTo == null) return;
        var at = grid.CellAt(wizard.transform.position);
        if (Mathf.Max(Mathf.Abs(at.x - x), Mathf.Abs(at.y - y)) <= ChemEffects.SwingReachCells) StampEffect(new EffectCommand(EffectKind.Swing, x, y, "", ThingId.None));
    }

    /// <summary>
    /// Asks the host to run a defense command (the start pedestal: DefenseField). Anyone may ask; the
    /// host stamps it into the next tick's batch, so every machine starts the waves on the same tick.
    /// </summary>
    public void RequestDefense(DefenseCommand command) => session?.Request(defense, command);

    /// <summary>This player's name in the core's casts (World.CastOf).</summary>
    public string LocalPlayer => PlayerName(NetworkManager.LocalClientId);

    /// <summary>A client's name in the core's casts.</summary>
    public static string PlayerName(ulong clientId) => LockstepChannels.PlayerName(NgoLockstepTransport.PeerOf(clientId));

    /// <summary>Asks the host to run a player's combo input; it lands on every machine at the same tick (the host names the player).</summary>
    public void RequestCombo(ComboCommand command) => session?.Request(combos, command);

    /// <summary>Asks the host to run this wizard's stand or strike; it lands on every machine at the same tick.</summary>
    public void RequestStrikeCommand(StrikeCommand command) => session?.Request(strikes, command);

    /// <summary>Host only: the dragon is summoned on the next tick, on every machine (a Reset, once the view has it). A client's call does nothing.</summary>
    public void SummonDragon()
    {
        if (IsServer && session != null) session.Stamp(dragon, new DragonCommand());
    }

    /// <summary>
    /// Host only: the world resets for everyone after <paramref name="delayTicks"/> (at least 1;
    /// ResetCountdownTicks for the 3 s countdown). Every machine hears ResetComing at once, and at
    /// that tick rebuilds the world from its seed, the mods in force re-stamped with it (J-014).
    /// A reset already coming isn't moved.
    /// </summary>
    public void StampReset(int delayTicks)
    {
        if (IsServer) session?.StampReset(delayTicks);
    }

    void Update()
    {
        if (!IsSpawned || session == null) return;
        long from = System.Diagnostics.Stopwatch.GetTimestamp();
        transport.Deliver(session, PeerJoined, PeerLeft);
        if (session == null) return; // a hello sent this peer to the host's map
        budget.Restart();
        session.Update(Time.deltaTime, over);
        grid.StepMs = (System.Diagnostics.Stopwatch.GetTimestamp() - from) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        transport.Flush();
    }

    // --- the host's chunk plan and casts --------------------------------------------

    // Just before a tick's batch: the chunk switches around every wizard, against what is already planned, on it.
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

    // The plan from the world going online, or (a reset) from the start's reach a rebuilt world has active (#209).
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

    // Host: a cast taken, with the tick it lands on: everyone shows it early and shoves after it.
    void Taken(LockstepTaken taken)
    {
        if (taken.Channel != casts) return;
        var c = (CastCommand)taken.Command;
        ulong caster = NgoLockstepTransport.ClientOf(taken.From);
        var from = caster == NetworkManager.LocalClientId ? hostFrom
            : castFrom.TryGetValue(caster, out var q) && q.Count > 0 ? q.Dequeue() : Vector3.zero;
        // Only the server can look a player's wizard up by client id; everyone else finds it by its object id.
        var wizard = NetworkManager.SpawnManager.GetPlayerNetworkObject(caster);
        AnnounceCastRpc(new Unity.Collections.FixedString32Bytes(c.Spell), c.X, c.Y, c.Dir | (c.Charge << 8), c.AimX, c.AimY,
            wizard != null ? wizard.NetworkObjectId : ulong.MaxValue, caster, taken.Tick, from);
    }

    // Cosmetic first: every machine shows the cast (gesture, projectile, landing effect) before its tick arrives, at its
    // charge (a held beam is longer, a held storm wider), which rides in dirAndCharge's second byte. Then its shove waits for its tick.
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

    // --- everyone ------------------------------------------------------------------

    // A tick has finished: the physical side of its casts, and listeners.
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
        // The whole checksum, for the probes that listen: `?.` builds it only when one does.
        TickApplied?.Invoke(tick, tick % HashEvery == 0 ? session.Checksum() : null);
    }

    // The physical side of the casts just applied: the host moves the props (its physics is the
    // one that counts), and every player moves their own wizard unless they cast it.
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

    // The online reset (J-014): the world rebuilt from its seed, things.json, the clock and the weather
    // (ChemGrid.ResetWorld; its WorldReset listeners can read Resetting and LastResetTick). The room stays.
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

    /// <summary>
    /// The checksum every machine compares: the cells' hash, the rules' fingerprint, the world's
    /// things' (World.ThingsNow: the props' stable ids), the sky's, the defense's, the players' casts
    /// and grimoires, the dragon's and, last, the land's (JoinSnapshot.ChecksumOf's parts, joined by '/').
    /// </summary>
    public string CurrentChecksum => string.Join("/", JoinSnapshot.ChecksumOf(grid.World));

    // The session's world port: the grid's world, rules and batches, its reset and its snapshots.
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
