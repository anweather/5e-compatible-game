using System;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.SceneManagement;

// The Play Together panel, top right. Solo play uses the scene's own wizard;
// hosting or joining removes it and the network spawns one wizard per player
// (up to MaxPlayers).
//
// Online: Unity Relay through a Multiplayer Services session. The host gets a
// short join code to send a friend; nobody needs an account (anonymous
// sign-in) and nobody opens router ports. The session starts Netcode itself.
// Same network: host on UDP port Port, join by address (also how Multiplayer
// Play Mode tests run without the cloud).
public class NetSession : MonoBehaviour
{
    public const int MaxPlayers = 4;
    public const ushort Port = 7777;

    string address = "127.0.0.1";
    string joinCode = "";
    string status;
    bool busy;
    ISession session;
    GUIStyle box, codeStyle;
    NetworkManager manager;

    // The scene's NetworkManager (Netcode's singleton), looked up once it exists.
    NetworkManager Net
    {
        get
        {
            if (manager == null) manager = NetworkManager.Singleton;
            return manager;
        }
    }

    UnityTransport Transport => Net.GetComponent<UnityTransport>();

    void Start()
    {
        if (Net != null) Net.ConnectionApprovalCallback = Approve;
    }

    // --- online (Relay) ------------------------------------------------------

    public Task HostOnline() => Run("Starting an online yard…", async () =>
    {
        await SignIn();
        PrepareForNetwork();
        session = await MultiplayerService.Instance.CreateSessionAsync(new SessionOptions { MaxPlayers = MaxPlayers }.WithRelayNetwork());
    });

    public Task JoinOnline(string code) => Run("Joining…", async () =>
    {
        await SignIn();
        PrepareForNetwork();
        session = await MultiplayerService.Instance.JoinSessionByCodeAsync(code.Trim().ToUpperInvariant());
    });

    /// <summary>The code friends type to join this yard, once hosting online.</summary>
    public string SessionCode => session?.Code;

    // Each running copy signs in as its own anonymous player, so several copies on one
    // machine (Multiplayer Play Mode, or two builds) don't collide.
    static async Task SignIn()
    {
        if (UnityServices.State != ServicesInitializationState.Initialized)
        {
            var options = new InitializationOptions();
            options.SetProfile("p" + Guid.NewGuid().ToString("N").Substring(0, 12));
            await UnityServices.InitializeAsync(options);
        }
        if (!AuthenticationService.Instance.IsSignedIn) await AuthenticationService.Instance.SignInAnonymouslyAsync();
    }

    async Task Run(string doing, Func<Task> work)
    {
        if (busy) return;
        busy = true;
        status = doing;
        try
        {
            await work();
            status = null;
        }
        catch (Exception e)
        {
            status = $"That didn't work: {e.Message}";
            Debug.LogException(e);
        }
        finally
        {
            busy = false;
        }
    }

    // --- same network --------------------------------------------------------

    public void Host()
    {
        PrepareForNetwork();
        Transport.SetConnectionData("0.0.0.0", Port, "0.0.0.0");
        Net.StartHost();
    }

    public void Join(string host)
    {
        PrepareForNetwork();
        Transport.SetConnectionData(host, Port);
        Net.StartClient();
    }

    public async Task Leave()
    {
        if (session != null)
        {
            try { await session.LeaveAsync(); }
            catch (Exception e) { Debug.LogException(e); }
            session = null;
        }
        if (Net.IsListening) Net.Shutdown();
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    // The solo wizard isn't networked; the network spawns everyone's.
    static void PrepareForNetwork()
    {
        var solo = WizardRig.FindLocal();
        if (solo != null && solo.GetComponent<NetworkObject>() == null) Destroy(solo);
    }

    void Approve(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
    {
        bool room = Net.ConnectedClientsIds.Count < MaxPlayers;
        response.Approved = room;
        response.CreatePlayerObject = room;
        response.Reason = room ? null : $"The yard is full ({MaxPlayers} players).";
    }

    // --- panel ---------------------------------------------------------------

    void OnGUI()
    {
        if (Net == null || !EventLog.HudVisible) return;
        box ??= new GUIStyle(GUI.skin.box) { fontSize = 16, alignment = TextAnchor.UpperLeft, padding = new RectOffset(10, 10, 8, 8) };
        codeStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold };
        GUI.skin.button.fontSize = 16;
        GUI.skin.textField.fontSize = 16;
        GUI.skin.label.fontSize = 16;
        GUILayout.BeginArea(new Rect(Screen.width - 316, 12, 300, 210), box);
        if (Net.IsListening || session != null) OnlinePanel();
        else OfflinePanel();
        if (status != null) GUILayout.Label(status);
        GUILayout.EndArea();
    }

    void OfflinePanel()
    {
        GUI.enabled = !busy;
        GUILayout.Label("Play together (up to 4)");
        if (GUILayout.Button("Host online")) _ = HostOnline();
        GUILayout.BeginHorizontal();
        joinCode = GUILayout.TextField(joinCode, 12, GUILayout.Width(150));
        if (GUILayout.Button("Join code") && joinCode.Trim().Length > 0) _ = JoinOnline(joinCode);
        GUILayout.EndHorizontal();
        GUILayout.Label("Same network:");
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Host", GUILayout.Width(60))) Host();
        address = GUILayout.TextField(address, GUILayout.Width(120));
        if (GUILayout.Button("Join")) Join(address);
        GUILayout.EndHorizontal();
        GUI.enabled = true;
    }

    string RoleLabel()
    {
        if (Net.IsHost) return "Hosting";
        return Net.IsConnectedClient ? "Connected" : "Connecting…";
    }

    void OnlinePanel()
    {
        string role = RoleLabel();
        int players = session?.PlayerCount ?? (Net.IsServer ? Net.ConnectedClientsIds.Count : 0);
        GUILayout.Label(players > 0 ? $"{role} · {players}/{MaxPlayers} players" : role);
        if (SessionCode != null)
        {
            GUILayout.Label("Join code:");
            GUILayout.BeginHorizontal();
            GUILayout.Label(SessionCode, codeStyle);
            if (GUILayout.Button("Copy", GUILayout.Width(60))) GUIUtility.systemCopyBuffer = SessionCode;
            GUILayout.EndHorizontal();
        }
        else GUILayout.Label($"Same network · port {Port}");
        if (GUILayout.Button("Leave")) _ = Leave();
    }
}
