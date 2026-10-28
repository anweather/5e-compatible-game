using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

// Starts the player without bringing it to the front. Windows lets a process
// started by the app in front take the foreground, so a run started from a
// terminal Andrew was typing in caught his keys (seed 103, 2026-10-27; an R
// reset the scene). On Windows the window is shown without activating it
// (SW_SHOWNOACTIVATE), and the window in front is passed to the player
// (-playtest-hand-focus), which hands it back if Unity raises itself anyway
// (PlaytestWindow.HandBack). Elsewhere a plain Process.Start.
namespace Playtest.Runner
{
    public static class Launch
    {
        /// <summary>The window in front now, as a number for -playtest-hand-focus; 0 off Windows.</summary>
        public static long FrontWindow() => OperatingSystem.IsWindows() ? GetForegroundWindow().ToInt64() : 0;

        public static Process Start(string exe, string arguments)
        {
            if (!OperatingSystem.IsWindows()) return Process.Start(new ProcessStartInfo(exe, arguments) { UseShellExecute = false });
            return StartWithoutFocus(exe, arguments);
        }

        [SupportedOSPlatform("windows")]
        static Process StartWithoutFocus(string exe, string arguments)
        {
            var startup = new StartupInfo { Size = Marshal.SizeOf<StartupInfo>(), Flags = UseShowWindow, ShowWindow = ShowNoActivate };
            string commandLine = "\"" + exe + "\" " + arguments;
            if (!CreateProcess(null, commandLine, IntPtr.Zero, IntPtr.Zero, false, 0, IntPtr.Zero, System.IO.Path.GetDirectoryName(exe), ref startup, out var info))
                throw new InvalidOperationException($"couldn't start {exe} (error {Marshal.GetLastWin32Error()})");
            CloseHandle(info.Thread);
            // Process opens its own handle; ours closes after, so the exit code is still there to read.
            var p = Process.GetProcessById(info.ProcessId);
            _ = p.Handle;
            CloseHandle(info.Process);
            return p;
        }

        const int UseShowWindow = 0x1;
        const short ShowNoActivate = 4;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct StartupInfo
        {
            public int Size;
            public string Reserved, Desktop, Title;
            public int X, Y, XSize, YSize, XCountChars, YCountChars, FillAttribute, Flags;
            public short ShowWindow, Reserved2;
            public IntPtr Reserved2Ptr, StdInput, StdOutput, StdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct ProcessInformation
        {
            public IntPtr Process, Thread;
            public int ProcessId, ThreadId;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool CreateProcess(string application, string commandLine, IntPtr processAttributes, IntPtr threadAttributes,
            [MarshalAs(UnmanagedType.Bool)] bool inheritHandles, uint creationFlags, IntPtr environment, string currentDirectory,
            ref StartupInfo startup, out ProcessInformation info);

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool CloseHandle(IntPtr handle);

        [DllImport("user32.dll")]
        static extern IntPtr GetForegroundWindow();
    }
}
