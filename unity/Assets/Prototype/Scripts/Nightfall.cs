using Chem;
using UnityEngine;

// The view's half of the night (backlog #8; the rules' half is World.Night): how
// lit the lamps are and how bright the stars, from the light level the rules
// give (World.Daylight: bright, dim at dawn and dusk, dark at night), eased so a
// change fades over a few seconds. Every peer's world has the same light level
// at the same tick, so every screen lights its lamps together. Village lights
// its windows and door lamps from Lamps, CloudSky its stars from Stars, and
// AmbientCritters its fireflies by the hour. It also says once a night, when
// the wizard stands in the dark with no light of its own, how to see.
public class Nightfall : MonoBehaviour
{
    static float lamps, stars;
    static int steppedFrame = -1;

    // How lit the lamps are and how bright the stars at each light level.
    const float DimLamps = 0.6f, DimStars = 0.5f, DarkStars = 1.6f;
    // How fast a change fades in (per second).
    const float Ease = 1.2f;
    // A light this close to the wizard counts as its own (a lantern, a glowing ball, Dancing Lights, a lamp).
    const float OwnLightReach = 6f;

    ChemGrid grid;
    float nextLook;
    bool hinted;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        lamps = stars = 0f;
        steppedFrame = -1;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        var go = new GameObject("Nightfall");
        DontDestroyOnLoad(go);
        go.AddComponent<Nightfall>();
    }

    /// <summary>0 by day to 1 at night: how lit the lamps and windows are (eased).</summary>
    public static float Lamps(World world)
    {
        Step(world);
        return lamps;
    }

    /// <summary>0 by day, about 1.6 on a dark night: how bright the stars are (eased; clouds hide them on top).</summary>
    public static float Stars(World world)
    {
        Step(world);
        return stars;
    }

    // Once a frame, whoever asks first.
    static void Step(World world)
    {
        if (world == null || steppedFrame == Time.frameCount) return;
        bool first = steppedFrame < 0;
        steppedFrame = Time.frameCount;
        var level = world.Daylight;
        float lampsTo = level switch { LightLevel.Bright => 0f, LightLevel.Dim => DimLamps, _ => 1f };
        float starsTo = level switch { LightLevel.Bright => 0f, LightLevel.Dim => DimStars, _ => DarkStars };
        float k = first ? 1f : 1f - Mathf.Exp(-Time.deltaTime * Ease);
        lamps = Mathf.Lerp(lamps, lampsTo, k);
        stars = Mathf.Lerp(stars, starsTo, k);
    }

    // Twice a second: is the wizard in the dark with nothing to see by? Said once a night.
    void Update()
    {
        if (Time.unscaledTime < nextLook) return;
        nextLook = Time.unscaledTime + 0.5f;
        if (grid == null) grid = FindAnyObjectByType<ChemGrid>();
        var world = grid != null ? grid.World : null;
        var wizard = WizardRig.FindLocal();
        if (world == null || wizard == null) return;
        if (world.Daylight == LightLevel.Bright)
        {
            hinted = false;
            return;
        }
        if (hinted) return;
        var at = grid.CellAt(wizard.transform.position);
        if (world.LightAt(at.x, at.y) != LightLevel.Dark || OwnLight(wizard.transform.position)) return;
        hinted = true;
        EventLog.Add("It's dark: carry a lantern or a glowing ball, or cast Dancing Lights, to see by");
    }

    static bool OwnLight(Vector3 at)
    {
        foreach (var light in FindObjectsByType<Light>(FindObjectsSortMode.None))
            if (light.enabled && light.type != LightType.Directional && Vector3.Distance(light.transform.position, at) < OwnLightReach)
                return true;
        return false;
    }
}
