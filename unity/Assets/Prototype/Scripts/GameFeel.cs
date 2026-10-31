using UnityEngine;

// The player's "Shake and rumble" setting, kept between sessions (PlayerPrefs,
// as GameVolume): 1 is punchy, 0 is still. Everything a cast does to the camera
// and the pad scales by it, and at 0 none of it happens: no hit-stop, no impulse,
// no tightening, no rumble. The Esc menu changes it (SettingsMenu).
public static class GameFeel
{
    const string ShakeKey = "feel.shake";

    static bool loaded;
    static float shake = 1f;

    // Starts over each Play mode, even with the domain reload off (Enter Play Mode Options).
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        loaded = false;
        shake = 1f;
    }

    /// <summary>How much of the shake and rumble to play, 0 (off) to 1.</summary>
    public static float Shake
    {
        get
        {
            if (!loaded)
            {
                loaded = true;
                shake = PlayerPrefs.GetFloat(ShakeKey, shake);
            }
            return shake;
        }
        set
        {
            loaded = true;
            shake = Mathf.Clamp01(Mathf.Round(value * 20f) / 20f);
            PlayerPrefs.SetFloat(ShakeKey, shake);
            PlayerPrefs.Save();
        }
    }
}
