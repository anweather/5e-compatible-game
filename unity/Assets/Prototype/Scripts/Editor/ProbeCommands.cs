using System;
using System.IO;
using System.Linq;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEngine;

// The fast loop's probe (docs/FAST_LOOP.md): CLI commands for an agent driving
// the open Editor.
//
//   unity command probe list
//   unity command probe run --name smoke [--shots <folder>] [--seconds 10]
//   unity command probe result --wait true --timeout 300
//
// Entering Play mode reloads the scripting domain (this project keeps domain
// reload on), which drops the CLI's connection, so one request can't span a
// run. `probe run` saves the request in SessionState and enters Play mode;
// after the reload the hook below adds the scenario (ProbeScenarios), waits
// for it, writes its result to Temp/Probe/result.json and leaves Play mode.
// `probe result` reads that file off the main thread, waiting for it if asked.
// tools/probe.sh does all three steps in one call.
public static class ProbeCommands
{
    const string Group = "probe";
    const string RequestKey = "Prototype.Probe.Request";
    const string Starting = "starting", Running = "running", Done = "done", Aborted = "aborted";

    static readonly JsonSerializer Json = JsonSerializer.Create(new JsonSerializerSettings
    {
        ContractResolver = new DefaultContractResolver { NamingStrategy = new SnakeCaseNamingStrategy() },
    });

    // The project folder: the Editor's working directory, readable from any thread.
    static string ResultPath => Path.Combine(Directory.GetCurrentDirectory(), "Temp", "Probe", "result.json");

