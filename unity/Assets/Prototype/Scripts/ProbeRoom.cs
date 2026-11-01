using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Chem;
using Unity.Netcode;
using UnityEngine;

// The probe's room scenarios (docs/RULES_MCP.md "Change modes"): this Editor hosts a
// room in a change mode, a Multiplayer Play Mode virtual player joins, and both change
// the rules through RuleSwitcher, as the mods panel does. After each step the two compare
// what they saw: the same lines at the same ticks, the same room, the same rules and every
// 30-tick checksum alike. The two talk through their own pair of files beside ProbeLink's.
// roomopen goes on to a reset and a late join; roomhost and roomlocked stop at the refusals.

/// <summary>What the host asks the virtual player to do next, in order.</summary>
[Serializable]
public class RoomProbeAsk
{
    public string id;
    public int step;
    /// <summary>"join", "report", "toggle" with a mod's id, "hostile", "rejoin", "film" (mod: the stand's cell and the target's) or "cut".</summary>
    public string ask, mod;
}

/// <summary>What one side has seen by now.</summary>
[Serializable]
public class RoomProbeSeen
{
    public string id;
    public int step;
    public int netTick;
    public List<string> lines = new();
    public string roomText, rules;
    public int fireballRadius;
    public List<int> hashTicks = new();
    public List<string> hashes = new();
    public bool desync;
    public int errorCount;
    public List<string> errors = new();
}

/// <summary>One side's record of a room: every decision with its tick, and the checksums.</summary>
public sealed class RoomProbeRecord : IDisposable
{
    readonly ChemGrid grid;
    readonly NetChem net;
    readonly RoomProbeSeen seen = new();

    public RoomProbeRecord(ChemGrid grid, NetChem net)
    {
        this.grid = grid;
        this.net = net;
        grid.RoomDecided += Decided;
        net.TickApplied += Ticked;
    }

    public RoomProbeSeen Seen(string id, int step)
    {
        seen.id = id;
        seen.step = step;
        seen.netTick = net.NetTick;
        seen.roomText = grid.World.RoomText();
        seen.rules = grid.World.Rules.Fingerprint;
        seen.fireballRadius = grid.World.Rules.Spell(SpellIds.Fireball).Radius;
        seen.desync = net.DesyncSeen;
        return seen;
    }

    public int Lines => seen.lines.Count;

    void Decided(RoomCommand command, RoomVerdict verdict) => seen.lines.Add(verdict.Line);

    void Ticked(int tick, string hash)
    {
        if (hash == null) return;
        seen.hashTicks.Add(tick);
        seen.hashes.Add(hash);
    }

    public void Dispose()
    {
        grid.RoomDecided -= Decided;
        net.TickApplied -= Ticked;
    }
}

