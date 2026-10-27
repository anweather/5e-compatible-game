using UnityEngine;
using UnityEngine.InputSystem;

// Our own controls, beside the Starter Assets ones (move, look, jump and
// sprint already work on keyboard, mouse and gamepad). Each action lists
// its keyboard/mouse and gamepad bindings in one place; Xbox and
// PlayStation controllers both map to <Gamepad>.
//
//   Cast spell   left mouse    right trigger
//   Spell radial hold Tab      hold left bumper (L1)   (SpellRadialHud: aim with the
//                                                        mouse / right stick, release to pick;
//                                                        a tap swaps to the previous spell)
//   Cancel radial (release     B / Circle (while it's open; otherwise B throws)
//                 in centre)
//   Next spell   E             right bumper (R1)       (in the current element)
//   Prev spell   Q
//   Element      1-8 (opens that element's spells)  d-pad left / right (steps)
//   Zoom camera  mouse wheel   d-pad up / down
//   Reset        R             View / Share button
//   Esc menu     Esc / M       Start / Options      (MenuControls, the menu's own;
//                                                    M opens it on the Mods page)
//   Pick up/drop E             X / Square
//   Use item     F             Y / Triangle
//   Dodge roll   Left Ctrl     left trigger (L2)       (DodgeRoll; sprint left it for the stick click)
//   Throw item   G or right    B / Circle      (hold to throw harder)
//                mouse
//
// E is both "next spell" and "pick up": while an item is in reach or in hand
// (ItemHolder sets ItemInReach), or a seat is (Sitter sets SeatInReach), it picks
// up, drops, sits or stands, and the spell stays put.
public class PlayerControls : MonoBehaviour
{
    public InputAction Cast { get; private set; }
    public InputAction RadialHold { get; private set; }
    public InputAction RadialCancel { get; private set; }
    public InputAction NextSpell { get; private set; }
    public InputAction PrevSpell { get; private set; }
    public InputAction PickElement { get; private set; }
    public InputAction NextElement { get; private set; }
    public InputAction PrevElement { get; private set; }
    public InputAction Zoom { get; private set; }
    public InputAction Reset { get; private set; }
    public InputAction Interact { get; private set; }
    public InputAction Use { get; private set; }
    public InputAction Throw { get; private set; }
    public InputAction Roll { get; private set; }

    /// <summary>Set by ItemHolder: an item is in reach or in hand, so the E key goes to it.</summary>
    public bool ItemInReach { get; set; }

    /// <summary>Set by Sitter: a seat is in reach or the wizard is sitting, so the E key goes to it.</summary>
    public bool SeatInReach { get; set; }

    /// <summary>True when the last control the player touched was on a gamepad.</summary>
    public bool UsingGamepad { get; private set; }

    InputAction[] all;

    void Awake()
    {
        Cast = Button("Cast", "<Mouse>/leftButton", "<Gamepad>/rightTrigger");
        RadialHold = Button("Spell radial", "<Keyboard>/tab", "<Gamepad>/leftShoulder");
        RadialCancel = Button("Cancel spell radial", "<Gamepad>/buttonEast");
        NextSpell = Button("Next spell", "<Keyboard>/e", "<Gamepad>/rightShoulder");
        PrevSpell = Button("Previous spell", "<Keyboard>/q");
        PickElement = Button("Pick element", "<Keyboard>/1", "<Keyboard>/2", "<Keyboard>/3", "<Keyboard>/4", "<Keyboard>/5", "<Keyboard>/6", "<Keyboard>/7", "<Keyboard>/8");
        NextElement = Button("Next element", "<Gamepad>/dpad/right");
        PrevElement = Button("Previous element", "<Gamepad>/dpad/left");
        Reset = Button("Reset", "<Keyboard>/r", "<Gamepad>/select");
        Interact = Button("Pick up or drop", "<Keyboard>/e", "<Gamepad>/buttonWest");
        Use = Button("Use item", "<Keyboard>/f", "<Gamepad>/buttonNorth");
        Throw = Button("Throw item", "<Keyboard>/g", "<Mouse>/rightButton", "<Gamepad>/buttonEast");
        Roll = Button("Dodge roll", "<Keyboard>/leftCtrl", "<Gamepad>/leftTrigger");

        Zoom = new InputAction("Zoom", InputActionType.Value);
        Zoom.AddBinding("<Mouse>/scroll/y");
        Zoom.AddCompositeBinding("1DAxis")
            .With("Negative", "<Gamepad>/dpad/up")
            .With("Positive", "<Gamepad>/dpad/down");

        all = new[] { Cast, RadialHold, RadialCancel, NextSpell, PrevSpell, PickElement, NextElement, PrevElement, Zoom, Reset, Interact, Use, Throw, Roll };
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

    /// <summary>Next spell was pressed this frame, unless it was the E key and E went to an item.</summary>
    public bool NextSpellPressed() =>
        NextSpell.WasPressedThisFrame() && !((ItemInReach || SeatInReach) && NextSpell.activeControl?.device is Keyboard);

    /// <summary>The element (0-7, radial order) of the number key pressed this frame, or -1.</summary>
    public int PickedElement()
    {
        if (!PickElement.WasPressedThisFrame() || PickElement.activeControl == null) return -1;
        return int.TryParse(PickElement.activeControl.name, out int n) ? n - 1 : -1;
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
