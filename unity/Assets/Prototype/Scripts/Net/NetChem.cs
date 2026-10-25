using System.Collections.Generic;
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
// - Every batch carries the host's checksum of an earlier tick; a client
//   that disagrees logs a desync.
// - A late joiner asks for the cast history and replays it from tick 0.
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

    // Everyone.
    readonly Dictionary<int, CastIntent[]> batches = new();
    readonly Dictionary<int, string> hostHashes = new();
    int knownUpTo;
    bool synced;
    bool desyncReported;

    public bool Online => IsSpawned;

    void Awake() => grid = GetComponent<ChemGrid>();

    public override void OnNetworkSpawn()
    {
        grid.ExternalTicks = true;
        grid.ResetWorld();
        batches.Clear();
        hostHashes.Clear();
        knownUpTo = 0;
        desyncReported = false;
        if (IsServer)
        {
            hostTick = 0;
            clock = 0f;
            synced = true;
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

            // The host's own world lags its clock by at most a tick, so a recent hash is always available.
            int hashTick = grid.World.Tick;
            string hash = hashTick % HashEvery == 0 ? grid.World.CellHash() : "";
            ApplyBatchRpc(hostTick, due.ToArray(), hashTick, new FixedString64Bytes(hash));
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
        SendHistoryRpc(hostTick, history.ToArray(), RpcTarget.Single(rpc.Receive.SenderClientId, RpcTargetUse.Temp));
    }

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
    void ApplyBatchRpc(int tick, CastIntent[] casts, int hashTick, FixedString64Bytes hash)
    {
        batches[tick] = casts;
        if (hash.Length > 0) hostHashes[hashTick] = hash.ToString();
        if (synced) ExtendKnown();
    }

    [Rpc(SendTo.SpecifiedInParams)]
    void SendHistoryRpc(int upToTick, CastIntent[] past, RpcParams rpc = default)
    {
        var byTick = new Dictionary<int, List<CastIntent>>();
        foreach (var c in past)
        {
            if (!byTick.TryGetValue(c.Tick, out var list)) byTick[c.Tick] = list = new List<CastIntent>();
            list.Add(c);
        }
        for (int t = 1; t <= upToTick; t++)
            if (!batches.ContainsKey(t)) batches[t] = byTick.TryGetValue(t, out var list) ? list.ToArray() : System.Array.Empty<CastIntent>();
        knownUpTo = upToTick;
        synced = true;
        ExtendKnown();
        EventLog.Add($"Joined the yard at tick {upToTick}; catching up");
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
            if (batches.TryGetValue(next, out var casts))
            {
                foreach (var c in casts) world.Cast(c.Spell.ToString(), c.X, c.Y, c.Dir);
                batches.Remove(next);
            }
            world.Step();
            CheckHash(world.Tick);
        }
    }

    void CheckHash(int tick)
    {
        if (IsServer || desyncReported || !hostHashes.TryGetValue(tick, out var expected)) return;
        hostHashes.Remove(tick);
        if (expected == grid.World.CellHash()) return;
        desyncReported = true;
        EventLog.Add($"Desync at tick {tick}: this yard no longer matches the host's");
        Debug.LogWarning($"NetChem desync at tick {tick}");
    }
}

/// <summary>A spell cast, stamped with the sim tick it takes effect on.</summary>
public struct CastIntent : INetworkSerializable
{
    public FixedString32Bytes Spell;
    public int X, Y, Dir, Tick;
    public ulong Caster;

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
