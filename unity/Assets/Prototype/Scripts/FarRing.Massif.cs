using System.Threading.Tasks;
using Chem;
using UnityEngine;

// The far ring's massif (docs/design/alpine.md, "Seeing it from far off"): a hills map's alpine massif drawn whole as one
// coarse mesh of its land (every MassifStep cells, where the massif rises at all; rock high on the ring), with a far landmark's own thin haze
// (FarLand.shader), so its mountains stand on the horizon from anywhere on the map, past the known ring where no other
// land is drawn. Inside the known ring the chunk terrains and known meshes are the land: the mesh isn't drawn nearer
// than the known ring's edge (_NearClip) and stands a little under the land, so it never shows through them. On a torus
// it stands at the copy nearest the camera. All view: nothing shared reads it.
public sealed partial class FarRing
{
    // The mesh's spacing in cells, how far under the land it stands (m), and where the massif counts as risen (land units);
    // it's bare rock from RockFrom land units over the meadow, all rock by RockAll.
    const int MassifStep = 16, MassifRises = 120;
    const float MassifSink = 3f;
    const int RockFrom = 250, RockAll = 900;
    // Seen from: the far side of the massif from the farthest point on the map, about; 90% haze there.
    const float MassifSeenMetres = 12000f;

    static readonly int NearClipId = Shader.PropertyToID("_NearClip");

    Task<FarMeshes.Data> massifBuild;
    GameObject massif;
    Vector2 massifCorner;
    float massifHalf;
    Material massifMaterial;

    /// <summary>Whether a hills map's massif is drawn far off now (the probe and the stills read it).</summary>
    public bool MassifShown => massif != null && massif.activeSelf;

    // Starts the massif's mesh on a worker, once the palette is known.
    void StartMassif(GeneratedMap m)
    {
        var hills = m.Hills;
        if (hills?.Massif == null) return;
        var colours = palette;
        float cell = land.Layout.CellSize;
        massifBuild = Task.Run(() => MassifMesh(hills, cell, colours));
        var mm = hills.Massif;
        int r = mm.Reach + mm.Warp;
        // The mesh's north-west corner as a map cell's corner: the line between cells, as Corner() takes a chunk's.
        int x0 = mm.CentreX - r, y0 = mm.CentreY - r;
        var l = land.Layout;
        massifCorner = new Vector2(l.OriginX + (x0 + l.U0) * l.CellSize, l.OriginZ + (l.V0 + 1 - y0) * l.CellSize);
        massifHalf = r * l.CellSize;
    }

    static FarMeshes.Data MassifMesh(HillsLand hills, float cell, FarMeshes.Palette colours)
    {
        var mm = hills.Massif;
        int r = mm.Reach + mm.Warp, n = 2 * r / MassifStep + 1;
        int x0 = mm.CentreX - r, y0 = mm.CentreY - r;
        float unit = (float)MapFile.LandUnitMetres, step = MassifStep * cell;
        var h = new float[n * n];
        var risen = new bool[n * n];
        var high = new float[n * n];
        for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
            {
                int x = x0 + i * MassifStep, y = y0 + j * MassifStep;
                h[j * n + i] = hills.At(x, y) * unit - MassifSink;
                int rise = mm.At(x, y);
                risen[j * n + i] = rise >= MassifRises;
                high[j * n + i] = Mathf.InverseLerp(mm.MeadowHeight + RockFrom, mm.MeadowHeight + RockAll, rise);
            }
        var d = new FarMeshes.Data();
        var index = new int[n * n];
        float H(int i, int j) => h[Mathf.Clamp(j, 0, n - 1) * n + Mathf.Clamp(i, 0, n - 1)];
        for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
            {
                index[j * n + i] = -1;
                bool near = false;
                for (int dj = -1; dj <= 1 && !near; dj++)
                    for (int di = -1; di <= 1 && !near; di++)
                    {
                        int a = i + di, b = j + dj;
                        near = a >= 0 && b >= 0 && a < n && b < n && risen[b * n + a];
                    }
                if (!near) continue;
                float dx = (H(i + 1, j) - H(i - 1, j)) / (2 * step), dz = (H(i, j - 1) - H(i, j + 1)) / (2 * step);
                var normal = new Vector3(-dx, 1f, -dz).normalized;
                // Rock where it's steep, as the known meshes paint it, and high on the ring, where the cells are rock and scree.
                var c = Color.Lerp(colours.Grass, colours.Rock, Mathf.Max(Mathf.InverseLerp(0.85f, 0.6f, normal.y), high[j * n + i]));
                index[j * n + i] = d.Vertex(new Vector3(i * step, H(i, j), -j * step), normal, c);
            }
        for (int j = 0; j < n - 1; j++)
            for (int i = 0; i < n - 1; i++)
            {
                int a = index[j * n + i], b = index[j * n + i + 1], c = index[(j + 1) * n + i], e = index[(j + 1) * n + i + 1];
                if (a >= 0 && b >= 0 && c >= 0 && e >= 0) d.Quad(a, b, c, e);
            }
        return d;
    }

    // The worker's mesh to Unity when it's done; then, a few times a second, at the copy nearest the camera.
    void UpdateMassif()
    {
        if (massifBuild != null && massifBuild.IsCompleted)
        {
            var job = massifBuild;
            massifBuild = null;
            if (job.IsFaulted)
            {
                Debug.LogError($"FAR RING: the massif's mesh failed: {job.Exception?.GetBaseException().Message}");
                return;
            }
            massif = new GameObject("Far massif");
            massif.transform.SetParent(transform, false);
            var mesh = job.Result.ToMesh(massif.name);
            massif.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = massif.AddComponent<MeshRenderer>();
            massifMaterial = new Material(landmarkMaterial) { name = "Far massif" };
            Quiet(r, massifMaterial);
            var block = new MaterialPropertyBlock();
            block.SetFloat(SeenMetresId, MassifSeenMetres);
            block.SetFloat(HazeAtSeenId, HazeAtSeen);
            block.SetFloat(FadeMetresId, 1.5f * chunkMetres);
            block.SetFloat(StretchId, 1f);
            block.SetFloat(NotchId, 0f);
            block.SetFloat(TopId, mesh.bounds.max.y);
            r.SetPropertyBlock(block);
            Debug.Log($"FAR RING: the massif drawn far off, {job.Result.Vertices.Count} vertices");
        }
        if (massif == null) return;
        // Not nearer than the known ring's edge, where the land is the terrains' and the known meshes'.
        massifMaterial.SetFloat(NearClipId, HazeMetres);
        float half = massifHalf;
        var to = new Vector3(ViewWrap.NearX(massifCorner.x + half) - half, 0f, ViewWrap.NearZ(massifCorner.y - half) + half);
        if ((massif.transform.position - to).sqrMagnitude > 1f) massif.transform.position = to;
    }
}
