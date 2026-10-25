using UnityEngine;
using UnityEngine.InputSystem;

// Our own controls, beside the Starter Assets ones (move, look, jump and
// sprint already work on keyboard, mouse and gamepad). Each action lists
// its keyboard/mouse and gamepad bindings in one place; Xbox and
// PlayStation controllers both map to <Gamepad>.
//
//   Cast spell   left mouse    right trigger
//   Next spell   E             right bumper (R1)
//   Prev spell   Q             left bumper (L1)
//   Pick spell   1-8
//   Zoom camera  mouse wheel   d-pad up / down
//   Reset        R             View / Share button
public class PlayerControls : MonoBehaviour
{
    public InputAction Cast { get; private set; }
    public InputAction NextSpell { get; private set; }
    public InputAction PrevSpell { get; private set; }
    public InputAction PickSpell { get; private set; }
    public InputAction Zoom { get; private set; }
    public InputAction Reset { get; private set; }

    /// <summary>True when the last control the player touched was on a gamepad.</summary>
    public bool UsingGamepad { get; private set; }

    InputAction[] all;

    void Awake()
    {
        Cast = Button("Cast", "<Mouse>/leftButton", "<Gamepad>/rightTrigger");
        NextSpell = Button("Next spell", "<Keyboard>/e", "<Gamepad>/rightShoulder");
        PrevSpell = Button("Previous spell", "<Keyboard>/q", "<Gamepad>/leftShoulder");
        PickSpell = Button("Pick spell", "<Keyboard>/1", "<Keyboard>/2", "<Keyboard>/3", "<Keyboard>/4", "<Keyboard>/5", "<Keyboard>/6", "<Keyboard>/7", "<Keyboard>/8");
        Reset = Button("Reset", "<Keyboard>/r", "<Gamepad>/select");

        Zoom = new InputAction("Zoom", InputActionType.Value);
        Zoom.AddBinding("<Mouse>/scroll/y");
        Zoom.AddCompositeBinding("1DAxis")
            .With("Negative", "<Gamepad>/dpad/up")
            .With("Positive", "<Gamepad>/dpad/down");

        all = new[] { Cast, NextSpell, PrevSpell, PickSpell, Zoom, Reset };
        foreach (var a in all) a.performed += Track;
    }

    static InputAction Button(string name, params string[] bindings)
    {
        var action = new InputAction(name, InputActionType.Button);
        foreach (var b in bindings) action.AddBinding(b);
        return action;
    }

    void Track(InputAction.CallbackContext ctx) => UsingGamepad = ctx.control.device is Gamepad;

    // Movement and look belong to Starter Assets, so watch the devices too.
    void Update()
    {
        var pad = Gamepad.current;
        if (pad != null && (pad.leftStick.ReadValue().sqrMagnitude > 0.04f || pad.rightStick.ReadValue().sqrMagnitude > 0.04f || pad.buttonSouth.wasPressedThisFrame))
            UsingGamepad = true;
        else if ((Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame) || (Mouse.current != null && Mouse.current.delta.ReadValue().sqrMagnitude > 1f))
            UsingGamepad = false;
    }

    /// <summary>The spell slot (0-7) of the number key pressed this frame, or -1.</summary>
    public int PickedSlot()
    {
        if (!PickSpell.WasPressedThisFrame() || PickSpell.activeControl == null) return -1;
        return int.TryParse(PickSpell.activeControl.name, out int n) ? n - 1 : -1;
    }

    void OnEnable() { foreach (var a in all) a.Enable(); }
    void OnDisable() { foreach (var a in all) a.Disable(); }

    void OnDestroy()
    {
        foreach (var a in all)
        {
            a.performed -= Track;
            a.Dispose();
        }
    }
}
