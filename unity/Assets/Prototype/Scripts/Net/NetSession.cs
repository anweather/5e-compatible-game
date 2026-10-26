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
//
// Both players must run the same build (lockstep). A joining player sends its
// version and commit (BuildInfo) in the connection request and the host turns
// a different one away with a reason (BuildVersion.Mismatch). Netcode itself
// refuses a build with different network prefabs earlier, without a reason;
// either way the joiner goes back to solo with a message saying why.
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
    bool joining; // started a client that the host hasn't accepted yet

    // Why the last join failed; static so it outlives the scene reload back to solo.
    static string joinFailure;
    static float joinFailureUntil;

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
        if (Net == null) return;
        Net.ConnectionApprovalCallback = Approve;
        Net.OnClientConnectedCallback += Connected;
        Net.OnClientDisconnectCallback += Disconnected;
    }

    void OnDestroy()
    {
        if (manager == null) return;
        manager.OnClientConnectedCallback -= Connected;
        manager.OnClientDisconnectCallback -= Disconnected;
    }

    void Connected(ulong clientId)
    {
        if (clientId == Net.LocalClientId) joining = false;
    }

    // A join the host refused (or that never got through): say why and go back to solo.
    void Disconnected(ulong clientId)
    {
        if (Net.IsServer || !joining) return;
        joining = false;
        joinFailure = BuildVersion.JoinFailure(Net.DisconnectReason);
        joinFailureUntil = Time.realtimeSinceStartup + 20f;
        Debug.LogWarning($"NET: {joinFailure} (Netcode said: {Net.DisconnectReason})");
        _ = Leave();
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
        joining = true;
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
        catch (Exception e) when (joinFailure != null)
        {
            // The host turned us away and Disconnected already said why; the session's own error adds nothing.
            Debug.Log($"NET: the join ended: {e.Message}");
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
        joining = true;
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

    // The solo wizard isn't networked; the network spawns everyone's. The connection
    // request carries this copy's version for the host to check.
    void PrepareForNetwork()
    {
        joinFailure = null;
        Net.NetworkConfig.ConnectionData = BuildVersion.Encode(BuildInfo.Stamp);
        var solo = WizardRig.FindLocal();
        if (solo != null && solo.GetComponent<NetworkObject>() == null) Destroy(solo);
    }

    void Approve(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
    {
        bool self = request.ClientNetworkId == NetworkManager.ServerClientId;
        string refusal = self ? null : BuildVersion.Mismatch(BuildInfo.Stamp, BuildVersion.Decode(request.Payload));
        if (refusal == null && Net.ConnectedClientsIds.Count >= MaxPlayers) refusal = $"The yard is full ({MaxPlayers} players).";
        if (refusal != null) Debug.Log($"NET: turned client {request.ClientNetworkId} away: {refusal}");
        response.Approved = refusal == null;
        response.CreatePlayerObject = refusal == null;
        response.Reason = refusal;
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
        else if (!online && joinFailure != null) GUILayout.Label($"<color=#ff9a7a>{joinFailure}</color>", label);
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
        if (joinFailure != null && Time.realtimeSinceStartup < joinFailureUntil && EventLog.HudVisible)
        {
            MakeStyles();
            GUI.Label(new Rect(Screen.width - 616f, 76f, 600f, 90f), $"<color=#ff9a7a>{joinFailure}</color>", label);
        }
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
