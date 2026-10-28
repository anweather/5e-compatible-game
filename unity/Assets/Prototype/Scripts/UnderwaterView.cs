using UnityEngine;

// What the camera sees with its eye under water (Andrew's playtest #7): a blue-green
// tint that closes in a few metres away, no sky (a deep blue backdrop instead), and
// the surface overhead seen from below as a bright, flat sheet. Under the water drawn
// on a cell (ChemGrid.WaterSurfaceY, the lake's level) or under the sea (Ocean: its
// level and swell, seaward of its coast where the ground is below it). Everything
// is put back as the eye comes up; SkyLight sets the haze again each frame anyway.
// Starts itself on the main camera, so no scene change is needed.
[DefaultExecutionOrder(1000)]
[RequireComponent(typeof(Camera))]
public class UnderwaterView : MonoBehaviour
{
    static readonly Color Tint = new(0.06f, 0.27f, 0.32f);
    static readonly Color Deep = new(0.02f, 0.11f, 0.16f);
    static readonly Color Surface = new(0.55f, 0.82f, 0.86f);
    // How far you can see under water (m), and the surface sheet's half size.
    const float FogEnd = 14f, SheetHalf = 60f;

    Camera cam;
    ChemGrid grid;
    GameObject sheet;
    bool under;
    CameraClearFlags clear;
    Color background;
    bool fogWas;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Attach()
    {
        var main = Camera.main;
        if (main != null && main.GetComponent<UnderwaterView>() == null) main.gameObject.AddComponent<UnderwaterView>();
    }

    void Awake()
    {
        cam = GetComponent<Camera>();
        grid = FindFirstObjectByType<ChemGrid>();
    }

    void LateUpdate()
    {
        if (grid == null || grid.World == null) return;
        var eye = transform.position;
        float top = WaterTop(grid, eye);
        bool now = eye.y < top - 0.05f;
        if (now != under) Switch(now);
        if (!under) return;
        RenderSettings.fog = true;
        RenderSettings.fogColor = Tint;
        RenderSettings.fogStartDistance = 0f;
        RenderSettings.fogEndDistance = FogEnd;
        sheet.transform.position = new Vector3(eye.x, top, eye.z);
    }

    /// <summary>
    /// The top of the water over a point: a cell's drawn water (lakes, rivers, pools; not ice) or
    /// the sea with its swell, seaward of the coast over ground below it; negative infinity where
    /// there's none. WaterSplashes uses it too.
    /// </summary>
    public static float WaterTop(ChemGrid grid, Vector3 eye)
    {
        var cell = grid.CellAt(eye);
        var world = grid.World;
        bool inside = cell.x >= 0 && cell.y >= 0 && cell.x < world.W && cell.y < world.H;
        float top = float.NegativeInfinity, ground = float.NegativeInfinity;
        if (inside)
        {
            var c = world.At(cell.x, cell.y);
            ground = grid.WaterSurfaceY(cell.x, cell.y);
            if (c != null && c.Water > 0 && !c.Frozen) top = ground;
        }
        var sea = Ocean.Instance;
        var xz = new Vector2(eye.x, eye.z);
        // The sea: out from the coast (Ocean.shoreOrigin, seaward), over ground below its level.
        bool seaward = sea != null && Vector2.Dot(xz - sea.shoreOrigin, sea.seaward.normalized) > 0f;
        if (seaward && (!inside || ground < sea.level))
            top = Mathf.Max(top, sea.level + sea.WaveOffset(xz));
        return top;
    }

    void Switch(bool toUnder)
    {
        under = toUnder;
        if (toUnder)
        {
            clear = cam.clearFlags;
            background = cam.backgroundColor;
            fogWas = RenderSettings.fog;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Deep;
            if (sheet == null) sheet = MakeSheet();
            sheet.SetActive(true);
            return;
        }
        cam.clearFlags = clear;
        cam.backgroundColor = background;
        RenderSettings.fog = fogWas;
        if (sheet != null) sheet.SetActive(false);
    }

    // A flat sheet whose face looks down (seen from below) but is lit as if it faced up, so
    // the daylight shows through it; VillageLit draws its vertex colour.
    static GameObject MakeSheet()
    {
        var mesh = new Mesh { name = "Water from below" };
        float h = SheetHalf;
        mesh.vertices = new[] { new Vector3(-h, 0f, -h), new Vector3(h, 0f, -h), new Vector3(h, 0f, h), new Vector3(-h, 0f, h) };
        mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
        mesh.colors = new[] { Surface, Surface, Surface, Surface };
        mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        mesh.bounds = new Bounds(Vector3.zero, new Vector3(2f * h, 1f, 2f * h));
        var go = new GameObject("Water from below", typeof(MeshFilter), typeof(MeshRenderer));
        go.GetComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.GetComponent<MeshRenderer>();
        r.sharedMaterial = new Material(Shader.Find("Prototype/VillageLit"));
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        go.SetActive(false);
        return go;
    }

    void OnDisable()
    {
        if (under) Switch(false);
    }
}