/// <summary>The virtual player's half: starts itself and does what the host's file asks.</summary>
public class RoomProbeClient : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void StartInVirtualPlayer()
    {
        if (!ProbeLink.InVirtualPlayer) return;
        var go = new GameObject("Probe room client");
        DontDestroyOnLoad(go);
        go.AddComponent<RoomProbeClient>();
    }

    static readonly WaitForSecondsRealtime Poll = new(0.1f);

    readonly List<string> errors = new();
    int errorCount, lastStep;
    string lastId;
    RoomProbeRecord record;
    RoomFilm film;

    void OnEnable() => Application.logMessageReceived += OnLog;
    void OnDisable() => Application.logMessageReceived -= OnLog;

    void OnLog(string message, string stack, LogType type)
    {
        if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
        errorCount++;
        if (errors.Count < 10) errors.Add($"{type}: {message}");
    }

    IEnumerator Start()
    {
        while (isActiveAndEnabled)
        {
            yield return Poll;
            var ask = ProbeLink.Read<RoomProbeAsk>(RoomProbe.AskFile);
            if (ask == null || (ask.id == lastId && ask.step <= lastStep)) continue;
            lastId = ask.id;
            lastStep = ask.step;
            yield return Do(ask);
            var seen = record != null ? record.Seen(ask.id, ask.step) : new RoomProbeSeen { id = ask.id, step = ask.step };
            seen.errorCount = errorCount;
            seen.errors = errors;
            ProbeLink.Write(RoomProbe.SeenFile, seen);
        }
    }

    IEnumerator Do(RoomProbeAsk ask)
    {
        var grid = FindAnyObjectByType<ChemGrid>();
        var net = grid != null ? grid.GetComponent<NetChem>() : null;
        switch (ask.ask)
        {
            case "join":
                yield return Join();
                break;
            case "toggle":
                int lines = record.Lines;
                grid.GetComponent<RuleSwitcher>().Toggle(ask.mod);
                for (float t = 0f; t < 3f && record.Lines == lines; t += Time.unscaledDeltaTime) yield return null;
                break;
            case "hostile":
                // Claims to be someone else, with a patch that can't be read.
                int before = record.Lines;
                net.RequestRoom(RoomCommand.Apply("Player 1", "junk", "{ not json"));
                for (float t = 0f; t < 3f && record.Lines == before; t += Time.unscaledDeltaTime) yield return null;
                break;
            case "film":
                var wizard = NetworkManager.Singleton.SpawnManager.GetLocalPlayerObject().gameObject;
                var cells = ask.mod.Split(',').Select(int.Parse).ToArray();
                film = gameObject.AddComponent<RoomFilm>();
                film.Begin(grid, wizard, new Vector2Int(cells[0], cells[1]), new Vector2Int(cells[2], cells[3]), "client");
                break;
            case "cut":
                film.End();
                Destroy(film);
                break;
            case "rejoin":
                record.Dispose();
                record = null;
                var leaving = FindAnyObjectByType<NetSession>().Leave();
                while (!leaving.IsCompleted) yield return null;
                yield return null;
                yield return Join();
                break;
        }
    }

    IEnumerator Join()
    {
        var session = FindAnyObjectByType<NetSession>();
        session.Join("127.0.0.1");
        var grid = FindAnyObjectByType<ChemGrid>();
        var net = grid.GetComponent<NetChem>();
        record = new RoomProbeRecord(grid, net);
        for (float t = 0f; t < 30f && !(net.Online && net.CaughtUp && grid.World.Tick > 0); t += Time.unscaledDeltaTime) yield return null;
    }
}

/// <summary>The host's half.</summary>
public abstract class RoomProbe : ProbeScenario
{
    const float ReplySeconds = 40f;
    protected const string Wildfire = "wildfire", BigFireball = "big-fireball";

    static string Beside(string name) => Path.Combine(Path.GetDirectoryName(ProbeLink.HostFile), name);
    public static string AskFile => Beside("room-ask.json");
    public static string SeenFile => Beside("room-seen.json");
    /// <summary>Written when a game is ready for a Claude outside (roomasks).</summary>
    public static string ReadyFile => Beside("room-ready.json");

    protected abstract ChangeMode Mode { get; }
    protected virtual bool StartsWithAMod => true;

    string id;
    int step;
    protected NetChem Net;
    protected RuleSwitcher Switcher;
    protected RoomProbeRecord Record;
    protected RoomProbeSeen Theirs;

    protected override void OnDestroy()
    {
        Record?.Dispose();
        NetSession.RoomMode = ChangeMode.Open;
        if (File.Exists(AskFile)) File.Delete(AskFile);
        if (File.Exists(SeenFile)) File.Delete(SeenFile);
        base.OnDestroy();
    }

    protected override IEnumerator Run()
    {
        id = Guid.NewGuid().ToString("N").Substring(0, 8);
        if (File.Exists(SeenFile)) File.Delete(SeenFile);
        NetSession.RoomMode = Mode;
        Switcher = grid.GetComponent<RuleSwitcher>();
        // On in solo, before the room is hosted: it stays on as a starting mod, whatever the mode.
        if (StartsWithAMod) Switcher.Toggle(Wildfire);
        yield return Wait(0.5f);
        bool onSolo = !StartsWithAMod || World.Room.Mods.Any(m => m.Id == Wildfire);
        Check("solo_opens_an_open_room", World.Room.Mode == ChangeMode.Open && onSolo, $"{RoomPolicy.Words(World.Room.Mode)}, {World.Room.Mods.Count} mods on");
        FindAnyObjectByType<NetSession>().Host();
        yield return null;
        Net = grid.GetComponent<NetChem>();
        Record = new RoomProbeRecord(grid, Net);
        Check("hosted_in_the_chosen_mode", World.Room.Mode == Mode && World.Room.Mods.Count == (StartsWithAMod ? 1 : 0),
            $"{RoomPolicy.Words(World.Room.Mode)} room, {World.Room.Mods.Count} mods on");

        yield return Ask("join");
        if (Theirs == null) yield break;
        Check("client_has_the_room", Theirs.roomText == World.RoomText(), $"{Theirs.roomText}");
        Check("client_has_the_mods_in_force", Theirs.rules == World.Rules.Fingerprint, $"{Theirs.rules}, here {World.Rules.Fingerprint}");
        yield return Steps();
        yield return Wait(2f);
        yield return Ask("report");
        if (Theirs == null) yield break;
        CompareHashes();
        Check("no_client_errors", Theirs.errorCount == 0, string.Join(" | ", Theirs.errors));
    }

