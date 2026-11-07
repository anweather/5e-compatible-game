using UnityEngine;
using UnityEngine.InputSystem;

// The Esc menu's own controls (PauseMenu), beside the wizard's
// (PlayerControls), which are switched off while the menu is open. Like
// PlayerControls, each action lists its keyboard and gamepad bindings in one
// place; Xbox and PlayStation controllers both map to <Gamepad>.
//
//   Open / close menu   Esc           Start / Options
//   Mods page           M
//   Move highlight      ↑ ↓ or W S    d-pad or left stick up / down (hold to repeat)
//   Choose              Enter         A / Cross
//   Back                Backspace     B / Circle    (closes the menu on its main page)
//   Scroll                            right stick up / down
//
// The mouse clicks the menu's buttons as before; nothing here is bound to Tab,
// the bumpers or the d-pad's left and right, which the wizard's spell controls
// use. Navigation only does anything while the menu is open, and none while a
// text field on a page has the keys (PauseMenu.FieldHasKeys), so W, S and Enter
// stay letters and a new line there.
public sealed class MenuControls : System.IDisposable
{
    public InputAction Menu { get; }
    public InputAction Mods { get; }
    public InputAction Up { get; }
    public InputAction Down { get; }
    public InputAction Submit { get; }
    public InputAction Back { get; }
    public InputAction Scroll { get; }

    readonly InputAction[] all;
    int heldDirection;
    float heldFor;

    public MenuControls()
    {
        Menu = Button("Menu", "<Keyboard>/escape", "<Gamepad>/start");
        Mods = Button("Mods page", "<Keyboard>/m");
        Up = Button("Menu up", "<Gamepad>/dpad/up", "<Gamepad>/leftStick/up", "<Keyboard>/upArrow", "<Keyboard>/w");
        Down = Button("Menu down", "<Gamepad>/dpad/down", "<Gamepad>/leftStick/down", "<Keyboard>/downArrow", "<Keyboard>/s");
        Submit = Button("Menu choose", "<Gamepad>/buttonSouth", "<Keyboard>/enter", "<Keyboard>/numpadEnter");
        Back = Button("Menu back", "<Gamepad>/buttonEast", "<Keyboard>/backspace");
        Scroll = new InputAction("Menu scroll", InputActionType.Value, "<Gamepad>/rightStick/y");
        all = new[] { Menu, Mods, Up, Down, Submit, Back, Scroll };
        foreach (var a in all) a.Enable();
    }

    static InputAction Button(string name, params string[] bindings)
    {
        var action = new InputAction(name, InputActionType.Button);
        foreach (var b in bindings) action.AddBinding(b);
        return action;
    }

    /// <summary>True if the action was pressed this frame on a gamepad (Start rather than Esc).</summary>
    public static bool PressedOnPad(InputAction action) =>
        action.WasPressedThisFrame() && action.activeControl != null && action.activeControl.device is Gamepad;

    /// <summary>
    /// Steps for the highlight this frame: +1 down, -1 up, 0 none. A press steps at once;
    /// holding repeats (MenuNavigator.RepeatSteps).
    /// </summary>
    public int Steps()
    {
        int direction = (Down.IsPressed() ? 1 : 0) - (Up.IsPressed() ? 1 : 0);
        if (direction != heldDirection)
        {
            heldDirection = direction;
            heldFor = 0f;
            return direction;
        }
        if (direction == 0) return 0;
        float before = heldFor;
        heldFor += Time.unscaledDeltaTime;
        return MenuNavigator.RepeatSteps(heldFor) > MenuNavigator.RepeatSteps(before) ? direction : 0;
    }

    public void Dispose()
    {
        foreach (var a in all) a.Dispose();
    }
}
