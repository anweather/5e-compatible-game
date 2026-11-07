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

    /// <summary>False hides all on-screen UI (help, spell bar, log, the online status line), e.g. for trailer recordings.</summary>
    public static bool HudVisible { get; set; } = true;

    // Starts over each Play mode, even with the domain reload off (Enter Play Mode Options).
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
    // Over the update notice in the corner (UpdateCheck).
    const float LogWidth = 640f, LogLine = 22f, LogBottom = 56f;

    void Awake() => instance = this;
    void Start()
    {
        if (caster == null) caster = FindAnyObjectByType<SpellCaster>();
    }

    /// <summary>Shows this caster's spells and controls; the local player's caster calls it when enabled.</summary>
    /// <summary>The local player's caster, or null (e.g. the mods panel pauses its controls).</summary>
    public static SpellCaster TrackedCaster => instance != null ? instance.caster : null;

    /// <summary>How full the cast bar was drawn last, 0 to 1, for the probe.</summary>
    public static float CastBarShare => instance != null ? instance.castHud.Share : 0f;

    /// <summary>The cast's HUD, the hands' icons and all, for the probe; null with no log.</summary>
    public static CastHud CastShown => instance != null ? instance.castHud : null;

    public static void Track(SpellCaster c)
    {
        if (instance != null) instance.caster = c;
    }
    void OnDestroy() { if (instance == this) instance = null; }

    // A line that starts with the same text as the last one replaces it, so
    // "The oil ignites (×3)" updates "The oil ignites (×2)" in place.
    public static void Add(string line, string sameAs = null)
    {
        Debug.Log(line);
        if (instance == null) return;
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

    // How long Reset is held to rebuild the valley at once, and how long it has been (below zero: not counting).
    const float RebuildHoldSeconds = 3f;
    float resetHeld = -1f;

    /// <summary>
    /// The Reset: in the valley the dragon comes and puts it back (BACKLOG 26), online for everyone if the host asks, and a
    /// client asks the host to (OnlineReset). In a scene with no valley it reloads the scene. True if it did.
    /// Held for a few seconds it's RebuildYard, for a world that has gone wrong and can't wait for the dragon.
    /// </summary>
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

    /// <summary>
    /// The way out of a world that has gone wrong, as Reset was before the dragon: solo, reloads the scene and returns true.
    /// Online the host rebuilds the world for everyone and a client asks the host to (OnlineReset); the scene isn't
    /// reloaded, so it returns false.
    /// </summary>
    public static bool RebuildYard()
    {
        // Reloading the scene online would drop this machine's world and everyone's wizards with it.
        if (OnlineReset.Ask()) return false;
        // Nor under a playtest run: a stray R or pad press on the bot's window reloaded the scene
        // and destroyed its wizard (seed 103, 2026-10-27).
        if (PlaytestBot.Active)
        {
            Debug.Log("PLAYTEST: reset ignored while the bot runs");
            return false;
        }
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        return true;
    }

    // A plus with a dark shadow, so it reads against the sky as well as the ground.
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

    // The message log down the free right side, the newest at the bottom.
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

    // The wizard's bars, the crosshair, and the spell chip: the selected spell in its element's
    // colour, with the keys that change it (the spell radial: SpellRadialHud). The rest of the
    // controls are on the menu's Controls page (ControlsMenu).
    void DrawCasterHud()
    {
        bool pad = caster.controls != null && caster.controls.UsingGamepad;
        float vitalsHeight = vitalsHud.Draw(caster.gameObject, style);
        if (Cursor.lockState == CursorLockMode.Locked || pad) Crosshair();
        castHud.Draw(caster, style);
        // The spell the radial picked: what Cast casts, only in the debug layout. With the hands (D16), in the world and the
        // Hold, the hands' icons say what goes off, so there's no spell selector.
        bool hands = caster.controls != null && caster.controls.HandPad;
        chipStyle ??= new GUIStyle(style) { fontSize = 15 };
        if (!hands) GUI.Label(new Rect(VitalsHud.Margin, VitalsHud.Margin + vitalsHeight + 6f, 1400, 26), SpellChip(pad), chipStyle);
    }

    /// <summary>The keyboard's spell keys, on the chip.</summary>
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
