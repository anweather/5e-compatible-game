#if UNITY_EDITOR_WIN || (UNITY_STANDALONE_WIN && !UNITY_EDITOR)
using System;
using System.Runtime.InteropServices;

// Which microphones Windows has as its defaults, by name, from Core Audio, which Unity doesn't
// expose: Microphone.devices has no notion of a default. Read-only.
public static class WindowsCaptureDevice
{
    const int Capture = 1, Console = 0, Communications = 2;

    /// <summary>Windows' default recording device, or null if it won't say.</summary>
    public static string Default() => Name(Console);

    /// <summary>Windows' default communications recording device, which can be a different one.</summary>
    public static string ForCalls() => Name(Communications);

    /// <summary>The device a name like "Microphone (2- Scarlett 2i2 USB)" is on; the whole name if it isn't like that.</summary>
    public static string Short(string name)
    {
        if (name == null) return null;
        int open = name.IndexOf('('), close = name.LastIndexOf(')');
        if (open < 0 || close <= open + 1) return name;
        string device = name.Substring(open + 1, close - open - 1).Trim();
        int dash = device.IndexOf("- ", StringComparison.Ordinal);
        return dash > 0 && int.TryParse(device.Substring(0, dash), out _) ? device.Substring(dash + 2) : device;
    }

    static string Name(int role)
    {
        try
        {
            var devices = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            if (devices.GetDefaultAudioEndpoint(Capture, role, out var device) != 0) return null;
            if (device.OpenPropertyStore(0, out var store) != 0) return null;
            var key = new PropertyKey { Group = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), Id = 14 };
            if (store.GetValue(ref key, out var value) != 0) return null;
            string name = Marshal.PtrToStringUni(value.Pointer);
            PropVariantClear(ref value);
            return string.IsNullOrEmpty(name) ? null : name;
        }
        catch (Exception e) when (e is COMException or InvalidCastException or InvalidComObjectException)
        {
            UnityEngine.Debug.Log("Voice: Windows wouldn't say its default microphone: " + e.Message);
            return null;
        }
    }

    [DllImport("ole32.dll")]
    static extern int PropVariantClear(ref PropVariant value);

    [StructLayout(LayoutKind.Sequential)]
    struct PropertyKey
    {
        public Guid Group;
        public int Id;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct PropVariant
    {
        readonly ushort type, reserved1, reserved2, reserved3;
        public IntPtr Pointer;
        readonly IntPtr rest;
    }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    class MMDeviceEnumerator
    {
    }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int flow, int mask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid id, int context, IntPtr parameters, out IntPtr result);
        [PreserveSig] int OpenPropertyStore(int access, out IPropertyStore store);
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyStore
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetAt(int index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
    }
}
#endif
