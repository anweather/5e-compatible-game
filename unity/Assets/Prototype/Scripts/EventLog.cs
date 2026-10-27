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
    GUIStyle style, barText;
    SpellCaster caster;
    Vitals vitals;

    void Awake() => instance = this;
    void Start()
    {
        if (caster == null) caster = FindAnyObjectByType<SpellCaster>();
    }

    /// <summary>Shows this caster's spells and controls; the local player's caster calls it when enabled.</summary>
    /// <summary>The local player's caster, or null (e.g. the mods panel pauses its controls).</summary>
    public static SpellCaster TrackedCaster => instance != null ? instance.caster : null;

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
        if (reset) ResetYard();
    }

    /// <summary>
    /// Solo, reloads the scene and returns true. Online the host resets the world for everyone and a
    /// client asks the host to (OnlineReset); the scene isn't reloaded, so it returns false.
    /// </summary>
    public static bool ResetYard()
    {
        // Reloading the scene online would drop this machine's world and everyone's wizards with it.
        if (OnlineReset.Ask()) return false;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        return true;
    }

    const string FirelineHelp = "<b>WASD</b> move   ·   <b>Click</b> Fire Bolt   ·   <b>R</b> reset   ·   <b>Esc</b> menu";

    // A plus with a dark shadow, so it reads against the sky as well as the ground.
    void Crosshair()
    {
        var c = new Vector2(Screen.width / 2f, Screen.height / 2f);
        var cross = new GUIStyle(style) { alignment = TextAnchor.MiddleCenter, fontSize = 28 };
        GUI.Label(new Rect(c.x - 11, c.y - 14, 24, 30), "<color=#000000aa><b>+</b></color>", cross);
        GUI.Label(new Rect(c.x - 12, c.y - 15, 24, 30), "<b>+</b>", cross);
    }

    void OnGUI()
    {
        if (!HudVisible) return;
        style ??= new GUIStyle(GUI.skin.label) { fontSize = 18, richText = true };
        if (caster == null) GUI.Label(new Rect(16, 12, 900, 30), FirelineHelp, style);
        else DrawCasterHud();
        for (int i = 0; i < lines.Count; i++)
            GUI.Label(new Rect(16, Screen.height - 32 - (lines.Count - i) * 26, 900, 26), lines[i], style);
    }

    // The wizard's bars, the crosshair, and the spell chip: the selected spell in its element's
    // colour, with the keys that change it (the spell radial: SpellRadialHud). The rest of the
    // controls are on the menu's Controls page (ControlsMenu).
    void DrawCasterHud()
    {
        bool pad = caster.controls != null && caster.controls.UsingGamepad;
        DrawVitals();
        if (Cursor.lockState == CursorLockMode.Locked || pad) Crosshair();
        GUI.Label(new Rect(16, 40, 1400, 30), SpellChip(pad), style);
    }

    static readonly Color HealthColor = new(0.85f, 0.25f, 0.25f), StaminaColor = new(0.9f, 0.75f, 0.2f), MagicColor = new(0.35f, 0.55f, 1f);

    // Health, stamina and magic (Vitals), side by side at the top left.
    void DrawVitals()
    {
        if (vitals == null || vitals.gameObject != caster.gameObject) vitals = caster.GetComponent<Vitals>();
        if (vitals == null) return;
        Bar(0, "Health", vitals.Health, vitals.HealthMax, HealthColor);
        Bar(1, "Stamina", vitals.Stamina, Vitals.StaminaMax, StaminaColor);
        Bar(2, "Magic", vitals.Magic, Vitals.MagicMax, MagicColor);
    }

    void Bar(int slot, string name, float value, float max, Color colour)
    {
        const float width = 170f, height = 20f;
        var r = new Rect(16f + slot * (width + 10f), 14f, width, height);
        GUI.color = new Color(0f, 0f, 0f, 0.55f);
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = colour;
        GUI.DrawTexture(new Rect(r.x + 2f, r.y + 2f, (r.width - 4f) * Mathf.Clamp01(value / max), r.height - 4f), Texture2D.whiteTexture);
        GUI.color = Color.white;
        barText ??= new GUIStyle(style) { fontSize = 13, alignment = TextAnchor.MiddleCenter };
        GUI.Label(r, $"<b>{name}</b> {Mathf.CeilToInt(value)}/{Mathf.CeilToInt(max)}", barText);
    }

    string SpellChip(bool pad)
    {
        var group = caster.TryGetComponent<SpellRadialHud>(out var radial) ? radial.CurrentGroup : null;
        string colour = group?.Color ?? "#ffd24a";
        string element = group != null ? $"<color={colour}>{group.Name}</color>  " : "";
        string keys = pad ? PadGlyphs.Text("hold {LB}: spells, tap: swap  ·  {RB} next  ·  D-pad ◀ ▶ element") : "hold Tab: spells, tap: swap  ·  Q E next  ·  1–8 element";
        return $"<color={colour}>●</color> {element}<color=#ffd24a><b>{caster.SelectedName}</b></color>   <color=#ffffff88>{keys}</color>";
    }
}