    protected abstract IEnumerator Steps();

    /// <summary>Asks the virtual player, and waits for what it saw; Theirs is null if it never answered.</summary>
    protected IEnumerator Ask(string ask, string mod = null)
    {
        step++;
        ProbeLink.Write(AskFile, new RoomProbeAsk { id = id, step = step, ask = ask, mod = mod });
        Theirs = null;
        for (float t = 0f; t < ReplySeconds && Theirs == null; t += Time.unscaledDeltaTime)
        {
            var seen = ProbeLink.Read<RoomProbeSeen>(SeenFile);
            if (seen != null && seen.id == id && seen.step == step) Theirs = seen;
            yield return null;
        }
        Check($"answered_{step}_{ask}", Theirs != null, $"no answer in {ReplySeconds:0} s");
    }

    /// <summary>Waits for this side to take the next decision in the room (or a few seconds).</summary>
    protected IEnumerator HostToggle(string mod)
    {
        int lines = Record.Lines;
        Switcher.Toggle(mod);
        for (float t = 0f; t < 3f && Record.Lines == lines; t += Time.unscaledDeltaTime) yield return null;
    }

    /// <summary>A step's end: both sides saw the same last line, starting so, and hold the same room and rules.</summary>
    protected IEnumerator Agree(string name, string line)
    {
        yield return Wait(0.6f);
        var mine = Record.Seen(id, step);
        yield return Ask("report");
        if (Theirs == null) yield break;
        Check(name + "_line_here", (mine.lines.LastOrDefault() ?? "").StartsWith(line, StringComparison.Ordinal), mine.lines.LastOrDefault());
        Check(name + "_line_there", Theirs.lines.LastOrDefault() == mine.lines.LastOrDefault(), Theirs.lines.LastOrDefault());
        Check(name + "_same_room", Theirs.roomText == World.RoomText(), $"{Theirs.roomText}, here {World.RoomText()}");
        Check(name + "_same_rules", Theirs.rules == World.Rules.Fingerprint, $"{Theirs.rules}, here {World.Rules.Fingerprint}");
    }

    void CompareHashes()
    {
        var mine = Record.Seen(id, step);
        var here = new Dictionary<int, string>();
        for (int i = 0; i < mine.hashTicks.Count; i++) here[mine.hashTicks[i]] = mine.hashes[i];
        int same = 0, differ = 0;
        for (int i = 0; i < Theirs.hashTicks.Count; i++)
        {
            if (!here.TryGetValue(Theirs.hashTicks[i], out var hash)) continue;
            if (hash == Theirs.hashes[i]) same++;
            else differ++;
        }
        Metric("checksums_compared", same + differ);
        Check("checksums_match", same >= 1 && differ == 0, $"{same} same, {differ} differ");
        Check("no_desync", !Theirs.desync, "the client saw none");
    }
}

/// <summary>Open: the client's change goes in for both, anyone reverts it, and a reset and a late join keep what's on.</summary>
public class RoomOpenProbe : RoomProbe
{
    protected override ChangeMode Mode => ChangeMode.Open;

