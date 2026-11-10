using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// The tree impostors' stills (docs/design/tree-impostors.md, J-090), best on the nine. A tree of the trial's species near
// the wizard, the trees along the cameras' line and round it taken away for these shots (put back after), shot today
// (the terrain's SpeedTree, its billboard past 160 m) and as the impostor (Near 0, so even the nearest shot is the
// impostor) from the same cameras: at 50, 120, 250 and 500 m from the ground and from 100 m up, at 8 and at noon; then
// circling it at 30 m. Then the forest as it stands, today and with the impostors past 60 m. `--shots <folder>` keeps the
// pictures; the board is made from them. Also counts the map's trees by species (every chunk read once) and times the
// reads. `--seconds 5` or less: a quick look at the lighting instead.
public class TreeImpostorsProbe : ProbeScenario
{
    const string Species = "Conifer";
    static readonly float[] Distances = { 50f, 120f, 250f, 500f };

    Vector3 tree, centre;
    Vector3 away;
    readonly List<Renderer> hidden = new();

    protected override IEnumerator Run()
    {
        var impostors = TreeImpostors.Instance;
        Check("renderer_up", impostors != null);
        if (impostors == null) yield break;
        bool wasOn = TreeImpostors.On;
        float wasNear = TreeImpostors.Near, wasFar = TreeImpostors.Far;
        yield return Switch(At(8f) + ", 'weather': { 'calmPeriods': 0, 'rampMinutes': 0.02, 'weights': " +
            "{ 'clear': 1, 'cloudy': 0, 'windy': 0, 'fog': 0, 'rain': 0, 'storm': 0, 'snow': 0 } }");
        yield return Wait(2f);

        if (Seconds <= 5f)
        {
            yield return LookDev();
            yield break;
        }
        yield return Count(impostors);
        bool found = Subject();
        Check("subject_found", found, $"tree at {tree}, shot from {away}");
        if (found)
        {
            Hide(true);
            // The lone tree: every other tree along the cameras' line and round the tree taken away for these shots.
            Clear(true);
            impostors.Forget();
            yield return LoneTree();
            Clear(false);
            impostors.Forget();
            // The forest as the trial would draw it: the full mesh within 60 m, impostors past it.
            yield return Forest();
            Hide(false);
        }
        TreeImpostors.On = wasOn;
        TreeImpostors.Near = wasNear;
        TreeImpostors.Far = wasFar;
        Check("shots_taken", Result.Screenshots.Count > 0 || string.IsNullOrEmpty(ScreenshotDir), $"{Result.Screenshots.Count} shots");
    }

    // Every chunk's trees read once, far out: the count by species, and what reading them costs and unpacks.
    IEnumerator Count(TreeImpostors impostors)
    {
        Metric("unpacked_chunks_before", World.UnpackedCount);
        Metric("read_unpacks_before", World.ReadUnpacks);
        impostors.Forget();
        TreeImpostors.On = true;
        TreeImpostors.Near = 0f;
        TreeImpostors.Far = 4000f;
        for (int n = 0; n < 600 && impostors.Held < 729; n++) yield return null;
        foreach (var (kind, count) in impostors.Totals()) Metric($"trees_{kind}", count);
        Metric("chunks_read", TreeImpostors.ChunksRead);
        Metric("read_ms_total", TreeImpostors.ReadMs);
        Metric("read_ms_a_chunk", TreeImpostors.ChunksRead > 0 ? TreeImpostors.ReadMs / TreeImpostors.ChunksRead : 0);
        Metric("unpacked_chunks", World.UnpackedCount);
        Metric("read_unpacks", World.ReadUnpacks);
        TreeImpostors.Far = 700f;
        TreeImpostors.On = false;
        yield return null;
    }

    IEnumerator LoneTree()
    {
        foreach (float hour in new[] { 8f, 12f })
        {
            yield return Switch(At(hour));
            yield return Wait(1.5f);
            string h = hour < 10f ? "morning" : "noon";
            foreach (float d in Distances)
            {
                yield return Pair($"{h}-ground-{d:0}", Ground(d), centre, 0f);
                yield return Pair($"{h}-up-{d:0}", tree + away * d + Vector3.up * 100f, centre, 0f);
            }
        }
        // Circling at 30 m, the impostor: every 45 degrees, then 3 degrees apart across one view's edge; then the mesh.
        TreeImpostors.On = true;
        TreeImpostors.Near = 0f;
        yield return Wait(0.5f);
        float baseAngle = Mathf.Atan2(away.z, away.x) * Mathf.Rad2Deg;
        for (int a = 0; a < 360; a += 45) yield return Still($"circle-{a:000}", Around(baseAngle + a, 30f), centre);
        for (int a = 0; a <= 12; a += 3) yield return Still($"circlefine-{a:00}", Around(baseAngle + a, 30f), centre);
        TreeImpostors.On = false;
        yield return Wait(0.5f);
        for (int a = 0; a < 360; a += 45) yield return Still($"circlemesh-{a:000}", Around(baseAngle + a, 30f), centre);
    }

