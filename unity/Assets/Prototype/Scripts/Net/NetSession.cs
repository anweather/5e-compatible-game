using System;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

// Playing together: the Play together section of the Esc menu (PauseMenu), its
// Join by code page for controllers (CodePicker: up / down change a letter, A
// next, B back; a copied code is filled in), and a one-line status with the
// join code top right while online.
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
    readonly CodePicker picker = new();
    PauseMenu.Page codePage;
    GUIStyle slot, slotOn;

    /// <summary>The Join by code page's id in the Esc menu.</summary>
    public const string CodePage = "join-code";

    // Why the last join failed; static so it outlives the scene reload back to solo.
    static string joinFailure;
    static float joinFailureUntil;

    // Starts over each Play mode, even with the domain reload off (Enter Play Mode Options).
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        joinFailure = null;
        joinFailureUntil = 0f;
    }

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

    void OnEnable()
    {
        codePage ??= new PauseMenu.Page
        {
            Title = "Join by code",
            Draw = DrawCodePage,
            Move = delta => { picker.Step(-delta); return true; }, // up: next letter
            Submit = SubmitPicked,
            Back = () => picker.Back(),
        };
        PauseMenu.AddSection(MenuSection);
        PauseMenu.AddPage(CodePage, codePage);
    }

    void OnDisable()
    {
        PauseMenu.RemoveSection(MenuSection);
        PauseMenu.RemovePage(CodePage);
    }

    // A on the Join by code page: the next letter, or join after the last.
    bool SubmitPicked()
    {
        if (picker.Next()) JoinPicked();
        return true;
    }

    /// <summary>Opens the Join by code page, filled in with a copied (or typed) code if there is one.</summary>
    public void PickCode()
    {
        string clip = GUIUtility.systemCopyBuffer;
        picker.Begin(CodePicker.LooksLikeCode(joinCode) ? joinCode : clip);
        PauseMenu.Instance.ShowPage(CodePage);
    }

    // The picker's code is done: join with it, back on the main page where the status shows.
    void JoinPicked()
    {
        joinCode = picker.Code;
        PauseMenu.Instance.CloseSubPages();
        _ = JoinOnline(joinCode);
    }

    void DrawCodePage()
    {
        MakeStyles();
        GUILayout.Label(PadGlyphs.Text("Up / down change the letter, {South} goes to the next one (on the last, {South} joins), {East} goes back."), label);
        GUILayout.Space(8);
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        string code = picker.Code;
        for (int i = 0; i < CodePicker.Length; i++)
            GUILayout.Label(i == picker.Slot ? $"▲\n{code[i]}\n▼" : $"\n{code[i]}\n", i == picker.Slot ? slotOn : slot, GUILayout.Width(44));
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
        GUILayout.Space(8);
        GUI.enabled = !busy;
        if (GUILayout.Button($"Join {code}", PauseMenu.ButtonStyle)) JoinPicked();
        GUI.enabled = true;
    }

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
        if (PauseMenu.Button("Host online", PauseMenu.ButtonStyle)) _ = HostOnline();
        GUILayout.BeginHorizontal();
        joinCode = GUILayout.TextField(joinCode, 12, field, GUILayout.Width(170));
        if (PauseMenu.Button("Join code", null, GUILayout.Height(32)) && joinCode.Trim().Length > 0) _ = JoinOnline(joinCode);
        GUILayout.EndHorizontal();
        // The code field needs a keyboard; with a controller, pick the code letter by letter.
        if (Gamepad.current != null && PauseMenu.Button("Enter a code with the controller", null, GUILayout.Height(32))) PickCode();
        GUILayout.Label("Same network:", label);
        GUILayout.BeginHorizontal();
        if (PauseMenu.Button("Host", null, GUILayout.Width(70), GUILayout.Height(32))) Host();
        address = GUILayout.TextField(address, field, GUILayout.Width(150));
        if (PauseMenu.Button("Join", null, GUILayout.Height(32))) Join(address);
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
            if (PauseMenu.Button("Copy", null, GUILayout.Width(70), GUILayout.Height(32))) GUIUtility.systemCopyBuffer = SessionCode;
            GUILayout.EndHorizontal();
        }
        if (PauseMenu.Button("Leave", PauseMenu.ButtonStyle)) _ = Leave();
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
        HudScale.Apply();
        if (joinFailure != null && Time.realtimeSinceStartup < joinFailureUntil && EventLog.HudVisible)
        {
            MakeStyles();
            GUI.Label(new Rect(HudScale.Width - 616f, 76f, 600f, 90f), $"<color=#ff9a7a>{joinFailure}</color>", label);
        }
        if (Net == null || !EventLog.HudVisible || !(Net.IsListening || session != null)) return;
        MakeStyles();
        GUI.Label(new Rect(HudScale.Width - 456f, 76f, 440f, 30f), StatusText() + "   <color=#ffffff88>Esc: menu</color>", corner);
    }

    void MakeStyles()
    {
        if (label != null) return;
        label = new GUIStyle(GUI.skin.label) { fontSize = 16, richText = true, wordWrap = true };
        field = new GUIStyle(GUI.skin.textField) { fontSize = 18, fixedHeight = 32 };
        codeStyle = new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold };
        corner = new GUIStyle(label) { alignment = TextAnchor.UpperRight, wordWrap = false };
        slot = new GUIStyle(GUI.skin.box) { fontSize = 26, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, fixedHeight = 96 };
        slotOn = new GUIStyle(slot);
        slotOn.normal.textColor = new Color(1f, 0.82f, 0.29f);
        slotOn.normal.background = PauseMenu.Solid(new Color(1f, 0.82f, 0.29f, 0.22f));
    }
}
