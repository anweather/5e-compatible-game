using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Chem;
using Chem.Mcp;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

// The probe's chatprompts scenario (J-064, #59): a fixed set of prompts through the game's real chat,
// each on a fresh world, so a record has real "in the world" times, which chatbench can't have. A
// stand-in answers unless --real-claude true is given: that spends real prompts on whoever is signed in,
// so it's only ever by hand, and capped here. It works in its own folder, not the player's, and if
// nobody is signed in it stops; it never signs in. Beside each record it keeps the raw stream, so the
// record can be checked against something it didn't make, and a watch of what the frames show, so
// the stamp can be too. With --shots it applies the recorded patches again and films them, for nothing.
// Records and streams stay in the --out folder (docs/telemetry/chat-base-2026-11-02.md reads them).
public class ChatPromptsProbe : RowHousesProbe
{
    /// <summary>The most prompts one run may spend, whatever it's asked for (J-064).</summary>
    public const int MostPrompts = 8;

    // Past ClaudeChat's own watchdog, which ends a run at 240 s.
    const float MostSeconds = 260f, MostReset = 10f, MostWrite = 5f, SettleSeconds = 1f, WatchOn = 3f, SlowMs = 50f, ReplayBurn = 3f, FireAtY = 12f;
    const int FireAtX = 2, FireDir = 3, HouseLook = 4;
    // J-064: no runs past this share of the weekly limit. Each real stream says the share in its rate_limit_event.
    const double MostWeekly = 0.85;
    const string PromptsFile = "chat-prompts.json";
    static readonly string[] ClaudesOwn = { "CLAUDECODE", "CLAUDE_CODE_ENTRYPOINT", "CLAUDE_CODE_SESSION_ID" };
    static readonly HashSet<string> WorldTools = new() { "apply", "revert", "set_time", "set_weather" };

    // What the frames showed for one prompt, from the world and the view themselves, in ms from the send.
    sealed class Watch
    {
        public RuleSet rules;
        public int rows, strength, peak, framesAfter;
        public long? rulesMs, rowsMs, skyMs, peakMs, lastRecolourMs, stampMs, lastSlowMs;
        public float worstAfter;
    }

    string outDir, chatFolder, modsDir;
    string[] modsWere;
    int spent;
    volatile string rateLine;
    readonly object tapping = new();

    protected override IEnumerator Run()
    {
        houses = grid.GetComponent<RowHouses>();
        var prompts = Set();
        int count = Mathf.Min(MostPrompts, Prompts > 0 ? Prompts : prompts.Count, prompts.Count);
        Check("the_set_fits_the_cap", prompts.Count is > 0 and <= MostPrompts, $"{prompts.Count} prompts");
        outDir = OutDir ?? Path.Combine(Application.temporaryCachePath, "chat-prompts");
        chatFolder = Path.Combine(outDir, "claude-terminal");
        modsDir = Path.Combine(ChemGrid.FindRulesDir(), "mods");
        modsWere = Directory.GetFiles(modsDir).OrderBy(f => f, StringComparer.Ordinal).ToArray();
        if (RealClaude && !SafeToSpend()) yield break;
        Directory.CreateDirectory(Path.Combine(outDir, "raw"));
        ClaudeTerminal.TestFolder = chatFolder;
        ChatTelemetry.Folder = outDir;
        McpLog.Folder = outDir;
        ClaudeChat.StandIn = RealClaude ? null : StandIn();
        try
        {
            var records = new List<(JObject prompt, JObject record)>();
            for (int i = 0; i < count; i++)
            {
                var prompt = prompts[i];
                if (i == 0 || (bool?)prompt["keep"] != true) yield return Fresh();
                JObject record = null;
                yield return Send(i + 1, prompt, r => record = r);
                if (record == null || !Carries(i + 1, prompt, record)) break;
                records.Add((prompt, record));
            }
            yield return Fresh();
            Metric("prompts_spent", RealClaude ? spent : 0);
            Check("every_prompt_went", records.Count == count, $"{records.Count} of {count}");
            if (!string.IsNullOrEmpty(ScreenshotDir)) yield return Replays(records);
        }
        finally
        {
            ClaudeChat.Tap = null;
            ClaudeChat.StandIn = null;
            ClaudeTerminal.TestFolder = null;
            ChatTelemetry.Folder = null;
        }
    }