    protected override IEnumerator Steps()
    {
        int radius = World.Rules.Spell(SpellIds.Fireball).Radius;
        yield return Ask("toggle", BigFireball);
        if (Theirs == null) yield break;
        yield return Agree("client_on", "Player 2 switched on Big fireball (Open room)");
        Check("bigger_on_both", World.Rules.Spell(SpellIds.Fireball).Radius > radius && Theirs.fireballRadius == World.Rules.Spell(SpellIds.Fireball).Radius,
            $"radius {radius} to {World.Rules.Spell(SpellIds.Fireball).Radius}, there {Theirs.fireballRadius}");

        yield return HostToggle(BigFireball);
        yield return Agree("host_reverts", "Player 1 switched off Big fireball (Open room)");
        Check("back_on_both", World.Rules.Spell(SpellIds.Fireball).Radius == radius && Theirs.fireballRadius == radius, $"radius {radius}, there {Theirs.fireballRadius}");

        yield return Ask("hostile");
        yield return Agree("bad_patch", "Switch on refused: junk doesn't pass the checks");
        Check("bad_patch_changes_nothing", World.Room.Mods.Count == 1, $"{World.Room.Mods.Count} mods on");

        yield return Ask("toggle", BigFireball);
        yield return Agree("client_on_again", "Player 2 switched on Big fireball (Open room)");
        Net.StampReset(6);
        int resetAt = Net.LastResetTick;
        for (float t = 0f; t < 8f && Net.LastResetTick == resetAt; t += Time.unscaledDeltaTime) yield return null;
        yield return Wait(1f);
        yield return Ask("report");
        if (Theirs == null) yield break;
        Check("reset_keeps_the_mods", World.Room.Mods.Count == 2 && Theirs.roomText == World.RoomText() && Theirs.rules == World.Rules.Fingerprint,
            $"{World.Room.Mods.Count} mods on, there {Theirs.roomText}");

        // A late joiner after a reset and a change after it: the mods as they stood, then the change.
        yield return HostToggle(Wildfire);
        yield return Wait(0.5f);
        yield return Ask("rejoin");
        if (Theirs == null) yield break;
        Check("late_joiner_has_the_room", Theirs.roomText == World.RoomText(), $"{Theirs.roomText}, here {World.RoomText()}");
        Check("late_joiner_has_the_rules", Theirs.rules == World.Rules.Fingerprint, $"{Theirs.rules}, here {World.Rules.Fingerprint}");
        Check("late_joiner_fireball", Theirs.fireballRadius == World.Rules.Spell(SpellIds.Fireball).Radius, $"radius {Theirs.fireballRadius}");
    }
}

/// <summary>Host only: the client's change is refused with the sentence on both; the host's goes in.</summary>
public class RoomHostProbe : RoomProbe
{
    protected override ChangeMode Mode => ChangeMode.HostOnly;

    protected override IEnumerator Steps()
    {
        yield return Ask("toggle", BigFireball);
        if (Theirs == null) yield break;
        yield return Agree("client_refused", "Switch on refused: only the host can change this room");
        Check("still_one_mod", World.Room.Mods.Count == 1, $"{World.Room.Mods.Count} mods on");
        yield return HostToggle(BigFireball);
        yield return Agree("host_on", "Player 1 switched on Big fireball (Host-only room)");
        yield return Ask("toggle", BigFireball);
        yield return Agree("client_revert_refused", "Revert refused: only the host can change this room");
        Check("still_on", World.Room.Mods.Count == 2, $"{World.Room.Mods.Count} mods on");
    }
}

/// <summary>Locked: nothing goes in, from anyone; the mods it started with stay.</summary>
public class RoomLockedProbe : RoomProbe
{
    const string Refused = "Switch on refused: this room is locked, so its rules stay as it started";

    protected override ChangeMode Mode => ChangeMode.Locked;

    protected override IEnumerator Steps()
    {
        yield return Ask("toggle", BigFireball);
        if (Theirs == null) yield break;
        yield return Agree("client_refused", Refused);
        yield return HostToggle(BigFireball);
        yield return Agree("host_refused", Refused);
        Check("started_with_wildfire_only", World.Room.Mods.Count == 1, $"{World.Room.Mods.Count} mods on");
    }
}

/// <summary>
/// Solo, the rules MCP as a client meets it (RulesMcpServer): over real HTTP, the handshake, the room, a mod on
/// and off with the room's sentence back, and a web page's request turned away.
/// </summary>
public class RoomMcpProbe : ProbeScenario
{
    static readonly HttpClient Http = new();

