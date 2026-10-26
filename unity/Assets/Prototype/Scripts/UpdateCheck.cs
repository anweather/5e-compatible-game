using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

// Asks GitHub once per launch whether a newer alpha is out, and if so says so:
// a small hint bottom right on the HUD (until the Esc menu is first opened)
// and a line with an "Open download page" button in the Esc menu
// (GameInfoMenu). It runs in player builds that aren't development builds,
// and in the Editor; it never blocks anything, gives up after a few seconds,
// and stays quiet when offline or when GitHub says no. The version logic is
// BuildVersion.NewestRelease.
public class UpdateCheck : MonoBehaviour
{
    const int TimeoutSeconds = 5;

    static bool checkedThisLaunch;
    static UpdateCheck instance;

    /// <summary>The newer release, once the check has found one; null otherwise.</summary>
    public static BuildVersion.Release Available { get; private set; }

    /// <summary>What the check concluded, for the log and tests: "checking", "up to date", "newer: X", "failed: ...".</summary>
    public static string Result { get; private set; } = "not run";

    GUIStyle hint, updating;
    bool hintDismissed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if (checkedThisLaunch || instance != null) return;
        if (!Application.isEditor && Debug.isDebugBuild) return; // development builds are ours, not players'
        checkedThisLaunch = true;
        var go = new GameObject("Update check");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<UpdateCheck>();
        instance.StartCoroutine(SelfUpdate.Installed ? CheckInstalled() : Check(BuildVersion.ReleasesApi));
    }

    // A Velopack install asks Velopack, which can then download and apply what it finds.
    static IEnumerator CheckInstalled()
    {
        Result = "checking";
        Available = null;
        var check = SelfUpdate.CheckAsync();
        yield return new WaitUntil(() => check.IsCompleted);
        if (check.IsFaulted)
        {
            Result = $"failed: {check.Exception?.GetBaseException().Message}";
            Debug.Log($"Update check: Velopack couldn't check ({Result}); carrying on.");
            yield break;
        }
        string version = check.Result;
        Available = version == null ? null : new BuildVersion.Release { Version = version, Url = BuildVersion.ReleasesPage };
        Result = version == null ? "up to date" : $"newer: {version}";
        Debug.Log(version == null
            ? $"Update check: up to date (Velopack found nothing newer than {SelfUpdate.CurrentVersion})."
            : $"Update check: alpha {version} is out; it can update in place.");
        if (version != null && SelfUpdate.AutoUpdate) _ = SelfUpdate.DownloadAndRestart();
    }

    /// <summary>Runs the check again against a URL (the real API or a test server); for tests in the Editor.</summary>
    public static void RunAgain(string url)
    {
        if (instance == null)
        {
            var go = new GameObject("Update check");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<UpdateCheck>();
        }
        instance.StartCoroutine(Check(url));
    }

    static IEnumerator Check(string url)
    {
        Result = "checking";
        Available = null;
        using var request = UnityWebRequest.Get(url);
        request.timeout = TimeoutSeconds;
        request.SetRequestHeader("User-Agent", "SpellChemistryValley/" + Application.version);
        request.SetRequestHeader("Accept", "application/vnd.github+json");
        yield return request.SendWebRequest();
        if (request.result != UnityWebRequest.Result.Success)
        {
            Result = $"failed: {request.error}";
            Debug.Log($"Update check: couldn't reach GitHub ({request.error}); carrying on.");
            yield break;
        }
        Conclude(request.downloadHandler.text);
    }

    /// <summary>Reads a releases response the way the check does (also for tests with made-up JSON).</summary>
    public static void Conclude(string releasesJson)
    {
        Available = BuildVersion.NewestRelease(releasesJson, Application.version);
        Result = Available == null ? "up to date" : $"newer: {Available.Version}";
        Debug.Log(Available == null
            ? $"Update check: up to date (alpha {Application.version} is the newest alpha release)."
            : $"Update check: alpha {Available.Version} is out: {Available.Url}");
    }

    /// <summary>Opens the newer release's page in the browser.</summary>
    public static void OpenDownloadPage() => Application.OpenURL(Available?.Url ?? BuildVersion.ReleasesPage);

    void OnGUI()
    {
        if (Available == null) return;
        if (SelfUpdate.Progress >= 0)
        {
            // Updating in place: say so over everything until the game restarts.
            GUI.depth = -200;
            updating ??= new GUIStyle(GUI.skin.box) { fontSize = 22, richText = true, alignment = TextAnchor.MiddleCenter };
            GUI.Box(new Rect(Screen.width / 2f - 260f, Screen.height / 2f - 40f, 520f, 80f),
                $"Updating to alpha {Available.Version}...  {SelfUpdate.Progress}%\n<size=15><color=#ffffff99>The game restarts by itself when it's done.</color></size>", updating);
            return;
        }
        if (!EventLog.HudVisible) return;
        var menu = FindFirstObjectByType<PauseMenu>();
        if (menu != null && menu.Open) hintDismissed = true;
        if (hintDismissed) return;
        hint ??= new GUIStyle(GUI.skin.label) { fontSize = 15, richText = true, alignment = TextAnchor.LowerRight };
        GUI.Label(new Rect(Screen.width - 476f, Screen.height - 40f, 460f, 28f),
            $"<color=#ffd24a>alpha {Available.Version} is out</color>   <color=#ffffff88>Esc: {(SelfUpdate.Installed ? "update" : "download")}</color>", hint);
    }

    void OnDestroy() { if (instance == this) instance = null; }
}
