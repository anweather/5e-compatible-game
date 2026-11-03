using System.Collections.Generic;
using System.Linq;
using Chem;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

// Lockstep chemistry over the network, the same design as the web
// sandbox's (docs/LIVE_LAB.md, docs/MULTIPLAYER.md). Only cast intents
// travel; every machine steps its own Chem.World, and because the world is
// deterministic they all stay identical.
//
// - Anyone casting sends a request to the host.
// - The host owns the clock: each sim tick it stamps the casts due and
//   broadcasts the batch (empty batches too, so clients know a tick is
//   complete). Everyone, the host included, applies a batch and steps.
// - A rule change (a mod on or off; RuleSwitcher, the room's change mode) travels
//   the same way (NetChem.Room.cs): anyone asks with the patch, the host stamps it
//   for the next tick, and every machine decides it there itself (World.Room.cs).
// - A batch every 5 s carries the host's checksum of the window just ended: the
//   cells' hash, a slice a tick (RollingHash), the rules' fingerprint, the world's
//   things' (the props' stable ids) and the sky's (the time spells' clock and
//   weather); a client that disagrees logs a desync. The time spells are casts like any other, so a
//   late joiner's replay rebuilds the clock and the held weather too.
// - What physics props do to cells (a barrel's blast, a lantern's spill;
//   ChemEffects) is seen by the host, whose physics is the only one that
//   counts, and stamped into the next batch like a cast.
// - When a batch's casts are applied, their shoves are too (SpellForces): the
//   host moves the props, and each player moves their own wizard, from the
//   caster's position carried in the cast.
// - Which chunks are simulated follows the players (ChunkStreaming): the host
//   plans it from every wizard's position and stamps the switches into its
//   batches, so every machine pauses the same chunks at the same tick.
// - The defense field's start command (DefenseField) travels like a cast:
//   anyone asks the host, which sends it for the next tick just before that
//   tick's batch, so the waves start on the same tick everywhere. The waves
//   and raiders are then pure chemistry: every machine runs them, and the
//   checksum's last part is their hash (World.DefenseHash).
// - Players' combo input (ComboCommand: an element loaded, a cast cancelled, the d-pad's spells)
//   travels like a cast: anyone asks, the host names the player and stamps it into the next
//   batch, and every machine drains the same meters. A cast that goes off is then aimed by the
//   player whose it is (SpellCaster.Combos), as an ordinary cast.
// - The host can reset the world for everyone (J-014, StampReset): every machine hears
//   ResetComing at once (a countdown), and at that tick rebuilds the world from its seed
//   (ChemGrid.ResetWorld), the mods in force re-stamped in the same batch. The network's ticks
//   keep counting; the rebuilt world's own tick starts again at 0 (tickBase), and so do its clock
//   and weather. The host keeps the history from the last reset on only.
// - A late joiner asks for the history (casts, rule switches, prop effects, defense commands,
//   combo input, the dragon's summons and chunk switches) and replays it from tick 0, or from the last reset, which it replays first.
// - The host alone summons the dragon (SummonDragon): stamped for the next tick like a defense command,
//   then it's rules (World.Dragon.cs): every machine flies the same flight from there.
// - A wizard's stands and unarmed strikes (WizardStrike, World.Strike.cs) travel as combo input
//   does: its owner asks, the host names the player and stamps them into the next tick. The host
//   sends a leaving player's Leave. A late joiner gets them with the history; a stand that a later
//   one replaces before any strike is dropped from it, so walking doesn't make it grow.
[RequireComponent(typeof(ChemGrid))]
public partial class NetChem : NetworkBehaviour
{
    const int HashEvery = RollingHash.Every, HashesKept = 4;
    // A late joiner replays the whole history; spread it so no frame spends more than this.
    const double CatchUpBudgetMs = 8.0;

    ChemGrid grid;

    // Host only.
    float clock;
    int hostTick;
    readonly List<CastIntent> scheduled = new();
    readonly List<CastIntent> history = new();
    readonly List<RuleChangeMsg> ruleHistory = new();
    readonly List<EffectIntent> pendingEffects = new();
    readonly List<EffectIntent> effectHistory = new();
    readonly List<ChunkIntent> chunkHistory = new();
    readonly List<DefenseIntent> pendingDefense = new();
    readonly List<DefenseIntent> defenseHistory = new();
    readonly List<ComboIntent> pendingCombos = new();
    readonly List<ComboIntent> comboHistory = new();
    readonly List<StrikeIntent> pendingStrikes = new();
    readonly List<StrikeIntent> strikeHistory = new();
    // The last tick with a strike in the history: stands before it are kept.
    int lastStrikeTick;
    bool dragonAsked;
    readonly List<int> dragonHistory = new();
    // The chunks awake once every stamped switch has run (the host's world is a tick or so behind).
    readonly HashSet<(int cx, int cy)> plannedAwake = new();
    // Plans the switches, lingering chunks still live; host only, since only the switches are shared.
    ChunkPlanner planner = new();
    // Chunks are planned every few ticks: players move a chunk in seconds, not ticks.
    const int PlanEvery = 3;

