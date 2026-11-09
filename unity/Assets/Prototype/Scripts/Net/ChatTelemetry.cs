using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Chem;
using Chem.Mcp;
using Newtonsoft.Json.Linq;
using UnityEngine;

// What each prompt to Claude cost (docs/design/chat-telemetry.md): the game's half of the record in
// ChatRecorder. It keeps the clock, stamps "in the world" once this peer has the change drawn, counts
// what the frames paid, writes the record off the main thread, and adds a later line when a mod is
// reverted or a next prompt fixes it. Local only: nothing here leaves the machine. The list in the
// Claude menu reads these records and counts nothing for itself. They hold the player's prompts, so they
// go in a folder of their own, not beside Player.log, which players send with a bug report. RulesMcpServer adds it.
[RequireComponent(typeof(GameChat))]
public class ChatTelemetry : MonoBehaviour
{
    const float GraceSeconds = 5f, HitchMs = 50f, SecondsPerMs = 0.001f, Thousand = 1000f;
    const int SettleFrames = 2, MostShown = 8, MostPromptChars = 34;

    /// <summary>One finished prompt, as the Claude menu lists it.</summary>
    public sealed class Entry
    {
        public string Id, Prompt, Outcome;
        public long Tokens;
        public double? Cost;
        public long? WorldMs;
    }

    static ChatRecorder live;

    readonly Stopwatch clock = Stopwatch.StartNew();
    readonly List<Entry> finished = new();
    // What's on from which record, by mod id, and what each record's patches touched.
    readonly Dictionary<string, (string record, long at)> onFrom = new();
    readonly List<(string record, HashSet<string> entries)> touched = new();
    readonly HashSet<string> touching = new();
    readonly object gate = new();

    ChatRecorder current;
    string currentId;
    ChemGrid hooked;
    bool settling, stamped;
    int settleFrames, hitches;
    float worstMs;
    float? endedAt;

    /// <summary>The telemetry, or null where the game doesn't serve Claude.</summary>
    public static ChatTelemetry Instance { get; private set; }

    /// <summary>Where the records go in place of Home, for the probe.</summary>
    public static string Folder { get; set; }

    // Each Play mode starts from these, with the domain reload off too (Enter Play Mode Options): a probe sets them.
    [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        Instance = null;
        live = null;
        Folder = null;
    }

    /// <summary>Where the records and the MCP log go now: ChatLog.Home unless the probe says otherwise. On the main thread.</summary>
    public static string Home => Folder ?? ChatLog.Home(ChemGrid.Folders.PlayerData, GivenLogFolder());

    // The folder of a log this process was told to use: a test's. Null for a player started as players start it.
    static string GivenLogFolder()
    {
        string log = Application.consoleLogPath;
        bool given = Application.isEditor || Environment.GetCommandLineArgs().Any(a => a.Equals("-logFile", StringComparison.OrdinalIgnoreCase));
        return given && !string.IsNullOrEmpty(log) ? Path.GetDirectoryName(log) : null;
    }

    /// <summary>The prompt being answered, for the progress bar; null with none.</summary>
    public ChatRecorder Current => current;

    /// <summary>The newest prompts first, at most a few.</summary>
    public IEnumerable<Entry> Latest => Enumerable.Reverse(finished).Take(MostShown);

    /// <summary>The clock every time in a record is read from, in ms.</summary>
    public long Now => clock.ElapsedMilliseconds;

    void Awake()
    {
        Instance = this;
        QuickExit.BeforeExit += StillOn;
    }

    void OnEnable() => PauseMenu.AddSection(DrawList, PauseMenu.LastOrder, PauseMenu.Submenu.Claude);

    void OnDisable() => PauseMenu.RemoveSection(DrawList);

    void OnDestroy()
    {
        QuickExit.BeforeExit -= StillOn;
        StillOn();
        Hook(null);
        if (Instance == this) Instance = null;
    }

    /// <summary>A prompt is going to Claude; source is typed or voice. A record still open is written first.</summary>
    public ChatRecorder Begin(string prompt, string source)
    {
        if (current != null) Flush();
        currentId = Guid.NewGuid().ToString("N").Substring(0, 12);
        var grid = ChemGrid.Current?.Grid;
        current = new ChatRecorder(prompt, source, Now, Who(grid, currentId));
        live = current;
        worstMs = 0f;
        hitches = 0;
        settling = stamped = false;
        endedAt = null;
        lock (gate) touching.Clear();
        Hook(grid);
        Note("send");
        return current;
    }

    /// <summary>The process has gone: the record is written once the world has the change, or never will.</summary>
    public void ProcessEnded()
    {
        if (current != null && endedAt == null) endedAt = Time.unscaledTime;
    }

    /// <summary>A patch reached the room, from the MCP's thread: the record keeps it whole.</summary>
    public static void Patched(string key, string patch)
    {
        live?.Patched(key, patch);
        var telemetry = Instance;
        if (telemetry != null) telemetry.Touch(patch);
    }

    void Touch(string patch)
    {
        lock (gate) touching.UnionWith(Entries(patch));
    }

    // What a patch touches: each section's entries, as "terrain.stucco".
    static IEnumerable<string> Entries(string patch)
    {
        JObject json;
        try
        {
            json = JObject.Parse(patch);
        }
        catch (Newtonsoft.Json.JsonException)
        {
            yield break;
        }
        foreach (var section in (json["patch"] as JObject ?? json).Properties())
            if (section.Value is JObject entries)
                foreach (var entry in entries.Properties()) yield return section.Name + "." + entry.Name;
    }

