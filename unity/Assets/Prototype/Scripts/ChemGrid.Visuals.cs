using System.Collections.Generic;
using Chem;
using UnityEngine;
using UnityEngine.Rendering;

// How the grid looks: ground blocks with a little colour variation, grass
// tufts, trees and bramble bushes, see-through water and glossy ice,
// covers, and the Particle Pack's flames, smoke, steam and sparks. Cubes
// stand in for any effect prefab that isn't assigned (for example before
// the Asset Store packs are restored). A small pool of flickering lights
// follows the biggest fires.
public partial class ChemGrid
{
    [Header("Effects (optional; cubes stand in when missing)")]
    public GameObject fireSmall;
    public GameObject fireMedium;
    public GameObject fireLarge;
    public GameObject smokeFx;
    public GameObject steamFx;
    public GameObject sparksFx;
    public int maxFireLights = 16;

    static readonly Dictionary<string, (Color color, float height)> TerrainLook = new()
    {
        ["bedrock"] = (new Color(0.24f, 0.23f, 0.25f), 1.5f), ["stone"] = (new Color(0.50f, 0.50f, 0.52f), 1.2f),
        ["pillar"] = (new Color(0.55f, 0.45f, 0.35f), 1.2f), ["rubble"] = (new Color(0.45f, 0.43f, 0.40f), 0.05f),
        ["dirt"] = (new Color(0.50f, 0.38f, 0.25f), 0f), ["grass"] = (new Color(0.33f, 0.55f, 0.22f), 0f),
        ["brush"] = (new Color(0.20f, 0.36f, 0.14f), 0.6f), ["tree"] = (new Color(0.16f, 0.38f, 0.15f), 2.2f),
        ["stump"] = (new Color(0.40f, 0.28f, 0.16f), 0.15f), ["wood"] = (new Color(0.58f, 0.40f, 0.22f), 0f),
        ["pit"] = (new Color(0.30f, 0.24f, 0.18f), 0f), ["metal"] = (new Color(0.62f, 0.66f, 0.72f), 0.02f),
    };

    // Terrains drawn as a ground tile with something standing on it; their height is an invisible collider.
    static readonly HashSet<string> Dressed = new() { "tree", "brush", "stump", "rubble" };

    sealed class View
    {
        public GameObject tile, water, cover, fire, gas, spark;
        public string terrain;
        public int key = int.MinValue;
        public int fireLevel;
        public string gasKind;
        public bool charged;
    }

    View[] views;
    readonly Dictionary<string, Material> materials = new();
    readonly List<Light> fireLights = new();
    readonly List<(int x, int y, int fire)> burning = new();
    MaterialPropertyBlock tint;
    Mesh tuftMesh;

    void InitVisuals()
    {
        views = new View[World.W * World.H];
        for (int i = 0; i < views.Length; i++) views[i] = new View();
        tint = new MaterialPropertyBlock();
        if (tuftMesh == null) tuftMesh = BuildTuftMesh();
        for (int i = 0; i < maxFireLights; i++)
        {
            var l = new GameObject("Fire light").AddComponent<Light>();
            l.transform.SetParent(transform, false);
            l.type = LightType.Point;
            l.color = new Color(1f, 0.55f, 0.22f);
            l.range = 7f;
            l.shadows = LightShadows.None;
            l.enabled = false;
            fireLights.Add(l);
        }
    }