    // Everyone.
    readonly Dictionary<int, TickBatch> batches = new();
    // The casts of batches that arrived live (not replayed history), for their shoves.
    readonly Dictionary<int, CastIntent[]> liveCasts = new();
    // Defense commands stamped for a tick, sent just before its batch, waiting to join it.
    readonly Dictionary<int, DefenseIntent[]> defenseFor = new();
    // The same for combo input.
    readonly Dictionary<int, ComboIntent[]> combosFor = new();
    readonly Dictionary<int, StrikeIntent[]> strikesFor = new();
    // And the dragon's summons, by the tick they're stamped for.
    readonly HashSet<int> dragonFor = new();
    // Joining: the combo input of the history, sent just before the rest of it.
    ComboIntent[] pastCombos = System.Array.Empty<ComboIntent>();
    StrikeIntent[] pastStrikes = System.Array.Empty<StrikeIntent>();
    // Joining: the ticks of the dragon's summons in the history, the same way.
    int[] pastDragon = System.Array.Empty<int>();
    // Windows of the desync check (RollingHash) still waiting for the other side's: either can come first.
    readonly Dictionary<int, string> hostHashes = new(), ownHashes = new();
    RollingHash rolling = new();
    // On the host: the last finished window, until a batch takes it.
    int windowTick;
    string windowHash = "";
    int knownUpTo;
    bool synced;
    bool desyncReported;

    public bool Online => IsSpawned;

    /// <summary>
    /// Raised on every machine after a tick is applied, with the tick; at every HashEvery'th tick
    /// also with its checksum (null otherwise). The probe's online scenario compares these.
    /// </summary>
    public event System.Action<int, string> TickApplied;

    /// <summary>True once this client has found a checksum that differs from the host's.</summary>
    public bool DesyncSeen => desyncReported;

    // The network tick the world's tick 0 sits at: 0 until an online reset rebuilds the world
    // (J-014), after which the world counts from 0 again (so its clock and weather start again)
    // while the network's ticks keep counting.
    int tickBase;
    // Joining, the history up to this tick was replayed: a reset in it is a replay, not live.
    int replayedUpTo;
    // Host only: the tick a stamped reset happens at (0: none coming), and where the kept history starts.
    int resetAt, historyFrom = 1;

    /// <summary>The network's tick: the world's own, plus the ticks before the last online reset.</summary>
    public int NetTick => tickBase + grid.World.Tick;

    /// <summary>
    /// Whether this machine is playing live: the host always; a client once it has the history
    /// (SendHistoryRpc) and has replayed all of it. Joining, the replay
    /// runs CatchUpBudgetMs a frame, so for a while the world is in the past; a probe or a
    /// scenario waits on this before it acts. It stays true after (live batches keep it within a
    /// tick or two of the host), so it doesn't flicker with each batch's arrival.
    /// </summary>
    public bool CaughtUp => synced && NetTick >= replayedUpTo;

    /// <summary>The countdown to an online reset, in seconds (J-014).</summary>
    public const float ResetCountdownSeconds = 3f;

    /// <summary>The countdown in ticks at the rules' tick rate (18 at 6 a second): the delay to pass StampReset.</summary>
    public int ResetCountdownTicks => Mathf.RoundToInt(ResetCountdownSeconds * Mathf.Max(1, grid.World.Rules.Sim.TicksPerSecond));

    /// <summary>
    /// A reset is coming at this network tick: raised on every machine as soon as the host stamps
    /// it, for a countdown. At that tick the world is rebuilt (ChemGrid.WorldReset fires).
    /// </summary>
    public event System.Action<int> ResetComing;

    /// <summary>
    /// While ChemGrid.WorldReset fires for an online reset: true for a late joiner replaying the
    /// history (the reset that heads it), false for a reset happening now.
    /// </summary>
    public bool ResetIsReplay { get; private set; }

    /// <summary>The network tick of the last online reset (0 for none).</summary>
    public int LastResetTick { get; private set; }

    void Awake() => grid = GetComponent<ChemGrid>();

    public override void OnNetworkSpawn()
    {
        // The host keeps the mods it had on solo, as the room's starting mods; a client hears the room from the host.
        if (IsServer) grid.PlanRoom(NetSession.RoomMode);
        else grid.AwaitRoom();
        grid.ExternalTicks = true;
        grid.ResetWorld();
        ClearRoom();
        batches.Clear();
        liveCasts.Clear();
        history.Clear();
        ruleHistory.Clear();
        pendingEffects.Clear();
        effectHistory.Clear();
        chunkHistory.Clear();
        pendingDefense.Clear();
        defenseHistory.Clear();
        defenseFor.Clear();
        combosFor.Clear();
        pastCombos = System.Array.Empty<ComboIntent>();
        pendingCombos.Clear();
        comboHistory.Clear();
        strikesFor.Clear();
        pastStrikes = System.Array.Empty<StrikeIntent>();
        pendingStrikes.Clear();
        strikeHistory.Clear();
        lastStrikeTick = 0;
        dragonAsked = false;
        dragonHistory.Clear();
        dragonFor.Clear();
        pastDragon = System.Array.Empty<int>();
        plannedAwake.Clear();
        foreach (var c in grid.World.ActiveChunks) plannedAwake.Add(c);
        planner = new ChunkPlanner();
        hostHashes.Clear();
        ownHashes.Clear();
        rolling = new RollingHash();
        windowHash = "";
        knownUpTo = 0;
        desyncReported = false;
        tickBase = 0;
        replayedUpTo = 0;
        resetAt = 0;
        historyFrom = 1;
        LastResetTick = 0;
        if (IsServer)
        {
            hostTick = 0;
            clock = 0f;
            synced = true;
            NetworkManager.OnClientDisconnectCallback += PlayerLeft;
            NoteRoomStart();
        }
        else
        {
            synced = false;
            RequestHistoryRpc();
        }
    }