    static ChatWho Who(ChemGrid grid, string id)
    {
        var room = grid != null && grid.World != null ? grid.World.Room : null;
        return new ChatWho
        {
            Id = id,
            GameVersion = BuildInfo.Stamp.Version,
            Commit = BuildInfo.Stamp.Commit,
            RoomMode = room != null ? room.Mode.ToString() : "",
            Players = Unity.Netcode.NetworkManager.Singleton != null && Unity.Netcode.NetworkManager.Singleton.IsListening ? Unity.Netcode.NetworkManager.Singleton.ConnectedClientsIds.Count : 1,
            ModsOn = room != null ? room.Mods.Select(m => m.Title ?? m.Id).ToList() : new List<string>(),
        };
    }

    void Hook(ChemGrid grid)
    {
        if (hooked != null) hooked.RoomDecided -= Decided;
        hooked = grid;
        if (hooked != null) hooked.RoomDecided += Decided;
    }

    void Decided(RoomCommand command, RoomVerdict verdict)
    {
        if (!verdict.Done) return;
        if (command.Change == RoomChange.Revert && onFrom.Remove(command.Mod, out var was))
            Write(ChatLog.Afterwards(was.record, Now - was.at));
        if (current == null || !hooked.TryGetComponent<RuleSwitcher>(out var me) || command.Peer != me.Me) return;
        if (command.Change == RoomChange.Apply) onFrom[command.Mod] = (currentId, Now);
        Note($"apply {command.Mod}");
        settling = true;
        settleFrames = 0;
    }

    void Update()
    {
        if (current == null) return;
        float ms = Time.unscaledDeltaTime * Thousand;
        if (!stamped)
        {
            worstMs = Mathf.Max(worstMs, ms);
            if (ms > HitchMs) hitches++;
        }
        if (settling && ++settleFrames >= SettleFrames && !ViewBusy()) Stamp();
        if (endedAt != null && (!settling || Time.unscaledTime - endedAt > GraceSeconds)) Flush();
    }

    // The first frame after the switch with nothing left queued to draw.
    bool ViewBusy()
    {
        var valley = hooked != null ? hooked.valley : null;
        return valley != null && valley.Recolouring;
    }

    void Stamp()
    {
        settling = false;
        stamped = true;
        current.InWorld(Now);
        Note("in the world");
    }

    void Flush()
    {
        double draw = hooked != null && hooked.AnnouncedAt > 0f ? (Time.realtimeSinceStartup - hooked.AnnouncedAt) * Thousand : 0;
        current.GameCost(worstMs, hitches, hooked != null ? hooked.ComposeMs : 0, stamped ? draw : 0);
        var record = current.Record();
        Fixes(currentId);
        finished.Add(EntryOf(record));
        current = null;
        live = null;
        Write(record);
    }

    // The plain rule: a prompt that applies over what an earlier one did fixes it.
    void Fixes(string id)
    {
        HashSet<string> now;
        lock (gate) now = new HashSet<string>(touching);
        if (now.Count == 0) return;
        foreach (var (record, entries) in touched)
            if (entries.Overlaps(now)) Write(ChatLog.Afterwards(record, null, id));
        touched.Add((id, now));
    }

    static Entry EntryOf(JObject record)
    {
        var tokens = record["tokens"];
        long count = new[] { "input", "cacheWrite", "cacheRead", "output" }.Sum(k => (long?)tokens?[k] ?? 0);
        return new Entry
        {
            Id = (string)record["id"],
            Prompt = (string)record["prompt"],
            Outcome = (string)record["outcome"],
            Tokens = count,
            Cost = (double?)tokens?["cost"],
            WorldMs = (long?)record["at"]?["inWorld"],
        };
    }

    static void Write(JObject line)
    {
        string folder = Home;
        var day = DateTime.UtcNow;
        Task.Run(() => ChatLog.Append(folder, day, line));
    }

    // At the end of a session: what stayed on says so.
    void StillOn()
    {
        string folder = Home;
        foreach (var (_, was) in onFrom) ChatLog.Append(folder, DateTime.UtcNow, ChatLog.Afterwards(was.record, null));
        onFrom.Clear();
    }

    void Note(string what)
    {
        if (PlayLog.Instance != null) PlayLog.Instance.Note($"chat {currentId} {what}");
    }

    void DrawList()
    {
        GUILayout.Label(finished.Count == 0 ? "Prompts: none yet" : "Prompts, newest first (dollars are a rough list price, not money spent):");
        foreach (var e in Latest) GUILayout.Label(Line(e));
    }

    /// <summary>A prompt's line in the list.</summary>
    public static string Line(Entry e)
    {
        string prompt = e.Prompt == null || e.Prompt.Length <= MostPromptChars ? e.Prompt : e.Prompt.Substring(0, MostPromptChars) + "...";
        string cost = e.Cost is { } dollars ? $", about ${dollars:0.00} list" : "";
        string world = e.WorldMs is { } ms ? $", {ms * SecondsPerMs:0.0} s" : "";
        return $"{prompt}: {e.Outcome}, {e.Tokens / Thousand:0.#}k tokens{cost}{world}";
    }
}