    sealed class Request
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Shots { get; set; }
        public float Seconds { get; set; }
        public DateTime RequestedUtc { get; set; }
    }

    // The run in progress (Play mode doesn't reload the domain on the way out, so this lasts the run).
    static Request active;
    static ProbeScenario scenario;
    static double startedAt;

    [CliGroupCommand(Group, "List the probe scenarios and what each checks", MainThreadRequired = false)]
    public static object List() => ProbeScenarios.All.Select(e => new
    {
        name = e.Name,
        description = e.Description,
        max_seconds = e.MaxSeconds,
    }).ToList();

    [CliGroupCommand(Group, "Enter Play mode and run a probe scenario in the open scene (the Chemistry Yard). " +
        "Returns at once; read the JSON result with `probe result --wait true`.")]
    public static object Run(
        [CliArg("name", "Scenario name, from `probe list`", Required = true)] string name,
        [CliArg("shots", "Folder to save screenshots in (absolute, e.g. a scratchpad); empty for none")] string shots = "",
        [CliArg("seconds", "perf only: how long to hold the camera, in seconds")] float seconds = 10f)
    {
        var entry = ProbeScenarios.Find(name);
        if (entry == null)
            throw new ArgumentException($"No probe scenario '{name}'. Known: {string.Join(", ", ProbeScenarios.All.Select(e => e.Name))}");
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Already in Play mode: stop it first.");
        if (EditorApplication.isCompiling) throw new InvalidOperationException("Scripts are compiling: try again when they finish.");
        if (UnityEngine.Object.FindFirstObjectByType<ChemGrid>() == null)
            throw new InvalidOperationException("The open scene has no ChemGrid: open Assets/Prototype/Scenes/ChemistryYard.unity.");

        var request = new Request
        {
            Id = Guid.NewGuid().ToString("N").Substring(0, 8),
            Name = entry.Name,
            Shots = string.IsNullOrWhiteSpace(shots) ? null : Path.GetFullPath(shots),
            Seconds = Mathf.Clamp(seconds, 1f, 300f),
            RequestedUtc = DateTime.UtcNow,
        };
        SessionState.SetString(RequestKey, JsonConvert.SerializeObject(request));
        WriteResult(Status(request, Starting));
        EditorApplication.isPlaying = true;
        return new { id = request.Id, scenario = request.Name, state = Starting, next = "unity command probe result --wait true --timeout 300" };
    }

    [CliGroupCommand(Group, "The last probe run's JSON result (Temp/Probe/result.json); with wait=true, waits until it has finished",
        MainThreadRequired = false)]
    public static object Result(
        [CliArg("wait", "Wait until the run is done or aborted")] bool wait = false,
        [CliArg("timeout", "Timeout in seconds")] double timeoutInSeconds = 300)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(Math.Max(1, Math.Min(timeoutInSeconds, 3600) - 2));
        while (true)
        {
            var result = ReadResult();
            string state = (string)result?["state"];
            if (!wait || state == Done || state == Aborted) return result ?? (object)new { state = "none" };
            if (DateTime.UtcNow > deadline)
            {
                result ??= new JObject { ["state"] = "none" };
                result["waited_out"] = true;
                return result;
            }
            Thread.Sleep(250);
        }
    }

    // --- the run, in Play mode -------------------------------------------------------

    [InitializeOnLoadMethod]
    static void Hook() => EditorApplication.playModeStateChanged += OnPlayModeChanged;

    static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode) Begin();
        else if (state == PlayModeStateChange.ExitingPlayMode && active != null)
        {
            // Left Play mode before the scenario finished (stopped by hand, or timed out).
            Finish(Aborted, $"Play mode ended after {EditorApplication.timeSinceStartup - startedAt:0.0} s, before the scenario finished");
        }
    }

    static void Begin()
    {
        string saved = SessionState.GetString(RequestKey, "");
        if (saved == "") return;
        SessionState.EraseString(RequestKey);
        active = JsonConvert.DeserializeObject<Request>(saved);
        var entry = ProbeScenarios.Find(active.Name);
        var go = new GameObject($"Probe ({entry.Name})");
        go.SetActive(false);   // so the settings are in before Awake and Start
        scenario = (ProbeScenario)go.AddComponent(entry.Type);
        scenario.Result.Scenario = entry.Name;
        scenario.ScreenshotDir = active.Shots;
        scenario.Seconds = active.Seconds;
        go.SetActive(true);
        if (active.Shots != null) DemoRecorder.HideGizmos();
        startedAt = EditorApplication.timeSinceStartup;
        WriteResult(Status(active, Running));
        EditorApplication.update += Tick;
    }

    static void Tick()
    {
        if (active == null) { EditorApplication.update -= Tick; return; }
        if (scenario != null && scenario.Done) { Finish(Done, null); return; }
        var entry = ProbeScenarios.Find(active.Name);
        if (scenario == null) Finish(Aborted, "The scenario's GameObject was destroyed");
        else if (EditorApplication.timeSinceStartup - startedAt > entry.MaxSeconds)
            Finish(Aborted, $"Timed out after {entry.MaxSeconds:0} s in Play mode (an exception in the scenario stops it: see console)");
    }

    static void Finish(string state, string reason)
    {
        EditorApplication.update -= Tick;
        var run = active;
        active = null;
        var result = Status(run, state);
        if (scenario != null)
        {
            var found = JObject.FromObject(scenario.Result, Json);
            found.Merge(result);
            result = found;
        }
        if (state != Done) result["pass"] = false;
        if (reason != null) result["reason"] = reason;
        result["wall_seconds"] = Math.Round((DateTime.UtcNow - run.RequestedUtc).TotalSeconds, 1);
        result["unity"] = Application.unityVersion;
        WriteResult(result);
        DemoRecorder.RestoreGizmos();
        scenario = null;
        if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
    }

    // --- the result file -----------------------------------------------------------------

    static JObject Status(Request run, string state) => new JObject
    {
        ["id"] = run.Id,
        ["scenario"] = run.Name,
        ["state"] = state,
    };

    static void WriteResult(JObject result)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ResultPath));
        string temp = ResultPath + ".tmp";
        File.WriteAllText(temp, result.ToString(Formatting.Indented));
        if (File.Exists(ResultPath)) File.Delete(ResultPath);
        File.Move(temp, ResultPath);
    }

    static JObject ReadResult()
    {
        try
        {
            return File.Exists(ResultPath) ? JObject.Parse(File.ReadAllText(ResultPath)) : null;
        }
        catch (IOException) { return null; }
        catch (JsonException) { return null; }
    }
}