    static async Task<(int status, string body)> Post(string json, string origin = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, RulesMcpServer.Url) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        if (origin != null) request.Headers.Add("Origin", origin);
        using var response = await Http.SendAsync(request);
        return ((int)response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    static string Tool(string name, string arguments) =>
        $"{{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/call\",\"params\":{{\"name\":\"{name}\",\"arguments\":{arguments}}}}}";

    protected override IEnumerator Run()
    {
        Check("serving", RulesMcpServer.Url != null, RulesMcpServer.ConnectLine);
        if (RulesMcpServer.Url == null) yield break;
        var hello = Post("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-06-18\"}}");
        yield return Until(hello);
        Check("initialize", hello.Result.status == 200 && hello.Result.body.Contains("spell-chemistry-valley"), hello.Result.body);
        var list = Post("{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/list\"}");
        yield return Until(list);
        Check("tools_listed", list.Result.body.Contains("\"apply\"") && list.Result.body.Contains("\"add_spell\""), $"{list.Result.body.Length} characters");
        var silent = Post("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}");
        yield return Until(silent);
        Check("notification_gets_202", silent.Result.status == 202, $"{silent.Result.status}");
        var page = Post(Tool("room", "{}"), "https://example.com");
        yield return Until(page);
        Check("web_page_turned_away", page.Result.status == 403, $"{page.Result.status}");

        int radius = World.Rules.Spell(SpellIds.Fireball).Radius;
        var room = Post(Tool("room", "{}"));
        yield return Until(room);
        Check("room_is_this_game", room.Result.body.Contains("Open room") && room.Result.body.Contains("You play as Player 1"), room.Result.body);
        var on = Post(Tool("apply", "{\"mod\":\"big-fireball\"}"));
        yield return Until(on);
        Check("apply_answers_the_line", on.Result.body.Contains("Player 1 switched on Big fireball (Open room)"), on.Result.body);
        yield return Wait(0.5f);
        Check("fireball_bigger_in_the_game", World.Rules.Spell(SpellIds.Fireball).Radius > radius, $"radius {radius} to {World.Rules.Spell(SpellIds.Fireball).Radius}");
        var bad = Post(Tool("apply", "{\"mod\":\"no-such-mod\"}"));
        yield return Until(bad);
        Check("unknown_mod_is_a_sentence", bad.Result.body.Contains("\"isError\":true"), bad.Result.body);
        var off = Post(Tool("revert", "{\"mod\":\"big-fireball\"}"));
        yield return Until(off);
        Check("revert_answers_the_line", off.Result.body.Contains("Player 1 switched off Big fireball (Open room)"), off.Result.body);
        yield return Wait(0.5f);
        Check("fireball_back", World.Rules.Spell(SpellIds.Fireball).Radius == radius, $"radius {World.Rules.Spell(SpellIds.Fireball).Radius}");
        yield return OffClosesThePort();
        yield return Stills();
    }

    // The Mods page's switch (J-047): off refuses a connection, and on serves again; the choice is kept.
    IEnumerator OffClosesThePort()
    {
        var server = RulesMcpServer.Instance;
        string url = RulesMcpServer.Url;
        server.Serving = false;
        Check("off_says_so", RulesMcpServer.ConnectLine == null && !server.Serving, "no line to show");
        var refused = Http.PostAsync(url, new StringContent("{}", Encoding.UTF8, "application/json"));
        yield return Until(refused);
        Check("off_refuses_the_port", refused.IsFaulted, refused.IsFaulted ? refused.Exception.GetBaseException().Message : $"status {refused.Status}");
        server.Serving = true;
        yield return null;
        var again = Post(Tool("room", "{}"));
        yield return Until(again);
        Check("on_serves_again", again.Status == TaskStatus.RanToCompletion && again.Result.status == 200, again.Status.ToString());
    }

    // With --shots: the menu's change mode, and the Mods page with the switch and the line.
    IEnumerator Stills()
    {
        EventLog.HudVisible = true;
        PauseMenu.Instance.SetOpen(true);
        yield return Wait(0.3f);
        Shot("room-menu");
        yield return Wait(0.3f);
        ModsPanel.SetOpen(true);
        yield return Wait(0.3f);
        Shot("mods-page");
        yield return Wait(0.3f);
        PauseMenu.Instance.SetOpen(false);
    }

    static IEnumerator Until(Task task)
    {
        for (float t = 0f; t < 12f && !task.IsCompleted; t += Time.unscaledDeltaTime) yield return null;
    }
}

/// <summary>
/// Holds a game open for seconds=N while someone outside changes it through the MCP (a real Claude). Passes on
/// the first mod switched on, with what the game said.
/// </summary>
public class RoomClaudeProbe : ProbeScenario
{
    protected override IEnumerator Run()
    {
        Check("serving", RulesMcpServer.Url != null, RulesMcpServer.ConnectLine);
        for (float t = 0f; t < Mathf.Max(30f, Seconds) && World.Room.Mods.Count == 0; t += Time.unscaledDeltaTime) yield return null;
        var mod = World.Room.Mods.FirstOrDefault();
        Check("claude_changed_the_game", mod != null, mod != null ? $"{mod.Title} ({mod.Id}), by {mod.By}" : "no mod came on");
        if (mod == null) yield break;
        yield return Wait(1f);
        Check("line_in_the_log", World.Log.Any(l => l.Text.Contains("switched on")), World.Log.LastOrDefault()?.Text);
        Check("wrote_a_reaction", mod.Json.Contains("reactions"), mod.Json.Length > 400 ? mod.Json.Substring(0, 400) : mod.Json);
        Metric("fireball_radius", World.Rules.Spell(SpellIds.Fireball).Radius);
    }
}

/// <summary>
/// Three asks no saved mod covers, each written by a real Claude outside as a new mod (seconds=N for all three): a
/// bigger fireball, grass burning twice as fast, and a new reaction of fire on water. After each, this casts on a stage
/// of its own and measures what changed against the base rules.
/// </summary>
public class RoomAsksProbe : SpellLooksProbe
{
    const int GrassSize = 13, PondSize = 9;
    const float BurnSeconds = 8f;

