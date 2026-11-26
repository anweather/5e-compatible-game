using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

// The probe's combo scenario (docs/SPELL_COMBOS.md): the d-pad's spells on a
// virtual gamepad, through the wizard's own controls, with the HUD on: right
// cycles them, up casts the selected one, B cancels it with an item in hand and
// doesn't throw it, and the cast bar runs smoothly between the core's ticks; then
// Back from the menu's grimoire and the stamina line. With --shots, pictures of
// each and of the whole HUD.
//
// Since the hands (D16) the shoulders and triggers are hands' gestures, so what
// this probe did with them went to the probes made for them: loading elements
// and their pair (hands, castfeel), the slower walk while casting (castfeel's
// charging_walks_slower), d-pad down (now the hands menu: handsmenu), the orbs and
// the slam (castfeel, handsclip), and a held element's steps (hands' charge).
public class ComboProbe : ProbeScenario
{
    // Most of the bar a frame may gain past the time it took (one tick of Light's 6-tick cast is 0.17).
    const float MostBarJump = 0.07f;
    // The casts of BarRunsEven: how far between two ticks each is pressed, in ticks (the later, the sooner the first one), and held, in seconds.
    static readonly (float late, float held)[] PressesBetweenTicks = { (0.55f, 0f), (0.7f, 0f), (0.85f, 0f), (0.7f, 0.2f) };

    Gamepad pad;
    PlayerControls controls;
    string me;

    protected override IEnumerator Run()
    {
        controls = caster.controls;
        bool controlsWereOn = controls != null && controls.enabled, casterWasOn = caster.enabled;
        if (controls != null) controls.enabled = true;
        caster.enabled = true;
        pad = InputSystem.AddDevice<Gamepad>("Combo pad");
        EventLog.HudVisible = true;
        me = caster.CastPlayer;
        try
        {
            Place(8, 12);
            var at = Yard(14, 12);
            at.y = grid.GroundAt(at);
            Cut(wizard.transform.position + V(-3f, 4f, 0f), at);
            yield return Wait(0.5f);
            yield return Dpad();
            yield return Cancel();
            yield return Grimoire();
            yield return BarFills();
            yield return BarRunsEven();
            yield return WholeHud();
        }
        finally
        {
            if (controls != null) controls.enabled = controlsWereOn;
            caster.enabled = casterWasOn;
            InputSystem.RemoveDevice(pad);
        }
    }

    // Right cycles the d-pad's spells; on to Light (a cast that changes nothing the rest needs), up casts it.
    IEnumerator Dpad()
    {
        var combos = World.Rules.Combos;
        int slot = World.CastOf(me).Slot;
        yield return Press(GamepadButton.DpadRight);
        Check("dpad_right_cycles", World.CastOf(me).Slot == (slot + 1) % combos.Dpad.Count, $"slot {slot} -> {World.CastOf(me).Slot}");
        for (int i = 0; i < combos.Dpad.Count && World.SelectedDpad(me)?.Spell != SpellIds.Light; i++) yield return Press(GamepadButton.DpadRight);
        var spell = World.SelectedDpad(me);
        Check("dpad_reaches_light", spell?.Spell == SpellIds.Light, spell?.Name ?? "none");
        yield return Press(GamepadButton.DpadUp);
        Check("dpad_up_casts", World.CastOf(me).DpadCasting >= 0 && World.CastOf(me).Casting, $"{spell?.Name}: casting slot {World.CastOf(me).DpadCasting}");
        Shot("dpad");
        yield return null;
        yield return Wait(spell != null ? spell.CastTicks / (float)World.Rules.Sim.TicksPerSecond + 0.5f : 1f);
        Check("dpad_cast_finishes", !World.CastOf(me).Casting);
        yield return Wait(0.5f);
    }

    // B drops the cast and leaves what's in the hand there: nothing is thrown and nothing casts.
    IEnumerator Cancel()
    {
        var item = FindObjectsByType<Carryable>(FindObjectsSortMode.InstanceID).FirstOrDefault(c => c.Holder == null);
        if (item != null) hands.Grant(item);
        yield return null;
        yield return Press(GamepadButton.DpadUp);
        Check("cast_started", World.CastOf(me).Casting);
        yield return Press(GamepadButton.East);
        Check("b_cancels", !World.CastOf(me).Casting, $"meter {World.CastOf(me).Meter}");
        yield return Wait(0.3f);
        bool held = item != null && hands.Held == item;
        Check("b_cancel_keeps_the_item", item == null || held, held ? "still held" : "it left the hand, or no item");
        if (held) hands.Drop();
        yield return Wait(0.3f);
    }

