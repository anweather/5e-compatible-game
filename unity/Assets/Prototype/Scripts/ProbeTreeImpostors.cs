using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// The tree impostors' board (docs/design/tree-impostors.md, J-090, J-091), best on the nine. First the far trees' reads:
// every chunk's trees read once (the count by species, what reading them costs and unpacks), a tree in a far packed chunk
// felled and the chunk packed again, which must not show (#213), and the pinned valley's far trees against its terrains'
// (they mustn't move as a terrain is made). Then for each species, a tree of it near the wizard, the trees along the
// cameras' line and round it taken away for these shots (put back after), shot with the impostors off (the terrain's
// SpeedTree, its billboard past 160 m) and as the impostor (Near 0, so even the nearest shot is the impostor) from the same
// cameras: at 50, 120, 250 and 500 m from the ground and from 100 m up, at 8 and at noon. Then circling the first one at
// 30 m as its prefab and as the impostor, and its prefab in a windy stretch, a still every quarter second (the wind). Then
// the forest as it stands, off against on (prefabs within 60 m, impostors past it). `--shots <folder>` keeps the pictures;
// the board is made from them. `--seconds 5` or less: a quick look at the lighting instead.
public class TreeImpostorsProbe : ProbeScenario
{
    static readonly string[] Species = { "Conifer", "Cypress" };
    static readonly float[] Distances = { 50f, 120f, 250f, 500f };

    Vector3 tree, centre;
    Vector3 away;
    readonly List<Renderer> hidden = new();

    protected override IEnumerator Run()
    {
        var impostors = TreeImpostors.Instance;
        Check("renderer_up", impostors != null);
        if (impostors == null) yield break;
        Check("on_by_default", TreeImpostors.On, "TreeImpostors.On as play starts");
        bool wasOn = TreeImpostors.On;
        float wasNear = TreeImpostors.Near, wasFar = TreeImpostors.Far;
        yield return Switch(At(8f) + Calm);
        yield return Wait(2f);

        if (Seconds <= 5f)
        {
            yield return LookDev();
            yield break;
        }
        yield return Count(impostors);
        yield return Felled(impostors);
        yield return PinnedTrees(impostors);
        bool first = true;
        foreach (var species in Species)
        {
            bool found = Subject(species);
            Check($"subject_found_{species}", found, $"tree at {tree}, shot from {away}");
            if (!found) continue;
            Hide(true);
            // The lone tree: every other tree along the cameras' line and round the tree taken away for these shots.
            Clear(true);
            impostors.Forget();
            yield return LoneTree(species, first);
            Clear(false);
            impostors.Forget();
            if (first) yield return Forest();
            first = false;
            Hide(false);
        }
        TreeImpostors.On = wasOn;
        TreeImpostors.Near = wasNear;
        TreeImpostors.Far = wasFar;
        Check("shots_taken", Result.Screenshots.Count > 0 || string.IsNullOrEmpty(ScreenshotDir), $"{Result.Screenshots.Count} shots");
    }

    const string Calm = ", 'weather': { 'calmPeriods': 0, 'rampMinutes': 0.02, 'weights': " +
        "{ 'clear': 1, 'cloudy': 0, 'windy': 0, 'fog': 0, 'rain': 0, 'storm': 0, 'snow': 0 } }";
    const string Windy = ", 'weather': { 'calmPeriods': 0, 'rampMinutes': 0.02, 'weights': " +
        "{ 'clear': 0, 'cloudy': 0, 'windy': 1, 'fog': 0, 'rain': 0, 'storm': 0, 'snow': 0 } }";

    // A tree in a far chunk with no terrain (none made out there) felled, its chunk packed again: once its trees are read
    // again it's gone, read from the packed chunk's changes without unpacking it (#213).
    IEnumerator Felled(TreeImpostors impostors)
    {
        TreeImpostors.On = true;
        TreeImpostors.Near = 0f;
        TreeImpostors.Far = 700f;
        for (int n = 0; n < 300 && impostors.Held < 100; n++) yield return null;
        var layout = MapLand.Current.Layout;
        var me = wizard.transform.position;
        var spot = impostors.FarTree(me, 380f, 600f);
        Check("felled_tree_found", spot.HasValue, "a tree 380 to 600 m out, in a chunk with no terrain");
        if (!spot.HasValue) yield break;
        var (x, y) = layout.CellAt(spot.Value.x, spot.Value.z);
        int cx = x >> Chem.Chunks.Shift, cy = y >> Chem.Chunks.Shift;
        Check("felled_chunk_inactive", !World.IsActive(cx, cy), $"chunk ({cx}, {cy})");
        if (World.IsActive(cx, cy)) yield break;
        Check("felled_cell_is_tree", World.At(x, y).T == TerrainIds.Tree, $"cell ({x}, {y})");
        World.At(x, y).T = TerrainIds.Stump;
        World.Pack(cx, cy);
        Metric("felled_chunk_changes", World.PackedOf(cx, cy)?.Count ?? -1);
        int unpacks = World.Unpacks;
        impostors.Forget();
        for (int n = 0; n < 300 && impostors.Held < 100; n++) yield return null;
        Check("felled_gone", !impostors.TreeNear(spot.Value, 0.05f, out _), $"no tree at {spot.Value} once its chunk is read again");
        Check("felled_not_unpacked", World.IsPacked(cx, cy) && World.Unpacks == unpacks, $"unpacks {World.Unpacks - unpacks}");
        Metric("changed_chunks_read", TreeImpostors.ChangedRead);
        World.At(x, y).T = TerrainIds.Tree;
        World.Pack(cx, cy);
        impostors.Forget();
        yield return null;
    }

