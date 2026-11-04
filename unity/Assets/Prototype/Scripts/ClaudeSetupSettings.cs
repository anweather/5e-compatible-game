using System.IO;
using Chem.Mcp;
using UnityEngine;

// The Esc menu's Claude setup page (J-071): how the game reaches Claude, from ChatProfile's list. With an API key: a masked
// box, Check and Remove, the models that key can use, and the session's spend limit. With Claude Code: the install to
// run, Check, and the environment variables for its runs. A saved key or a variable's value never shows again. This
// page is the only thing that writes ChatSetup: that path runs a program on the player's PC. Starts itself.
public class ClaudeSetupSettings : MonoBehaviour
{
    /// <summary>The page's id in the Esc menu.</summary>
    public const string PageId = "claude-setup";
    const int SectionOrder = 510, MostChars = 400;
    const float PageHeight = 470f, CapStep = 0.5f;
    const char Mask = '*';
    const string KeyField = "setup-key", PathField = "setup-path", NameField = "setup-name", ValueField = "setup-value",
        Whose = "This key is yours. It stays on this PC, and what you use is billed to your Anthropic account.",
        Separate = "Make a separate key just for this game, with a low spend limit, so you can revoke it any time.",
        ConsoleLink = "https://console.anthropic.com/settings/keys",
        NoVault = "This computer has no safe place to keep these yet. Set the SPELL_VALLEY_API_KEY environment variable for the API, or the game uses claude from your PATH.";

    /// <summary>How many times the page has been drawn, for the probe.</summary>
    public static int Drawn { get; private set; }

