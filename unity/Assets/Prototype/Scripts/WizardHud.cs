using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

// The wizard's HUD on the event log (EventLog's add-on, through ILogHud): the spell bar, vitals, cast HUD,
// crosshair and spell chip when the scene has a SpellCaster. R reloads the scene solo, and online resets the world
// for everyone (the host) or asks the host to (OnlineReset); held, it rebuilds the valley at once. Neither while the
// playtest bot plays (BotPlaying, which PlaytestBot.Active sets).
[GridAddOn(GridStage.Hud, 10)]
public class WizardHud : MonoBehaviour, ILogHud
{
    static WizardHud instance;

    public static bool BotPlaying { get; set; }

    SpellCaster caster;
    readonly VitalsHud vitalsHud = new();
    readonly CastHud castHud = new();
    GUIStyle chipStyle;

    void Awake() => instance = this;

    void Start()
    {
        if (caster == null) caster = SpellCaster.InPlay;
    }

    public static SpellCaster TrackedCaster => instance != null ? instance.caster : null;

    public static float CastBarShare => instance != null ? instance.castHud.Share : 0f;

    public static CastHud CastShown => instance != null ? instance.castHud : null;

    public static void Track(SpellCaster c)
    {
        if (instance != null) instance.caster = c;
    }

    void OnDestroy() { if (instance == this) instance = null; }

    Component ILogHud.Wizard => caster;

    PlayerControls ILogHud.Controls => caster != null ? caster.controls : null;

    void ILogHud.Tick()
    {
        var controls = caster != null ? caster.controls : null;
        bool reset = controls != null ? controls.Reset.WasPressedThisFrame() : Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame;
        bool held = controls != null ? controls.Reset.IsPressed() : Keyboard.current != null && Keyboard.current.rKey.isPressed;
        if (reset)
        {
            resetHeld = 0f;
            if (!ResetYard()) EventLog.Add("Keep holding Reset to rebuild the valley at once");
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
        if (BotPlaying)
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
        if (BotPlaying)
        {
            Debug.Log("PLAYTEST: reset ignored while the bot runs");
            return false;
        }
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        return true;
    }

    static void Crosshair(GUIStyle style)
    {
        var c = new Vector2(HudScale.Width / 2f, HudScale.Height / 2f);
        var cross = new GUIStyle(style) { alignment = TextAnchor.MiddleCenter, fontSize = 28 };
        GUI.Label(new Rect(c.x - 11, c.y - 14, 24, 30), "<color=#000000aa><b>+</b></color>", cross);
        GUI.Label(new Rect(c.x - 12, c.y - 15, 24, 30), "<b>+</b>", cross);
    }

    void ILogHud.Draw(GUIStyle style)
    {
        if (caster != null) DrawCasterHud(style);
    }

    void DrawCasterHud(GUIStyle style)
    {
        bool pad = caster.controls != null && caster.controls.UsingGamepad;
        float vitalsHeight = vitalsHud.Draw(caster.gameObject, style);
        if (Cursor.lockState == CursorLockMode.Locked || pad) Crosshair(style);
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
