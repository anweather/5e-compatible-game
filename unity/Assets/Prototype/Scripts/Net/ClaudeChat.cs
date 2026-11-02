using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Chem.Mcp;
using Debug = UnityEngine.Debug;

// The in-game chat's messages to the player's own Claude Code (J-060): one headless process for
// each, in the folder the F9 terminal uses, with nothing kept between them but notes.md, whose
// text goes ahead of the message. Its output is read on a thread of its own, so a slow answer
// never holds a frame, and comes back through Take on the main thread. The game holds no credential:
// whoever is signed in to claude on this machine is who answers, and if nobody is, the player signs
// in themselves. An account that can't use the chat's model gets one retry on its own default (#64).
public sealed class ClaudeChat : IChatHarness
{
    /// <summary>How long one message may run before it's stopped.</summary>
    const int MostSeconds = 240;
    const int StopWaitMs = 2000;
    const string WouldntStart = "Claude wouldn't start. The player log says why.";

    /// <summary>What the chat shows next: words, a tool in use, the end, or a failure with its reason.</summary>
    readonly ConcurrentQueue<(ClaudeSaid said, string text)> heard = new();
    Process running;
    string lastError = "";
    // The model this account refused: this session asks for no model after that, so no message has to fail first.
    string refusedModel;
    volatile bool timedOut, stopped;

    /// <summary>A program to run instead of claude, for the probe's stand-in; empty, for no claude at all.</summary>
    public static string StandIn { get; set; }

    /// <summary>Told each raw line and its clock, on the reading thread: the real-prompt probe keeps the stream, to check the record against. Null in play.</summary>
    public static Action<string, long> Tap { get; set; }

    /// <summary>More arguments for claude, for the real-prompt probe's comparisons (a model, less context). Null in play.</summary>
    public static IReadOnlyList<string> Extra { get; set; }

    /// <summary>A model for the real-prompt probe's comparisons, in place of the chat's own. Null in play.</summary>
    public static string Model { get; set; }

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
    public bool Send(string message, string folder, string url, ChatRecorder recorder, Func<long> clock)
    {
        if (Busy) return false;
        timedOut = stopped = false;
        string claude = FindClaude();
        if (claude == null)
        {
            recorder.Ended(clock(), ChatOutcome.Failed);
            heard.Enqueue((ClaudeSaid.Failed, ClaudeTerminal.NotInstalled));
            return false;
        }
        ClaudeTerminal.WriteFolder(folder, url);
        TrimNotes(folder);
        string model = Model ?? (GameChat.AccountModel ? null : ClaudeRun.ChatModel);
        if (model == refusedModel) model = null;
        var process = Started(claude, folder, model);
        if (process == null)
        {
            recorder.Ended(clock(), ChatOutcome.Failed);
            heard.Enqueue((ClaudeSaid.Failed, WouldntStart));
            return false;
        }
        recorder.Started(clock());
        running = process;
        var input = Encoding.UTF8.GetBytes(ClaudeRun.Message(ClaudeRun.Notes(folder), message));
        new Thread(() => Follow(process, claude, folder, model, input, recorder, clock)) { IsBackground = true, Name = "Claude chat" }.Start();
        return true;
    }

    // One process, reading its errors; null if it wouldn't start.
    Process Started(string claude, string folder, string model)
    {
        var process = new Process { StartInfo = StartOf(claude, folder, model), EnableRaisingEvents = true };
        try
        {
            process.Start();
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Debug.LogWarning("Claude chat: " + e.Message);
            return null;
        }
        new Thread(() => lastError = process.StandardError.ReadToEnd()) { IsBackground = true, Name = "Claude chat errors" }.Start();
        return process;
    }

    static ProcessStartInfo StartOf(string claude, string folder, string model)
    {
        string arguments = ClaudeRun.CommandLine(ClaudeRun.Arguments(model).Concat(Extra ?? Array.Empty<string>()));
        bool script = claude.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase);
        var start = new ProcessStartInfo(script ? "cmd.exe" : claude, script ? $"/c \"\"{claude}\" {arguments}\"" : arguments)
        {
            WorkingDirectory = folder,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
        };
        ClaudeRun.Scrub(start.Environment);
        return start;
    }

    static void TrimNotes(string folder)
    {
        string notes = Path.Combine(folder, ClaudeRun.NotesFile);
        if (File.Exists(notes)) File.WriteAllText(notes, ClaudeRun.Trimmed(File.ReadAllText(notes)));
    }

    // On its own thread: the message's whole life, with one more go if the account refused the model.
    void Follow(Process process, string claude, string folder, string model, byte[] input, ChatRecorder recorder, Func<long> clock)
    {
        string failure = Attempt(process, input, recorder, clock);
        if (failure != null && !timedOut && !stopped && ClaudeRun.RetriesOnTheAccount(model, failure, recorder.Progress()))
        {
            refusedModel = model;
            heard.Enqueue((ClaudeSaid.Notice, ClaudeRun.ModelFallback));
            recorder.Retried(clock(), model, failure);
            model = null;
            lastError = "";
            var second = Started(claude, folder, model);
            if (second == null) failure = WouldntStart;
            else
            {
                running = second;
                if (stopped) Stop(second); // a Cancel that came between the two
                failure = Attempt(second, input, recorder, clock);
            }
        }
        ChatOutcome? how = null;
        if (stopped) how = ChatOutcome.Stopped;
        if (timedOut) how = ChatOutcome.TimedOut;
        recorder.Ended(clock(), how);
        if (failure != null) heard.Enqueue((ClaudeSaid.Failed, ClaudeRun.Pointed(failure, how == null ? model : null)));
    }

    // One process: the message in, each line out, and a watchdog so one that never ends can't hold the chat. A failure comes back
    // rather than going to the player, as it may yet be retried; null if the process said it was done.
    string Attempt(Process process, byte[] input, ChatRecorder recorder, Func<long> clock)
    {
        using var watchdog = new Timer(_ =>
        {
            timedOut = true;
            Stop(process);
        }, null, MostSeconds * 1000, Timeout.Infinite);
        bool done = false;
        string failure = null;
        try
        {
            process.StandardInput.BaseStream.Write(input, 0, input.Length);
            process.StandardInput.Close();
            string line;
            while ((line = process.StandardOutput.ReadLine()) != null)
            {
                long at = clock();
                Tap?.Invoke(line, at);
                recorder.Line(line, at);
                foreach (var said in ClaudeRun.Read(line))
                {
                    if (said.said == ClaudeSaid.Failed)
                    {
                        failure = said.text;
                        continue;
                    }
                    heard.Enqueue(said);
                    done |= said.said == ClaudeSaid.Done;
                }
            }
            process.WaitForExit(StopWaitMs);
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or ObjectDisposedException)
        {
            lastError = e.Message;
        }
        if (done) return null;
        return failure ?? (string.IsNullOrWhiteSpace(lastError) ? "Claude stopped before it answered." : lastError.Trim());
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
        if (running == null) return;
        stopped = true;
        Stop(running);
    }
}
