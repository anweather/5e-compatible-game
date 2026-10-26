using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Netcode;
using UnityEngine;

// The probe's online scenario (docs/FAST_LOOP.md): the main Editor hosts, one
// Multiplayer Play Mode virtual player joins, and both cast through the
// network on a shared schedule of sim ticks. Each side writes down the
// checksum of every 30th tick and what it sees at fixed ticks; the host
// compares the two.
//
// The two Editors are separate processes on one machine, so they talk through
// files in the main project's Temp/Probe/online/ (ProbeLink): host.json from
// the host (hosting, then the start tick), client.json from the virtual player
// (joined, then its report). The virtual player's half (OnlineProbeClient)
// starts itself in a virtual player's Play mode and waits for a host.json it
// hasn't seen.

/// <summary>The files the host and the virtual player exchange.</summary>
public static class ProbeLink
{
    public const string Hosting = "hosting", Go = "go", Joined = "joined", Reported = "reported";

    /// <summary>True in a Multiplayer Play Mode virtual player (its project is a copy under Library/VP/).</summary>
    public static bool InVirtualPlayer => Application.isEditor && VirtualRoot() != null;

    // The main project's folder: the virtual player's project is <main>/Library/VP/<id>/.
    static string VirtualRoot()
    {
        string data = Application.dataPath.Replace('\\', '/');
        int at = data.IndexOf("/Library/VP/", StringComparison.Ordinal);
        return at < 0 ? null : data.Substring(0, at);
    }

    static string Dir => Path.Combine(VirtualRoot() ?? Directory.GetCurrentDirectory(), "Temp", "Probe", "online");

    public static string HostFile => Path.Combine(Dir, "host.json");
    public static string ClientFile => Path.Combine(Dir, "client.json");

    public static void Write(string file, object message)
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(file + ".tmp", JsonUtility.ToJson(message, true));
        if (File.Exists(file)) File.Delete(file);
        File.Move(file + ".tmp", file);
    }

    public static T Read<T>(string file) where T : class
    {
        try
        {
            return File.Exists(file) ? JsonUtility.FromJson<T>(File.ReadAllText(file)) : null;
        }
        catch (IOException) { return null; }
        catch (ArgumentException) { return null; }
    }

    public static void Clear()
    {
        if (File.Exists(HostFile)) File.Delete(HostFile);
        if (File.Exists(ClientFile)) File.Delete(ClientFile);
    }
}

[Serializable]
public class ProbeHostMessage
{
    public string id;
    public string phase;
    public int startTick;
}

/// <summary>What one side saw: the same fields from the host and from the virtual player.</summary>
[Serializable]
public class OnlineObservation
{
    public string id;
    public string phase;
    public int joinedTick;
    public List<int> hashTicks = new();
    public List<string> hashes = new();
    public bool desync;
    public int errorCount;
    public List<string> errors = new();
    /// <summary>The pushed crate, before the shove and once it has settled (OnlineSchedule.PropTick).</summary>
    public Vector3 propBefore, propAfter;
    public bool propFound;
    /// <summary>The pushed crate's stable id (world/things.json), and every thing this side has, as "id kind", sorted, at PropTick.</summary>
    public string propId;
    public List<string> things = new();
    /// <summary>The host's wizard (this side's copy), before the virtual player's Thunderwave and at PropTick.</summary>
    public Vector3 hostWizardBefore, hostWizardAfter;
    /// <summary>At OnlineSchedule.SampleTick, in the order of OnlineSchedule.Lines: per spell, its cells' summed height, wet cells, water units, raised walls, cells drawn wet, and the terrain drawn at their centres.</summary>
    public List<int> heightSums = new(), wetCells = new(), waterUnits = new(), raisedWalls = new(), drawnWet = new();
    public List<float> drawnGround = new(), drawnGroundAtStart = new();
    public int sampledTick;
    public string sampledChecksum;
}