    void Render()
    {
        for (int y = 0; y < World.H; y++)
        for (int x = 0; x < World.W; x++)
        {
            var c = World.At(x, y);
            var v = views[y * World.W + x];
            int key = Key(c);
            if (key == v.key && v.terrain == c.T) continue;
            v.key = key;

            if (v.terrain != c.T) BuildTile(v, c, x, y);
            float ground = SurfaceY(x, y);
            if (Dressed.Contains(c.T)) ground = GroundTop(c);
            var center = CellCenter(x, y);

            // Water and ice: a slab from the ground up to the water surface. Ice is solid to walk on.
            if (c.Water > 0)
            {
                float top = (World.ElevationUnits(c) + c.Water) / (float)World.Rules.Sim.WaterUnitsPerStep * stepHeight;
                top = Mathf.Max(top, ground + 0.04f);
                var slab = Ensure(ref v.water, "Water", center, keepCollider: true);
                slab.transform.position = new Vector3(center.x, (ground + top) / 2f, center.z);
                slab.transform.localScale = new Vector3(cellSize, top - ground, cellSize);
                slab.GetComponent<Renderer>().sharedMaterial = c.Frozen ? Mat("Ice", new Color(0.80f, 0.93f, 1f), 0.97f) : WaterMat();
                slab.GetComponent<Renderer>().shadowCastingMode = c.Frozen ? ShadowCastingMode.On : ShadowCastingMode.Off;
                slab.GetComponent<Collider>().enabled = c.Frozen;
                ground = top;
            }
            else Hide(v.water);

            if (c.Cover != null)
            {
                var col = c.Cover == "oil" ? new Color(0.06f, 0.05f, 0.05f) : c.Cover == "grease" ? new Color(0.85f, 0.78f, 0.40f) : new Color(0.35f, 0.34f, 0.33f);
                var film = Ensure(ref v.cover, "Cover", center);
                film.transform.position = new Vector3(center.x, ground + 0.02f, center.z);
                film.transform.localScale = new Vector3(0.96f * cellSize, 0.03f, 0.96f * cellSize);
                film.GetComponent<Renderer>().sharedMaterial = Mat("Cover-" + c.Cover, col, c.Cover == "ash" ? 0.05f : 0.9f);
            }
            else Hide(v.cover);

            RenderFire(v, c, center, ground);
            RenderGas(v, c, center, ground, (x + y) % 2 == 0);
            RenderCharge(v, c, center, ground);
        }
    }

    // --- fire, gas, charge -------------------------------------------------

    void RenderFire(View v, Cell c, Vector3 center, float ground)
    {
        var prefab = c.Fire >= 3 ? fireLarge : c.Fire == 2 ? fireMedium : fireSmall;
        if (c.Fire == 0 || prefab == null)
        {
            if (v.fireLevel != 0) { Retire(v.fire); v.fire = null; v.fireLevel = 0; }
            if (c.Fire > 0) FallbackFire(v, c, center, ground);
            return;
        }
        if (v.fireLevel == c.Fire && v.fire != null) return;
        Retire(v.fire);
        v.fire = Instantiate(prefab, new Vector3(center.x, ground, center.z), Quaternion.identity, transform);
        v.fire.transform.localScale = Vector3.one * (cellSize / 1.5f);
        v.fireLevel = c.Fire;
    }

    void FallbackFire(View v, Cell c, Vector3 center, float ground)
    {
        var flame = Ensure(ref v.fire, "Fire", center);
        if (flame.GetComponent<Flicker>() == null) flame.AddComponent<Flicker>();
        float s = 0.25f + 0.12f * c.Fire;
        flame.transform.localScale = new Vector3(s, s * 1.3f, s);
        flame.transform.position = new Vector3(center.x, ground + s * 0.65f, center.z);
        flame.GetComponent<Renderer>().sharedMaterial = Mat("Fire", new Color(1f, 0.38f, 0.05f), 0.2f, new Color(1f, 0.3f, 0.02f) * 1.2f);
    }

    // Gas drifts over many cells at once, so effects go on every other cell at reduced emission;
    // a full effect per cell turns into a wall.
    void RenderGas(View v, Cell c, Vector3 center, float ground, bool showFx)
    {
        string kind = c.Gas != null && c.GasAmt > 0 ? c.Gas : null;
        var prefab = kind == "steam" ? steamFx : kind == "smoke" ? smokeFx : null;
        if (prefab != null && !showFx) kind = null;
        if (kind != v.gasKind || (kind != null && prefab == null))
        {
            if (v.gasKind != null && v.gas != null && v.gas.GetComponent<ParticleSystem>() != null) { Retire(v.gas); v.gas = null; }
            v.gasKind = kind;
            if (kind != null && prefab != null)
            {
                Hide(v.gas);
                v.gas = Instantiate(prefab, new Vector3(center.x, ground + 0.3f, center.z), Quaternion.identity, transform);
                foreach (var ps in v.gas.GetComponentsInChildren<ParticleSystem>())
                {
                    var emission = ps.emission;
                    emission.rateOverTimeMultiplier *= kind == "smoke" ? 0.3f : 0.6f;
                }
            }
        }
        if (kind == null) { Hide(v.gas); return; }
        if (prefab != null) return;

        var puff = Ensure(ref v.gas, "Gas", center);
        float s = 0.25f + 0.1f * c.GasAmt;
        puff.transform.localScale = Vector3.one * s;
        puff.transform.position = new Vector3(center.x, ground + 1.2f, center.z);
        puff.GetComponent<Renderer>().sharedMaterial = kind == "steam" ? Mat("Steam", new Color(0.95f, 0.97f, 1f), 0.3f) : Mat("Smoke", new Color(0.3f, 0.3f, 0.32f), 0.1f);
    }

