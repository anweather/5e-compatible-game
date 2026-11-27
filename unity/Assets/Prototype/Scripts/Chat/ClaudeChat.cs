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
// in themselves. An account that can't use the chat's model is told so: there's never a second run (#64).
public sealed class ClaudeChat : IChatHarness
{
    const int MostSeconds = 240;
    const int StopWaitMs = 2000;

    readonly ConcurrentQueue<(ClaudeSaid said, string text)> heard = new();
    Process running;
    string lastError = "";
    volatile bool timedOut, stopped;

    public static string StandIn { get; set; }

    public static Action<string, long> Tap { get; set; }

    public static IReadOnlyList<string> Extra { get; set; }

    public static string Model { get; set; }

    public static bool NotesOff { get; set; }

    [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        StandIn = null;
        Tap = null;
        Extra = null;
        Model = null;
        NotesOff = false;
    }

    public bool Busy => running != null;

    public static string FindClaude() =>
        StandIn == "" ? null : StandIn ?? Locate(ChatSetup.ProgramPath);

    public static string Locate(string path) =>
        ClaudeRun.Resolve(path, Environment.GetEnvironmentVariable("PATH"), Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), File.Exists);

    public bool Take(out (ClaudeSaid said, string text) line)
    {
        if (!heard.TryDequeue(out line)) return false;
        if (line.said is ClaudeSaid.Done or ClaudeSaid.Failed) running = null;
        return true;
    }

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
        var process = new Process { StartInfo = StartOf(claude, folder, model), EnableRaisingEvents = true };
        try
        {
            process.Start();
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Debug.LogWarning("Claude chat: " + e.Message);
            recorder.Ended(clock(), ChatOutcome.Failed);
            heard.Enqueue((ClaudeSaid.Failed, "Claude wouldn't start. The player log says why."));
            return false;
        }
        new Thread(() => lastError = process.StandardError.ReadToEnd()) { IsBackground = true, Name = "Claude chat errors" }.Start();
        recorder.Started(clock());
        running = process;
        var input = Encoding.UTF8.GetBytes(ClaudeRun.Message(NotesOff ? "" : ClaudeRun.Notes(folder), message));
        new Thread(() => Follow(process, model, input, recorder, clock)) { IsBackground = true, Name = "Claude chat" }.Start();
        return true;
    }

    static ProcessStartInfo StartOf(string claude, string folder, string model)
    {
        var start = Starting(claude, folder, ClaudeRun.CommandLine(ClaudeRun.Arguments(model).Concat(Extra ?? Array.Empty<string>())));
        ChatSetup.Hand(start.Environment);
        secrets = ChatSetup.Secrets;
        return start;
    }

    public static ProcessStartInfo Starting(string claude, string folder, string arguments)
    {
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

    void Follow(Process process, string model, byte[] input, ChatRecorder recorder, Func<long> clock)
    {
        string failure = Hear(process, input, recorder, clock);
        ChatOutcome? how = null;
        if (stopped) how = ChatOutcome.Stopped;
        if (timedOut) how = ChatOutcome.TimedOut;
        string asked = how == null ? model : null;
        var (told, modelRefused) = ClaudeRun.Explain(failure, asked);
        if (modelRefused) recorder.ModelUnavailable(asked);
        recorder.Ended(clock(), how);
        if (failure != null) heard.Enqueue((ClaudeSaid.Failed, told));
    }

    static string[] secrets = Array.Empty<string>();

    string Hear(Process process, byte[] input, ChatRecorder recorder, Func<long> clock)
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
                line = ClaudeRun.Without(line, secrets);
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
            lastError = ClaudeRun.Without(e.Message, secrets);
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

    public void Cancel()
    {
        if (running == null) return;
        stopped = true;
        Stop(running);
    }
}
