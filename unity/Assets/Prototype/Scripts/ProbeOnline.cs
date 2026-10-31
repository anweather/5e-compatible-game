using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Chem;
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
    /// <summary>After the reset: the host asks for the checksums since it (up to startTick), the virtual player sends them.</summary>
    public const string ResetSettled = "reset-settled", ResetCheck = "reset-check", ResetReported = "reset-reported";

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
    /// <summary>The host's wizard seen here in an animal's shape, then back in its own (Shapeshift, NetWizard's form).</summary>
    public bool sawForm, sawFormEnd;
    /// <summary>The beats the other wizard's cast went through here (CastBeats), one line for each copy that moved.</summary>
    public List<string> otherBeats = new();
    /// <summary>The hit-stops that wizard's copy had here: none, they're its owner's view.</summary>
    public int otherStops;
    public int joinedTick;
    /// <summary>The virtual player's lag at the start tick: ticks it knew of but hadn't run yet (0 caught up).</summary>
    public int lagAtStart;
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
    /// <summary>Every player's casts and grimoire (World.CastState) at the sample tick.</summary>
    public string castState;
    /// <summary>The glow balls left at the sample tick (the host breaks one).</summary>
    public int glowBalls;
    public string sampledChecksum;
}

/// <summary>The shared script, in sim ticks after the start tick (6 a second). Yard cells; directions 0 N, 1 E, 2 S, 3 W.</summary>
public static class OnlineSchedule
{
    /// <summary>The glow balls still in the world, on this side.</summary>
    public static List<Carryable> GlowBalls() => UnityEngine.Object.FindObjectsByType<Carryable>(FindObjectsSortMode.InstanceID)
        .Where(c => c.Holder == null && c.TryGetComponent<PhysicsProp>(out var p) && p.kind.StartsWith("glow-ball", StringComparison.Ordinal)).ToList();

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
        S(30, false, SpellIds.FireBolt, (5, 13), (2, 12), 3),         // the meadow, west of the old yard
        S(36, true, SpellIds.FireBolt, (16, 11), (19, 3), 0),         // the oil spill by the barrels
        S(42, false, SpellIds.EarthenTrench, (16, 5), (15, 5), 3),    // west from the pond's bank: fills
        S(48, true, SpellIds.EarthenTrench, (16, 8), (15, 8), 3),
        S(54, false, SpellIds.WallOfStone, (10, 6), (12, 7), 0),
        S(60, true, SpellIds.WallOfStone, (6, 4), (8, 4), 0),
        S(72, false, Fall, (10, 6), (10, 6), 0),                  // the host's wizard falls and lands
    };

    /// <summary>When the shoved crate has settled (before any fire can reach it).</summary>
    public const int PropTick = 15;
    /// <summary>When both sides write down what they see: 20 s after the fall, while the fires still spread.</summary>
    public const int SampleTick = 192;

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
        var spawner = UnityEngine.Object.FindAnyObjectByType<ThingSpawner>();
        return spawner != null ? spawner.Find(id) : null;
    }

    /// <summary>Every thing this side has, as "id kind", sorted by id.</summary>
    public static List<string> Things()
    {
        var spawner = UnityEngine.Object.FindAnyObjectByType<ThingSpawner>();
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
            Seen.castState = grid.World.CastState();
            Seen.glowBalls = OnlineSchedule.GlowBalls().Count;
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

    // This side's wizard once it's in the game and caught up with the host's history, else null.
    static GameObject JoinedWizard(ChemGrid grid, NetChem net)
    {
        var network = NetworkManager.Singleton;
        var local = network != null && network.IsConnectedClient ? network.SpawnManager.GetLocalPlayerObject() : null;
        return local != null && net.Online && grid.World.Tick > 0 && net.CaughtUp ? local.gameObject : null;
    }

    IEnumerator Take(string id)
    {
        errorCount = 0;
        errors.Clear();
        var session = FindAnyObjectByType<NetSession>();
        session.Join("127.0.0.1");
        var grid = FindAnyObjectByType<ChemGrid>();
        var net = grid.GetComponent<NetChem>();
        GameObject wizard = null;
        for (float waited = 0f; waited < 30f && wizard == null; waited += Time.unscaledDeltaTime)
        {
            wizard = JoinedWizard(grid, net);
            if (wizard == null) yield return null;
        }
        if (wizard == null) yield break;   // the host times out waiting for "joined"
        var recorder = new OnlineProbeRecorder(grid, client: true);
        recorder.Seen.id = id;
        recorder.Seen.phase = ProbeLink.Joined;
        recorder.Seen.joinedTick = grid.World.Tick;
        ProbeLink.Write(ProbeLink.ClientFile, recorder.Seen);

        ProbeHostMessage go = null;
        for (float waited = 0f; waited < 60f; waited += Time.unscaledDeltaTime)
        {
            go = ProbeLink.Read<ProbeHostMessage>(ProbeLink.HostFile);
            if (go != null && go.id == id && go.phase == ProbeLink.Go) break;
            go = null;
            yield return null;
        }
        if (go == null) { recorder.Stop(); yield break; }
        recorder.StartTick = go.startTick;
        // At the start tick: how far behind the host this side still is (ticks it knows of but
        // hasn't run), then caught up before it casts anything.
        while (grid.World.Tick < go.startTick) yield return null;
        recorder.Seen.lagAtStart = Lag(net);
        while (!net.CaughtUp) yield return null;
        net.RequestCombo(ComboCommand.Load("", ElementIds.Water));
        net.RequestCombo(ComboCommand.Load("", ElementIds.Air));
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
        yield return AfterReset(id, net);
    }

    // The other wizard's shape as this side draws it.
    static void WatchForms(OnlineObservation seen)
    {
        var mine = NetworkManager.Singleton != null ? NetworkManager.Singleton.SpawnManager.GetLocalPlayerObject() : null;
        foreach (var shape in FindObjectsByType<Shapeshift>(FindObjectsSortMode.None))
        {
            if (mine != null && shape.gameObject == mine.gameObject) continue;
            if (shape.ModelHidden) seen.sawForm = true;
            else if (seen.sawForm) seen.sawFormEnd = true;
        }
    }

    // The other wizard's beats on this side.
    static CastBeats OtherBeats()
    {
        var mine = NetworkManager.Singleton != null ? NetworkManager.Singleton.SpawnManager.GetLocalPlayerObject() : null;
        return FindObjectsByType<NetWizard>(FindObjectsSortMode.None).Where(w => mine == null || w.gameObject != mine.gameObject)
            .Select(w => w.GetComponent<CastBeats>()).FirstOrDefault();
    }

    // Ticks this side knows of but hasn't run yet (NetChem's knownUpTo - NetTick; 0 when caught up).
    static int Lag(NetChem net)
    {
        var known = typeof(NetChem).GetField("knownUpTo", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return known != null ? System.Math.Max(0, (int)known.GetValue(net) - net.NetTick) : -1;
    }

    // Tells the host once the reset's stall is over here and this side has caught up (OnlineProbe.ClientSettled).
    static IEnumerator TellSettled(string id, NetChem net)
    {
        for (float waited = 0f; waited < 90f; waited += Time.unscaledDeltaTime)
        {
            if (net.LastResetTick > 0 && Lag(net) <= 1)
            {
                ProbeLink.Write(ProbeLink.ClientFile, new OnlineObservation { id = id, phase = ProbeLink.ResetSettled });
                yield break;
            }
            yield return null;
        }
    }

    // The host then resets the world (OnlineProbe.TryReset): the checksums from after the reset,
    // sent when the host asks, up to the tick it names.
    IEnumerator AfterReset(string id, NetChem net)
    {
        var after = new OnlineObservation { id = id };
        var other = OtherBeats();
        void Heard(Beat beat) => after.otherBeats.Add(beat.ToString());
        if (other != null) other.BeatChanged += Heard;
        void OnTick(int tick, string checksum)
        {
            if (checksum == null || net.LastResetTick <= 0 || tick <= net.LastResetTick) return;
            after.hashTicks.Add(tick);
            after.hashes.Add(checksum);
        }
        net.TickApplied += OnTick;
        ProbeHostMessage ask = null;
        StartCoroutine(TellSettled(id, net));
        for (float waited = 0f; waited < 90f && ask == null; waited += Time.unscaledDeltaTime)
        {
            ask = ProbeLink.Read<ProbeHostMessage>(ProbeLink.HostFile);
            if (ask == null || ask.id != id || ask.phase != ProbeLink.ResetCheck) ask = null;
            WatchForms(after);
            yield return null;
        }
        for (float waited = 0f; ask != null && waited < 20f && net.NetTick < ask.startTick; waited += Time.unscaledDeltaTime) yield return null;
        net.TickApplied -= OnTick;
        if (other != null) other.BeatChanged -= Heard;
        if (ask == null) yield break;
        after.otherStops = other != null ? other.Stops : 0;
        after.phase = ProbeLink.ResetReported;
        after.desync = net.DesyncSeen;
        after.errorCount = errorCount;
        after.errors = new List<string>(errors);
        ProbeLink.Write(ProbeLink.ClientFile, after);
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
        if (resetNet != null) resetNet.TickApplied -= CollectAfterReset;
        ProbeLink.Clear();
        base.OnDestroy();
    }

    protected override IEnumerator Run()
    {
        string id = Guid.NewGuid().ToString("N").Substring(0, 8);
        ProbeLink.Clear();
        var session = FindAnyObjectByType<NetSession>();
        session.Host();
        yield return null;
        wizard = NetworkManager.Singleton.SpawnManager.GetLocalPlayerObject().gameObject;
        puppet = new WizardPuppet(wizard);
        caster = wizard.GetComponent<SpellCaster>();
        caster.enabled = false;
        // Before anyone joins, so the joiner has to replay it (NetChem's combo history).
        var net = grid.GetComponent<NetChem>();
        net.RequestCombo(ComboCommand.Load("", ElementIds.Water));
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
        ballsBefore = OnlineSchedule.GlowBalls().Count;
        net.RequestCombo(ComboCommand.Load("", ElementIds.Earth));
        recorder.Play(wizard);
        recorder.RecordProp(after: false);
        bool ballBroken = false;
        while (!recorder.Sampled)
        {
            recorder.Play(wizard);
            // After both sides have listed every thing (RecordProp at PropTick).
            if (!ballBroken && World.Tick > recorder.StartTick + OnlineSchedule.PropTick)
            {
                ballBroken = true;
                BreakBall();
            }
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
        Check("virtual_player_reported", report != null, report != null ? $"sampled at tick {report.sampledTick}; client {report.lagAtStart} ticks behind at the start tick" : $"no report in {ReportSeconds:0} s");
        if (report == null) yield break;
        Compare(recorder.Seen, report);
        yield return TryReset();
        yield return ClientSettled(id);
        yield return TryTag();
        yield return TryShape();
        yield return TryBeats();
        yield return CompareAfterReset(id);
    }

    // A reset stalls a peer's frame for a couple of seconds and leaves it ticks behind. A deer that
    // came and went inside that stall is never drawn there, so what follows waits for it. (A peer
    // catching up keeps its beats too: castfeel's mid_tick cast.)
    IEnumerator ClientSettled(string id)
    {
        float waited = 0f;
        for (; waited < ReportSeconds && ReadClient(id, ProbeLink.ResetSettled) == null; waited += Time.unscaledDeltaTime) yield return null;
        Metric("reset_client_settled_s", waited);
    }

    int ballsBefore;
    ThingId ballId = ThingId.None;

    // The host's checksums since the reset (TryReset starts collecting them).
    readonly Dictionary<int, string> afterReset = new();
    NetChem resetNet;

    void CollectAfterReset(int tick, string checksum)
    {
        if (checksum != null && resetNet.LastResetTick > 0 && tick > resetNet.LastResetTick) afterReset[tick] = checksum;
    }

    // Lockstep across the reset: every 30-tick checksum after it equal on both sides, three or more.
    IEnumerator CompareAfterReset(string id)
    {
        for (float t = 0f; t < 30f && afterReset.Count < 3; t += Time.unscaledDeltaTime) yield return null;
        int upTo = afterReset.Count > 0 ? afterReset.Keys.Max() : resetNet.NetTick;
        ProbeLink.Write(ProbeLink.HostFile, new ProbeHostMessage { id = id, phase = ProbeLink.ResetCheck, startTick = upTo });
        OnlineObservation client = null;
        for (float t = 0f; t < 30f && client == null; t += Time.unscaledDeltaTime)
        {
            client = ReadClient(id, ProbeLink.ResetReported);
            yield return null;
        }
        resetNet.TickApplied -= CollectAfterReset;
        if (client == null)
        {
            Check("reset_checksums_equal", false, "the virtual player sent no checksums after the reset");
            yield break;
        }
        Check("shape_seen_online", client.sawForm, "the host's wizard drawn as a deer on the virtual player");
        Check("shape_ended_online", client.sawFormEnd, "and drawn as itself again once a cast button ended it (issue #6)");
        Check("remote_wizard_has_no_hit_stop", client.otherStops == 0 && wizard.GetComponent<CastBeats>().Stops > 0,
            $"its copy on the virtual player {client.otherStops}, the host's own {wizard.GetComponent<CastBeats>().Stops}");
        Check("beats_seen_online", client.otherBeats.SequenceEqual(hostBeats) && hostBeats.Contains(nameof(Beat.Release)) && hostBeats.Contains(nameof(Beat.Charge)),
            $"host [{string.Join(" ", hostBeats)}], its wizard on the virtual player [{string.Join(" ", client.otherBeats)}]");
        int compared = 0, equal = 0;
        string firstDiff = null;
        for (int i = 0; i < client.hashTicks.Count; i++)
        {
            if (!afterReset.TryGetValue(client.hashTicks[i], out var expected)) continue;
            compared++;
            if (expected == client.hashes[i]) equal++;
            else firstDiff ??= $"tick {client.hashTicks[i]}: host {expected}, client {client.hashes[i]}";
        }
        Metric("reset_checksums_compared", compared);
        Check("reset_checksums_equal", compared >= 3 && equal == compared && !client.desync,
            firstDiff ?? $"{equal}/{compared} checksums equal after the reset (tick {resetNet.LastResetTick}); client desync {client.desync}, {client.errorCount} client errors");
    }

    // The host takes a deer's shape and ends it as a cast button does (Shapeshift.ChangeBackByChoice), on its own machine;
    // the virtual player reports what it drew (WatchForms). Every animal is local, so the deer is only this side's.
    IEnumerator TryShape()
    {
        var shape = wizard.GetComponent<Shapeshift>();
        var fauna = FindAnyObjectByType<ValleyFauna>();
        int deer = fauna != null ? fauna.SpeciesIndex("Deer") : -1;
        var at = wizard.transform.position + Vector3.right * 3f;
        if (deer < 0 || shape == null)
        {
            Check("shape_taken_online", false, "no deer or no Shapeshift");
            yield break;
        }
        fauna.Puppet(fauna.Spawn(deer, new Unity.Mathematics.float3(at.x, grid.GroundAt(at), at.z), 0f), 0f, 0f);
        yield return null;
        var cell = grid.CellAt(at);
        Check("shape_taken_online", shape.Land(World.Rules.Spell(SpellIds.Shapeshift), cell.x, cell.y, 1), shape.FormName ?? "not shifted");
        yield return Wait(1f);
        Check("shape_ended_by_choice", shape.ChangeBackByChoice(), "ChangeBackByChoice");
        yield return Wait(1f);
    }

    readonly List<string> hostBeats = new();

    // The host casts a pair and a charged single through the core; the virtual player must show the same beats.
    IEnumerator TryBeats()
    {
        var beats = wizard.GetComponent<CastBeats>();
        void Heard(Beat beat) => hostBeats.Add(beat.ToString());
        beats.BeatChanged += Heard;
        var aim = wizard.transform.position + wizard.transform.forward * 5f;
        aim.y = grid.GroundAt(aim);
        caster.AimOverride = aim;
        caster.enabled = true;
        caster.LoadElement(ElementIds.Water);
        yield return Wait(0.4f);
        caster.LoadElement(ElementIds.Earth);
        yield return UntilNeutral(beats);
        // A new cast cuts Recovery short by design, so one on the heels of Neutral could land before
        // that beat on the other side; this compares the beats, not that race.
        yield return Wait(0.3f);
        caster.LoadElement(ElementIds.Fire, held: true);
        yield return Wait(1.6f);
        caster.LetGo();
        yield return UntilNeutral(beats);
        caster.enabled = false;
        caster.AimOverride = null;
        beats.BeatChanged -= Heard;
    }

    static IEnumerator UntilNeutral(CastBeats beats)
    {
        for (float t = 0f; t < 0.3f; t += Time.deltaTime) yield return null;
        for (float t = 0f; t < 6f && beats.Now != Beat.Neutral; t += Time.deltaTime) yield return null;
    }

    // Tag: the host starts it as "it" and steps up to the virtual player's wizard, who is then it.
    IEnumerator TryTag()
    {
        var tag = grid.GetComponent<NetTag>();
        var network = NetworkManager.Singleton;
        var other = network.ConnectedClientsList.FirstOrDefault(c => c.ClientId != network.LocalClientId);
        bool started = tag != null && other != null && tag.StartWith(network.LocalClientId);
        if (!started)
        {
            Check("tag_passes_on_touch", false, "tag didn't start (no NetTag, or no second player)");
            yield break;
        }
        var body = wizard.GetComponent<CharacterController>();
        body.enabled = false;
        wizard.transform.position = other.PlayerObject.transform.position + new Vector3(0.6f, 0.2f, 0f);
        body.enabled = true;
        for (int i = 0; i < 30 && tag.It == network.LocalClientId; i++) yield return null;
        Check("tag_passes_on_touch", tag.It == other.ClientId, $"it: {tag.It} (host {network.LocalClientId}, other {other.ClientId})");
        tag.Stop();
    }

    // Reset (R / View) online (J-014, OnlineReset): the host's resets the world for everyone after a
    // countdown, without reloading the scene. Before it, a prop is broken and the wizard walks off;
    // after it, every thing is back with its id (the broken one too) and the wizard is at its start.
    IEnumerator TryReset()
    {
        var net = grid.GetComponent<NetChem>();
        resetNet = net;
        net.TickApplied += CollectAfterReset;
        var spawner = FindAnyObjectByType<ThingSpawner>();
        var before = new HashSet<ThingId>(spawner.Live.Select(t => t.id));
        if (!ballId.IsNone) before.Add(ballId); // the ball broken earlier comes back too
        var broken = spawner.Live.Select(t => t.prop).FirstOrDefault(p => p.State != null && p.kind == EntityIds.Crate);
        var brokenId = broken != null ? broken.Thing : ThingId.None;
        if (broken != null) broken.Hurt(9999);
        var start = grid.CellCenter(grid.PlayerStart.x, grid.PlayerStart.y);
        Teleport(wizard.transform.position + new Vector3(8f, 0.5f, 0f));
        int lastReset = net.LastResetTick, heard = -1;
        void Heard(int tick) => heard = tick;
        net.ResetComing += Heard;
        bool reloaded = EventLog.ResetYard();
        float waited = 0f;
        for (; waited < 8f && net.LastResetTick == lastReset; waited += Time.unscaledDeltaTime) yield return null;
        net.ResetComing -= Heard;
        for (int i = 0; i < 10; i++) yield return null;
        int players = NetworkManager.Singleton.ConnectedClientsIds.Count;
        Check("reset_online", !reloaded && players == 2 && net.LastResetTick > lastReset && heard == net.LastResetTick,
            $"reloaded {reloaded}, {players} players; countdown heard for tick {heard}, reset at {net.LastResetTick} after {waited:0.0} s");
        Metric("reset_wait_s", waited);
        var after = new HashSet<ThingId>(spawner.Live.Select(t => t.id));
        Check("reset_things_back", before.Count > 0 && before.SetEquals(after) && (brokenId.IsNone || after.Contains(brokenId)),
            $"{after.Count} things after, {before.Count} before; {before.Except(after).Count()} missing, {after.Except(before).Count()} new; broken {brokenId} back: {after.Contains(brokenId)}");
        var flat = wizard.transform.position - start;
        flat.y = 0f;
        Check("reset_wizard_at_start", flat.magnitude < 2f * grid.cellSize + 0.5f, $"{flat.magnitude:0.0} m from the start cell");
        Check("reset_clock_restarts", World.Tick < 60, $"world tick {World.Tick} (network tick {net.NetTick})");
    }

    // The virtual player's client.json if it is from this run and in the given phase (any phase when null), else null.
    static OnlineObservation ReadClient(string id, string phase)
    {
        var seen = ProbeLink.Read<OnlineObservation>(ProbeLink.ClientFile);
        return seen != null && seen.id == id && (phase == null || seen.phase == phase) ? seen : null;
    }

    // A glow ball hurled at the ground near the wizard: the host's physics breaks it for everyone.
    void BreakBall()
    {
        var ball = OnlineSchedule.GlowBalls().FirstOrDefault();
        if (ball == null) return;
        ballId = ball.GetComponent<PhysicsProp>().Thing;
        var from = wizard.transform.position + wizard.transform.forward * 2f + Vector3.up * 1.5f;
        var body = ball.GetComponent<Rigidbody>();
        body.isKinematic = false;
        body.position = from;
        ball.transform.position = from;
        body.linearVelocity = Vector3.down * 12f;
    }

    void CompareCombos(OnlineObservation host, OnlineObservation client)
    {
        Metric("glow_balls_before", ballsBefore);
        Metric("glow_balls_host", host.glowBalls);
        Metric("glow_balls_client", client.glowBalls);
        Check("broken_ball_gone_on_both", ballsBefore > 0 && host.glowBalls == ballsBefore - 1 && client.glowBalls == host.glowBalls,
            $"{ballsBefore} glow balls, then {host.glowBalls} on the host and {client.glowBalls} on the client");
        Check("combo_state_equal", host.castState == client.castState, $"host {host.castState}; client {client.castState}");
        string me = NetworkManager.Singleton.LocalClientId == 0 ? "p0" : $"p{NetworkManager.Singleton.LocalClientId}";
        Check("late_joiner_replays_combos", World.KnowsCombo(me, "water") || World.KnowsCombo(me, "water+earth"), host.castState);
        Check("client_combo_reached_everyone", host.castState != null && host.castState.Contains("water+air", StringComparison.Ordinal), host.castState);
    }

    void Compare(OnlineObservation host, OnlineObservation client)
    {
        CompareChecksums(host, client);
        CompareCombos(host, client);
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
        Metric("client_lag_at_start", client.lagAtStart);
        Metric("first_checksum_tick", compared > 0 ? client.hashTicks.First(mine.ContainsKey) : -1);
        Metric("last_checksum_tick", compared > 0 ? client.hashTicks.Last(mine.ContainsKey) : -1);
        Check("checksums_equal", compared >= 3 && equal == compared,
            (firstDiff ?? $"{equal}/{compared} checksums equal (every 30 ticks, {client.hashTicks.FirstOrDefault()} to {client.hashTicks.LastOrDefault()})") + $"; client {client.lagAtStart} ticks behind at the start tick");
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
