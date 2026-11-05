using System.Collections;
using Chem;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

// The probe's hands scenario (docs/design/the-hold.md, phase 3): every hand input through real devices, added here, with what
// goes off read from the core. The wall cell it stands on and the cell it aims at are this map's; what each input should cast
// comes from the rules' table. Nothing here is in the game.
public class HandsProbe : ProbeScenario
{
    const int WallX = 21, WallY = 3, AimNorth = 10;
    const float MostWait = 8f;

    Gamepad pad;
    Mouse mouse;
    Keyboard keys;
    PlayerControls controls;
    readonly FiredCastsHeard heard = new();
    BuildingRow row;

    World Core => grid.World;
    string Me => caster.CastPlayer;
    HandsDef Table => Core.Rules.Sim.Hold.Hands;

    void Stand(Vector2Int cell)
    {
        var p = grid.CellCenter(cell.x, cell.y);
        p.y = grid.GroundAt(p);
        Teleport(p + Vector3.up * 0.05f);
    }

    void Let()
    {
        InputSystem.QueueStateEvent(pad, new GamepadState());
        InputSystem.QueueStateEvent(mouse, new MouseState());
        InputSystem.QueueStateEvent(keys, new KeyboardState());
    }

    // A press, held for a few frames, let go, and what the core sets off; then the hands cool down.
    IEnumerator Fires(string name, string spell, System.Action press, System.Action second = null)
    {
        heard.StartFrom(Core);
        int started = caster.CastsStarted;
        press();
        yield return null;
        yield return null;
        yield return null;
        if (second != null)
        {
            second();
            yield return null;
            yield return null;
            yield return null;
        }
        Let();
        string fired = null;
        for (float t = 0f; t < MostWait && fired == null; t += Time.deltaTime)
        {
            foreach (var f in heard.Take(Core))
                if (f.Player == Me) fired = f.Spell;
            yield return null;
        }
        Check(name, fired == spell, $"{fired}, wanted {spell}");
        yield return Wait(0.3f);
        Check(name + "_cast", caster.CastsStarted > started, $"{caster.CastsStarted - started} casts");
        for (float t = 0f; t < MostWait && (Core.HandCooling(Me, HandsDef.Left) > 0 || Core.HandCooling(Me, HandsDef.Right) > 0); t += Time.deltaTime) yield return null;
    }

    void Pad(float left, float right, params GamepadButton[] held)
    {
        var state = new GamepadState { leftTrigger = left, rightTrigger = right };
        foreach (var b in held) state = state.WithButton(b);
        InputSystem.QueueStateEvent(pad, state);
    }

    void Click(bool shift, params MouseButton[] held)
    {
        var state = new MouseState();
        foreach (var b in held) state = state.WithButton(b);
        if (shift)
        {
            InputSystem.QueueStateEvent(keys, new KeyboardState(Key.LeftShift));
            InputSystem.Update();
        }
        InputSystem.QueueStateEvent(mouse, state);
    }

    IEnumerator AttuneTo(string element, int hand, System.Action press)
    {
        for (int i = 0; i < Core.Rules.Combos.Elements.Count && Core.HandsOf(Me).Element[hand] != element; i++)
        {
            press();
            yield return null;
            yield return null;
            Let();
            yield return Wait(0.4f);
        }
        Check($"attuned_{hand}_to_{element}", Core.HandsOf(Me).Element[hand] == element, Core.HandsOf(Me).Element[hand]);
    }

    IEnumerator Attune(string left, string right)
    {
        var plate = Core.HoldSite.Plate;
        Stand(new Vector2Int(plate.x, plate.y));
        for (float t = 0f; t < MostWait && !Core.InAttuning(Me); t += Time.deltaTime) yield return null;
        yield return Wait(0.3f);
        Check("in_the_attuning_room", Core.InAttuning(Me), grid.CellAt(wizard.transform.position).ToString());
        Check("the_hands_layout_is_on", controls.HandPad);
        yield return AttuneTo(left, HandsDef.Left, () => Pad(1f, 0f));
        yield return AttuneTo(right, HandsDef.Right, () => Click(false, MouseButton.Right));
        Stand(new Vector2Int(row.X + WallX, row.Y + WallY));
        for (float t = 0f; t < MostWait && Core.InAttuning(Me); t += Time.deltaTime) yield return null;
        yield return Wait(0.3f);
    }

