using System;
using System.Runtime.InteropServices;
using System.Text;

// Moves the playtest player's own window out of the way (playtest/DESIGN.md):
// off the left of the screen and behind other windows, without taking focus,
// so a run doesn't get in anyone's way, and hands the front back to the window
// the runner saw there should Unity raise itself as it starts (HandBack).
// Windows players only; elsewhere it does nothing.
public static class PlaytestWindow
{
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string title);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr hWnd);

    static readonly IntPtr Bottom = new IntPtr(1);
    const uint NoSize = 0x0001, NoActivate = 0x0010;

    public static void MoveOffScreen(StringBuilder notes)
    {
        var hwnd = Own();
        bool moved = hwnd != IntPtr.Zero && SetWindowPos(hwnd, Bottom, -4000, 0, 0, 0, NoSize | NoActivate);
        notes.Append($"window moved off-screen: {moved}; ");
    }

    public static bool HandBack(long window)
    {
        var user = new IntPtr(window);
        if (user == IntPtr.Zero || !IsWindow(user)) return Say(false, $"window {window} isn't a window");
        GetWindowThreadProcessId(GetForegroundWindow(), out uint pid);
        if (pid != (uint)System.Diagnostics.Process.GetCurrentProcess().Id) return Say(false, $"process {pid} is in front, not this player");
        return SetForegroundWindow(user) || Say(false, "SetForegroundWindow refused");
    }

    static readonly System.Collections.Generic.HashSet<string> said = new();
    static bool Say(bool result, string why)
    {
        if (said.Add(why)) UnityEngine.Debug.Log("PLAYTEST: front not handed back: " + why);
        return result;
    }

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

    public static bool HandBack(long window) => false;
#endif
}
