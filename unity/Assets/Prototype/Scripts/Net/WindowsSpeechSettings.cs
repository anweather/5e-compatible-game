using System;
using System.Runtime.InteropServices;

// The Windows settings dictation needs, read before a hold so a switched-off one is said plainly
// instead of failing silently (#58): microphone access for the PC, for apps and for desktop apps
// (Windows' recogniser captures in SpeechRuntime.exe, itself a desktop app), and online speech
// recognition. Read-only: the game never changes them. A value Windows doesn't have counts as on,
// so only a setting that's plainly off stops a hold.
public static class WindowsSpeechSettings
{
    const string Mic = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\microphone";
    const string Online = @"Software\Microsoft\Speech_OneCore\Settings\OnlineSpeechPrivacy";
    const string Where = "Settings > Privacy & security > ";

    /// <summary>What the player is told when online speech recognition is off.</summary>
    public const string OnlineSpeechOff = "Voice needs Windows' online speech recognition: turn it on in " + Where + "Speech.";

    /// <summary>The settings as Windows holds them; null where it has none.</summary>
    public readonly struct Values
    {
        public readonly string MicForPc, MicForApps, MicForDesktopApps;
        public readonly int? OnlineSpeech;

        public Values(string micForPc, string micForApps, string micForDesktopApps, int? onlineSpeech)
        {
            MicForPc = micForPc;
            MicForApps = micForApps;
            MicForDesktopApps = micForDesktopApps;
            OnlineSpeech = onlineSpeech;
        }

        public override string ToString() =>
            $"microphone for the PC {MicForPc ?? "unset"}, for apps {MicForApps ?? "unset"}, for desktop apps {MicForDesktopApps ?? "unset"}, online speech {(OnlineSpeech?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unset")}";
    }

    /// <summary>What to tell the player about these settings, or null if none is off.</summary>
    public static string Problem(Values v)
    {
        if (Off(v.MicForPc)) return $"Windows has the microphone off for this PC: turn on {Where}Microphone > Microphone access.";
        if (Off(v.MicForApps)) return $"Windows has the microphone off for apps: turn on {Where}Microphone > Let apps access your microphone.";
        if (Off(v.MicForDesktopApps)) return $"Windows has the microphone off for desktop apps, which voice needs: turn on {Where}Microphone > Let desktop apps access your microphone.";
        if (v.OnlineSpeech == 0) return OnlineSpeechOff;
        return null;
    }

    static bool Off(string consent) => string.Equals(consent, "Deny", StringComparison.OrdinalIgnoreCase);

#if UNITY_EDITOR_WIN || (UNITY_STANDALONE_WIN && !UNITY_EDITOR)
    static readonly UIntPtr CurrentUser = (UIntPtr)0x80000001u, LocalMachine = (UIntPtr)0x80000002u;
    const uint AnyString = 0x2, Dword = 0x10;

    /// <summary>This PC's settings, from the registry. Cheap: four reads.</summary>
    public static Values Read() => new(
        Text(LocalMachine, Mic), Text(CurrentUser, Mic), Text(CurrentUser, Mic + @"\NonPackaged"), Number(CurrentUser, Online, "HasAccepted"));

    static string Text(UIntPtr root, string key)
    {
        var buffer = new char[64];
        uint size = (uint)(buffer.Length * sizeof(char));
        if (RegGetValueW(root, key, "Value", AnyString, IntPtr.Zero, buffer, ref size) != 0) return null;
        int length = Array.IndexOf(buffer, '\0');
        return new string(buffer, 0, length < 0 ? buffer.Length : length);
    }

    static int? Number(UIntPtr root, string key, string name)
    {
        uint size = sizeof(int);
        return RegGetValueW(root, key, name, Dword, IntPtr.Zero, out int value, ref size) == 0 ? value : null;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    static extern int RegGetValueW(UIntPtr root, string key, string name, uint flags, IntPtr type, char[] data, ref uint size);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    static extern int RegGetValueW(UIntPtr root, string key, string name, uint flags, IntPtr type, out int data, ref uint size);
#endif
}
