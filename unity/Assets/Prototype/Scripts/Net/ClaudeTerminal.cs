using System;
using System.Collections;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Chem.Mcp;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using Debug = UnityEngine.Debug;

// A real terminal beside the game, for the player's own Claude (J-056; the chat box, GameChat, works in the same folder): F9, or the Play with
// Claude page, opens one in a folder of the game's where a .mcp.json points at this game's
// rules MCP, so typing claude there is all it takes. The game never starts claude and holds no
// credentials: the player signs in to their own. The game stays drawing and running behind it
// (Run In Background, borderless: FullScreenStart), so they can watch the world change.
// Windows only for now; elsewhere it says so in the log.
public class ClaudeTerminal : MonoBehaviour
{
    const float StartWait = 12f;
    const string ServerName = ClaudeRun.ServerName, WelcomeFile = "welcome.txt", StartFile = "open.cmd", SignInFile = "sign-in.cmd";

    public const string NotInstalled = "Claude Code isn't installed. Get it from claude.com/claude-code, then ask again.";

    public const string TalkLine = ClaudeRun.TalkLine;

    const string Welcome = "Spell Chemistry Valley\r\n\r\nType claude and press Enter to start, then ask it to change the game:\r\n" +
        "  make all fire blue\r\n  when lightning hits water, leave fog\r\n  make the fire near me green\r\n\r\n" + TalkLine + "\r\n\r\n";

    const string Guide = ClaudeRun.Guide;

    static string Folder => ChemGrid.Folders.Claude.Path;

    IEnumerator Start()
    {
        if (!Environment.GetCommandLineArgs().Contains("-open-terminal")) yield break;
        yield return new WaitForSecondsRealtime(StartWait);
        Debug.Log("Claude terminal: " + Open());
    }

    void Update()
    {
        if (Keyboard.current != null && Keyboard.current.f9Key.wasPressedThisFrame) Open();
    }

    public static string Open()
    {
        var server = RulesMcpServer.Instance;
        if (server == null) return "This copy of the game doesn't serve Claude.";
        if (!server.Serving || RulesMcpServer.Url == null) server.Serving = true;
        if (RulesMcpServer.Url == null) return "No free port for Claude to reach the game.";
        WriteFolder(Folder, RulesMcpServer.Url);
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        string opened = Launch(Folder, StartFile);
        Debug.Log($"Claude terminal: {opened} in {Folder}");
        return $"Opened {opened}. Type claude there.";
#else
        Debug.Log($"Claude terminal: Windows only for now; the folder is ready at {Folder}");
        return $"Windows only for now. The folder is ready: {Folder}";
#endif
    }

    public static void WriteFolder(string folder, string url)
    {
        Directory.CreateDirectory(folder);
        var config = new JObject
        {
            ["mcpServers"] = new JObject { [ServerName] = new JObject { ["type"] = "http", ["url"] = url } },
        };
        File.WriteAllText(Path.Combine(folder, ".mcp.json"), config.ToString());
        File.WriteAllText(Path.Combine(folder, "CLAUDE.md"), Guide);
        File.WriteAllText(Path.Combine(folder, WelcomeFile), Welcome);
        File.WriteAllText(Path.Combine(folder, StartFile), PathLine() + "@type " + WelcomeFile + "\r\n");
        File.WriteAllText(Path.Combine(folder, SignInFile), PathLine() + $"@{SignInProgram()} auth login\r\n");
    }

    static string PathLine()
    {
        string claude = ClaudeChat.FindClaude();
        return claude == null ? "" : $"@set \"PATH={Path.GetDirectoryName(claude)};%PATH%\"\r\n";
    }

    static string SignInProgram()
    {
        string claude = ClaudeChat.FindClaude();
        return claude == null ? "claude" : $"\"{claude}\"";
    }

    public static void OpenSignIn()
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        if (ClaudeChat.StandIn == null && ChemGrid.Folders.Claude.Test == null) Launch(Folder, SignInFile);
#endif
    }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
    static string Launch(string folder, string script)
    {
        if (ChatSetup.Secrets.Length > 0)
        {
            var start = new ProcessStartInfo("cmd.exe", $"/k .\\{script}") { WorkingDirectory = folder, UseShellExecute = false };
            ChatSetup.Hand(start.Environment);
            Process.Start(start);
            return "a command prompt";
        }
        try
        {
            Process.Start(new ProcessStartInfo("wt.exe", $"-d \"{folder}\" cmd /k .\\{script}") { UseShellExecute = true });
            return "Windows Terminal";
        }
        catch (Win32Exception)
        {
            Process.Start(new ProcessStartInfo("cmd.exe", $"/k .\\{script}") { WorkingDirectory = folder, UseShellExecute = true });
            return "a command prompt";
        }
    }
#endif
}
