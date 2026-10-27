using System.Collections.Generic;
using UnityEngine;

// What stands between the camera and the local wizard, for OccluderFade: a
// few rays from the camera to the wizard's knees, chest and head find what
// has a collider (a cabin, a rock, a prop, a raised wall's blocker, a hill of
// the terrain), and the terrain's trees, whose colliders are only round their
// trunks, are checked by their crowns. Anything found whose material is
// URP's Lit is switched onto DitherLit (the same look, with the occluder
// dither), and switched back once the dither has faded out.
public sealed class Occluders
{
    // Points on the wizard the rays go to, above its feet (m); rays stop this short of them.
    static readonly float[] Heights = { 0.5f, 1.1f, 1.7f };
    const float Clearance = 0.6f;
    // The most renderers switched at once, and how often the terrain's trees are checked (s).
    const int MaxSwapped = 48;
    const float TreeCheckEvery = 0.1f;

    readonly Transform wizard;
    readonly Shader ditherLit;
    readonly RaycastHit[] hits = new RaycastHit[16];
    readonly List<MeshRenderer> found = new();
    readonly Dictionary<Material, Material> dithered = new();
    readonly Dictionary<Renderer, (Material[] own, Material first)> swapped = new();
    readonly HashSet<Renderer> plain = new();
    readonly Dictionary<TerrainData, Vector2[]> crowns = new();
    ChemGrid grid;
    float treesCheckedAt = -1f;
    bool treeInTheWay;

    public Occluders(Transform wizard, Shader ditherLit)
    {
        this.wizard = wizard;
        this.ditherLit = ditherLit;
    }

    /// <summary>How many renderers are on their dithered copies now.</summary>
    public int Swapped => swapped.Count;

    /// <summary>Whether anything stands between the eye and the wizard; Lit renderers in the way go onto DitherLit.</summary>
    public bool Look(Vector3 eye, Transform held)
    {
        var feet = wizard.position;
        bool any = false;
        foreach (float h in Heights) any |= Ray(eye, feet + Vector3.up * h, held);
        if (Time.time - treesCheckedAt >= TreeCheckEvery || Time.time < treesCheckedAt)
        {
            treesCheckedAt = Time.time;
            treeInTheWay = TreeBetween(eye, feet + Vector3.up * Heights[1]);
        }
        return any || treeInTheWay;
    }

