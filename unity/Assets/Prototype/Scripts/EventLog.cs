using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

// On-screen help and a short "what happened" log, like the web sandbox's.
// Shows the spell bar when the scene has a SpellCaster. R reloads the scene solo,
// and online resets the world for everyone (the host) or asks the host to (OnlineReset).
public class EventLog : MonoBehaviour
{
    static EventLog instance;

    public static bool HudVisible { get; set; } = true;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        HudVisible = true;
    }

    readonly List<string> lines = new List<string>();
    GUIStyle style;
    SpellCaster caster;
    readonly VitalsHud vitalsHud = new();
    readonly CastHud castHud = new();
    GUIStyle logStyle, logShadow, chipStyle;
    const float LogWidth = 640f, LogLine = 22f, LogBottom = 56f;

    void Awake() => instance = this;
    void Start()
    {
        if (caster == null) caster = FindAnyObjectByType<SpellCaster>();
    }

    public static SpellCaster TrackedCaster => instance != null ? instance.caster : null;

    public static float CastBarShare => instance != null ? instance.castHud.Share : 0f;

    public static CastHud CastShown => instance != null ? instance.castHud : null;

    public static void Track(SpellCaster c)
    {
        if (instance != null) instance.caster = c;
    }
    void OnDestroy() { if (instance == this) instance = null; }

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
        var controls = caster != null ? caster.controls : null;
        bool reset = controls != null ? controls.Reset.WasPressedThisFrame() : Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame;
        bool held = controls != null ? controls.Reset.IsPressed() : Keyboard.current != null && Keyboard.current.rKey.isPressed;
        if (reset)
        {
            resetHeld = 0f;
            if (!ResetYard()) Add("Keep holding Reset to rebuild the valley at once");
        }
        if (!held)
        {
            resetHeld = -1f;
            return;
        }
        if (resetHeld < 0f) return;
        resetHeld += Time.unscaledDeltaTime;
        if (resetHeld < RebuildHoldSeconds) return;
        resetHeld = -1f;
        RebuildYard();
    }

    const float RebuildHoldSeconds = 3f;
    float resetHeld = -1f;

    public static bool ResetYard()
    {
        if (OnlineReset.Summon()) return false;
        var grid = ChemGrid.Current?.Grid;
        if (grid == null || grid.valley == null || grid.World == null || !grid.World.HasStart) return RebuildYard();
        if (PlaytestBot.Active)
        {
            Debug.Log("PLAYTEST: reset ignored while the bot runs");
            return false;
        }
        grid.SummonDragon();
        return false;
    }

    public static bool RebuildYard()
    {
        if (OnlineReset.Ask()) return false;
        if (PlaytestBot.Active)
        {
            Debug.Log("PLAYTEST: reset ignored while the bot runs");
            return false;
        }
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        return true;
    }

    void Crosshair()
    {
        var c = new Vector2(HudScale.Width / 2f, HudScale.Height / 2f);
        var cross = new GUIStyle(style) { alignment = TextAnchor.MiddleCenter, fontSize = 28 };
        GUI.Label(new Rect(c.x - 11, c.y - 14, 24, 30), "<color=#000000aa><b>+</b></color>", cross);
        GUI.Label(new Rect(c.x - 12, c.y - 15, 24, 30), "<b>+</b>", cross);
    }

    void OnGUI()
    {
        if (!HudVisible) return;
        HudScale.Apply();
        style ??= new GUIStyle(GUI.skin.label) { fontSize = 18, richText = true };
        if (caster != null) DrawCasterHud();
        DrawLog();
    }

    void DrawLog()
    {
        logStyle ??= new GUIStyle(style) { fontSize = 16, alignment = TextAnchor.MiddleRight };
        logShadow ??= new GUIStyle(logStyle) { richText = false };
        logShadow.normal.textColor = new Color(0f, 0f, 0f, 0.75f);
        float width = LogWidth, x = HudScale.Width - width - VitalsHud.Margin;
        for (int i = 0; i < lines.Count; i++)
        {
            var at = new Rect(x, HudScale.Height - LogBottom - (lines.Count - i) * LogLine, width, LogLine);
            GUI.Label(new Rect(at.x + 1f, at.y + 1f, at.width, at.height), System.Text.RegularExpressions.Regex.Replace(lines[i], "<[^>]+>", ""), logShadow);
            GUI.Label(at, lines[i], logStyle);
        }
    }

    void DrawCasterHud()
    {
        bool pad = caster.controls != null && caster.controls.UsingGamepad;
        float vitalsHeight = vitalsHud.Draw(caster.gameObject, style);
        if (Cursor.lockState == CursorLockMode.Locked || pad) Crosshair();
        castHud.Draw(caster, style);
        bool hands = caster.controls != null && caster.controls.HandPad;
        chipStyle ??= new GUIStyle(style) { fontSize = 15 };
        if (!hands) GUI.Label(new Rect(VitalsHud.Margin, VitalsHud.Margin + vitalsHeight + 6f, 1400, 26), SpellChip(pad), chipStyle);
    }

    public const string KeyHints = "Tab: spells  ·  Q element  ·  E spell  ·  1-8 an element's spells  ·  F2 the hands";

    string SpellChip(bool pad)
    {
        var group = caster.TryGetComponent<SpellRadialHud>(out var radial) ? radial.CurrentGroup : null;
        string colour = group?.Color ?? "#ffd24a";
        string element = group != null ? $"<color={colour}>{group.Name}</color>  " : "";
        string keys = pad ? PadGlyphs.Text("hold {LB}: spells, tap: swap  ·  {RB} next  ·  D-pad ◀ ▶ element") : KeyHints;
        return $"<color={colour}>●</color> {element}<color=#ffd24a><b>{caster.SelectedName}</b></color>   <color=#ffffff88>{keys}</color>";
    }
}