    static ClaudeSetupSettings instance;
    readonly ApiKeyCheck keyCheck = new();
    readonly ClaudeCheck claudeCheck = new();
    PauseMenu.Page page;
    GUIStyle label;
    Vector2 scroll;
    string typed = "", says = "", path, variable = "", value = "";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        ApiKey.FindFile();
        ChatSetup.FindFile();
        if (instance != null) return;
        var go = new GameObject("Claude setup");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<ClaudeSetupSettings>();
    }

    void OnEnable()
    {
        page ??= new PauseMenu.Page { Title = "Claude setup", Width = 640, Draw = Draw };
        PauseMenu.AddSection(DrawEntry, SectionOrder, PauseMenu.Submenu.Settings);
        PauseMenu.AddPage(PageId, page);
    }

    void OnDisable()
    {
        PauseMenu.RemoveSection(DrawEntry);
        PauseMenu.RemovePage(PageId);
    }

    void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    void Update()
    {
        var checkedKey = keyCheck.Take();
        if (checkedKey != null) says = checkedKey.Says;
        says = claudeCheck.Take() ?? says;
    }

    void DrawEntry()
    {
        if (PauseMenu.Button("Claude setup", PauseMenu.ButtonStyle)) PauseMenu.Instance.ShowPage(PageId);
    }

    void Draw()
    {
        Drawn++;
        label ??= new GUIStyle(GUI.skin.label) { fontSize = 17, richText = false, wordWrap = true, alignment = TextAnchor.MiddleCenter };
        if (ApiKey.Vault == null)
        {
            GUILayout.Label(NoVault, label);
            return;
        }
        scroll = PauseMenu.ScrollView(scroll, PageHeight, DrawContent);
    }

    void DrawContent()
    {
        DrawPicker();
        GUILayout.Space(8);
        if (ChatSetup.Chosen == ChatProfile.Api) DrawApi();
        else DrawClaudeCode();
        GUILayout.Space(8);
        GUILayout.Label(keyCheck.Checking || claudeCheck.Checking ? "Checking..." : says, label);
    }

    void DrawPicker()
    {
        GUILayout.BeginHorizontal();
        foreach (var profile in ChatProfile.All)
            if (PauseMenu.Button(profile == ChatSetup.Chosen ? $"> {profile.Name} <" : profile.Name, PauseMenu.ButtonStyle) && profile != ChatSetup.Chosen)
            {
                Keep(() => ChatSetup.Choose(profile));
                says = "";
            }
        GUILayout.EndHorizontal();
    }

    void DrawApi()
    {
        GUILayout.Label(Whose, label);
        GUILayout.Label(Separate, label);
        if (PauseMenu.Button("Open the Anthropic Console", PauseMenu.ButtonStyle)) Application.OpenURL(ConsoleLink);
        GUILayout.Space(8);
        if (ApiKey.Saved) DrawSavedKey();
        else DrawKeyBox();
        GUILayout.Space(8);
        DrawCap();
    }

    void DrawKeyBox()
    {
        typed = Masked(KeyField, typed);
        if (!PauseMenu.Button("Check", PauseMenu.ButtonStyle) || keyCheck.Checking || string.IsNullOrWhiteSpace(typed)) return;
        keyCheck.Start(typed);
        typed = "";
        GUIUtility.keyboardControl = 0;
    }

    void DrawSavedKey()
    {
        GUILayout.Label("A key is saved on this PC.", label);
        GUILayout.BeginHorizontal();
        if (PauseMenu.Button("Check", PauseMenu.ButtonStyle) && !keyCheck.Checking) keyCheck.Start(null);
        if (PauseMenu.Button("Remove", PauseMenu.ButtonStyle))
        {
            ApiKey.Remove();
            says = "The key is gone from this PC.";
        }
        GUILayout.EndHorizontal();
        var models = ApiKey.Models;
        if (models.Length == 0) return;
        GUILayout.Label("The chat's model:", label);
        string chosen = ApiKey.Model;
        foreach (string model in models)
            if (PauseMenu.Button(model == chosen ? $"> {model} <" : model, PauseMenu.ButtonStyle)) ApiKey.Model = model;
    }

    void DrawCap()
    {
        GUILayout.Label($"Spend limit this session: ${ApiKey.Cap:0.00}. Spent so far: ${ApiKey.Session.Spent:0.00}.", label);
        GUILayout.BeginHorizontal();
        if (PauseMenu.Button("−", PauseMenu.ButtonStyle)) ApiKey.Cap -= CapStep;
        if (PauseMenu.Button("+", PauseMenu.ButtonStyle)) ApiKey.Cap += CapStep;
        GUILayout.EndHorizontal();
    }

    void DrawClaudeCode()
    {
        GUILayout.Label("The game runs your own Claude Code. Give a full path to use another install.", label);
        path ??= ChatSetup.ProgramPath;
        GUI.SetNextControlName(PathField);
        path = GUILayout.TextField(path, MostChars, GUILayout.Height(34));
        Typing(PathField);
        if (PauseMenu.Button("Check", PauseMenu.ButtonStyle) && !claudeCheck.Checking) claudeCheck.Start(path);
        GUILayout.Space(8);
        GUILayout.Label("Environment variables for its runs, such as CLAUDE_CONFIG_DIR for a separate account:", label);
        foreach (string name in ChatSetup.VariableNames)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(name, label);
            if (PauseMenu.Button("Remove", PauseMenu.ButtonStyle)) Keep(() => ChatSetup.RemoveVariable(name));
            GUILayout.EndHorizontal();
        }
        DrawNewVariable();
    }

    void DrawNewVariable()
    {
        GUILayout.BeginHorizontal();
        GUI.SetNextControlName(NameField);
        variable = GUILayout.TextField(variable, MostChars, GUILayout.Height(34));
        Typing(NameField);
        value = Masked(ValueField, value);
        GUILayout.EndHorizontal();
        if (!PauseMenu.Button("Add", PauseMenu.ButtonStyle)) return;
        string name = variable.Trim();
        if (!ChatProfile.IsVariableName(name))
        {
            says = "That can't be the name of a variable.";
            return;
        }
        string kept = value;
        Keep(() => ChatSetup.SetVariable(name, kept));
        variable = value = "";
        GUIUtility.keyboardControl = 0;
    }

    static string Masked(string field, string text)
    {
        GUI.SetNextControlName(field);
        string result = GUILayout.PasswordField(text, Mask, MostChars, GUILayout.Height(34));
        Typing(field);
        return result;
    }

    static void Typing(string field)
    {
        if (GUI.GetNameOfFocusedControl() == field) PauseMenu.FieldHasKeys();
    }

    void Keep(System.Action store)
    {
        try
        {
            store();
        }
        catch (System.Exception e) when (e is IOException or System.UnauthorizedAccessException)
        {
            says = "That couldn't be saved on this PC.";
        }
    }
}