    bool Ray(Vector3 eye, Vector3 target, Transform held)
    {
        var to = target - eye;
        float distance = to.magnitude - Clearance;
        if (distance <= 0f) return false;
        int n = Physics.RaycastNonAlloc(eye, to.normalized, hits, distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        bool any = false;
        for (int i = 0; i < n; i++)
        {
            var c = hits[i].collider;
            if (c.transform.IsChildOf(wizard) || (held != null && c.transform.IsChildOf(held))) continue;
            any = true;
            Swap(c);
        }
        return any;
    }

    // The renderers drawn for a collider: its body's, or for a raised wall's invisible blocker, the wall's crags.
    void Swap(Collider c)
    {
        if (ditherLit == null || swapped.Count >= MaxSwapped || c is TerrainCollider) return;
        GameObject drawn;
        if (CellBlockers.IsBlocker(c)) drawn = Dressing(c.bounds.center);
        else drawn = c.attachedRigidbody != null ? c.attachedRigidbody.gameObject : c.gameObject;
        if (drawn == null) return;
        drawn.GetComponentsInChildren(found);
        // A collider on its own child (a cabin's wall) is drawn by its siblings.
        if (found.Count == 0 && drawn.transform.parent != null) drawn.transform.parent.GetComponentsInChildren(found);
        foreach (var r in found)
        {
            if (swapped.Count < MaxSwapped && !swapped.ContainsKey(r) && !plain.Contains(r)) Swap(r);
        }
    }

    GameObject Dressing(Vector3 at)
    {
        if (grid == null) grid = Object.FindFirstObjectByType<ChemGrid>();
        if (grid == null) return null;
        var cell = grid.CellAt(at);
        return grid.DressingAt(cell.x, cell.y);
    }

    void Swap(MeshRenderer r)
    {
        var own = r.sharedMaterials;
        var copy = new Material[own.Length];
        bool any = false;
        for (int i = 0; i < own.Length; i++)
        {
            copy[i] = Dithered(own[i]);
            any |= copy[i] != own[i];
        }
        if (!any || copy.Length == 0)
        {
            plain.Add(r);
            return;
        }
        r.sharedMaterials = copy;
        swapped[r] = (own, copy[0]);
    }

    // The DitherLit copy of a Lit material (made once); any other material stays as it is.
    Material Dithered(Material m)
    {
        if (m == null) return null;
        if (dithered.TryGetValue(m, out var d)) return d;
        d = m;
        if (m.shader != null && m.shader.name == UrpMaterials.LitShader)
        {
            d = new Material(m) { name = m.name + " (dithered)" };
            d.shader = ditherLit;
        }
        dithered[m] = d;
        return d;
    }

    /// <summary>Puts every switched renderer back on its own materials (unless something else has changed them since: a cabin charred).</summary>
    public void Restore()
    {
        foreach (var pair in swapped)
        {
            if (pair.Key != null && pair.Key.sharedMaterial == pair.Value.first) pair.Key.sharedMaterials = pair.Value.own;
        }
        swapped.Clear();
        plain.Clear();
    }

    /// <summary>Restores everything and frees the dithered copies.</summary>
    public void Destroy()
    {
        Restore();
        foreach (var pair in dithered)
        {
            if (pair.Value != pair.Key && pair.Value != null) Object.Destroy(pair.Value);
        }
        dithered.Clear();
    }

    // A terrain tree whose crown crosses the line from the eye to the wizard.
    bool TreeBetween(Vector3 eye, Vector3 target)
    {
        foreach (var t in Terrain.activeTerrains)
        {
            var data = t.terrainData;
            var origin = t.GetPosition();
            var size = data.size;
            if (Mathf.Max(eye.x, target.x) < origin.x - 5f || Mathf.Min(eye.x, target.x) > origin.x + size.x + 5f
                || Mathf.Max(eye.z, target.z) < origin.z - 5f || Mathf.Min(eye.z, target.z) > origin.z + size.z + 5f) continue;
            if (TreeBetween(data, origin, eye, target)) return true;
        }
        return false;
    }

    bool TreeBetween(TerrainData data, Vector3 origin, Vector3 eye, Vector3 target)
    {
        var crown = Crowns(data);
        int n = data.treeInstanceCount;
        for (int i = 0; i < n; i++)
        {
            var tree = data.GetTreeInstance(i);
            if (tree.prototypeIndex < 0 || tree.prototypeIndex >= crown.Length) continue;
            var c = crown[tree.prototypeIndex];
            var foot = origin + Vector3.Scale(tree.position, data.size);
            if (Crosses(eye, target, foot, c.x * tree.widthScale, c.y * tree.heightScale)) return true;
        }
        return false;
    }

    // Each species' crown: its radius and height (m), from its model's bounds.
    Vector2[] Crowns(TerrainData data)
    {
        if (crowns.TryGetValue(data, out var c)) return c;
        var protos = data.treePrototypes;
        c = new Vector2[protos.Length];
        for (int p = 0; p < protos.Length; p++)
        {
            var mesh = protos[p].prefab != null ? protos[p].prefab.GetComponentInChildren<MeshFilter>() : null;
            var b = mesh != null && mesh.sharedMesh != null ? mesh.sharedMesh.bounds : new Bounds(new Vector3(0f, 5f, 0f), new Vector3(4f, 10f, 4f));
            c[p] = new Vector2(Mathf.Max(b.extents.x, b.extents.z) * 0.75f, b.max.y);
        }
        crowns[data] = c;
        return c;
    }

    // Whether the line passes through a crown: an upright cylinder from a little above the foot to the top.
    static bool Crosses(Vector3 eye, Vector3 target, Vector3 foot, float radius, float height)
    {
        var a = new Vector2(eye.x, eye.z);
        var ab = new Vector2(target.x, target.z) - a;
        var p = new Vector2(foot.x, foot.z);
        float len2 = ab.sqrMagnitude;
        float t = len2 > 1e-4f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2) : 0f;
        // Closest at the wizard: it stands beside or under the tree, not behind it.
        if (t > 0.98f) return false;
        if ((a + ab * t - p).sqrMagnitude > radius * radius) return false;
        float y = Mathf.Lerp(eye.y, target.y, t);
        return y > foot.y + height * 0.15f && y < foot.y + height;
    }
}