    public override void OnNetworkDespawn()
    {
        grid.ExternalTicks = false;
        if (NetworkManager != null) NetworkManager.OnClientDisconnectCallback -= PlayerLeft;
    }

    // Host only: the core forgets a wizard whose player has gone.
    void PlayerLeft(ulong clientId) => pendingStrikes.Add(StrikeIntent.From(StrikeCommand.Leave(PlayerName(clientId)), 0));

    /// <summary>
    /// Asks the host to cast a spell. It takes effect on every machine at the same tick, after
    /// <paramref name="delayTicks"/>; <paramref name="from"/> is where the caster stands (a burst's centre).
    /// </summary>
    public void RequestCast(CastCommand command, int delayTicks, Vector3 from)
    {
        var cast = new CastIntent
        {
            Spell = new FixedString32Bytes(command.Spell), X = command.X, Y = command.Y, Dir = command.Dir,
            Charge = (byte)World.ClampCharge(command.Charge), AimX = command.AimX, AimY = command.AimY, From = from,
        };
        RequestCastRpc(cast, delayTicks);
    }

    /// <summary>Host only: stamps a physics prop's effect on cells into the next tick's batch, for every machine.</summary>
    public void StampEffect(EffectCommand effect)
    {
        if (!IsServer) return;
        pendingEffects.Add(EffectIntent.From(effect, 0));
    }

    // How far from a client's wizard a punch may land, in cells (ChemEffects.Strike).
    const int StrikeReachCells = 4;

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

    /// <summary>
    /// Asks the host to run a defense command (the start pedestal: DefenseField). Anyone may ask; the
    /// host stamps it into the next tick's batch, so every machine starts the waves on the same tick.
    /// </summary>
    public void RequestDefense(DefenseCommand command) => RequestDefenseRpc(DefenseIntent.From(command, 0));

    /// <summary>This player's name in the core's casts (World.CastOf).</summary>
    public string LocalPlayer => PlayerName(NetworkManager.LocalClientId);

    /// <summary>A client's name in the core's casts.</summary>
    public static string PlayerName(ulong clientId) => $"p{clientId}";

    /// <summary>Asks the host to run a player's combo input; it lands on every machine at the same tick.</summary>
    public void RequestCombo(ComboCommand command) => RequestComboRpc(ComboIntent.From(command, 0));

    /// <summary>Asks the host to run this wizard's stand or strike; it lands on every machine at the same tick.</summary>
    public void RequestStrikeCommand(StrikeCommand command) => RequestStrikeCommandRpc(StrikeIntent.From(command, 0));

    /// <summary>Host only: the dragon is summoned on the next tick, on every machine (a Reset, once the view has it). A client's call does nothing.</summary>
    public void SummonDragon()
    {
        if (IsServer) dragonAsked = true;
    }

    /// <summary>
    /// Host only: the world resets for everyone after <paramref name="delayTicks"/> (at least 1;
    /// ResetCountdownTicks for the 3 s countdown). Every machine hears ResetComing at once, and at
    /// that tick rebuilds the world from its seed, the mods in force re-stamped with it (J-014).
    /// A reset already coming isn't moved.
    /// </summary>
    public void StampReset(int delayTicks)
    {
        if (!IsServer || resetAt > hostTick) return;
        resetAt = hostTick + Mathf.Max(1, delayTicks);
        ResetComingRpc(resetAt);
    }

    [Rpc(SendTo.ClientsAndHost)]
    void ResetComingRpc(int tick) => ResetComing?.Invoke(tick);

    void Update()
    {
        if (!IsSpawned) return;
        long from = System.Diagnostics.Stopwatch.GetTimestamp();
        if (IsServer) RunHostClock();
        Advance();
        grid.StepMs = (System.Diagnostics.Stopwatch.GetTimestamp() - from) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        if (IsServer) ServeJoiners();
    }

    // --- host ----------------------------------------------------------------

    void RunHostClock()
    {
        float tickLength = 1f / Mathf.Max(1, grid.World.Rules.Sim.TicksPerSecond);
        clock = Mathf.Min(clock + Time.deltaTime, ChemGrid.MostOwedSeconds); // peers keep the host's pace, whatever it is
        int guard = 4;
        while (clock >= tickLength && guard-- > 0)
        {
            clock -= tickLength;
            hostTick++;
            var due = scheduled.FindAll(c => c.Tick <= hostTick);
            scheduled.RemoveAll(c => c.Tick <= hostTick);
            for (int i = 0; i < due.Count; i++)
            {
                var c = due[i];
                c.Tick = hostTick;
                due[i] = c;
            }
            history.AddRange(due);
            var effects = pendingEffects.ToArray();
            pendingEffects.Clear();
            for (int i = 0; i < effects.Length; i++) effects[i].Tick = hostTick;
            effectHistory.AddRange(effects);
            bool reset = hostTick == resetAt;
            var rules = reset ? StampResetRules() : RuleChangeMsg.None;

            var chunks = PlanChunks();
            chunkHistory.AddRange(chunks);
            StampDefense();
            StampCombos();
            StampStrikes();
            StampDragon();
            StampRoom();

            // The window the host's world finished last goes out once, with the next batch.
            ApplyBatchRpc(hostTick, due.ToArray(), effects, rules, chunks, windowTick, new FixedString128Bytes(windowHash));
            windowHash = "";
        }
    }

