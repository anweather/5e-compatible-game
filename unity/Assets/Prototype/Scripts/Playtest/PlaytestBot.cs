using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Playtest;
using StarterAssets;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

// The playtest bot (playtest/DESIGN.md). Start a player build with -playtest
// and it plays on its own for a while, then writes what it found to its run
// folder and quits; without the flag it does nothing. It picks goals from the
// run's seed (PlaytestPlayer carries them out), feeds the detectors
// (Logic/PlaytestWatches) every frame, takes a screenshot for each goal and
// each new candidate, writes a heartbeat for the runner, and writes run.json.
// Game time is locked to 1/30 s a frame from before the scene loads, frame
// budgets count work rather than milliseconds (ChemGrid.ExactFrames),
// UnityEngine.Random is seeded from the run, and a replay picks its goals
// against the coverage the original started with, so the same seed on the
// same build replays the same run. A sync step in the trace every second (the
// tick and the wizard) shows where two runs part, if they do.
//
// Flags: -playtest-out <dir>, -playtest-seed <n>, -playtest-minutes <n> (or
// -playtest-seconds <n>), -playtest-replay <run.json> (replays its seed),
// -playtest-until-frame <n>, -playtest-coverage <coverage.json>,
// -playtest-goals <key,key,...> (these goals first, in order, then the dice's),
// -playtest-offscreen, -playtest-mute (mutes the listener, which also
// deafens the sound detectors; tools/playtest mutes the player in the
// Windows mixer instead).
public class PlaytestBot : MonoBehaviour
{
    // PerfScenario reads these, to tell the bot's own cost from the game's.
    static readonly ProfilerMarker WatchMarker = new("PlaytestBot.LateUpdate"), ShotMarker = new("PlaytestBot.Shot");

    const int Fps = 30, ShotWidth = 960, ShotHeight = 540, PropEvery = 15, SoundEvery = 30, HeartbeatEvery = 30;

    // Settings, from the command line.
    string outDir;
    float seconds = 480f;
    int untilFrame = int.MaxValue;
    bool offscreen, mute;
    // The window in front when the runner started this player (-playtest-hand-focus), and how often
    // the front was handed back to it.
    long handFocus;
    int handedBack;
    float handUntil;
    const float HandFocusSeconds = 20f;
    Dictionary<string, int> coverage = new();

    // The run.
    readonly RunReport run = new();
    readonly CandidateLog found = new();
    readonly FrameWatch frameWatch = new();
    readonly AudioWatch audioWatch = new();
    readonly PropWatch propWatch = new();
    readonly Dictionary<PhysicsProp, (Vector3 spawn, Rigidbody body)> props = new();
    WizardWatch wizardWatch;
    ChemGrid grid;
    GameObject wizard;
    ThirdPersonController controller;
    SurfaceEffects surface;
    PlaytestPlayer player;
    PlaytestAudioTap tap;
    List<(string name, Vector3 at)> places;
    PlaytestRandom dice;
    int frame;
    bool running, soundsPlaying;
    string goal = "starting";
    float startedReal;

    // The run's seed, worked out before the scene loads (UnityEngine.Random needs it by then).
    static ulong? runSeed;

    // Before the scene loads, so its first frames are locked too: the chemistry clock adds up
    // deltaTime, and one slow loading frame on real time put a run a tick ahead of its replay.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void LockTimeIfAsked()
    {
        // Statics outlive a Play session when the Editor skips the domain reload: start each one clean.
        ChemGrid.ExactFrames = false;
        runSeed = null;
        var args = Environment.GetCommandLineArgs();
        if (!args.Contains("-playtest")) return;
        // Game time moves exactly 1/30 s a frame; the cap keeps the bot from spinning the GPU.
        Time.captureFramerate = Fps;
        Application.targetFrameRate = Fps;
        QualitySettings.vSyncCount = 0;
        ChemGrid.ExactFrames = true;
        runSeed = SeedOf(args);
        UnityEngine.Random.InitState(ReplayCheck.UnitySeed(runSeed.Value));
    }

