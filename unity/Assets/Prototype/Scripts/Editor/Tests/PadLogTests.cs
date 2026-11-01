using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Layouts;

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

    // Counted from the log itself: once a play session has run, PadLog's device hook stays on
    // in the Editor, so adding a pad here may or may not log its own line.
    [Test]
    public void ThePadInUseMovingIsLoggedOnce()
    {
        var inUse = new List<string>();
        Application.LogCallback note = (line, _, _) =>
        {
            if (line.StartsWith("Input device InUse")) inUse.Add(line);
        };
        var first = (Gamepad)InputSystem.AddDevice("Gamepad", "Log test first");
        var second = (Gamepad)InputSystem.AddDevice("Gamepad", "Log test second");
        Application.logMessageReceived += note;
        try
        {
            first.MakeCurrent();
            _ = PadGlyphs.Current;
            second.MakeCurrent();
            _ = PadGlyphs.Current;
            _ = PadGlyphs.Current;
            Assert.That(inUse.Count, Is.EqualTo(2), string.Join("\n", inUse));
            StringAssert.IsMatch($"^Input device InUse: id={first.deviceId} ", inUse[0]);
            StringAssert.IsMatch($"^Input device InUse: id={second.deviceId} kind=Gamepad .* Gamepad.current={second.deviceId} .* glyphs=Generic$", inUse[1]);
        }
        finally
        {
            Application.logMessageReceived -= note;
            InputSystem.RemoveDevice(first);
            InputSystem.RemoveDevice(second);
        }
    }
}
