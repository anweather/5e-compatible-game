using System.Collections;
using System.IO;
using System.Linq;
using Chem.Mcp;
using UnityEngine;

// The probe's chat scenario (J-060): the chat box to Claude with a stand-in for claude, a small
// script that writes down how it was started and what came on stdin, then answers in stream-json.
// No real Claude runs here, and nothing here signs in to anything.
public class ChatProbe : ProbeScenario
{
    const string Message = "make all fire blue";
    const string Recording = "make the fireball bigger", RecordedMod = "big-fireball";
    const float MostWrite = 3f, MostWait = 12f, FilmTyping = 1.5f, FilmReading = 3f;

    string folder, logged;

    protected override IEnumerator Run()
    {
        var chat = GameChat.Instance;
        Check("chat_here", chat != null);
        if (chat == null) yield break;
        folder = Path.Combine(Application.temporaryCachePath, "chat-probe");
        Directory.CreateDirectory(folder);
        logged = Path.Combine(folder, "asked.txt");
        ClaudeTerminal.TestFolder = Path.Combine(folder, "claude-terminal"); // an Editor shares its data folder with the player's game
        var controls = caster.controls;
        bool controlsWere = controls != null && controls.enabled;
        if (controls != null) controls.enabled = true;
        try
        {
            chat.SetOpen(true);
            Check("open_takes_the_controls", chat.Open && (controls == null || !controls.enabled));
            Check("open_keeps_m_and_esc_from_the_menu", PauseMenu.KeysTaken);
            chat.SetOpen(false);
            Check("closed_gives_them_back", !chat.Open && (controls == null || controls.enabled));
            yield return Answers(chat);
            yield return Waits(chat);
            yield return SignIn(chat);
            yield return Plain(chat);
            yield return Nothing(chat);
            yield return NotInstalled(chat);
            Notes();
        }
        finally
        {
            ClaudeChat.StandIn = null;
            ClaudeTerminal.TestFolder = null;
            if (controls != null) controls.enabled = controlsWere;
        }
    }

