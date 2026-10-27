using System;
using System.Runtime.InteropServices;
using System.Text;

// Moves the playtest player's own window out of the way (playtest/DESIGN.md):
// off the left of the screen and behind other windows, without taking focus,
// so a run doesn't get in anyone's way. Windows players only; elsewhere it
// does nothing.
public static class PlaytestWindow
{
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string title);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    static readonly IntPtr Bottom = new IntPtr(1);
    const uint NoSize = 0x0001, NoActivate = 0x0010;

    public static void MoveOffScreen(StringBuilder notes)
    {
        var hwnd = Own();
        bool moved = hwnd != IntPtr.Zero && SetWindowPos(hwnd, Bottom, -4000, 0, 0, 0, NoSize | NoActivate);
        notes.Append($"window moved off-screen: {moved}; ");
    }

    // This process's Unity window, found by its window class (no callbacks, so it works under IL2CPP).
    static IntPtr Own()
    {
        uint me = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
        var hwnd = IntPtr.Zero;
        while ((hwnd = FindWindowEx(IntPtr.Zero, hwnd, "UnityWndClass", null)) != IntPtr.Zero)
        {
            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == me) return hwnd;
        }
        return IntPtr.Zero;
    }
#else
    public static void MoveOffScreen(StringBuilder notes) => notes.Append("window not moved (not a Windows player); ");
#endif
}
