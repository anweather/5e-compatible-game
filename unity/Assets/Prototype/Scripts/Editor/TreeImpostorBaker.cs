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
// public repo can't hold, and the menu makes it again. Nothing here is used at run time but the files it writes.
public static class TreeImpostorBaker
{
    /// <summary>Where the atlases and materials go: a Resources folder, so a player build carries them.</summary>
    public const string Folder = "Assets/Prototype/Generated/Resources/" + TreeImpostors.ResourceFolder;
    const string Trees = "Assets/Prototype/Generated/Trees";

    /// <summary>The trial's species: the nine's commonest (Conifer: 53,643 of its 105,951 trees, the Cypress the rest).</summary>
    public const string TrialSpecies = "Conifer";

    [MenuItem("Prototype/Bake Tree Impostor")]
    static void BakeTrial() => Debug.Log(Bake(TrialSpecies, 12, 2048));

    [MenuItem("Prototype/Bake Tree Impostors (every species, 12 x 12)")]
    static void BakeEvery()
    {
        foreach (var path in Directory.GetFiles(Trees, "*.prefab")) Debug.Log(Bake(Path.GetFileNameWithoutExtension(path), 12, 2048));
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
                var m = mats[s];
                if (m == null) continue;
                var props = new MaterialPropertyBlock();
                if (m.HasProperty("_MainTex") && m.GetTexture("_MainTex") != null) props.SetTexture("_MainTex", m.GetTexture("_MainTex"));
                if (m.HasProperty("_BumpMap") && m.GetTexture("_BumpMap") != null) props.SetTexture("_BumpMap", m.GetTexture("_BumpMap"));
                props.SetColor("_Color", m.HasProperty("_Color") ? m.GetColor("_Color") : Color.white);
                props.SetFloat("_Cutoff", m.HasProperty("_AlphaClipThreshold") ? m.GetFloat("_AlphaClipThreshold") : 0f);
                parts.Add(new Part { Mesh = filter.sharedMesh, Submesh = s, Matrix = root * r.transform.localToWorldMatrix, Props = props });
            }
        }
        return parts;
    }

    // The tree's bounding sphere: its box's centre, out to its farthest vertex.
    static (Vector3 centre, float radius) Bounds(List<Part> parts)
    {
        var box = new Bounds();
        bool first = true;
        var points = new List<Vector3>();
        foreach (var mesh in parts.Select(p => (p.Mesh, p.Matrix)).Distinct())
        {
            foreach (var v in mesh.Mesh.vertices)
            {
                var w = mesh.Matrix.MultiplyPoint3x4(v);
                points.Add(w);
                if (first) box = new Bounds(w, Vector3.zero);
                else box.Encapsulate(w);
                first = false;
            }
        }
        float r = points.Count == 0 ? 1f : points.Max(p => (p - box.center).magnitude);
        return (box.center, r * 1.02f);
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
            int k = queue.Dequeue(), x = k % size, y = k / size, tx = x / tile, ty = y / tile;
            for (int s = 0; s < 4; s++)
            {
                int qx = x + (s == 0 ? 1 : s == 1 ? -1 : 0), qy = y + (s == 2 ? 1 : s == 3 ? -1 : 0);
                if (qx < 0 || qy < 0 || qx >= size || qy >= size || qx / tile != tx || qy / tile != ty) continue;
                int q = qy * size + qx;
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