    protected override IEnumerator Run()
    {
        var switcher = grid.GetComponent<RuleSwitcher>();
        Check("serving", RulesMcpServer.Url != null, RulesMcpServer.ConnectLine);
        var burn = new float[2];
        yield return Grass(burn);
        float grassBefore = burn[0], cellBefore = burn[1];
        Metric("grass_burnt_base", grassBefore);
        Metric("grass_cell_seconds_base", cellBefore);
        var gas = new int[1];
        yield return Pond(gas);
        int gasBefore = gas[0];
        Metric("gas_base", gasBefore);
        Debug.Log("ROOM ASKS: ready");
        ProbeLink.Write(RoomProbe.ReadyFile, new RoomProbeSeen { id = "ready" });

        float limit = Time.unscaledTime + Mathf.Max(60f, Seconds);
        yield return NewMod(1, limit, switcher);
        Check("fireball_bigger", World.Rules.Spell(SpellIds.Fireball).Radius > 2, $"radius {World.Rules.Spell(SpellIds.Fireball).Radius}");
        var dirt = Spot(14, 14);
        Patch(dirt, 14, 14, TerrainIds.Dirt);
        Stage(dirt, 14);
        yield return Wait(0.3f);
        caster.Cast(SpellIds.Fireball, dirt.x + 7, dirt.y + 7, 0);
        yield return Wait(1.6f);
        Shot("ask-1-fireball");
        Metric("fire_cells_ask_1", BurningCells());
        PutOutFires();

        yield return NewMod(2, limit, switcher);
        yield return Grass(burn);
        Metric("grass_burnt_ask_2", burn[0]);
        Metric("grass_cell_seconds_ask_2", burn[1]);
        Check("grass_burns_faster", burn[0] >= grassBefore * 1.4f, $"{grassBefore} cells burnt out in {BurnSeconds:0} s, then {burn[0]}; a cell took {cellBefore:0.0} s, then {burn[1]:0.0} s");

        yield return NewMod(3, limit, switcher);
        Check("about_the_steam", World.Room.Mods.Count >= 3 && World.Room.Mods[2].Json.Contains("steam"), World.Room.Mods.Count >= 3 ? World.Room.Mods[2].Json : "none");
        yield return Pond(gas);
        Metric("gas_ask_3", gas[0]);
        Check("fire_on_water_does_more", gas[0] > gasBefore, $"{gasBefore} units of steam on the stage, then {gas[0]}");
    }