    // The reset's batch (J-014): the mods in force (or about to be) re-stamped, since a rebuilt world
    // starts on the base rules; the history from here on only, headed by them, so a late joiner never
    // replays what came before; and the chunk plan starting again from a world all awake.
    RuleChangeMsg StampResetRules()
    {
        var rules = RuleChangeMsg.From(new RuleChange(hostTick, grid.CurrentRules.Mods));
        rules.Reset = true;
        history.RemoveAll(c => c.Tick < hostTick);
        effectHistory.RemoveAll(e => e.Tick < hostTick);
        chunkHistory.RemoveAll(c => c.Tick < hostTick);
        defenseHistory.RemoveAll(d => d.Tick < hostTick);
        comboHistory.RemoveAll(c => c.Tick < hostTick);
        strikeHistory.RemoveAll(s => s.Tick < hostTick);
        dragonHistory.RemoveAll(t => t < hostTick);
        roomHistory.RemoveAll(r => r.Tick < hostTick);
        ruleHistory.Clear();
        ruleHistory.Add(rules);
        historyFrom = hostTick;
        plannedAwake.Clear();
        foreach (var c in grid.World.ChunkCoords) plannedAwake.Add(c);
        planner = new ChunkPlanner();
        return rules;
    }

    // The dragon's summons, if the host asked since the last tick, goes out for this one, just before its batch.
    void StampDragon()
    {
        if (!dragonAsked) return;
        dragonAsked = false;
        dragonHistory.Add(hostTick);
        DragonStampedRpc(hostTick);
    }

    [Rpc(SendTo.ClientsAndHost)]
    void DragonStampedRpc(int tick)
    {
        if (batches.TryGetValue(tick, out var batch)) batch.Dragon.Add(new DragonCommand());
        else dragonFor.Add(tick);
    }

    // The defense commands asked for since the last tick go out for this one, just before its batch
    // (the same reliable channel keeps them in order), and into the history for late joiners.
    void StampDefense()
    {
        if (pendingDefense.Count == 0) return;
        var stamped = pendingDefense.ToArray();
        pendingDefense.Clear();
        for (int i = 0; i < stamped.Length; i++) stamped[i].Tick = hostTick;
        defenseHistory.AddRange(stamped);
        DefenseStampedRpc(hostTick, stamped);
    }

    [Rpc(SendTo.Server)]
    void RequestDefenseRpc(DefenseIntent command) => pendingDefense.Add(command);

    // As StampDefense: the input asked for since the last tick goes out just before this tick's batch.
    void StampCombos()
    {
        if (pendingCombos.Count == 0) return;
        var stamped = pendingCombos.ToArray();
        pendingCombos.Clear();
        for (int i = 0; i < stamped.Length; i++) stamped[i].Tick = hostTick;
        comboHistory.AddRange(stamped);
        CombosStampedRpc(hostTick, stamped);
    }

    [Rpc(SendTo.ClientsAndHost)]
    void CombosStampedRpc(int tick, ComboIntent[] commands)
    {
        if (batches.TryGetValue(tick, out var batch)) batch.Combos.AddRange(commands.Select(c => c.ToCommand()));
        else combosFor[tick] = commands;
    }

    // As StampCombos. Replaying, a stand only has to be there for the next strike or the end, so a newer one replaces it;
    // but while a step trigger is in force every stand is a step that may fire one (World.Triggers.cs), so each is kept.
    void StampStrikes()
    {
        if (pendingStrikes.Count == 0) return;
        var stamped = pendingStrikes.ToArray();
        pendingStrikes.Clear();
        bool steps = grid.World != null && grid.World.Rules.TriggersOn(TriggerEvent.Step).Count > 0;
        for (int i = 0; i < stamped.Length; i++)
        {
            stamped[i].Tick = hostTick;
            var player = stamped[i].Player;
            if ((StrikeKind)stamped[i].Kind == StrikeKind.Stand && !steps) strikeHistory.RemoveAll(s => (StrikeKind)s.Kind == StrikeKind.Stand && s.Player == player && s.Tick > lastStrikeTick);
            else lastStrikeTick = hostTick;
            strikeHistory.Add(stamped[i]);
        }
        StrikesStampedRpc(hostTick, stamped);
    }

    [Rpc(SendTo.ClientsAndHost)]
    void StrikesStampedRpc(int tick, StrikeIntent[] commands)
    {
        if (batches.TryGetValue(tick, out var batch)) batch.Strikes.AddRange(commands.Select(c => c.ToCommand()));
        else strikesFor[tick] = commands;
    }

    // The sender is whose wizard it is, whatever it says.
    [Rpc(SendTo.Server)]
    void RequestStrikeCommandRpc(StrikeIntent command, RpcParams rpc = default)
    {
        command.Player = new FixedString32Bytes(PlayerName(rpc.Receive.SenderClientId));
        pendingStrikes.Add(command);
    }

    // The sender is who the input is for, whatever it says.
    [Rpc(SendTo.Server)]
    void RequestComboRpc(ComboIntent command, RpcParams rpc = default)
    {
        command.Player = new FixedString32Bytes(PlayerName(rpc.Receive.SenderClientId));
        pendingCombos.Add(command);
    }

    [Rpc(SendTo.ClientsAndHost)]
    void DefenseStampedRpc(int tick, DefenseIntent[] commands)
    {
        if (batches.TryGetValue(tick, out var batch)) batch.Defense.AddRange(commands.Select(c => c.ToCommand()));
        else defenseFor[tick] = commands;
    }

