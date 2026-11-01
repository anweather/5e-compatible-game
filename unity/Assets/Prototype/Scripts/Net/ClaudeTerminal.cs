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
    const string FolderName = "claude-terminal", ServerName = ClaudeRun.ServerName, WelcomeFile = "welcome.txt", StartFile = "open.cmd", SignInFile = "sign-in.cmd";

    /// <summary>The chat's line when there's no claude on this machine.</summary>
    public const string NotInstalled = "Claude Code isn't installed. Get it from claude.com/claude-code, then ask again.";

    /// <summary>The line that says speech to text is there, on the terminal's first screen and in its CLAUDE.md.</summary>
    public const string TalkLine = "Press Win+H to talk instead of type.";

    const string Welcome = "Spell Chemistry Valley\r\n\r\nType claude and press Enter to start, then ask it to change the game:\r\n" +
        "  make all fire blue\r\n  when lightning hits water, leave fog\r\n  make the fire near me green\r\n\r\n" + TalkLine + "\r\n\r\n";

    const string Guide = "# Spell Chemistry Valley\n\n" +
        "You're in a terminal beside a running game: a 3D RPG on the open 5E rules, built on physics-based interactions between " +
        "spells. Its rules are data, and the `spell-valley` tools here change them in the game the player is watching, with no restart.\n\n" +
        "Use the tools. Ask `here` where the player is and `list` or `describe` what's in the rules, make a draft (`set_look`, " +
        "`set_spell`, `set_material`, `add_reaction`, `tune`), `validate` it, then `apply` it with a short title the players will read. " +
        "The tools' own instructions say more. If the tools don't connect, the game has closed or Claude is off on its Play with Claude page.\n\n" +
        "Keep your answer to a line or two: the player is in the middle of a game.\n\n" +
        "## Notes\n\n" +
        "Each message starts a new process that remembers nothing, so read `notes.md` here first. When you've finished, add a line or two " +
        "there saying what you changed and what's on now. The game keeps only its newest lines.\n\n" +
        "## What's outside the rules\n\n" +
        "These are in the game's code, not its rules, so no tool changes them yet. Say so in a line, offer what you can do instead, and " +
        "don't look for source: there is none here.\n\n" +
        "- How hot metal glows, the player's size, the size of animals\n" +
        "- Slippery ground (only covers can be slippery), the cover weather leaves\n" +
        "- Houses and other buildings; filling an area bigger than a spell of radius 6\n" +
        "- White or grey fire, steam and smoke: these only change hue (`set_look` says so)\n" +
        "- Telling the time or the weather directly: moving `day.startHour` is a workaround, so say that it is\n\n" +
        "What to try: \"make all fire blue\", \"make the fire near me green\", \"each player a different fire colour\", " +
        "\"make grass burn twice as fast\", \"when lightning hits water, leave fog\".\n\n" + TalkLine + "\n";

    /// <summary>The folder a terminal opens in.</summary>
    public static string Folder => Path.Combine(Application.persistentDataPath, FolderName);

    // -open-terminal opens one by itself once the game is up, to test a player with nobody at its keys.
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

    /// <summary>
    /// Turns the rules MCP on if it's off, writes the folder and opens a terminal there. Says what happened,
    /// for the Play with Claude page to show.
    /// </summary>
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

    /// <summary>The folder's files: the MCP's address, what the game is, and the terminal's first screen. Nothing secret.</summary>
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
        File.WriteAllText(Path.Combine(folder, SignInFile), PathLine() + "@claude auth login\r\n");
    }

    // claude's own folder for this one window, as its installer may have put it where a game started from the desktop can't see.
    static string PathLine()
    {
        string claude = ClaudeChat.FindClaude();
        return claude == null ? "" : $"@set \"PATH={Path.GetDirectoryName(claude)};%PATH%\"\r\n";
    }

    /// <summary>Opens a terminal on Anthropic's own sign-in: the game never sees it. The player's chat says why.</summary>
    public static void OpenSignIn()
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        if (ClaudeChat.StandIn == null) Launch(Folder, SignInFile);
#endif
    }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
    // Windows Terminal if it's there, else a plain command prompt.
    static string Launch(string folder, string script)
    {
        try
        {
            Process.Start(new ProcessStartInfo("wt.exe", $"-d \"{folder}\" cmd /k {script}") { UseShellExecute = true });
            return "Windows Terminal";
        }
        catch (Win32Exception)
        {
            Process.Start(new ProcessStartInfo("cmd.exe", $"/k {script}") { WorkingDirectory = folder, UseShellExecute = true });
            return "a command prompt";
        }
    }
#endif
}
