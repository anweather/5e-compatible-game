using System;
using UnityEngine;

// The game opens full screen at the display's own resolution, every launch.
// Unity remembers the window mode a player last ran in (in the registry on
// Windows, in preferences on macOS) and that beats the project's setting, so
// one windowed run (a test, a resized window) kept every later launch
// windowed. A window asked for on the command line (-screen-fullscreen 0,
// -window-mode, -popupwindow, as the online tests do) still wins, and Alt+Enter
// (Cmd+F on a Mac) still switches while playing. Not in the Editor.
public static class FullScreenStart
{
    static readonly string[] WindowArgs = { "-screen-fullscreen", "-window-mode", "-popupwindow", "-screen-width", "-screen-height" };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Go()
    {
        if (Application.isEditor || AskedForAWindow(Environment.GetCommandLineArgs())) return;
        var display = Display.main;
        Screen.SetResolution(display.systemWidth, display.systemHeight, FullScreenMode.FullScreenWindow);
    }

    /// <summary>Whether the command line chose the window itself.</summary>
    public static bool AskedForAWindow(string[] args)
    {
        foreach (var a in args)
            if (Array.Exists(WindowArgs, w => string.Equals(w, a, StringComparison.OrdinalIgnoreCase))) return true;
        return false;
    }
}
