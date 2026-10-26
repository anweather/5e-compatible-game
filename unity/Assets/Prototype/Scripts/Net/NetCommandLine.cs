using System;
using System.Globalization;
using Unity.Netcode;
using UnityEngine;

// Online play from the command line, for testing two machines without anyone
// typing join codes:
//
//   SpellChemistryValley -host-online          host through Relay; the join code goes to the log
//   SpellChemistryValley -join-code K7QX2M     join that code
//
// Either way it also writes a "NET STATUS" line to the player log every 10
// seconds: role, players, the chemistry tick, and whether this machine has
// seen a desync, so both machines' logs show whether they stayed in step.
// Without the flags it does nothing.
public class NetCommandLine : MonoBehaviour
{
    const float StatusEvery = 10f;

    static bool started; // once per run: Leave reloads the scene, which mustn't host again
    NetSession session;
    float nextStatus;
    bool codeLogged;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void StartIfAsked()
    {
        if (started) return;
        var args = Environment.GetCommandLineArgs();
        bool host = Array.IndexOf(args, "-host-online") >= 0;
        string code = ValueAfter(args, "-join-code");
        if (!host && code == null) return;
        started = true;
        var go = new GameObject("Net command line");
        DontDestroyOnLoad(go);
        go.AddComponent<NetCommandLine>().Begin(host, code);
    }

    static string ValueAfter(string[] args, string flag)
    {
        int i = Array.IndexOf(args, flag);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    void Begin(bool host, string code)
    {
        session = FindFirstObjectByType<NetSession>();
        if (session == null)
        {
            Debug.LogError("NET: no NetSession in this scene; can't go online.");
            return;
        }
        Debug.Log(host ? "NET: hosting online" : $"NET: joining {code}");
        _ = host ? session.HostOnline() : session.JoinOnline(code);
    }

    void Update()
    {
        if (session == null) session = FindFirstObjectByType<NetSession>();
        if (session != null && !codeLogged && session.SessionCode != null)
        {
            codeLogged = true;
            Debug.Log($"NET JOIN CODE: {session.SessionCode}");
        }
        if (Time.unscaledTime < nextStatus) return;
        nextStatus = Time.unscaledTime + StatusEvery;
        Debug.Log(Status());
    }

    static string Status()
    {
        var grid = FindFirstObjectByType<ChemGrid>();
        var chem = FindFirstObjectByType<NetChem>();
        int players = FindObjectsByType<NetWizard>(FindObjectsSortMode.None).Length;
        string tick = grid != null && grid.World != null ? grid.World.Tick.ToString(CultureInfo.InvariantCulture) : "-";
        bool desync = chem != null && chem.DesyncSeen;
        return $"NET STATUS: role={Role()} players={players} tick={tick} desync={desync}";
    }

    static string Role()
    {
        var net = NetworkManager.Singleton;
        if (net == null || !net.IsListening) return "offline";
        if (net.IsHost) return "host";
        return net.IsConnectedClient ? "client" : "connecting";
    }
}
