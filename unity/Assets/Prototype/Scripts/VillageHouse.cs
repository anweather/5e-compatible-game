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

    Mesh body, roofMesh;
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