    IEnumerator NewMod(int count, float limit, RuleSwitcher switcher)
    {
        while (World.Room.Mods.Count < count && Time.unscaledTime < limit) yield return null;
        var mod = World.Room.Mods.ElementAtOrDefault(count - 1);
        Check($"ask_{count}_went_in", mod != null, mod != null ? $"{mod.Title} ({mod.Id})" : "no new mod came on");
        if (mod == null) yield break;
        Check($"ask_{count}_is_a_new_mod", switcher.Catalog.Find(mod.Id) == null, mod.Id);
        yield return Wait(0.6f);
    }

    void Stage(Vector2Int o, int size)
    {
        var mid = grid.CellCenter(o.x + size / 2, o.y + size / 2);
        mid.y = grid.GroundAt(mid);
        wizard.transform.position = grid.CellCenter(o.x + size / 2, o.y - 5) + Vector3.up * (mid.y + 0.1f);
        Cut(mid + new Vector3(0f, 16f, -22f), mid);
    }

    // Fire set at the middle of fresh grass: how many of its cells have burnt out in the time, and how long the first took.
    IEnumerator Grass(float[] burnt)
    {
        var o = Spot(GrassSize, GrassSize);
        Patch(o, GrassSize, GrassSize, TerrainIds.Grass);
        Stage(o, GrassSize);
        yield return Wait(0.3f);
        int mid = GrassSize / 2;
        var first = World.At(o.x + mid, o.y + mid);
        World.Ignite(first, 3);
        burnt[1] = BurnSeconds;
        for (float t = 0f; t < BurnSeconds; t += Time.deltaTime)
        {
            if (first.T != TerrainIds.Grass) burnt[1] = Mathf.Min(burnt[1], t);
            yield return null;
        }
        burnt[0] = 0;
        for (int y = 0; y < GrassSize; y++)
            for (int x = 0; x < GrassSize; x++)
                if (World.At(o.x + x, o.y + y).T != TerrainIds.Grass) burnt[0]++;
        Shot($"grass-{World.Room.Mods.Count}");
        PutOutFires();
    }

    // A Fireball on a shallow pond: how much steam is on it soon after.
    IEnumerator Pond(int[] gas)
    {
        var o = Spot(PondSize, PondSize);
        Patch(o, PondSize, PondSize, TerrainIds.Dirt);
        for (int y = 0; y < PondSize; y++)
            for (int x = 0; x < PondSize; x++)
                World.At(o.x + x, o.y + y).Water = 4;
        Stage(o, PondSize);
        yield return Wait(0.3f);
        caster.Cast(SpellIds.Fireball, o.x + PondSize / 2, o.y + PondSize / 2, 0);
        yield return Wait(1.8f);
        gas[0] = 0;
        for (int y = -3; y < PondSize + 3; y++)
            for (int x = -3; x < PondSize + 3; x++)
            {
                var c = World.At(o.x + x, o.y + y);
                if (c.Gas == GasIds.Steam) gas[0] += c.GasAmt;
            }
        Shot($"pond-{World.Room.Mods.Count}");
        yield return Wait(0.5f);
        PutOutFires();
    }
}

/// <summary>
/// Films this peer, with each frame named by the clock both peers share, for the room clip's two halves side by
/// side: the screen with the HUD on the host, the camera on a virtual player, whose hidden Game view is a sliver.
/// Faces the wizard and its camera at the target meanwhile.
/// </summary>
public class RoomFilm : MonoBehaviour
{
    const float FrameSeconds = 1f / 12f;

    string folder;
    bool rolling;
    WizardPuppet puppet;
    Vector3 target;
    RenderTexture lens;

    /// <summary>The folder both peers film under, beside the probe's files.</summary>
    static string Root => Path.Combine(Path.GetDirectoryName(ProbeLink.HostFile), "room-clip");