    // A prompt that changes something, as the real path has it: the bar, the stamp, the record and its later line.
    IEnumerator Recorded(GameChat chat)
    {
        string records = Path.Combine(folder, "records");
        ChatTelemetry.Folder = records;
        ClaudeChat.StandIn = StandIn("recorded", 3f,
            "{\"type\":\"system\",\"subtype\":\"init\",\"model\":\"stand-in\",\"claude_code_version\":\"0\"}",
            "{\"type\":\"assistant\",\"message\":{\"id\":\"m1\",\"model\":\"stand-in\",\"content\":[{\"type\":\"tool_use\",\"id\":\"t1\",\"name\":\"mcp__spell-valley__whats_on\",\"input\":{}}]}}",
            "{\"type\":\"result\",\"subtype\":\"success\",\"is_error\":false,\"result\":\"Done.\",\"total_cost_usd\":0.05,\"num_turns\":2,\"duration_ms\":100,\"duration_api_ms\":90,\"usage\":{\"input_tokens\":10,\"cache_creation_input_tokens\":20,\"cache_read_input_tokens\":3000,\"output_tokens\":40}}");
        ClipFilm film = null;
        if (!string.IsNullOrEmpty(ScreenshotDir))
        {
            film = new ClipFilm(this, Path.Combine(ScreenshotDir, "bar"), withHud: true);
            film.Roll();
        }
        chat.Send(Recording);
        yield return null;
        yield return null;
        Check("the_bar_shows_while_it_works", chat.Progress != null, chat.Progress?.what);
        Check("a_save_nobody_asked_for_is_refused", GameChat.SaveRefusal() == ClaudeRun.SaveNotAsked);
        var switcher = grid.GetComponent<RuleSwitcher>();
        switcher.Toggle(RecordedMod);
        yield return Until(chat, () => ChatTelemetry.Instance.Current == null);
        yield return null;
        if (film != null) yield return film.Stop();
        Check("the_bar_goes_with_the_prompt", chat.Progress == null);
        for (float t = 0f; t < MostWrite && !Directory.Exists(records); t += Time.unscaledDeltaTime) yield return null;
        var file = Directory.Exists(records) ? Directory.GetFiles(records, "chat-log-*.jsonl").FirstOrDefault() : null;
        Check("a_record_is_written", file != null, records);
        if (file == null) yield break;
        var record = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllLines(file)[0]);
        Check("it_has_the_prompt_and_source", (string)record["prompt"] == Recording && (string)record["source"] == GameChat.FromKeys, record.ToString());
        Check("it_has_tokens_and_cost", (long?)record["tokens"]?["cacheRead"] == 3000 && record["tokens"]?["cost"] != null, record["tokens"]?.ToString());
        Check("it_is_stamped_in_the_world", record["at"]?["inWorld"]?.Type == Newtonsoft.Json.Linq.JTokenType.Integer, record["at"]?.ToString());
        Check("it_has_the_game_cost", (double?)record["gameCost"]?["worstFrameMs"] > 0, record["gameCost"]?.ToString());
        Check("the_list_shows_it", ChatTelemetry.Instance.Latest.Any(e => e.Prompt == Recording && ChatTelemetry.Line(e).Contains("k tokens")), ChatTelemetry.Instance.Latest.Select(ChatTelemetry.Line).FirstOrDefault());
        switcher.Toggle(RecordedMod);
        yield return Wait(1f);
        Check("a_revert_adds_a_later_line", File.ReadAllLines(file).Any(l => l.Contains("\"of\"") && l.Contains("\"reverted\":true")), File.ReadAllText(file));
        Check("a_terminal_may_save_while_the_chat_is_idle", GameChat.SaveRefusal() == null);
        chat.Send("save that as a mod");
        yield return null;
        Check("a_save_the_message_asked_for_goes_through", chat.Busy && GameChat.SaveRefusal() == null);
        yield return Until(chat, () => ChatTelemetry.Instance.Current == null);
        ChatTelemetry.Folder = null;
    }

    // A script standing in for claude: it notes its arguments and stdin, waits a little, then prints these lines.
    string StandIn(string name, float seconds, params string[] lines) => Script(folder, logged, name, seconds, lines);

    /// <summary>Writes a stand-in for claude into this folder, and says where it is.</summary>
    public static string Script(string folder, string logged, string name, float seconds, params string[] lines)
    {
        string script = Path.Combine(folder, name + ".cmd");
        var body = new System.Text.StringBuilder("@echo off\r\n");
        body.Append($"echo %* > \"{logged}\"\r\n");
        body.Append($"more >> \"{logged}\"\r\n");
        if (seconds > 0f) body.Append($"ping -n {Mathf.CeilToInt(seconds) + 1} 127.0.0.1 > nul\r\n");
        foreach (string line in lines) body.Append("echo ").Append(line.Replace("^", "^^").Replace("&", "^&").Replace("<", "^<").Replace(">", "^>")).Append("\r\n");
        File.WriteAllText(script, body.ToString());
        return script;
    }

    IEnumerator Until(GameChat chat, System.Func<bool> done)
    {
        for (float t = 0f; t < MostWait && !done(); t += Time.unscaledDeltaTime) yield return null;
    }

    IEnumerator Answers(GameChat chat)
    {
        ClaudeChat.StandIn = StandIn("answers", 0f,
            "{\"type\":\"system\",\"subtype\":\"init\"}",
            "{\"type\":\"assistant\",\"message\":{\"content\":[{\"type\":\"text\",\"text\":\"All fire is blue now.\"},{\"type\":\"tool_use\",\"name\":\"mcp__spell-valley__set_look\",\"input\":{}}]}}",
            "{\"type\":\"result\",\"subtype\":\"success\",\"is_error\":false,\"result\":\"All fire is blue now.\"}");
        ClipFilm film = null;
        if (!string.IsNullOrEmpty(ScreenshotDir))
        {
            film = new ClipFilm(this, ScreenshotDir, withHud: true);
            film.Roll();
            yield return Wait(1f);
            chat.SetOpen(true);
            chat.Type(Message);
            yield return Wait(FilmTyping);
            chat.SetOpen(false);
        }
        bool toolsWere = GameChat.ShowTools;
        GameChat.ShowTools = true;
        chat.Send(Message);
        Check("busy_while_it_answers", chat.Busy);
        yield return Until(chat, () => !chat.Busy);
        GameChat.ShowTools = toolsWere;
        if (film != null)
        {
            yield return Wait(FilmReading);
            yield return film.Stop();
        }
        var said = chat.Lines.ToList();
        Check("your_words_show", said.Contains("You: " + Message), string.Join(" | ", said));
        Check("claude_words_show", said.Contains("Claude: All fire is blue now."), string.Join(" | ", said));
        Check("the_tool_shows_behind_the_developer_switch", said.Any(l => l.Contains("set_look")), string.Join(" | ", said));
        Check("one_answer_not_two", said.Count(l => l.Contains("All fire is blue now.")) == 1, string.Join(" | ", said));
        string asked = File.Exists(logged) ? File.ReadAllText(logged) : "";
        Check("the_message_came_on_stdin", asked.Contains(Message), asked);
        Check("headless_with_only_the_games_tools", asked.Contains("-p") && asked.Contains("dontAsk") && asked.Contains("--strict-mcp-config") && asked.Contains("mcp__spell-valley") && !asked.Contains("Bash"), asked);
        Check("no_terminal_window_needed", File.Exists(Path.Combine(ClaudeTerminal.Folder, ".mcp.json")));
    }

    IEnumerator Waits(GameChat chat)
    {
        ClaudeChat.StandIn = StandIn("slow", 2f, "{\"type\":\"result\",\"subtype\":\"success\",\"is_error\":false,\"result\":\"ok\"}");
        chat.Send("first");
        chat.Send("second");
        Check("a_second_message_waits", chat.Queued != null && chat.Queued.Contains("second"), chat.Queued);
        yield return Until(chat, () => !chat.Busy);
        Check("then_it_goes", chat.Lines.Contains("You: second") && chat.Queued == null, string.Join(" | ", chat.Lines));
        Check("the_chat_is_free_again", !chat.Busy);
        yield return Recorded(chat);
    }

    IEnumerator SignIn(GameChat chat)
    {
        ClaudeChat.StandIn = StandIn("signed-out", 0f, "{\"type\":\"result\",\"subtype\":\"success\",\"is_error\":true,\"result\":\"Not logged in. Please run /login\"}");
        chat.Send("hello");
        yield return Until(chat, () => !chat.Busy);
        Check("not_signed_in_says_so", chat.Lines.Last().Contains("aren't signed in"), chat.Lines.Last());
    }

    IEnumerator Plain(GameChat chat)
    {
        ClaudeChat.StandIn = StandIn("fails", 0f, "not json at all");
        chat.Send("hello");
        yield return Until(chat, () => !chat.Busy);
        Check("a_process_with_no_answer_says_so", chat.Lines.Last().Contains("stopped"), chat.Lines.Last());
    }

    IEnumerator Nothing(GameChat chat)
    {
        ClaudeChat.StandIn = Path.Combine(folder, "no-such-program.exe");
        chat.Send("hello");
        yield return Until(chat, () => !chat.Busy);
        Check("it_wont_start_and_says_so", chat.Lines.Last().Contains("wouldn't start"), chat.Lines.Last());
    }

    IEnumerator NotInstalled(GameChat chat)
    {
        ClaudeChat.StandIn = "";
        chat.Send("hello");
        yield return Until(chat, () => chat.Lines.Last().Contains("isn't installed"));
        Check("no_claude_says_where_to_get_it", chat.Lines.Last().Contains("claude.com/claude-code"), chat.Lines.Last());
        Check("no_claude_holds_nothing_up", !chat.Busy);
    }

    void Notes()
    {
        string notes = Path.Combine(ClaudeTerminal.Folder, ClaudeRun.NotesFile);
        string kept = File.Exists(notes) ? File.ReadAllText(notes) : null;
        File.WriteAllText(notes, string.Join("\n", Enumerable.Range(0, ClaudeRun.MostNoteLines * 2)) + "\n");
        File.WriteAllText(notes, ClaudeRun.Trimmed(File.ReadAllText(notes)));
        Check("notes_stay_bounded", File.ReadAllLines(notes).Length == ClaudeRun.MostNoteLines);
        if (kept != null) File.WriteAllText(notes, kept);
        else File.Delete(notes);
        string guide = File.ReadAllText(Path.Combine(ClaudeTerminal.Folder, "CLAUDE.md"));
        Check("guide_says_read_the_notes", guide.Contains("notes.md"));
        Check("guide_lists_whats_outside_the_rules", guide.Contains("outside_rules"));
    }
}
