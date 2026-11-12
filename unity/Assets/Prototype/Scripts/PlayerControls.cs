using UnityEngine;
using UnityEngine.InputSystem;

// Our own controls, beside the Starter Assets ones (move, look, jump and
// sprint already work on keyboard, mouse and gamepad). Each action lists
// its keyboard/mouse and gamepad bindings in one place; Xbox and
// PlayStation controllers both map to <Gamepad>.
//
//   Casting with the hands (D16, docs/design/unified-casting.md), everywhere (HandPad):
//   Left hand    thrust, throw, sweep    left click, Q, both     LT, LB, both
//   Right hand   thrust, throw, sweep    right click, E, both    RT, RB, both
//                hold to charge, let go to fire; the same gesture on both hands can merge
//   Cancel cast  B                       B / Circle
//   Kit spells   [ ]   (T casts)         d-pad left / right   (up casts)
//   Hands menu                           hold d-pad down      (each stick points its hand at an element, let go attunes: HandsMenu)
//   Spell list   Tab                     (the Esc menu's Spells) (attune, the table, every spell)
//   Dodge roll   Left Ctrl               click the right stick
//   Punch        F                       B / Circle           (empty-handed and not casting: ItemHolder.Punch.cs)
//   Pick up/sit  E (near a thing)        X / Square
//   In an animal's shape any of the cast buttons changes back.
//
//   F2 swaps to the debug layout below, the spell radial's (ElementPad false):
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
//   Reset        R             View / Share button (calls the dragon; held 3 s, rebuilds at once)
//   Esc menu     Esc / M       Start / Options      (MenuControls, the menu's own;
//                                                    M opens it on the Mods page)
//   Pick up/drop E             X / Square
//   Use item     F             Y / Triangle
//   Dodge roll   Left Ctrl     left trigger (L2)       (DodgeRoll; sprint left it for the stick click)
//   Throw item   G or right    B / Circle      (hold to throw harder)
//                mouse
//
// E is the right hand's throw, "next spell" in the debug layout, and "pick up": while an
// item is in reach or in hand (ItemHolder sets ItemInReach), or a seat is (Sitter sets
// SeatInReach), it picks up, drops, sits or stands, and the hand and the spell stay put.
public class PlayerControls : MonoBehaviour
{
    /// <summary>The pad's punch shares Circle with Cancel cast, which goes first (ItemHolder.MayPunch); Square only picks up and sits.</summary>
    public const string PadPunch = "<Gamepad>/buttonEast", PadInteract = "<Gamepad>/buttonWest";

    public InputAction Cast { get; private set; }
    public InputAction RadialHold { get; private set; }
    public InputAction RadialCancel { get; private set; }
    public InputAction NextSpell { get; private set; }
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
    public InputAction CancelCast { get; private set; }
    public InputAction DpadNext { get; private set; }
    public InputAction DpadPrevious { get; private set; }
    public InputAction DpadCast { get; private set; }
    /// <summary>The d-pad's down: held, the hands menu (HandsMenu); without the hands, the spell list (SpellListMenu).</summary>
    public InputAction OpenSpellList { get; private set; }
    /// <summary>The hands (<see cref="HandPad"/>): each hand's two buttons, its thrust and its throw; both at once are its sweep.</summary>
    public InputAction ThrustLeft { get; private set; }
    public InputAction ThrustRight { get; private set; }
    public InputAction ThrowLeft { get; private set; }
    public InputAction ThrowRight { get; private set; }

    /// <summary>
    /// The layout: casting with the hands (the default), or with false the spell radial's, kept as a
    /// debug tool (F2). The two share buttons, so each turns the other's bindings off.
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

    /// <summary>
    /// The hands' layout, on wherever the rules have hands and the debug layout isn't (SpellCaster): the triggers, bumpers,
    /// clicks, Q and E are hands' gestures, so what else they do is off. The d-pad and Cancel stay.
    /// </summary>
    public bool HandPad
    {
        get => handPad;
        set
        {
            if (handPad == value) return;
            handPad = value;
            ApplyPadLayout();
        }
    }

