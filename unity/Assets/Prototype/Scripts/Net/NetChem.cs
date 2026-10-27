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
// - A rule switch (mods on or off; RuleSwitcher) travels the same way: the
//   host checks it and stamps it into the next batch with the full mod list
//   and each mod's JSON, and every machine switches at that tick.
// - Every batch carries the host's checksum of an earlier tick, the cells'
//   hash, the rules' fingerprint, the world's things' (the props' stable
//   ids) and the sky's (the time spells' clock and weather); a client that
//   disagrees logs a desync. The time spells are casts like any other, so a
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
// - The host can reset the world for everyone (J-014, StampReset): every machine hears
//   ResetComing at once (a countdown), and at that tick rebuilds the world from its seed
//   (ChemGrid.ResetWorld), the mods in force re-stamped in the same batch. The network's ticks
//   keep counting; the rebuilt world's own tick starts again at 0 (tickBase), and so do its clock
//   and weather. The host keeps the history from the last reset on only.
// - A late joiner asks for the history (casts, rule switches, prop effects, defense commands
//   and chunk switches) and replays it from tick 0, or from the last reset, which it replays first.
[RequireComponent(typeof(ChemGrid))]
public class NetChem : NetworkBehaviour
{
    const int HashEvery = 30;
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
    // The chunks awake once every stamped switch has run (the host's world is a tick or so behind).
    readonly HashSet<(int cx, int cy)> plannedAwake = new();
    // Plans the switches, lingering chunks still live; host only, since only the switches are shared.
    ChunkPlanner planner = new();
    RuleSet stampNext;
    // Chunks are planned every few ticks: players move a chunk in seconds, not ticks.
    const int PlanEvery = 3;

    // Everyone.
    readonly Dictionary<int, TickBatch> batches = new();
    // The casts of batches that arrived live (not replayed history), for their shoves.
    readonly Dictionary<int, CastIntent[]> liveCasts = new();
    // Defense commands stamped for a tick, sent just before its batch, waiting to join it.
    readonly Dictionary<int, DefenseIntent[]> defenseFor = new();
    readonly Dictionary<int, string> hostHashes = new();
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

    /// <summary>Host only: the rules the host has most recently stamped (or will stamp next), or null if none since spawn.</summary>
    public RuleSet HostUpcoming { get; private set; }

