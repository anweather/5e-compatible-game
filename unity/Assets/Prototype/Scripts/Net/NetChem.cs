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
//   hash and the rules' fingerprint; a client that disagrees logs a desync.
// - A late joiner asks for the history (casts and rule switches) and replays
//   it from tick 0.
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
    RuleSet stampNext;

    // Everyone.
    readonly Dictionary<int, TickBatch> batches = new();
    readonly Dictionary<int, string> hostHashes = new();
    int knownUpTo;
    bool synced;
    bool desyncReported;

    public bool Online => IsSpawned;

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
        history.Clear();
        ruleHistory.Clear();
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

    /// <summary>Asks the host to cast a spell. It takes effect on every machine at the same tick, after <paramref name="delayTicks"/>.</summary>
    public void RequestCast(string spellId, int x, int y, int dir, int delayTicks)
    {
        RequestCastRpc(new FixedString32Bytes(spellId), x, y, dir, delayTicks);
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
            var rules = RuleChangeMsg.None;
            if (stampNext != null)
            {
                rules = RuleChangeMsg.From(new RuleChange(hostTick, stampNext.Mods));
                ruleHistory.Add(rules);
                stampNext = null;
            }

            // The host's own world lags its clock by at most a tick, so a recent hash is always available.
            int hashTick = grid.World.Tick;
            string hash = hashTick % HashEvery == 0 ? Checksum(grid) : "";
            ApplyBatchRpc(hostTick, due.ToArray(), rules, hashTick, new FixedString64Bytes(hash));
        }
    }

    [Rpc(SendTo.Server)]
    void RequestCastRpc(FixedString32Bytes spell, int x, int y, int dir, int delayTicks, RpcParams rpc = default)
    {
        ulong caster = rpc.Receive.SenderClientId;
        scheduled.Add(new CastIntent { Spell = spell, X = x, Y = y, Dir = dir, Caster = caster, Tick = hostTick + 1 + Mathf.Clamp(delayTicks, 0, 30) });
        AnnounceCastRpc(spell, x, y, dir, caster);
    }

    [Rpc(SendTo.Server)]
    void RequestHistoryRpc(RpcParams rpc = default)
    {
        SendHistoryRpc(hostTick, history.ToArray(), ruleHistory.ToArray(), RpcTarget.Single(rpc.Receive.SenderClientId, RpcTargetUse.Temp));
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

    /// <summary>The checksum every machine compares: the cells' hash and the rules' fingerprint.</summary>
    static string Checksum(ChemGrid grid) => grid.World.CellHash() + "/" + grid.World.Rules.Fingerprint;

    // --- everyone --------------------------------------------------------------

    // Cosmetic only: every machine shows the cast (gesture, projectile, landing effect) before its tick arrives.
    [Rpc(SendTo.ClientsAndHost)]
    void AnnounceCastRpc(FixedString32Bytes spell, int x, int y, int dir, ulong caster)
    {
        var player = NetworkManager.SpawnManager.GetPlayerNetworkObject(caster);
        if (player != null && player.TryGetComponent<SpellCaster>(out var spellCaster))
            spellCaster.PlayCastEffect(spell.ToString(), x, y, dir);
    }

    [Rpc(SendTo.ClientsAndHost)]
    void ApplyBatchRpc(int tick, CastIntent[] casts, RuleChangeMsg rules, int hashTick, FixedString64Bytes hash)
    {
        batches[tick] = new TickBatch(tick, casts.Select(c => c.ToCommand()), rules.Has ? rules.ToChange().Mods : null);
        if (hash.Length > 0) hostHashes[hashTick] = hash.ToString();
        if (synced) ExtendKnown();
    }

    [Rpc(SendTo.SpecifiedInParams)]
    void SendHistoryRpc(int upToTick, CastIntent[] past, RuleChangeMsg[] ruleChanges, RpcParams rpc = default)
    {
        var timeline = TickBatch.Timeline(past.Select(c => (c.Tick, c.ToCommand())), ruleChanges.Select(r => r.ToChange()), upToTick);
        for (int t = 1; t <= upToTick; t++)
            if (!batches.ContainsKey(t)) batches[t] = timeline[t];
        knownUpTo = upToTick;
        synced = true;
        ExtendKnown();
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
            CheckHash(world.Tick);
        }
    }

    void CheckHash(int tick)
    {
        if (IsServer || desyncReported || !hostHashes.TryGetValue(tick, out var expected)) return;
        hostHashes.Remove(tick);
        string here = Checksum(grid);
        if (expected == here) return;
        desyncReported = true;
        var (hostCells, hostRules) = Split(expected);
        var (ownCells, ownRules) = Split(here);
        if (hostRules != ownRules)
        {
            EventLog.Add($"<color=#ff8a80>Desync at tick {tick}: this machine runs different rules from the host's (host {hostRules}, here {ownRules}). Same mods, same files?</color>");
            Debug.LogWarning($"NetChem desync at tick {tick}: rules fingerprint host {hostRules} here {ownRules}");
            return;
        }
        EventLog.Add($"Desync at tick {tick}: this yard no longer matches the host's");
        Debug.LogWarning($"NetChem desync at tick {tick}: cells host {hostCells} here {ownCells}");
    }

    static (string cells, string rules) Split(string checksum)
    {
        int at = checksum.IndexOf('/');
        return at < 0 ? (checksum, "") : (checksum.Substring(0, at), checksum.Substring(at + 1));
    }

    /// <summary>For tests and the CLI: the last checksum this machine would send or compare.</summary>
    public string CurrentChecksum => Checksum(grid);
}

/// <summary>A spell cast, stamped with the sim tick it takes effect on.</summary>
public struct CastIntent : INetworkSerializable
{
    public FixedString32Bytes Spell;
    public int X, Y, Dir, Tick;
    public ulong Caster;

    public CastCommand ToCommand() => new CastCommand(Spell.ToString(), X, Y, Dir);

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref Spell);
        serializer.SerializeValue(ref X);
        serializer.SerializeValue(ref Y);
        serializer.SerializeValue(ref Dir);
        serializer.SerializeValue(ref Tick);
        serializer.SerializeValue(ref Caster);
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
