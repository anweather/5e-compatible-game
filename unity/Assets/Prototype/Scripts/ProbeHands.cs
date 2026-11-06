using System.Collections;
using System.Linq;
using Chem;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

// The probe's hands scenario (docs/design/the-hold.md, phase 3): every hand input through real devices, added here, with what
// goes off read from the core. The wall cell it stands on and the cell it aims at are this map's; what each input should cast
// comes from the rules' table. Nothing here is in the game.
public class HandsProbe : ProbeScenario
{
    const int WallX = 21, WallY = 3, AimNorth = 6, AimFar = 60;
    const float MostWait = 8f;

    Gamepad pad;
    Mouse mouse;
    Keyboard keys;
    PlayerControls controls;
    readonly FiredCastsHeard heard = new();
    BuildingRow row;
    HandPose body;
    int leftArm, rightArm;
    bool together;

    // What the arms played of a press: each its gesture, the two as one.
    void Watch()
    {
        if (body.Playing(HandsDef.Left) != HandsDef.NoGesture) leftArm = body.Playing(HandsDef.Left);
        if (body.Playing(HandsDef.Right) != HandsDef.NoGesture) rightArm = body.Playing(HandsDef.Right);
        together |= body.Together;
    }

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

    // A press, held for a few frames, let go, and what the core sets off, spells and acts; then the hands cool down.
    IEnumerator Fires(string name, string[] wanted, System.Action press, System.Action second = null)
    {
        heard.StartFrom(Core);
        int started = caster.CastsStarted;
        body = wizard.GetComponent<HandPose>();
        leftArm = rightArm = HandsDef.NoGesture;
        together = false;
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
        var fired = new System.Collections.Generic.List<string>();
        for (float t = 0f; t < MostWait && fired.Count == 0; t += Time.deltaTime)
        {
            Watch();
            foreach (var f in heard.Take(Core))
                if (f.Player == Me) fired.Add(f.Spell ?? f.Act);
            yield return null;
        }
        fired.Sort(System.StringComparer.Ordinal);
        var sorted = wanted.OrderBy(w => w, System.StringComparer.Ordinal);
        Check(name, fired.SequenceEqual(sorted), $"{string.Join("+", fired)}, wanted {string.Join("+", sorted)}");
        yield return Wait(0.3f);
        int spells = wanted.Count(w => Core.Rules.Spell(w) != null);
        Check(name + "_cast", caster.CastsStarted - started == spells, $"{caster.CastsStarted - started} casts of {spells}");
        for (float t = 0f; t < MostWait && (Core.HandCooling(Me, HandsDef.Left) > 0 || Core.HandCooling(Me, HandsDef.Right) > 0); t += Time.deltaTime) yield return null;
    }

    IEnumerator Fires(string name, string wanted, System.Action press, System.Action second = null) => Fires(name, new[] { wanted }, press, second);

    void Pad(float left, float right, params GamepadButton[] held)
    {
        var state = new GamepadState { leftTrigger = left, rightTrigger = right };
        foreach (var b in held) state = state.WithButton(b);
        InputSystem.QueueStateEvent(pad, state);
    }

    // The mouse's clicks with the keyboard's bumpers, Q and C.
    void Keys(bool left, bool right, params MouseButton[] held)
    {
        var state = new MouseState();
        foreach (var b in held) state = state.WithButton(b);
        var keyboard = new KeyboardState();
        if (left) keyboard.Set(Key.Q, true);
        if (right) keyboard.Set(Key.C, true);
        InputSystem.QueueStateEvent(keys, keyboard);
        InputSystem.QueueStateEvent(mouse, state);
    }

