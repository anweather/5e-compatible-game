using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

// Bakes a tree species into an octahedral impostor (docs/design/tree-impostors.md, J-090): the tree's nearest LOD drawn
// from N x N directions over the upper hemisphere (TreeImpostorMath), orthographic and unlit, into two atlases (albedo
// and coverage; the tree-space normal and the depth from each view's plane), and a material on Prototype/TreeImpostor
// that TreeImpostors draws with. Everything it writes is under Generated/ (ignored): it is made from pack trees, which a
// public repo can't hold, and the menu makes it again. Nothing here is used at run time but the files it writes. Every
// species in Generated/Trees is baked when its impostor is missing or was baked by an older baker (BakeVersion): when the
// Editor starts, after a yard build, and before a player build, as the yard itself is made when it's missing.
[InitializeOnLoad]
public static class TreeImpostorBaker
{
    /// <summary>The baker's version, kept on each material (_BakeVersion): a species baked by an older one is baked again.</summary>
    public const int BakeVersion = 3;

    const int Frames = 12, AtlasPixels = 2048;

    static TreeImpostorBaker()
    {
        EditorApplication.update -= WhenFree;
        EditorApplication.update += WhenFree;
    }

    // Once, on the first Editor update that isn't importing, compiling or playing.
    static void WhenFree()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        EditorApplication.update -= WhenFree;
        if (Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode) return;
        BakeMissing();
    }

    /// <summary>The species in Generated/Trees (as Build Chemistry Yard repairs them), by name.</summary>
    public static string[] Species() =>
        Directory.Exists(Trees) ? Directory.GetFiles(Trees, "*.prefab").Select(Path.GetFileNameWithoutExtension).OrderBy(n => n, System.StringComparer.Ordinal).ToArray() : new string[0];

    /// <summary>Whether a species' impostor is there and was baked by this baker.</summary>
    public static bool IsBaked(string species)
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>($"{Folder}/{species}.mat");
        return mat != null && mat.HasProperty(BakeVersionId) && Mathf.RoundToInt(mat.GetFloat(BakeVersionId)) == BakeVersion
            && mat.GetTexture("_AlbedoAtlas") != null && mat.GetTexture("_NormalAtlas") != null;
    }

    /// <summary>Bakes every species whose impostor is missing or old; returns how many it baked.</summary>
    public static int BakeMissing()
    {
        int baked = 0;
        foreach (var species in Species())
        {
            if (IsBaked(species)) continue;
            Debug.Log(Bake(species, Frames, AtlasPixels));
            baked++;
        }
        return baked;
    }

    const string BakeVersionId = "_BakeVersion";

    // Each species' impostor against its own mesh at the 60 m seam, measured (tools/probe.sh treeimpostors on the nine,
    // the pixels that differ between a pair at 50 and 120 m, from the ground and from above, at 8 and at noon): with
    // SpeedTree's occlusion baked in, the Conifer's impostor was 6.4% redder, 5.8% greener and as blue (a little bright and
    // yellow), the Cypress's 3.6% and 3.5%. The tint takes that back out. A species not measured keeps white.
    static readonly Dictionary<string, Color> Tints = new()
    {
        ["Conifer"] = new Color(1f / 1.064f, 1f / 1.058f, 1f / 0.998f, 1f),
        ["Cypress"] = new Color(1f / 1.036f, 1f / 1.035f, 1f / 0.999f, 1f),
    };
    /// <summary>Where the atlases and materials go: a Resources folder, so a player build carries them.</summary>
    public const string Folder = "Assets/Prototype/Generated/Resources/" + TreeImpostors.ResourceFolder;
    const string Trees = "Assets/Prototype/Generated/Trees";

    [MenuItem("Prototype/Bake Tree Impostors (every species, 12 x 12)")]
    static void BakeEvery()
    {
        foreach (var species in Species()) Debug.Log(Bake(species, Frames, AtlasPixels));
    }

    // One part of the tree to draw: a submesh, where it stands in the tree, and its textures.
    struct Part
    {
        public Mesh Mesh;
        public int Submesh;
        public Matrix4x4 Matrix;
        public MaterialPropertyBlock Props;
    }

    /// <summary>
    /// Bakes species (a prefab in Generated/Trees, as Build Chemistry Yard repairs it) at frames x frames views into
    /// atlas-pixel atlases; returns a line saying what it made, how long it took and what the atlases hold in memory.
    /// </summary>
    public static string Bake(string species, int frames, int atlas)
    {
        var watch = Stopwatch.StartNew();
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{Trees}/{species}.prefab");
        if (prefab == null) return $"TREE IMPOSTOR: no {Trees}/{species}.prefab (run Prototype > Build Chemistry Yard)";
        var parts = PartsOf(prefab);
        var (centre, radius) = Bounds(parts);
        int tile = atlas / frames;

        var bake = new Material(Shader.Find("Hidden/Prototype/TreeImpostorBake"));
        var albedoRT = new RenderTexture(atlas, atlas, GraphicsFormat.R8G8B8A8_SRGB, GraphicsFormat.None);
        var normalRT = new RenderTexture(atlas, atlas, GraphicsFormat.R8G8B8A8_UNorm, GraphicsFormat.None);
        var depthRT = new RenderTexture(atlas, atlas, GraphicsFormat.None, GraphicsFormat.D32_SFloat);
        albedoRT.Create();
        normalRT.Create();
        depthRT.Create();

        var cmd = new CommandBuffer { name = "Bake tree impostor" };
        cmd.SetRenderTarget(new RenderTargetIdentifier[] { albedoRT, normalRT }, depthRT);
        cmd.ClearRenderTarget(true, true, Color.clear);
        for (int j = 0; j < frames; j++)
        {
            for (int i = 0; i < frames; i++)
            {
                var d = TreeImpostorMath.FrameDir(i, j, frames);
                var eye = centre + d * (radius * 3f);
                var turn = Quaternion.LookRotation(-d, TreeImpostorMath.Up(d));
                var view = Matrix4x4.Scale(new Vector3(1f, 1f, -1f)) * Matrix4x4.TRS(eye, turn, Vector3.one).inverse;
                var proj = Matrix4x4.Ortho(-radius, radius, -radius, radius, radius * 0.5f, radius * 5.5f);
                cmd.SetViewport(new Rect(i * tile, j * tile, tile, tile));
                cmd.SetViewProjectionMatrices(view, proj);
                cmd.SetGlobalVector("_BakeCentre", new Vector4(centre.x, centre.y, centre.z, radius));
                cmd.SetGlobalVector("_BakeDir", d);
                foreach (var p in parts) cmd.DrawMesh(p.Mesh, p.Matrix, bake, p.Submesh, 0, p.Props);
            }
        }
        Graphics.ExecuteCommandBuffer(cmd);
        cmd.Release();

        var albedo = Read(albedoRT, false);
        var normal = Read(normalRT, true);
        albedoRT.Release();
        normalRT.Release();
        depthRT.Release();
        Object.DestroyImmediate(bake);
        Dilate(albedo, normal, atlas, tile);

        Directory.CreateDirectory(Folder);
        string albedoPath = $"{Folder}/{species}_Albedo.png", normalPath = $"{Folder}/{species}_Normal.png";
        File.WriteAllBytes(albedoPath, albedo.EncodeToPNG());
        File.WriteAllBytes(normalPath, normal.EncodeToPNG());
        Object.DestroyImmediate(albedo);
        Object.DestroyImmediate(normal);
        AssetDatabase.ImportAsset(albedoPath);
        AssetDatabase.ImportAsset(normalPath);
        Import(albedoPath, true);
        Import(normalPath, false);

        var mat = EditorAssets.LoadOrCreate($"{Folder}/{species}.mat", () => new Material(Shader.Find("Prototype/TreeImpostor")));
        mat.shader = Shader.Find("Prototype/TreeImpostor");
        var albedoTex = AssetDatabase.LoadAssetAtPath<Texture2D>(albedoPath);
        var normalTex = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);
        mat.SetTexture("_AlbedoAtlas", albedoTex);
        mat.SetTexture("_NormalAtlas", normalTex);
        mat.SetFloat("_Frames", frames);
        mat.SetFloat("_FrameSize", tile / (float)atlas);
        mat.SetFloat("_Radius", radius);
        mat.SetVector("_Centre", centre);
        mat.SetFloat("_Cutoff", 0.5f);
        mat.SetFloat("_Wrap", 0.5f);
        mat.SetFloat("_Translucency", 0.5f);
        mat.SetFloat("_ShadowStrength", 0.7f);
        mat.SetFloat("_Debug", 0f);
        mat.SetFloat(BakeVersionId, BakeVersion);
        mat.SetColor("_Tint", Tints.TryGetValue(species, out var tint) ? tint : Color.white);
        mat.enableInstancing = true;
        EditorUtility.SetDirty(mat);
        AssetDatabase.SaveAssets();

        long bytes = UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(albedoTex) + UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(normalTex);
        return $"TREE IMPOSTOR: {species} {frames}x{frames} views, {atlas} px atlases ({tile} px a view), radius {radius:F1} m, " +
               $"centre {centre}, {parts.Count} parts; baked in {watch.ElapsedMilliseconds} ms; atlases {bytes / (1024f * 1024f):F1} MB in memory";
    }

    // The tree's nearest LOD (all of it, without a LOD group), each submesh with its material's textures.
    static List<Part> PartsOf(GameObject prefab)
    {
        var lod = prefab.GetComponent<LODGroup>();
        var renderers = lod != null ? lod.GetLODs()[0].renderers : prefab.GetComponentsInChildren<Renderer>();
        var root = prefab.transform.worldToLocalMatrix;
        var parts = new List<Part>();
        foreach (var r in renderers.Where(r => r != null))
        {
            if (!r.TryGetComponent<MeshFilter>(out var filter) || filter.sharedMesh == null) continue;
            var mats = r.sharedMaterials;
            for (int s = 0; s < filter.sharedMesh.subMeshCount && s < mats.Length; s++)
            {
                if (mats[s] == null) continue;
                parts.Add(new Part { Mesh = filter.sharedMesh, Submesh = s, Matrix = root * r.transform.localToWorldMatrix, Props = PropsOf(mats[s]) });
            }
        }
        return parts;
    }

    // A tree material's textures, colour and alpha clip, as the bake shader takes them.
    static MaterialPropertyBlock PropsOf(Material m)
    {
        var props = new MaterialPropertyBlock();
        foreach (var name in new[] { "_MainTex", "_BumpMap" })
            if (m.HasProperty(name) && m.GetTexture(name) != null) props.SetTexture(name, m.GetTexture(name));
        props.SetColor("_Color", m.HasProperty("_Color") ? m.GetColor("_Color") : Color.white);
        props.SetFloat("_Cutoff", m.HasProperty("_AlphaClipThreshold") ? m.GetFloat("_AlphaClipThreshold") : 0f);
        return props;
    }

    // The tree's bounding sphere: its box's centre, out to its farthest vertex.
    static (Vector3 centre, float radius) Bounds(List<Part> parts)
    {
        var points = parts.Select(p => (p.Mesh, p.Matrix)).Distinct()
            .SelectMany(m => m.Mesh.vertices.Select(v => m.Matrix.MultiplyPoint3x4(v))).ToList();
        if (points.Count == 0) return (Vector3.up, 1f);
        var box = new Bounds(points[0], Vector3.zero);
        foreach (var w in points) box.Encapsulate(w);
        return (box.center, points.Max(p => (p - box.center).magnitude) * 1.02f);
    }

    static Texture2D Read(RenderTexture rt, bool linear)
    {
        var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false, linear);
        var was = RenderTexture.active;
        RenderTexture.active = rt;
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        tex.Apply();
        RenderTexture.active = was;
        return tex;
    }

    // Spreads each view's colours and normals into the empty pixels round them, within the view, nearest first, so the
    // filtering and the mips at the tree's edge blend toward the tree's own colour, not the clear black. Coverage (the
    // albedo's alpha) stays as baked.
    static void Dilate(Texture2D albedo, Texture2D normal, int size, int tile)
    {
        var a = albedo.GetPixels32();
        var n = normal.GetPixels32();
        var done = new bool[a.Length];
        var queue = new Queue<int>();
        for (int k = 0; k < a.Length; k++)
        {
            if (a[k].a < 8) continue;
            done[k] = true;
            queue.Enqueue(k);
        }
        while (queue.Count > 0)
        {
            int k = queue.Dequeue();
            foreach (int q in Neighbours(k, size, tile))
            {
                if (done[q]) continue;
                done[q] = true;
                a[q] = new Color32(a[k].r, a[k].g, a[k].b, 0);
                n[q] = n[k];
                queue.Enqueue(q);
            }
        }
        albedo.SetPixels32(a);
        normal.SetPixels32(n);
        albedo.Apply();
        normal.Apply();
    }

    static readonly (int x, int y)[] Steps = { (1, 0), (-1, 0), (0, 1), (0, -1) };

    // The pixels beside pixel k that are in the same view.
    static IEnumerable<int> Neighbours(int k, int size, int tile)
    {
        int x = k % size, y = k / size;
        foreach (var (sx, sy) in Steps)
        {
            int qx = x + sx, qy = y + sy;
            if (qx >= 0 && qy >= 0 && qx < size && qy < size && qx / tile == x / tile && qy / tile == y / tile) yield return qy * size + qx;
        }
    }

    static void Import(string path, bool srgb)
    {
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Default;
        importer.sRGBTexture = srgb;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.alphaIsTransparency = false;
        importer.mipmapEnabled = true;
        importer.mipMapsPreserveCoverage = srgb;
        importer.alphaTestReferenceValue = 0.5f;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.filterMode = FilterMode.Bilinear;
        importer.maxTextureSize = 4096;
        importer.textureCompression = TextureImporterCompression.CompressedHQ;
        importer.SaveAndReimport();
    }
}
