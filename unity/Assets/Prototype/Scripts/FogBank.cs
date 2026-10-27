using System.Collections.Generic;
using UnityEngine;

// Fog Cloud's fog, drawn as soft billboards: a few big, faint puffs stacked
// over each square of the core's "fog" gas, turned to the camera, drifting
// and breathing a little, thicker where the fog is thicker, fading in and out
// as it comes and goes. Overlapping, they read as a bank of volumetric fog
// rather than as cards. Lit, so it greys at dusk and darkens at night.
// Presentation only: where the fog is, and what it hides, is the core's
// (World.Fogged, World.LineOfSight). ChemGrid.RenderGas feeds it; it lives on
// a child of the grid, made when the first fog comes.
public class FogBank : MonoBehaviour
{
    // Puffs over each square, and how high they stack, in squares.
    const int PuffsPerSquare = 3;
    const float Rise = 0.75f;
    // A puff's size across, in squares, and its opacity over the thickest fog.
    const float PuffSize = 2.8f, Opacity = 0.42f;
    // How fast a square's fog fades in or out (opacity a second), and how far a puff wanders (in squares).
    const float FadeRate = 0.8f, Wander = 0.35f;

    sealed class Square
    {
        public Vector3 home;
        public float target, shown, seed;
        public readonly List<Renderer> puffs = new();
    }

    readonly Dictionary<int, Square> squares = new();
    readonly Stack<Square> free = new();
    readonly List<int> gone = new();
    MaterialPropertyBlock block;
    Material material;
    Texture2D texture;
    float cellSize = 1.5f;
    static readonly Color Tint = new(0.86f, 0.88f, 0.9f, 1f);

    /// <summary>A fog bank on a new child of <paramref name="grid"/>.</summary>
    public static FogBank On(ChemGrid grid)
    {
        var go = new GameObject("Fog");
        go.transform.SetParent(grid.transform, false);
        var bank = go.AddComponent<FogBank>();
        bank.cellSize = grid.cellSize;
        return bank;
    }

    /// <summary>How many squares of fog are drawn (fading ones too), for the probe.</summary>
    public int SquaresShown => squares.Count;

    /// <summary>
    /// The fog over square <paramref name="index"/> standing on <paramref name="ground"/> at
    /// <paramref name="center"/>: <paramref name="amount"/> units out of <paramref name="max"/>; 0 fades it out.
    /// </summary>
    public void Set(int index, Vector3 center, float ground, int amount, int max)
    {
        float target = max > 0 ? Mathf.Clamp01(amount / (float)max) : 0f;
        if (!squares.TryGetValue(index, out var s))
        {
            if (target <= 0f) return;
            s = Take();
            s.shown = 0f;
            s.seed = Mathf.Abs(index * 0.6180339f % 1f) * 100f;
            squares[index] = s;
        }
        s.home = new Vector3(center.x, ground, center.z);
        s.target = target;
    }

    void LateUpdate()
    {
        var cam = Camera.main;
        if (cam == null || squares.Count == 0) return;
        var turn = cam.transform.rotation;
        float t = Time.time, step = Time.deltaTime * FadeRate;
        gone.Clear();
        foreach (var pair in squares)
        {
            var s = pair.Value;
            s.shown = Mathf.MoveTowards(s.shown, s.target, step);
            if (s.shown <= 0f && s.target <= 0f)
            {
                gone.Add(pair.Key);
                continue;
            }
            Place(s, turn, t);
        }
        foreach (int index in gone)
        {
            var s = squares[index];
            squares.Remove(index);
            foreach (var r in s.puffs) r.gameObject.SetActive(false);
            free.Push(s);
        }
    }

    // A square's puffs, stacked and drifting about its centre, faced to the camera.
    void Place(Square s, Quaternion turn, float t)
    {
        var color = Tint;
        color.a = Opacity * s.shown;
        for (int i = 0; i < s.puffs.Count; i++)
        {
            var r = s.puffs[i];
            float phase = s.seed + i * 2.1f;
            var drift = new Vector3(Mathf.Sin(t * 0.13f + phase), 0f, Mathf.Cos(t * 0.11f + phase * 1.3f)) * (Wander * cellSize);
            float breathe = 1f + 0.12f * Mathf.Sin(t * 0.3f + phase);
            r.transform.SetPositionAndRotation(s.home + drift + Vector3.up * ((0.45f + i * Rise) * cellSize), turn * Quaternion.Euler(0f, 0f, phase * 40f));
            r.transform.localScale = Vector3.one * (PuffSize * cellSize * breathe);
            UrpMaterials.SetColor(r, block, color);
        }
    }

    Square Take()
    {
        if (free.Count > 0)
        {
            var s = free.Pop();
            foreach (var r in s.puffs) r.gameObject.SetActive(true);
            return s;
        }
        EnsureMaterial();
        var made = new Square();
        for (int i = 0; i < PuffsPerSquare; i++)
        {
            var quad = Primitives.Visual(PrimitiveType.Quad, "Fog puff");
            quad.transform.SetParent(transform, false);
            var r = quad.GetComponent<Renderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            made.puffs.Add(r);
        }
        return made;
    }

    void EnsureMaterial()
    {
        if (material != null) return;
        block = new MaterialPropertyBlock();
        texture = ProceduralTextures.SoftPuff(64);
        material = UrpMaterials.NewLit("Fog", Tint, 0f);
        UrpMaterials.MakeTransparent(material);
        material.SetTexture("_BaseMap", texture);
        // Both faces, so a puff the camera ends up behind still shows.
        material.SetFloat("_Cull", 0f);
    }

    void OnDestroy()
    {
        if (material != null) Destroy(material);
        if (texture != null) Destroy(texture);
    }
}
