using UnityEngine;

// A village in the scene (D12, docs/WORLD_LIFE.md): the root of its
// buildings (VillageHouse, each building its own meshes) and its square's
// well, which this builds the same way. It also sets how brightly every
// village window glows (VillageLit's _LampGlow) from the sky's daylight.
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
            nextSkyLookup = Time.unscaledTime + 2f;
        }
        if (sky != null) Shader.SetGlobalFloat(LampGlow, Mathf.SmoothStep(0f, 1f, 1f - sky.Daylight));
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
