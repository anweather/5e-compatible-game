using UnityEngine;

// Resetting the world while playing together (J-014): the host decides. The
// host's Reset (R / View) stamps it for everyone (NetChem.StampReset) with a
// 3 s countdown; a client's asks the host, whose log says who asked (the
// host's own R then resets). Every machine shows the countdown (ResetComing),
// and at its tick NetChem rebuilds the world; the view's own parts follow
// ChemGrid.WorldReset when Happened says it's a live reset: ThingSpawner
// brings every thing back with its id, NetWizard puts each player back at
// the start, NetCourt goes back to 0-0. Solo, Reset still reloads the scene.
public class OnlineReset : MonoBehaviour
{
    static OnlineReset instance;
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
        instance = go.AddComponent<OnlineReset>();
    }

    /// <summary>
    /// Online: the host resets the world for everyone, a client asks the host to. Returns false
    /// when not online (EventLog.ResetYard then reloads the scene).
    /// </summary>
    public static bool Ask()
    {
        var net = FindAnyObjectByType<NetChem>();
        if (net == null || !net.Online) return false;
        if (net.IsServer)
        {
            net.StampReset(net.ResetCountdownTicks);
            return true;
        }
        var me = NetWizard.Local;
        if (me != null) me.AskForReset();
        EventLog.Add("You asked the host to reset the world");
        return true;
    }

    /// <summary>
    /// Whether a WorldReset now is a live online reset that the view follows: not going online
    /// (a fresh world, no reset yet), and not a late joiner replaying one from its history.
    /// </summary>
    public static bool Happened(NetChem net) => net != null && net.Online && net.LastResetTick > 0 && !net.ResetIsReplay;

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
