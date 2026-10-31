using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

// Part of PerfStage's pso scenario: does URP's Particles/Lit shader draw on this
// GPU? Metal logs "Error creating pipeline state" for 6 of its states as the
// shipped collection warms up, so this lists what the collection holds for the
// shader and then tries every material on it that the scene has loaded, one at
// a time, as a quad and as a particle: it logs a "PERF PSO" line with the pixels
// each changed against the frame before, and any pipeline error that came while
// it was on show. Run it with -no-pso-warmup too, so an error has to come from a
// real draw.
public sealed class PerfPsoCheck
{
    const int Quiet = 3, Differs = 24;
    const float Size = 6f, Before = 8f;

    readonly PerfStage stage;
    readonly List<string> errors = new();

    public PerfPsoCheck(PerfStage stage) => this.stage = stage;

    public IEnumerator Run()
    {
        Application.logMessageReceived += OnLog;
        ListCollection();
        var camera = Camera.main;
        var eye = new Vector3(40f, 30f, 1f);
        stage.CutTo(eye, eye + new Vector3(0.3f, -1f, 0.3f));
        yield return Frames(Quiet);
        int noise = 0;
        var still = Grab();
        yield return Frames(Quiet);
        noise = Changed(still, Grab());
        Debug.Log($"PERF PSO noise pixels={noise}");
        var mats = Resources.FindObjectsOfTypeAll<Material>().Where(m => m.shader != null && m.shader.name == UrpMaterials.VertexLitShader).OrderBy(m => m.name).ToList();
        Debug.Log($"PERF PSO materials={mats.Count}");
        foreach (var material in mats)
        {
            yield return Try(material, "quad", Quad(material, camera));
            yield return Try(material, "particle", Particles(material, camera));
        }
        Application.logMessageReceived -= OnLog;
    }

    static void ListCollection()
    {
        var collection = Resources.Load<GraphicsStateCollection>("Pso" + SystemInfo.graphicsDeviceType);
        if (collection == null)
        {
            Debug.Log("PERF PSO no collection");
            return;
        }
        var variants = new List<GraphicsStateCollection.ShaderVariant>();
        collection.GetVariants(variants);
        var states = new List<GraphicsStateCollection.GraphicsState>();
        foreach (var v in variants.Where(v => v.shader != null && v.shader.name == UrpMaterials.VertexLitShader))
        {
            collection.GetGraphicsStatesForVariant(v.shader, v.passId, v.keywords, states);
            Debug.Log($"PERF PSO variant pass={v.passId.PassIndex}/{v.passId.SubshaderIndex} states={states.Count} keywords={string.Join(",", v.keywords.Select(k => k.name))}");
        }
    }

    IEnumerator Try(Material material, string how, GameObject shown)
    {
        errors.Clear();
        shown.SetActive(false);
        yield return Frames(Quiet);
        var without = Grab();
        shown.SetActive(true);
        yield return Frames(Quiet);
        int changed = Changed(without, Grab());
        Object.Destroy(shown);
        string keywords = string.Join(",", material.enabledKeywords.Select(k => k.name));
        Debug.Log($"PERF PSO material=\"{material.name}\" as={how} pixels={changed} pipeline_errors={errors.Count} keywords={keywords} queue={material.renderQueue}");
    }

    static IEnumerator Frames(int n)
    {
        for (int i = 0; i < n; i++) yield return null;
        yield return new WaitForEndOfFrame();
    }

    static GameObject Quad(Material material, Camera camera)
    {
        var go = new GameObject("Pso quad", typeof(MeshFilter), typeof(MeshRenderer));
        var mesh = new Mesh();
        mesh.SetVertices(new List<Vector3> { new(-1f, -1f, 0f), new(1f, -1f, 0f), new(-1f, 1f, 0f), new(1f, 1f, 0f) });
        mesh.SetUVs(0, new List<Vector2> { Vector2.zero, Vector2.right, Vector2.up, Vector2.one });
        mesh.SetColors(Enumerable.Repeat(Color.white, 4).ToList());
        mesh.SetTriangles(new[] { 0, 2, 1, 2, 3, 1 }, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();
        go.GetComponent<MeshFilter>().sharedMesh = mesh;
        go.GetComponent<MeshRenderer>().sharedMaterial = material;
        Face(go.transform, camera);
        go.transform.localScale = Vector3.one * Size;
        return go;
    }

    static GameObject Particles(Material material, Camera camera)
    {
        var go = new GameObject("Pso particles");
        Face(go.transform, camera);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.startSpeed = 0f;
        main.startSize = Size;
        main.startLifetime = 60f;
        go.GetComponent<ParticleSystemRenderer>().sharedMaterial = material;
        ps.Emit(4);
        return go;
    }

    static void Face(Transform t, Camera camera)
    {
        var c = camera.transform;
        t.SetPositionAndRotation(c.position + c.forward * Before, c.rotation);
    }

    static Color32[] Grab()
    {
        var shot = ScreenCapture.CaptureScreenshotAsTexture();
        var pixels = shot.GetPixels32();
        Object.Destroy(shot);
        return pixels;
    }

    static int Changed(Color32[] a, Color32[] b)
    {
        int n = 0;
        for (int i = 0; i < a.Length; i++)
            if (Mathf.Abs(a[i].r - b[i].r) + Mathf.Abs(a[i].g - b[i].g) + Mathf.Abs(a[i].b - b[i].b) > Differs) n++;
        return n;
    }

    void OnLog(string message, string stack, LogType type)
    {
        if (message.Contains("pipeline state")) errors.Add(message);
    }
}
