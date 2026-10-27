using System;
using System.Collections;
using Unity.Netcode;
using UnityEngine;

// Ends the Windows player as soon as it quits, instead of letting Unity shut
// down. Unity's own shutdown crashes in the DirectX 12 runtime (D3D12Core.dll)
// after 20 s or more, writing a 42 MB crash dump, while the hidden process
// holds its files (JUDGMENT_CALLS.md J-010). So every quit (the menu's Quit,
// Alt+F4, closing the window, an update's restart) is caught: settings are
// saved, a host or client leaves the session cleanly, and the process ends.
// -clean-quit turns this off to see Unity's own shutdown. The Editor and the
// Mac quit as before.
//
// ANYTHING THAT MUST HAPPEN ON QUIT goes in BeforeExit. Unity's own quit
// callbacks (OnApplicationQuit, Application.quitting, OnDestroy) never run
// in a Windows player.
public class QuickExit : MonoBehaviour
{
    const string Off = "-clean-quit";
    // Long enough for a disconnect to reach the other peers, short enough not to be noticed.
    const float MostToLeave = 0.5f;

    /// <summary>
    /// Runs once as the game quits, before the process ends: save or send what can't be
    /// lost here. In a Windows player Unity's own quit callbacks don't run (see the top).
    /// </summary>
    public static event Action BeforeExit;

    static bool ending;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Boot()
    {
        // Starts over each run, even with the Editor's domain reload off.
        ending = false;
        BeforeExit = null;
        Application.wantsToQuit -= Quitting;
        if (Application.platform != RuntimePlatform.WindowsPlayer) return;
        if (Array.IndexOf(Environment.GetCommandLineArgs(), Off) >= 0) return;
        Application.wantsToQuit += Quitting;
    }

    // Unity asks before it quits: say no, and end it ourselves once the session is left.
    static bool Quitting()
    {
        if (ending) return false;
        ending = true;
        var go = new GameObject("Quick exit");
        DontDestroyOnLoad(go);
        go.AddComponent<QuickExit>().StartCoroutine(Leave());
        return false;
    }

    static IEnumerator Leave()
    {
        try
        {
            BeforeExit?.Invoke();
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
        PlayerPrefs.Save();

        var net = NetworkManager.Singleton;
        if (net != null && net.IsListening)
        {
            net.Shutdown();
            float until = Time.realtimeSinceStartup + MostToLeave;
            while (net.ShutdownInProgress && Time.realtimeSinceStartup < until) yield return null;
            yield return null; // one more frame for the transport to send it
        }
        Debug.Log("Quit: ending the process (-clean-quit for Unity's own shutdown).");
        Now(0);
    }

    /// <summary>
    /// Ends the process at once, with no shutdown. On Windows that's TerminateProcess:
    /// ExitProcess would run every DLL's detach under the loader lock, which deadlocks with
    /// Unity's threads still running (seen on the PC).
    /// </summary>
    public static void Now(uint exitCode)
    {
        if (Application.platform == RuntimePlatform.WindowsPlayer) TerminateProcess(GetCurrentProcess(), exitCode);
        System.Diagnostics.Process.GetCurrentProcess().Kill();
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    static extern IntPtr GetCurrentProcess();

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    static extern bool TerminateProcess(IntPtr process, uint exitCode);
}
