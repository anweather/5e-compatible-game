using Chem;
using UnityEngine;
using UnityEngine.InputSystem;

// The pad's hands menu (pad-hands-menu; docs/playtests/2026-11-08-human-results.md M1): hold the d-pad's down and the two hands
// show, each with the rules' elements round it; the left stick points the left hand at one, the right stick the right hand, and
// letting go of the d-pad attunes the hands that changed (B lets go without). Nothing else. The choosing is HandsPick, engine-free;
// this feeds it the sticks, draws it, and attunes through SpellCaster.AttuneTo, which says why when it can't. The core attunes one
// hand at a time, so a second hand waits here until the first is done. While it's open the sticks are the menu's: the wizard
// stands and the camera stays (as in the Esc menu), and the hands cast nothing. An element a hand can't take here (locked, or in a
// Hold run anywhere but at a tower, or another tower's) is dimmed and can't be pointed at. SpellCaster feeds it each frame; only
// the local wizard has controls, so there's one.
public class HandsMenu : MonoBehaviour
{
    // A hand's ring: how far apart the two sit, how far out the elements are, and the orbs' sizes.
    const float Apart = 300f, Spoke = 96f, Orb = 28f, PickedOrb = 36f, Middle = 40f, Backing = 150f;
    static readonly Color Shade = new(0f, 0f, 0f, 0.5f), Gold = new(1f, 0.82f, 0.3f);
    const float Dim = 0.28f;

    static HandsMenu instance;
    SpellCaster caster;
    Gamepad pad;
    bool open;
    // Shut with B while the d-pad's down is still held: it opens again only after letting go.
    bool shutWhileHeld;
    readonly string[] picks = new string[HandsDef.Count];
    // A hand still to attune once the other is done.
    SpellCaster waitingFor;
    int waitingHand = -1;
    string waitingElement;
    PlayerInput pausedInput;
    Component inputs;
    GUIStyle word;

    /// <summary>Whether the menu is showing.</summary>
    public static bool IsOpen => instance != null && instance.open;

    /// <summary>What a hand is pointed at in the menu (null: nothing yet), for the probe.</summary>
    public static string Pick(int hand) => instance != null ? instance.picks[hand] : null;

    /// <summary>Whether a hand is still waiting for the other to finish attuning, for the probe.</summary>
    public static bool Waiting => instance != null && instance.waitingHand >= 0;

