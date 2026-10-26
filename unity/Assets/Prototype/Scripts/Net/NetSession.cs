using System;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.SceneManagement;

// Playing together: the Play together section of the Esc menu (PauseMenu), and
// a one-line status with the join code top right while online.
// Solo play uses the scene's own wizard; hosting or joining removes it and
// the network spawns one wizard per player (up to MaxPlayers).
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
    GUIStyle label, field, codeStyle, corner;
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

    // --- in the Esc menu, and a status line while online ----------------------

    void OnEnable() => PauseMenu.AddSection(MenuSection);
    void OnDisable() => PauseMenu.RemoveSection(MenuSection);

    // The Play together section of the Esc menu.
    void MenuSection()
    {
        if (Net == null) return;
        MakeStyles();
        bool online = Net.IsListening || session != null;
        GUILayout.Label("<b>Play together</b>  <color=#ffffff88>(up to 4)</color>", label);
        if (online) OnlineControls();
        else OfflineControls();
        if (status != null) GUILayout.Label(status, label);
    }

    void OfflineControls()
    {
        GUI.enabled = !busy;
        if (GUILayout.Button("Host online", PauseMenu.ButtonStyle)) _ = HostOnline();
        GUILayout.BeginHorizontal();
        joinCode = GUILayout.TextField(joinCode, 12, field, GUILayout.Width(170));
        if (GUILayout.Button("Join code", GUILayout.Height(32)) && joinCode.Trim().Length > 0) _ = JoinOnline(joinCode);
        GUILayout.EndHorizontal();
        GUILayout.Label("Same network:", label);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Host", GUILayout.Width(70), GUILayout.Height(32))) Host();
        address = GUILayout.TextField(address, field, GUILayout.Width(150));
        if (GUILayout.Button("Join", GUILayout.Height(32))) Join(address);
        GUILayout.EndHorizontal();
        GUI.enabled = true;
    }

    void OnlineControls()
    {
        GUILayout.Label(StatusText(), label);
        if (SessionCode != null)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(SessionCode, codeStyle);
            if (GUILayout.Button("Copy", GUILayout.Width(70), GUILayout.Height(32))) GUIUtility.systemCopyBuffer = SessionCode;
            GUILayout.EndHorizontal();
        }
        if (GUILayout.Button("Leave", PauseMenu.ButtonStyle)) _ = Leave();
    }

    string RoleLabel()
    {
        if (Net.IsHost) return "Hosting";
        return Net.IsConnectedClient ? "Connected" : "Connecting…";
    }

    string StatusText()
    {
        int players = session?.PlayerCount ?? (Net.IsServer ? Net.ConnectedClientsIds.Count : 0);
        string where = SessionCode != null ? $"code <b>{SessionCode}</b>" : $"same network, port {Port}";
        return players > 0 ? $"{RoleLabel()} · {players}/{MaxPlayers} players · {where}" : $"{RoleLabel()} · {where}";
    }

    // While online, one line top right (under the help line and spell bar) so the join
    // code is always there to share; everything else is in the Esc menu.
    void OnGUI()
    {
        if (Net == null || !EventLog.HudVisible || !(Net.IsListening || session != null)) return;
        MakeStyles();
        GUI.Label(new Rect(Screen.width - 456f, 76f, 440f, 30f), StatusText() + "   <color=#ffffff88>Esc: menu</color>", corner);
    }

    void MakeStyles()
    {
        if (label != null) return;
        label = new GUIStyle(GUI.skin.label) { fontSize = 16, richText = true, wordWrap = true };
        field = new GUIStyle(GUI.skin.textField) { fontSize = 18, fixedHeight = 32 };
        codeStyle = new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold };
        corner = new GUIStyle(label) { alignment = TextAnchor.UpperRight, wordWrap = false };
    }
}