    static string Arg(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    // The replayed run's seed, else -playtest-seed, else the time.
    static ulong SeedOf(string[] args)
    {
        string replay = Arg(args, "-playtest-replay");
        if (replay != null && File.Exists(replay)) return RunReport.FromJson(File.ReadAllText(replay)).Seed;
        return ulong.TryParse(Arg(args, "-playtest-seed"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var s) ? s : (ulong)DateTime.Now.Ticks;
    }

    // The window is up by the splash screen and before the valley loads (half a minute): give the
    // front back then too, so keys typed elsewhere during the load stay there.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)]
    static void HandBackAtSplash() => HandBackEarly();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void HandBackBeforeLoad() => HandBackEarly();

    static void HandBackEarly()
    {
        if (long.TryParse(Arg(Environment.GetCommandLineArgs(), "-playtest-hand-focus"), NumberStyles.Integer, CultureInfo.InvariantCulture, out long window) && window != 0)
            PlaytestWindow.HandBack(window);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void StartIfAsked()
    {
        if (Environment.GetCommandLineArgs().Contains("-playtest"))
            new GameObject("Playtest bot").AddComponent<PlaytestBot>();
    }

    /// <summary>Whether a playtest run is going (EventLog.ResetYard won't reload the scene under it).</summary>
    public static bool Active { get; private set; }

    void Awake()
    {
        Active = true;
        handUntil = Time.realtimeSinceStartup + HandFocusSeconds;
        DontDestroyOnLoad(gameObject);
        ReadSettings(Environment.GetCommandLineArgs());
        Directory.CreateDirectory(Path.Combine(outDir, "shots"));
        Application.logMessageReceived += OnLog;
    }

    void OnDestroy()
    {
        Active = false;
        Application.logMessageReceived -= OnLog;
    }

    void ReadSettings(string[] args)
    {
        string Arg(string name) => PlaytestBot.Arg(args, name);
        float Number(string name, float fallback) =>
            float.TryParse(Arg(name), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;

        run.RunId = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        run.Started = DateTime.Now.ToString("s", CultureInfo.InvariantCulture);
        run.Build = BuildInfo.Label;
        run.Seed = runSeed ?? SeedOf(args);
        string replay = Arg("-playtest-replay");
        RunReport original = replay != null && File.Exists(replay) ? RunReport.FromJson(File.ReadAllText(replay)) : null;
        if (original != null) run.ReplayOf = original.RunId;
        seconds = Number("-playtest-seconds", Number("-playtest-minutes", 8f) * 60f);
        untilFrame = int.TryParse(Arg("-playtest-until-frame"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var u) ? u : int.MaxValue;
        goalsFirst = (Arg("-playtest-goals") ?? "").Split(',').Select(k => k.Trim()).Where(k => k.Length > 0).ToList();
        string cov = Arg("-playtest-coverage");
        if (cov != null && File.Exists(cov)) coverage = JsonConvert.DeserializeObject<Dictionary<string, int>>(File.ReadAllText(cov)) ?? new();
        // A replay chooses against what the original knew (coverage.json has moved on since).
        if (original != null) coverage = original.Coverage ?? new();
        run.Coverage = new Dictionary<string, int>(coverage);
        offscreen = args.Contains("-playtest-offscreen");
        long.TryParse(Arg("-playtest-hand-focus"), NumberStyles.Integer, CultureInfo.InvariantCulture, out handFocus);
        mute = args.Contains("-playtest-mute");
        outDir = Arg("-playtest-out") ?? Path.Combine(Application.persistentDataPath, "playtest", run.RunId);
        dice = new PlaytestRandom(run.Seed);
    }

    IEnumerator Start()
    {
        var notes = new System.Text.StringBuilder();
        if (offscreen) PlaytestWindow.MoveOffScreen(notes);
        if (mute) AudioListener.volume = 0f;

        for (int i = 0; i < 600 && (wizard == null || grid == null || grid.World == null); i++)
        {
            wizard = WizardRig.FindLocal();
            grid = FindAnyObjectByType<ChemGrid>();
            yield return null;
        }
        if (wizard == null || grid == null)
        {
            Finish("error: no wizard or world");
            yield break;
        }
        Setup();
        while (running && frame < untilFrame && Time.realtimeSinceStartup - startedReal < seconds * 3f && Time.time < seconds)
        {
            var next = NextGoal();
            goal = next.ToString();
            run.Goals.Add(next.Key);
            Shoot("goal");
            yield return player.Do(next);
        }
        Finish(frame >= untilFrame ? "until-frame" : "done");
    }

    // Goals asked for on the command line come first, in order (one the options don't allow is
    // skipped, and logged); then the dice choose as usual.
    List<string> goalsFirst = new();

    Goal NextGoal()
    {
        var options = player.Options();
        while (goalsFirst.Count > 0)
        {
            string key = goalsFirst[0];
            goalsFirst.RemoveAt(0);
            var asked = GoalPicker.ByKey(dice, options, key);
            if (asked != null) return asked;
            Trace("no goal", key + " isn't possible here");
        }
        return GoalPicker.Next(dice, options, coverage);
    }

    void Setup()
    {
        controller = wizard.GetComponent<ThirdPersonController>();
        surface = wizard.GetComponent<SurfaceEffects>();
        var listener = FindAnyObjectByType<AudioListener>();
        if (listener != null) tap = listener.gameObject.AddComponent<PlaytestAudioTap>();
        places = PlaytestPlaces.Find(grid, wizard.transform.position);
        var lo = grid.CellCenter(0, grid.World.H - 1);
        var hi = grid.CellCenter(grid.World.W - 1, 0);
        wizardWatch = new WizardWatch(Mathf.Min(lo.x, hi.x) - 5f, Mathf.Min(lo.z, hi.z) - 5f, Mathf.Max(lo.x, hi.x) + 5f, Mathf.Max(lo.z, hi.z) + 5f);
        player = new PlaytestPlayer(grid, wizard, dice, places, Trace, Add, Shoot);
        run.Quiet = Environment.GetCommandLineArgs().Contains("-playtest-quiet");
        startedReal = Time.realtimeSinceStartup;
        running = true;
        DeafToRealInput();
    }

    // The bot drives the wizard through its inputs, never the real devices: a key or pad press that
    // reaches the bot's window (it took focus once, seed 103) mustn't change the run or reset it.
    static void DeafToRealInput()
    {
        foreach (var device in InputSystem.devices.ToList())
            if (device is Keyboard || device is Mouse || device is Gamepad || device is Joystick)
                InputSystem.DisableDevice(device);
        InputSystem.onDeviceChange += (device, change) =>
        {
            if (change == InputDeviceChange.Added && (device is Keyboard || device is Mouse || device is Gamepad || device is Joystick))
                InputSystem.DisableDevice(device);
        };
    }

    // From the bot's first frame (the valley takes half a minute to load, so not from the process's
    // start): Unity raises its window as it starts up, whatever the runner asked.
    void Update()
    {
        if (handFocus == 0 || Time.realtimeSinceStartup > handUntil || !PlaytestWindow.HandBack(handFocus)) return;
        if (handedBack++ == 0) Debug.Log($"PLAYTEST: handed the front back to window {handFocus} at {Time.realtimeSinceStartup:0.0} s");
    }

    void LateUpdate()
    {
        if (!running) return;
        using var marked = WatchMarker.Auto();
        // The scene went away under the run (seed 103 on a development build: a reload, every frame
        // after it a NullReferenceException here): stop cleanly with what was found so far.
        if (wizard == null || grid == null)
        {
            Add(new Candidate("bot-lost-wizard", Here0, Category.GameBreaking, frame, "the wizard or the world was destroyed mid-run (a scene reload?)"));
            Finish("bot-lost-wizard");
            return;
        }
        frame++;
        player.Frame = frame;
        player.HoldLook();
        WatchWizard();
        if (frame % ReplayCheck.SyncEvery == 0)
        {
            var p = wizard.transform.position;
            Trace(ReplayCheck.Sync, ReplayCheck.SyncDetail(grid.World.Tick, p.x, p.y, p.z));
        }
        // Spikes while Hasten Time runs the chemistry fast are their own paper cut, not the place's.
        string spikeAt = grid.World.Haste.HasValue ? Here + " (hasten time)" : Here;
        foreach (var c in frameWatch.Frame(frame, Time.unscaledDeltaTime * 1000f, spikeAt)) Add(c);
        if (tap != null) WatchSound();
        if (frame % PropEvery == 0) WatchProps();
        if (frame % HeartbeatEvery == 0) File.WriteAllText(Path.Combine(outDir, "heartbeat.txt"), frame.ToString(CultureInfo.InvariantCulture));
    }

    string Here => PlaytestPlaces.Near(places, grid, wizard.transform.position);

    // A dark picture is right whenever the sky isn't bright: at night, and at dawn and dusk
    // (World.Daylight, sim.json day), which Hasten Time often runs the clock into.
    bool Night => grid.World.Daylight != Chem.LightLevel.Bright;

    void WatchWizard()
    {
        var p = wizard.transform.position;
        var sample = new WizardWatch.Sample
        {
            X = p.x, Y = p.y, Z = p.z,
            Ground = grid.GroundAt(p),
            // Swimming isn't on the ground but isn't airborne either.
            Grounded = controller == null || controller.Grounded || (surface != null && surface.Swimming),
            Pushing = player.Pushing,
            BotMoved = player.TeleportedOnFrame == frame,
            Place = Here,
        };
        foreach (var c in wizardWatch.Frame(frame, sample)) Add(c);
    }

    void WatchSound()
    {
        if (frame % SoundEvery == 0)
            soundsPlaying = FindObjectsByType<AudioSource>(FindObjectsSortMode.None).Any(s => s.isPlaying && s.volume > 0.01f && !s.mute);
        foreach (var c in audioWatch.Frame(frame, tap.TakePeak(), tap.SawNaN, soundsPlaying, Here)) Add(c);
    }

    void WatchProps()
    {
        foreach (var prop in FindObjectsByType<PhysicsProp>(FindObjectsSortMode.None))
        {
            if (!props.TryGetValue(prop, out var known))
            {
                known = (prop.transform.position, prop.GetComponent<Rigidbody>());
                props[prop] = known;
            }
            var p = prop.transform.position;
            var chunk = grid.ChunkAt(p);
            var sample = new PropWatch.Sample
            {
                Id = prop.Thing.ToString(), Kind = prop.kind,
                X = p.x, Y = p.y, Z = p.z,
                Speed = known.body != null ? known.body.linearVelocity.magnitude : 0f,
                Ground = grid.GroundAt(p),
                SpawnX = known.spawn.x, SpawnZ = known.spawn.z,
                Asleep = prop.Asleep,
                ChunkActive = grid.IsChunkActive(chunk.x, chunk.y),
            };
            foreach (var c in propWatch.Frame(frame, sample)) Add(c);
        }
    }

    void OnLog(string message, string stack, LogType type)
    {
        bool error = type == LogType.Error || type == LogType.Exception || type == LogType.Assert;
        // The bot's own lines never count against the game.
        if (!error || message.StartsWith("PLAYTEST", StringComparison.Ordinal)) return;
        var c = LogWatch.Line(frame, message, stack, true);
        if (c != null) Add(c);
    }

    void Trace(string action, string detail) => run.Trace.Add(new TraceStep { Frame = frame, Action = action, Detail = detail });

    // Records a candidate; the first of its signature in this run gets a screenshot.
    void Add(Candidate c)
    {
        c.Goal = goal;
        if (found.Add(c) && !c.Detector.StartsWith("image-", StringComparison.Ordinal)) c.Screenshot = Shoot(c.Detector);
    }

    // A screenshot from the main camera into a RenderTexture (it doesn't need the window), saved as
    // shots/<frame>-<label>.jpg and checked for magenta, black and white. Returns its path in the run.
    string Shoot(string label)
    {
        using var marked = ShotMarker.Auto();
        frameWatch.ShotTaken(frame);
        var cam = Camera.main;
        if (cam == null) return null;
        // In the 0.1.8 runs a third of the shots showed only ground, sky or night. At night a goal's
        // routine shot is skipped; a candidate's or a cast's is lit instead.
        bool night = Night;
        if (night && label == "goal")
        {
            Trace("no shot", "night");
            return null;
        }
        string unusable = Unusable(cam);
        if (unusable != null)
        {
            Trace("reframe", unusable);
            cam = Reframed(cam);
        }
        var ambient = RenderSettings.ambientProbe;
        if (night) RenderSettings.ambientProbe = ambient + NightFill();
        var rt = RenderTexture.GetTemporary(ShotWidth, ShotHeight, 24);
        var was = cam.targetTexture;
        cam.targetTexture = rt;
        cam.Render();
        cam.targetTexture = was;
        RenderSettings.ambientProbe = ambient;
        var active = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(ShotWidth, ShotHeight, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, ShotWidth, ShotHeight), 0, 0);
        tex.Apply();
        RenderTexture.active = active;
        RenderTexture.ReleaseTemporary(rt);

        string file = Path.Combine("shots", $"{frame:D6}-{label}.jpg");
        File.WriteAllBytes(Path.Combine(outDir, file), tex.EncodeToJPG(80));
        Trace(ReplayCheck.Shot, file);
        foreach (var c in ImageCheck.Pixels(frame, tex.GetRawTextureData(), Here, Night))
        {
            c.Screenshot = file;
            Add(c);
        }
        Destroy(tex);
        return file;
    }

    // Why the camera's view can't be judged, or null: inside the ground, straight down at it, or
    // up at open sky (nothing in front of it for 500 m).
    string Unusable(Camera cam)
    {
        var at = cam.transform.position;
        var forward = cam.transform.forward;
        if (at.y < grid.GroundAt(at) + 0.1f) return "the camera is in the ground";
        if (forward.y < -0.9f) return "the camera looks straight down";
        if (forward.y > 0.3f && !Physics.Raycast(at, forward, 500f)) return "the camera faces open sky";
        return null;
    }

    Camera shotCamera;

    // A camera behind and above the wizard, looking at them, with the main camera's settings: for
    // a shot the main camera can't take.
    Camera Reframed(Camera main)
    {
        if (shotCamera == null)
        {
            shotCamera = new GameObject("Playtest shot camera").AddComponent<Camera>();
            shotCamera.transform.SetParent(transform);
            shotCamera.enabled = false;
        }
        shotCamera.CopyFrom(main);
        var w = wizard.transform;
        var back = new Vector3(w.forward.x, 0f, w.forward.z).normalized;
        var at = w.position - back * 6f + Vector3.up * 3f;
        at.y = Mathf.Max(at.y, grid.GroundAt(at) + 2f);
        shotCamera.transform.position = at;
        shotCamera.transform.LookAt(w.position + Vector3.up * 1.2f);
        return shotCamera;
    }

    // Flat grey ambient light added for a night shot, enough to see shapes and colours by.
    static SphericalHarmonicsL2 NightFill()
    {
        var fill = new SphericalHarmonicsL2();
        fill.AddAmbientLight(new Color(0.45f, 0.45f, 0.5f));
        return fill;
    }

    // Where the run was last, for a candidate raised once the wizard is gone.
    string Here0 => run.Goals.Count > 0 ? run.Goals[run.Goals.Count - 1] : "start";

    void Finish(string ended)
    {
        if (run.Ended != null) return;
        running = false;
        run.Ended = ended;
        run.Frames = frame;
        run.RealSeconds = Time.realtimeSinceStartup - startedReal;
        run.Candidates = found.Candidates.ToList();
        foreach (var c in run.Candidates) run.Counts[c.Signature] = found.CountOf(c.Signature);
        run.Metrics["frame_ms_p50"] = frameWatch.Percentile(0.5);
        run.Metrics["frame_ms_p95"] = frameWatch.Percentile(0.95);
        run.Metrics["frame_ms_worst"] = frameWatch.Times.Count == 0 ? 0f : frameWatch.Times.Max();
        run.Metrics["audio_peak"] = tap != null ? tap.Peak : -1f;
        run.Metrics["focus_handed_back"] = handedBack;
        run.Metrics["memory_mb"] = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / (1024f * 1024f);
        File.WriteAllText(Path.Combine(outDir, "run.json"), run.ToJson());
        Debug.Log($"PLAYTEST: {ended}; {frame} frames, {run.Goals.Count} goals, {run.Candidates.Count} candidates; wrote {outDir}");
        Application.Quit();
    }
}
