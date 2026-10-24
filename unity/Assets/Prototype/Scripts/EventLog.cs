using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

// On-screen help and a short "what happened" log, like the web sandbox's.
// Shows the spell bar when the scene has a SpellCaster. R reloads the scene.
public class EventLog : MonoBehaviour
{
    static EventLog instance;
    readonly List<string> lines = new List<string>();
    GUIStyle style;
    SpellCaster caster;

    void Awake() => instance = this;
    void Start() => caster = FindFirstObjectByType<SpellCaster>();
    void OnDestroy() { if (instance == this) instance = null; }

    // A line that starts with the same text as the last one replaces it, so
    // "The oil ignites (×3)" updates "The oil ignites (×2)" in place.
    public static void Add(string line, string sameAs = null)
    {
        Debug.Log(line);
        if (instance == null) return;
        var list = instance.lines;
        if (list.Count > 0 && list[list.Count - 1].StartsWith(sameAs ?? line))
        {
            list[list.Count - 1] = line;
            return;
        }
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
        if (caster == null)
            GUI.Label(new Rect(16, 12, 900, 30), "<b>WASD</b> move   ·   <b>Click</b> Fire Bolt   ·   <b>R</b> reset", style);
        else
        {
            GUI.Label(new Rect(16, 12, 1400, 30), "<b>WASD</b> move   ·   <b>Mouse</b> look   ·   <b>Space</b> jump   ·   <b>Shift</b> sprint   ·   <b>1–6</b> or <b>wheel</b> pick a spell   ·   <b>Click</b> cast   ·   <b>R</b> reset", style);
            if (Cursor.lockState == CursorLockMode.Locked)
            {
                var c = new Vector2(Screen.width / 2f, Screen.height / 2f);
                GUI.Label(new Rect(c.x - 12, c.y - 15, 24, 30), "<b>+</b>", new GUIStyle(style) { alignment = TextAnchor.MiddleCenter, fontSize = 26 });
            }
            var bar = new StringBuilder();
            for (int i = 0; i < caster.spellIds.Length; i++)
            {
                var name = caster.grid?.World?.Rules.Spell(caster.spellIds[i])?.Name ?? caster.spellIds[i];
                bar.Append(i == caster.Selected ? $"<color=#ffd24a><b>[{i + 1}] {name}</b></color>   " : $"{i + 1} {name}   ");
            }
            GUI.Label(new Rect(16, 40, 1400, 30), bar.ToString(), style);
        }
        for (int i = 0; i < lines.Count; i++)
            GUI.Label(new Rect(16, Screen.height - 32 - (lines.Count - i) * 26, 900, 26), lines[i], style);
    }
}
