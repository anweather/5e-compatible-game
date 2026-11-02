using System.Collections.Generic;
using Chem.Mcp;
using UnityEngine;
using UnityEngine.InputSystem;

// The chat box in the game (J-060): Enter opens it, or holding to talk does (GameVoice, J-061, the
// only way in from a pad); what's typed or said goes to the player's own Claude Code, one process a message (ClaudeChat), and what comes back
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
    /// <summary>Where a message came from, for the record.</summary>
    public const string FromKeys = "typed", FromVoice = "voice";
    const float BarHeight = 5f, SayAfter = 12f;
    static readonly Color BarColour = new(1f, 0.75f, 0.35f);
    const string ListeningMark = "<color=#ff8a80>●</color> ";
    const string SignInLine = "You aren't signed in to Claude. A terminal is opening so you can: follow it, then ask again.";

    readonly ClaudeChat claude = new();
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

    /// <summary>The chat, or null where the game doesn't serve Claude.</summary>
    public static GameChat Instance { get; private set; }

    /// <summary>Whether the box is open, taking the keys.</summary>
    public bool Open { get; private set; }

    /// <summary>Whether the box is being filled by voice: it shows the words and takes no keys.</summary>
    public bool Talking { get; set; }

    /// <summary>What the box says until words come, while Talking.</summary>
    public string TalkHint { get; set; } = "";

    /// <summary>What's listening, by name, shown under the box while Talking; null shows nothing.</summary>
    public string TalkSource { get; set; }

    /// <summary>How loud the voice coming in is, 0 to 1, shown under the box while Talking.</summary>
    public float Level { get; set; }

    /// <summary>Whether the box could open now.</summary>
    public bool CanOpen => !Open && !Blocked();

    /// <summary>What's in the box, for the probe.</summary>
    public string Typed => typed;

    /// <summary>What the chat is showing, oldest first, for the probe.</summary>
    public IEnumerable<string> Lines
    {
        get
        {
            foreach (var (text, _) in lines) yield return text;
        }
    }

    /// <summary>Whether Claude is still answering, or messages wait for it.</summary>
    public bool Busy => claude.Busy || waiting.Count > 0;

    /// <summary>The one line about messages waiting for Claude, or null with none.</summary>
    public string Queued => waiting.Count == 0 ? null
        : $"{(turnedAway ? "That's too many, so the last wasn't sent. " : "")}Waiting ({waiting.Count}): {waiting.Peek().message}";

    /// <summary>Whether each tool Claude uses shows in the chat: debugging, in the Developer menu. Otherwise a bar shows how far it's got.</summary>
    public static bool ShowTools
    {
        get => PlayerPrefs.GetInt(ToolsKey, 0) == 1;
        set
        {
            PlayerPrefs.SetInt(ToolsKey, value ? 1 : 0);
            PlayerPrefs.Save();
        }
    }

    /// <summary>What the progress bar says now, 0 to 1, for the probe; null with no prompt going.</summary>
    public (float done, string what)? Progress { get; private set; }

    void Awake()
    {
        Instance = this;
        // -claude-stand-in runs this program instead of claude, to test a player without anyone's Claude.
        var args = System.Environment.GetCommandLineArgs();
        int asked = System.Array.IndexOf(args, "-claude-stand-in");
        if (asked >= 0 && asked + 1 < args.Length) ClaudeChat.StandIn = args[asked + 1];
        // -chat-say sends these messages, split on |, once the game is up, to test a player with nobody at its keys.
        int say = System.Array.IndexOf(args, "-chat-say");
        if (say >= 0 && say + 1 < args.Length) StartCoroutine(Says(args[say + 1].Split('|')));
        QuickExit.BeforeExit += claude.Cancel; // some players have no OnApplicationQuit
    }

    System.Collections.IEnumerator Says(string[] messages)
    {
        yield return new WaitForSecondsRealtime(SayAfter);
        foreach (string message in messages)
        {
            Send(message);
            yield return new WaitUntil(() => !Busy);
        }
    }

    void OnEnable() => PauseMenu.AddSection(DrawDeveloper, PauseMenu.LastOrder, PauseMenu.Submenu.Developer);

    void OnDisable() => PauseMenu.RemoveSection(DrawDeveloper);

    void DrawDeveloper()
    {
        if (PauseMenu.Button($"Claude's tools in the chat: {(ShowTools ? "On" : "Off")}", PauseMenu.ButtonStyle)) ShowTools = !ShowTools;
    }

    void OnDestroy()
    {
        QuickExit.BeforeExit -= claude.Cancel;
        claude.Cancel();
        PauseMenu.KeysTaken = false;
        if (Instance == this) Instance = null;
    }

    void Update()
    {
        if (!Open) PauseMenu.KeysTaken = false; // a frame late, so the key that closed the box can't open the menu
        while (claude.Take(out var heard)) Show(heard.said, heard.text);
        Progress = ChatBar.Of(ChatTelemetry.Instance != null ? ChatTelemetry.Instance.Current : null);
        if (!claude.Busy && waiting.Count > 0)
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

    // Here and not in OnGUI, which can see a key a frame before the Input System does and so reopen the box.
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
                waiting.Clear(); // they'd only fail the same way
                turnedAway = false;
                ClaudeTerminal.OpenSignIn();
                break;
            case ClaudeSaid.Failed:
                Say("<color=#ff8a80>" + text + "</color>");
                break;
        }
    }

    /// <summary>A line in the chat.</summary>
    public void Say(string text)
    {
        lines.Add((text, Time.unscaledTime));
        if (lines.Count > MostLines) lines.RemoveAt(0);
    }

    /// <summary>Sends a message to Claude, as Enter does in the box; it waits its turn if Claude is answering.</summary>
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
        claude.Send(message, ClaudeTerminal.Folder, RulesMcpServer.Url, telemetry.Begin(message, source), () => telemetry.Now);
    }

    /// <summary>
    /// The sentence refusing a save, on the server's thread: only while a chat message is running, and it didn't ask.
    /// A terminal's saves stay the player's, who answers Claude's own prompt there. Nothing else in the chat stops one (J-064).
    /// </summary>
    public static string SaveRefusal()
    {
        var chat = Instance;
        return chat != null && chat.claude.Busy && !chat.saveAsked ? ClaudeRun.SaveNotAsked : null;
    }

    /// <summary>Fills the box, as typing does; voice comes this way too.</summary>
    public void Type(string text) => typed = text;

    /// <summary>Opens or closes the box; the wizard's controls are off while it's open, and the game plays on.</summary>
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
            e.Use(); // BoxKeys acts on them
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

    // The newest at the bottom, each fading out on its own unless the box is open.
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

    // How far Claude has got: what it's on, and a bar under it.
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
        // A dark band behind each line, so it reads over snow and fire alike.
        GUI.color = new Color(0f, 0f, 0f, ShadeAlpha * fade);
        shade ??= Texture2D.whiteTexture;
        GUI.DrawTexture(new Rect(x - ShadeEdge, y, Width + 2f * ShadeEdge, high), shade);
        GUI.color = new Color(colour.r, colour.g, colour.b, colour.a * fade);
        GUI.Label(new Rect(x, y, Width, high), text, lineStyle);
        return y;
    }
}