    // The chunk switches for this tick's batch: around every wizard, against what is already planned.
    ChunkIntent[] PlanChunks()
    {
        if (!grid.streamChunks || hostTick % PlanEvery != 1) return System.Array.Empty<ChunkIntent>();
        var plan = planner.Plan(grid.World, (cx, cy) => plannedAwake.Contains((cx, cy)), grid.PlayerCells());
        var stamped = new ChunkIntent[plan.Count];
        for (int i = 0; i < plan.Count; i++)
        {
            var s = plan[i];
            if (s.Active) plannedAwake.Add((s.CX, s.CY));
            else plannedAwake.Remove((s.CX, s.CY));
            stamped[i] = ChunkIntent.From(s, hostTick);
        }
        return stamped;
    }

    [Rpc(SendTo.Server)]
    void RequestCastRpc(CastIntent cast, int delayTicks, RpcParams rpc = default)
    {
        ulong caster = rpc.Receive.SenderClientId;
        cast.Caster = caster;
        cast.Tick = hostTick + 1 + Mathf.Clamp(delayTicks, 0, 30);
        scheduled.Add(cast);
        var (spell, x, y, dir) = (cast.Spell, cast.X, cast.Y, cast.Dir);
        // Only the server can look a player's wizard up by client id; everyone else finds it by its object id.
        var wizard = NetworkManager.SpawnManager.GetPlayerNetworkObject(caster);
        if (wizard != null) AnnounceCastRpc(spell, x, y, dir | (cast.Charge << 8), cast.AimX, cast.AimY, wizard.NetworkObjectId);
    }

    [Rpc(SendTo.Server)]
    void RequestHistoryRpc(RpcParams rpc = default) => joiners.Add(rpc.Receive.SenderClientId);

    void SendHistory(ulong client)
    {
        var joiner = RpcTarget.Single(client, RpcTargetUse.Temp);
        SendRoomHistoryRpc((byte)grid.World.Room.Mode, grid.World.Room.Host, roomStart, roomHistory.ToArray(), joiner);
        SendComboHistoryRpc(comboHistory.ToArray(), joiner);
        SendStrikeHistoryRpc(strikeHistory.ToArray(), joiner);
        SendDragonHistoryRpc(dragonHistory.ToArray(), joiner);
        SendHistoryRpc(hostTick, history.ToArray(), ruleHistory.ToArray(), effectHistory.ToArray(), chunkHistory.ToArray(), defenseHistory.ToArray(),
            joiner);
    }

    /// <summary>
    /// The checksum every machine compares: the cells' hash, the rules' fingerprint, the world's
    /// things' (World.ThingsNow: the props' stable ids), the sky's (Hasten Time's clock and Control
    /// Weather's weather, World.SkyHash; a sky that differs reports like the cells), the defense's, the
    /// players' casts and grimoires (World.CastHash) and, last, the dragon's (World.DragonHash).
    /// </summary>
    static string Checksum(ChemGrid grid) => grid.World.CellHash() + Others(grid);

    // All but the cells: what follows them in a checksum, in a window's too.
    static string Others(ChemGrid grid) =>
        "/" + grid.World.Rules.Fingerprint + "/" + grid.World.ThingsNow.Fingerprint + "/" + grid.World.SkyHash() + "/" + grid.World.DefenseHash() + "/" + grid.World.CastHash() + "/" + grid.World.DragonHash();

    // --- everyone --------------------------------------------------------------

    // Cosmetic only: every machine shows the cast (gesture, projectile, landing effect) before its tick arrives,
    // at its charge (a held beam is longer, a held storm wider), which rides in dirAndCharge's second byte.
    [Rpc(SendTo.ClientsAndHost)]
    void AnnounceCastRpc(FixedString32Bytes spell, int x, int y, int dirAndCharge, int aimX, int aimY, ulong casterObject)
    {
        if (NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(casterObject, out var player) && player.TryGetComponent<SpellCaster>(out var spellCaster))
            spellCaster.PlayCastEffect(new CastCommand(spell.ToString(), x, y, dirAndCharge & 0xff, (byte)(dirAndCharge >> 8), aimX, aimY));
    }

    [Rpc(SendTo.ClientsAndHost)]
    void ApplyBatchRpc(int tick, CastIntent[] casts, EffectIntent[] effects, RuleChangeMsg rules, ChunkIntent[] chunks, int hashTick, FixedString128Bytes hash)
    {
        batches[tick] = new TickBatch(tick, casts.Select(c => c.ToCommand()), rules.Has ? rules.ToChange().Mods : null, effects.Select(e => e.ToCommand()),
            chunks.Select(c => c.ToSwitch())) { Reset = rules.Has && rules.Reset };
        if (defenseFor.Remove(tick, out var defense)) batches[tick].Defense.AddRange(defense.Select(d => d.ToCommand()));
        if (combosFor.Remove(tick, out var combos)) batches[tick].Combos.AddRange(combos.Select(c => c.ToCommand()));
        if (strikesFor.Remove(tick, out var strikes)) batches[tick].Strikes.AddRange(strikes.Select(s => s.ToCommand()));
        if (dragonFor.Remove(tick)) batches[tick].Dragon.Add(new DragonCommand());
        if (roomFor.Remove(tick, out var room)) batches[tick].Room.AddRange(room.Select(r => r.ToCommand()));
        if (casts.Length > 0) liveCasts[tick] = casts;
        if (hash.Length > 0)
        {
            hostHashes[hashTick] = hash.ToString();
            CheckHash(hashTick);
        }
        if (synced) ExtendKnown();
    }

