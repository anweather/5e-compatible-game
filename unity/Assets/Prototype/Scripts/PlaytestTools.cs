using System;
using System.Globalization;
using System.IO;
using Chem;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

// Playtest tools (#220): "where am I" at the HUD's top right (cell, chunk, biome; F6, or Esc > Settings), a record of every
// F8 mark's place in marks-<time>.txt beside the play log so the lead can go to each one, and a dev fly mode (F4, or
// Esc > Settings) to cross the nine in a minute. All of it is the player's own view: the readout choice lives in
// PlayerPrefs and flying in memory, never in shared state, and the fly mode refuses online (a peer's place is shared).
public sealed class PlaytestTools : MonoBehaviour
{
    const string ShownKey = "playtest-where";
    const int SectionOrder = 507; // the Settings sub menu, after the frame readout's
    // Metres a second; [ and ] step through them. The last crosses the nine in about a minute.
    static readonly float[] Speeds = { 12f, 40f, 120f, 400f };
    static readonly string[] Points = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
    static readonly CultureInfo Plain = CultureInfo.InvariantCulture;

    /// <summary>The tools in this session, for the probe.</summary>
    public static PlaytestTools Instance { get; private set; }

    GUIStyle style;
    string marksPath;
    CharacterController frozenBody;
    Behaviour frozenDriver;
    int speed = 1;

    /// <summary>Whether the where-am-I line shows: on in a player build, off in the Editor, until the player chooses.</summary>
    public bool Shown { get; private set; }

    /// <summary>Whether the wizard is flying.</summary>
    public bool Flying { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void StartWithTheGame()
    {
        if (ProbeLink.InVirtualPlayer || Instance != null) return;
        var go = new GameObject("Playtest tools");
        DontDestroyOnLoad(go);
        go.AddComponent<PlaytestTools>();
    }

    void Awake()
    {
        Instance = this;
        Shown = PlayerPrefs.GetInt(ShownKey, Application.isEditor ? 0 : 1) == 1;
    }

    void OnEnable()
    {
        PauseMenu.AddSection(DrawEntry, SectionOrder, PauseMenu.Submenu.Settings);
        PlayLog.Marked += OnMarked;
    }

    void OnDisable()
    {
        PauseMenu.RemoveSection(DrawEntry);
        PlayLog.Marked -= OnMarked;
        Fly(false);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>Shows or hides the where-am-I line, and remembers it.</summary>
    public void Show(bool on)
    {
        Shown = on;
        PlayerPrefs.SetInt(ShownKey, on ? 1 : 0);
        PlayerPrefs.Save();
    }

    void DrawEntry()
    {
        if (PauseMenu.Button($"Where am I: {(Shown ? "On" : "Off")} (F6)", PauseMenu.ButtonStyle)) Show(!Shown);
        if (PauseMenu.Button($"Fly mode: {(Flying ? "On" : "Off")} (F4)", PauseMenu.ButtonStyle)) Fly(!Flying);
    }

    /// <summary>The biome at a cell: the generated map's strongest, "pinned" in its hand-made square, else "valley" (the hand-made map).</summary>
    public static string BiomeAt(int x, int y) => BiomeGround.Last == null ? "valley" : BiomeGround.Last.BiomeAt(x, y) ?? "pinned";

    /// <summary>"cell 3012,4410  chunk 47,68  grassland" for a world point, or null with no grid.</summary>
    public static string Where(Vector3 p, out Vector2Int cell, out Vector2Int chunk)
    {
        cell = chunk = default;
        var chem = ChemGrid.Current?.Grid;
        if (chem == null) return null;
        cell = chem.CellAt(p);
        chunk = new Vector2Int(Chunks.ChunkOf(cell.x), Chunks.ChunkOf(cell.y));
        return $"cell {cell.x},{cell.y}  chunk {chunk.x},{chunk.y}  {BiomeAt(cell.x, cell.y)}";
    }

    /// <summary>The camera's heading as a compass point and degrees (north is +z, the top of the map).</summary>
    public static string Heading()
    {
        var cam = Camera.main;
        if (cam == null) return "";
        float deg = (cam.transform.eulerAngles.y % 360f + 360f) % 360f;
        return $"{Points[Mathf.RoundToInt(deg / 45f) % 8]} {deg:0}";
    }

    void Update()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null) return;
        if (keyboard.f6Key.wasPressedThisFrame) Show(!Shown);
        if (keyboard.f4Key.wasPressedThisFrame) Fly(!Flying);
        if (Flying) FlyStep(keyboard);
    }