    IEnumerator Cooling()
    {
        Pad(0f, 1f);
        yield return null;
        yield return null;
        Let();
        for (float t = 0f; t < MostWait && Core.HandCooling(Me, HandsDef.Right) == 0; t += Time.deltaTime) yield return null;
        Check("it_cools_down", Core.HandCooling(Me, HandsDef.Right) > 0, $"{Core.HandCooling(Me, HandsDef.Right)} ticks");
        yield return null;
        Pad(0f, 1f);
        yield return null;
        yield return null;
        Let();
        yield return null;
        Check("a_cooling_hand_is_refused", Core.CastOf(Me)?.Casting != true);
        for (float t = 0f; t < MostWait && Core.HandCooling(Me, HandsDef.Right) > 0; t += Time.deltaTime) yield return null;
    }

    IEnumerator Inputs(string left, string right)
    {
        const GamepadButton lb = GamepadButton.LeftShoulder, rb = GamepadButton.RightShoulder;
        string one(string e, bool m) => Table.Spell(e, null, m).Spell;
        string both(bool m) => Table.Spell(left, right, m).Spell;
        yield return Fires("right_trigger", one(right, false), () => Pad(0f, 1f));
        yield return Fires("left_trigger", one(left, false), () => Pad(1f, 0f));
        yield return Fires("right_bumper_and_trigger", one(right, true), () => Pad(0f, 1f, rb));
        yield return Fires("left_bumper_and_trigger", one(left, true), () => Pad(1f, 0f, lb));
        yield return Fires("both_triggers", both(false), () => Pad(1f, 1f));
        yield return Fires("both_triggers_a_bumper", both(true), () => Pad(1f, 1f, lb));
        yield return Fires("second_trigger_a_moment_late", both(false), () => Pad(0f, 1f), () => Pad(1f, 1f));
        yield return Fires("right_click", one(right, false), () => Click(false, MouseButton.Right));
        yield return Fires("left_click", one(left, false), () => Click(false, MouseButton.Left));
        yield return Fires("shift_right_click", one(right, true), () => Click(true, MouseButton.Right));
        yield return Fires("shift_left_click", one(left, true), () => Click(true, MouseButton.Left));
        yield return Fires("both_clicks", both(false), () => Click(false, MouseButton.Left, MouseButton.Right));
        yield return Fires("shift_both_clicks", both(true), () => Click(true, MouseButton.Left, MouseButton.Right));
    }

    protected override IEnumerator Run()
    {
        var world = Core;
        var site = world.HoldSite;
        Check("the_map_has_a_hold", site != null);
        if (site == null) yield break;
        row = world.BuildingRowOf(site.Castle);
        controls = caster.controls;
        controls.enabled = true;
        caster.enabled = true;
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        pad = InputSystem.AddDevice<Gamepad>("Hands pad");
        mouse = InputSystem.AddDevice<Mouse>("Hands mouse");
        keys = InputSystem.AddDevice<Keyboard>("Hands keys");
        var wall = grid.CellCenter(row.X + WallX, row.Y + WallY - AimNorth);
        caster.AimOverride = new Vector3(wall.x, grid.GroundAt(wall), wall.z);
        heard.StartFrom(world);
        Check("off_outside_the_hold", !world.HandsOn(Me) && !controls.HandPad);

        Stand(new Vector2Int(site.Plate.x, site.Plate.y));
        for (float t = 0f; t < MostWait && !world.HoldRunning; t += Time.deltaTime) yield return null;
        Check("a_run_starts", world.HoldRunning);
        if (!world.HoldRunning) yield break;
        world.Hold.Health = int.MaxValue / 2; // the run outlasts the probe

        var hud = grid.GetComponent<HoldShow>();
        yield return Attune("air", "fire");
        Check("the_hud_says_the_hands", hud.HandsLine(Me).Contains("air") && hud.HandsLine(Me).Contains("fire"), hud.HandsLine(Me));
        var held = world.HandsOf(Me).Element[HandsDef.Right];
        yield return Cooling();
        Check("off_the_room_a_press_attunes_nothing", world.HandsOf(Me).Element[HandsDef.Right] == held);
        yield return Inputs("air", "fire");

        var kit = world.SelectedDpad(Me);
        Check("the_kit_is_the_rows", world.DpadOf(Me) == Table.Kit, kit?.Name);
        yield return Fires("dpad_up_casts_the_kit", kit?.Spell, () => InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(GamepadButton.DpadUp)));

        yield return Attune("fire", "fire");
        yield return Fires("same_element_both_triggers", Table.Spell("fire", "fire", false).Spell, () => Pad(1f, 1f));
        yield return Fires("same_element_a_bumper", Table.Spell("fire", "fire", true).Spell, () => Pad(1f, 1f, GamepadButton.RightShoulder));

        caster.AimOverride = null;
        caster.enabled = false;
        InputSystem.RemoveDevice(pad);
        InputSystem.RemoveDevice(mouse);
        InputSystem.RemoveDevice(keys);
    }
}
