using System.Collections;
using UnityEngine;

// The probe's dither scenario (docs/FAST_LOOP.md): the wizard is put behind
// the terrain tree nearest to it, with the camera cut to look at it through
// the tree, then the camera is cut round to the open side. While the tree is
// in the way the wizard's OccluderFade should fade in (the global dither on,
// the see-through figure drawn); from the open side, fade back out.
public class DitherProbe : ProbeScenario
{
    static readonly int DitherId = Shader.PropertyToID("_OccluderDither");

    protected override IEnumerator Run()
    {
        wizard.TryGetComponent<OccluderFade>(out var fade);
        bool found = NearestTree(wizard.transform.position, out var tree);
        string parts = found ? "a tree near the wizard" : "no terrain tree found";
        Check("dither_parts", fade != null && found, fade == null ? "no OccluderFade on the wizard" : parts);
        if (fade == null || !found) yield break;

        // The wizard 2.5 m beyond the tree, the camera 7 m before it and 6 m up, looking at the wizard's chest.
        var away = Flat(tree - wizard.transform.position);
        away = away.sqrMagnitude > 0.01f ? away.normalized : Vector3.forward;
        var spot = tree + away * 2.5f;
        Teleport(new Vector3(spot.x, TerrainGround(spot) + 0.1f, spot.z));
        yield return Wait(0.3f);
        var chest = wizard.transform.position + Vector3.up * 1.1f;
        var eye = tree - away * 7f;
        Cut(new Vector3(eye.x, TerrainGround(eye) + 6f, eye.z), chest);
        yield return Wait(0.8f);
        float cut = Shader.GetGlobalVector(DitherId).w;
        Metric("hidden_fade", fade.Fade);
        Metric("hidden_cut", cut);
        Metric("hidden_swapped", fade.Swapped);
        Check("tree_occludes", fade.Occluded, "something found between the camera and the wizard");
        Check("dither_on", fade.Fade > 0.9f && cut > 0.5f, $"fade {fade.Fade:0.00}, global cut {cut:0.00}");
        Check("silhouette_on", fade.SilhouetteShown);
        Shot("behind-tree");

        // Round to the open side: the tree is behind the wizard now.
        var open = wizard.transform.position + away * 6f;
        Cut(new Vector3(open.x, TerrainGround(open) + 3f, open.z), chest);
        yield return Wait(0.8f);
        cut = Shader.GetGlobalVector(DitherId).w;
        Metric("clear_fade", fade.Fade);
        Check("clear_view", !fade.Occluded, "nothing between the camera and the wizard");
        Check("dither_off", fade.Fade < 0.01f && cut < 0.01f, $"fade {fade.Fade:0.00}, global cut {cut:0.00}");
        Check("silhouette_off", !fade.SilhouetteShown);
        Check("materials_restored", fade.Swapped == 0, $"{fade.Swapped} renderers still on DitherLit");
        Shot("open-side");
    }

    // The terrain tree nearest a point, at its foot.
    static bool NearestTree(Vector3 near, out Vector3 foot)
    {
        foot = Vector3.zero;
        float best = float.MaxValue;
        foreach (var t in Terrain.activeTerrains)
        {
            var data = t.terrainData;
            var origin = t.GetPosition();
            for (int i = 0; i < data.treeInstanceCount; i++)
            {
                var at = origin + Vector3.Scale(data.GetTreeInstance(i).position, data.size);
                float d = (Flat(at) - Flat(near)).sqrMagnitude;
                if (d >= best) continue;
                best = d;
                foot = at;
            }
        }
        return best < float.MaxValue;
    }

    static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
}
