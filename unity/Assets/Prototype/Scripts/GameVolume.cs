using UnityEngine;

// The player's volume settings, kept between sessions (PlayerPrefs): master
// (everything, through the AudioListener), effects (spells, fire, breaking
// things) and ambience (rain, wind, thunder). Whatever plays a sound scales
// its volume by its kind's setting; the Sound page of the menu (SettingsMenu)
// changes them.
public static class GameVolume
{
    const string MasterKey = "volume.master", EffectsKey = "volume.effects", AmbienceKey = "volume.ambience";

    static bool loaded;

    // Starts over each Play mode, even with the domain reload off (Enter Play Mode Options).
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        loaded = false;
    }

    static float master = 0.8f, effects = 1f, ambience = 0.7f;

    public static float Master
    {
        get { Load(); return master; }
        set { master = Save(MasterKey, value); AudioListener.volume = master; }
    }

    public static float Effects
    {
        get { Load(); return effects; }
        set => effects = Save(EffectsKey, value);
    }

    public static float Ambience
    {
        get { Load(); return ambience; }
        set => ambience = Save(AmbienceKey, value);
    }

    /// <summary>Reads the saved settings (once) and sets the listener's volume.</summary>
    public static void Load()
    {
        if (loaded) return;
        loaded = true;
        master = PlayerPrefs.GetFloat(MasterKey, master);
        effects = PlayerPrefs.GetFloat(EffectsKey, effects);
        ambience = PlayerPrefs.GetFloat(AmbienceKey, ambience);
        AudioListener.volume = master;
    }

    static float Save(string key, float value)
    {
        Load();
        value = Mathf.Clamp01(Mathf.Round(value * 20f) / 20f);
        PlayerPrefs.SetFloat(key, value);
        PlayerPrefs.Save();
        return value;
    }
}
