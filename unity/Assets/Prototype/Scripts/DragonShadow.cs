using UnityEngine;

// The shadow that crosses the valley: a sheet laid over the ground under the dragon,
// its dark shape drawn by Prototype/DragonShadow. The sun's own shadow won't do: the
// dragon is high and the sky dark when it matters, and the sun casts only near the
// camera. A small grid of vertices moved each frame, not a decal: it lies on any
// hillside, in one draw.
public class DragonShadow : MonoBehaviour
{
    const int Side = 17;
    const float Above = 0.6f;
    static readonly int StrengthId = Shader.PropertyToID("_Strength");

    ChemGrid grid;
    Mesh mesh;
    MeshRenderer look;
    MaterialPropertyBlock block;
    readonly Vector3[] vertices = new Vector3[Side * Side];

    /// <summary>A shadow on a new child of <paramref name="view"/>.</summary>
    public static DragonShadow On(Transform view, ChemGrid grid, Shader shader)
    {
        var go = new GameObject("Dragon's shadow");
        go.transform.SetParent(view, false);
        var shadow = go.AddComponent<DragonShadow>();
        shadow.Begin(grid, shader);
        return shadow;
    }

    void Begin(ChemGrid on, Shader shader)
    {
        grid = on;
        mesh = new Mesh { name = "Dragon's shadow", bounds = new Bounds(Vector3.zero, Vector3.one * 100000f) };
        mesh.MarkDynamic();
        var uv = new Vector2[Side * Side];
        var triangles = new int[(Side - 1) * (Side - 1) * 6];
        for (int y = 0, t = 0; y < Side; y++)
            for (int x = 0; x < Side; x++)
            {
                uv[y * Side + x] = new Vector2(x, y) / (Side - 1f);
                if (x == Side - 1 || y == Side - 1) continue;
                int i = y * Side + x;
                foreach (int k in new[] { i, i + Side, i + 1, i + 1, i + Side, i + Side + 1 }) triangles[t++] = k;
            }
        mesh.vertices = vertices;
        mesh.uv = uv;
        mesh.triangles = triangles;
        gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
        look = gameObject.AddComponent<MeshRenderer>();
        look.sharedMaterial = new Material(shader);
        look.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        look.receiveShadows = false;
        block = new MaterialPropertyBlock();
        look.enabled = false;
    }

    /// <summary>Lays the shadow, <paramref name="size"/> metres across, round a point, its nose along <paramref name="forward"/>.</summary>
    public void Lay(Vector3 at, Vector3 forward, float size, float strength)
    {
        forward.y = 0f;
        forward = forward.sqrMagnitude > 0.001f ? forward.normalized : Vector3.forward;
        var right = new Vector3(forward.z, 0f, -forward.x);
        for (int y = 0; y < Side; y++)
            for (int x = 0; x < Side; x++)
            {
                var p = at + right * ((x / (Side - 1f) - 0.5f) * size) + forward * ((y / (Side - 1f) - 0.5f) * size);
                p.y = Mathf.Max(grid.GroundAt(p), grid.valley != null ? grid.valley.lakeLevel : 0f) + Above;
                vertices[y * Side + x] = p;
            }
        mesh.vertices = vertices;
        block.SetFloat(StrengthId, strength);
        look.SetPropertyBlock(block);
        look.enabled = true;
    }

    public void Hide()
    {
        if (look != null) look.enabled = false;
    }

    void OnDestroy()
    {
        if (look != null) Destroy(look.sharedMaterial);
        if (mesh != null) Destroy(mesh);
    }
}
