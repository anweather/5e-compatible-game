using UnityEngine;

// The one writer of the air into the drawing (docs/design/hills.md "One air", the boat game's B166): late each frame, after
// SkyLight has eased the weather, CloudSky has worked out the sky's horizon and UnderwaterView has said where the eye is,
// it makes the Air and writes it: _Air, _AirFar, _AirHorizon, _AirGlow and _AirSun for every shader that draws by
// Air.hlsl, and URP's fog (linear: its colour, start and end) for the things drawn with URP's own shaders, matched to
// the air near (Air.LinearFog). Nothing else writes the scene's fog (AirTests holds that). The map's dials are the far ring's
// (FarRing.AirDials: a hills map's clear air reaches kilometres; another generated map's haze closes at its known ring),
// or, on the valley, the scene's linear haze as SkyLight closes it in. Starts itself on every scene.
[DefaultExecutionOrder(1100)]
public sealed class AirInPlay : MonoBehaviour
{
    static readonly int AirId = Shader.PropertyToID("_Air"), AirFarId = Shader.PropertyToID("_AirFar"),
        HorizonId = Shader.PropertyToID("_AirHorizon"), GlowId = Shader.PropertyToID("_AirGlow"), SunId = Shader.PropertyToID("_AirSun");

    /// <summary>The air written last.</summary>
    public static Air Written { get; private set; }

    /// <summary>No air at all while true (the farringclip probe's bare still).</summary>
    public static bool Lifted { get; set; }

    /// <summary>A horizon colour held in place of the sky's (OvernightShowcase's dusk), or null.</summary>
    public static Color? HeldHorizon { get; set; }

    SkyLight skyLight;
    CloudSky cloudSky;
    bool linearScene;
    float sceneStart, sceneEnd;
    Color sceneColour;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        Written = default;
        HeldHorizon = null;
        Lifted = false;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Attach()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
        Begin();
    }

    static void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode) => Begin();

    static void Begin()
    {
        if (FindAnyObjectByType<AirInPlay>() == null) new GameObject("Air").AddComponent<AirInPlay>();
    }

    void Awake()
    {
        // The scene's own haze, as its builder set it: the valley's is linear.
        linearScene = RenderSettings.fogMode == FogMode.Linear;
        sceneStart = RenderSettings.fogStartDistance;
        sceneEnd = RenderSettings.fogEndDistance;
        sceneColour = RenderSettings.fogColor;
    }

    void LateUpdate()
    {
        if (skyLight == null) skyLight = SkyLight.InPlay;
        if (cloudSky == null) cloudSky = FindAnyObjectByType<CloudSky>();
        Write(Make());
    }

    Air Make()
    {
        bool sky = cloudSky != null && cloudSky.Ready;
        var horizon = HeldHorizon ?? (sky ? cloudSky.HorizonNow : sceneColour);
        var glow = sky && HeldHorizon == null ? cloudSky.SunGlow : Vector3.zero;
        var sun = sky ? cloudSky.SunDirection : Vector3.up;
        if (UnderwaterView.EyeUnder) return Air.Faded(0f, UnderwaterView.WaterSeen, UnderwaterView.WaterTint, Vector3.zero, Vector3.up);
        if (!RenderSettings.fog || Lifted) return Air.None(horizon);
        float rain = skyLight != null ? skyLight.Rain : 0f, fog = skyLight != null ? skyLight.Fog : 0f;
        var far = FarRing.Instance;
        if (far != null && far.IsReady)
        {
            var d = far.AirDials;
            return Air.Exponential(d.reach, Air.GaussianMetres(d.clear, d.rainy, rain, fog), d.clear, d.farFrom, d.farTo, horizon, glow, sun);
        }
        if (linearScene)
        {
            var (start, end) = skyLight != null ? skyLight.LinearHaze : (sceneStart, sceneEnd);
            return Air.Faded(start, end, horizon, glow, sun);
        }
        return Air.Exponential(0f, Air.HazeK / Mathf.Max(1e-6f, RenderSettings.fogDensity), Air.HazeK / Mathf.Max(1e-6f, RenderSettings.fogDensity), 0f, 0f, horizon, glow, sun);
    }

    static void Write(Air air)
    {
        Written = air;
        var mean = air.Mean;
        RenderSettings.fogColor = new Color(mean.x, mean.y, mean.z).gamma;
        var (start, end) = air.LinearFog;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogStartDistance = start;
        RenderSettings.fogEndDistance = end;
        Shader.SetGlobalVector(AirId, air.ForShaders);
        Shader.SetGlobalVector(AirFarId, air.FarForShaders);
        Shader.SetGlobalColor(HorizonId, air.Horizon);
        Shader.SetGlobalVector(GlowId, air.Glow);
        Shader.SetGlobalVector(SunId, air.Sun);
    }
}
