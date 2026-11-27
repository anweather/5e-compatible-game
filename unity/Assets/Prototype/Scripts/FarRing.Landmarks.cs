using System.Collections.Generic;
using Chem;
using UnityEngine;

// The far ring's landmarks (docs/design/world-squares.md section 4 and its "On paper (2026-11-09)" notes): every landmark
// the plan places that shows from far off (GeneratedMap.Far, world/horizon.json) is a stand-in mesh of its shape and size,
// drawn while the camera is within its seenFrom radius. It's in the one air (FarLand.shader, docs/design/hills.md "One
// air"), fading wholly into it over its last chunk and a half, so it never pops out. Over a known chunk it stands a little
// proud of the coarse land (which has a stamp's rise in it); where its chunk is drawn as a chunk terrain it gives way to
// what's really there (the stamp, the valley's own castle), in the same air. Past
// where it can give way, its horizon.json "far" look comes in (FarLand.shader): the volcano stands taller, its rim shows
// the eye a notch, and an apron of land runs from its foot down to the water, since no land is drawn past the known ring;
// so it's its true size wherever it meets the land. All view: nothing shared reads it.
public sealed partial class FarRing
{
    const float CoverRadius = 1.02f, CoverMetres = 1f;
    const float SinkMetres = 8f;
    const float LandmarkEvery = 0.25f;

    readonly List<(FarLandmark landmark, GameObject go)> landmarks = new();
    float nextLandmarks;

    static readonly int SeenMetresId = Shader.PropertyToID("_SeenMetres"), FadeMetresId = Shader.PropertyToID("_FadeMetres"), StretchId = Shader.PropertyToID("_Stretch"), NotchId = Shader.PropertyToID("_Notch"),
        TopId = Shader.PropertyToID("_Top"), StretchFromId = Shader.PropertyToID("_FarStretchFrom"), StretchToId = Shader.PropertyToID("_FarStretchTo");

    float StretchFrom => (sight + 1) * chunkMetres * 1.4142f;

    public const float StretchOverChunks = 3f;

    public int LandmarksShown { get; private set; }

    public IEnumerable<(FarLandmark landmark, Vector3 foot, bool shown)> Landmarks
    {
        get
        {
            foreach (var (f, go) in landmarks) yield return (f, go.transform.position, go.activeSelf);
        }
    }

    public bool IsReady => ring != null;

    public int KnownDrawn => drawn.Count;

    void MakeLandmarks(Loaded loaded)
    {
        var l = land.Layout;
        for (int n = 0; n < loaded.Far.All.Count; n++)
        {
            var f = loaded.Far.All[n];
            var look = loaded.Looks.TryGetValue(f.Type, out var lk) ? lk : FarMeshes.FarLook.Plain;
            float top = f.Height + CoverMetres, ground = loaded.Ground[n];
            var data = FarMeshes.Landmark(f.Type, f.Shape, f.Radius * CoverRadius, top, SinkMetres, look.Apron, ground - land.WaterLevel + 1f);
            var go = new GameObject($"Far {f.Type}{(f.Name != null ? " " + f.Name : "")}");
            go.transform.SetParent(transform, false);
            var (x, z) = l.CellCenter(f.X, f.Y);
            go.transform.position = new Vector3(x, ground, z);
            var mesh = data.ToMesh(go.name);
            var b = mesh.bounds;
            b.Encapsulate(new Vector3(b.center.x, top * look.Stretch, b.center.z));
            mesh.bounds = b;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            Quiet(r, landmarkMaterial);
            var block = new MaterialPropertyBlock();
            block.SetFloat(SeenMetresId, f.SeenFrom * chunkMetres);
            block.SetFloat(FadeMetresId, 1.5f * chunkMetres);
            block.SetFloat(StretchId, look.Stretch);
            block.SetFloat(NotchId, look.Notch);
            block.SetFloat(TopId, top);
            r.SetPropertyBlock(block);
            go.SetActive(false);
            landmarks.Add((f, go));
        }
    }

    void UpdateLandmarks()
    {
        var cam = Camera.main;
        if (cam == null || Time.unscaledTime < nextLandmarks) return;
        nextLandmarks = Time.unscaledTime + LandmarkEvery;
        SeeFar(cam);
        var p = cam.transform.position;
        var (x, y) = land.Layout.CellAt(p.x, p.z);
        int shown = 0;
        foreach (var (f, go) in landmarks)
        {
            bool show = f.SeenAt(x, y) && !LandIsTerrain(f.X / ChunkGen.Size, f.Y / ChunkGen.Size);
            if (go.activeSelf != show) go.SetActive(show);
            if (show) shown++;
        }
        LandmarksShown = shown;
    }

    public const float SeeMetres = 15000f;

    public const float NearMetres = 500f;

    public const int Layer = 2;

    void SeeFar(Camera cam)
    {
        float cut = HazeMetres > 0f ? HazeHides : NearMetres;
        if (cam.farClipPlane >= SeeMetres && !cam.layerCullSpherical && Mathf.Approximately(cam.layerCullDistances[0], cut)) return;
        cam.farClipPlane = SeeMetres;
        var cull = new float[32];
        cull[0] = cut;
        cam.layerCullDistances = cull;
        cam.layerCullSpherical = false;
        foreach (var v in FindObjectsByType<Unity.Cinemachine.CinemachineCamera>(FindObjectsSortMode.None))
        {
            var lens = v.Lens;
            if (lens.FarClipPlane >= SeeMetres) continue;
            lens.FarClipPlane = SeeMetres;
            v.Lens = lens;
        }
    }
}
