using Punchy.Fauna;
using UnityEngine;

// A village in the scene (D12, docs/WORLD_LIFE.md): the root of its
// buildings (VillageHouse, each building its own meshes), its folk (residents,
// whom ValleyFauna brings out leashed to the square) and its square's
// well, which this builds the same way. It also sets how brightly every
// village window glows (VillageLit's _LampGlow), and lights the inn's and the shop's
// door lights and a lamp over the well as the rules' daylight drops at dusk (Nightfall).
// ValleyBuilder lays it out from world/villages.json through the core's
// VillageLayout, so its walls are cells like the cabins'.
[ExecuteAlways]
public class Village : MonoBehaviour
{
    [Tooltip("The village's name in world/villages.json.")]
    public string villageName;
    [Tooltip("What its buildings are drawn with (VillageLit).")]
    public Material material;
    [Tooltip("Where the well stands, on the square (local).")]
    public Vector3 wellAt;
    public int seed;
    [Tooltip("Who lives here (one of Punchy's folk), how many are out and about, and how far they wander from the square (m). ValleyFauna brings them out when someone comes near.")]
    public CreatureSpecies residents;
    public int residentCount = 6;
    public float wander = 22f;

    static readonly int LampGlow = Shader.PropertyToID("_LampGlow");

    SkyLight sky;
    float nextSkyLookup;
    GameObject well;
    Mesh wellMesh;

    void OnEnable()
    {
        BuildWell();
        Shader.SetGlobalFloat(LampGlow, 0.3f);
    }

    void OnDisable()
    {
        if (well != null) DestroyNow(well);
        if (wellMesh != null) DestroyNow(wellMesh);
    }

    void Update()
    {
        if (sky == null && Time.unscaledTime >= nextSkyLookup)
        {
            sky = FindAnyObjectByType<SkyLight>();
            grid = FindAnyObjectByType<ChemGrid>();
            nextSkyLookup = Time.unscaledTime + 2f;
        }
        // Playing, the windows and door lamps light as the rules' daylight drops from bright
        // (Nightfall); in the Editor, from the sky as it stands.
        var world = grid != null ? grid.World : null;
        float lit;
        if (world != null) lit = Nightfall.Lamps(world);
        else if (sky != null) lit = Mathf.SmoothStep(0f, 1f, 1f - sky.Daylight);
        else return;
        Shader.SetGlobalFloat(LampGlow, lit);
        if (Application.isPlaying) LightLamps(lit);
    }

    // --- door lamps --------------------------------------------------------------------------

    // The inn's and the shop's door lights (the buildings' own) and a lamp over the well, lit at dusk:
    // each at its own brightness times how lit the lamps are.
    const string DoorLight = "Door light";
    const float WellRange = 8f, WellIntensity = 1.6f;
    static readonly Color LampColour = new(1f, 0.72f, 0.42f);
    // Each lamp with its building's roof: a building that burnt down or caved in has its roof hidden
    // (ValleyChemistry), and its lamp stays out (Andrew's playtest #10c). The well's lamp has none.
    readonly System.Collections.Generic.List<(Light lamp, float full, Renderer roof)> lamps = new();
    ChemGrid grid;
    bool lampsFound;

    void LightLamps(float lit)
    {
        if (!lampsFound) FindLamps();
        foreach (var (lamp, full, roof) in lamps)
        {
            if (lamp == null) continue;
            bool standing = roof == null || roof.enabled;
            lamp.enabled = standing && lit > 0.02f;
            lamp.intensity = full * lit;
        }
    }

    void FindLamps()
    {
        lampsFound = true;
        foreach (var light in GetComponentsInChildren<Light>(true))
        {
            if (light.name != DoorLight) continue;
            Renderer roof = null;
            if (light.transform.parent != null)
                foreach (var r in light.transform.parent.GetComponentsInChildren<Renderer>(true))
                    if (r.name == "Roof") { roof = r; break; }
            lamps.Add((light, light.intensity, roof));
        }
        var go = new GameObject("Well lamp") { hideFlags = HideFlags.DontSave };
        go.transform.SetParent(transform, false);
        go.transform.position = transform.TransformPoint(wellAt) + Vector3.up * 2.2f;
        var lamp = go.AddComponent<Light>();
        lamp.type = LightType.Point;
        lamp.color = LampColour;
        lamp.range = WellRange;
        lamp.shadows = LightShadows.None;
        lamp.enabled = false;
        lamps.Add((lamp, WellIntensity, null));
    }

    // A round stone well with two posts, a little shingled roof, a windlass and a bucket.
    void BuildWell()
    {
        var kit = new MeshKit();
        var stone = new Color(0.55f, 0.54f, 0.5f, 0f);
        var wood = new Color(0.33f, 0.23f, 0.15f, 0f);
        var roofColour = new Color(0.45f, 0.3f, 0.2f, 0f);
        var rng = new System.Random(seed);
        const int stones = 12;
        const float radius = 0.75f;
        for (int course = 0; course < 3; course++)
            for (int i = 0; i < stones; i++)
            {
                float t = (i + course * 0.5f) * Mathf.PI * 2f / stones;
                var at = new Vector3(Mathf.Cos(t) * radius, 0.15f + course * 0.28f, Mathf.Sin(t) * radius);
                float k = 0.9f + (float)rng.NextDouble() * 0.2f;
                kit.Box(at, new Vector3(0.42f, 0.27f, 0.25f), new Color(stone.r * k, stone.g * k, stone.b * k, 0f), Quaternion.Euler(0f, -t * Mathf.Rad2Deg + 90f, 0f));
            }
        for (int side = -1; side <= 1; side += 2)
            kit.Box(new Vector3(side * 0.85f, 1.15f, 0f), new Vector3(0.14f, 2.3f, 0.14f), wood);
        kit.Cylinder(new Vector3(-0.85f, 1.55f, 0f), new Vector3(0.85f, 1.55f, 0f), 0.07f, 6, wood);
        kit.Box(new Vector3(0f, 1.2f, 0f), new Vector3(0.28f, 0.3f, 0.28f), new Color(0.4f, 0.3f, 0.2f, 0f));
        kit.Box(new Vector3(0f, 1.42f, 0f), new Vector3(0.02f, 0.25f, 0.02f), new Color(0.6f, 0.55f, 0.45f, 0f));
        for (int side = -1; side <= 1; side += 2)
        {
            var down = new Vector3(0f, -0.6f, side * 0.8f).normalized;
            var up = new Vector3(0f, 0.8f, side * 0.6f);
            kit.Box(new Vector3(0f, 2.45f, side * 0.45f), new Vector3(2.1f, 0.06f, 1.05f), roofColour, Quaternion.LookRotation(down, up));
        }
        wellMesh = kit.ToMesh("Well");
        wellMesh.hideFlags = HideFlags.DontSave;
        well = new GameObject("Well") { hideFlags = HideFlags.DontSave | HideFlags.NotEditable };
        well.transform.SetParent(transform, false);
        well.transform.localPosition = wellAt;
        well.AddComponent<MeshFilter>().sharedMesh = wellMesh;
        well.AddComponent<MeshRenderer>().sharedMaterial = material;
    }

    static void DestroyNow(Object o)
    {
        if (Application.isPlaying) Destroy(o);
        else DestroyImmediate(o);
    }
}
