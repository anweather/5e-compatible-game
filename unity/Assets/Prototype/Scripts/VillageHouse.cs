using Chem;
using UnityEngine;

// One building of a village (D12): the scene keeps only its plan (kind, size,
// walls, roof, seed), and it builds its own meshes when it loads, in the Editor
// too, so nothing generated reaches the scene file or git. Its two children,
// the body and the "Roof", are what ValleyChemistry chars and hides when the
// building burns, as it does a cabin's. ValleyBuilder places it; its local -z
// faces its street.
[ExecuteAlways]
public class VillageHouse : MonoBehaviour
{
    public BuildingKind kind;
    public WallKind walls;
    public RoofKind roof;
    public RoofCover cover;
    [Range(1, 3)] public int storeys = 1;
    public bool chimney = true;
    public int seed;
    [Tooltip("The footprint in metres, along the front and front to back.")]
    public float width = 6f, depth = 4.5f;
    [Tooltip("Drawn with VillageLit; the Village above sets it if this is empty.")]
    public Material material;
    [Tooltip("The window panes' material (WindowGlass).")]
    public Material glass;
    [Tooltip("The windows from the plan: x its wall (BuildingMesher.Spec), y its place along it in metres. Empty: the mesher spaces them itself, with no cells behind them.")]
    public Vector2[] windows = System.Array.Empty<Vector2>();
    [Tooltip("Where the door stands along the front (m), from the plan; NaN: the mesher picks.")]
    public float doorAt = float.NaN;

    Mesh body, roofMesh;
    MeshCollider roofCollider;
    MeshRenderer roofRenderer;
    readonly System.Collections.Generic.List<GameObject> solids = new();
    GameObject bodyGo, roofGo;

    /// <summary>What a burning or wrecked house is called in the log.</summary>
    public string Label => kind switch
    {
        BuildingKind.Inn => "inn",
        BuildingKind.Shop => "shop",
        _ => walls == WallKind.Stone ? "stone house" : "house",
    };

    public BuildingMesher.Spec Spec => new()
    {
        kind = kind,
        walls = walls,
        roof = roof,
        cover = cover,
        storeys = storeys,
        chimney = chimney,
        seed = seed,
        width = width,
        depth = depth,
        windows = windows,
        doorAt = float.IsNaN(doorAt) ? null : doorAt,
    };

    void OnEnable() => Rebuild();

    void OnDisable() => Clear();

    /// <summary>Builds the meshes again from the fields.</summary>
    public void Rebuild()
    {
        Clear();
        if (material == null)
        {
            var village = GetComponentInParent<Village>();
            if (village != null) material = village.material;
        }
        var made = BuildingMesher.Make(Spec);
        body = made.body;
        roofMesh = made.roof;
        bodyGo = Part("Body", body);
        roofGo = Part("Roof", roofMesh);
        if (!TryGetComponent<WindowPanes>(out var panes)) panes = gameObject.AddComponent<WindowPanes>();
        panes.Show(made.panes, glass);
        // The roof can be stood on, and the window boxes and planters bumped into (Andrew's
        // playtest #4); the builder's box for the walls stops at the eaves.
        roofCollider = roofGo.AddComponent<MeshCollider>();
        roofCollider.sharedMesh = roofMesh;
        roofRenderer = roofGo.GetComponent<MeshRenderer>();
        foreach (var (at, size) in made.solids)
        {
            var solid = new GameObject("Solid") { hideFlags = HideFlags.DontSave | HideFlags.NotEditable };
            solid.transform.SetParent(transform, false);
            solid.transform.localPosition = at;
            solid.AddComponent<BoxCollider>().size = size;
            solids.Add(solid);
        }
    }

    // A building that burns down or caves in has its roof hidden (ValleyChemistry): its roof
    // stops being something to stand on with it.
    void Update()
    {
        if (roofCollider != null && roofRenderer != null && roofCollider.enabled != roofRenderer.enabled)
            roofCollider.enabled = roofRenderer.enabled;
    }

    GameObject Part(string name, Mesh mesh)
    {
        mesh.hideFlags = HideFlags.DontSave;
        var go = new GameObject(name) { hideFlags = HideFlags.DontSave | HideFlags.NotEditable };
        go.transform.SetParent(transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = material;
        return go;
    }

    void Clear()
    {
        DestroyNow(bodyGo);
        DestroyNow(roofGo);
        foreach (var solid in solids) DestroyNow(solid);
        solids.Clear();
        roofCollider = null;
        roofRenderer = null;
        DestroyNow(body);
        DestroyNow(roofMesh);
        bodyGo = roofGo = null;
        body = roofMesh = null;
    }

    static void DestroyNow(Object o)
    {
        if (o == null) return;
        if (Application.isPlaying) Destroy(o);
        else DestroyImmediate(o);
    }
}
