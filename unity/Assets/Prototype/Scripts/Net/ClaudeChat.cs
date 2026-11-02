using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using Chem.Mcp;
using Debug = UnityEngine.Debug;

// The in-game chat's messages to the player's own Claude Code (J-060): one headless process for
// each, in the folder the F9 terminal uses, with nothing kept between them but notes.md. Its
// output is read on a thread of its own, so a slow answer never holds a frame, and comes back
// through Take on the main thread. The game holds no credential: whoever is signed in to
// claude on this machine is who answers, and if nobody is, the player signs in themselves.
public sealed class ClaudeChat
{
    /// <summary>How long one message may run before it's stopped.</summary>
    const int MostSeconds = 240;
    const int StopWaitMs = 2000;

    /// <summary>What the chat shows next: words, a tool in use, the end, or a failure with its reason.</summary>
    readonly ConcurrentQueue<(ClaudeSaid said, string text)> heard = new();
    Process running;
    string lastError = "";

    /// <summary>A program to run instead of claude, for the probe's stand-in; empty, for no claude at all.</summary>
    public static string StandIn { get; set; }

    /// <summary>Whether a message is still being answered.</summary>
    public bool Busy => running != null;

    /// <summary>Where claude is, or null if it isn't installed.</summary>
    public static string FindClaude() =>
        StandIn == "" ? null : StandIn ?? ClaudeRun.Find(Environment.GetEnvironmentVariable("PATH"), Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), File.Exists);

    /// <summary>The next thing heard, on the main thread; false when there's nothing yet.</summary>
    public bool Take(out (ClaudeSaid said, string text) line)
    {
        if (!heard.TryDequeue(out line)) return false;
        if (line.said is ClaudeSaid.Done or ClaudeSaid.Failed) running = null;
        return true;
    }

    /// <summary>Starts one process for this message, in a folder ready for it. False, with the reason heard, if it can't.</summary>
    public bool Send(string message, string folder, string url)
    {
        if (Busy) return false;
        string claude = FindClaude();
        if (claude == null)
        {
            heard.Enqueue((ClaudeSaid.Failed, ClaudeTerminal.NotInstalled));
            return false;
        }
        ClaudeTerminal.WriteFolder(folder, url);
        TrimNotes(folder);
        var process = new Process { StartInfo = StartOf(claude, folder), EnableRaisingEvents = true };
        try
        {
            process.Start();
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Debug.LogWarning("Claude chat: " + e.Message);
            heard.Enqueue((ClaudeSaid.Failed, "Claude wouldn't start. The player log says why."));
            return false;
        }
        running = process;
        var input = Encoding.UTF8.GetBytes(message);
        new Thread(() => Follow(process, input)) { IsBackground = true, Name = "Claude chat" }.Start();
        new Thread(() => lastError = process.StandardError.ReadToEnd()) { IsBackground = true, Name = "Claude chat errors" }.Start();
        return true;
    }

    static ProcessStartInfo StartOf(string claude, string folder)
    {
        string arguments = ClaudeRun.CommandLine(ClaudeRun.Arguments());
        bool script = claude.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase);
        return new ProcessStartInfo(script ? "cmd.exe" : claude, script ? $"/c \"\"{claude}\" {arguments}\"" : arguments)
        {
            WorkingDirectory = folder,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
        };
    }

    static void TrimNotes(string folder)
    {
        string notes = Path.Combine(folder, ClaudeRun.NotesFile);
        if (File.Exists(notes)) File.WriteAllText(notes, ClaudeRun.Trimmed(File.ReadAllText(notes)));
    }

    // On its own thread: the message in, each line out, and a watchdog so one that never ends can't hold the chat.
    void Follow(Process process, byte[] input)
    {
        using var watchdog = new Timer(_ => Stop(process), null, MostSeconds * 1000, Timeout.Infinite);
        bool ended = false;
        try
        {
            process.StandardInput.BaseStream.Write(input, 0, input.Length);
            process.StandardInput.Close();
            string line;
            while ((line = process.StandardOutput.ReadLine()) != null)
                foreach (var said in ClaudeRun.Read(line))
                {
                    heard.Enqueue(said);
                    ended |= said.Item1 is ClaudeSaid.Done or ClaudeSaid.Failed;
                }
            process.WaitForExit(StopWaitMs);
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or ObjectDisposedException)
        {
            lastError = e.Message;
        }
        if (!ended) heard.Enqueue((ClaudeSaid.Failed, string.IsNullOrWhiteSpace(lastError) ? "Claude stopped before it answered." : lastError.Trim()));
    }

    static void Stop(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill();
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Debug.Log("Claude chat: it had already stopped");
        }
    }

    /// <summary>Stops a message in progress: the player asked, or the game is closing.</summary>
    public void Cancel()
    {
        if (running != null) Stop(running);
    }
}
