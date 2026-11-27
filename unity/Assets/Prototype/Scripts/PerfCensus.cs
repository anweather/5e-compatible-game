using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// What the picture is made of, for a PerfScenario run (-fps-probe -perf-scenario ...): "PERF CENSUS" lines as measuring
// starts and as it ends, so two maps seen from one place can be told apart by what each draws (J-086: what is drawn should
// follow the drawn tier, never the map's size). Renderers by group (the scene root and its first child), all enabled ones
// and those some camera drew last frame (Renderer.isVisible, shadow passes included), with their triangles; the same by
// shader; the terrains, the cameras, the lights, URP's settings and the tree impostors' own counts. Slow (it walks every
// renderer), so it runs only outside the measured frames.
//
// PerfKnobs takes parts of the picture away for a run, to see what each costs the GPU.
public static class PerfCensus
{
    const int MostGroups = 24, MostShaders = 16;

    sealed class Tally
    {
        public int Enabled, Visible, Shadows;
        public long Tris, VisibleTris;
    }

    public static List<string> Lines(string label)
    {
        var inv = CultureInfo.InvariantCulture;
        var byGroup = new Dictionary<string, Tally>();
        var byShader = new Dictionary<string, Tally>();
        var all = new Tally();
        foreach (var r in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
        {
            if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
            long tris = Triangles(r);
            bool seen = r.isVisible;
            bool shadow = seen && r.shadowCastingMode != ShadowCastingMode.Off;
            Add(all, tris, seen, shadow);
            Add(Get(byGroup, Group(r.transform)), tris, seen, shadow);
            var m = r.sharedMaterial;
            Add(Get(byShader, m != null && m.shader != null ? m.shader.name : "(none)"), tris, seen, shadow);
        }
        var lines = new List<string>
        {
            $"PERF CENSUS {label} renderers={all.Enabled} visible={all.Visible} visible_shadow_casters={all.Shadows} tris_enabled={all.Tris} tris_visible={all.VisibleTris}" +
            $" {Terrains()} {Impostors()} {Urp()}",
            $"PERF CENSUS {label} cameras {string.Join(" ", Camera.allCameras.Select(Describe))}",
            $"PERF CENSUS {label} lights {Lights()}",
        };
        lines.Add($"PERF CENSUS {label} groups" + Top(byGroup, MostGroups));
        lines.Add($"PERF CENSUS {label} shaders" + Top(byShader, MostShaders));
        return lines;

        string Top(Dictionary<string, Tally> tallies, int most)
        {
            var sb = new StringBuilder();
            foreach (var (name, t) in tallies.OrderByDescending(p => p.Value.VisibleTris).ThenByDescending(p => p.Value.Enabled).Take(most).Select(p => (p.Key, p.Value)))
                sb.Append(' ').Append(name.Replace(' ', '_')).Append('=').Append(t.Enabled.ToString(inv)).Append('/').Append(t.Visible.ToString(inv))
                    .Append('/').Append(t.Shadows.ToString(inv)).Append('/').Append((t.VisibleTris / 1000).ToString(inv)).Append('k');
            return sb.ToString();
        }
    }

    static void Add(Tally t, long tris, bool seen, bool shadow)
    {
        t.Enabled++;
        t.Tris += tris;
        if (!seen) return;
        t.Visible++;
        t.VisibleTris += tris;
        if (shadow) t.Shadows++;
    }

    static Tally Get(Dictionary<string, Tally> tallies, string key)
    {
        if (!tallies.TryGetValue(key, out var t)) tallies[key] = t = new Tally();
        return t;
    }

    public static string Group(Transform t)
    {
        var root = t.root;
        if (t == root) return root.name;
        var under = t;
        while (under.parent != root) under = under.parent;
        return root.name + "/" + under.name;
    }

    static long Triangles(Renderer r)
    {
        Mesh mesh = r switch
        {
            MeshRenderer => r.TryGetComponent<MeshFilter>(out var f) ? f.sharedMesh : null,
            SkinnedMeshRenderer s => s.sharedMesh,
            _ => null,
        };
        if (mesh == null) return 0;
        long indices = 0;
        for (int i = 0; i < mesh.subMeshCount; i++)
            if (mesh.GetTopology(i) == MeshTopology.Triangles) indices += mesh.GetIndexCount(i);
        return indices / 3;
    }

    static string Terrains()
    {
        int made = 0, enabled = 0, heightmap = 0, trees = 0, details = 0;
        foreach (var t in UnityEngine.Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None))
        {
            made++;
            if (!t.enabled || !t.gameObject.activeInHierarchy) continue;
            enabled++;
            if (t.drawHeightmap) heightmap++;
            if (t.drawTreesAndFoliage && t.treeDistance > 0f && t.terrainData != null) trees += t.terrainData.treeInstanceCount;
            if (t.drawTreesAndFoliage && t.detailObjectDistance > 0f) details++;
        }
        return $"terrains={made} terrains_on={enabled} terrains_heightmap={heightmap} terrain_tree_instances={trees} terrains_with_details={details}";
    }

