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
// --model, --context and --also change how claude starts, for this run only: with none, it starts as a player's does.
// --notes off sends the message without notes.md, for this run only: the file's still there.
// --replay casts the spells earlier runs recorded, also for nothing: a cast is the one thing a record can't say.
// Records and streams stay in the --out folder (docs/telemetry/chat-base-2026-11-02.md reads them).
public class ChatPromptsProbe : RowHousesProbe
{
    /// <summary>The most prompts one run may spend, whatever it's asked for (J-064).</summary>
    public const int MostPrompts = 8;

    // Past ClaudeChat's own watchdog, which ends a run at 240 s.
    const float MostSeconds = 260f, MostReset = 10f, MostWrite = 5f, SettleSeconds = 1f, WatchOn = 3f, SlowMs = 50f, ReplayBurn = 3f, FireAtY = 12f;
    const int FireAtX = 2, FireDir = 3, HouseLook = 4, CastDir = 2, WaterSearch = 24, WaterStep = 3;
    const float CastLands = 3f;
    // J-064: no runs past this share of the weekly limit. Each real stream says the share in its rate_limit_event.
    const double MostWeekly = 0.85, MostFiveHour = 0.60;
    const string PromptsFile = "chat-prompts.json", BareContext = "bare", NotesOff = "off", StandInNote = "- The stand-in's note.", CastsFile = "casts.jsonl", AskedFile = "asked.txt", ApiHarness = "api", LongCache = "1h";
    // No user settings (their skills and plugins) and no slash commands. Neither reads or moves the login.
    static readonly string[] BareArguments = { "--setting-sources", "project,local", "--disable-slash-commands" };
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
    ApiBudget budget;
    string[] modsWere;
    int spent;
    volatile string rateLine;
    readonly object tapping = new();

    protected override IEnumerator Run()
    {
        houses = grid.GetComponent<RowHouses>();
        if (!string.IsNullOrEmpty(Replay))
        {
            yield return CastThem();
            yield break;
        }
        var prompts = Chosen();
        int count = Mathf.Min(MostPrompts, Prompts > 0 ? Prompts : prompts.Count, prompts.Count);
        bool knownContext = string.IsNullOrEmpty(Context) || Context == BareContext;
        Check("the_context_is_known", knownContext, Context);
        bool knownHarness = string.IsNullOrEmpty(Harness) || Harness == ApiHarness;
        Check("the_harness_is_known", knownHarness, Harness);
        bool knownNotes = string.IsNullOrEmpty(Notes) || Notes == NotesOff;
        Check("the_notes_switch_is_known", knownNotes, Notes);
        if (!knownContext || !knownHarness || !knownNotes) yield break;
        outDir = OutDir ?? Path.Combine(Application.temporaryCachePath, "chat-prompts");
        chatFolder = Path.Combine(outDir, "claude-terminal");
        modsDir = Path.Combine(ChemGrid.FindRulesDir(), "mods");
        modsWere = ModsNow();
        if (RealClaude && !SafeToSpend()) yield break;
        Directory.CreateDirectory(Path.Combine(outDir, "raw"));
        ClaudeTerminal.TestFolder = chatFolder;
        ChatTelemetry.Folder = outDir;
        McpLog.Folder = outDir;
        ClaudeChat.StandIn = RealClaude ? null : StandIn();
        ClaudeChat.Model = string.IsNullOrEmpty(Model) ? null : Model;
        ClaudeChat.Extra = Arguments();
        ClaudeChat.NotesOff = Notes == NotesOff;
        var harnessWas = GameChat.Instance.Harness;
        if (Api) GameChat.Instance.Harness = ApiHarnessNow();
        try
        {
            yield return SendAll(prompts, count);
        }
        finally
        {
            GameChat.Instance.Harness = harnessWas;
            ClaudeChat.Tap = null;
            ClaudeChat.StandIn = null;
            ClaudeChat.Model = null;
            ClaudeChat.Extra = null;
            ClaudeChat.NotesOff = false;
            ClaudeTerminal.TestFolder = null;
            ChatTelemetry.Folder = null;
        }
    }

