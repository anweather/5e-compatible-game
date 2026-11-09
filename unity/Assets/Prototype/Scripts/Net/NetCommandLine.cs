using System;
using System.Globalization;
using Unity.Netcode;
using UnityEngine;

// Online play from the command line, for testing two machines without anyone
// typing join codes:
//
//   SpellChemistryValley -host-online          host through Relay; the join code goes to the log
//   SpellChemistryValley -join-code K7QX2M     join that code
//   -room-mode Locked (or HostOnly, Open)      the change mode of a room this copy hosts
//   -host-local / -join-address 127.0.0.1      the same on one network, no Relay (two copies on one machine)
//   -walk-to 620,1440                          once in, walk to that cell along a path (TestWalk; a test run's walk
//                                              from the valley into the next square, with no one at the keys)
//
// Either way it also writes a "NET STATUS" line to the player log every 10
// seconds: role, players, the chemistry tick, whether this machine has seen a
// desync, the frames since the last line (mean and 95th percentile) and the
// memory the process holds, so both machines' logs show whether they stayed in step.
// And once Netcode starts, a "NET CONFIG" line with the NetworkConfig hash:
// a host refuses a client whose hash differs (different network prefabs),
// silently in a release build, so compare the two machines' lines.
// Without the flags it does nothing.
public class NetCommandLine : MonoBehaviour
{
    const float StatusEvery = 10f;

    static bool started; // once per run: Leave reloads the scene, which mustn't host again