/// <summary>The shared script, in sim ticks after the start tick (6 a second). Yard cells; directions 0 N, 1 E, 2 S, 3 W.</summary>
public static class OnlineSchedule
{
    public readonly struct Step
    {
        public readonly int At;
        public readonly bool Client;
        public readonly string Spell;
        public readonly Vector2Int Stand, Target;
        public readonly int Dir;

        public Step(int at, bool client, string spell, Vector2Int stand, Vector2Int target, int dir)
        {
            At = at;
            Client = client;
            Spell = spell;
            Stand = stand;
            Target = target;
            Dir = dir;
        }
    }

    static Step S(int at, bool client, string spell, (int x, int y) stand, (int x, int y) target, int dir) =>
        new Step(at, client, spell, new Vector2Int(stand.x, stand.y), new Vector2Int(target.x, target.y), dir);

    /// <summary>Not a spell: the wizard is put FallMetres above its cell and drops (a landing on the other screen).</summary>
    public const string Fall = "(fall)";
    const float FallMetres = 10f;

    /// <summary>The yard cell of the crate the virtual player's Thunderwave shoves east (found by its stable id).</summary>
    public static readonly Vector2Int PushedCell = new Vector2Int(16, 6);

    /// <summary>The pushed crate's stable id, from the world's data (the same on both sides).</summary>
    public static Chem.ThingId PushedThing(ChemGrid grid)
    {
        var cell = grid.YardCell(PushedCell.x, PushedCell.y);
        var thing = grid.Things.At(cell.x, cell.y);
        return thing != null ? thing.Id : Chem.ThingId.None;
    }

    public static readonly Step[] Steps =
    {
        S(0, false, null, (15, 7), (15, 7), 0),                 // the host stands beside the client, to be thrown too
        S(0, true, SpellIds.Thunderwave, (15, 6), (15, 6), 1),
        S(18, false, SpellIds.FireBolt, (5, 13), (2, 12), 3),         // the meadow, west of the old yard
        S(24, true, SpellIds.FireBolt, (16, 11), (19, 3), 0),         // the oil spill by the barrels
        S(30, false, SpellIds.EarthenTrench, (16, 5), (15, 5), 3),    // west from the pond's bank: fills
        S(36, true, SpellIds.EarthenTrench, (16, 8), (15, 8), 3),
        S(42, false, SpellIds.WallOfStone, (10, 6), (12, 7), 0),
        S(48, true, SpellIds.WallOfStone, (6, 4), (8, 4), 0),
        S(60, false, Fall, (10, 6), (10, 6), 0),                  // the host's wizard falls and lands
    };

    /// <summary>When the shoved crate has settled (before any fire can reach it).</summary>
    public const int PropTick = 15;
    /// <summary>When both sides write down what they see: 20 s after the fall, while the fires still spread.</summary>
    public const int SampleTick = 180;

    /// <summary>The earth spells' lines, whose cells are observed.</summary>
    public static IEnumerable<Step> Lines => Steps.Where(s => s.Spell == SpellIds.EarthenTrench || s.Spell == SpellIds.WallOfStone);

    /// <summary>Places the local wizard and casts one step through the network (SpellCaster.Cast); a step without a spell only places.</summary>
    public static void Cast(ChemGrid grid, GameObject wizard, Step step)
    {
        var body = wizard.GetComponent<CharacterController>();
        body.enabled = false;
        var stand = grid.YardCell(step.Stand.x, step.Stand.y);
        float lift = step.Spell == Fall ? FallMetres : 0.05f;
        wizard.transform.position = grid.CellCenter(stand.x, stand.y) + Vector3.up * (grid.SurfaceY(stand.x, stand.y) + lift);
        body.enabled = true;
        if (step.Spell == null || step.Spell == Fall) return;
        var target = grid.YardCell(step.Target.x, step.Target.y);
        new WizardPuppet(wizard).Face(grid.CellCenter(target.x, target.y));
        wizard.GetComponent<SpellCaster>().Cast(step.Spell, target.x, target.y, step.Dir);
    }

