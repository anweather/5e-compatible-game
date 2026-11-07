using System.Collections;
using Chem;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

// The probe's spell list scenario (docs/SPELL_RADIAL.md): the list played on a
// virtual keyboard the run adds, so Tab, the arrows and Enter are real presses.
// Clicks can't be sent here (IMGUI's events come from the Editor), so a row's
// click isn't tested; Enter runs the same button. Shots: the list at 1080p and
// at a 4K scale.
public class SpellListProbe : ProbeScenario
{
    Keyboard keys;
    Gamepad pad;

    protected override IEnumerator Run()
    {
        var hud = wizard.GetComponent<SpellRadialHud>();
        var controls = caster.controls;
        var menu = PauseMenu.Instance;
        bool controlsWereOn = controls != null && controls.enabled, casterWasOn = caster.enabled, hudWasOn = EventLog.HudVisible;
        bool elementPadWas = controls != null && controls.ElementPad;
        if (controls != null) controls.enabled = true;
        caster.enabled = true;
        EventLog.HudVisible = true;
        keys = InputSystem.AddDevice<Keyboard>("Probe keys");
        pad = InputSystem.AddDevice<Gamepad>("Probe pad");
        try
        {
            yield return Frames(3);
            yield return HandsTab(menu);

            // The rest is the debug layout's (F2): the list picks what Cast casts there.
            controls.ElementPad = false;
            caster.Select(SpellIds.Thunderwave);
            yield return Frames(3);

            yield return Press(Key.Tab);
            Check("tab_opens_the_list", SpellListMenu.Open, $"page '{menu.CurrentPage}'");
            Check("not_the_wheel", hud != null && !hud.IsOpen);
            Check("opens_on_the_spells_element", SpellListMenu.Tabs.Count > 0 && SpellListMenu.Tabs[SpellListMenu.Tab].Name == "Storm", $"tab {SpellListMenu.Tab}");
            Check("highlight_on_the_spell", menu.FocusedLabel != null && menu.FocusedLabel.Contains("Thunderwave"), menu.FocusedLabel);
            Metric("tabs", SpellListMenu.Tabs.Count);
            Shot("list");
            yield return Wait(0.2f);
            HudScale.Forced = 2f;
            try
            {
                yield return Frames(3);
                Shot("list-4k-scale");
                yield return Wait(0.2f);
            }
            finally
            {
                HudScale.Forced = 0f;
            }

            yield return Press(Key.Tab);
            yield return Frames(3);
            Check("tab_closes_it", !menu.Open && (hud == null || !hud.IsOpen), $"open {menu.Open}");
            Check("wizard_back", !controlsWereOn || controls.enabled);

            yield return Press(Key.Tab);
            yield return Press(Key.Escape);
            yield return Frames(3);
            Check("esc_closes_it", !menu.Open, $"open {menu.Open}");

            // To the last tab (left wraps round), then down a row and Enter.
            yield return Press(Key.Tab);
            int tabs = SpellListMenu.Tabs.Count;
            while (SpellListMenu.Tab != tabs - 1) yield return Press(Key.LeftArrow);
            var offWheel = SpellListMenu.Tabs[tabs - 1];
            Check("last_tab_is_off_the_wheel", offWheel.Name == SpellList.OffWheelName, offWheel.Name);
            Shot("off-the-wheel");
            yield return Wait(0.2f);
            yield return Press(Key.DownArrow);
            yield return Press(Key.Enter);
            yield return Frames(3);
            Check("enter_picks_and_closes", !menu.Open && caster.SelectedId == offWheel.SpellIds[1], $"open {menu.Open}, selected {caster.SelectedId}");

            yield return EveryOffWheelSpellCasts(offWheel);

            yield return QAndE(hud);

            // The wheel is still the pad's. It's behind the debug layout, as before.
            controls.ElementPad = false;
            InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(GamepadButton.LeftShoulder));
            yield return Frames(3);
            Check("lb_still_opens_the_wheel", hud != null && hud.IsOpen && !menu.Open, $"wheel {hud?.IsOpen}, menu {menu.Open}");
            InputSystem.QueueStateEvent(pad, new GamepadState());
            yield return Frames(3);
        }
        finally
        {
            HudScale.Forced = 0f;
            if (menu.Open) menu.SetOpen(false);
            if (controls != null)
            {
                controls.ElementPad = elementPadWas;
                controls.enabled = controlsWereOn;
            }
            InputSystem.RemoveDevice(keys);
            InputSystem.RemoveDevice(pad);
            caster.enabled = casterWasOn;
            EventLog.HudVisible = hudWasOn;
            caster.Select(SpellIds.FireBolt);
        }
    }

    // With the hands (D16): Tab and the d-pad's down open the list on the hands' tab, the arrows move and Enter attunes.
    IEnumerator HandsTab(PauseMenu menu)
    {
        var world = grid.World;
        string me = caster.CastPlayer, wasLeft = world.HandsOf(me).Holds[HandsDef.Left];
        Check("the_hands_are_on", caster.controls.HandPad);
        yield return Press(Key.Tab);
        Check("tab_opens_on_the_hands", SpellListMenu.Open && SpellListMenu.Tabs.Count > 0 && SpellListMenu.Tabs[SpellListMenu.Tab].Name == SpellListMenu.HandsName,
            $"tab {SpellListMenu.Tab} of {SpellListMenu.Tabs.Count}");
        Shot("hands-tab");
        yield return Wait(0.2f);
        int focus = menu.Focus;
        yield return Press(Key.DownArrow);
        yield return Press(Key.DownArrow);
        Check("the_arrows_move_the_highlight", menu.Focus == focus + 2, $"{focus} -> {menu.Focus}");
        // The left hand's row: fire, water, earth, air, nothing; the third is earth.
        yield return Press(Key.Enter);
        for (float t = 0f; t < 3f && world.HandsOf(me).Holds[HandsDef.Left] != "earth"; t += Time.deltaTime) yield return null;
        Check("enter_attunes_the_left_hand", world.HandsOf(me).Holds[HandsDef.Left] == "earth", world.HandsOf(me).Holds[HandsDef.Left]);
        yield return Press(Key.Tab);
        Check("tab_closes_the_hands_tab", !menu.Open);
        caster.AttuneTo(HandsDef.Left, wasLeft);
        for (float t = 0f; t < 3f && world.HandsOf(me).Busy; t += Time.deltaTime) yield return null;

        InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(GamepadButton.DpadDown));
        yield return Frames(3);
        InputSystem.QueueStateEvent(pad, new GamepadState());
        yield return Frames(3);
        Check("dpad_down_opens_the_same_list", SpellListMenu.Open && SpellListMenu.Tabs[SpellListMenu.Tab].Name == SpellListMenu.HandsName, $"open {SpellListMenu.Open}");
        menu.SetOpen(false);
        yield return Frames(3);
    }

    // Q moves to the next element; E stays in it, on its next spell. The hint says so.
    IEnumerator QAndE(SpellRadialHud hud)
    {
        var layout = hud.Radial.Layout;
        caster.Select(SpellIds.FireBolt);
        yield return Frames(2);
        int fire = layout.GroupOfSpell(caster.SelectedId);
        yield return Press(Key.Q);
        int next = layout.GroupOfSpell(caster.SelectedId);
        Check("q_changes_element", next == (fire + 1) % layout.Count, $"element {fire} -> {next} ({caster.SelectedId})");
        string first = caster.SelectedId;
        yield return Press(Key.E);
        bool sameElement = layout.GroupOfSpell(caster.SelectedId) == next;
        Check("e_changes_spell_in_the_element", sameElement && (caster.SelectedId != first || layout.Groups[next].SpellIds.Count == 1), $"{first} -> {caster.SelectedId}");
        Check("hud_says_q_and_e", EventLog.KeyHints.Contains("Q element") && EventLog.KeyHints.Contains("E spell"), EventLog.KeyHints);
    }

    // The list offers these, so each must be something a player can pick and cast without an error.
    IEnumerator EveryOffWheelSpellCasts(SpellTab offWheel)
    {
        var cell = grid.CellAt(wizard.transform.position + wizard.transform.forward * 6f);
        int picked = 0, started = 0;
        foreach (var id in offWheel.SpellIds)
        {
            if (!caster.Select(id)) continue;
            picked++;
            int before = caster.CastsStarted;
            caster.Cast(id, cell.x, cell.y, 1);
            yield return Wait(0.3f);
            if (caster.CastsStarted > before) started++;
        }
        Check("every_off_wheel_spell_picks", picked == offWheel.SpellIds.Count, $"{picked} of {offWheel.SpellIds.Count}");
        Metric("off_wheel_casts_started", started);
        caster.Select(SpellIds.FireBolt);
    }

    // Presses and releases a key on the virtual keyboard.
    IEnumerator Press(Key key)
    {
        InputSystem.QueueStateEvent(keys, new KeyboardState(key));
        yield return Frames(2);
        InputSystem.QueueStateEvent(keys, new KeyboardState());
        yield return Frames(3);
    }

    static IEnumerator Frames(int n)
    {
        for (int i = 0; i < n; i++) yield return null;
    }
}
