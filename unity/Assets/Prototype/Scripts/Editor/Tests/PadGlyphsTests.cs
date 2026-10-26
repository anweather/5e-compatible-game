using NUnit.Framework;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.DualShock;
using UnityEngine.InputSystem.XInput;

// PadGlyphs: each kind of controller is told apart and its buttons get the
// names printed on them, and the hints follow the pad used last. Test pads
// are added and made current here; a real controller plugged into the
// machine is put back as current afterwards.
public class PadGlyphsTests
{
    Gamepad before;

    [SetUp]
    public void SetUp() => before = Gamepad.current;

    [TearDown]
    public void TearDown() => before?.MakeCurrent();

    static T Add<T>() where T : InputDevice => InputSystem.AddDevice<T>("Glyph test pad");

    [Test]
    public void TellsTheFamiliesApart()
    {
        var ds4 = Add<DualShock4GamepadHID>();
        var ds5 = Add<DualSenseGamepadHID>();
        var xbox = InputSystem.AddDevice("XInputController");
        var plain = Add<Gamepad>();
        try
        {
            Assert.That(PadGlyphs.FamilyOf(ds4), Is.EqualTo(PadFamily.PlayStation4));
            Assert.That(PadGlyphs.FamilyOf(ds5), Is.EqualTo(PadFamily.PlayStation5));
            Assert.That(xbox, Is.InstanceOf<XInputController>());
            Assert.That(PadGlyphs.FamilyOf(xbox), Is.EqualTo(PadFamily.Xbox));
            Assert.That(PadGlyphs.FamilyOf(plain), Is.EqualTo(PadFamily.Generic));
            Assert.That(PadGlyphs.FamilyOf(null), Is.EqualTo(PadFamily.Generic));
        }
        finally
        {
            foreach (var d in new InputDevice[] { ds4, ds5, xbox, plain }) InputSystem.RemoveDevice(d);
        }
    }

    [Test]
    public void TellsPadsWithoutTheirOwnLayoutApartByName()
    {
        Assert.That(PadGlyphs.FamilyOf("SwitchProControllerHID", "Pro Controller", "Nintendo Co., Ltd.", false), Is.EqualTo(PadFamily.Switch));
        Assert.That(PadGlyphs.FamilyOf("Gamepad", "Steam Deck Controller", "Valve Software", false), Is.EqualTo(PadFamily.Steam));
        Assert.That(PadGlyphs.FamilyOf("Gamepad", "Steam Controller", "Valve", true), Is.EqualTo(PadFamily.Steam));
        Assert.That(PadGlyphs.FamilyOf("Gamepad", "Wireless Controller", "Sony Interactive Entertainment", false), Is.EqualTo(PadFamily.PlayStation4));
        Assert.That(PadGlyphs.FamilyOf("Gamepad", "DualSense Wireless Controller", "Sony Interactive Entertainment", false), Is.EqualTo(PadFamily.PlayStation5));
        Assert.That(PadGlyphs.FamilyOf("XInputControllerWindows", null, null, true), Is.EqualTo(PadFamily.Xbox));
        Assert.That(PadGlyphs.FamilyOf("Gamepad", "Some Pad", "Someone", false), Is.EqualTo(PadFamily.Generic));
    }

    [Test]
    public void LabelsAreThePrintedNames()
    {
        Assert.That(PadGlyphs.Label(PadFamily.Xbox, PadButton.South), Is.EqualTo("A"));
        Assert.That(PadGlyphs.Label(PadFamily.PlayStation5, PadButton.South), Is.EqualTo("✕"));
        Assert.That(PadGlyphs.Label(PadFamily.PlayStation5, PadButton.Select), Is.EqualTo("Create"));
        Assert.That(PadGlyphs.Label(PadFamily.PlayStation4, PadButton.Select), Is.EqualTo("Share"));
        // A Switch pad's bottom button is B: the Input System names buttons by place, Nintendo swaps the letters.
        Assert.That(PadGlyphs.Label(PadFamily.Switch, PadButton.South), Is.EqualTo("B"));
        Assert.That(PadGlyphs.Label(PadFamily.Switch, PadButton.East), Is.EqualTo("A"));
        Assert.That(PadGlyphs.Label(PadFamily.Steam, PadButton.LB), Is.EqualTo("L1"));
    }

    [Test]
    public void EveryFamilyHasEveryButton()
    {
        foreach (PadFamily f in System.Enum.GetValues(typeof(PadFamily)))
            foreach (PadButton b in System.Enum.GetValues(typeof(PadButton)))
                Assert.That(PadGlyphs.Label(f, b), Is.Not.Empty, $"{f} {b}");
    }

    [Test]
    public void TextFillsInEveryToken()
    {
        string all = "{South} {East} {West} {North} {LB} {RB} {LT} {RT} {Start} {Select}";
        Assert.That(PadGlyphs.Text(PadFamily.PlayStation4, all), Is.EqualTo("✕ ○ □ △ L1 R1 L2 R2 Options Share"));
        Assert.That(PadGlyphs.Text(PadFamily.Xbox, "hold {LB}: spells"), Is.EqualTo("hold LB: spells"));
    }

    [Test]
    public void FollowsThePadUsedLast()
    {
        const string hint = "{East}: cancel";
        var ds = Add<DualSenseGamepadHID>();
        var xbox = InputSystem.AddDevice("XInputController");
        try
        {
            ((Gamepad)xbox).MakeCurrent();
            Assert.That(PadGlyphs.Text(hint), Is.EqualTo("B: cancel"));
            ds.MakeCurrent();
            Assert.That(PadGlyphs.Current, Is.EqualTo(PadFamily.PlayStation5));
            Assert.That(PadGlyphs.Text(hint), Is.EqualTo("○: cancel"));
        }
        finally
        {
            InputSystem.RemoveDevice(ds);
            InputSystem.RemoveDevice(xbox);
        }
    }
}
