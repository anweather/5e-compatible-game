using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

// The probe's menu scenario (docs/FAST_LOOP.md): the Esc menu driven the way a
// controller drives it. Start is pressed on a virtual gamepad (a device the
// Input System adds for the run, so the binding itself is exercised); moving
// the highlight, A and B go through the menu's own methods (Navigate, Submit,
// Back), the ones its Update calls for the d-pad, A and B. Screenshots: the
// main page with the highlight, each sub menu, the Mods page, Credits, and Join by code.
public class MenuProbe : ProbeScenario
{
    Gamepad pad;
    int opened;

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
            Check("main_page_is_short", menu.ItemCount == 7, $"{menu.ItemCount} items, first '{menu.FocusedLabel}'");
            Shot("main");
            yield return Wait(0.2f);
            yield return EveryEntryHasAHome(menu);

            // Game, then down to Mods; A opens the Mods page.
            yield return Enter(menu, PauseMenu.Submenu.Game, "Mods");
            menu.Submit();
            yield return Frames(3);
            int mods = switcher.Catalog.Mods.Count;
            Metric("mods", mods);
            Check("mods_page_opens", menu.CurrentPage == ModsPanel.PageId && ModsPanel.Open, $"page '{menu.CurrentPage}'");
            Check("mods_page_lists_mods", mods > 0 && menu.ItemCount == mods + 2, $"{mods} mods, {menu.ItemCount} items (mods, Play with Claude, Back)");

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
            Check("b_backs_out_of_mods", menu.Open && menu.CurrentPage == PauseMenu.PageOf(PauseMenu.Submenu.Game) && menu.FocusedLabel != null && menu.FocusedLabel.StartsWith("Mods", System.StringComparison.Ordinal),
                $"page '{menu.CurrentPage}', highlight on '{menu.FocusedLabel}'");
            menu.Back();
            yield return Frames(3);
            Check("b_backs_to_the_main_page", menu.CurrentPage == MenuNavigator.MainPage && menu.FocusedLabel == "Game", $"page '{menu.CurrentPage}', highlight on '{menu.FocusedLabel}'");

            // Controls: the table of every control, then back.
            yield return Enter(menu, PauseMenu.Submenu.Settings, "Controls");
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

            // Shake and rumble: A on − turns it down a step, + puts it back.
            yield return FocusOn(menu, "Shake and rumble");
            menu.Submit();
            yield return Frames(3);
            Check("feel_page_opens", menu.CurrentPage == SettingsMenu.FeelPage && menu.ItemCount == 3, $"page '{menu.CurrentPage}', {menu.ItemCount} items");
            float shake = GameFeel.Shake;
            menu.Submit();
            yield return Frames(2);
            float softer = GameFeel.Shake;
            menu.Navigate(1);
            yield return Frames(2);
            menu.Submit();
            yield return Frames(2);
            Check("feel_buttons_step", softer < shake && Mathf.Approximately(GameFeel.Shake, shake),
                $"shake {shake:0.00} -> {softer:0.00} -> {GameFeel.Shake:0.00}");
            menu.Back();
            yield return Frames(3);
            menu.Back();
            yield return Frames(3);

            yield return PlayLogSwitch(menu);

            // Credits, then back.
            yield return Enter(menu, PauseMenu.Submenu.Game, "About and credits");
            menu.Submit();
            yield return Frames(3);
            Check("credits_page_opens", menu.CurrentPage == GameInfoMenu.CreditsPage, $"page '{menu.CurrentPage}'");
            Shot("credits");
            yield return Wait(0.2f);
            menu.Back();
            yield return Frames(3);
            menu.Back();
            yield return Frames(3);

            // Join by code: up changes the letter, A moves on, B goes back a letter then leaves.
            yield return Enter(menu, PauseMenu.Submenu.Multiplayer, "Enter a code");
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
            Check("code_page_b_leaves", menu.CurrentPage == PauseMenu.PageOf(PauseMenu.Submenu.Multiplayer), $"page '{menu.CurrentPage}'");
            menu.Back();
            yield return Frames(3);

            // Claude's two entries, reached by pad.
            yield return Enter(menu, PauseMenu.Submenu.Claude, "Open a Claude terminal");
            Check("claude_submenu_has_the_terminal", menu.FocusedLabel != null && menu.FocusedLabel.StartsWith("Open a Claude terminal", System.StringComparison.Ordinal), $"highlight on '{menu.FocusedLabel}'");
            menu.Back();
            yield return Frames(3);

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