    // The pinned valley's chunks: each one with a terrain made, its trees as the pinned ground has them (what a far one
    // draws before its terrain is made) against the terrain's. Every terrain tree should be there, of the same species.
    IEnumerator PinnedTrees(TreeImpostors impostors)
    {
        yield return null;
        var chunks = MapTerrains.Instance.Chunks;
        int compared = 0, terrainTrees = 0, matched = 0;
        for (int k = 0; k < chunks.Length; k++)
        {
            if (!impostors.PinnedAgainstTerrain(k, out int t, out int m)) continue;
            compared++;
            terrainTrees += t;
            matched += m;
        }
        Metric("pinned_chunks_compared", compared);
        Metric("pinned_terrain_trees", terrainTrees);
        Metric("pinned_trees_matched", matched);
        if (compared > 0) Check("pinned_trees_stay", matched == terrainTrees, $"{matched} of {terrainTrees} terrain trees in {compared} pinned chunks are where the far read put them");
        Metric("pinned_chunks_read", TreeImpostors.PinnedRead);
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

    IEnumerator LoneTree(string species, bool circle)
    {
        foreach (float hour in new[] { 8f, 12f })
        {
            yield return Switch(At(hour) + Calm);
            yield return Wait(1.5f);
            string h = hour < 10f ? "morning" : "noon";
            foreach (float d in Distances)
            {
                yield return Pair($"{species}-{h}-ground-{d:0}", Ground(d), centre, 0f);
                // Within Near, the tree as its prefab too: what stands against the impostor at the 60 m join.
                if (d < 60f) yield return Prefab($"{species}-{h}-ground-{d:0}", Ground(d), centre);
                yield return Pair($"{species}-{h}-up-{d:0}", tree + away * d + Vector3.up * 100f, centre, 0f);
            }
        }
        if (!circle) yield break;
        // Circling at 30 m: the prefab every 45 degrees, then the impostor.
        float baseAngle = Mathf.Atan2(away.z, away.x) * Mathf.Rad2Deg;
        TreeImpostors.On = true;
        TreeImpostors.Near = 60f;
        yield return Wait(0.5f);
        Metric("near_standing", TreeImpostors.NearDrawn);
        for (int a = 0; a < 360; a += 45) yield return Still($"circlemesh-{a:000}", Around(baseAngle + a, 30f), centre);
        TreeImpostors.Near = 0f;
        yield return Wait(0.5f);
        for (int a = 0; a < 360; a += 45) yield return Still($"circle-{a:000}", Around(baseAngle + a, 30f), centre);
        // The wind: a windy stretch, the prefab from one place, a still every quarter second.
        TreeImpostors.Near = 60f;
        yield return Switch(At(12f) + Windy);
        yield return Wait(8f);
        Metric("wind_main", TreeWind.Instance != null ? TreeWind.Instance.Main : -1f);
        Cut(Around(baseAngle + 90f, 30f), centre);
        yield return Wait(0.5f);
        for (int n = 0; n < 16; n++)
        {
            Shot($"wind-{n:00}");
            yield return Wait(0.25f);
        }
        yield return Switch(At(12f) + Calm);
    }

    IEnumerator Forest()
    {
        foreach (float hour in new[] { 8f, 12f })
        {
            yield return Switch(At(hour) + Calm);
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
        bool found = Subject(Species[0]);
        Check("subject_found", found, $"tree at {tree}, shot from {away}");
        if (!found) yield break;
        Hide(true);
        yield return Switch(At(12f) + Calm);
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

    // The tree as its prefab (on, as play has it) from one camera.
    IEnumerator Prefab(string label, Vector3 from, Vector3 at)
    {
        TreeImpostors.On = true;
        TreeImpostors.Near = 60f;
        yield return Still($"{label}-prefab", from, at);
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

    // The wizard's renderers off for the shots (it stands near the tree, keeping the terrains made round it), and the
    // generated props' (the coast's palms have no collider, so the cameras' line can't see them to keep clear of them).
    void Hide(bool hide)
    {
        if (hide)
        {
            hidden.Clear();
            var props = FindObjectsByType<GeneratedProp>(FindObjectsSortMode.None).SelectMany(p => p.GetComponentsInChildren<Renderer>());
            foreach (var r in wizard.GetComponentsInChildren<Renderer>().Concat(props))
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

    // A tree of a species within 320 m of the wizard with the fewest trees round it, and the way to look at it from
    // with the fewest trees and no ground in the line out to 500 m.
    bool Subject(string species)
    {
        var trees = Trees(species);
        var me = wizard.transform.position;
        float best = float.MaxValue;
        foreach (var c in trees)
        {
            if (!c.mine || (c.at - me).sqrMagnitude > 320f * 320f) continue;
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
    static List<(Vector3 at, bool mine, float height)> Trees(string species)
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
                trees.Add((at, prefab != null && prefab.name == species, ti.heightScale));
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

    // Whether the ground rises into the line from 2 m over the ground 500 m out to 10 m up the tree, or the line runs out
    // over water (a camera there would be under the sea, or shoot across it at the coast's palms).
    static bool GroundInTheWay(Vector3 at, Vector3 dir)
    {
        float eye = (MapTerrains.Instance.HeightAt(at + dir * 500f) ?? at.y) + 2f;
        float water = MapLand.Current != null ? MapLand.Current.WaterLevel : float.MinValue;
        for (float s = 20f; s <= 500f; s += 10f)
        {
            float g = MapTerrains.Instance.HeightAt(at + dir * s) ?? at.y;
            if (g > Mathf.Lerp(at.y + 10f, eye, s / 500f) - 1f || g < water + 1f) return true;
        }
        return false;
    }
}
