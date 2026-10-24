using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

// On-screen help and a short "what happened" log, like the web sandbox's.
// R reloads the scene.
public class EventLog : MonoBehaviour
{
    static EventLog instance;
    readonly List<string> lines = new List<string>();
    GUIStyle style;

    void Awake() => instance = this;
    void OnDestroy() { if (instance == this) instance = null; }

    public static void Add(string line)
    {
        Debug.Log(line);
        if (instance == null) return;
        var list = instance.lines;
        if (list.Count > 0 && list[list.Count - 1].StartsWith(line)) return;
        list.Add(line);
        if (list.Count > 8) list.RemoveAt(0);
    }

    void Update()
    {
        if (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame)
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    void OnGUI()
    {
        style ??= new GUIStyle(GUI.skin.label) { fontSize = 18, richText = true };
        GUI.Label(new Rect(16, 12, 900, 30), "<b>WASD</b> move   ·   <b>Click</b> Fire Bolt   ·   <b>R</b> reset", style);
        for (int i = 0; i < lines.Count; i++)
            GUI.Label(new Rect(16, Screen.height - 32 - (lines.Count - i) * 26, 900, 26), lines[i], style);
    }
}
