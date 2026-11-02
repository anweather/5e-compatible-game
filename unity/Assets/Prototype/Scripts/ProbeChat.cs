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
    const float MostWait = 12f, FilmTyping = 1.5f, FilmReading = 3f;

    string folder, logged;

    protected override IEnumerator Run()
    {
        var chat = GameChat.Instance;
        Check("chat_here", chat != null);
        if (chat == null) yield break;
        folder = Path.Combine(Application.temporaryCachePath, "chat-probe");
        Directory.CreateDirectory(folder);
        logged = Path.Combine(folder, "asked.txt");
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
            if (controls != null) controls.enabled = controlsWere;
        }
    }

    // A script standing in for claude: it notes its arguments and stdin, waits a little, then prints these lines.
    string StandIn(string name, float seconds, params string[] lines)
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
        chat.Send(Message);
        Check("busy_while_it_answers", chat.Busy);
        yield return Until(chat, () => !chat.Busy);
        if (film != null)
        {
            yield return Wait(FilmReading);
            yield return film.Stop();
        }
        var said = chat.Lines.ToList();
        Check("your_words_show", said.Contains("You: " + Message), string.Join(" | ", said));
        Check("claude_words_show", said.Contains("Claude: All fire is blue now."), string.Join(" | ", said));
        Check("the_tool_shows", said.Any(l => l.Contains("set_look")), string.Join(" | ", said));
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
        Check("guide_lists_whats_outside_the_rules", guide.Contains("outside the rules") && guide.Contains("the player's size"));
    }
}