    // Starts over each Play mode, even with the domain reload off (Enter Play Mode Options).
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        started = false;
    }

    NetSession session;
    float nextStatus;
    bool codeLogged;
    bool configLogged;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void StartIfAsked()
    {
        if (started) return;
        var args = Environment.GetCommandLineArgs();
        bool host = Array.IndexOf(args, "-host-online") >= 0, hostLocal = Array.IndexOf(args, "-host-local") >= 0;
        string code = ValueAfter(args, "-join-code"), address = ValueAfter(args, "-join-address");
        if (!host && !hostLocal && code == null && address == null) return;
        started = true;
        if (Enum.TryParse(ValueAfter(args, "-room-mode"), out Chem.ChangeMode mode)) NetSession.RoomMode = mode;
        var go = new GameObject("Net command line");
        DontDestroyOnLoad(go);
        var line = go.AddComponent<NetCommandLine>();
        string[] to = (ValueAfter(args, "-walk-to") ?? "").Split(',');
        if (to.Length == 2 && int.TryParse(to[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int tx)
            && int.TryParse(to[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int ty))
        {
            line.walkTo = new Vector2Int(tx, ty);
            line.walkSeconds = 240f;
        }
        if (hostLocal || address != null) line.BeginLocal(hostLocal, address);
        else line.Begin(host, code);
    }

    float walkSeconds, walked;

    void BeginLocal(bool host, string address) => StartCoroutine(Local(host, address));

    // A frame later, not as the scene loads: Netcode's generated serializers start then too, in no set order, and a
    // network started before them can't send its variables (the host's map, a FixedString).
    System.Collections.IEnumerator Local(bool host, string address)
    {
        yield return null;
        session = FindAnyObjectByType<NetSession>();
        if (session == null)
        {
            Debug.LogError("NET: no NetSession in this scene; can't go online.");
            yield break;
        }
        Debug.Log(host ? "NET: hosting on this network" : $"NET: joining {address}");
        if (host) session.Host();
        else session.Join(address);
    }

    // To the cell -walk-to names, along a path over the cells (TestWalk): the controller's stick is the camera's.
    void Walk()
    {
        var net = NetworkManager.Singleton;
        var me = net != null && net.IsConnectedClient ? net.SpawnManager.GetLocalPlayerObject() : null;
        var chem = FindAnyObjectByType<NetChem>();
        var grid = ChemGrid.Current?.Grid;
        if (me == null || chem == null || !chem.CaughtUp || Camera.main == null || grid == null) return;
        var inputs = me.GetComponent(WizardRig.InputsComponent);
        var move = WizardRig.Field(inputs, "move", isPublic: true);
        var sprint = WizardRig.Field(inputs, "sprint", isPublic: true);
        var jump = WizardRig.Field(inputs, "jump", isPublic: true);
        if (move == null) return;
        if (walk == null)
        {
            walk = TestWalk.To(grid, grid.CellAt(me.transform.position), walkTo);
            Debug.Log(walk != null ? $"NET WALK: {walk.Length} cells to {walkTo}" : $"NET WALK: no way to {walkTo}");
            if (walk == null)
            {
                walkSeconds = 0f;
                return;
            }
        }
        walked += Time.deltaTime;
        bool on = walked < walkSeconds && !walk.Done;
        bool hop = false;
        var stick = on ? walk.Stick(grid, me.transform, Camera.main.transform, Time.deltaTime, out hop) : Vector2.zero;
        move.SetValue(inputs, stick);
        sprint?.SetValue(inputs, on);
        jump?.SetValue(inputs, on && hop);
        if (!on)
        {
            walkSeconds = 0f;
            Debug.Log($"NET WALK: {(walk.Done ? "there" : "out of time")}, at cell {grid.CellAt(me.transform.position)} after {walked:0} s");
        }
    }

    TestWalk walk;
    Vector2Int walkTo;

    static string ValueAfter(string[] args, string flag)
    {
        int i = Array.IndexOf(args, flag);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    void Begin(bool host, string code)
    {
        session = FindAnyObjectByType<NetSession>();
        if (session == null)
        {
            Debug.LogError("NET: no NetSession in this scene; can't go online.");
            return;
        }
        Debug.Log(host ? "NET: hosting online" : $"NET: joining {code}");
        var net = NetworkManager.Singleton;
        if (net != null)
        {
            net.OnServerStarted += LogConfig;
            net.OnClientStarted += LogConfig;
        }
        _ = host ? session.HostOnline() : session.JoinOnline(code);
    }

    // After Netcode has registered its prefabs, so the hash is the one it sends and compares.
    void LogConfig()
    {
        if (configLogged || NetworkManager.Singleton == null) return;
        configLogged = true;
        Debug.Log($"NET CONFIG: {NetworkManager.Singleton.NetworkConfig.GetConfig()} (host and client must match)");
    }

    void Update()
    {
        if (session == null) session = FindAnyObjectByType<NetSession>();
        if (session != null && !codeLogged && session.SessionCode != null)
        {
            codeLogged = true;
            Debug.Log($"NET JOIN CODE: {session.SessionCode}");
        }
        if (walkSeconds > 0f) Walk();
        frames.Add(Time.unscaledDeltaTime * 1000f);
        if (Time.unscaledTime < nextStatus) return;
        nextStatus = Time.unscaledTime + StatusEvery;
        Debug.Log(Status());
        frames.Clear();
    }

    readonly System.Collections.Generic.List<float> frames = new();

    string Status()
    {
        var world = ChemGrid.Current?.World;
        var chem = FindAnyObjectByType<NetChem>();
        int players = FindObjectsByType<NetWizard>(FindObjectsSortMode.None).Length;
        string tick = world != null ? world.Tick.ToString(CultureInfo.InvariantCulture) : "-";
        bool desync = chem != null && chem.DesyncSeen;
        frames.Sort();
        float mean = 0f;
        foreach (float f in frames) mean += f;
        mean = frames.Count > 0 ? mean / frames.Count : 0f;
        float p95 = frames.Count > 0 ? frames[Mathf.Min(frames.Count - 1, (int)(frames.Count * 0.95f))] : 0f;
        long mb = UnityEngine.Profiling.Profiler.GetTotalReservedMemoryLong() / (1024 * 1024);
        return string.Format(CultureInfo.InvariantCulture, "NET STATUS: role={0} players={1} tick={2} desync={3} frame_mean_ms={4:0.0} frame_p95_ms={5:0.0} memory_mb={6}",
            Role(), players, tick, desync, mean, p95, mb);
    }

    static string Role()
    {
        var net = NetworkManager.Singleton;
        if (net == null || !net.IsListening) return "offline";
        if (net.IsHost) return "host";
        return net.IsConnectedClient ? "client" : "connecting";
    }
}