    // From the menu's own button, Back from the grimoire goes back to the menu (#10).
    IEnumerator Grimoire()
    {
        var menu = PauseMenu.Instance;
        if (menu == null)
        {
            Check("back_from_menu_stays_in_menu", false, "no menu");
            yield break;
        }
        menu.SetOpen(true);
        menu.ShowPage("grimoire");
        yield return null;
        yield return null;
        bool shown = menu.Open && menu.CurrentPage == "grimoire";
        Shot("grimoire");
        menu.Back();
        Check("back_from_menu_stays_in_menu", shown && menu.Open && menu.CurrentPage != "grimoire", $"shown {shown}, open {menu.Open}, page {menu.CurrentPage}");
        menu.SetOpen(false);
        yield return null;
        Check("back_to_play", controls.enabled, "the wizard's controls are back");
        yield return Wait(0.3f);
    }

    // The cast bar runs on between the core's ticks: many more steps than ticks, and only ever up (#9).
    IEnumerator BarFills()
    {
        yield return Press(GamepadButton.DpadUp);
        var cast = World.CastOf(me);
        int ticks = cast.Total, steps = 0;
        float last = 0f, lowest = 1f, highest = 0f;
        bool shot = false, onlyUp = true;
        while (World.CastOf(me).Casting)
        {
            float share = EventLog.CastBarShare;
            if (!Mathf.Approximately(share, last)) steps++;
            onlyUp &= share >= last;
            lowest = Mathf.Min(lowest, share);
            highest = Mathf.Max(highest, share);
            last = share;
            if (!shot && share > 0.5f)
            {
                shot = true;
                Shot("bar-half");
            }
            yield return null;
        }
        Metric("bar_ticks", ticks);
        Metric("bar_steps", steps);
        Check("bar_fills_smoothly", steps > ticks * 2 && onlyUp && lowest < 0.4f && highest > 0.85f, $"{steps} steps over {ticks} ticks, {lowest:0.00} to {highest:0.00}, only up {onlyUp}");
        yield return Wait(1f);
    }

    // Pressed at any moment between two ticks and tapped short or long, the bar runs at the pace of the time
    // left and doesn't leap when the core's first tick comes, however soon that is, or when the press ends (#9).
    // What it gains in a frame, past that frame's share of the cast.
    IEnumerator BarRunsEven()
    {
        var end = new WaitForEndOfFrame();
        int perSecond = World.Rules.Sim.TicksPerSecond;
        float worst = 0f;
        bool allCast = true;
        foreach (var (late, held) in PressesBetweenTicks)
        {
            int tick = World.Tick;
            while (World.Tick == tick) yield return null;
            yield return Wait(late / perSecond);
            InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(GamepadButton.DpadUp));
            for (float t = 0f; !World.CastOf(me).Casting && t < 2f; t += Time.deltaTime) yield return null;
            allCast &= World.CastOf(me).Casting;
            float last = 0f, lastAt = Time.time, letGoAt = Time.time + held;
            bool down = true;
            while (World.CastOf(me).Casting)
            {
                if (down && Time.time >= letGoAt)
                {
                    down = false;
                    InputSystem.QueueStateEvent(pad, new GamepadState());
                }
                yield return end;
                float share = EventLog.CastBarShare, seconds = World.CastOf(me).Total / (float)perSecond;
                worst = Mathf.Max(worst, share - last - (Time.time - lastAt) / seconds);
                (last, lastAt) = (share, Time.time);
            }
            InputSystem.QueueStateEvent(pad, new GamepadState());
            yield return Wait(1f);
        }
        Metric("bar_worst_jump", worst);
        Check("bar_runs_even", allCast && worst < MostBarJump, $"each press cast {allCast}, gained {worst:0.00} of the bar past the time gone, in one frame");
    }

    // Everything on the screen at once: sprinting for the stamina line, a d-pad cast under way.
    IEnumerator WholeHud()
    {
        var sprint = new GamepadState { leftStick = Vector2.up }.WithButton(GamepadButton.LeftStick);
        InputSystem.QueueStateEvent(pad, sprint);
        yield return Wait(0.1f);
        InputSystem.QueueStateEvent(pad, new GamepadState { leftStick = Vector2.up });
        yield return Wait(1.2f);
        InputSystem.QueueStateEvent(pad, new GamepadState { leftStick = Vector2.up }.WithButton(GamepadButton.DpadUp));
        yield return null;
        yield return null;
        InputSystem.QueueStateEvent(pad, new GamepadState { leftStick = Vector2.up });
        yield return Wait(0.2f);
        var vitals = wizard.GetComponent<Vitals>();
        // Stamina is infinite in the sandbox, which would leave the line full: spent for real for the picture.
        vitals.infinite = false;
        vitals.TrySpendStamina(vitals.Stamina * 0.35f);
        yield return null;
        Metric("hud_stamina", vitals.Stamina);
        Check("stamina_drains", vitals.Stamina < Vitals.StaminaMax * 0.7f, $"stamina {vitals.Stamina:0}");
        Shot("hud");
        yield return null;
        yield return null;
        vitals.infinite = true;
        InputSystem.QueueStateEvent(pad, new GamepadState());
        yield return Wait(3f);
    }

    // A tap of a button.
    IEnumerator Press(GamepadButton button)
    {
        InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(button));
        yield return null;
        yield return null;
        InputSystem.QueueStateEvent(pad, new GamepadState());
        yield return null;
        yield return null;
    }
}