    /// <summary>The cells of the earth spells' lines, as the world stands now (both sides take them at the start tick).</summary>
    public static List<List<(int x, int y)>> LineCells(ChemGrid grid) => Lines.Select(line =>
    {
        var t = grid.YardCell(line.Target.x, line.Target.y);
        return grid.World.SpellCells(grid.World.Rules.Spell(line.Spell), t.x, t.y, line.Dir);
    }).ToList();

    /// <summary>The terrain drawn at the centres of each line's cells, summed, now.</summary>
    public static List<float> DrawnGround(ChemGrid grid, List<List<(int x, int y)>> lines) =>
        lines.Select(cells => cells.Sum(c => grid.GroundAt(grid.CellCenter(c.x, c.y)))).ToList();

    /// <summary>Writes down the lines' cells and how they are drawn (call at SampleTick).</summary>
    public static void Sample(ChemGrid grid, List<List<(int x, int y)>> lines, OnlineObservation o)
    {
        var w = grid.World;
        o.sampledTick = w.Tick;
        o.sampledChecksum = w.CellHash() + "/" + w.Rules.Fingerprint;
        foreach (var cells in lines)
        {
            o.heightSums.Add(cells.Sum(c => w.At(c.x, c.y).Height));
            o.wetCells.Add(cells.Count(c => w.At(c.x, c.y).Water > 0));
            o.waterUnits.Add(cells.Sum(c => w.At(c.x, c.y).Water));
            o.raisedWalls.Add(cells.Count(c => w.IsRaisedWall(w.At(c.x, c.y))));
            o.drawnWet.Add(cells.Count(c => grid.DrawnWaterTop(c.x, c.y).HasValue));
        }
        o.drawnGround = DrawnGround(grid, lines);
    }

    /// <summary>This side's copy of a thing, by its stable id.</summary>
    public static PhysicsProp FindProp(Chem.ThingId id)
    {
        var spawner = UnityEngine.Object.FindFirstObjectByType<ThingSpawner>();
        return spawner != null ? spawner.Find(id) : null;
    }

    /// <summary>Every thing this side has, as "id kind", sorted by id.</summary>
    public static List<string> Things()
    {
        var spawner = UnityEngine.Object.FindFirstObjectByType<ThingSpawner>();
        if (spawner == null) return new List<string>();
        return spawner.Live.OrderBy(t => t.id.Value).Select(t => $"{t.id} {t.prop.kind}").ToList();
    }
}

/// <summary>
/// One side's part: casts its steps when their ticks come and records checksums and samples
/// from NetChem.TickApplied, so both sides record the very same ticks.
/// </summary>
public sealed class OnlineProbeRecorder
{
    readonly ChemGrid grid;
    readonly NetChem net;
    readonly bool client;
    public readonly OnlineObservation Seen = new();
    public int StartTick = int.MaxValue;
    int nextStep;
    List<List<(int x, int y)>> lines;
    public bool Sampled { get; private set; }

    public OnlineProbeRecorder(ChemGrid grid, bool client)
    {
        this.grid = grid;
        this.client = client;
        net = grid.GetComponent<NetChem>();
        net.TickApplied += OnTick;
    }

    public void Stop() => net.TickApplied -= OnTick;

    void OnTick(int tick, string checksum)
    {
        if (checksum != null)
        {
            Seen.hashTicks.Add(tick);
            Seen.hashes.Add(checksum);
        }
        if (tick == StartTick)
        {
            lines = OnlineSchedule.LineCells(grid);
            Seen.drawnGroundAtStart = OnlineSchedule.DrawnGround(grid, lines);
        }
        if (tick == StartTick + OnlineSchedule.PropTick) RecordProp(after: true);
        if (tick == StartTick + OnlineSchedule.SampleTick)
        {
            OnlineSchedule.Sample(grid, lines, Seen);
            Seen.desync = net.DesyncSeen;
            Sampled = true;
        }
    }

