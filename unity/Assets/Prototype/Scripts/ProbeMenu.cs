using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

// The probe's menu scenario (docs/FAST_LOOP.md): the Esc menu driven the way a
// controller drives it. Start is pressed on a virtual gamepad (a device the
// Input System adds for the run, so the binding itself is exercised); moving
// the highlight, A and B go through the menu's own methods (Navigate, Submit,
// Back), the ones its Update calls for the d-pad, A and B. Screenshots: the
// main page with the highlight, the Mods page, Credits, and Join by code.
public class MenuProbe : ProbeScenario
{
    Gamepad pad;

    protected override IEnumerator Run()
    {
        var menu = PauseMenu.Instance;
        var switcher = grid.GetComponent<RuleSwitcher>();
        pad = InputSystem.AddDevice<Gamepad>("Probe pad");
        try
        {
            // Start opens the menu, on its main page, with the highlight showing.
            var controls = caster.controls;
            bool controlsWereOn = controls != null && controls.enabled;
            yield return PressStart();
            Check("start_opens_menu", menu.Open && menu.CurrentPage == MenuNavigator.MainPage && menu.Highlight,
                $"open {menu.Open}, page '{menu.CurrentPage}', highlight {menu.Highlight}");
            Check("wizard_paused", !controlsWereOn || !controls.enabled, $"controls were on {controlsWereOn}");
            yield return null;
            Metric("main_items", menu.ItemCount);
            Check("main_page_items", menu.ItemCount >= 9, $"{menu.ItemCount} items, first '{menu.FocusedLabel}'");

            // Down to Mods (passing every main-page button), A opens the Mods page.
            yield return FocusOn(menu, "Mods");
            Shot("main");
            yield return Wait(0.2f);
            menu.Submit();
            yield return Frames(3);
            int mods = switcher.Catalog.Mods.Count;
            Metric("mods", mods);
            Check("mods_page_opens", menu.CurrentPage == ModsPanel.PageId && ModsPanel.Open, $"page '{menu.CurrentPage}'");
            Check("mods_page_lists_mods", mods > 0 && menu.ItemCount == mods + 1, $"{mods} mods, {menu.ItemCount} items (mods and Back)");

            // A on the first mod switches it on; A again switches it back off.
            string first = switcher.Catalog.Mods[0].Id;
            bool wasOn = switcher.Rules.IsOn(first);
            menu.Submit();
            yield return Wait(0.5f);
            Check("a_toggles_mod", switcher.Rules.IsOn(first) != wasOn, $"{first}: {wasOn} -> {switcher.Rules.IsOn(first)}");
            Shot("mods");
            yield return Wait(0.2f);
            menu.Submit();
            yield return Wait(0.5f);
            Check("a_toggles_mod_back", switcher.Rules.IsOn(first) == wasOn, $"{first}: {switcher.Rules.IsOn(first)}");

            // B goes back to the main page, the highlight on Mods again.
            menu.Back();
            yield return Frames(3);
            Check("b_backs_out_of_mods", menu.Open && menu.CurrentPage == MenuNavigator.MainPage && menu.FocusedLabel != null && menu.FocusedLabel.StartsWith("Mods", System.StringComparison.Ordinal),
                $"page '{menu.CurrentPage}', highlight on '{menu.FocusedLabel}'");

            // Controls: the table of every control, then back.
            yield return FocusOn(menu, "Controls");
            menu.Submit();
            yield return Frames(3);
            Check("controls_page_opens", menu.CurrentPage == ControlsMenu.ControlsPage, $"page '{menu.CurrentPage}'");
            Shot("controls");
            yield return Wait(0.2f);
            menu.Back();
            yield return Frames(3);

            // Sound: A on Effects' − turns effects down a step, + puts it back.
            yield return FocusOn(menu, "Sound");
            menu.Submit();
            yield return Frames(3);
            Check("sound_page_opens", menu.CurrentPage == SettingsMenu.SoundPage && menu.ItemCount == 7, $"page '{menu.CurrentPage}', {menu.ItemCount} items");
            float effects = GameVolume.Effects;
            menu.Navigate(1);
            menu.Navigate(1);
            yield return Frames(2);
            menu.Submit();
            yield return Frames(2);
            float lowered = GameVolume.Effects;
            menu.Navigate(1);
            yield return Frames(2);
            menu.Submit();
            yield return Frames(2);
            Check("sound_buttons_step", lowered < effects && Mathf.Approximately(GameVolume.Effects, effects),
                $"effects {effects:0.00} -> {lowered:0.00} -> {GameVolume.Effects:0.00}");
            Shot("sound");
            menu.Back();
            yield return Frames(3);

            // Credits, then back.
            yield return FocusOn(menu, "Credits");
            menu.Submit();
            yield return Frames(3);
            Check("credits_page_opens", menu.CurrentPage == GameInfoMenu.CreditsPage, $"page '{menu.CurrentPage}'");
            Shot("credits");
            yield return Wait(0.2f);
            menu.Back();
            yield return Frames(3);

            // Join by code: up changes the letter, A moves on, B goes back a letter then leaves.
            yield return FocusOn(menu, "Enter a code");
            menu.Submit();
            yield return Frames(3);
            Check("code_page_opens", menu.CurrentPage == NetSession.CodePage, $"page '{menu.CurrentPage}'");
            menu.Navigate(-1);
            menu.Navigate(-1);
            menu.Submit();
            menu.Navigate(-1);
            yield return Frames(3);
            Shot("join-code");
            yield return Wait(0.2f);
            menu.Back();
            Check("code_page_b_goes_back_a_letter", menu.CurrentPage == NetSession.CodePage);
            menu.Back();
            yield return Frames(3);
            Check("code_page_b_leaves", menu.CurrentPage == MenuNavigator.MainPage, $"page '{menu.CurrentPage}'");

            // B on the main page closes the menu and gives the wizard back its controls.
            menu.Back();
            yield return Frames(2);
            Check("b_closes_menu", !menu.Open);
            Check("wizard_resumed", !controlsWereOn || controls.enabled);

            // Start opens it again and Start closes it.
            yield return PressStart();
            bool reopened = menu.Open;
            yield return PressStart();
            Check("start_closes_menu", reopened && !menu.Open, $"reopened {reopened}, open {menu.Open}");
        }
        finally
        {
            if (menu.Open) menu.SetOpen(false);
            InputSystem.RemoveDevice(pad);
        }
    }

    // Presses and releases Start on the virtual gamepad, a frame each.
    IEnumerator PressStart()
    {
        InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(GamepadButton.Start));
        yield return Frames(2);
        InputSystem.QueueStateEvent(pad, new GamepadState());
        yield return Frames(2);
    }

    // Moves the highlight down until it is on the button whose text starts with this.
    static IEnumerator FocusOn(PauseMenu menu, string label)
    {
        for (int i = 0; i <= menu.ItemCount; i++)
        {
            yield return Frames(2);
            if (menu.FocusedLabel != null && menu.FocusedLabel.StartsWith(label, System.StringComparison.Ordinal)) yield break;
            menu.Navigate(1);
        }
    }

    static IEnumerator Frames(int n)
    {
        for (int i = 0; i < n; i++) yield return null;
    }
}
