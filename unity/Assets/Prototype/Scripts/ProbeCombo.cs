using System.Collections;
using System.Linq;
using Chem;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

// The probe's combo scenario (docs/SPELL_COMBOS.md): casting by elements on a
// virtual gamepad, through the wizard's own controls: an element loaded, then a
// second in the window; the meter draining and the wizard slowed under it; the
// cast going off from the crosshair and joining the grimoire; cancelling with an
// item in hand, which must not throw it; the d-pad's spells; the grimoire page.
// With the HUD on, it takes a picture of the whole of it (--shots).
public class ComboProbe : ProbeScenario
{
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
            yield return Pair(at);
            yield return Slowed();
            yield return Cancel();
            yield return Dpad();
            yield return Grimoire();
            yield return Hands();
            yield return WholeHud();
        }
        finally
        {
            if (controls != null) controls.enabled = controlsWereOn;
            caster.enabled = casterWasOn;
            InputSystem.RemoveDevice(pad);
        }
    }

    // Water then earth: both loaded in the window, the meter drains, and it goes off where the crosshair is.
    IEnumerator Pair(Vector3 at)
    {
        int wetBefore = WetNear(at);
        yield return Press(GamepadButton.RightShoulder);
        var cast = World.CastOf(me);
        Check("first_element_loads", cast?.First == ElementIds.Water && cast.Casting, $"first {cast?.First}, meter {cast?.Meter}");
        InputSystem.QueueStateEvent(pad, new GamepadState { leftTrigger = 1f });
        yield return null;
        yield return null;
        InputSystem.QueueStateEvent(pad, new GamepadState());
        yield return null;
        yield return null;
        Check("second_element_loads", cast?.Second == ElementIds.Earth, $"second {cast?.Second}");
        Shot("loaded");
        yield return null;
        float waited = 0f;
        while (World.CastOf(me).Casting && waited < 4f)
        {
            waited += Time.deltaTime;
            yield return null;
        }
        yield return Wait(1f);
        Check("pair_joins_grimoire", World.KnowsCombo(me, "water+earth"), $"known: {string.Join(", ", World.CastOf(me).Known)}");
        Metric("wet_cells_before", wetBefore);
        Metric("wet_cells_after", WetNear(at));
        Check("cast_lands_at_the_crosshair", WetNear(at) > wetBefore, $"{wetBefore} wet cells near the aim, then {WetNear(at)}");
    }

    int WetNear(Vector3 at)
    {
        var c = grid.CellAt(at);
        int wet = 0;
        for (int y = -6; y <= 6; y++)
            for (int x = -6; x <= 6; x++)
                if (World.InBounds(c.x + x, c.y + y) && World.At(c.x + x, c.y + y).Water > 0) wet++;
        return wet;
    }

    // Walking at the rules' factor while the meter drains.
    IEnumerator Slowed()
    {
        var from = wizard.transform.position;
        InputSystem.QueueStateEvent(pad, new GamepadState { leftStick = Vector2.up });
        yield return Wait(0.8f);
        var start = wizard.transform.position;
        yield return Wait(0.6f);
        float free = Vector3.Distance(start, wizard.transform.position) / 0.6f;
        InputSystem.QueueStateEvent(pad, new GamepadState { leftStick = Vector2.up }.WithButton(GamepadButton.LeftShoulder));
        yield return null;
        yield return null;
        InputSystem.QueueStateEvent(pad, new GamepadState { leftStick = Vector2.up });
        yield return Wait(0.1f);
        InputSystem.QueueStateEvent(pad, new GamepadState { leftStick = Vector2.up }.WithButton(GamepadButton.LeftShoulder));
        yield return null;
        yield return null;
        InputSystem.QueueStateEvent(pad, new GamepadState { leftStick = Vector2.up });
        yield return Wait(0.4f);
        start = wizard.transform.position;
        yield return Wait(0.6f);
        float casting = Vector3.Distance(start, wizard.transform.position) / 0.6f;
        Metric("walk_speed_free", free);
        Metric("walk_speed_casting", casting);
        double factor = World.Rules.Combos.MoveFactor;
        Check("casting_slows_walking", World.CastOf(me).Casting && Mathf.Abs(casting / free - (float)factor) < 0.12f, $"{free:0.00} m/s, then {casting:0.00} m/s, the factor {factor}");
        InputSystem.QueueStateEvent(pad, new GamepadState());
        Teleport(from);
        yield return Wait(2.5f);
    }

    // B drops the cast and leaves what's in the hand there: nothing is thrown and nothing casts.
    IEnumerator Cancel()
    {
        var item = FindObjectsByType<Carryable>(FindObjectsSortMode.InstanceID).FirstOrDefault(c => c.Holder == null);
        if (item != null) hands.Grant(item);
        yield return null;
        yield return Press(GamepadButton.RightShoulder);
        Check("cast_started", World.CastOf(me).Casting);
        yield return Press(GamepadButton.East);
        Check("b_cancels", !World.CastOf(me).Casting, $"meter {World.CastOf(me).Meter}");
        yield return Wait(0.3f);
        bool held = item != null && hands.Held == item;
        Check("b_cancel_keeps_the_item", item == null || held, held ? "still held" : "it left the hand, or no item");
        if (held) hands.Drop();
        yield return Wait(0.3f);
    }

    IEnumerator Dpad()
    {
        var combos = World.Rules.Combos;
        int slot = World.CastOf(me).Slot;
        yield return Press(GamepadButton.DpadRight);
        Check("dpad_right_cycles", World.CastOf(me).Slot == (slot + 1) % combos.Dpad.Count, $"slot {slot} -> {World.CastOf(me).Slot}");
        // On to one with a spell: a slot without one only shows.
        for (int i = 0; i < combos.Dpad.Count && World.SelectedDpad(me)?.Spell == null; i++) yield return Press(GamepadButton.DpadRight);
        var spell = World.SelectedDpad(me);
        yield return Press(GamepadButton.DpadUp);
        Check("dpad_up_casts", World.CastOf(me).DpadCasting >= 0 && World.CastOf(me).Casting, $"{spell?.Name}: casting slot {World.CastOf(me).DpadCasting}");
        Shot("dpad");
        yield return null;
        yield return Wait(spell != null ? spell.CastTicks / (float)World.Rules.Sim.TicksPerSecond + 0.5f : 1f);
        Check("dpad_cast_finishes", !World.CastOf(me).Casting);
    }

    IEnumerator Grimoire()
    {
        yield return Press(GamepadButton.DpadDown);
        yield return Wait(0.2f);
        var menu = PauseMenu.Instance;
        Check("dpad_down_opens_grimoire", menu != null && menu.Open && menu.CurrentPage == "grimoire", menu != null ? $"page {menu.CurrentPage}" : "no menu");
        Shot("grimoire");
        yield return null;
        yield return null;
        if (menu != null) menu.SetOpen(false);
        yield return Wait(0.3f);
    }

    // The orbs in the hands, seen from the front, and the slam as they go off.
    IEnumerator Hands()
    {
        var t = wizard.transform;
        Cut(t.position + t.forward * 2.6f + Vector3.up * 1.5f, t.position + Vector3.up * 1.2f);
        yield return Press(GamepadButton.RightShoulder);
        yield return Press(GamepadButton.LeftShoulder);
        yield return Wait(0.3f);
        Shot("hands");
        yield return null;
        while (World.CastOf(me).Casting) yield return null;
        yield return Wait(0.09f);
        Shot("slam");
        yield return null;
        yield return Wait(1.5f);
    }

    // Everything on the screen at once: sprinting for the stamina ring, a pair loaded for the cast.
    IEnumerator WholeHud()
    {
        var sprint = new GamepadState { leftStick = Vector2.up }.WithButton(GamepadButton.LeftStick);
        InputSystem.QueueStateEvent(pad, sprint);
        yield return Wait(0.1f);
        InputSystem.QueueStateEvent(pad, new GamepadState { leftStick = Vector2.up });
        yield return Wait(1.2f);
        InputSystem.QueueStateEvent(pad, new GamepadState { leftStick = Vector2.up }.WithButton(GamepadButton.LeftShoulder));
        yield return null;
        yield return null;
        InputSystem.QueueStateEvent(pad, new GamepadState { leftStick = Vector2.up });
        yield return Wait(0.1f);
        InputSystem.QueueStateEvent(pad, new GamepadState { leftStick = Vector2.up }.WithButton(GamepadButton.RightShoulder));
        yield return null;
        yield return null;
        InputSystem.QueueStateEvent(pad, new GamepadState { leftStick = Vector2.up });
        yield return Wait(0.2f);
        var vitals = wizard.GetComponent<Vitals>();
        // Stamina is infinite in the sandbox, which would leave the ring full: spent for real for the picture.
        vitals.infinite = false;
        vitals.TrySpendStamina(vitals.Stamina * 0.35f);
        yield return null;
        Metric("hud_stamina", vitals.Stamina);
        Check("ring_drains", vitals.Stamina < Vitals.StaminaMax * 0.7f, $"stamina {vitals.Stamina:0}");
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