    /// <summary>Whether this wizard's menu is open: its hands and d-pad are the menu's.</summary>
    public static bool OpenFor(SpellCaster caster) => IsOpen && instance.caster == caster;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if (instance != null) return;
        var go = new GameObject("Hands menu");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<HandsMenu>();
    }

    void OnDestroy() { if (instance == this) instance = null; }

    /// <summary>
    /// Each frame, from the wizard with the hands on: whether the d-pad's down is held, and the pad it's held on. Opens the menu,
    /// reads the sticks while it's open, and on letting go attunes what changed.
    /// </summary>
    public static void Feed(SpellCaster caster, bool held, Gamepad pad)
    {
        if (instance == null) return;
        instance.AttuneWaiting(caster);
        if (!held) instance.shutWhileHeld = false;
        if (!instance.open)
        {
            if (held && !instance.shutWhileHeld && pad != null && (PauseMenu.Instance == null || !PauseMenu.Instance.Open)) instance.Show(caster, pad);
            return;
        }
        if (instance.caster != caster) return;
        if (instance.pad == null || !instance.pad.added || instance.pad.buttonEast.wasPressedThisFrame)
        {
            Close();
            instance.shutWhileHeld = held;
        }
        else if (!held) instance.Confirm();
        else instance.Steer();
    }

    /// <summary>Shuts the menu without attuning anything.</summary>
    public static void Close()
    {
        if (instance == null || !instance.open) return;
        instance.open = false;
        instance.Resume();
    }

    void Show(SpellCaster by, Gamepad on)
    {
        caster = by;
        pad = on;
        open = true;
        System.Array.Clear(picks, 0, picks.Length);
        Pause();
    }

    World World => caster != null && caster.grid != null ? caster.grid.World : null;

    void Steer()
    {
        var world = World;
        if (world == null) return;
        var elements = world.Rules.Combos.Elements;
        for (int hand = 0; hand < HandsDef.Count; hand++)
        {
            var stick = (hand == HandsDef.Left ? pad.leftStick : pad.rightStick).ReadValue();
            int at = HandsPick.Pointed(stick.x, stick.y, elements.Count);
            if (at >= 0 && Offered(world, caster.CastPlayer, hand, elements[at])) picks[hand] = elements[at];
        }
    }

    void Confirm()
    {
        var world = World;
        open = false;
        Resume();
        if (world == null) return;
        var changes = HandsPick.Confirmed(world.HandsOf(caster.CastPlayer).Holds, picks);
        for (int i = 0; i < changes.Count; i++)
        {
            var (hand, element) = changes[i];
            if (i == 0)
            {
                caster.AttuneTo(hand, element);
                continue;
            }
            waitingFor = caster;
            waitingHand = hand;
            waitingElement = element;
        }
    }

    // The second hand, once the first has attuned.
    void AttuneWaiting(SpellCaster by)
    {
        if (waitingHand < 0 || by != waitingFor) return;
        var world = World ?? (by.grid != null ? by.grid.World : null);
        if (world == null || world.HandsOf(by.CastPlayer).AttuneLeft > 0) return;
        int hand = waitingHand;
        waitingHand = -1;
        by.AttuneTo(hand, waitingElement);
    }

    // What a hand can take here: an open hand and element, and in a Hold run only at a tower, its own.
    static bool Offered(World world, string player, int hand, string element)
    {
        if (!world.HandOpen(player, hand) || !world.ElementOpen(player, element)) return false;
        if (world.TowerOf(player) is { } tower) return tower.Element == element;
        return !world.HoldRunning || world.HandsOf(player).Holds[hand] == element;
    }

    // The sticks are the menu's while it's open: the wizard stands still and the camera stays, as in the Esc menu.
    void Pause()
    {
        if (caster.TryGetComponent<PlayerInput>(out var input) && input.enabled)
        {
            pausedInput = input;
            input.enabled = false;
        }
        inputs = caster.GetComponent(WizardRig.InputsComponent);
        Still();
    }

    void Resume()
    {
        if (pausedInput != null) pausedInput.enabled = true;
        pausedInput = null;
        Still();
        inputs = null;
    }

    // The last move and look the wizard had are cleared, so they don't carry on.
    void Still()
    {
        if (inputs == null) return;
        foreach (var name in new[] { "look", "move" })
        {
            var field = WizardRig.Field(inputs, name, isPublic: true);
            if (field != null && field.FieldType == typeof(Vector2)) field.SetValue(inputs, Vector2.zero);
        }
    }

    // --- drawing -----------------------------------------------------------------------

    void OnGUI()
    {
        var world = World;
        if (!open || world == null || !EventLog.HudVisible) return;
        word ??= new GUIStyle(GUI.skin.label) { fontSize = 22, richText = true, alignment = TextAnchor.MiddleCenter };
        HudScale.Apply();
        GUI.depth = -50; // over the HUD, under the Esc menu
        var centre = new Vector2(HudScale.Width / 2f, HudScale.Height / 2f);
        for (int hand = 0; hand < HandsDef.Count; hand++)
            DrawHand(world, hand, centre + new Vector2(hand == HandsDef.Left ? -Apart : Apart, 0f));
    }

    void DrawHand(World world, int hand, Vector2 at)
    {
        string player = caster.CastPlayer;
        var elements = world.Rules.Combos.Elements;
        string holds = world.HandsOf(player).Holds[hand], picked = picks[hand];
        bool handOpen = world.HandOpen(player, hand);
        HudDraw.Disc(at, Backing, Shade);
        for (int i = 0; i < elements.Count; i++)
        {
            var (x, y) = HandsPick.Direction(i, elements.Count);
            var spot = at + new Vector2(x, -y) * Spoke;
            string element = elements[i];
            bool chosen = element == (picked ?? holds);
            if (chosen) HudDraw.Disc(spot, PickedOrb + 6f, Gold);
            HudDraw.ElementOrb(spot, chosen ? PickedOrb : Orb, element, Offered(world, player, hand, element) ? 1f : Dim);
        }
        string now = picked ?? holds;
        if (now != null) HudDraw.ElementOrb(at, Middle, now, handOpen ? 1f : Dim);
        else HudDraw.Disc(at, Middle, new Color(0.4f, 0.4f, 0.45f, handOpen ? 1f : Dim));
        if (now != null) GUI.Label(new Rect(at.x - 100f, at.y + Backing + 6f, 200f, 32f), $"<b>{now}</b>", word);
    }
}
