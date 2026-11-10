using UnityEngine;

// The wind the near trees sway in (J-091): SpeedTree's wind comes from a WindZone, and the scene has none, so this makes
// one and follows the weather with it: a breeze when it's still, harder in a windy stretch or a storm, blowing the way the
// world's wind does (World.WindDir: Gust of Wind's while it blows, else the weather's). Eased, so a change of weather
// doesn't jerk the trees. Only drawn (D4): nothing reads it back.
public sealed class TreeWind : MonoBehaviour
{
    /// <summary>The scene's wind (null before it starts).</summary>
    public static TreeWind Instance { get; private set; }

    /// <summary>How hard it blows now (the WindZone's main), and how hard it's heading for.</summary>
    public float Main => zone != null ? zone.windMain : 0f;
    public float Target { get; private set; }

    const float Breeze = 0.25f, Windy = 0.8f, Storm = 1.2f, EaseSeconds = 6f;

    WindZone zone;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Instance != null || FindAnyObjectByType<WindZone>() != null) return;
        var go = new GameObject("TreeWind");
        DontDestroyOnLoad(go);
        go.AddComponent<TreeWind>();
    }

    void Awake()
    {
        Instance = this;
        zone = gameObject.AddComponent<WindZone>();
        zone.mode = WindZoneMode.Directional;
        zone.windMain = Breeze;
        zone.windTurbulence = 0.3f;
        zone.windPulseMagnitude = 0.4f;
        zone.windPulseFrequency = 0.08f;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Update()
    {
        var world = ChemGrid.Current?.World;
        int dir = world != null ? world.WindDir : -1;
        var kind = world?.Weather.Kind ?? Chem.WeatherKind.Clear;
        Target = kind switch
        {
            Chem.WeatherKind.Storm => Storm,
            Chem.WeatherKind.Windy => Windy,
            _ => dir >= 0 ? Windy * 0.75f : Breeze,
        };
        float step = Time.deltaTime / EaseSeconds;
        zone.windMain = Mathf.MoveTowards(zone.windMain, Target, step * Storm);
        zone.windTurbulence = 0.3f + zone.windMain * 0.4f;
        // Toward its direction: 0..3 N E S W, north +z (CloudSky's drift); without one, from the west-south-west.
        var toward = dir switch
        {
            0 => Vector3.forward,
            1 => Vector3.right,
            2 => Vector3.back,
            3 => Vector3.left,
            _ => new Vector3(0.8f, 0f, 0.6f),
        };
        var want = Quaternion.LookRotation(toward);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, want, 30f * Time.deltaTime);
    }
}