    void RenderCharge(View v, Cell c, Vector3 center, float ground)
    {
        bool on = c.Charge > 0;
        if (sparksFx != null)
        {
            if (on && !v.charged)
            {
                var fx = Instantiate(sparksFx, new Vector3(center.x, ground + 0.1f, center.z), Quaternion.identity, transform);
                Destroy(fx, 1.5f);
            }
            v.charged = on;
            return;
        }
        if (!on) { Hide(v.spark); return; }
        var spark = Ensure(ref v.spark, "Charge", center);
        spark.transform.position = new Vector3(center.x, ground + 0.08f, center.z);
        spark.transform.localScale = new Vector3(0.8f * cellSize, 0.05f, 0.8f * cellSize);
        spark.GetComponent<Renderer>().sharedMaterial = Mat("Charge", new Color(1f, 0.95f, 0.3f), 0.5f, new Color(1f, 0.9f, 0.2f) * 6f);
    }

    // Let a particle effect finish its particles instead of popping out.
    static void Retire(GameObject go)
    {
        if (go == null) return;
        var systems = go.GetComponentsInChildren<ParticleSystem>();
        if (systems.Length == 0) { Destroy(go); return; }
        foreach (var ps in systems) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        Destroy(go, 3f);
    }

    // Spread the light pool over the biggest fires, at least two cells apart.
    void UpdateFireLights()
    {
        burning.Clear();
        for (int y = 0; y < World.H; y++)
        for (int x = 0; x < World.W; x++)
        {
            int f = World.At(x, y).Fire;
            if (f > 0) burning.Add((x, y, f));
        }
        burning.Sort((a, b) => b.fire.CompareTo(a.fire));
        int used = 0;
        foreach (var b in burning)
        {
            if (used >= fireLights.Count) break;
            bool near = false;
            for (int i = 0; i < used && !near; i++)
            {
                var p = CellAt(fireLights[i].transform.position);
                near = Mathf.Abs(p.x - b.x) <= 2 && Mathf.Abs(p.y - b.y) <= 2;
            }
            if (near) continue;
            var l = fireLights[used++];
            l.transform.position = CellCenter(b.x, b.y) + Vector3.up * (GroundTop(World.At(b.x, b.y)) + 1f);
            l.intensity = 1.5f + b.fire;
            l.enabled = true;
        }
        for (int i = used; i < fireLights.Count; i++) fireLights[i].enabled = false;
    }

    void FlickerFireLights()
    {
        for (int i = 0; i < fireLights.Count; i++)
        {
            var l = fireLights[i];
            if (!l.enabled) continue;
            l.range = 6f + 1.5f * Mathf.PerlinNoise(Time.time * 6f, i * 7.3f);
        }
    }

    // --- tiles -------------------------------------------------------------

    static int Key(Cell c)
    {
        unchecked
        {
            int k = c.Water;
            k = k * 31 + (c.Frozen ? 1 : 0);
            k = k * 31 + (c.Cover?.GetHashCode() ?? 0);
            k = k * 31 + c.Fire;
            k = k * 31 + (c.Gas?.GetHashCode() ?? 0);
            k = k * 31 + c.GasAmt;
            k = k * 31 + (c.Charge > 0 ? 1 : 0);
            return k;
        }
    }

    float GroundTop(Cell c) => World.ElevationUnits(c) / (float)World.Rules.Sim.WaterUnitsPerStep * stepHeight;

