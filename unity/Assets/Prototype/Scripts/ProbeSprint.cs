using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

// The probe's sprint scenario (docs/FAST_LOOP.md): a virtual controller walks
// the wizard north, clicks the left stick to run (SprintToggle), lets go of
// the stick to stop, and tries the left trigger, which no longer sprints.
public class SprintProbe : ProbeScenario
{
    Gamepad pad;

    protected override IEnumerator Run()
    {
        pad = InputSystem.AddDevice<Gamepad>("ProbePad");
        var up = new Vector2(0f, 1f);

        Hold(up);
        yield return Wait(1.5f);
        float walk = 0f;
        yield return Speed(v => walk = v);

        Hold(up, GamepadButton.LeftStick);
        yield return null;
        yield return null;
        Hold(up);
        yield return Wait(1f);
        float run = 0f;
        yield return Speed(v => run = v);
        Check("click_runs", run > walk * 1.3f, $"walk {walk:0.0} m/s, after the click {run:0.0} m/s");

        Hold(Vector2.zero);
        yield return Wait(0.6f);
        Hold(up);
        yield return Wait(1.5f);
        float after = 0f;
        yield return Speed(v => after = v);
        Check("letting_go_stops", after < run * 0.8f, $"{after:0.0} m/s after letting go of the stick and pushing again");

        HoldTrigger(up);
        yield return Wait(1.5f);
        float trigger = 0f;
        yield return Speed(v => trigger = v);
        Check("trigger_free", trigger < run * 0.8f, $"{trigger:0.0} m/s with the left trigger held");

        Hold(Vector2.zero);
        yield return null;
    }

    protected override void OnDestroy()
    {
        if (pad != null) InputSystem.RemoveDevice(pad);
        base.OnDestroy();
    }

    void Hold(Vector2 stick, GamepadButton? button = null)
    {
        var state = new GamepadState { leftStick = stick };
        if (button.HasValue) state = state.WithButton(button.Value);
        InputSystem.QueueStateEvent(pad, state);
    }

    void HoldTrigger(Vector2 stick) => InputSystem.QueueStateEvent(pad, new GamepadState { leftStick = stick, leftTrigger = 1f });

    // The wizard's ground speed over half a second.
    IEnumerator Speed(System.Action<float> result)
    {
        var a = wizard.transform.position;
        yield return Wait(0.5f);
        var d = wizard.transform.position - a;
        d.y = 0f;
        result(d.magnitude / 0.5f);
    }
}
