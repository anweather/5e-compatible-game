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
//   hash, the rules' fingerprint and the world's things' (the props' stable
//   ids); a client that disagrees logs a desync.
// - What physics props do to cells (a barrel's blast, a lantern's spill;
//   ChemEffects) is seen by the host, whose physics is the only one that
//   counts, and stamped into the next batch like a cast.
// - When a batch's casts are applied, their shoves are too (SpellForces): the
//   host moves the props, and each player moves their own wizard, from the
//   caster's position carried in the cast.
// - Which chunks are simulated follows the players (ChunkStreaming): the host
//   plans it from every wizard's position and stamps the switches into its
//   batches, so every machine pauses the same chunks at the same tick.
// - A late joiner asks for the history (casts, rule switches, prop effects
//   and chunk switches) and replays it from tick 0.
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
    // The chunks awake once every stamped switch has run (the host's world is a tick or so behind).
    readonly HashSet<(int cx, int cy)> plannedAwake = new();
    RuleSet stampNext;
    // Chunks are planned every few ticks: players move a chunk in seconds, not ticks.
    const int PlanEvery = 3;

    // Everyone.
    readonly Dictionary<int, TickBatch> batches = new();
    // The casts of batches that arrived live (not replayed history), for their shoves.
    readonly Dictionary<int, CastIntent[]> liveCasts = new();
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
        plannedAwake.Clear();
        foreach (var c in grid.World.ActiveChunks) plannedAwake.Add(c);
        stampNext = null;
        HostUpcoming = null;
        hostHashes.Clear();
        knownUpTo = 0;
        desyncReported = false;
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
    public void RequestCast(string spellId, int x, int y, int dir, int delayTicks, Vector3 from, int charge = 1)
    {
        var cast = new CastIntent { Spell = new FixedString32Bytes(spellId), X = x, Y = y, Dir = dir, Charge = (byte)World.ClampCharge(charge), From = from };
        RequestCastRpc(cast, delayTicks);
    }

    /// <summary>Host only: stamps a physics prop's effect on cells into the next tick's batch, for every machine.</summary>
    public void StampEffect(EffectCommand effect)
    {
        if (!IsServer) return;
        pendingEffects.Add(EffectIntent.From(effect, 0));
    }

    /// <summary>Host only: stamps a checked rule switch into the next tick's batch, for every machine.</summary>
    public void StampRules(RuleSet next)
    {
        if (!IsServer) return;
        stampNext = next;
        HostUpcoming = next;
    }

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
            var rules = RuleChangeMsg.None;
            if (stampNext != null)
            {
                rules = RuleChangeMsg.From(new RuleChange(hostTick, stampNext.Mods));
                ruleHistory.Add(rules);
                stampNext = null;
            }

            var chunks = PlanChunks();
            chunkHistory.AddRange(chunks);

            // The host's own world lags its clock by at most a tick, so a recent hash is always available.
            int hashTick = grid.World.Tick;
            string hash = hashTick % HashEvery == 0 ? Checksum(grid) : "";
            ApplyBatchRpc(hostTick, due.ToArray(), effects, rules, chunks, hashTick, new FixedString64Bytes(hash));
        }
    }

    // The chunk switches for this tick's batch: around every wizard, against what is already planned.
    ChunkIntent[] PlanChunks()
    {
        if (!grid.streamChunks || hostTick % PlanEvery != 1) return System.Array.Empty<ChunkIntent>();
        var plan = ChunkStreaming.Plan(grid.World.ChunkCoords, (cx, cy) => plannedAwake.Contains((cx, cy)), grid.PlayerCells());
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
        if (wizard != null) AnnounceCastRpc(spell, x, y, dir, wizard.NetworkObjectId);
    }

    [Rpc(SendTo.Server)]
    void RequestHistoryRpc(RpcParams rpc = default)
    {
        SendHistoryRpc(hostTick, history.ToArray(), ruleHistory.ToArray(), effectHistory.ToArray(), chunkHistory.ToArray(), RpcTarget.Single(rpc.Receive.SenderClientId, RpcTargetUse.Temp));
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
    /// The checksum every machine compares: the cells' hash, the rules' fingerprint and the world's
    /// things' (world/things.json: the props' stable ids).
    /// </summary>
    static string Checksum(ChemGrid grid) => grid.World.CellHash() + "/" + grid.World.Rules.Fingerprint + "/" + grid.Things.Fingerprint;

    // --- everyone --------------------------------------------------------------

    // Cosmetic only: every machine shows the cast (gesture, projectile, landing effect) before its tick arrives.
    [Rpc(SendTo.ClientsAndHost)]
    void AnnounceCastRpc(FixedString32Bytes spell, int x, int y, int dir, ulong casterObject)
    {
        if (NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(casterObject, out var player) && player.TryGetComponent<SpellCaster>(out var spellCaster))
            spellCaster.PlayCastEffect(spell.ToString(), x, y, dir);
    }

    [Rpc(SendTo.ClientsAndHost)]
    void ApplyBatchRpc(int tick, CastIntent[] casts, EffectIntent[] effects, RuleChangeMsg rules, ChunkIntent[] chunks, int hashTick, FixedString64Bytes hash)
    {
        batches[tick] = new TickBatch(tick, casts.Select(c => c.ToCommand()), rules.Has ? rules.ToChange().Mods : null, effects.Select(e => e.ToCommand()),
            chunks.Select(c => c.ToSwitch()));
        if (casts.Length > 0) liveCasts[tick] = casts;
        if (hash.Length > 0) hostHashes[hashTick] = hash.ToString();
        if (synced) ExtendKnown();
    }

    [Rpc(SendTo.SpecifiedInParams)]
    void SendHistoryRpc(int upToTick, CastIntent[] past, RuleChangeMsg[] ruleChanges, EffectIntent[] effects, ChunkIntent[] chunks, RpcParams rpc = default)
    {
        var timeline = TickBatch.Timeline(past.Select(c => (c.Tick, c.ToCommand())), ruleChanges.Select(r => r.ToChange()), upToTick,
            effects.Select(e => (e.Tick, e.ToCommand())), chunks.Select(c => (c.Tick, c.ToSwitch())));
        for (int t = 1; t <= upToTick; t++)
            if (!batches.ContainsKey(t)) batches[t] = timeline[t];
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
        var world = grid.World;
        var budget = System.Diagnostics.Stopwatch.StartNew();
        while (world.Tick < knownUpTo && budget.Elapsed.TotalMilliseconds < CatchUpBudgetMs)
        {
            int next = world.Tick + 1;
            if (batches.TryGetValue(next, out var batch)) batches.Remove(next);
            grid.RunBatch(batch ?? new TickBatch(next));
            if (liveCasts.Remove(next, out var casts)) Shove(casts);
            CheckHash(world.Tick);
            TickApplied?.Invoke(world.Tick, world.Tick % HashEvery == 0 ? Checksum(grid) : null);
        }
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
    public ulong Caster;
    /// <summary>Where the caster stood when casting: the centre of a burst's shove (Thunderwave).</summary>
    public Vector3 From;

    public CastCommand ToCommand() => new CastCommand(Spell.ToString(), X, Y, Dir, Charge);

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref Spell);
        serializer.SerializeValue(ref X);
        serializer.SerializeValue(ref Y);
        serializer.SerializeValue(ref Dir);
        serializer.SerializeValue(ref Charge);
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
