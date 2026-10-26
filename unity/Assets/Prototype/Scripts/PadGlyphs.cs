using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.DualShock;
using UnityEngine.InputSystem.XInput;

/// <summary>A gamepad button by where it sits, as the Input System names them (the bottom face button is South on every pad).</summary>
public enum PadButton { South, East, West, North, LB, RB, LT, RT, Start, Select }

/// <summary>The kinds of controller whose buttons are labelled differently.</summary>
public enum PadFamily { Generic, Xbox, PlayStation4, PlayStation5, Switch, Steam }

// The names printed on the controller's buttons: the bottom face button is A
// on an Xbox pad, ✕ on a PlayStation one and B on a Switch one. Hints are
// written once with a token per button ({South}, {East}, {LB}, {Start}, ...)
// and Text() fills them in for the pad used last (Gamepad.current, which the
// Input System moves to whichever pad had input), so the labels follow a
// swapped controller live. Labels is the only place a button's name lives.
public static class PadGlyphs
{
    // One row per family, in PadButton's order.
    static readonly Dictionary<PadFamily, string[]> Labels = new Dictionary<PadFamily, string[]>
    {
        [PadFamily.Generic] = new[] { "A", "B", "X", "Y", "LB", "RB", "LT", "RT", "Start", "Select" },
        [PadFamily.Xbox] = new[] { "A", "B", "X", "Y", "LB", "RB", "LT", "RT", "Menu", "View" },
        [PadFamily.PlayStation4] = new[] { "✕", "○", "□", "△", "L1", "R1", "L2", "R2", "Options", "Share" },
        [PadFamily.PlayStation5] = new[] { "✕", "○", "□", "△", "L1", "R1", "L2", "R2", "Options", "Create" },
        [PadFamily.Switch] = new[] { "B", "A", "Y", "X", "L", "R", "ZL", "ZR", "+", "−" },
        [PadFamily.Steam] = new[] { "A", "B", "X", "Y", "L1", "R1", "L2", "R2", "Menu", "View" },
    };

    static readonly string[] Tokens = Array.ConvertAll(Enum.GetNames(typeof(PadButton)), n => "{" + n + "}");

    static int seenDevice = -1;
    static PadFamily seen;
    static PadFamily cachedFor;
    static readonly Dictionary<string, string> cache = new Dictionary<string, string>();

    /// <summary>The family of the pad used last, or Generic with no pad.</summary>
    public static PadFamily Current
    {
        get
        {
            var pad = Gamepad.current;
            if (pad == null) return PadFamily.Generic;
            if (pad.deviceId != seenDevice)
            {
                seenDevice = pad.deviceId;
                seen = FamilyOf(pad);
            }
            return seen;
        }
    }

    public static PadFamily FamilyOf(InputDevice device)
    {
        if (device == null) return PadFamily.Generic;
        // Every PlayStation pad's layout derives from DualShockGamepad, on every platform.
        if (device is DualShockGamepad)
            return Has(device.layout, "DualSense") || Has(device.description.product, "DualSense") ? PadFamily.PlayStation5 : PadFamily.PlayStation4;
        return FamilyOf(device.layout, device.description.product, device.description.manufacturer, device is XInputController);
    }

    /// <summary>
    /// The family from what a device says about itself, for pads the Input System has no layout of its own for
    /// (a Steam Deck or a Switch pad on some platforms) and for tests. Steam Input's virtual Xbox pads say
    /// nothing about Steam and come out as Xbox, which is what their buttons are.
    /// </summary>
    public static PadFamily FamilyOf(string layout, string product, string manufacturer, bool xinput)
    {
        string about = $"{layout} {product} {manufacturer}";
        if (Has(about, "Steam") || Has(about, "Valve")) return PadFamily.Steam;
        if (Has(about, "DualSense")) return PadFamily.PlayStation5;
        if (Has(about, "DualShock") || Has(about, "Sony")) return PadFamily.PlayStation4;
        if (Has(about, "Switch") || Has(about, "Nintendo") || Has(about, "Pro Controller") || Has(about, "Joy-Con")) return PadFamily.Switch;
        if (xinput || Has(about, "Xbox") || Has(about, "XInput")) return PadFamily.Xbox;
        return PadFamily.Generic;
    }

    static bool Has(string text, string part) => text != null && text.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0;

    public static string Label(PadButton button) => Label(Current, button);
    public static string Label(PadFamily family, PadButton button) => Labels[family][(int)button];

    /// <summary>The template with each {button} token replaced by its label on the pad used last. For fixed text: results are kept per template.</summary>
    public static string Text(string template)
    {
        var family = Current;
        if (family != cachedFor)
        {
            cache.Clear();
            cachedFor = family;
        }
        if (!cache.TryGetValue(template, out var text)) cache[template] = text = Text(family, template);
        return text;
    }

    public static string Text(PadFamily family, string template)
    {
        var labels = Labels[family];
        var text = new StringBuilder(template);
        for (int i = 0; i < Tokens.Length; i++) text.Replace(Tokens[i], labels[i]);
        return text.ToString();
    }
}
