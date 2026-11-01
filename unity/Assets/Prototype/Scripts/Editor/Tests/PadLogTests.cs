using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Layouts;
using UnityEngine.TestTools;

// PadLog: the line a player's log gets for each device. Test devices stand in
// for a pad and a joystick with a Linux description; they aren't Linux pads,
// and show only that the line reports what a device says about itself.
public class PadLogTests
{
    Gamepad before;

    [SetUp]
    public void SetUp() => before = Gamepad.current;

    [TearDown]
    public void TearDown() => before?.MakeCurrent();

    static InputDeviceDescription Linux(string product, string capabilities = null) => new InputDeviceDescription
    {
        interfaceName = "Linux",
        product = product,
        manufacturer = "Maker",
        capabilities = capabilities,
    };

    [Test]
    public void TheLineSaysWhatAGamepadIsAndThatItIsCurrent()
    {
        var pad = (Gamepad)InputSystem.AddDevice("Gamepad", "Log test pad");
        try
        {
            pad.MakeCurrent();
            InputSystem.SetDeviceUsage(pad, "Test");
            Assert.That(PadLog.Line("Added", pad, pad), Is.EqualTo(
                $"Input device Added: id={pad.deviceId} kind=Gamepad layout=\"Gamepad\" interface=\"\" product=\"\" manufacturer=\"\"" +
                $" usages=[Test] capabilities=\"\" Gamepad.current={pad.deviceId} \"\""));
        }
        finally
        {
            InputSystem.RemoveDevice(pad);
        }
    }

    [Test]
    public void AJoystickIsToldFromAGamepadAndNoCurrentPadIsSaid()
    {
        InputSystem.RegisterLayout<Joystick>("Log test stick", new InputDeviceMatcher().WithProduct("Log test stick"));
        var stick = InputSystem.AddDevice(Linux("Log test stick", "{\"controls\":\n[]}  "));
        try
        {
            string line = PadLog.Line("Added", stick, null);
            StringAssert.Contains("kind=Joystick", line);
            StringAssert.Contains("interface=\"Linux\" product=\"Log test stick\" manufacturer=\"Maker\"", line);
            StringAssert.Contains("capabilities=\"{\"controls\": []}\"", line);
            StringAssert.EndsWith("Gamepad.current=none", line);
        }
        finally
        {
            InputSystem.RemoveDevice(stick);
            InputSystem.RemoveLayout("Log test stick");
        }
    }

    [Test]
    public void LongCapabilitiesAreTrimmedAndNoDeviceStillMakesALine()
    {
        var pad = (Gamepad)InputSystem.AddDevice("Gamepad", "Log test pad");
        try
        {
            InputSystem.RegisterLayout<Joystick>("Log test long", new InputDeviceMatcher().WithProduct("Log test long"));
            var stick = InputSystem.AddDevice(Linux("Log test long", "{\"controls\":[],\"more\":\"" + new string('x', 5000) + "\"}"));
            StringAssert.EndsWith("xxx...\" Gamepad.current=none", PadLog.Line("Added", stick, null));
            InputSystem.RemoveDevice(stick);
            InputSystem.RemoveLayout("Log test long");
            Assert.That(PadLog.Line("Start", null, null), Is.EqualTo("Input device Start: no device Gamepad.current=none"));
        }
        finally
        {
            InputSystem.RemoveDevice(pad);
        }
    }

    [Test]
    public void ThePadInUseMovingIsLoggedOnce()
    {
        var first = (Gamepad)InputSystem.AddDevice("Gamepad", "Log test first");
        var second = (Gamepad)InputSystem.AddDevice("Gamepad", "Log test second");
        try
        {
            first.MakeCurrent();
            LogAssert.Expect(LogType.Log, new Regex($"^Input device InUse: id={first.deviceId} "));
            _ = PadGlyphs.Current;
            second.MakeCurrent();
            LogAssert.Expect(LogType.Log, new Regex($"^Input device InUse: id={second.deviceId} kind=Gamepad .* Gamepad.current={second.deviceId} .* glyphs=Generic$"));
            _ = PadGlyphs.Current;
            _ = PadGlyphs.Current;
            LogAssert.NoUnexpectedReceived();
        }
        finally
        {
            InputSystem.RemoveDevice(first);
            InputSystem.RemoveDevice(second);
        }
    }
}
