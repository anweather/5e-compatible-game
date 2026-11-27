using UnityEngine;

// The online reset's countdown (J-014, OnlineReset): every machine hears NetChem.ResetComing when the host stamps a
// rebuild, and this shows "The world resets in 3" big at the top of the screen until that tick. It starts itself, from
// the Net side, as the other runtime singletons do; the doors that ask for a reset are the game's (OnlineReset).
public class ResetCountdown : MonoBehaviour
{
    static ResetCountdown instance;
    NetChem net;
    float nextFind;
    int resetAt = -1;
    GUIStyle style;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (instance != null) return;
        var go = new GameObject("Online reset");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<ResetCountdown>();
    }

    void Update()
    {
        if (net != null || Time.unscaledTime < nextFind) return;
        nextFind = Time.unscaledTime + 1f;
        net = FindAnyObjectByType<NetChem>();
        if (net != null) net.ResetComing += Coming;
    }

    void OnDestroy()
    {
        if (net != null) net.ResetComing -= Coming;
        net = null;
        // Leaving Play mode in the Editor keeps statics (no domain reload): a static still naming this one would hold its
        // NetChem, and through it the grid and the whole last world, until the next Play (cell-memory: 0.8 GB on flat-1728).
        if (instance == this) instance = null;
    }

    void Coming(int tick) => resetAt = tick;

    // The countdown, big at the top of the screen, until the reset's tick.
    void OnGUI()
    {
        HudScale.Apply();
        if (resetAt < 0 || net == null || !EventLog.HudVisible) return;
        if (!net.Online || net.NetTick >= resetAt)
        {
            resetAt = -1;
            return;
        }
        int tps = Mathf.Max(1, Mathf.RoundToInt(net.ResetCountdownTicks / NetChem.ResetCountdownSeconds));
        int seconds = Mathf.CeilToInt((resetAt - net.NetTick) / (float)tps);
        style ??= new GUIStyle(GUI.skin.label) { fontSize = 34, alignment = TextAnchor.MiddleCenter, richText = true };
        GUI.Label(new Rect(0, HudScale.Height * 0.18f, HudScale.Width, 50), $"<b>The world resets in {seconds}</b>", style);
    }
}
