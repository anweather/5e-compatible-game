using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Chem.Mcp;

// One check of a Claude Code install, off the main thread (J-071): the path run with --version and the player's
// variables, and a plain word on whether it's there. A path that answers is kept; one that doesn't never is.
public sealed class ClaudeCheck
{
    const int MostMs = 15000, MostVersion = 60;
    const string Missing = "Couldn't find Claude Code there. Check the path, or install it from claude.com/claude-code.",
        Failing = "That program started but didn't answer as Claude Code does.";

    string path, says;
    bool found;

    /// <summary>Whether it's still waiting on the program.</summary>
    public bool Checking { get; private set; }

    /// <summary>Starts on this path. Returns at once.</summary>
    public void Start(string given)
    {
        if (Checking) return;
        Checking = true;
        path = given;
        string program = ClaudeChat.Locate(given);
        var environment = new Dictionary<string, string>();
        ChatSetup.Hand(environment);
        var secrets = ChatSetup.Secrets;
        new Thread(() => Run(program, environment, secrets)) { IsBackground = true, Name = "Claude Code check" }.Start();
    }

    /// <summary>On the main thread: what to tell the player once it's known, a path that answered being kept. Null until then.</summary>
    public string Take()
    {
        string result = Volatile.Read(ref says);
        if (result == null) return null;
        says = null;
        Checking = false;
        if (found) ChatSetup.SetPath(path);
        return result;
    }

    /// <summary>Whether the last check found it.</summary>
    public bool Found => found;

    void Run(string program, Dictionary<string, string> environment, string[] secrets)
    {
        found = false;
        Volatile.Write(ref says, program == null ? Missing : Asked(program, environment, secrets));
    }

    string Asked(string program, Dictionary<string, string> environment, string[] secrets)
    {
        try
        {
            var start = ClaudeChat.Starting(program, Path.GetTempPath(), "--version");
            foreach (var variable in environment) start.Environment[variable.Key] = variable.Value;
            using var process = System.Diagnostics.Process.Start(start);
            process.StandardInput.Close();
            var reading = process.StandardOutput.ReadLineAsync();
            if (!process.WaitForExit(MostMs))
            {
                process.Kill();
                return Failing;
            }
            string version = reading.Result;
            if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(version)) return Failing;
            found = true;
            string shown = ClaudeRun.Without(version.Trim(), secrets);
            return "Found it: " + (shown.Length > MostVersion ? shown.Substring(0, MostVersion) : shown);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            return Missing;
        }
    }
}
