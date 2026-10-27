using System;
using System.Globalization;
using System.Runtime.InteropServices;
using UnityEditor;

// Hands the keyboard back to whatever window the user was in when a probe run
// started (a game they're playtesting full screen, say). On Windows the Editor
// brings itself to the front as it enters and leaves Play mode, whatever the
// Game view's Play Focused says. Remember() notes the window in front; for a few
// seconds after that and after each Guard(), whenever the Editor takes the
// front, it gives it straight back. Outside those moments it leaves the user
// alone, so clicking into the Editor mid-run still works. Windows only;
// elsewhere it does nothing.
[InitializeOnLoad]
static class FocusKeeper
{
    const string WindowKey = "Prototype.Probe.UserWindow", UntilKey = "Prototype.Probe.GuardUntil", LastKey = "Prototype.Probe.GuardLast";
    const float GuardSeconds = 5f;

    // Domain reloads (entering Play mode) drop the update hook; put it back.
    static FocusKeeper() => EditorApplication.update += Watch;

    /// <summary>Notes the window in front now (unless it's the Editor) and guards it for a few seconds.</summary>
    public static void Remember()
    {
        Forget();
#if UNITY_EDITOR_WIN
        var front = GetForegroundWindow();
        if (front == IntPtr.Zero || Ours(front)) return;
        SessionState.SetString(WindowKey, front.ToInt64().ToString(CultureInfo.InvariantCulture));
        Guard();
#endif
    }

    /// <summary>Guards the remembered window for the next few seconds (the Editor is about to raise itself).</summary>
    public static void Guard()
    {
        if (SessionState.GetString(WindowKey, "").Length > 0)
            SessionState.SetFloat(UntilKey, (float)EditorApplication.timeSinceStartup + GuardSeconds);
    }

    /// <summary>The run is over: stop guarding once this last guard runs out.</summary>
    public static void Release()
    {
        Guard();
        SessionState.SetBool(LastKey, true);
    }

    static void Forget()
    {
        SessionState.EraseString(WindowKey);
        SessionState.EraseFloat(UntilKey);
        SessionState.EraseBool(LastKey);
    }

    static void Watch()
    {
        float until = SessionState.GetFloat(UntilKey, 0f);
        if (until <= 0f) return;
        if (EditorApplication.timeSinceStartup > until)
        {
            if (SessionState.GetBool(LastKey, false)) Forget();
            return;
        }
#if UNITY_EDITOR_WIN
        if (!long.TryParse(SessionState.GetString(WindowKey, ""), out long handle)) return;
        var user = new IntPtr(handle);
        if (IsWindow(user) && Ours(GetForegroundWindow())) SetForegroundWindow(user);
#endif
    }

#if UNITY_EDITOR_WIN
    static bool Ours(IntPtr window)
    {
        GetWindowThreadProcessId(window, out uint pid);
        return pid == (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
    }

    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
#endif
}