    public void RecordProp(bool after)
    {
        var id = OnlineSchedule.PushedThing(grid);
        var prop = OnlineSchedule.FindProp(id);
        Seen.propFound = prop != null;
        Seen.propId = id.ToString();
        if (after) Seen.things = OnlineSchedule.Things();
        var host = UnityEngine.Object.FindObjectsByType<NetWizard>(FindObjectsSortMode.None).FirstOrDefault(w => w.OwnerClientId == NetworkManager.ServerClientId);
        if (host != null && after) Seen.hostWizardAfter = host.transform.position;
        else if (host != null) Seen.hostWizardBefore = host.transform.position;
        if (prop == null) return;
        if (after) Seen.propAfter = prop.transform.position;
        else Seen.propBefore = prop.transform.position;
    }

    /// <summary>Call every frame: casts this side's steps whose tick has come.</summary>
    public void Play(GameObject wizard)
    {
        var steps = OnlineSchedule.Steps;
        while (nextStep < steps.Length && grid.World.Tick >= StartTick + steps[nextStep].At)
        {
            var step = steps[nextStep++];
            if (step.Client == client) OnlineSchedule.Cast(grid, wizard, step);
        }
    }
}

/// <summary>The virtual player's half: joins when the host says so, plays its steps, reports.</summary>
public class OnlineProbeClient : MonoBehaviour
{
    const float PollSeconds = 0.25f;
    // A host.json older than this is left over from an earlier run.
    const double FreshSeconds = 120;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void StartInVirtualPlayer()
    {
        if (!ProbeLink.InVirtualPlayer) return;
        var go = new GameObject("Probe online client");
        DontDestroyOnLoad(go);
        go.AddComponent<OnlineProbeClient>();
    }

    readonly HashSet<string> handled = new();
    int errorCount;
    readonly List<string> errors = new();

    void OnEnable() => Application.logMessageReceived += OnLog;
    void OnDisable() => Application.logMessageReceived -= OnLog;

    void OnLog(string message, string stack, LogType type)
    {
        if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
        errorCount++;
        if (errors.Count < 10) errors.Add($"{type}: {message} (at {stack?.Split('\n').FirstOrDefault()})");
    }

    IEnumerator Start()
    {
        while (isActiveAndEnabled)
        {
            yield return new WaitForSecondsRealtime(PollSeconds);
            var host = ProbeLink.Read<ProbeHostMessage>(ProbeLink.HostFile);
            if (host == null || host.phase != ProbeLink.Hosting || handled.Contains(host.id)) continue;
            if ((DateTime.UtcNow - File.GetLastWriteTimeUtc(ProbeLink.HostFile)).TotalSeconds > FreshSeconds) continue;
            handled.Add(host.id);
            yield return Take(host.id);
        }
    }

    IEnumerator Take(string id)
    {
        errorCount = 0;
        errors.Clear();
        var session = FindFirstObjectByType<NetSession>();
        session.Join("127.0.0.1");
        var grid = FindFirstObjectByType<ChemGrid>();
        var net = grid.GetComponent<NetChem>();
        float waited = 0f;
        GameObject wizard = null;
        while (waited < 30f)
        {
            var local = NetworkManager.Singleton != null && NetworkManager.Singleton.IsConnectedClient
                ? NetworkManager.Singleton.SpawnManager.GetLocalPlayerObject() : null;
            if (local != null && net.Online && grid.World.Tick > 0) { wizard = local.gameObject; break; }
            waited += Time.unscaledDeltaTime;
            yield return null;
        }
        if (wizard == null) yield break;   // the host times out waiting for "joined"
        var recorder = new OnlineProbeRecorder(grid, client: true);
        recorder.Seen.id = id;
        recorder.Seen.phase = ProbeLink.Joined;
        recorder.Seen.joinedTick = grid.World.Tick;
        ProbeLink.Write(ProbeLink.ClientFile, recorder.Seen);

        ProbeHostMessage go = null;
        for (waited = 0f; waited < 60f; waited += Time.unscaledDeltaTime)
        {
            go = ProbeLink.Read<ProbeHostMessage>(ProbeLink.HostFile);
            if (go != null && go.id == id && go.phase == ProbeLink.Go) break;
            go = null;
            yield return null;
        }
        if (go == null) { recorder.Stop(); yield break; }
        recorder.StartTick = go.startTick;
        while (grid.World.Tick < go.startTick) yield return null;
        recorder.Play(wizard);
        recorder.RecordProp(after: false);
        while (!recorder.Sampled)
        {
            recorder.Play(wizard);
            yield return null;
        }
        recorder.Stop();
        recorder.Seen.phase = ProbeLink.Reported;
        recorder.Seen.errorCount = errorCount;
        recorder.Seen.errors = new List<string>(errors);
        ProbeLink.Write(ProbeLink.ClientFile, recorder.Seen);
    }
}