    bool Api => Harness == ApiHarness;

    // The game's own loop: on a stand-in unless real money was asked for, which needs a budget file SafeToSpend has checked.
    ApiChat ApiHarnessNow()
    {
        budget = new ApiBudget(RealClaude ? BudgetFile : Path.Combine(outDir, "stand-in-budget.json"));
        IChatWire wire = RealClaude ? new ApiWire() : new ApiStandIn();
        return new ApiChat(wire, budget, string.IsNullOrEmpty(Model) ? ClaudeRun.ChatModel : Model, string.IsNullOrEmpty(PrefixTtl) ? LongCache : PrefixTtl);
    }

    // The set, or the one kind asked for.
    List<JObject> Chosen()
    {
        var prompts = Set();
        if (!string.IsNullOrEmpty(OnlyKind)) prompts = prompts.Where(p => (string)p["kind"] == OnlyKind).ToList();
        Check("the_set_fits_the_cap", prompts.Count is > 0 and <= MostPrompts, $"{prompts.Count} prompts");
        return prompts;
    }

    IEnumerator SendAll(List<JObject> prompts, int count)
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
        if (!RealClaude && !Api) CheckAsked();
        if (!RealClaude && Api) CheckStandInLoop(records);
        if (!string.IsNullOrEmpty(ScreenshotDir)) yield return Replays(records);
    }

    // What this run adds to claude's arguments: nothing unless asked.
    List<string> Arguments()
    {
        var more = new List<string>();
        if (Context == BareContext) more.AddRange(BareArguments);
        if (!string.IsNullOrEmpty(Also)) more.AddRange(Also.Split(';', StringSplitOptions.RemoveEmptyEntries));
        return more;
    }

    // The stand-in wrote down how it was started: the same as a player's, plus only what this run asked for.
    void CheckAsked()
    {
        string file = Path.Combine(outDir, AskedFile), asked = File.Exists(file) ? File.ReadAllText(file) : "";
        bool bare = asked.Contains(BareArguments[0]) && asked.Contains(BareArguments[2]);
        string model = string.IsNullOrEmpty(Model) ? ClaudeRun.ChatModel : Model;
        Check("claude_gets_the_chats_model_unless_asked", asked.Split("--model").Length == 2 && asked.Contains("--model " + model), asked);
        Check("claude_gets_bare_only_if_asked", bare == (Context == BareContext), asked);
        Check("claude_gets_the_chats_own_prompt", asked.Contains(ClaudeRun.ChatPrompt), asked);
        Check("the_notes_go_unless_off", asked.Contains(StandInNote) != ClaudeChat.NotesOff, asked);
    }

    List<JObject> Set()
    {
        string file = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "docs", "telemetry", string.IsNullOrEmpty(SetFile) ? PromptsFile : SetFile));
        return ((JArray)JObject.Parse(File.ReadAllText(file))["prompts"]).Cast<JObject>().ToList();
    }

    // The loop on its stand-in: a tool outside its list never reached the server, and the record reads as Claude Code's does.
    void CheckStandInLoop(List<(JObject prompt, JObject record)> records)
    {
        var first = records.FirstOrDefault().record;
        Check("the_loop_keeps_a_record", (string)first?["harness"] == ApiHarness && (string)first["tokens"]?["costBasis"] == ApiLoop.RealMoney, first?.ToString());
        var used = (first?["tools"] as JArray ?? new JArray()).Select(t => (string)t["name"] + "=" + (bool?)t["ok"]).ToList();
        Check("the_loop_turned_a_shell_away", used.Contains("Bash=False") && used.Contains("whats_on=True"), string.Join(", ", used));
        var save = (first?["tools"] as JArray ?? new JArray()).FirstOrDefault(t => (string)t["name"] == "save");
        Check("the_server_turned_an_unasked_save_away", save != null && (bool?)save["ok"] == false && ((string)save["reason"] ?? "").StartsWith("Saving is the player's call", StringComparison.Ordinal), save?.ToString());
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
        if (Api)
        {
            Check("a_key_is_here", ApiWire.HasKey);
            Check("the_budget_has_room", !string.IsNullOrEmpty(BudgetFile) && new ApiBudget(BudgetFile).HasRoom, BudgetFile);
        }
        else Check("claude_is_here", ClaudeChat.FindClaude() != null);
        Check("the_server_is_up", RulesMcpServer.Url != null, RulesMcpServer.Url);
        Check("the_chat_is_here", GameChat.Instance != null && ChatTelemetry.Instance != null);
        return Result.Checks.All(c => c.Pass);
    }

    // A script standing in for claude, for the proof that spends nothing.
    string StandIn()
    {
        Directory.CreateDirectory(outDir);
        return ChatProbe.Script(outDir, Path.Combine(outDir, AskedFile), "stand-in", 1f,
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
        if (!RealClaude && !Api) PlantNote();
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

    // A stand-in writes no notes: this one is what shows whether they go with the message.
    void PlantNote()
    {
        Directory.CreateDirectory(chatFolder);
        File.WriteAllText(Path.Combine(chatFolder, ClaudeRun.NotesFile), StandInNote + "\n");
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

    // The share of a limit used, as the last stream had it; null if it hasn't said.
    double? Window(string name)
    {
        string line = rateLine;
        return line == null ? null : (double?)JObject.Parse(line)["rate_limit_info"]?["unifiedWindows"]?[name]?["utilization"];
    }

    double? Weekly() => Window("seven_day");

    double? FiveHour() => Window("five_hour");

    // What's true in the world once a prompt has ended, from the rules in force: chat-strict scores this
    // over the record's patches. A spell is kept if it's fire or not in the base rules.
    JObject WorldNow()
    {
        var rules = JObject.Parse(grid.World.Rules.ToJson());
        var plain = grid.CurrentRules.Base;
        var spells = new JArray();
        foreach (var spell in ((JArray)rules["spells"]).OfType<JObject>())
            if ((string)spell["element"] == ElementIds.Fire || plain.Spell((string)spell["id"]) == null)
                spells.Add(new JObject { ["id"] = spell["id"], ["group"] = spell["group"], ["element"] = spell["element"], ["level"] = spell["level"], ["shape"] = spell["shape"], ["origin"] = spell["origin"], ["radius"] = spell["radius"], ["radial"] = spell["radial"], ["damage"] = spell["damage"], ["look"] = spell["look"] });
        var switcher = grid.GetComponent<RuleSwitcher>();
        return new JObject
        {
            ["mods"] = new JArray(switcher.Room.Mods.Select(m => m.Id)),
            ["fire"] = rules["elementLooks"]?["fire"]?["look"]?["color"],
            ["embers"] = rules["covers"]?["embers"]?["look"]?["color"],
            ["grass"] = new JObject { ["catch"] = rules["terrain"]?["grass"]?["flammability"], ["fuel"] = rules["terrain"]?["grass"]?["fuel"] },
            ["spells"] = spells,
            ["elementColours"] = new JObject(((JObject)rules["elementLooks"]).Properties().Select(p => new JProperty(p.Name, p.Value["look"]?["color"]))),
            ["spellColours"] = new JObject(((JArray)rules["spells"]).OfType<JObject>().Select(s => new JProperty((string)s["id"], s["look"]?["color"]))),
            ["burn"] = Burning(rules),
            ["weather"] = grid.World.Weather.Kind.ToString(),
            ["rows"] = new JArray(grid.World.Rules.ModBuildings.Select(r => r.Id)),
        };
    }

    // Each material's catch and fuel, as the engine has them, with what burns only.
    static JObject Burning(JObject rules)
    {
        var burn = new JObject();
        foreach (var table in new[] { "terrain", "covers" })
            foreach (var material in ((JObject)rules[table]).Properties().Where(p => p.Value["fuel"] != null))
                burn[material.Name] = new JObject { ["catch"] = material.Value["flammability"], ["fuel"] = material.Value["fuel"] };
        return burn;
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
            ["fiveHour"] = FiveHour(),
            ["askedModel"] = string.IsNullOrEmpty(Model) ? null : Model,
            ["context"] = string.IsNullOrEmpty(Context) ? null : Context,
            ["notes"] = string.IsNullOrEmpty(Notes) ? null : Notes,
            ["after"] = WorldNow(),
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
        TakeSaved(name);
        bool same = ModsNow().SequenceEqual(modsWere);
        Check($"{name}_left_rules_mods_alone", same);
        if (!RealClaude) return true;
        if (Api && !budget.HasRoom)
        {
            Check($"{name}_under_the_budget", false, $"{budget.Spent}");
            return false;
        }
        var weekly = Weekly();
        var fiveHour = FiveHour();
        bool under = (weekly == null || weekly <= MostWeekly) && (fiveHour == null || fiveHour <= MostFiveHour);
        Metric("weekly_used", weekly ?? -1);
        Metric("five_hour_used", fiveHour ?? -1);
        Check($"{name}_under_the_limits", under, $"weekly {weekly?.ToString(CultureInfo.InvariantCulture)}, five-hour {fiveHour?.ToString(CultureInfo.InvariantCulture)}");
        bool theModel = string.IsNullOrEmpty(Model) || (string)record["model"] == Model;
        Check($"{name}_ran_the_model_asked", theModel, (string)record["model"]);
        var used = ((JArray)record["tools"]).Where(t => (bool?)t["ok"] == true).Select(t => (string)t["name"]).ToList();
        var wanted = (prompt["tools"] as JArray)?.Select(t => (string)t).ToList() ?? new List<string>();
        Check($"{name}_outcome", outcome == (string)prompt["outcome"], $"{outcome}: {why}");
        Check($"{name}_tools", wanted.Count == 0 || wanted.Any(used.Contains), string.Join(", ", used));
        if ((bool?)prompt["noWorldTools"] == true) Check($"{name}_changed_nothing", !used.Any(WorldTools.Contains), string.Join(", ", used));
        return same && under && theModel && !signedOut && outcome is not ("failed" or "timedout" or "stopped");
    }

    // Each mod file and when it was written: a model can save over one as well as add one.
    string[] ModsNow() => Directory.GetFiles(modsDir).OrderBy(f => f, StringComparer.Ordinal).Select(f => $"{f}|{File.GetLastWriteTimeUtc(f).Ticks}").ToArray();

    // A mod a model saved unasked is a finding, not a fault: it goes beside the record and out of the rules.
    // Anything else that moved in rules/mods stays there for Carries to stop on.
    void TakeSaved(string name)
    {
        var known = modsWere.Select(m => m.Substring(0, m.LastIndexOf('|'))).ToHashSet();
        foreach (string file in Directory.GetFiles(modsDir).Where(f => !known.Contains(f)))
        {
            File.Move(file, Path.Combine(outDir, "raw", $"{name}-saved-{Path.GetFileName(file)}"));
            Metric("mods_saved_unasked", Result.Metrics.GetValueOrDefault("mods_saved_unasked") + 1);
        }
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

    // Each run's recorded spell, cast as a player would, on open water where it should show. Spends nothing.
    IEnumerator CastThem()
    {
        chatFolder = Path.Combine(Application.temporaryCachePath, "chat-prompts", "claude-terminal");
        var switcher = grid.GetComponent<RuleSwitcher>();
        bool found = FindShore(out var shore);
        Check("lake_shore_found", found);
        if (!found) yield break;
        var yard = grid.yardCells;
        Place(shore.x - yard.x, shore.y - yard.y);
        foreach (string folder in Replay.Split(','))
        {
            var change = RecordedSpell(folder, out string id);
            Check($"record_has_a_spell_{Path.GetFileName(folder)}", change != null, folder);
            if (change == null) continue;
            const string mod = "replay-cast";
            switcher.Ask(RoomCommand.Apply(switcher.Me, mod, change["patch"].ToString(Formatting.None), (string)change["title"]));
            for (float t = 0f; t < MostReset && !switcher.Room.Mods.Exists(m => m.Id == mod); t += Time.unscaledDeltaTime) yield return null;
            yield return Wait(SettleSeconds);
            var outcome = new JObject { ["record"] = change["record"], ["spell"] = id, ["goesOn"] = switcher.Room.Mods.Exists(m => m.Id == mod) };
            yield return CastOne(id, shore, outcome);
            File.AppendAllText(Path.Combine(folder, CastsFile), outcome.ToString(Formatting.None) + "\n");
            yield return Fresh();
        }
    }

    IEnumerator CastOne(string id, Vector2Int shore, JObject outcome)
    {
        var spell = World.Rules.Spell(id);
        outcome["selected"] = caster.Select(id);
        outcome["onTheWheel"] = spell != null && spell.Radial;
        outcome["inTheList"] = SpellList.From(World.Rules).Any(t => t.Name != SpellList.AllName && t.Name != SpellList.OffWheelName && t.SpellIds.Contains(id));
        if (spell == null || !OpenWater(spell, shore, out var at)) yield break;
        var cells = World.SpellCells(spell, at.x, at.y, CastDir);
        int mark = ConsoleMark;
        puppet.Face(grid.CellCenter(at.x, at.y));
        caster.Cast(caster.SelectedId, at.x, at.y, CastDir);
        yield return Wait(CastLands);
        outcome["cells"] = cells.Count;
        outcome["logged"] = ConsoleSince(mark).Any(l => l.Contains("You cast", StringComparison.Ordinal));
        outcome["frozen"] = cells.Count(c => World.At(c.x, c.y).Frozen);
    }

    // Wet cells a cast can show on, none frozen yet: an earlier cast may have left ice nearby.
    bool OpenWater(SpellDef spell, Vector2Int shore, out Vector2Int at)
    {
        for (int y = shore.y + 2; y < shore.y + WaterSearch; y += WaterStep)
            for (int x = shore.x - WaterSearch; x < shore.x + WaterSearch; x += WaterStep)
            {
                var cells = World.SpellCells(spell, x, y, CastDir);
                if (cells.Count == 0 || !cells.All(c => World.At(c.x, c.y).Water > 0 && !World.At(c.x, c.y).Frozen)) continue;
                at = new Vector2Int(x, y);
                return true;
            }
        at = default;
        return false;
    }

    // The spell prompt's last patch in a run's folder, and the spell it adds.
    static JObject RecordedSpell(string folder, out string id)
    {
        id = null;
        var line = File.ReadAllLines(Path.Combine(folder, "prompts.jsonl")).Select(JObject.Parse).FirstOrDefault(p => ((string)p["name"]).EndsWith("-spell", StringComparison.Ordinal));
        string recordId = (string)line?["record"];
        var record = Directory.GetFiles(folder, "chat-log-*.jsonl").SelectMany(File.ReadAllLines).Select(JObject.Parse).FirstOrDefault(r => (string)r["id"] == recordId);
        var change = (record?["changes"] as JArray)?.OfType<JObject>().LastOrDefault(c => c["patch"]?["spells"] is JArray);
        if (change == null) return null;
        id = (string)change["patch"]["spells"].Last["id"];
        change["record"] = recordId;
        return change;
    }

    IEnumerator Burn(string kind)
    {
        yield return CastFromBar(SpellIds.FireBolt, FireAtX, (int)FireAtY, FireDir, ReplayBurn);
        var cell = grid.YardCell(FireAtX, (int)FireAtY);
        yield return Look(kind, cell.x, cell.y);
    }
}
