using System;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Takes parts of the picture away for a PerfScenario run, as measuring starts, so a run with one off against a run
// without tells what that part costs (the GPU above all). Each is a player flag; none changes the world (D4), and none
// is for play. -perf-shadows-off (the sun's shadows), -perf-hard-shadows, -perf-cascades N, -perf-post-off,
// -perf-ssao-off (the renderers' SSAO feature), -perf-ssao-after-opaque / -perf-ssao-before-opaque (when SSAO is worked
// out), -perf-fog-off, -perf-render-scale S, -perf-grass-off (terrain details), -perf-detail-metres M, -perf-detail-density D,
// -perf-grass-no-shadows, -perf-terrain-off
// (the terrains' heightmaps), -perf-impostors-off (every tree TreeImpostors draws), -perf-brush-far (brush clumps drawn
// to the haze again, as before their LOD), -perf-gpu-occlusion and -perf-small-mesh P (the Resident Drawer's culling),
// -perf-hide A,B,... (the renderers of the census groups that start with A or B, PerfCensus.Group, as they stand when
// measuring starts), -perf-hide-shader S [-perf-hide-beyond M], -perf-pack-tufts (the terrain pack's own grass tufts, as
// before LightTufts) and -perf-grass-fade S,E (the grass material's dither fade).
public static class PerfKnobs
{
    /// <summary>The picture as it was before the 2026-11-23 GPU fixes: brush clumps to the haze and SSAO before the opaques.</summary>
    public static void AsBefore()
    {
        if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset pipeline) SsaoAfterOpaque(pipeline, false);
        DrawBrushFar();
        Debug.Log("PERF KNOBS as_before");
    }

    static string[] args;
    static UniversalRenderPipelineAsset urp;

    static bool Has(string flag) => args.Contains(flag);

    static string Arg(string flag)
    {
        int at = Array.IndexOf(args, flag);
        return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
    }

    static bool Number(string flag, out float value) =>
        float.TryParse(Arg(flag), NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    static string Text(float value, string format) => value.ToString(format, CultureInfo.InvariantCulture);

    static T[] All<T>() where T : UnityEngine.Object => UnityEngine.Object.FindObjectsByType<T>(FindObjectsSortMode.None);

    // Each knob: what it did, or null when it wasn't asked for.
    static readonly Func<string>[] Knobs =
    {
        ShadowsOff, HardShadows, Cascades, PostOff, SsaoOff, SsaoWhen, Fog, RenderScale, Details, DetailSettings, GrassNoShadows,
        ImpostorsOff, BrushFar, Resident, HideByShader, HideGroups, PackTuftsKnob, GrassFade,
    };

    /// <summary>Applies the knobs asked for and logs a "PERF KNOBS" line of what it did.</summary>
    public static void Apply()
    {
        args = Environment.GetCommandLineArgs();
        urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        var done = new System.Text.StringBuilder();
        foreach (var knob in Knobs)
        {
            string did = knob();
            if (did != null) done.Append(' ').Append(did);
        }
        if (done.Length > 0) Debug.Log("PERF KNOBS" + done);
    }

    static string ShadowsOff()
    {
        if (!Has("-perf-shadows-off")) return null;
        foreach (var l in All<Light>()) l.shadows = LightShadows.None;
        if (urp != null) urp.shadowDistance = 0f;
        return "shadows_off";
    }

    static string HardShadows()
    {
        if (!Has("-perf-hard-shadows")) return null;
        foreach (var l in All<Light>().Where(l => l.shadows == LightShadows.Soft)) l.shadows = LightShadows.Hard;
        return "hard_shadows";
    }

    static string Cascades()
    {
        if (urp == null || !int.TryParse(Arg("-perf-cascades"), out int cascades) || cascades <= 0) return null;
        urp.shadowCascadeCount = cascades;
        return $"cascades={cascades}";
    }

    static string PostOff()
    {
        if (!Has("-perf-post-off")) return null;
        foreach (var extra in Camera.allCameras.Select(c => c.GetUniversalAdditionalCameraData()).Where(e => e != null)) extra.renderPostProcessing = false;
        return "post_off";
    }

    static string SsaoOff()
    {
        if (urp == null || !Has("-perf-ssao-off")) return null;
        int off = 0;
        foreach (var feature in Ssao(urp))
        {
            feature.SetActive(false);
            off++;
        }
        return $"ssao_off={off}";
    }

    static string SsaoWhen()
    {
        if (urp == null) return null;
        if (Has("-perf-ssao-after-opaque")) return $"ssao_after_opaque={SsaoAfterOpaque(urp, true)}";
        return Has("-perf-ssao-before-opaque") ? $"ssao_before_opaque={SsaoAfterOpaque(urp, false)}" : null;
    }

    static string Fog()
    {
        if (!Has("-perf-fog-off")) return null;
        RenderSettings.fog = false;
        return "fog_off";
    }

    static string RenderScale()
    {
        if (urp == null || !Number("-perf-render-scale", out float scale) || scale <= 0f) return null;
        urp.renderScale = scale;
        return $"render_scale={Text(scale, "0.00")}";
    }

    static string Details()
    {
        bool grassOff = Has("-perf-grass-off"), terrainOff = Has("-perf-terrain-off");
        if (!grassOff && !terrainOff) return null;
        foreach (var t in All<Terrain>())
        {
            if (grassOff) t.detailObjectDistance = 0f;
            if (terrainOff) t.drawHeightmap = false;
        }
        return (grassOff ? "grass_off " : "") + (terrainOff ? "terrain_off" : "");
    }

    static string DetailSettings()
    {
        bool metres = Number("-perf-detail-metres", out float m), density = Number("-perf-detail-density", out float d);
        if (!metres && !density) return null;
        foreach (var t in All<Terrain>())
        {
            if (metres && t.detailObjectDistance > 0f) t.detailObjectDistance = m;
            if (density) t.detailObjectDensity = d;
        }
        return $"detail_metres={Text(metres ? m : -1f, "0")} detail_density={Text(density ? d : -1f, "0.00")}";
    }

    /// <summary>Every terrain that draws grass draws it to <paramref name="metres"/>.</summary>
    public static void DetailMetres(float metres)
    {
        foreach (var t in All<Terrain>().Where(t => t.detailObjectDistance > 0f)) t.detailObjectDistance = metres;
        Debug.Log($"PERF KNOBS detail_metres={Text(metres, "0")}");
    }

    static string GrassNoShadows() => Has("-perf-grass-no-shadows") ? $"grass_shadows_off={GrassShadows(ShadowCastingMode.Off)}" : null;

    /// <summary>
    /// The terrains' grass (their detail prototypes) cast shadows as <paramref name="mode"/> says: On is the grass as it was
    /// before LightTufts turned its shadows off (for -perf-look-pair-grass-shadows). How many renderers it changed.
    /// </summary>
    public static int GrassShadows(ShadowCastingMode mode)
    {
        int changed = 0;
        foreach (var data in All<Terrain>().Select(t => t.terrainData).Where(d => d != null))
        {
            var renderers = data.detailPrototypes.Where(p => p.prototype != null).SelectMany(p => p.prototype.GetComponentsInChildren<Renderer>(true));
            foreach (var r in renderers.Where(r => r.shadowCastingMode != mode))
            {
                r.shadowCastingMode = mode;
                changed++;
            }
            data.RefreshPrototypes();
        }
        return changed;
    }

    static string PackTuftsKnob() => Has("-perf-pack-tufts") ? $"pack_tufts={PackTufts()}" : null;

    /// <summary>
    /// The grass drawn with the terrain pack's own tufts again (MapTerrains.packTufts), as before LightTufts: the terrains'
    /// detail prototypes and the brush clumps made from them. How many meshes it swapped.
    /// </summary>
    public static int PackTufts()
    {
        var packs = MapTerrains.Instance != null ? MapTerrains.Instance.packTufts : null;
        if (packs == null || packs.Length == 0) return 0;
        var swap = new System.Collections.Generic.Dictionary<Mesh, Mesh>();
        foreach (var data in All<Terrain>().Select(t => t.terrainData).Where(d => d != null).Distinct())
        {
            foreach (var filter in data.detailPrototypes.Where(p => p.prototype != null).Select(p => p.prototype.GetComponent<MeshFilter>()).Where(f => f != null))
            {
                var pack = packs.FirstOrDefault(m => m != null && m != filter.sharedMesh && m.name == filter.sharedMesh.name);
                if (pack == null) continue;
                swap[filter.sharedMesh] = pack;
                filter.sharedMesh = pack;
            }
        }
        foreach (var data in All<Terrain>().Select(t => t.terrainData).Where(d => d != null).Distinct()) data.RefreshPrototypes();
        int swapped = swap.Count;
        foreach (var filter in All<MeshFilter>())
        {
            if (filter.sharedMesh == null || !swap.TryGetValue(filter.sharedMesh, out var pack)) continue;
            filter.sharedMesh = pack;
            swapped++;
        }
        return swapped;
    }

    // -perf-grass-fade S,E: the grass's own dither fade (the pack's TerrainGrass material, 40 to 80 m as it comes) from
    // S to E metres, on the detail prototypes' materials.
    static string GrassFade()
    {
        var range = Arg("-perf-grass-fade")?.Split(',');
        if (range == null || range.Length != 2 || !float.TryParse(range[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float from)
            || !float.TryParse(range[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float to)) return null;
        var materials = All<Terrain>().Select(t => t.terrainData).Where(d => d != null)
            .SelectMany(d => d.detailPrototypes).Where(p => p.prototype != null)
            .SelectMany(p => p.prototype.GetComponentsInChildren<Renderer>(true)).SelectMany(r => r.sharedMaterials)
            .Where(m => m != null && m.HasProperty(FadeStart)).Distinct().ToList();
        foreach (var m in materials)
        {
            m.SetFloat(FadeStart, from);
            m.SetFloat(FadeEnd, to);
        }
        return $"grass_fade={Text(from, "0")}-{Text(to, "0")}({materials.Count})";
    }

    static readonly int FadeStart = Shader.PropertyToID("Distance_Fade_Start"), FadeEnd = Shader.PropertyToID("Distance_Fade_End");

    static string ImpostorsOff()
    {
        if (!Has("-perf-impostors-off")) return null;
        TreeImpostors.Skip = _ => true;
        return "impostors_off";
    }

    static string BrushFar() => Has("-perf-brush-far") ? $"brush_far={DrawBrushFar()}" : null;

    static int DrawBrushFar()
    {
        int far = 0;
        foreach (var lod in All<LODGroup>().Where(l => l.name.StartsWith("brush ", StringComparison.Ordinal)))
        {
            lod.enabled = false;
            far++;
        }
        return far;
    }

    static string Resident()
    {
        if (urp == null) return null;
        string did = null;
        if (Has("-perf-gpu-occlusion"))
        {
            urp.gpuResidentDrawerEnableOcclusionCullingInCameras = true;
            did = "gpu_occlusion";
        }
        if (Number("-perf-small-mesh", out float small) && small >= 0f)
        {
            urp.smallMeshScreenPercentage = small;
            did += $" small_mesh={Text(small, "0.00")}";
        }
        return did;
    }

    static string HideGroups()
    {
        string hide = Arg("-perf-hide");
        if (string.IsNullOrEmpty(hide)) return null;
        var prefixes = hide.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Replace('_', ' ')).ToArray();
        int hidden = 0;
        foreach (var r in All<Renderer>().Where(r => r.enabled))
        {
            string group = PerfCensus.Group(r.transform);
            if (!prefixes.Any(p => group.StartsWith(p, StringComparison.Ordinal))) continue;
            r.enabled = false;
            hidden++;
        }
        return $"hidden={hidden}({hide})";
    }

    static System.Collections.Generic.List<ScriptableRendererFeature> Ssao(UniversalRenderPipelineAsset pipeline)
    {
        var found = new System.Collections.Generic.List<ScriptableRendererFeature>();
        foreach (var data in pipeline.rendererDataList)
            if (data != null) found.AddRange(data.rendererFeatures.Where(f => f != null && f.name.Contains("Ambient")));
        return found;
    }

    // SSAO worked out after the opaques from the copied depth, or before them (which costs a depth prepass of everything).
    static int SsaoAfterOpaque(UniversalRenderPipelineAsset pipeline, bool afterOpaque)
    {
        const System.Reflection.BindingFlags Any = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
        int set = 0;
        foreach (var feature in Ssao(pipeline))
        {
            var settings = feature.GetType().GetField("m_Settings", Any)?.GetValue(feature);
            var after = settings?.GetType().GetField("AfterOpaque", Any);
            if (after == null) continue;
            after.SetValue(settings, afterOpaque);
            feature.SetActive(false);
            feature.SetActive(true);
            set++;
        }
        return set;
    }

    // -perf-hide-shader S [-perf-hide-beyond M]: the renderers drawn with a shader whose name starts with S (underscores
    // for spaces), all of them or only those further than M metres from the camera (a cull distance, tried).
    static string HideByShader()
    {
        string shader = Arg("-perf-hide-shader");
        if (string.IsNullOrEmpty(shader)) return null;
        shader = shader.Replace('_', ' ');
        float far = Number("-perf-hide-beyond", out float m) ? m : 0f;
        var eye = Camera.main != null ? Camera.main.transform.position : Vector3.zero;
        int hidden = 0;
        foreach (var r in All<Renderer>().Where(r => r.enabled && UsesShader(r, shader)))
        {
            if (far > 0f && (r.bounds.center - eye).sqrMagnitude <= far * far) continue;
            r.enabled = false;
            hidden++;
        }
        return $"hidden_by_shader={hidden}({shader} beyond {Text(far, "0")} m)";
    }

    static bool UsesShader(Renderer r, string shader)
    {
        var mat = r.sharedMaterial;
        return mat != null && mat.shader != null && mat.shader.name.StartsWith(shader, StringComparison.Ordinal);
    }
}