/// <summary>
/// The host's half and the checks. Needs a virtual player (Player 2, Multiplayer Play Mode):
/// `probe run --name online` activates it first if it isn't running.
/// </summary>
public class OnlineProbe : ProbeScenario
{
    const int LeadTicks = 30;
    const float JoinSeconds = 60f, ReportSeconds = 60f;

    // So a virtual player started later doesn't join from this run's files.
    protected override void OnDestroy()
    {
        ProbeLink.Clear();
        base.OnDestroy();
    }

    protected override IEnumerator Run()
    {
        string id = Guid.NewGuid().ToString("N").Substring(0, 8);
        ProbeLink.Clear();
        var session = FindFirstObjectByType<NetSession>();
        session.Host();
        yield return null;
        wizard = NetworkManager.Singleton.SpawnManager.GetLocalPlayerObject().gameObject;
        puppet = new WizardPuppet(wizard);
        caster = wizard.GetComponent<SpellCaster>();
        caster.enabled = false;
        ProbeLink.Write(ProbeLink.HostFile, new ProbeHostMessage { id = id, phase = ProbeLink.Hosting });

        OnlineObservation joined = null;
        for (float t = 0f; t < JoinSeconds && joined == null; t += Time.unscaledDeltaTime)
        {
            joined = ReadClient(id, phase: null);
            yield return null;
        }
        int players = NetworkManager.Singleton.ConnectedClientsIds.Count;
        Metric("players", players);
        Check("virtual_player_joined", joined != null && players == 2,
            joined != null ? $"{players} players; joined at tick {joined.joinedTick}" : $"no virtual player joined in {JoinSeconds:0} s (is Player 2 running and in Play mode?)");
        if (joined == null) yield break;

        var recorder = new OnlineProbeRecorder(grid, client: false);
        recorder.Seen.id = id;
        recorder.StartTick = World.Tick + LeadTicks;
        ProbeLink.Write(ProbeLink.HostFile, new ProbeHostMessage { id = id, phase = ProbeLink.Go, startTick = recorder.StartTick });
        // The camera over the pond, the trenches and the walls.
        Cut(Yard(13, 14) + V(0f, 22f, 0f), Yard(13, 6));
        while (World.Tick < recorder.StartTick) yield return null;
        recorder.Play(wizard);
        recorder.RecordProp(after: false);
        while (!recorder.Sampled)
        {
            recorder.Play(wizard);
            yield return null;
        }
        recorder.Stop();
        Shot("host");

        OnlineObservation report = null;
        for (float t = 0f; t < ReportSeconds && report == null; t += Time.unscaledDeltaTime)
        {
            report = ReadClient(id, ProbeLink.Reported);
            yield return null;
        }
        Check("virtual_player_reported", report != null, report != null ? $"sampled at tick {report.sampledTick}" : $"no report in {ReportSeconds:0} s");
        if (report == null) yield break;
        Compare(recorder.Seen, report);
        yield return TryReset();
    }

    // Reset (R / View) online is refused rather than reloading the host's scene from under everyone.
    IEnumerator TryReset()
    {
        bool reloaded = EventLog.ResetYard();
        for (int i = 0; i < 10; i++) yield return null;
        int players = NetworkManager.Singleton.ConnectedClientsIds.Count;
        Check("reset_refused_online", !reloaded && players == 2, $"reloaded {reloaded}, {players} players after");
    }