    public override void OnNetworkSpawn()
    {
        // The host keeps the mods it had on solo: they go out at tick 1, so they are in the history too.
        var soloMods = grid.CurrentRules != null ? grid.CurrentRules.Mods : null;
        grid.ExternalTicks = true;
        grid.ResetWorld();
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
        plannedAwake.Clear();
        foreach (var c in grid.World.ActiveChunks) plannedAwake.Add(c);
        planner = new ChunkPlanner();
        stampNext = null;
        HostUpcoming = null;
        hostHashes.Clear();
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
            if (soloMods != null && soloMods.Count > 0) TryStamp(soloMods);
        }
        else
        {
            synced = false;
            RequestHistoryRpc();
        }
    }

    public override void OnNetworkDespawn() => grid.ExternalTicks = false;

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

    /// <summary>
    /// Asks the host to run a defense command (the start pedestal: DefenseField). Anyone may ask; the
    /// host stamps it into the next tick's batch, so every machine starts the waves on the same tick.
    /// </summary>
    public void RequestDefense(DefenseCommand command) => RequestDefenseRpc(DefenseIntent.From(command, 0));

    /// <summary>Host only: stamps a checked rule switch into the next tick's batch, for every machine.</summary>
    public void StampRules(RuleSet next)
    {
        if (!IsServer) return;
        stampNext = next;
        HostUpcoming = next;
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

    /// <summary>Client: asks the host to switch a mod on or off (the host checks it with its own copy of the file).</summary>
    public void RequestToggle(string modId) => RequestToggleRpc(new FixedString128Bytes(modId));

    void TryStamp(IReadOnlyList<ActiveMod> mods)
    {
        try { StampRules(grid.CurrentRules.WithMods(mods)); }
        catch (ModException e) { EventLog.Add($"Mods left off online: {e.Errors[0]}"); }
    }

    void Update()
    {
        if (!IsSpawned) return;
        if (IsServer) RunHostClock();
        Advance();
    }

    // --- host ----------------------------------------------------------------

    void RunHostClock()
    {
        float tickLength = 1f / Mathf.Max(1, grid.World.Rules.Sim.TicksPerSecond);
        clock += Time.deltaTime;
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
            var rules = reset ? StampResetRules() : TakeRuleSwitch();

            var chunks = PlanChunks();
            chunkHistory.AddRange(chunks);
            StampDefense();

            // The host's own world lags its clock by at most a tick, so a recent hash is always available.
            // Never the hash of a tick still part done (World.StepSome): that one is skipped, and peers check the next.
            int hashTick = NetTick;
            string hash = hashTick % HashEvery == 0 && !grid.World.TickInProgress ? Checksum(grid) : "";
            ApplyBatchRpc(hostTick, due.ToArray(), effects, rules, chunks, hashTick, new FixedString64Bytes(hash));
        }
    }

    // A rule switch waiting to go out, stamped into this tick's batch (or none).
    RuleChangeMsg TakeRuleSwitch()
    {
        if (stampNext == null) return RuleChangeMsg.None;
        var rules = RuleChangeMsg.From(new RuleChange(hostTick, stampNext.Mods));
        ruleHistory.Add(rules);
        stampNext = null;
        return rules;
    }

    // The reset's batch (J-014): the mods in force (or about to be) re-stamped, since a rebuilt world
    // starts on the base rules; the history from here on only, headed by them, so a late joiner never
    // replays what came before; and the chunk plan starting again from a world all awake.
    RuleChangeMsg StampResetRules()
    {
        var mods = (stampNext ?? HostUpcoming ?? grid.CurrentRules)?.Mods ?? System.Array.Empty<ActiveMod>();
        var rules = RuleChangeMsg.From(new RuleChange(hostTick, mods));
        rules.Reset = true;
        stampNext = null;
        history.RemoveAll(c => c.Tick < hostTick);
        effectHistory.RemoveAll(e => e.Tick < hostTick);
        chunkHistory.RemoveAll(c => c.Tick < hostTick);
        defenseHistory.RemoveAll(d => d.Tick < hostTick);
        ruleHistory.Clear();
        ruleHistory.Add(rules);
        historyFrom = hostTick;
        plannedAwake.Clear();
        foreach (var c in grid.World.ChunkCoords) plannedAwake.Add(c);
        planner = new ChunkPlanner();
        return rules;
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
    void RequestHistoryRpc(RpcParams rpc = default)
    {
        SendHistoryRpc(hostTick, history.ToArray(), ruleHistory.ToArray(), effectHistory.ToArray(), chunkHistory.ToArray(), defenseHistory.ToArray(),
            RpcTarget.Single(rpc.Receive.SenderClientId, RpcTargetUse.Temp));
    }

    [Rpc(SendTo.Server)]
    void RequestToggleRpc(FixedString128Bytes modId, RpcParams rpc = default)
    {
        if (!TryGetComponent<RuleSwitcher>(out var switcher)) return;
        string error = switcher.Toggle(modId.ToString());
        if (error != null) RuleRefusedRpc(new FixedString512Bytes(Clip(error, 120)), RpcTarget.Single(rpc.Receive.SenderClientId, RpcTargetUse.Temp));
    }

    // FixedString512Bytes holds up to 509 UTF-8 bytes; a few non-ASCII characters are at most 4 bytes each.
    static string Clip(string s, int chars) => s.Length <= chars ? s : s.Substring(0, chars - 1) + "…";

    [Rpc(SendTo.SpecifiedInParams)]
    void RuleRefusedRpc(FixedString512Bytes why, RpcParams rpc = default)
    {
        if (TryGetComponent<RuleSwitcher>(out var switcher)) switcher.Refused(why.ToString());
    }

    /// <summary>
    /// The checksum every machine compares: the cells' hash, the rules' fingerprint, the world's
    /// things' (world/things.json: the props' stable ids) and, last, the sky's (Hasten Time's clock
    /// and Control Weather's weather, World.SkyHash; a sky that differs reports like the cells).
    /// </summary>
    static string Checksum(ChemGrid grid) =>
        grid.World.CellHash() + "/" + grid.World.Rules.Fingerprint + "/" + grid.Things.Fingerprint + "/" + grid.World.SkyHash() + "/" + grid.World.DefenseHash();

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
    void ApplyBatchRpc(int tick, CastIntent[] casts, EffectIntent[] effects, RuleChangeMsg rules, ChunkIntent[] chunks, int hashTick, FixedString64Bytes hash)
    {
        batches[tick] = new TickBatch(tick, casts.Select(c => c.ToCommand()), rules.Has ? rules.ToChange().Mods : null, effects.Select(e => e.ToCommand()),
            chunks.Select(c => c.ToSwitch())) { Reset = rules.Has && rules.Reset };
        if (defenseFor.Remove(tick, out var defense)) batches[tick].Defense.AddRange(defense.Select(d => d.ToCommand()));
        if (casts.Length > 0) liveCasts[tick] = casts;
        if (hash.Length > 0) hostHashes[hashTick] = hash.ToString();
        if (synced) ExtendKnown();
    }

    [Rpc(SendTo.SpecifiedInParams)]
    void SendHistoryRpc(int upToTick, CastIntent[] past, RuleChangeMsg[] ruleChanges, EffectIntent[] effects, ChunkIntent[] chunks, DefenseIntent[] defense,
        RpcParams rpc = default)
    {
        // Since an online reset the host keeps the history from it on, headed by the reset's rules:
        // the replay starts there, on a fresh world counting from that tick (the reset itself replays first).
        int from = ruleChanges.Where(r => r.Reset).Select(r => r.Tick).DefaultIfEmpty(1).Max();
        var timeline = TickBatch.Timeline(past.Select(c => (c.Tick, c.ToCommand())), ruleChanges.Select(r => r.ToChange()), upToTick,
            effects.Select(e => (e.Tick, e.ToCommand())), chunks.Select(c => (c.Tick, c.ToSwitch())), defense.Select(d => (d.Tick, d.ToCommand())), from);
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
        tickBase = tick - 1;
        EventLog.Add("The world resets");
    }

    // A tick has finished: the physical side of its casts, the desync check, and listeners.
    void Applied(int tick)
    {
        if (liveCasts.Remove(tick, out var casts)) Shove(casts);
        CheckHash(tick);
        TickApplied?.Invoke(tick, tick % HashEvery == 0 ? Checksum(grid) : null);
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
        if (IsServer || desyncReported || !hostHashes.TryGetValue(tick, out var expected)) return;
        hostHashes.Remove(tick);
        string here = Checksum(grid);
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
