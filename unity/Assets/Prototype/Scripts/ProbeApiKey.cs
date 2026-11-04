using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Chem.Mcp;
using UnityEngine;

// The probe's apikey scenario (J-071): a player's own key from the menu's check to a chat message, against ApiStandIn, so
// no real key and no real call. With no key the chat stays Claude Code's; a good key turns it over to ApiChat; a message
// runs through the real server and the loop; then the key's text is looked for everywhere it mustn't be.
public class ApiKeyProbe : ProbeScenario
{
    const float MostSeconds = 30f;

    readonly List<string> logged = new();
    string key;

    void Heard(string message, string stack, LogType type) => logged.Add(message + stack);

    protected override IEnumerator Run()
    {
        var chat = GameChat.Instance;
        Check("the_chat_is_here", chat != null);
        if (chat == null) yield break;
        string folder = Path.Combine(Path.GetTempPath(), "apikey-probe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        key = ApiStandIn.GoodKey + "-" + Guid.NewGuid().ToString("N");
        var listerWas = ApiKeyCheck.Lister;
        var wireWas = GameChat.ApiWireMaker;
        ApiKey.TestFile = Path.Combine(folder, "api-key.dat");
        ChatSetup.TestFile = Path.Combine(folder, "chat-setup.dat");
        ChatSetup.Reload();
        ChemGrid.Folders.Claude.Test = Path.Combine(folder, "claude-terminal");
        ChatTelemetry.Folder = Path.Combine(folder, "records");
        ApiKeyCheck.Lister = () => new ApiStandIn();
        GameChat.ApiWireMaker = () => new ApiStandIn();
        Application.logMessageReceived += Heard;
        try
        {
            Check("no_key_stays_on_claude_code", !ApiKey.InPlay && chat.ChosenHarness is ClaudeChat);
            var check = new ApiKeyCheck();
            check.Start(key);
            float from = Time.realtimeSinceStartup;
            Check("the_check_doesnt_block", check.Checking, "Start returned while it was still checking");
            KeyCheck result = null;
            while (result == null && Time.realtimeSinceStartup - from < MostSeconds)
            {
                yield return null;
                result = check.Take();
            }
            Check("a_good_key_is_taken", result?.Answer == KeyAnswer.Good && ApiKey.Saved, result?.Says);
            Check("the_chat_turns_to_the_api", chat.ChosenHarness is ApiChat);
            yield return ShowSetup("api");
            chat.Send("make fire blue");
            yield return new WaitUntil(() => !chat.Busy || Time.realtimeSinceStartup - from > MostSeconds);
            yield return Wait(0.5f);
            var said = chat.Lines.ToList();
            Check("the_loop_answers_in_the_chat", chat.Harness is ApiChat && said.Contains("Claude: Done."), string.Join(" | ", said));
            CheckNowhere(folder);
            float capWas = ApiKey.Cap;
            ApiKey.Session.Limit = ApiKey.Session.Spent / 2;
            chat.Send("make fire green");
            yield return new WaitUntil(() => !chat.Busy || Time.realtimeSinceStartup - from > MostSeconds);
            yield return Wait(0.5f);
            said = chat.Lines.ToList();
            Check("the_cap_stops_the_chat_and_says_why", said.Any(l => l.Contains("spend limit")) && said.Count(l => l == "Claude: Done.") == 1, string.Join(" | ", said));
            ApiKey.Session.Limit = capWas;
            ApiKey.Remove();
            Check("removed_goes_back_to_claude_code", !ApiKey.InPlay && chat.ChosenHarness is ClaudeChat);
            yield return ShowSetup("claude-code");
        }
        finally
        {
            Application.logMessageReceived -= Heard;
            ApiKey.Remove();
            ApiKey.TestFile = null;
            ChatSetup.TestFile = null;
            ChatSetup.Reload();
            ApiKeyCheck.Lister = listerWas;
            GameChat.ApiWireMaker = wireWas;
            ChemGrid.Folders.Claude.Test = null;
            ChatTelemetry.Folder = null;
        }
    }

    // The Settings page drawn for real, so a layout that throws shows in the console's check.
    IEnumerator ShowSetup(string label)
    {
        int drawn = ClaudeSetupSettings.Drawn;
        PauseMenu.Instance.ShowPage(ClaudeSetupSettings.PageId);
        yield return Wait(0.4f);
        Check("the_page_draws_" + label, ClaudeSetupSettings.Drawn > drawn, $"{ClaudeSetupSettings.Drawn - drawn} draws");
        Shot(label);
        PauseMenu.Instance.SetOpen(false);
        yield return null;
    }

    // Everything a message leaves behind, the console, the process's environment and a save.
    void CheckNowhere(string folder)
    {
        var files = Directory.GetFiles(folder, "*", SearchOption.AllDirectories).Where(f => f != ApiKey.TestFile).ToList();
        var holding = files.Where(f => File.ReadAllText(f).Contains(key)).ToList();
        Check("records_written", files.Count > 0, $"{files.Count} files");
        Check("no_record_holds_the_key", holding.Count == 0, string.Join(", ", holding.Select(Path.GetFileName)));
        Check("the_console_never_saw_it", !logged.Any(l => l.Contains(key)), $"{logged.Count} lines");
        var environment = Environment.GetEnvironmentVariables();
        Check("no_environment_holds_it", !environment.Values.Cast<object>().Any(v => v?.ToString().Contains(key) == true) && environment[ClaudeRun.GameKey] == null,
            "a child process inherits this environment");
        Check("no_save_holds_it", !grid.World.Save(new NoChunks()).Contains(key));
    }

    sealed class NoChunks : Chem.IChunkSource
    {
        public string Version => "none";
        public void Make(int x, int y, Chem.Cell cell) { }
    }
}