    [Rpc(SendTo.SpecifiedInParams)]
    void SendComboHistoryRpc(ComboIntent[] combos, RpcParams rpc = default) => pastCombos = combos;

    [Rpc(SendTo.SpecifiedInParams)]
    void SendStrikeHistoryRpc(StrikeIntent[] strikes, RpcParams rpc = default) => pastStrikes = strikes;

    [Rpc(SendTo.SpecifiedInParams)]
    void SendDragonHistoryRpc(int[] ticks, RpcParams rpc = default) => pastDragon = ticks;

    [Rpc(SendTo.SpecifiedInParams)]
    void SendHistoryRpc(int upToTick, CastIntent[] past, RuleChangeMsg[] ruleChanges, EffectIntent[] effects, ChunkIntent[] chunks, DefenseIntent[] defense,
        RpcParams rpc = default)
    {
        // Since an online reset the host keeps the history from it on, headed by the reset's rules:
        // the replay starts there, on a fresh world counting from that tick (the reset itself replays first).
        int from = ruleChanges.Where(r => r.Reset).Select(r => r.Tick).DefaultIfEmpty(1).Max();
        var timeline = TickBatch.Timeline(past.Select(c => (c.Tick, c.ToCommand())), ruleChanges.Select(r => r.ToChange()), upToTick,
            effects.Select(e => (e.Tick, e.ToCommand())), chunks.Select(c => (c.Tick, c.ToSwitch())), defense.Select(d => (d.Tick, d.ToCommand())), from);
        TickBatch.AddCombos(timeline, pastCombos.Select(c => (c.Tick, c.ToCommand())));
        TickBatch.AddStrikes(timeline, pastStrikes.Select(s => (s.Tick, s.ToCommand())));
        TickBatch.AddDragon(timeline, pastDragon);
        TickBatch.AddRoom(timeline, pastRoom.Select(r => (r.Tick, r.ToCommand())));
        for (int t = from; t <= upToTick; t++)
            if (!batches.ContainsKey(t)) batches[t] = timeline[t];
        tickBase = from - 1;
        replayedUpTo = upToTick;
        knownUpTo = upToTick;
        synced = true;
        ExtendKnown();
        if (effects.Length > 0) EventLog.Add($"Catching up {effects.Length} prop effect(s) on the yard");
        EventLog.Add(ruleChanges.Length > 0
            ? $"Joined the yard at tick {upToTick}; catching up, with {ruleChanges.Length} rule switch(es)"
            : $"Joined the yard at tick {upToTick}; catching up");
    }

    void ExtendKnown()
    {
        while (batches.ContainsKey(knownUpTo + 1)) knownUpTo++;
    }

    // Batch t is applied while the world is at tick t-1; stepping then brings it to t.
    void Advance()
    {
        if (!synced) return;
        var budget = System.Diagnostics.Stopwatch.StartNew();
        bool Over() => budget.Elapsed.TotalMilliseconds >= CatchUpBudgetMs;
        // A tick a busy frame left part done carries on first. Ticks run in slices within the
        // frame's budget (World.StepSome); a batch's commands only ever land between whole ticks,
        // so every peer computes the same ticks however its frames slice them. (grid.World is read
        // afresh each time: a reset replaces it.)
        if (grid.World.TickInProgress)
        {
            if (!grid.World.StepSome(Over)) return;
            Applied(NetTick);
        }
        while (NetTick < knownUpTo && !Over())
        {
            int next = NetTick + 1;
            if (!batches.TryGetValue(next, out var batch)) batch = new TickBatch(next);
            batches.Remove(next);
            if (batch.Reset) Rebuild(next);
            grid.PrepareBatch(batch);
            if (!grid.World.StepSome(Over)) return;
            Applied(NetTick);
        }
    }

    // The online reset (J-014) at a network tick: the world rebuilt from its seed, things.json, the
    // clock and the weather (ChemGrid.ResetWorld; its WorldReset listeners can read ResetIsReplay and
    // LastResetTick), counting from 0 again from here. The batch then re-stamps the mods on it.
    void Rebuild(int tick)
    {
        LastResetTick = tick;
        ResetIsReplay = tick <= replayedUpTo;
        try
        {
            grid.ResetWorld();
        }
        finally
        {
            ResetIsReplay = false;
        }
        if (IsServer) NoteRoomStart();
        tickBase = tick - 1;
        EventLog.Add("The world resets");
    }

    // A tick has finished: the physical side of its casts, the desync check, and listeners.
    void Applied(int tick)
    {
        if (liveCasts.Remove(tick, out var casts)) Shove(casts);
        grid.NoteTickDone();
        HashTick(tick);
        TickApplied?.Invoke(tick, tick % HashEvery == 0 ? Checksum(grid) : null);
    }

    // This tick's slice of the cells; a finished window goes to the peers, or is checked against the host's.
    void HashTick(int tick)
    {
        long from = System.Diagnostics.Stopwatch.GetTimestamp();
        string cells = rolling.Add(grid.World, tick);
        if (cells != null) FinishWindow(tick, cells + Others(grid));
        PerfCounts.Peak("net.hash_us", (System.Diagnostics.Stopwatch.GetTimestamp() - from) * 1000000 / System.Diagnostics.Stopwatch.Frequency);
    }

