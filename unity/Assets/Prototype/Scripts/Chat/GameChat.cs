using System.Collections.Generic;
using Chem.Mcp;
using UnityEngine;
using UnityEngine.InputSystem;

// The chat box in the game (J-060): Enter opens it, or holding to talk does (GameVoice, J-061, the
// only way in from a pad); what's typed or said goes to the player's own Claude Code, one process a message (ClaudeChat), or with
// their own API key to the game's own loop (ApiChat, J-071; ChatSetup says which), and what comes back
// fades up over the game while they carry on playing. Messages sent while Claude is answering wait
// in a short line, shown as one status line. The wizard's controls are off only while the
// box is open. It talks only to this game's rules MCP, and a room's change mode decides what
// goes through, as ever. RulesMcpServer adds it, so a copy that doesn't serve has none.
public class GameChat : MonoBehaviour
{
    const int MostLines = 7, MostWaiting = 3, Width = 640, FontSize = 17;
    const int SourceFontSize = 13;
    const float LevelHeight = 3f, SourceHeight = 20f, LineSeconds = 14f, FadeSeconds = 2f, BoxHeight = 34f, BottomGap = 90f, ShadeAlpha = 0.6f, ShadeEdge = 8f;
    const string Box = "game-chat-box", ToolsKey = "chat.show-tools";
    public const string ModelKey = "chat.account-model";
    public const string FromKeys = "typed", FromVoice = "voice";
    const float BarHeight = 5f, SayAfter = 12f;
    static readonly Color BarColour = new(1f, 0.75f, 0.35f);
    const string ListeningMark = "<color=#ff8a80>●</color> ";
    const string SignInLine = "You aren't signed in to Claude. A terminal is opening so you can: follow it, then ask again.";

    readonly List<(string text, float at)> lines = new();
    readonly Queue<(string message, string source)> waiting = new();
    string typed = "";
    bool focusBox, turnedAway;
    volatile bool saveAsked;
    PlayerControls pausedControls;
    PlayerInput pausedInput;
    CursorLockMode cursorWas;
    bool cursorVisibleWas;
    GUIStyle lineStyle, boxStyle, talkStyle, sourceStyle;
    Texture2D shade;

    public static System.Func<IChatWire> ApiWireMaker { get; set; } = () => new ApiWire();

    [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        ApiWireMaker = () => new ApiWire();
        Instance = null;
    }

    const string PrefixTtl = "1h";

    readonly ClaudeChat claudeCode = new();
    ApiChat api;
    IChatHarness harness, pinned;

    public IChatHarness Harness
    {
        get => harness ??= claudeCode;
        set => harness = pinned = value;
    }

    public IChatHarness ChosenHarness
    {
        get
        {
            if (pinned != null) return pinned;
            if (!ChatSetup.OnApi) return claudeCode;
            string model = ApiKey.Model;
            if (api == null || api.Model != model) api = new ApiChat(ApiWireMaker(), ApiKey.Session, model, PrefixTtl);
            return api;
        }
    }

    public static GameChat Instance { get; private set; }

    public bool Open { get; private set; }

    public bool Talking { get; set; }

    public string TalkHint { get; set; } = "";

    public string TalkSource { get; set; }

    public float Level { get; set; }

    public bool CanOpen => !Open && !Blocked();

    public string Typed => typed;

    public IEnumerable<string> Lines
    {
        get
        {
            foreach (var (text, _) in lines) yield return text;
        }
    }

    public bool Busy => Harness.Busy || waiting.Count > 0;

    public string Queued => waiting.Count == 0 ? null
        : $"{(turnedAway ? "That's too many, so the last wasn't sent. " : "")}Waiting ({waiting.Count}): {waiting.Peek().message}";

    public static bool ShowTools
    {
        get => PlayerPrefs.GetInt(ToolsKey, 0) == 1;
        set
        {
            PlayerPrefs.SetInt(ToolsKey, value ? 1 : 0);
            PlayerPrefs.Save();
        }
    }

    public static bool AccountModel
    {
        get => PlayerPrefs.GetInt(ModelKey, 0) == 1;
        set
        {
            PlayerPrefs.SetInt(ModelKey, value ? 1 : 0);
            PlayerPrefs.Save();
        }
    }

    public (float done, string what)? Progress { get; private set; }

    void Awake()
    {
        Instance = this;
        var args = System.Environment.GetCommandLineArgs();
        int asked = System.Array.IndexOf(args, "-claude-stand-in");
        if (asked >= 0 && asked + 1 < args.Length) ClaudeChat.StandIn = args[asked + 1];
        if (System.Array.IndexOf(args, "-api-stand-in") >= 0) StandInForTheApi();
        int say = System.Array.IndexOf(args, "-chat-say");
        if (say >= 0 && say + 1 < args.Length) StartCoroutine(Says(args[say + 1].Split('|')));
        QuickExit.BeforeExit += CancelNow;
    }