    IEnumerator Forest()
    {
        foreach (float hour in new[] { 8f, 12f })
        {
            yield return Switch(At(hour));
            yield return Wait(1.5f);
            string h = hour < 10f ? "morning" : "noon";
            yield return Pair($"{h}-forest-ground", Ground(120f), centre, 60f);
            yield return Pair($"{h}-forest-up", tree + away * 250f + Vector3.up * 100f, centre, 60f);
        }
    }

    // The trees taken away for the lone tree's shots, to put back.
    readonly List<(TerrainData data, TreeInstance[] was)> cleared = new();

    // Takes away (or puts back) every tree but the subject within 14 m of the cameras' line out to 520 m, and within 40 m
    // of the tree: from the terrains (put back as they were) and from the impostors (Skip).
    void Clear(bool clear)
    {
        if (!clear)
        {
            foreach (var (data, was) in cleared) if (data != null) data.treeInstances = was;
            cleared.Clear();
            TreeImpostors.Skip = null;
            return;
        }
        foreach (var t in MapTerrains.Instance.Chunks)
        {
            if (t == null || t.terrainData == null) continue;
            var data = t.terrainData;
            var was = data.treeInstances;
            var kept = was.Where(i => !InTheWay(t.transform.position + Vector3.Scale(i.position, data.size))).ToArray();
            if (kept.Length == was.Length) continue;
            cleared.Add((data, was));
            data.treeInstances = kept;
        }
        TreeImpostors.Skip = InTheWay;
        Metric("cleared_terrains", cleared.Count);
    }

    bool InTheWay(Vector3 p)
    {
        var rel = new Vector3(p.x - tree.x, 0f, p.z - tree.z);
        if (rel.sqrMagnitude < 0.25f) return false;
        if (rel.sqrMagnitude < 40f * 40f) return true;
        float along = Vector3.Dot(rel, away);
        return along > 0f && along < 520f && (rel - away * along).sqrMagnitude < 14f * 14f;
    }

    // A quick look (--seconds 5 or less): one camera at noon, today and the impostor's lighting a few ways.
    IEnumerator LookDev()
    {
        bool found = Subject();
        Check("subject_found", found, $"tree at {tree}, shot from {away}");
        if (!found) yield break;
        Hide(true);
        yield return Switch(At(12f) + ", 'weather': { 'calmPeriods': 0, 'rampMinutes': 0.02, 'weights': " +
            "{ 'clear': 1, 'cloudy': 0, 'windy': 0, 'fog': 0, 'rain': 0, 'storm': 0, 'snow': 0 } }");
        yield return Wait(3f);
        // As baked, the main light alone, no shadows taken, and less wrap and light through.
        var looks = new[] { (0.5f, 0.5f, 0.7f, 0f), (0.5f, 0.5f, 0.7f, 1f), (0.5f, 0.5f, 0f, 0f), (0.3f, 0.2f, 1f, 0f) };
        var sun = RenderSettings.sun;
        Metric("sun_intensity", sun != null ? sun.intensity : -1f);
        Metric("ambient_intensity", RenderSettings.ambientIntensity);
        foreach (var (name, from) in new[] { ("ground", Ground(120f)), ("up", tree + away * 120f + Vector3.up * 100f) })
        {
            TreeImpostors.On = false;
            yield return Still($"look-{name}-today", from, centre);
            for (int n = 0; n < looks.Length; n++)
            {
                foreach (var m in Resources.LoadAll<Material>(TreeImpostors.ResourceFolder))
                {
                    m.SetFloat("_Wrap", looks[n].Item1);
                    m.SetFloat("_Translucency", looks[n].Item2);
                    m.SetFloat("_ShadowStrength", looks[n].Item3);
                    m.SetFloat("_Debug", looks[n].Item4);
                }
                TreeImpostors.On = true;
                TreeImpostors.Near = 0f;
                yield return Still($"look-{name}-{n}", from, centre);
            }
        }
        TreeImpostors.On = false;
        foreach (var m in Resources.LoadAll<Material>(TreeImpostors.ResourceFolder))
        {
            m.SetFloat("_Wrap", looks[0].Item1);
            m.SetFloat("_Translucency", looks[0].Item2);
            m.SetFloat("_ShadowStrength", looks[0].Item3);
            m.SetFloat("_Debug", 0f);
        }
        Hide(false);
    }

    // Today's tree and the impostor (the full mesh within near) from one camera.
    IEnumerator Pair(string label, Vector3 from, Vector3 at, float near)
    {
        TreeImpostors.On = false;
        yield return Still($"{label}-today", from, at);
        TreeImpostors.On = true;
        TreeImpostors.Near = near;
        yield return Still($"{label}-impostor", from, at);
        TreeImpostors.On = false;
    }