    void BuildTile(View v, Cell c, int x, int y)
    {
        if (v.tile != null) Destroy(v.tile);
        v.terrain = c.T;
        var look = Look(c.T);
        bool dressed = Dressed.Contains(c.T);
        float top = dressed ? GroundTop(c) : SurfaceY(x, y);
        const float bottom = -1.5f;
        var center = CellCenter(x, y);

        var tile = GameObject.CreatePrimitive(PrimitiveType.Cube);
        tile.name = $"{c.T} {x},{y}";
        tile.transform.SetParent(transform, false);
        tile.transform.position = new Vector3(center.x, (top + bottom) / 2f, center.z);
        tile.transform.localScale = new Vector3(cellSize, top - bottom, cellSize);
        var groundColor = dressed ? Look(c.T == "brush" || c.T == "tree" ? "grass" : "dirt").color : look.color;
        Paint(tile.GetComponent<Renderer>(), "Ground", groundColor, c.T == "metal" ? 0.85f : 0.1f, x, y);
        v.tile = tile;

        var rng = new System.Random(x * 7919 + y * 104729 + c.T.GetHashCode());
        var surface = new Vector3(center.x, top, center.z);
        switch (c.T)
        {
            case "grass":
                for (int i = 0; i < 6; i++) Tuft(tile.transform, surface, rng, x, y);
                break;
            case "tree":
                Blocker(tile.transform, surface, look.height);
                Part(tile.transform, PrimitiveType.Cylinder, surface + Vector3.up * 1.1f, new Vector3(0.28f, 1.1f, 0.28f), "Bark", new Color(0.36f, 0.25f, 0.16f));
                for (int i = 0; i < 3; i++)
                {
                    var off = new Vector3((float)rng.NextDouble() - 0.5f, 2.4f + 0.5f * (float)rng.NextDouble(), (float)rng.NextDouble() - 0.5f) * cellSize * 0.6f;
                    off.y = 2.3f + 0.6f * (float)rng.NextDouble();
                    float r = 1.1f + 0.4f * (float)rng.NextDouble();
                    var leaf = Part(tile.transform, PrimitiveType.Sphere, surface + off, new Vector3(r, r * 0.85f, r), "Leaves", look.color);
                    Paint(leaf.GetComponent<Renderer>(), "Leaves", look.color, 0.1f, x + i * 13, y);
                }
                for (int i = 0; i < 3; i++) Tuft(tile.transform, surface, rng, x, y);
                break;
            case "brush":
                Blocker(tile.transform, surface, 1.2f);
                for (int i = 0; i < 5; i++)
                {
                    var off = new Vector3((float)rng.NextDouble() - 0.5f, 0f, (float)rng.NextDouble() - 0.5f) * cellSize * 0.7f;
                    float r = 0.55f + 0.35f * (float)rng.NextDouble();
                    var bush = Part(tile.transform, PrimitiveType.Sphere, surface + off + Vector3.up * r * 0.35f, new Vector3(r, r * 0.7f, r), "Leaves", look.color);
                    Paint(bush.GetComponent<Renderer>(), "Leaves", look.color, 0.1f, x + i * 17, y);
                }
                break;
            case "stump":
                Part(tile.transform, PrimitiveType.Cylinder, surface + Vector3.up * 0.15f, new Vector3(0.45f, 0.15f, 0.45f), "Bark", look.color);
                break;
            case "rubble":
                for (int i = 0; i < 4; i++)
                {
                    var off = new Vector3((float)rng.NextDouble() - 0.5f, 0.1f, (float)rng.NextDouble() - 0.5f) * cellSize * 0.8f;
                    var rock = Part(tile.transform, PrimitiveType.Cube, surface + off, Vector3.one * (0.25f + 0.2f * (float)rng.NextDouble()), "Stone", look.color);
                    rock.transform.rotation = Quaternion.Euler(rng.Next(360), rng.Next(360), rng.Next(360));
                }
                break;
        }
    }

    // An invisible box the character can't walk through, for terrain that blocks.
    void Blocker(Transform parent, Vector3 surface, float height)
    {
        var go = new GameObject("Blocker");
        go.transform.SetParent(parent, true);
        go.transform.position = surface + Vector3.up * height / 2f;
        var box = go.AddComponent<BoxCollider>();
        box.size = new Vector3(cellSize, height, cellSize);
    }

