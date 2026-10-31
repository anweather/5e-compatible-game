using UnityEngine;
using UnityEngine.InputSystem;

// Our own controls, beside the Starter Assets ones (move, look, jump and
// sprint already work on keyboard, mouse and gamepad). Each action lists
// its keyboard/mouse and gamepad bindings in one place; Xbox and
// PlayStation controllers both map to <Gamepad>.
//
//   Casting by elements (docs/SPELL_COMBOS.md), the controller's layout (ElementPad):
//   Fire, Water  Z, X          left, right bumper
//   Earth, Air   C, V          left, right trigger
//   Cancel cast  B             B / Circle
//   D-pad spells [ ]           d-pad left / right    (T / d-pad up casts; J / d-pad down: the grimoire)
//   Dodge roll   Left Ctrl     click the right stick
//   Punch        F             B / Circle              (empty-handed and not casting: ItemHolder.Punch.cs)
//   Pick up/sit  E             X / Square
//   In an animal's shape any of the cast buttons changes back.
//
//   F2 swaps the controller to the debug layout below, the spell radial's:
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
    /// <summary>The pad's punch shares Circle with Cancel cast, which goes first (ItemHolder.MayPunch); Square only picks up and sits.</summary>
    public const string PadPunch = "<Gamepad>/buttonEast", PadInteract = "<Gamepad>/buttonWest";

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
    public InputAction Punch { get; private set; }
    public InputAction Throw { get; private set; }
    public InputAction Roll { get; private set; }
    /// <summary>The elements' buttons, in the order of rules/combos.json.</summary>
    public InputAction[] Elements { get; private set; }
    public InputAction CancelCast { get; private set; }
    public InputAction DpadNext { get; private set; }
    public InputAction DpadPrevious { get; private set; }
    public InputAction DpadCast { get; private set; }
    public InputAction Grimoire { get; private set; }

    /// <summary>
    /// The controller's layout: casting by elements (the default), or with false the spell radial's,
    /// kept as a debug tool. The two share buttons, so each turns the other's bindings off.
    /// </summary>
    public bool ElementPad
    {
        get => elementPad;
        set
        {
            elementPad = value;
            ApplyPadLayout();
        }
    }

    /// <summary>Set by ItemHolder: an item is in reach or in hand, so the E key goes to it.</summary>
    public bool ItemInReach { get; set; }

    /// <summary>Set by Sitter: a seat is in reach or the wizard is sitting, so the E key goes to it.</summary>
    public bool SeatInReach { get; set; }

    /// <summary>True when the last control the player touched was on a gamepad.</summary>
    public bool UsingGamepad { get; private set; }

    InputAction[] all;
    InputAction swapLayout;
    bool elementPad = true;
    // Each layout's own bindings, by action and index.
    (InputAction action, int binding)[] radialOnly, elementOnly;

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
        Interact = Button("Pick up or drop", "<Keyboard>/e", PadInteract);
        Use = Button("Use item", "<Keyboard>/f", "<Gamepad>/buttonNorth");
        Punch = Button("Punch", "<Keyboard>/f", PadPunch);
        Throw = Button("Throw item", "<Keyboard>/g", "<Mouse>/rightButton", "<Gamepad>/buttonEast");
        Roll = Button("Dodge roll", "<Keyboard>/leftCtrl", "<Gamepad>/leftTrigger", "<Gamepad>/rightStickPress");
        Elements = new[]
        {
            Button("Fire", "<Keyboard>/z", "<Gamepad>/leftShoulder"),
            Button("Water", "<Keyboard>/x", "<Gamepad>/rightShoulder"),
            Button("Earth", "<Keyboard>/c", "<Gamepad>/leftTrigger"),
            Button("Air", "<Keyboard>/v", "<Gamepad>/rightTrigger"),
        };
        CancelCast = Button("Cancel cast", "<Keyboard>/b", "<Gamepad>/buttonEast");
        DpadNext = Button("Next d-pad spell", "<Keyboard>/rightBracket", "<Gamepad>/dpad/right");
        DpadPrevious = Button("Previous d-pad spell", "<Keyboard>/leftBracket", "<Gamepad>/dpad/left");
        DpadCast = Button("Cast d-pad spell", "<Keyboard>/t", "<Gamepad>/dpad/up");
        Grimoire = Button("Grimoire", "<Keyboard>/j", "<Gamepad>/dpad/down");
        swapLayout = Button("Swap the controller's layout", "<Keyboard>/f2");

        Zoom = new InputAction("Zoom", InputActionType.Value);
        Zoom.AddBinding("<Mouse>/scroll/y");
        Zoom.AddCompositeBinding("1DAxis")
            .With("Negative", "<Gamepad>/dpad/up")
            .With("Positive", "<Gamepad>/dpad/down");

        all = new[]
        {
            Cast, RadialHold, RadialCancel, NextSpell, PrevSpell, PickElement, NextElement, PrevElement, Zoom, Reset, Interact, Use, Punch, Throw, Roll,
            Elements[0], Elements[1], Elements[2], Elements[3], CancelCast, DpadNext, DpadPrevious, DpadCast, Grimoire, swapLayout,
        };
        foreach (var a in all) a.performed += Track;

        const int ZoomPadUp = 2, ZoomPadDown = 3;
        radialOnly = new[] { (Cast, 1), (RadialHold, 1), (NextSpell, 1), (NextElement, 0), (PrevElement, 0), (Zoom, ZoomPadUp), (Zoom, ZoomPadDown), (Roll, 1) };
        elementOnly = new[]
        {
            (Elements[0], 1), (Elements[1], 1), (Elements[2], 1), (Elements[3], 1), (CancelCast, 1), (DpadNext, 1), (DpadPrevious, 1), (DpadCast, 1), (Grimoire, 1), (Roll, 2),
        };
        ApplyPadLayout();
    }

    // An override to an empty path switches a binding off.
    void ApplyPadLayout()
    {
        Switch(radialOnly, !elementPad);
        Switch(elementOnly, elementPad);
    }

    static void Switch((InputAction action, int binding)[] bindings, bool on)
    {
        foreach (var (action, binding) in bindings)
        {
            if (on) action.RemoveBindingOverride(binding);
            else action.ApplyBindingOverride(binding, "");
        }
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
        if (swapLayout.WasPressedThisFrame())
        {
            ElementPad = !elementPad;
            EventLog.Add(elementPad ? "Controller: casting by elements" : "Controller: the spell radial (debug)");
        }
        var pad = Gamepad.current;
        if (pad != null && (pad.leftStick.ReadValue().sqrMagnitude > 0.04f || pad.rightStick.ReadValue().sqrMagnitude > 0.04f || pad.buttonSouth.wasPressedThisFrame))
            UsingGamepad = true;
        else if ((Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame) || (Mouse.current != null && Mouse.current.delta.ReadValue().sqrMagnitude > 1f))
            UsingGamepad = false;
    }

    /// <summary>Next spell was pressed this frame, unless it was the E key and E went to an item.</summary>
    public bool NextSpellPressed() =>
        NextSpell.WasPressedThisFrame() && !((ItemInReach || SeatInReach) && NextSpell.activeControl?.device is Keyboard);

    /// <summary>A cast button was pressed this frame, of either way of casting: Cast, an element or the d-pad's cast.</summary>
    public bool AnyCastPressed()
    {
        foreach (var element in Elements)
            if (element.WasPressedThisFrame()) return true;
        return Cast.WasPressedThisFrame() || DpadCast.WasPressedThisFrame();
    }

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