    /// <summary>The milliseconds on the shared clock, as frames are named.</summary>
    public static long Now => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    public void Begin(ChemGrid grid, GameObject wizard, Vector2Int stand, Vector2Int at, string side)
    {
        folder = Path.Combine(Root, side);
        Directory.CreateDirectory(folder);
        var body = wizard.GetComponent<CharacterController>();
        body.enabled = false;
        wizard.transform.position = grid.CellCenter(stand.x, stand.y) + Vector3.up * (grid.SurfaceY(stand.x, stand.y) + 0.05f);
        body.enabled = true;
        target = grid.CellCenter(at.x, at.y);
        puppet = new WizardPuppet(wizard);
        puppet.Face(target);
        EventLog.HudVisible = true;
        if (ProbeLink.InVirtualPlayer)
        {
            lens = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Camera.main.targetTexture = lens;
        }
        rolling = true;
        StartCoroutine(Roll());
    }

    public void End()
    {
        rolling = false;
        if (lens != null) Camera.main.targetTexture = null;
    }

    void Update()
    {
        if (rolling) puppet.AimCamera(target, 18f, 6f);
    }

    IEnumerator Roll()
    {
        var end = new WaitForEndOfFrame();
        float next = 0f;
        while (rolling)
        {
            yield return end;
            if (Time.unscaledTime < next) continue;
            next = Time.unscaledTime + FrameSeconds;
            var picture = lens != null ? Read(lens) : ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(Path.Combine(folder, $"{Now}.jpg"), picture.EncodeToJPG(85));
            Destroy(picture);
        }
    }

    static Texture2D Read(RenderTexture from)
    {
        var picture = new Texture2D(from.width, from.height, TextureFormat.RGB24, false);
        RenderTexture.active = from;
        picture.ReadPixels(new Rect(0, 0, from.width, from.height), 0, 0);
        RenderTexture.active = null;
        picture.Apply();
        return picture;
    }
}

/// <summary>
/// The clip for Andrew's board: both peers film their own screens while the host's wizard casts a Fireball, a real
/// Claude outside asks for a bigger one through the MCP, and the next Fireball is cast. Needs that Claude run while it holds.
/// </summary>
public class RoomClipProbe : RoomProbe
{
    static readonly Vector2Int HostStand = new(12, 14), ClientStand = new(15, 14), Middle = new(13, 7), First = new(9, 8), Second = new(17, 8);

    protected override ChangeMode Mode => ChangeMode.Open;
    protected override bool StartsWithAMod => false;

    protected override IEnumerator Steps()
    {
        var wizard = NetworkManager.Singleton.SpawnManager.GetLocalPlayerObject().gameObject;
        wizard.GetComponent<SpellCaster>().enabled = false;
        var film = gameObject.AddComponent<RoomFilm>();
        var middle = grid.YardCell(Middle.x, Middle.y);
        film.Begin(grid, wizard, grid.YardCell(HostStand.x, HostStand.y), middle, "host");
        var stand = grid.YardCell(ClientStand.x, ClientStand.y);
        yield return Ask("film", $"{stand.x},{stand.y},{middle.x},{middle.y}");
        if (Theirs == null) yield break;
        yield return Wait(2f);

        Metric("first_ms", RoomFilm.Now);
        Fireball(wizard, First);
        yield return Wait(7f);
        Metric("asking_ms", RoomFilm.Now);
        int radius = World.Rules.Spell(SpellIds.Fireball).Radius;
        for (float t = 0f; t < Mathf.Max(30f, Seconds) && World.Room.Mods.Count == 0; t += Time.unscaledDeltaTime) yield return null;
        Metric("line_ms", RoomFilm.Now);
        Check("claude_changed_the_game", World.Rules.Spell(SpellIds.Fireball).Radius > radius, $"radius {radius} to {World.Rules.Spell(SpellIds.Fireball).Radius}");
        yield return Wait(3f);
        Metric("second_ms", RoomFilm.Now);
        Fireball(wizard, Second);
        yield return Wait(7f);
        Metric("end_ms", RoomFilm.Now);
        film.End();
        yield return Ask("cut");
    }

    void Fireball(GameObject wizard, Vector2Int at)
    {
        var cell = grid.YardCell(at.x, at.y);
        wizard.GetComponent<SpellCaster>().Cast(SpellIds.Fireball, cell.x, cell.y, 0);
    }
}