    static List<JObject> Set()
    {
        string file = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "docs", "telemetry", PromptsFile));
        return ((JArray)JObject.Parse(File.ReadAllText(file))["prompts"]).Cast<JObject>().ToList();
    }

    // Everything that must hold before a real prompt goes. One that doesn't spends nothing.
    bool SafeToSpend()
    {
        bool outside = !string.IsNullOrEmpty(OutDir) && Path.IsPathRooted(OutDir)
            && !Path.GetFullPath(OutDir).StartsWith(Path.GetFullPath(Application.persistentDataPath), StringComparison.OrdinalIgnoreCase);
        Check("out_folder_is_given_and_not_the_players", outside, OutDir);
        bool freshFolder = outside && (!Directory.Exists(OutDir) || Directory.GetFiles(OutDir, "chat-log-*").Length == 0);
        Check("out_folder_holds_no_earlier_run", freshFolder, OutDir);
        var inherited = ClaudesOwn.Where(n => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(n))).ToList();
        Check("this_editor_isnt_inside_a_claude_session", inherited.Count == 0, string.Join(", ", inherited));
        Check("claude_is_here", ClaudeChat.FindClaude() != null);
        Check("the_server_is_up", RulesMcpServer.Url != null, RulesMcpServer.Url);
        Check("the_chat_is_here", GameChat.Instance != null && ChatTelemetry.Instance != null);
        return Result.Checks.All(c => c.Pass);
    }

    // A script standing in for claude, for the proof that spends nothing.
    string StandIn()
    {
        Directory.CreateDirectory(outDir);
        return ChatProbe.Script(outDir, Path.Combine(outDir, "asked.txt"), "stand-in", 1f,
            "{\"type\":\"system\",\"subtype\":\"init\",\"model\":\"stand-in\",\"claude_code_version\":\"0\"}",
            "{\"type\":\"assistant\",\"message\":{\"id\":\"m1\",\"model\":\"stand-in\",\"content\":[{\"type\":\"tool_use\",\"id\":\"t1\",\"name\":\"mcp__spell-valley__whats_on\",\"input\":{}}]}}",
            "{\"type\":\"result\",\"subtype\":\"success\",\"is_error\":false,\"result\":\"Done.\",\"total_cost_usd\":0.05,\"num_turns\":2,\"duration_ms\":100,\"duration_api_ms\":90,\"usage\":{\"input_tokens\":10,\"cache_creation_input_tokens\":20,\"cache_read_input_tokens\":3000,\"output_tokens\":40}}");
    }

    // No mods, the weather its own, no notes and no drafts left: a prompt starts as a new game does.
    IEnumerator Fresh()
    {
        var switcher = grid.GetComponent<RuleSwitcher>();
        foreach (var mod in switcher.Room.Mods.AsEnumerable().Reverse().ToList()) switcher.Ask(RoomCommand.Revert(switcher.Me, mod.Id));
        switcher.Ask(RoomCommand.SetWeather(switcher.Me, null));
        for (float t = 0f; t < MostReset && (switcher.Room.Mods.Count > 0 || Busy()); t += Time.unscaledDeltaTime) yield return null;
        Check("fresh_world_has_no_mods", switcher.Room.Mods.Count == 0, string.Join(", ", switcher.Room.Mods.Select(m => m.Id)));
        string notes = Path.Combine(chatFolder, ClaudeRun.NotesFile);
        if (File.Exists(notes)) File.Delete(notes);
        RulesMcpServer.Instance.FreshTools();
        yield return Wait(SettleSeconds);
    }

    bool Busy() => grid.valley != null && grid.valley.Recolouring;

    IEnumerator Send(int n, JObject prompt, Action<JObject> done)
    {
        string name = $"p{n}-{(string)prompt["kind"]}", text = (string)prompt["text"];
        var telemetry = ChatTelemetry.Instance;
        var chat = GameChat.Instance;
        using var raw = new StreamWriter(Path.Combine(outDir, "raw", name + ".jsonl"), false, new UTF8Encoding(false)) { AutoFlush = true };
        ClaudeChat.Tap = (line, at) =>
        {
            lock (tapping) raw.Write($"{DateTime.UtcNow:o}\t{at}\t{line}\n");
            if (line.Contains("\"rate_limit_event\"", StringComparison.Ordinal)) rateLine = line;
        };
        var watch = new Watch { rules = grid.CurrentRules, rows = houses.Count, strength = StrengthNow() };
        long sent = telemetry.Now;
        spent++;
        Metric("prompts_spent", RealClaude ? spent : 0);
        chat.Send(text);
        var recorder = telemetry.Current;
        Check($"{name}_went", recorder != null, text);
        if (recorder == null) yield break;
        if (!RealClaude) grid.GetComponent<RuleSwitcher>().Toggle(StandInMod);
        float t = 0f;
        while (t < MostSeconds && (chat.Busy || telemetry.Current != null))
        {
            Look(watch, recorder, telemetry.Now - sent);
            t += Time.unscaledDeltaTime;
            yield return null;
        }
        bool ended = !chat.Busy && telemetry.Current == null;
        Check($"{name}_ended", ended, $"after {t:0} s");
        for (float on = 0f; ended && on < WatchOn; on += Time.unscaledDeltaTime)
        {
            Look(watch, recorder, telemetry.Now - sent);
            yield return null;
        }
        ClaudeChat.Tap = null;
        string id = telemetry.Latest.FirstOrDefault()?.Id;
        JObject record = null;
        for (float w = 0f; w < MostWrite && record == null; w += Time.unscaledDeltaTime)
        {
            record = RecordOf(id);
            yield return null;
        }
        Keep(n, name, sent, prompt, record, watch);
        if (record == null) Check($"{name}_has_a_record", false, id);
        done(record);
    }

    void Look(Watch watch, ChatRecorder recorder, long ms)
    {
        // The object, not its fingerprint: a look doesn't change the fingerprint.
        if (watch.rulesMs == null && !ReferenceEquals(grid.CurrentRules, watch.rules)) watch.rulesMs = ms;
        if (watch.rowsMs == null && houses.Count != watch.rows) watch.rowsMs = ms;
        if (Busy()) watch.lastRecolourMs = ms;
        int strength = StrengthNow();
        if (watch.skyMs == null && strength != watch.strength) watch.skyMs = ms;
        if (strength > watch.peak)
        {
            watch.peak = strength;
            watch.peakMs = ms;
        }
        if (watch.stampMs == null && recorder.Progress().InWorld) watch.stampMs = ms;
        if (watch.stampMs == null) return;
        float frame = Time.unscaledDeltaTime * 1000f;
        watch.framesAfter++;
        watch.worstAfter = Mathf.Max(watch.worstAfter, frame);
        if (frame > SlowMs) watch.lastSlowMs = ms;
    }

    // The share of the weekly limit used, as the last stream had it; null if it hasn't said.
    double? Weekly()
    {
        string line = rateLine;
        return line == null ? null : (double?)JObject.Parse(line)["rate_limit_info"]?["unifiedWindows"]?["seven_day"]?["utilization"];
    }

    int StrengthNow() => grid.World.Weather.Kind == WeatherKind.Clear ? 0 : grid.World.Weather.Strength;

    JObject RecordOf(string id)
    {
        if (id == null) return null;
        foreach (string file in Directory.GetFiles(outDir, "chat-log-*.jsonl"))
        {
            string[] lines;
            try
            {
                lines = File.ReadAllLines(file);
            }
            catch (IOException)
            {
                return null; // still being written
            }
            foreach (string line in lines)
            {
                var o = JObject.Parse(line);
                if ((string)o["id"] == id) return o;
            }
        }
        return null;
    }

    // One line per prompt beside the records: which is which, the send's clock, the notes as they were, the watch.
    void Keep(int n, string name, long sent, JObject prompt, JObject record, Watch watch)
    {
        string notes = Path.Combine(chatFolder, ClaudeRun.NotesFile);
        if (File.Exists(notes)) File.Copy(notes, Path.Combine(outDir, "raw", name + "-notes.md"), true);
        var line = new JObject
        {
            ["n"] = n,
            ["name"] = name,
            ["prompt"] = prompt["text"],
            ["expected"] = prompt["outcome"],
            ["record"] = record?["id"],
            ["outcome"] = record?["outcome"],
            ["sentClock"] = sent,
            ["url"] = RulesMcpServer.Url,
            ["notesBytes"] = File.Exists(notes) ? new FileInfo(notes).Length : 0,
            ["weekly"] = Weekly(),
            ["watch"] = JObject.FromObject(new
            {
                inWorld = (long?)record?["at"]?["inWorld"],
                stampSeen = watch.stampMs,
                rulesChanged = watch.rulesMs,
                rowsChanged = watch.rowsMs,
                lastRecolouring = watch.lastRecolourMs,
                skyChanged = watch.skyMs,
                skyPeak = watch.peak,
                skyPeakAt = watch.peakMs,
                framesAfterStamp = watch.framesAfter,
                worstFrameAfterStamp = Math.Round(watch.worstAfter, 1),
                lastSlowFrameAfterStamp = watch.lastSlowMs,
            }),
        };
        File.AppendAllText(Path.Combine(outDir, "prompts.jsonl"), line.ToString(Formatting.None) + "\n");
    }

    // Whether the run should go on. A wrong answer is a finding and it does; what would waste the next prompt doesn't.
    bool Carries(int n, JObject prompt, JObject record)
    {
        string name = $"p{n}-{(string)prompt["kind"]}", outcome = (string)record["outcome"], why = (string)record["why"] ?? "";
        Metric(name + "_world_ms", (double?)record["at"]?["inWorld"] ?? -1);
        Metric(name + "_done_ms", (double?)record["at"]?["done"] ?? -1);
        bool signedOut = ClaudeRun.NeedsSignIn(why) || ClaudeRun.NeedsSignIn((string)record["reply"]);
        Check($"{name}_is_signed_in", !signedOut, signedOut ? why : null);
        bool same = Directory.GetFiles(modsDir).OrderBy(f => f, StringComparer.Ordinal).SequenceEqual(modsWere);
        Check($"{name}_left_rules_mods_alone", same);
        if (!RealClaude) return true;
        var weekly = Weekly();
        bool under = weekly == null || weekly <= MostWeekly;
        Metric("weekly_used", weekly ?? -1);
        Check($"{name}_under_the_weekly_limit", under, weekly?.ToString(CultureInfo.InvariantCulture));
        var used = ((JArray)record["tools"]).Where(t => (bool?)t["ok"] == true).Select(t => (string)t["name"]).ToList();
        var wanted = (prompt["tools"] as JArray)?.Select(t => (string)t).ToList() ?? new List<string>();
        Check($"{name}_outcome", outcome == (string)prompt["outcome"], $"{outcome}: {why}");
        Check($"{name}_tools", wanted.Count == 0 || wanted.Any(used.Contains), string.Join(", ", used));
        if ((bool?)prompt["noWorldTools"] == true) Check($"{name}_changed_nothing", !used.Any(WorldTools.Contains), string.Join(", ", used));
        return same && under && !signedOut && outcome is not ("failed" or "timedout" or "stopped");
    }

    const string StandInMod = "big-fireball";

    // The recorded patches again, with nobody's Claude: each must apply as it stands (a record can rebuild what
    // was done), and shows itself where a camera can see it. The fire's blue only shows on a fire.
    IEnumerator Replays(List<(JObject prompt, JObject record)> records)
    {
        var switcher = grid.GetComponent<RuleSwitcher>();
        int n = 0;
        foreach (var (prompt, record) in records)
        {
            n++;
            string kind = (string)prompt["kind"];
            var change = (record["changes"] as JArray)?.OfType<JObject>().LastOrDefault(c => c["patch"] is JObject);
            if (change == null) continue;
            int rowsWere = grid.World.BuildingsNow.Count();
            var before = grid.World.BuildingsNow.Select(r => r.Id).ToHashSet();
            string mod = $"replay-{n}";
            switcher.Ask(RoomCommand.Apply(switcher.Me, mod, change["patch"].ToString(Formatting.None), (string)change["title"]));
            for (float t = 0f; t < MostReset && !switcher.Room.Mods.Exists(m => m.Id == mod); t += Time.unscaledDeltaTime) yield return null;
            Check($"replay_{kind}_goes_on", switcher.Room.Mods.Exists(m => m.Id == mod), switcher.LastError);
            yield return Wait(SettleSeconds);
            if (kind is "colour" or "follow-up") yield return Burn(kind);
            var row = grid.World.BuildingsNow.FirstOrDefault(r => !before.Contains(r.Id));
            if (row != null)
            {
                Check($"replay_{kind}_draws_a_row", houses.Root(row.Id) != null && grid.World.BuildingsNow.Count() > rowsWere, row.Id);
                yield return Look(kind, row.X + HouseLook, row.Y + HouseLook);
            }
            yield return Fresh();
        }
    }

    IEnumerator Burn(string kind)
    {
        yield return CastFromBar(SpellIds.FireBolt, FireAtX, (int)FireAtY, FireDir, ReplayBurn);
        var cell = grid.YardCell(FireAtX, (int)FireAtY);
        yield return Look(kind, cell.x, cell.y);
    }
}
