using Chem;
using UnityEngine;

// How lit the lamps are and how bright the stars (the view's half of the night, with Nightfall; the rules' half is
// World.Night), from the light level the rules give (World.Daylight: bright, dim at dawn and dusk, dark at night),
// eased so a change fades over a few seconds. Every peer's world has the same light level at the same tick, so every
// screen lights its lamps together. Village lights its windows and door lamps from Lamps, the wet cells' shine
// (CellSlickMesh) follows them, and CloudSky its stars from Stars.
public static class NightLamps
{
    static float lamps, stars;
    static int steppedFrame = -1;

    const float DimLamps = 0.6f, DimStars = 0.5f, DarkStars = 1.6f;
    const float Ease = 1.2f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        lamps = stars = 0f;
        steppedFrame = -1;
    }

    public static float Lamps(World world)
    {
        Step(world);
        return lamps;
    }

    public static float Stars(World world)
    {
        Step(world);
        return stars;
    }

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
}