    // The virtual player's client.json if it is from this run and in the given phase (any phase when null), else null.
    static OnlineObservation ReadClient(string id, string phase)
    {
        var seen = ProbeLink.Read<OnlineObservation>(ProbeLink.ClientFile);
        return seen != null && seen.id == id && (phase == null || seen.phase == phase) ? seen : null;
    }

    void Compare(OnlineObservation host, OnlineObservation client)
    {
        CompareChecksums(host, client);
        CompareEarth(host, client);
        CompareThings(host, client);
        CompareProp(host, client);
        CompareKnockback(host, client);

        Metric("client_console_errors", client.errorCount);
        Check("client_no_console_errors", client.errorCount == 0,
            client.errorCount == 0 ? "0 errors" : $"{client.errorCount} errors: {string.Join(" | ", client.errors.Take(3))}");
    }

    // The checksums of the ticks both sides wrote down, the sample's tick and checksum, and the client's desync flag.
    void CompareChecksums(OnlineObservation host, OnlineObservation client)
    {
        var mine = new Dictionary<int, string>();
        for (int i = 0; i < host.hashTicks.Count; i++) mine[host.hashTicks[i]] = host.hashes[i];
        int compared = 0, equal = 0;
        string firstDiff = null;
        for (int i = 0; i < client.hashTicks.Count; i++)
        {
            if (!mine.TryGetValue(client.hashTicks[i], out var expected)) continue;
            compared++;
            if (expected == client.hashes[i]) equal++;
            else firstDiff ??= $"tick {client.hashTicks[i]}: host {expected}, client {client.hashes[i]}";
        }
        Metric("checksums_compared", compared);
        Metric("checksums_equal", equal);
        Metric("first_checksum_tick", compared > 0 ? client.hashTicks.First(mine.ContainsKey) : -1);
        Metric("last_checksum_tick", compared > 0 ? client.hashTicks.Last(mine.ContainsKey) : -1);
        Check("checksums_equal", compared >= 3 && equal == compared,
            firstDiff ?? $"{equal}/{compared} checksums equal (every 30 ticks, {client.hashTicks.FirstOrDefault()} to {client.hashTicks.LastOrDefault()})");
        Check("sampled_same_world", host.sampledTick == client.sampledTick && host.sampledChecksum == client.sampledChecksum,
            $"tick {host.sampledTick}: host {host.sampledChecksum}, client {client.sampledChecksum}");
        Check("client_saw_no_desync", !client.desync);
    }