    IEnumerator Still(string label, Vector3 from, Vector3 at)
    {
        Cut(from, at);
        yield return Wait(0.6f);
        Shot(label);
        Metric($"drawn_{label}", TreeImpostors.Drawn);
        yield return null;
        yield return null;
    }

    Vector3 Ground(float d)
    {
        var p = tree + away * d;
        p.y = (MapTerrains.Instance.HeightAt(p) ?? tree.y) + 2f;
        return p;
    }

    Vector3 Around(float degrees, float d)
    {
        float r = degrees * Mathf.Deg2Rad;
        var p = tree + new Vector3(Mathf.Cos(r), 0f, Mathf.Sin(r)) * d;
        p.y = (MapTerrains.Instance.HeightAt(p) ?? tree.y) + 2f;
        return p;
    }

    // The wizard's renderers off for the shots (it stands near the tree, keeping the terrains made round it).
    void Hide(bool hide)
    {
        if (hide)
        {
            hidden.Clear();
            foreach (var r in wizard.GetComponentsInChildren<Renderer>())
                if (r.enabled)
                {
                    r.enabled = false;
                    hidden.Add(r);
                }
        }
        else
        {
            foreach (var r in hidden) if (r != null) r.enabled = true;
        }
    }

    // Nothing with a collider (a rock, a cabin, another tree's trunk) between the ground cameras and the tree's middle.
    static bool Unblocked(Vector3 at, Vector3 dir, float height)
    {
        var middle = at + Vector3.up * (14.5f * height);
        foreach (float d in Distances)
        {
            var eye = at + dir * d;
            eye.y = (MapTerrains.Instance.HeightAt(eye) ?? at.y) + 2f;
            var to = middle + (eye - middle).normalized * 8f;
            if (Physics.Linecast(eye, to)) return false;
        }
        return true;
    }

    // A tree of the species within 200 m of the wizard with the fewest trees round it, and the way to look at it from
    // with the fewest trees and no ground in the line out to 500 m.
    bool Subject()
    {
        var trees = Trees();
        var me = wizard.transform.position;
        float best = float.MaxValue;
        foreach (var c in trees)
        {
            if (!c.mine || (c.at - me).sqrMagnitude > 200f * 200f) continue;
            int near = trees.Count(o => (o.at - c.at).sqrMagnitude < 20f * 20f) - 1;
            for (int h = 0; h < 16; h++)
            {
                float r = h * Mathf.PI / 8f;
                var dir = new Vector3(Mathf.Cos(r), 0f, Mathf.Sin(r));
                if (GroundInTheWay(c.at, dir) || !Unblocked(c.at, dir, c.height)) continue;
                float score = near * 10f + InLine(trees, c.at, dir);
                if (score >= best) continue;
                best = score;
                tree = c.at;
                away = dir;
                centre = c.at + Vector3.up * (14.5f * c.height);
            }
        }
        Metric("subject_neighbours_score", best);
        return best < float.MaxValue;
    }

    // Every tree the made terrains draw: where it stands, whether it's the species, its height's scale.
    static List<(Vector3 at, bool mine, float height)> Trees()
    {
        var trees = new List<(Vector3 at, bool mine, float height)>();
        foreach (var t in MapTerrains.Instance.Chunks)
        {
            if (t == null || t.terrainData == null) continue;
            var data = t.terrainData;
            var protos = data.treePrototypes;
            for (int i = 0; i < data.treeInstanceCount; i++)
            {
                var ti = data.GetTreeInstance(i);
                var at = t.transform.position + Vector3.Scale(ti.position, data.size);
                var prefab = ti.prototypeIndex < protos.Length ? protos[ti.prototypeIndex].prefab : null;
                trees.Add((at, prefab != null && prefab.name == Species, ti.heightScale));
            }
        }
        return trees;
    }

    // How many trees stand within 6 m of the line out from a tree, 8 to 500 m along.
    static int InLine(List<(Vector3 at, bool mine, float height)> trees, Vector3 from, Vector3 dir)
    {
        int inLine = 0;
        foreach (var o in trees)
        {
            var rel = o.at - from;
            float along = Vector3.Dot(rel, dir);
            if (along >= 8f && along <= 500f && (rel - dir * along).sqrMagnitude < 6f * 6f) inLine++;
        }
        return inLine;
    }

    // Whether the ground rises into the line from 2 m over the ground 500 m out to 10 m up the tree.
    static bool GroundInTheWay(Vector3 at, Vector3 dir)
    {
        float eye = (MapTerrains.Instance.HeightAt(at + dir * 500f) ?? at.y) + 2f;
        for (float s = 20f; s <= 500f; s += 10f)
        {
            float g = MapTerrains.Instance.HeightAt(at + dir * s) ?? at.y;
            if (g > Mathf.Lerp(at.y + 10f, eye, s / 500f) - 1f) return true;
        }
        return false;
    }
}