    // Every entry the old long page had is still one level down, and each sub menu fits the screen.
    // The entries cut for J-065 aren't on any of them.
    IEnumerator EveryEntryHasAHome(PauseMenu menu)
    {
        string[] cut = { "Host", "Join", "Frame-rate log: On", "Frame-rate log: Off", "Open log folder" };
        var everyLabel = new HashSet<string>();
        var homes = new (PauseMenu.Submenu submenu, string[] entries)[]
        {
            (PauseMenu.Submenu.Game, new[] { "Grimoire", "New look", "Mods", "About and credits" }),
            (PauseMenu.Submenu.Multiplayer, new[] { "Rule changes when you host", "Host online", "Join code", "Enter a code" }),
            (PauseMenu.Submenu.Settings, new[] { "Controls", "Sound", "Shake and rumble", "Play log" }),
            (PauseMenu.Submenu.Claude, new[] { "Play with Claude", "Open a Claude terminal" }),
            (PauseMenu.Submenu.Developer, new[] { "Claude's tools in the chat", "Claude's model in the chat" }),
        };
        foreach (var (submenu, entries) in homes)
        {
            yield return Enter(menu, submenu, entries[0]);
            var seen = new HashSet<string>();
            for (int i = 0; i < menu.ItemCount; i++)
            {
                yield return Frames(2);
                seen.Add(menu.FocusedLabel ?? "");
                menu.Navigate(1);
            }
            foreach (var entry in entries)
                Check($"{submenu}_has_{entry.Replace(' ', '_')}", seen.Any(l => l.StartsWith(entry, System.StringComparison.Ordinal)), string.Join(", ", seen));
            if (submenu == PauseMenu.Submenu.Developer)
                Check("play_log_not_in_developer", !seen.Any(l => l.StartsWith("Play log", System.StringComparison.Ordinal)), string.Join(", ", seen));
            everyLabel.UnionWith(seen);
            Shot(submenu.ToString().ToLowerInvariant());
            yield return Wait(0.2f);
            menu.Back();
            yield return Frames(3);
        }
        Check("cut_entries_gone", !everyLabel.Overlaps(cut), string.Join(", ", everyLabel.Intersect(cut)));
    }

    // Settings' Play log, flipped off and on by pad from a log writing in a folder of its own. Turning
    // it on writes beside the Editor's log, as it does for a player beside theirs, so that file goes too.
    IEnumerator PlayLogSwitch(PauseMenu menu)
    {
        var log = PlayLog.Instance;
        bool hadChoice = PlayerPrefs.HasKey(PlayLog.OnKey);
        int choice = PlayerPrefs.GetInt(PlayLog.OnKey, 1);
        string folder = Path.Combine(Application.temporaryCachePath, "play-log-menu-probe");
        string reopened = null;
        try
        {
            PlayLog.Wanted = true;
            log.Begin(folder);
            yield return Enter(menu, PauseMenu.Submenu.Settings, "Play log");
            Check("play_log_says_on", menu.FocusedLabel == "Play log: On", $"highlight on '{menu.FocusedLabel}'");

            menu.Submit();
            yield return Frames(3);
            Check("play_log_says_off", menu.FocusedLabel == "Play log: Off", $"highlight on '{menu.FocusedLabel}'");
            Check("play_log_off_is_kept", PlayerPrefs.GetInt(PlayLog.OnKey, 1) == 0, $"{PlayLog.OnKey} {PlayerPrefs.GetInt(PlayLog.OnKey, 1)}");
            Check("play_log_off_stops_writing", log.FilePath == null, log.FilePath);
            Check("play_log_off_stays_off_next_game", !PlayLog.StartsWriting(false, PlayLog.Wanted, System.Array.Empty<string>()));

            menu.Submit();
            yield return Frames(3);
            reopened = log.FilePath;
            Check("play_log_says_on_again", menu.FocusedLabel == "Play log: On", $"highlight on '{menu.FocusedLabel}'");
            Check("play_log_on_is_kept", PlayerPrefs.GetInt(PlayLog.OnKey, 0) == 1, $"{PlayLog.OnKey} {PlayerPrefs.GetInt(PlayLog.OnKey, 0)}");
            Check("play_log_on_writes_again", reopened != null, reopened);
            Check("play_log_on_stays_on_next_game", PlayLog.StartsWriting(false, PlayLog.Wanted, System.Array.Empty<string>()));
            menu.Back();
            yield return Frames(3);
        }
        finally
        {
            log.End();
            if (hadChoice) PlayerPrefs.SetInt(PlayLog.OnKey, choice);
            else PlayerPrefs.DeleteKey(PlayLog.OnKey);
            PlayerPrefs.Save();
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
            if (reopened != null) File.Delete(reopened);
        }
    }

    // From the main page, into a sub menu, the highlight on the entry that starts with this.
    IEnumerator Enter(PauseMenu menu, PauseMenu.Submenu submenu, string entry)
    {
        yield return FocusOn(menu, submenu.ToString());
        menu.Submit();
        yield return Frames(3);
        Check($"{submenu}_opens_by_pad_{opened++}", menu.CurrentPage == PauseMenu.PageOf(submenu), $"page '{menu.CurrentPage}'");
        yield return FocusOn(menu, entry);
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
