using UnityEngine;

// The Esc menu's Play with Claude page: how to connect Claude to this game, what it does
// then, and the switch that lets it in (RulesMcpServer, J-047). RulesMcpServer adds it, so a
// copy that doesn't serve (a virtual player) has no page.
public class ClaudeMenu : MonoBehaviour
{
    /// <summary>The page's id in the Esc menu.</summary>
    public const string PageId = "claude";
    const int SectionOrder = 492; // after Controls, before the frame-rate log and Mods
    const int Width = 640;
    const float MostHeight = 620f;

    const string NeedIt = "<b>1.</b> You need Claude Code, installed once: claude.com/claude-code";
    const string RunIt = "<b>2.</b> Run this once in a terminal:";
    const string AskIt = "<b>3.</b> Start a new Claude session, which picks up the game then. Keep the game running, and ask:";
    const string Asks = "<color=#ffffffbb>\"make fireballs bigger\"\n\"make grass burn twice as fast\"\n\"when lightning hits water, leave fog\"</color>";
    const string WhatHappens = "<b>What happens:</b> Claude writes a mod, the game checks it, and a line on screen says who changed what. " +
        "To undo it, Esc, then Mods. Who can change a room depends on its mode: Open, Host only or Locked.";
    const string TheSwitch = "<color=#ffffff99>While it's on, programs on this PC can change your game. Web pages can't.</color>";

    PauseMenu.Page page;
    GUIStyle text, line;
    Vector2 scroll;

    void OnEnable()
    {
        page ??= new PauseMenu.Page { Title = "Play with Claude", Width = Width, Draw = DrawPage };
        PauseMenu.AddSection(DrawEntry, SectionOrder);
        PauseMenu.AddPage(PageId, page);
    }

    void OnDisable()
    {
        PauseMenu.RemoveSection(DrawEntry);
        PauseMenu.RemovePage(PageId);
    }

    void DrawEntry()
    {
        if (PauseMenu.Button("Play with Claude", PauseMenu.ButtonStyle)) PauseMenu.Instance.ShowPage(PageId);
    }

    void DrawPage()
    {
        MakeStyles();
        var server = RulesMcpServer.Instance;
        string add = RulesMcpServer.AddLine;
        scroll = PauseMenu.ScrollView(scroll, Mathf.Min(MostHeight, HudScale.Height - 200f), () =>
        {
            GUILayout.Label(NeedIt, text);
            GUILayout.Label(RunIt, text);
            GUILayout.BeginHorizontal();
            GUILayout.Label(add, line);
            if (PauseMenu.Button("Copy", null, GUILayout.Width(70), GUILayout.Height(32))) GUIUtility.systemCopyBuffer = add;
            GUILayout.EndHorizontal();
            GUILayout.Label(AskIt, text);
            GUILayout.Label(Asks, text);
            GUILayout.Label(WhatHappens, text);
            if (PauseMenu.Button($"Claude can change this game: {(server.Serving ? "On" : "Off")}", PauseMenu.ButtonStyle)) server.Serving = !server.Serving;
            GUILayout.Label(TheSwitch, text);
        });
    }

    void MakeStyles()
    {
        if (text != null) return;
        text = new GUIStyle(GUI.skin.label) { fontSize = 17, richText = true, wordWrap = true };
        line = new GUIStyle(text) { fontSize = 13 };
    }
}