    void FinishWindow(int tick, string hash)
    {
        if (IsServer)
        {
            windowTick = tick;
            windowHash = hash;
            return;
        }
        ownHashes[tick] = hash;
        DropOldHashes(ownHashes, tick);
        DropOldHashes(hostHashes, tick);
        CheckHash(tick);
    }

    // A window the other side never had (it joined midway) would otherwise wait for ever.
    static void DropOldHashes(Dictionary<int, string> hashes, int tick)
    {
        hashes.Keys.Where(t => t < tick - HashEvery * HashesKept).ToList().ForEach(t => hashes.Remove(t));
    }

    // The physical side of the casts just applied: the host moves the props (its physics is the
    // one that counts), and every player moves their own wizard unless they cast it.
    void Shove(CastIntent[] casts)
    {
        var mine = NetworkManager.SpawnManager.GetLocalPlayerObject();
        var myWizard = mine != null ? mine.GetComponent<Knockback>() : null;
        foreach (var c in casts)
        {
            bool castHere = c.Caster == NetworkManager.LocalClientId;
            SpellForces.ApplyOnline(grid, c.ToCommand(), c.From, IsServer, castHere ? null : myWizard, castHere && mine != null ? mine.transform : null);
        }
    }

    void CheckHash(int tick)
    {
        if (IsServer || desyncReported || !hostHashes.TryGetValue(tick, out var expected) || !ownHashes.TryGetValue(tick, out var here)) return;
        hostHashes.Remove(tick);
        ownHashes.Remove(tick);
        if (expected == here) return;
        desyncReported = true;
        var (hostCells, hostRules, hostThings) = Split(expected);
        var (ownCells, ownRules, ownThings) = Split(here);
        if (hostThings != ownThings)
        {
            EventLog.Add($"<color=#ff8a80>Desync at tick {tick}: this machine's world/things.json differs from the host's, so props have different ids. Same files?</color>");
            Debug.LogWarning($"NetChem desync at tick {tick}: things fingerprint host {hostThings} here {ownThings}");
            return;
        }
        if (hostRules != ownRules)
        {
            EventLog.Add($"<color=#ff8a80>Desync at tick {tick}: this machine runs different rules from the host's (host {hostRules}, here {ownRules}). Same mods, same files?</color>");
            Debug.LogWarning($"NetChem desync at tick {tick}: rules fingerprint host {hostRules} here {ownRules}");
            return;
        }
        EventLog.Add($"Desync at tick {tick}: this yard no longer matches the host's");
        Debug.LogWarning($"NetChem desync at tick {tick}: cells host {hostCells} here {ownCells}");
    }

    static (string cells, string rules, string things) Split(string checksum)
    {
        var parts = checksum.Split('/');
        return (parts[0], parts.Length > 1 ? parts[1] : "", parts.Length > 2 ? parts[2] : "");
    }

    /// <summary>For tests and the CLI: the last checksum this machine would send or compare.</summary>
    public string CurrentChecksum => Checksum(grid);
}

/// <summary>A spell cast, stamped with the sim tick it takes effect on, with who cast it and from where.</summary>
public struct CastIntent : INetworkSerializable
{
    public FixedString32Bytes Spell;
    public int X, Y, Dir, Tick;
    /// <summary>How long it was held, 1 (a tap) to World.MaxCharge.</summary>
    public byte Charge;
    /// <summary>Its aim off the grid's four ways, or (0, 0) (CastCommand).</summary>
    public int AimX, AimY;
    public ulong Caster;
    /// <summary>Where the caster stood when casting: the centre of a burst's shove (Thunderwave).</summary>
    public Vector3 From;

    public CastCommand ToCommand() => new CastCommand(Spell.ToString(), X, Y, Dir, Charge, AimX, AimY);

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref Spell);
        serializer.SerializeValue(ref X);
        serializer.SerializeValue(ref Y);
        serializer.SerializeValue(ref Dir);
        serializer.SerializeValue(ref Charge);
        serializer.SerializeValue(ref AimX);
        serializer.SerializeValue(ref AimY);
        serializer.SerializeValue(ref Tick);
        serializer.SerializeValue(ref Caster);
        serializer.SerializeValue(ref From);
    }
}

/// <summary>A physics prop's effect on cells (Chem.EffectCommand), stamped with the sim tick it takes effect on.</summary>
public struct EffectIntent : INetworkSerializable
{
    public byte Kind;
    public int X, Y, Tick;
    public FixedString64Bytes Entity;
    /// <summary>The prop's stable id (Chem.ThingId), never its NetworkObjectId.</summary>
    public ulong Thing;

    public static EffectIntent From(EffectCommand e, int tick) => new EffectIntent
    {
        Kind = (byte)e.Kind,
        X = e.X,
        Y = e.Y,
        Tick = tick,
        Entity = new FixedString64Bytes(e.Entity ?? ""),
        Thing = e.Thing.Value,
    };

    public EffectCommand ToCommand() => new EffectCommand((EffectKind)Kind, X, Y, Entity.ToString(), new ThingId(Thing));

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref Kind);
        serializer.SerializeValue(ref X);
        serializer.SerializeValue(ref Y);
        serializer.SerializeValue(ref Tick);
        serializer.SerializeValue(ref Entity);
        serializer.SerializeValue(ref Thing);
    }
}

