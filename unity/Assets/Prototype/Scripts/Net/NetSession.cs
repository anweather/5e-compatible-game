using System;
using System.Threading.Tasks;
using Chem;
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
// Same network: host on UDP port Port, join by address. Not in the Esc menu:
// it's how Multiplayer Play Mode tests (the probes) run without the cloud.
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

    string joinCode = "";
    string status;
    bool busy;
    ISession session;
    GUIStyle label, field, codeStyle, corner;
    NetworkManager manager;
    bool joining;
    readonly CodePicker picker = new();
    PauseMenu.Page codePage;
    GUIStyle slot, slotOn;

    public const string CodePage = "join-code";

    public static ChangeMode RoomMode { get; set; }

    static string joinFailure;
    static float joinFailureUntil;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        joinFailure = null;
        joinFailureUntil = 0f;
        RoomMode = ChangeMode.Open;
        HostMap = null;
        hostWhenLoaded = false;
        joinCodeWhenLoaded = joinAddressWhenLoaded = joinedCode = joinedAddress = null;
    }

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
        ResumeAfterMapChange();
    }

    static bool hostWhenLoaded;
    static string joinCodeWhenLoaded, joinAddressWhenLoaded;
    static string joinedCode, joinedAddress;

    public static string HostMap { get; set; }

    void ResumeAfterMapChange()
    {
        if (hostWhenLoaded)
        {
            hostWhenLoaded = false;
            _ = HostOnline();
        }
        else if (joinCodeWhenLoaded != null)
        {
            string code = joinCodeWhenLoaded;
            joinCodeWhenLoaded = null;
            _ = JoinOnline(code);
        }
        else if (joinAddressWhenLoaded != null)
        {
            string address = joinAddressWhenLoaded;
            joinAddressWhenLoaded = null;
            Join(address);
        }
    }

    public static void JoinOnHostMap(string id)
    {
        Debug.Log($"NET: the host is on {id}; going there and joining again");
        joinCodeWhenLoaded = joinedCode;
        joinAddressWhenLoaded = joinedCode == null ? joinedAddress : null;
        var net = NetworkManager.Singleton;
        if (net != null && net.IsListening) net.Shutdown();
        ChemGrid.PlayMap(id);
    }

    void HostPicked()
    {
        if (HostMap == null || HostMap == ChemGrid.MapId)
        {
            _ = HostOnline();
            return;
        }
        hostWhenLoaded = true;
        ChemGrid.PlayMap(HostMap);
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

    void Disconnected(ulong clientId)
    {
        if (Net.IsServer || !joining) return;
        joining = false;
        joinFailure = BuildVersion.JoinFailure(Net.DisconnectReason);
        joinFailureUntil = Time.realtimeSinceStartup + 20f;
        string reason = Net.DisconnectReason;
        bool said = !string.IsNullOrEmpty(reason) && joinFailure.Contains(reason);
        Debug.LogWarning(said ? $"NET: {joinFailure}" : $"NET: {joinFailure} (Netcode said: {reason})");
        _ = Leave();
    }

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
        joinedCode = code;
        session = await MultiplayerService.Instance.JoinSessionByCodeAsync(code.Trim().ToUpperInvariant());
    });

    public string SessionCode => session?.Code;

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
        joinedAddress = host;
        joinedCode = null;
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
        ChemGrid.BackToOwnMap();
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    void PrepareForNetwork()
    {
        joinFailure = null;
        Net.NetworkConfig.ConnectionData = BuildVersion.Encode(BuildInfo.Stamp, MapIdentity.Offered(ChemGrid.Folders));
        var solo = WizardRig.FindLocal();
        if (solo != null && solo.GetComponent<NetworkObject>() == null) Destroy(solo);
    }

    void Approve(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
    {
        bool self = request.ClientNetworkId == NetworkManager.ServerClientId;
        var client = self ? null : BuildVersion.Decode(request.Payload);
        string refusal = self ? null : BuildVersion.Mismatch(BuildInfo.Stamp, client);
        if (refusal == null && !self) refusal = ChemGrid.MapRefusal(client.Maps);
        if (refusal == null && Net.ConnectedClientsIds.Count >= MaxPlayers) refusal = $"The yard is full ({MaxPlayers} players).";
        if (refusal != null) Debug.Log($"NET: turned client {request.ClientNetworkId} away: {refusal}");
        response.Approved = refusal == null;
        response.CreatePlayerObject = refusal == null;
        response.Reason = refusal;
    }

    void OnEnable()
    {
        codePage ??= new PauseMenu.Page
        {
            Title = "Join by code",
            Draw = DrawCodePage,
            Move = delta => { picker.Step(-delta); return true; },
            Submit = SubmitPicked,
            Back = () => picker.Back(),
        };
        PauseMenu.AddSection(MenuSection, 0, PauseMenu.Submenu.Multiplayer);
        PauseMenu.AddPage(CodePage, codePage);
    }

    void OnDisable()
    {
        PauseMenu.RemoveSection(MenuSection);
        PauseMenu.RemovePage(CodePage);
    }

    bool SubmitPicked()
    {
        if (picker.Next()) JoinPicked();
        return true;
    }

    public void PickCode()
    {
        string clip = GUIUtility.systemCopyBuffer;
        picker.Begin(CodePicker.LooksLikeCode(joinCode) ? joinCode : clip);
        PauseMenu.Instance.ShowPage(CodePage);
    }

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

    const string JoinField = "join-code";

    void OfflineControls()
    {
        GUI.enabled = !busy;
        if (PauseMenu.Button($"Rule changes when you host: {RoomPolicy.Words(RoomMode)}", PauseMenu.ButtonStyle)) RoomMode = (ChangeMode)(((int)RoomMode + 1) % 3);
        GUILayout.Label($"<color=#ffffff99>{ModeHint(RoomMode)}</color>", label);
        MapPicker();
        if (PauseMenu.Button("Host online", PauseMenu.ButtonStyle)) HostPicked();
        GUILayout.BeginHorizontal();
        GUI.SetNextControlName(JoinField);
        joinCode = GUILayout.TextField(joinCode, 12, field, GUILayout.Width(170));
        if (GUI.GetNameOfFocusedControl() == JoinField) PauseMenu.FieldHasKeys();
        if (PauseMenu.Button("Join code", null, GUILayout.Height(32)) && joinCode.Trim().Length > 0) _ = JoinOnline(joinCode);
        GUILayout.EndHorizontal();
        if (Gamepad.current != null && PauseMenu.Button("Enter a code with the controller", null, GUILayout.Height(32))) PickCode();
        GUI.enabled = true;
    }

    (string id, string name)[] maps;

    void MapPicker()
    {
        maps ??= ChemGrid.Maps();
        if (maps.Length < 2) return;
        string current = HostMap ?? ChemGrid.MapId;
        int at = System.Array.FindIndex(maps, m => m.id == current);
        string name = at >= 0 ? maps[at].name : current;
        if (PauseMenu.Button($"Map when you host: {name}", PauseMenu.ButtonStyle))
        {
            HostMap = maps[(at + 1) % maps.Length].id;
            SpawnPoints.StartIn = null;
        }
        StartPicker(HostMap ?? ChemGrid.MapId);
    }

    static void StartPicker(string map)
    {
        var spawns = SpawnPoints.OfMap(map);
        if (spawns.Count < 2) return;
        int at = spawns.FindIndex(s => s.Id == SpawnPoints.StartIn);
        if (at < 0) at = 0;
        if (PauseMenu.Button($"Start in: {spawns[at].Name}", PauseMenu.ButtonStyle)) SpawnPoints.StartIn = spawns[(at + 1) % spawns.Count].Id;
    }

    static string ModeHint(ChangeMode mode) => mode switch
    {
        ChangeMode.HostOnly => "Only you can change the rules once it's open.",
        ChangeMode.Locked => "No one can change the rules once it's open.",
        _ => "Anyone can change the rules for everyone, and anyone can undo it.",
    };

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