    GameObject Part(Transform parent, PrimitiveType type, Vector3 at, Vector3 scale, string mat, Color color)
    {
        var go = GameObject.CreatePrimitive(type);
        Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent, true);
        go.transform.position = at;
        go.transform.localScale = Vector3.one;
        go.transform.localScale = new Vector3(scale.x / parent.lossyScale.x, scale.y / parent.lossyScale.y, scale.z / parent.lossyScale.z);
        go.GetComponent<Renderer>().sharedMaterial = Mat(mat, color, 0.1f);
        return go;
    }

    void Tuft(Transform parent, Vector3 surface, System.Random rng, int x, int y)
    {
        var go = new GameObject("Grass");
        go.transform.SetParent(parent, true);
        var off = new Vector3((float)rng.NextDouble() - 0.5f, 0f, (float)rng.NextDouble() - 0.5f) * cellSize * 0.9f;
        go.transform.position = surface + off;
        go.transform.rotation = Quaternion.Euler(0f, rng.Next(360), 0f);
        float s = 0.7f + 0.6f * (float)rng.NextDouble();
        go.transform.localScale = new Vector3(s / parent.lossyScale.x, s / parent.lossyScale.y, s / parent.lossyScale.z);
        go.AddComponent<MeshFilter>().sharedMesh = tuftMesh;
        var r = go.AddComponent<MeshRenderer>();
        r.shadowCastingMode = ShadowCastingMode.Off;
        Paint(r, "Blades", new Color(0.36f, 0.62f, 0.24f), 0.15f, x * 3 + rng.Next(5), y);
    }

    // A tuft of five thin blades leaning outward. Each blade has a front and a
    // back face with their own vertices; shared vertices would average the two
    // normals to zero and light as NaN, which bloom then smears over the screen.
    static Mesh BuildTuftMesh()
    {
        var verts = new List<Vector3>();
        var normals = new List<Vector3>();
        var tris = new List<int>();
        for (int i = 0; i < 5; i++)
        {
            float a = i * Mathf.PI * 2f / 5f;
            var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            var side = Vector3.Cross(dir, Vector3.up) * 0.03f;
            var tip = dir * 0.1f + Vector3.up * (0.28f + 0.08f * (i % 3));
            var facing = Vector3.Cross(side, tip + side).normalized;
            foreach (var n in new[] { facing, -facing })
            {
                int b = verts.Count;
                verts.AddRange(new[] { -side, side, tip });
                normals.AddRange(new[] { n, n, n });
                if (n == facing) tris.AddRange(new[] { b, b + 1, b + 2 });
                else tris.AddRange(new[] { b + 2, b + 1, b });
            }
        }
        var mesh = new Mesh { name = "Grass tuft" };
        mesh.SetVertices(verts);
        mesh.SetNormals(normals);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    // Shared material, with a per-cell shade so neighbouring tiles don't read as one flat sheet.
    void Paint(Renderer r, string mat, Color color, float smoothness, int x, int y)
    {
        r.sharedMaterial = Mat(mat, Color.white, smoothness);
        float n = Mathf.PerlinNoise(x * 0.37f + 0.11f, y * 0.37f + 0.53f);
        float jitter = 0.86f + 0.24f * n + 0.04f * (((x * 73856093) ^ (y * 19349663)) & 7) / 7f;
        r.GetPropertyBlock(tint);
        tint.SetColor("_BaseColor", color * jitter);
        r.SetPropertyBlock(tint);
    }

    (Color color, float height) Look(string terrain) =>
        TerrainLook.TryGetValue(terrain, out var l) ? l : (Color.magenta, 0f);

    GameObject Ensure(ref GameObject go, string name, Vector3 at, bool keepCollider = false)
    {
        if (go == null || go.GetComponent<ParticleSystem>() != null)
        {
            go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(transform, false);
            go.transform.position = at;
            if (!keepCollider) Destroy(go.GetComponent<Collider>());
        }
        go.SetActive(true);
        return go;
    }

    static void Hide(GameObject go)
    {
        if (go != null && go.activeSelf && go.GetComponent<ParticleSystem>() == null) go.SetActive(false);
    }

    Material Mat(string name, Color color, float smoothness, Color? emission = null)
    {
        if (materials.TryGetValue(name, out var m)) return m;
        m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
        m.SetColor("_BaseColor", color);
        m.SetFloat("_Smoothness", smoothness);
        if (emission.HasValue)
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", emission.Value);
        }
        materials[name] = m;
        return m;
    }

    // URP Lit set up as a transparent surface.
    Material WaterMat()
    {
        if (materials.TryGetValue("Water", out var m)) return m;
        m = Mat("Water", new Color(0.16f, 0.42f, 0.62f, 0.62f), 0.95f);
        m.SetFloat("_Surface", 1f);
        m.SetFloat("_Blend", 0f);
        m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_ZWrite", 0f);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = (int)RenderQueue.Transparent;
        m.SetOverrideTag("RenderType", "Transparent");
        return m;
    }

    void OnDestroy()
    {
        foreach (var m in materials.Values) Destroy(m);
        if (tuftMesh != null) Destroy(tuftMesh);
    }
}