    // The trenches, their water and the walls, as each side has and draws them at the sample tick.
    void CompareEarth(OnlineObservation host, OnlineObservation client)
    {
        string Lines(OnlineObservation o, Func<OnlineObservation, IEnumerable<object>> f) => string.Join(",", f(o));
        bool Same(Func<OnlineObservation, IEnumerable<object>> f) => Lines(host, f) == Lines(client, f);

        // Earth spells: index 0 and 2 are the host's trench and wall, 1 and 3 the client's (OnlineSchedule.Lines).
        float dug = host.drawnGround.Count > 0 && host.drawnGroundAtStart.Count > 0 ? host.drawnGround[0] - host.drawnGroundAtStart[0] : 0f;
        float clientDug = client.drawnGround.Count > 1 && client.drawnGroundAtStart.Count > 1 ? client.drawnGround[1] - client.drawnGroundAtStart[1] : 0f;
        float worstGround = host.drawnGround.Zip(client.drawnGround, (a, b) => Mathf.Abs(a - b)).DefaultIfEmpty(float.MaxValue).Max();
        Metric("trench_terrain_drop_m", -dug);
        Metric("client_trench_terrain_drop_m", -clientDug);
        Metric("terrain_drawn_diff_m", worstGround);
        Check("client_terrain_matches", Same(o => o.heightSums.Cast<object>()) && worstGround < 0.01f && dug < -0.5f && clientDug < -0.5f,
            $"height sums host [{Lines(host, o => o.heightSums.Cast<object>())}] client [{Lines(client, o => o.heightSums.Cast<object>())}]; " +
            $"terrain drawn under the lines host [{string.Join(",", host.drawnGround.Select(g => g.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)))}] " +
            $"client [{string.Join(",", client.drawnGround.Select(g => g.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)))}]");
        Metric("trench_wet_cells", host.wetCells.FirstOrDefault());
        Metric("trench_drawn_wet_cells_client", client.drawnWet.FirstOrDefault());
        Check("client_water_matches", Same(o => o.wetCells.Cast<object>()) && Same(o => o.waterUnits.Cast<object>()) && Same(o => o.drawnWet.Cast<object>())
            && host.wetCells.FirstOrDefault() > 0 && client.drawnWet.FirstOrDefault() > 0,
            $"wet cells host [{Lines(host, o => o.wetCells.Cast<object>())}] client [{Lines(client, o => o.wetCells.Cast<object>())}]; " +
            $"drawn wet host [{Lines(host, o => o.drawnWet.Cast<object>())}] client [{Lines(client, o => o.drawnWet.Cast<object>())}]");
        Check("client_walls_match", Same(o => o.raisedWalls.Cast<object>()) && host.raisedWalls.Count == 4 && host.raisedWalls[2] > 0 && host.raisedWalls[3] > 0,
            $"raised wall cells host [{Lines(host, o => o.raisedWalls.Cast<object>())}] client [{Lines(client, o => o.raisedWalls.Cast<object>())}]");
    }

    // The crate the client's Thunderwave shoved: moved on the host, and in the same place on the client.
    void CompareProp(OnlineObservation host, OnlineObservation client)
    {
        float moved = Vector3.Distance(host.propBefore, host.propAfter);
        float apart = Vector3.Distance(host.propAfter, client.propAfter);
        Metric("prop_moved_m", moved);
        Metric("prop_apart_m", apart);
        Check("prop_moved_matches", host.propFound && client.propFound && moved > 1f && apart < 0.05f,
            $"crate {host.propId} moved {moved:0.00} m on the host by the client's Thunderwave; the client's copy {apart * 100f:0.0} cm from it");
    }

    void CompareKnockback(OnlineObservation host, OnlineObservation client)
    {
        // The client's Thunderwave throws the host's wizard on the host's machine (Knockback); its landing on the
        // client's screen is what threw in the Starter Assets controller before WizardRig.PrepareForAnimationEvents.
        float thrown = Vector3.Distance(host.hostWizardBefore, host.hostWizardAfter);
        float wizardApart = Vector3.Distance(host.hostWizardAfter, client.hostWizardAfter);
        Metric("host_wizard_thrown_m", thrown);
        Metric("host_wizard_apart_m", wizardApart);
        Check("player_knockback_matches", thrown > 1f && wizardApart < 0.05f,
            $"the host's wizard thrown {thrown:0.00} m by the client's Thunderwave; the client's copy {wizardApart * 100f:0.0} cm from it");
    }

    // Every prop and item by its stable id (world/things.json): the client adopted the host's, none missing.
    void CompareThings(OnlineObservation host, OnlineObservation client)
    {
        int inData = grid.Things.Count;
        Metric("things_host", host.things.Count);
        Metric("things_client", client.things.Count);
        bool sameThings = host.things.SequenceEqual(client.things);
        string firstOdd = host.things.Except(client.things).Concat(client.things.Except(host.things)).FirstOrDefault();
        Check("thing_ids_match", sameThings && host.things.Count == inData && host.propId == client.propId,
            sameThings ? $"{host.things.Count}/{inData} things, the same ids and kinds on both ({string.Join(", ", host.things.Take(3))}, ...); the pushed crate is {host.propId} on both"
                       : $"host {host.things.Count}, client {client.things.Count} of {inData}; first difference: {firstOdd}; pushed crate host {host.propId} client {client.propId}");
    }
}
