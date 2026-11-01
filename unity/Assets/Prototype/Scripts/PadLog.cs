using System;
using UnityEngine;
using UnityEngine.InputSystem;

// One line in Player.log for every input device seen: at start, on every change,
// and when the pad in use moves (PadGlyphs.Current, which already watches for that).
// The game reads Gamepad.current and <Gamepad> bindings, so each line says what that
// is: a controller that arrives as a Joystick has no way in, and that's the first
// thing to look for in a player's log. Nothing runs between changes. Starts itself.
public static class PadLog
{
    // A pad's capabilities on Linux list every axis and button, and those are the evidence, so keep plenty.
    const int CapabilitiesKept = 2000;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void LogAtStartup()
    {
        InputSystem.onDeviceChange -= Changed;
        InputSystem.onDeviceChange += Changed;
        foreach (var device in InputSystem.devices) Debug.Log(Line("Start", device, Gamepad.current));
        if (Gamepad.current == null) Debug.Log(Line("Start", null, null));
    }

    static void Changed(InputDevice device, InputDeviceChange change) => Debug.Log(Line(change.ToString(), device, Gamepad.current));

    /// <summary>The pad in use moved to this one, which gets these glyphs.</summary>
    public static void InUse(Gamepad pad, PadFamily family) => Debug.Log($"{Line("InUse", pad, pad)} glyphs={family}");

    /// <summary>The line for a device, or for no device: the change, then what the device says about itself.</summary>
    public static string Line(string change, InputDevice device, Gamepad current)
    {
        string gamepad = current != null ? $"{current.deviceId} \"{current.description.product}\"" : "none";
        if (device == null) return $"Input device {change}: no device Gamepad.current={gamepad}";
        var about = device.description;
        return $"Input device {change}: id={device.deviceId} kind={Kind(device)} layout=\"{device.layout}\" interface=\"{about.interfaceName}\"" +
            $" product=\"{about.product}\" manufacturer=\"{about.manufacturer}\" usages=[{string.Join(",", device.usages)}]" +
            $" capabilities=\"{Trimmed(about.capabilities)}\" Gamepad.current={gamepad}";
    }

    static string Kind(InputDevice device) => device is Gamepad ? "Gamepad" : device is Joystick ? "Joystick" : "other";

    static string Trimmed(string capabilities)
    {
        if (string.IsNullOrEmpty(capabilities)) return "";
        string flat = capabilities.Replace('\n', ' ').Replace('\r', ' ').Trim();
        return flat.Length <= CapabilitiesKept ? flat : flat.Substring(0, CapabilitiesKept) + "...";
    }
}