    static void StandInForTheApi()
    {
        string folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "spell-valley-api-stand-in");
        ApiKey.TestFile = System.IO.Path.Combine(folder, "api-key.dat");
        ChatSetup.TestFile = System.IO.Path.Combine(folder, "chat-setup.dat");
        ApiWireMaker = () => new ApiStandIn();
        ApiKey.Save(ApiStandIn.GoodKey);
    }

    System.Collections.IEnumerator Says(string[] messages)
    {
        yield return new WaitForSecondsRealtime(SayAfter);
        foreach (string message in messages)
        {
            Send(message);
            yield return new WaitUntil(() => !Busy);
        }
        yield return null;
        Debug.Log($"Chat said, through {Harness.GetType().Name}: {string.Join(" | ", Lines)}");
    }

    void OnEnable() => PauseMenu.AddSection(DrawDeveloper, PauseMenu.LastOrder, PauseMenu.Submenu.Developer);

    void OnDisable() => PauseMenu.RemoveSection(DrawDeveloper);

    void DrawDeveloper()
    {
        if (PauseMenu.Button($"Claude's tools in the chat: {(ShowTools ? "On" : "Off")}", PauseMenu.ButtonStyle)) ShowTools = !ShowTools;
        if (PauseMenu.Button($"Claude's model in the chat: {(AccountModel ? "Account default" : "Haiku")}", PauseMenu.ButtonStyle)) AccountModel = !AccountModel;
    }

    void CancelNow() => Harness.Cancel();

    void OnDestroy()
    {
        QuickExit.BeforeExit -= CancelNow;
        CancelNow();
        PauseMenu.KeysTaken = false;
        if (Instance == this) Instance = null;
    }

    void Update()
    {
        if (!Open) PauseMenu.KeysTaken = false;
        while (Harness.Take(out var heard)) Show(heard.said, heard.text);
        Progress = ChatBar.Of(ChatTelemetry.Instance != null ? ChatTelemetry.Instance.Current : null);
        if (!Harness.Busy && waiting.Count > 0)
        {
            turnedAway = false;
            var next = waiting.Dequeue();
            SendNow(next.message, next.source);
        }
        if (Open && Blocked()) SetOpen(false);
        if (Open)
        {
            BoxKeys();
            return;
        }
        if (!Blocked() && Keyboard.current != null && Keyboard.current.enterKey.wasPressedThisFrame) SetOpen(true);
    }

    void BoxKeys()
    {
        var keys = Keyboard.current;
        if (keys == null) return;
        if (keys.escapeKey.wasPressedThisFrame)
        {
            SetOpen(false);
            return;
        }
        if (Talking || !(keys.enterKey.wasPressedThisFrame || keys.numpadEnterKey.wasPressedThisFrame)) return;
        string message = typed;
        SetOpen(false);
        Send(message);
    }

    static bool Blocked() => PauseMenu.Instance != null && PauseMenu.Instance.Open;

    void Show(ClaudeSaid said, string text)
    {
        if (said is ClaudeSaid.Done or ClaudeSaid.Failed && ChatTelemetry.Instance != null) ChatTelemetry.Instance.ProcessEnded();
        switch (said)
        {
            case ClaudeSaid.Words:
                Say("Claude: " + text);
                break;
            case ClaudeSaid.Tool:
                if (ShowTools) Say($"<color=#ffffff88>{text}...</color>");
                break;
            case ClaudeSaid.Failed when ClaudeRun.NeedsSignIn(text):
                Say(SignInLine);
                waiting.Clear();
                turnedAway = false;
                ClaudeTerminal.OpenSignIn();
                break;
            case ClaudeSaid.Failed:
                Say("<color=#ff8a80>" + text + "</color>");
                break;
        }
    }

    public void Say(string text)
    {
        lines.Add((text, Time.unscaledTime));
        if (lines.Count > MostLines) lines.RemoveAt(0);
    }

    public void Send(string message, string source = FromKeys)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        if (Busy && waiting.Count >= MostWaiting) turnedAway = true;
        else if (Busy) waiting.Enqueue((message, source));
        else SendNow(message, source);
    }

    void SendNow(string message, string source)
    {
        Say("You: " + message);
        var server = RulesMcpServer.Instance;
        if (!server.Serving || RulesMcpServer.Url == null) server.Serving = true;
        if (RulesMcpServer.Url == null)
        {
            Say("No free port for Claude to reach the game.");
            return;
        }
        var telemetry = ChatTelemetry.Instance;
        saveAsked = ClaudeRun.AsksToSave(message);
        harness = ChosenHarness;
        Harness.Send(message, ChemGrid.Folders.Claude.Path, RulesMcpServer.Url, telemetry.Begin(message, source), () => telemetry.Now);
    }

    public static string SaveRefusal()
    {
        var chat = Instance;
        return chat != null && chat.Harness.Busy && !chat.saveAsked ? ClaudeRun.SaveNotAsked : null;
    }

    public void Type(string text) => typed = text;

    public void SetOpen(bool open)
    {
        if (open == Open) return;
        Open = open;
        Talking = false;
        var caster = EventLog.TrackedCaster;
        if (open)
        {
            PauseMenu.KeysTaken = true;
            focusBox = true;
            typed = "";
            if (caster != null && caster.controls != null && caster.controls.enabled)
            {
                pausedControls = caster.controls;
                pausedControls.enabled = false;
            }
            if (caster != null && caster.TryGetComponent<PlayerInput>(out var input) && input.enabled)
            {
                pausedInput = input;
                pausedInput.enabled = false;
            }
            cursorWas = Cursor.lockState;
            cursorVisibleWas = Cursor.visible;
            return;
        }
        if (pausedControls != null) pausedControls.enabled = true;
        if (pausedInput != null) pausedInput.enabled = true;
        pausedControls = null;
        pausedInput = null;
        Cursor.lockState = cursorWas;
        Cursor.visible = cursorVisibleWas;
        GUIUtility.keyboardControl = 0;
    }

    void OnGUI()
    {
        if (lines.Count == 0 && !Open && waiting.Count == 0 && Progress == null) return;
        HudScale.Apply();
        lineStyle ??= new GUIStyle(GUI.skin.label) { fontSize = FontSize, richText = true, wordWrap = true };
        boxStyle ??= new GUIStyle(GUI.skin.textField) { fontSize = FontSize, alignment = TextAnchor.MiddleLeft };
        float x = (HudScale.Width - Width) / 2f, y = HudScale.Height - BottomGap - BoxHeight;
        if (Open) DrawBox(x, y);
        DrawLines(x, y);
    }

    void DrawBox(float x, float y)
    {
        var e = Event.current;
        if (e.type == EventType.KeyDown && e.keyCode is KeyCode.Return or KeyCode.KeypadEnter or KeyCode.Escape)
        {
            e.Use();
            return;
        }
        if (Talking)
        {
            talkStyle ??= new GUIStyle(boxStyle) { richText = true };
            GUI.Label(new Rect(x, y, Width, BoxHeight), ListeningMark + (typed.Length > 0 ? typed : $"<color=#ffffff88>{TalkHint}</color>"), talkStyle);
            if (Level > 0f) GUI.DrawTexture(new Rect(x, y + BoxHeight, Width * Level, LevelHeight), Texture2D.whiteTexture);
            sourceStyle ??= new GUIStyle(lineStyle) { fontSize = SourceFontSize };
            if (TalkSource != null) GUI.Label(new Rect(x, y + BoxHeight + LevelHeight, Width, SourceHeight), "Listening on: " + TalkSource, sourceStyle);
            return;
        }
        GUI.SetNextControlName(Box);
        typed = GUI.TextField(new Rect(x, y, Width, BoxHeight), typed, boxStyle);
        if (!focusBox) return;
        GUI.FocusControl(Box);
        focusBox = false;
    }

    void DrawLines(float x, float y)
    {
        var colour = GUI.color;
        if (Progress is { } bar) y = DrawBar(bar.done, bar.what, x, y, colour);
        if (waiting.Count > 0) y = DrawLine($"<color=#ffffff88>{Queued}</color>", x, y, 1f, colour);
        for (int i = lines.Count - 1; i >= 0; i--)
        {
            float age = Time.unscaledTime - lines[i].at;
            float fade = Open ? 1f : Mathf.Clamp01((LineSeconds - age) / FadeSeconds);
            if (fade > 0f) y = DrawLine(lines[i].text, x, y, fade, colour);
        }
        GUI.color = colour;
    }

    float DrawBar(float done, string what, float x, float y, Color colour)
    {
        y -= BarHeight;
        GUI.color = new Color(0f, 0f, 0f, ShadeAlpha);
        GUI.DrawTexture(new Rect(x, y, Width, BarHeight), Texture2D.whiteTexture);
        GUI.color = BarColour;
        GUI.DrawTexture(new Rect(x, y, Width * done, BarHeight), Texture2D.whiteTexture);
        GUI.color = colour;
        return DrawLine($"<color=#ffffffcc>{what}</color>", x, y, 1f, colour);
    }

    float DrawLine(string text, float x, float y, float fade, Color colour)
    {
        float high = lineStyle.CalcHeight(new GUIContent(text), Width);
        y -= high;
        GUI.color = new Color(0f, 0f, 0f, ShadeAlpha * fade);
        shade ??= Texture2D.whiteTexture;
        GUI.DrawTexture(new Rect(x - ShadeEdge, y, Width + 2f * ShadeEdge, high), shade);
        GUI.color = new Color(colour.r, colour.g, colour.b, colour.a * fade);
        GUI.Label(new Rect(x, y, Width, high), text, lineStyle);
        return y;
    }
}
