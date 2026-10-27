using System;
using UnityEngine;
using UnityEngine.InputSystem;

// Sprint on a controller: click the left stick to start running, click again
// (or let go of the stick) to stop, rather than holding the left trigger, which
// is left free for something else. Shift on the keyboard still sprints while
// held. Starter Assets binds sprint to the left trigger in its own (pack) input
// actions, so that binding is overridden off here at runtime, and the toggle
// holds the Starter Assets input's sprint down (its SprintInput, reached by
// name as WizardRig does) while it's on. Only on the wizard whose PlayerInput
// is live (solo, or the owner's online).
[RequireComponent(typeof(PlayerInput))]
public class SprintToggle : MonoBehaviour
{
    // Let go of the stick this long and the run stops.
    const float StopAfter = 0.25f;

    PlayerInput input;
    Action<bool> sprint;
    bool unbound, running;
    float idle;

    void Start()
    {
        input = GetComponent<PlayerInput>();
        var starter = GetComponent(WizardRig.InputsComponent);
        if (starter != null) sprint = (Action<bool>)Delegate.CreateDelegate(typeof(Action<bool>), starter, "SprintInput", false, false);
    }

    void Update()
    {
        if (!input.enabled || sprint == null) return;
        if (!unbound) Unbind();
        var pad = Gamepad.current;
        if (pad == null) return;
        bool was = running;
        if (pad.leftStickButton.wasPressedThisFrame)
        {
            running = !running;
            idle = 0f; // clicked standing still, the stick has a moment to move before the run stops
        }
        idle = pad.leftStick.ReadValue().sqrMagnitude < 0.04f ? idle + Time.deltaTime : 0f;
        if (idle > StopAfter) running = false;
        // On, it holds sprint down every frame; switching off lets go once (Shift, if held, presses it again).
        if (running) sprint(true);
        else if (was) sprint(false);
    }

    // Takes the left trigger off sprint, leaving the keyboard's Shift.
    void Unbind()
    {
        unbound = true;
        if (input.actions == null) return;
        var action = input.actions.FindAction("Sprint");
        if (action == null) return;
        for (int i = 0; i < action.bindings.Count; i++)
            if (action.bindings[i].path.StartsWith("<Gamepad>", StringComparison.Ordinal))
                action.ApplyBindingOverride(i, "");
    }
}