    IEnumerator AttuneTo(string element, int hand, System.Action press)
    {
        for (int i = 0; i <= Core.Rules.Combos.Elements.Count && Core.HandsOf(Me).Holds[hand] != element; i++)
        {
            press();
            yield return null;
            yield return null;
            Let();
            yield return Wait(0.4f);
        }
        Check($"attuned_{hand}_to_{element ?? "nothing"}", Core.HandsOf(Me).Holds[hand] == element, Core.HandsOf(Me).Holds[hand]);
        Check($"it_shows_{hand}_{element ?? "nothing"}", wizard.GetComponent<HandPose>().Showing(hand) == element, wizard.GetComponent<HandPose>().Showing(hand));
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
        yield return AttuneTo(right, HandsDef.Right, () => Keys(false, false, MouseButton.Right));
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
        const MouseButton lc = MouseButton.Left, rc = MouseButton.Right;
        string one(string e, int g) => Table.One(e, g).Spell;
        string both(int g) => Table.Both(left, right, g).Spell;
        yield return Fires("right_trigger", one(right, HandsDef.Thrust), () => Pad(0f, 1f));
        yield return Fires("left_trigger", one(left, HandsDef.Thrust), () => Pad(1f, 0f));
        yield return Fires("right_bumper", one(right, HandsDef.Throw), () => Pad(0f, 0f, rb));
        yield return Fires("left_bumper", one(left, HandsDef.Throw), () => Pad(0f, 0f, lb));
        yield return Fires("right_trigger_and_bumper", one(right, HandsDef.Sweep), () => Pad(0f, 1f, rb));
        yield return Fires("left_trigger_and_bumper", one(left, HandsDef.Sweep), () => Pad(1f, 0f, lb));
        yield return Fires("trigger_then_bumper_a_moment_late", one(right, HandsDef.Sweep), () => Pad(0f, 1f), () => Pad(0f, 1f, rb));
        yield return Fires("both_triggers", both(HandsDef.Thrust), () => Pad(1f, 1f));
        Check("the_same_gesture_is_two_handed", together && leftArm == HandsDef.Thrust && rightArm == HandsDef.Thrust, $"{leftArm} {rightArm} {together}");
        yield return Fires("both_bumpers", both(HandsDef.Throw), () => Pad(0f, 0f, lb, rb));
        yield return Fires("second_trigger_a_moment_late", both(HandsDef.Thrust), () => Pad(0f, 1f), () => Pad(1f, 1f));
        yield return Fires("different_gestures_are_both", new[] { one(right, HandsDef.Thrust), one(left, HandsDef.Throw) }, () => Pad(0f, 1f, lb));
        Check("each_arm_plays_its_own", !together && leftArm == HandsDef.Throw && rightArm == HandsDef.Thrust, $"{leftArm} {rightArm} {together}");
        yield return Fires("both_sweeps_with_no_row_are_both", new[] { one(right, HandsDef.Sweep), one(left, HandsDef.Sweep) }, () => Pad(1f, 1f, lb, rb));
        yield return Fires("right_click", one(right, HandsDef.Thrust), () => Keys(false, false, rc));
        yield return Fires("left_click", one(left, HandsDef.Thrust), () => Keys(false, false, lc));
        yield return Fires("c_key", one(right, HandsDef.Throw), () => Keys(false, true));
        yield return Fires("q_key", one(left, HandsDef.Throw), () => Keys(true, false));
        yield return Fires("right_click_and_c", one(right, HandsDef.Sweep), () => Keys(false, true, rc));
        yield return Fires("left_click_and_q", one(left, HandsDef.Sweep), () => Keys(true, false, lc));
        yield return Fires("both_clicks", both(HandsDef.Thrust), () => Keys(false, false, lc, rc));
        yield return Fires("q_and_c", both(HandsDef.Throw), () => Keys(true, true));
    }

    // The bumper's spell aimed past the long range, and the trigger's past the short: they land inside them.
    IEnumerator Ranges(string holds, Vector2Int me)
    {
        var far = grid.CellCenter(me.x, me.y - AimFar);
        caster.AimOverride = new Vector3(far.x, grid.GroundAt(far), far.z);
        yield return Fires("aimed_past_the_range", Table.One(holds, HandsDef.Throw).Spell, () => Pad(0f, 0f, GamepadButton.RightShoulder));
        var landed = caster.LastHandAim;
        Metric("thrown_landed_cells", Vector2Int.Distance(landed, me));
        Check("it_lands_within_the_range", Vector2Int.Distance(landed, me) <= Table.RangeOf(HandsDef.Throw) + 1, $"{landed} from {me}");
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
        var aim = new Vector3(wall.x, grid.GroundAt(wall), wall.z);
        caster.AimOverride = aim;
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
        var held = world.HandsOf(Me).Holds[HandsDef.Right];
        yield return Cooling();
        Check("off_the_room_a_press_attunes_nothing", world.HandsOf(Me).Holds[HandsDef.Right] == held);
        yield return Inputs("air", "fire");

        var kit = world.SelectedDpad(Me);
        Check("the_kit_is_the_rows", world.DpadOf(Me) == Table.Kit, kit?.Name);
        yield return Fires("dpad_up_casts_the_kit", kit?.Spell, () => InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(GamepadButton.DpadUp)));

        yield return Ranges("fire", grid.CellAt(wizard.transform.position));
        caster.AimOverride = aim;

        yield return Attune("fire", "fire");
        yield return Fires("same_element_both_triggers", Table.Both("fire", "fire", HandsDef.Thrust).Spell, () => Pad(1f, 1f));
        yield return Fires("same_element_both_bumpers", Table.Both("fire", "fire", HandsDef.Throw).Spell, () => Pad(0f, 0f, GamepadButton.LeftShoulder, GamepadButton.RightShoulder));

        yield return Attune(null, "fire");
        var holder = wizard.GetComponent<ItemHolder>();
        int fists = 0;
        System.Action<ItemHolder.PunchLanding> landed = _ => fists++;
        holder.PunchLanded += landed;
        yield return Fires("an_empty_trigger_punches", Table.One(null, HandsDef.Thrust).Act, () => Pad(1f, 0f));
        yield return Wait(1f);
        Check("the_fist_lands", fists == 1, $"{fists} landed");
        yield return Fires("an_empty_bumper_throws", Table.One(null, HandsDef.Throw).Act, () => Pad(0f, 0f, GamepadButton.LeftShoulder));
        yield return Fires("an_empty_sweep_shoves", Table.One(null, HandsDef.Sweep).Act, () => Pad(1f, 0f, GamepadButton.LeftShoulder));
        yield return Wait(1f);
        Check("the_sweep_lands_too", fists == 2, $"{fists} landed");
        holder.PunchLanded -= landed;

        caster.AimOverride = null;
        caster.enabled = false;
        InputSystem.RemoveDevice(pad);
        InputSystem.RemoveDevice(mouse);
        InputSystem.RemoveDevice(keys);
    }
}