/// <summary>
/// A rule switch on the wire: the tick and the full mod list from then on, each mod's id and JSON.
/// Has is false in the (usual) batch that switches nothing.
/// </summary>
public struct RuleChangeMsg : INetworkSerializable
{
    public bool Has;
    public int Tick;
    /// <summary>The world resets at this tick (J-014): these are the mods in force, re-stamped on the rebuilt world.</summary>
    public bool Reset;
    public string[] Ids;
    public string[] Jsons;

    public static RuleChangeMsg None => new RuleChangeMsg { Ids = System.Array.Empty<string>(), Jsons = System.Array.Empty<string>() };

    public static RuleChangeMsg From(RuleChange change) => new RuleChangeMsg
    {
        Has = true,
        Tick = change.Tick,
        Ids = change.Mods.Select(m => m.Id).ToArray(),
        Jsons = change.Mods.Select(m => m.Json).ToArray(),
    };

    public RuleChange ToChange()
    {
        var ids = Ids;
        var jsons = Jsons;
        return new RuleChange(Tick, ids.Select((id, i) => new ActiveMod(id, jsons[i])).ToArray());
    }

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref Has);
        if (!Has)
        {
            if (serializer.IsReader) this = None;
            return;
        }
        serializer.SerializeValue(ref Tick);
        serializer.SerializeValue(ref Reset);
        int count = Ids?.Length ?? 0;
        serializer.SerializeValue(ref count);
        if (serializer.IsReader)
        {
            Ids = new string[count];
            Jsons = new string[count];
        }
        for (int i = 0; i < count; i++)
        {
            serializer.SerializeValue(ref Ids[i]);
            serializer.SerializeValue(ref Jsons[i]);
        }
    }
}

/// <summary>A chunk waking or going to sleep (Chem.ChunkSwitch), stamped with the sim tick it takes effect on.</summary>
public struct ChunkIntent : INetworkSerializable
{
    public int CX, CY, Tick;
    public bool Active;

    public static ChunkIntent From(ChunkSwitch s, int tick) => new ChunkIntent { CX = s.CX, CY = s.CY, Active = s.Active, Tick = tick };

    public ChunkSwitch ToSwitch() => new ChunkSwitch(CX, CY, Active);

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref CX);
        serializer.SerializeValue(ref CY);
        serializer.SerializeValue(ref Tick);
        serializer.SerializeValue(ref Active);
    }
}

/// <summary>A wizard's stand, strike or leaving (Chem.StrikeCommand), stamped with the sim tick it lands on.</summary>
public struct StrikeIntent : INetworkSerializable
{
    public byte Kind;
    public FixedString32Bytes Player;
    public int X, Y, AimX, AimY, Hp, Tick;

    public static StrikeIntent From(StrikeCommand c, int tick) => new StrikeIntent
    {
        Kind = (byte)c.Kind,
        Player = new FixedString32Bytes(c.Player ?? ""),
        X = c.X,
        Y = c.Y,
        AimX = c.AimX,
        AimY = c.AimY,
        Hp = c.Hp,
        Tick = tick,
    };

    public StrikeCommand ToCommand() => new StrikeCommand(Player.ToString(), (StrikeKind)Kind, X, Y, AimX, AimY, Hp);

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref Kind);
        serializer.SerializeValue(ref Player);
        serializer.SerializeValue(ref X);
        serializer.SerializeValue(ref Y);
        serializer.SerializeValue(ref AimX);
        serializer.SerializeValue(ref AimY);
        serializer.SerializeValue(ref Hp);
        serializer.SerializeValue(ref Tick);
    }
}

/// <summary>A player's combo input (Chem.ComboCommand), stamped with the sim tick it lands on.</summary>
public struct ComboIntent : INetworkSerializable
{
    public byte Kind;
    public FixedString32Bytes Player, Element;
    public int Step, Tick;
    public bool Hold;

    public static ComboIntent From(ComboCommand c, int tick) => new ComboIntent
    {
        Kind = (byte)c.Kind,
        Player = new FixedString32Bytes(c.Player ?? ""),
        Element = new FixedString32Bytes(c.Element ?? ""),
        Step = c.Step,
        Hold = c.Hold,
        Tick = tick,
    };

    public ComboCommand ToCommand() => new ComboCommand(Player.ToString(), (ComboCommandKind)Kind, Element.Length > 0 ? Element.ToString() : null, Step, Hold);

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref Kind);
        serializer.SerializeValue(ref Player);
        serializer.SerializeValue(ref Element);
        serializer.SerializeValue(ref Step);
        serializer.SerializeValue(ref Hold);
        serializer.SerializeValue(ref Tick);
    }
}

/// <summary>A defense command (Chem.DefenseCommand: the start pedestal's), stamped with the sim tick it takes effect on.</summary>
public struct DefenseIntent : INetworkSerializable
{
    public byte Kind;
    public int GateX, GateY, FlagX, FlagY, Tick;

    public static DefenseIntent From(DefenseCommand d, int tick) => new DefenseIntent
    {
        Kind = (byte)d.Kind,
        GateX = d.GateX,
        GateY = d.GateY,
        FlagX = d.FlagX,
        FlagY = d.FlagY,
        Tick = tick,
    };

    public DefenseCommand ToCommand() => new DefenseCommand((DefenseKind)Kind, GateX, GateY, FlagX, FlagY);

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref Kind);
        serializer.SerializeValue(ref GateX);
        serializer.SerializeValue(ref GateY);
        serializer.SerializeValue(ref FlagX);
        serializer.SerializeValue(ref FlagY);
        serializer.SerializeValue(ref Tick);
    }
}