    static string Impostors() =>
        $"impostors_on={TreeImpostors.On} impostors_drawn={TreeImpostors.Drawn} impostor_calls={TreeImpostors.Calls} near_trees={TreeImpostors.NearDrawn} near_trees_made={TreeImpostors.NearMade}" +
        $" prefabs_within={TreeImpostors.PrefabsWithin:0} impostors_within={TreeImpostors.ImpostorsWithin:0} trees_near={TreeImpostors.NearAround} trees_around={TreeImpostors.AllAround} {Brush()}";

    static string Brush()
    {
        var cam = Camera.main;
        int all = 0, within = 0, clumps = 0, clumpsWithin = 0;
        float reachSq = CellDressing.BrushDrawMetres * CellDressing.BrushDrawMetres;
        foreach (var lod in UnityEngine.Object.FindObjectsByType<LODGroup>(FindObjectsSortMode.None))
        {
            if (!lod.name.StartsWith("brush ", StringComparison.Ordinal)) continue;
            int n = lod.GetLODs() is { Length: > 0 } l ? l[0].renderers.Length : 0;
            all++;
            clumps += n;
            if (cam == null) continue;
            var d = lod.transform.position - cam.transform.position;
            if (d.x * d.x + d.z * d.z > reachSq) continue;
            within++;
            clumpsWithin += n;
        }
        return $"brush_cells={all} brush_cells_within={within} brush_renderers={clumps} brush_renderers_within={clumpsWithin}";
    }

    static string Urp()
    {
        if (GraphicsSettings.currentRenderPipeline is not UniversalRenderPipelineAsset urp) return "urp=none";
        var inv = CultureInfo.InvariantCulture;
        return $"shadow_distance={urp.shadowDistance.ToString("0", inv)} cascades={urp.shadowCascadeCount} render_scale={urp.renderScale.ToString("0.00", inv)} msaa={urp.msaaSampleCount} hdr={urp.supportsHDR} fog={RenderSettings.fog} fog_mode={RenderSettings.fogMode}";
    }

    static string Describe(Camera c)
    {
        var inv = CultureInfo.InvariantCulture;
        var extra = c.GetUniversalAdditionalCameraData();
        return $"{c.name.Replace(' ', '_')}(depth={c.depth.ToString("0", inv)},far={c.farClipPlane.ToString("0", inv)},cull0={c.layerCullDistances[0].ToString("0", inv)},post={(extra != null && extra.renderPostProcessing)},target={(c.targetTexture != null ? c.targetTexture.name : "screen")})";
    }

    static string Lights()
    {
        var sb = new StringBuilder();
        int on = 0, shadowed = 0;
        foreach (var l in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
        {
            if (!l.enabled || !l.gameObject.activeInHierarchy) continue;
            on++;
            if (l.shadows != LightShadows.None) shadowed++;
            if (l.type == LightType.Directional) sb.Append($" directional:{l.name.Replace(' ', '_')}:shadows={l.shadows}");
        }
        return $"on={on} shadowed={shadowed}{sb}";
    }
}
