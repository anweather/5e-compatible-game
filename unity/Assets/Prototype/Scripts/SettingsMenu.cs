using System;
using UnityEngine;

// The Esc menu's Sound page (PauseMenu): master, effects and ambience volume
// (GameVolume), and its Shake and rumble page (GameFeel), each with a slider for
// the mouse and − / + buttons the controller's highlight reaches. Starts itself.
public class SettingsMenu : MonoBehaviour
{
    /// <summary>The Sound page's id in the Esc menu.</summary>
    public const string SoundPage = "sound";
    /// <summary>The Shake and rumble page's id in the Esc menu.</summary>
    public const string FeelPage = "feel";
    const int SectionOrder = 500;
    const float Step = 0.1f;

    static SettingsMenu instance;
    PauseMenu.Page soundPage, feelPage;
    GUIStyle label;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        GameVolume.Load();
        if (instance != null) return;
        var go = new GameObject("Settings menu");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<SettingsMenu>();
    }

    void OnEnable()
    {
        soundPage ??= new PauseMenu.Page { Title = "Sound", Width = 520, Draw = DrawSound };
        PauseMenu.AddSection(DrawEntry, SectionOrder);
        PauseMenu.AddPage(SoundPage, soundPage);
        feelPage ??= new PauseMenu.Page { Title = "Shake and rumble", Width = 520, Draw = DrawFeel };
        PauseMenu.AddPage(FeelPage, feelPage);
    }

    void OnDisable()
    {
        PauseMenu.RemoveSection(DrawEntry);
        PauseMenu.RemovePage(SoundPage);
        PauseMenu.RemovePage(FeelPage);
    }
    void OnDestroy() { if (instance == this) instance = null; }

    void DrawEntry()
    {
        if (PauseMenu.Button("Sound", PauseMenu.ButtonStyle)) PauseMenu.Instance.ShowPage(SoundPage);
        if (PauseMenu.Button("Shake and rumble", PauseMenu.ButtonStyle)) PauseMenu.Instance.ShowPage(FeelPage);
    }

    void DrawSound()
    {
        label ??= new GUIStyle(GUI.skin.label) { fontSize = 18, richText = true, alignment = TextAnchor.MiddleCenter };
        Row("Master", () => GameVolume.Master, v => GameVolume.Master = v);
        Row("Effects", () => GameVolume.Effects, v => GameVolume.Effects = v);
        Row("Ambience (rain, wind, thunder)", () => GameVolume.Ambience, v => GameVolume.Ambience = v);
    }

    void DrawFeel()
    {
        label ??= new GUIStyle(GUI.skin.label) { fontSize = 18, richText = true, alignment = TextAnchor.MiddleCenter };
        Row("Camera shake, hit-stop and pad rumble", () => GameFeel.Shake, v => GameFeel.Shake = v);
        GUILayout.Space(6);
        GUILayout.Label(GameFeel.Shake > 0f ? "Punchy at 100%." : "Off: the camera stays still and the pad stays quiet.", label);
    }

    // A name and percentage over a slider, between − and + buttons.
    void Row(string name, Func<float> get, Action<float> set)
    {
        GUILayout.Space(6);
        GUILayout.Label($"{name}   <b>{Mathf.RoundToInt(get() * 100f)}%</b>", label);
        GUILayout.BeginHorizontal();
        if (PauseMenu.Button("−", null, GUILayout.Width(56), GUILayout.Height(34))) set(get() - Step);
        GUILayout.BeginVertical();
        GUILayout.Space(14);
        float slid = GUILayout.HorizontalSlider(get(), 0f, 1f);
        if (!Mathf.Approximately(slid, get())) set(slid);
        GUILayout.EndVertical();
        if (PauseMenu.Button("+", null, GUILayout.Width(56), GUILayout.Height(34))) set(get() + Step);
        GUILayout.EndHorizontal();
    }
}
