using UnityEngine;

// A generated prop on the coast (D12): a palm, a glowbulb, a lounger, an
// umbrella, a towel, a bench, driftwood, a starfish or a dock. The scene keeps only its
// kind and seed; it builds its mesh with PropMesher when it loads, in the Editor
// too. A palm stands on a tree square: when that burns (ValleyChemistry) the
// palm chars down to a stump.
[ExecuteAlways]
public class GeneratedProp : MonoBehaviour
{
    public enum Kind { Palm, Glowbulb, Lounger, Umbrella, Towel, Bench, Driftwood, Starfish, Dock }

    public Kind kind;
    public int seed;
    [Tooltip("A dock's length (m).")]
    public float length = 20f;
    [Tooltip("Drawn with VillageLit (vertex colours).")]
    public Material material;

    Mesh mesh;
    GameObject shape;

    /// <summary>Whether it has burned (a palm whose square burned).</summary>
    public bool Burnt { get; private set; }

    void OnEnable() => Rebuild();

    void OnDisable()
    {
        if (shape != null) DestroyNow(shape);
        if (mesh != null) DestroyNow(mesh);
    }

    public void Rebuild()
    {
        OnDisable();
        mesh = Make(kind, seed, length, out _);
        mesh.hideFlags = HideFlags.DontSave;
        shape = new GameObject(kind.ToString()) { hideFlags = HideFlags.DontSave | HideFlags.NotEditable };
        shape.transform.SetParent(transform, false);
        shape.AddComponent<MeshFilter>().sharedMesh = mesh;
        shape.AddComponent<MeshRenderer>().sharedMaterial = material;
    }

    /// <summary>The mesh for a kind and seed; <paramref name="top"/> is a palm's crown (zero otherwise).</summary>
    public static Mesh Make(Kind kind, int seed, float length, out Vector3 top)
    {
        top = Vector3.zero;
        return kind switch
        {
            Kind.Palm => PropMesher.Palm(seed, out top),
            Kind.Glowbulb => PropMesher.Glowbulb(seed),
            Kind.Lounger => PropMesher.Lounger(seed),
            Kind.Umbrella => PropMesher.Umbrella(seed),
            Kind.Towel => PropMesher.Towel(seed),
            Kind.Bench => PropMesher.Bench(seed),
            Kind.Driftwood => PropMesher.Driftwood(seed),
            Kind.Dock => PropMesher.Dock(seed, length),
            _ => PropMesher.Starfish(seed),
        };
    }

    /// <summary>Its square burned: a palm chars down to a stump (its fronds gone).</summary>
    public void Burn(Material charred)
    {
        if (Burnt || shape == null) return;
        Burnt = true;
        shape.GetComponent<MeshRenderer>().sharedMaterial = charred;
        shape.transform.localScale = new Vector3(1f, 0.3f, 1f);
        foreach (var c in GetComponents<Collider>()) c.enabled = false;
    }

    static void DestroyNow(Object o)
    {
        if (Application.isPlaying) Destroy(o);
        else DestroyImmediate(o);
    }
}