    // F8's place, appended to marks-<time>.txt (one file a session, made at the first mark), with a toast.
    void OnMarked(string why)
    {
        var wizard = WizardRig.FindLocal();
        if (wizard == null) return;
        var p = wizard.transform.position;
        string where = Where(p, out _, out var chunk);
        if (where == null) return;
        string line = $"{DateTime.Now.ToString("HH:mm:ss", Plain)}  {why}  {where}  at {p.x:0.0},{p.y:0.0},{p.z:0.0} m  facing {Heading()}";
        try
        {
            marksPath ??= Path.Combine(PlayLog.Folder(), $"marks-{DateTime.Now.ToString("yyyy-MM-dd-HHmmss", Plain)}.txt");
            File.AppendAllText(marksPath, line + "\n");
        }
        catch (IOException e)
        {
            Debug.LogWarning("MARKS: " + e.Message);
        }
        EventLog.Add($"Marked: chunk {chunk.x},{chunk.y}");
    }

    /// <summary>Starts or stops flying. Refused online, where the wizard's place is shared.</summary>
    public void Fly(bool on)
    {
        if (on == Flying) return;
        if (!on)
        {
            Flying = false;
            if (frozenBody != null) frozenBody.enabled = true;
            if (frozenDriver != null) frozenDriver.enabled = true;
            frozenBody = null;
            frozenDriver = null;
            return;
        }
        var wizard = WizardRig.FindLocal();
        if (wizard == null) return;
        if (NetWizard.Local != null)
        {
            EventLog.Add("Fly mode is for solo play");
            return;
        }
        frozenBody = wizard.GetComponent<CharacterController>();
        frozenDriver = wizard.GetComponent(WizardRig.ControllerComponent) as Behaviour;
        if (frozenBody != null) frozenBody.enabled = false;
        if (frozenDriver != null) frozenDriver.enabled = false;
        Flying = true;
        EventLog.Add($"Fly mode: {Speeds[speed]:0} m/s ([ and ] change it, F4 lands)");
    }

    // WASD along the camera's heading on the ground plane, Space up and C down; Shift doubles it.
    void FlyStep(Keyboard k)
    {
        var wizard = WizardRig.FindLocal();
        var cam = Camera.main;
        if (wizard == null || cam == null)
        {
            Fly(false);
            return;
        }
        if (k.leftBracketKey.wasPressedThisFrame) speed = Mathf.Max(0, speed - 1);
        if (k.rightBracketKey.wasPressedThisFrame) speed = Mathf.Min(Speeds.Length - 1, speed + 1);
        var forward = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
        var right = Vector3.ProjectOnPlane(cam.transform.right, Vector3.up).normalized;
        var move = forward * Axis(k.wKey, k.sKey) + right * Axis(k.dKey, k.aKey) + Vector3.up * Axis(k.spaceKey, k.cKey);
        float rate = Speeds[speed] * (k.leftShiftKey.isPressed ? 2f : 1f);
        wizard.transform.position += move.normalized * (rate * Time.unscaledDeltaTime);
    }

    static float Axis(KeyControl plus, KeyControl minus) => (plus.isPressed ? 1f : 0f) - (minus.isPressed ? 1f : 0f);

    void OnGUI()
    {
        if (!Shown || Event.current.type != EventType.Repaint || !EventLog.HudVisible) return;
        var wizard = WizardRig.FindLocal();
        if (wizard == null) return;
        string where = Where(wizard.transform.position, out _, out _);
        if (where == null) return;
        style ??= new GUIStyle(GUI.skin.label) { fontSize = 16, richText = true, alignment = TextAnchor.UpperRight };
        string fly = Flying ? $"  <color=#ffd866>FLY {Speeds[speed]:0} m/s</color>" : "";
        GUI.Label(new Rect(Screen.width - 536, 68, 520, 26), $"<color=#ffffffcc>{where}  {Heading()}{fly}</color>", style);
    }
}
