using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// The sea south-east of the valley (docs/WORLD_LIFE.md): one mesh at sea level
// from a little inland of the coast out to the horizon, finely spaced along
// the beach and coarser far out, drawn with the Ocean shader. Each vertex
// carries the water's depth there, sampled from the terrain when the mesh is
// built (and a guess past the terrain's edge), so the shader can grow the
// swell over the shoals, break it into foam and wash it up the sand. Built
// when the scene loads, in the Editor too; only its settings are saved.
// Looks only: the sea's cells are the lake's kind of water (ValleyBuilder),
// and the currents that push swimmers live in WaterCurrents.
[ExecuteAlways]
public class Ocean : MonoBehaviour
{
    public Material material;
    [Tooltip("The sea's surface height (the lake's, so the cells' water is one level).")]
    public float level = -0.6f;
    [Tooltip("A point on the coastline (x, z).")]
    public Vector2 shoreOrigin;
    [Tooltip("Out to sea, square to the coast (a direction on the ground plane).")]
    public Vector2 seaward = new(0.707f, -0.707f);
    [Tooltip("How far along the coast the sea reaches each way, how far out, and how far inland the mesh starts (m).")]
    public float alongHalf = 12000f, outFar = 12000f, inland = 45f;
    [Tooltip("Spacing over the beaches and the near sea (m), and how far each way it holds.")]
    public float fineStep = 2.5f, fineAlong = 430f, fineOut = 330f;

    Mesh mesh;
    GameObject surface;

    void OnEnable() => Build();

    void OnDisable()
    {
        if (surface != null) DestroyNow(surface);
        if (mesh != null) DestroyNow(mesh);
    }

    /// <summary>Builds the sea's mesh again (after the terrain under it changed shape).</summary>
    public void Build()
    {
        OnDisable();
        var out_ = seaward.normalized;
        var along = Vector2.Perpendicular(out_);
        var us = Stops(-alongHalf, alongHalf, -fineAlong, fineAlong);
        var vs = Stops(-inland, outFar, -inland, fineOut);
        var positions = new Vector3[us.Count * vs.Count];
        var water = new Vector2[positions.Length];
        int n = 0;
        foreach (float v in vs)
            foreach (float u in us)
            {
                var xz = shoreOrigin + along * u + out_ * v;
                positions[n] = new Vector3(xz.x, level, xz.y) - transform.position;
                water[n] = new Vector2(Depth(xz, v), v);
                n++;
            }
        var triangles = new int[(us.Count - 1) * (vs.Count - 1) * 6];
        int t = 0;
        for (int j = 0; j + 1 < vs.Count; j++)
            for (int i = 0; i + 1 < us.Count; i++)
            {
                int a = j * us.Count + i, b = a + 1, c = a + us.Count, d = c + 1;
                triangles[t++] = a;
                triangles[t++] = b;
                triangles[t++] = c;
                triangles[t++] = b;
                triangles[t++] = d;
                triangles[t++] = c;
            }
        mesh = new Mesh { name = "Ocean", indexFormat = IndexFormat.UInt32, hideFlags = HideFlags.DontSave };
        mesh.vertices = positions;
        mesh.uv2 = water;
        mesh.triangles = triangles;
        var up = new Vector3[positions.Length];
        System.Array.Fill(up, Vector3.up);
        mesh.normals = up;
        // The swell lifts the surface: keep it from being culled at the edges of the view.
        var bounds = mesh.bounds;
        bounds.Expand(new Vector3(4f, 4f, 4f));
        mesh.bounds = bounds;

        surface = new GameObject("Sea surface") { hideFlags = HideFlags.DontSave | HideFlags.NotEditable };
        surface.transform.SetParent(transform, false);
        surface.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = surface.AddComponent<MeshRenderer>();
        r.sharedMaterial = material;
        r.shadowCastingMode = ShadowCastingMode.Off;
    }

    // The water's depth at a point: the sea level less the terrain there; past the terrain, a bed
    // shelving away from the coast.
    float Depth(Vector2 xz, float outward)
    {
        var p = new Vector3(xz.x, 0f, xz.y);
        foreach (var terrain in Terrain.activeTerrains)
        {
            var size = terrain.terrainData.size;
            var local = p - terrain.transform.position;
            if (local.x < 0f || local.z < 0f || local.x > size.x || local.z > size.z) continue;
            return level - (terrain.SampleHeight(p) + terrain.transform.position.y);
        }
        return Mathf.Clamp(3f + outward * 0.12f, 3f, 60f);
    }

    // Coordinates from lo to hi: fineStep apart between fineLo and fineHi, growing by a sixth each
    // step beyond them.
    List<float> Stops(float lo, float hi, float fineLo, float fineHi)
    {
        var stops = new List<float>();
        float x = Mathf.Max(lo, fineLo);
        var below = new List<float>();
        for (float step = fineStep, y = x - step; y > lo; step *= 1.17f, y -= step) below.Add(y);
        below.Add(lo);
        for (int i = below.Count - 1; i >= 0; i--) stops.Add(below[i]);
        for (; x < Mathf.Min(hi, fineHi); x += fineStep) stops.Add(x);
        for (float step = fineStep; x < hi; step *= 1.17f, x += step) stops.Add(x);
        stops.Add(hi);
        // No two the same (where a fine run starts at an end).
        stops.Sort();
        var distinct = new List<float> { stops[0] };
        foreach (float s in stops)
            if (s > distinct[distinct.Count - 1] + 0.01f) distinct.Add(s);
        return distinct;
    }

    static void DestroyNow(Object o)
    {
        if (Application.isPlaying) Destroy(o);
        else DestroyImmediate(o);
    }
}
