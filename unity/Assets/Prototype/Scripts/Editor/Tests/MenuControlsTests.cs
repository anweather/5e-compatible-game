using System.Linq;
using NUnit.Framework;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

// The Esc menu's bindings (MenuControls), resolved against a virtual gamepad:
// Start opens the menu, the d-pad and left stick move, A chooses, B goes
// back, and none of them touch the spell buttons (Tab, the bumpers, the
// d-pad's left and right). The CLI's simulated keys don't fire press actions
// and EditMode doesn't feed them to actions either, so this checks what each
// action is bound to; pressing them is left to the probe and real hardware.
public class MenuControlsTests
{
    Gamepad pad;
    MenuControls controls;

    [SetUp]
    public void SetUp()
    {
        pad = InputSystem.AddDevice<Gamepad>("Menu test pad");
        controls = new MenuControls();
    }

    [TearDown]
    public void TearDown()
    {
        controls.Dispose();
        InputSystem.RemoveDevice(pad);
    }

    static bool Bound(InputAction action, InputControl control) => action.controls.Contains(control);

    [Test]
    public void GamepadButtonsReachTheirActions()
    {
        Assert.That(Bound(controls.Menu, pad.startButton), Is.True, "Start");
        Assert.That(Bound(controls.Submit, pad.buttonSouth), Is.True, "A / Cross");
        Assert.That(Bound(controls.Back, pad.buttonEast), Is.True, "B / Circle");
        Assert.That(Bound(controls.Up, pad.dpad.up) && Bound(controls.Up, pad.leftStick.up), Is.True, "up");
        Assert.That(Bound(controls.Down, pad.dpad.down) && Bound(controls.Down, pad.leftStick.down), Is.True, "down");
        Assert.That(Bound(controls.Scroll, pad.rightStick.y), Is.True, "scroll");
    }

    [Test]
    public void KeyboardKeepsEscAndM()
    {
        var keys = Keyboard.current ?? InputSystem.AddDevice<Keyboard>();
        Assert.That(Bound(controls.Menu, keys.escapeKey), Is.True);
        Assert.That(Bound(controls.Mods, keys.mKey), Is.True);
    }

    [Test]
    public void LeavesTheSpellButtonsAlone()
    {
        var taken = new InputControl[] { pad.dpad.left, pad.dpad.right, pad.leftShoulder, pad.rightShoulder };
        var keys = Keyboard.current;
        foreach (var a in new[] { controls.Menu, controls.Mods, controls.Up, controls.Down, controls.Submit, controls.Back, controls.Scroll })
        {
            foreach (var c in taken) Assert.That(Bound(a, c), Is.False, $"{a.name} on {c.path}");
            if (keys != null) Assert.That(Bound(a, keys.tabKey), Is.False, $"{a.name} on Tab");
        }
    }

    [Test]
    public void OnlyThePadsStartCountsAsAPadPress()
    {
        // This test's own pad: a real controller plugged into the machine has its own Start too.
        Assert.That(controls.Menu.controls.OfType<ButtonControl>().Count(c => c.device == pad), Is.EqualTo(1));
    }
}
