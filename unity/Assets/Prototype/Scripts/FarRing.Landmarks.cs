using System.Collections.Generic;
using Chem;
using UnityEngine;

// The far ring's landmarks (docs/design/world-squares.md section 4 and its "On paper (2026-11-09)" notes): every landmark
// the plan places that shows from far off (GeneratedMap.Far, world/horizon.json) is a stand-in mesh of its shape and size,
// drawn while the camera is within its seenFrom radius. Its haze is its own (FarLand.shader): the land's within sight,
// then thinner, about 90% at its seenFrom, fading into the sky's colour over its last chunk and a half, so a volcano or
// the spire shows through the fog past the known ring without popping in. Over a known chunk it stands a little proud of
// the coarse land (which has a stamp's rise in it); where its chunk is drawn as a chunk terrain it gives way to what's
// really there (the stamp, the valley's own castle), at the same haze, since its haze is the land's inside sight. All
// view: nothing shared reads it.
public sealed partial class FarRing
{
    // A far mesh stands this much wider and taller than its row, so it covers its own stamp in a known chunk's coarse land.
    const float CoverRadius = 1.02f, CoverMetres = 1f;
    // How far below the ground at it a far mesh's foot reaches (m), so it never floats on a slope.
    const float SinkMetres = 8f;
    /// <summary>The haze a landmark has at its seenFrom (the paper's 90%).</summary>
    public const float HazeAtSeen = 0.9f;
    // Visibility is worked out a few times a second: the camera crosses a chunk in seconds.
    const float LandmarkEvery = 0.25f;

    readonly List<(FarLandmark landmark, GameObject go)> landmarks = new();
    float nextLandmarks;

    static readonly int SeenMetresId = Shader.PropertyToID("_SeenMetres"), HazeAtSeenId = Shader.PropertyToID("_HazeAtSeen"),
        FadeMetresId = Shader.PropertyToID("_FadeMetres");

    /// <summary>How many far landmarks are drawn now (the probe and the clip read it).</summary>
    public int LandmarksShown { get; private set; }

    /// <summary>Every far landmark: what it is, where its foot stands, and whether it's drawn now.</summary>
    public IEnumerable<(FarLandmark landmark, Vector3 foot, bool shown)> Landmarks
    {
        get
        {
            foreach (var (f, go) in landmarks) yield return (f, go.transform.position, go.activeSelf);
        }
    }

    /// <summary>Whether the far ring is up: the map read, the haze set, the sea plane and the landmarks made.</summary>
    public bool IsReady => ring != null;

    /// <summary>How many known chunks are drawn as meshes now.</summary>
    public int KnownDrawn => drawn.Count;

    void MakeLandmarks(Loaded loaded)
    {
        var l = land.Layout;
        for (int n = 0; n < loaded.Far.All.Count; n++)
        {
            var f = loaded.Far.All[n];
            var data = FarMeshes.Landmark(f.Type, f.Shape, f.Radius * CoverRadius, f.Height + CoverMetres, SinkMetres);
            var go = new GameObject($"Far {f.Type}{(f.Name != null ? " " + f.Name : "")}");
            go.transform.SetParent(transform, false);
            var (x, z) = l.CellCenter(f.X, f.Y);
            go.transform.position = new Vector3(x, loaded.Ground[n], z);
            go.AddComponent<MeshFilter>().sharedMesh = data.ToMesh(go.name);
            var r = go.AddComponent<MeshRenderer>();
            Quiet(r, landmarkMaterial);
            var block = new MaterialPropertyBlock();
            block.SetFloat(SeenMetresId, f.SeenFrom * chunkMetres);
            block.SetFloat(HazeAtSeenId, HazeAtSeen);
            block.SetFloat(FadeMetresId, 1.5f * chunkMetres);
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

    /// <summary>How far the camera sees on a generated map (m): the sea plane to the horizon, as the yard's builder means it to.</summary>
    public const float SeeMetres = 15000f;

    /// <summary>
    /// How far everything on the Default layer still draws (m), as the follow camera's old far plane of 500 m let it: the
    /// cells' dressing is a GameObject a cell over the whole map (32,000 brush spheres on the nine), which drawn to the
    /// horizon cost 45 ms a frame, and to 700 m still 25 ms. Past 500 m the haze is over 85%.
    /// </summary>
    public const float NearMetres = 500f;

    /// <summary>The layer the far ring draws on, so the Default layer's cull distance doesn't reach it ("Ignore Raycast":
    /// nothing here has a collider).</summary>
    public const int Layer = 2;

    // The player's follow camera comes from the Starter Assets with a far plane of 500 m, which cut the known ring and the
    // landmarks off; on a generated map the cameras (and Cinemachine's lenses, which override them) see to the horizon,
    // and the Default layer stops where the old plane did. Checked a few times a second, so
    // a camera made later (online, a cut) gets it too.
    static void SeeFar(Camera cam)
    {
        if (cam.farClipPlane >= SeeMetres) return;
        cam.farClipPlane = SeeMetres;
        var cull = new float[32];
        cull[0] = NearMetres;
        cam.layerCullDistances = cull;
        cam.layerCullSpherical = true;
        foreach (var v in FindObjectsByType<Unity.Cinemachine.CinemachineCamera>(FindObjectsSortMode.None))
        {
            var lens = v.Lens;
            if (lens.FarClipPlane >= SeeMetres) continue;
            lens.FarClipPlane = SeeMetres;
            v.Lens = lens;
        }
    }
}
