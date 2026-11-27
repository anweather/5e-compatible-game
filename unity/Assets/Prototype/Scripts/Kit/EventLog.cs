using System;
using System.Collections.Generic;
using UnityEngine;

// A short "what happened" log on screen, like the web sandbox's, and whether the HUD shows at all (HudVisible).
// The HUD for the wizard (its vitals, the cast HUD, the crosshair, the spell chip, and the Reset key) is the log's
// add-on, WizardHud ([GridAddOn] at GridStage.Hud, added in Awake): the log ticks it before its own frame and draws
// it under the log, through ILogHud, so the log never names the wizard. The wizard the HUD follows, and its controls,
// are TrackedWizard and TrackedControls for what sits below the wizard (the pause menu, the row houses).
public interface ILogHud
{
    Component Wizard { get; }

    PlayerControls Controls { get; }

    void Tick();

    void Draw(GUIStyle style);
}

public class EventLog : MonoBehaviour
{
    static EventLog instance;

    public static bool HudVisible { get; set; } = true;

    public const float HudMargin = 24f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        HudVisible = true;
    }

    readonly List<string> lines = new List<string>();
    GUIStyle style;
    ILogHud hud;
    GUIStyle logStyle, logShadow;
    const float LogWidth = 640f, LogLine = 22f, LogBottom = 56f;

    void Awake()
    {
        instance = this;
        GridAddOns.Add(gameObject, GridStage.Hud);
        TryGetComponent(out hud);
    }

    void OnDestroy() { if (instance == this) instance = null; }

    public static Component TrackedWizard => instance != null && instance.hud != null ? instance.hud.Wizard : null;

    public static PlayerControls TrackedControls => instance != null && instance.hud != null ? instance.hud.Controls : null;

    const string CastLine = "You cast ";

    public static void Add(string line, string sameAs = null)
    {
        Debug.Log(line);
        if (instance == null || line.StartsWith(CastLine, StringComparison.Ordinal)) return;
        var list = instance.lines;
        if (list.Count > 0 && list[list.Count - 1].StartsWith(sameAs ?? line, StringComparison.Ordinal))
        {
            list[list.Count - 1] = line;
            return;
        }
        list.Add(line);
        if (list.Count > 8) list.RemoveAt(0);
    }

    void Update()
    {
        if (hud != null) hud.Tick();
    }

    void OnGUI()
    {
        if (!HudVisible) return;
        HudScale.Apply();
        style ??= new GUIStyle(GUI.skin.label) { fontSize = 18, richText = true };
        if (hud != null) hud.Draw(style);
        DrawLog();
    }

    void DrawLog()
    {
        logStyle ??= new GUIStyle(style) { fontSize = 16, alignment = TextAnchor.MiddleRight };
        logShadow ??= new GUIStyle(logStyle) { richText = false };
        logShadow.normal.textColor = new Color(0f, 0f, 0f, 0.75f);
        float width = LogWidth, x = HudScale.Width - width - HudMargin;
        for (int i = 0; i < lines.Count; i++)
        {
            var at = new Rect(x, HudScale.Height - LogBottom - (lines.Count - i) * LogLine, width, LogLine);
            GUI.Label(new Rect(at.x + 1f, at.y + 1f, at.width, at.height), System.Text.RegularExpressions.Regex.Replace(lines[i], "<[^>]+>", ""), logShadow);
            GUI.Label(at, lines[i], logStyle);
        }
    }
}