    /// <summary>
    /// The Hold's offer between waves is open (ProgressKeeper): the pad's d-pad chooses an upgrade and takes it, so the kit's
    /// d-pad buttons are off until it closes. The keyboard's stay.
    /// </summary>
    public bool OfferPad
    {
        get => offerPad;
        set
        {
            if (offerPad == value) return;
            offerPad = value;
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
    bool elementPad = true, handPad, offerPad;
    // Each layout's own bindings, by action and index.
    (InputAction action, int binding)[] radialOnly, elementOnly, handsTake, kitDpad;

    void Awake()
    {
        Cast = Button("Cast", "<Mouse>/leftButton", "<Gamepad>/rightTrigger");
        RadialHold = Button("Spell radial", "<Keyboard>/tab", "<Gamepad>/leftShoulder");
        RadialCancel = Button("Cancel spell radial", "<Gamepad>/buttonEast");
        NextSpell = Button("Next spell", "<Keyboard>/e", "<Gamepad>/rightShoulder");
        PickElement = Button("Pick element", "<Keyboard>/1", "<Keyboard>/2", "<Keyboard>/3", "<Keyboard>/4", "<Keyboard>/5", "<Keyboard>/6", "<Keyboard>/7", "<Keyboard>/8");
        NextElement = Button("Next element", "<Gamepad>/dpad/right", "<Keyboard>/q");
        PrevElement = Button("Previous element", "<Gamepad>/dpad/left");
        Reset = Button("Reset", "<Keyboard>/r", "<Gamepad>/select");
        Interact = Button("Pick up or drop", "<Keyboard>/e", PadInteract);
        Use = Button("Use item", "<Keyboard>/f", "<Gamepad>/buttonNorth");
        Punch = Button("Punch", "<Keyboard>/f", PadPunch);
        Throw = Button("Throw item", "<Keyboard>/g", "<Mouse>/rightButton", "<Gamepad>/buttonEast");
        Roll = Button("Dodge roll", "<Keyboard>/leftCtrl", "<Gamepad>/leftTrigger", "<Gamepad>/rightStickPress");
        CancelCast = Button("Cancel cast", "<Keyboard>/b", "<Gamepad>/buttonEast");
        DpadNext = Button("Next d-pad spell", "<Keyboard>/rightBracket", "<Gamepad>/dpad/right");
        DpadPrevious = Button("Previous d-pad spell", "<Keyboard>/leftBracket", "<Gamepad>/dpad/left");
        DpadCast = Button("Cast d-pad spell", "<Keyboard>/t", "<Gamepad>/dpad/up");
        OpenSpellList = Button("Spell list", "<Gamepad>/dpad/down");
        swapLayout = Button("Swap the controls' layout", "<Keyboard>/f2");
        ThrustLeft = Button("Left hand's thrust", "<Mouse>/leftButton", "<Gamepad>/leftTrigger");
        ThrustRight = Button("Right hand's thrust", "<Mouse>/rightButton", "<Gamepad>/rightTrigger");
        ThrowLeft = Button("Left hand's throw", "<Keyboard>/q", "<Gamepad>/leftShoulder");
        ThrowRight = Button("Right hand's throw", "<Keyboard>/e", "<Gamepad>/rightShoulder");

        Zoom = new InputAction("Zoom", InputActionType.Value);
        Zoom.AddBinding("<Mouse>/scroll/y");
        Zoom.AddCompositeBinding("1DAxis")
            .With("Negative", "<Gamepad>/dpad/up")
            .With("Positive", "<Gamepad>/dpad/down");

        all = new[]
        {
            Cast, RadialHold, RadialCancel, NextSpell, PickElement, NextElement, PrevElement, Zoom, Reset, Interact, Use, Punch, Throw, Roll,
            CancelCast, DpadNext, DpadPrevious, DpadCast, OpenSpellList, swapLayout,
            ThrustLeft, ThrustRight, ThrowLeft, ThrowRight,
        };
        foreach (var a in all) a.performed += Track;

        const int ZoomPadUp = 2, ZoomPadDown = 3;
        radialOnly = new[] { (Cast, 1), (RadialHold, 1), (NextSpell, 1), (NextElement, 0), (PrevElement, 0), (Zoom, ZoomPadUp), (Zoom, ZoomPadDown), (Roll, 1) };
        elementOnly = new[]
        {
            (CancelCast, 1), (DpadNext, 1), (DpadPrevious, 1), (DpadCast, 1), (OpenSpellList, 0), (Roll, 2),
        };
        // The mouse's clicks, Q and E are the hands'; so are the radial's keyboard keys, which stay the debug layout's.
        handsTake = new[]
        {
            (Cast, 0), (Throw, 1), (NextSpell, 0), (NextElement, 1),
            (PickElement, 0), (PickElement, 1), (PickElement, 2), (PickElement, 3), (PickElement, 4), (PickElement, 5), (PickElement, 6), (PickElement, 7),
        };
        kitDpad = new[] { (DpadNext, 1), (DpadPrevious, 1), (DpadCast, 1), (OpenSpellList, 0) };
        ApplyPadLayout();
    }

    // An override to an empty path switches a binding off.
    void ApplyPadLayout()
    {
        Switch(radialOnly, !elementPad && !handPad);
        Switch(elementOnly, elementPad || handPad);
        if (offerPad) Switch(kitDpad, false);
        Switch(handsTake, !handPad);
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
    static readonly Unity.Profiling.ProfilerMarker DIAG_UpdateMarker = new("PlayerControls.Update");
    void Update()
    {
        using var diagMarked = DIAG_UpdateMarker.Auto();
        if (swapLayout.WasPressedThisFrame())
        {
            ElementPad = !elementPad;
            EventLog.Add(elementPad ? "Controls: casting with the hands" : "Controls: the spell radial (debug)");
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

    /// <summary>The right hand's throw was pressed this frame, unless it was the E key and E went to an item or a seat.</summary>
    public bool ThrowRightPressed() =>
        ThrowRight.WasPressedThisFrame() && !((ItemInReach || SeatInReach) && ThrowRight.activeControl?.device is Keyboard);

    /// <summary>A cast button was pressed this frame, of any way of casting: Cast, a hand or the d-pad's cast.</summary>
    public bool AnyCastPressed()
    {
        if (handPad && (ThrustLeft.WasPressedThisFrame() || ThrustRight.WasPressedThisFrame() || ThrowLeft.WasPressedThisFrame() || ThrowRightPressed())) return true;
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
