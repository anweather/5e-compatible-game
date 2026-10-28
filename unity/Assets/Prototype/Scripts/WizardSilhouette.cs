using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// The local wizard seen through what can't be dithered away (the terrain's
// trees, a hill, a pack's rock), for OccluderFade: a copy of each of its
// meshes, the body and the whole outfit (hat, robe, cloth, beard, staff:
// Andrew saw only the robot through the trees), drawn with the
// WizardSilhouette shader (a dithered see-through figure, only where
// something stands in front of it), switched on only while something
// occludes. Skinned meshes are copied on the same bones; plain ones ride on
// their own part and share its mesh, so the robe's cloth moves in the copy
// too. The copies are made afresh each time it's shown, since the outfit
// changes with the look.
public sealed class WizardSilhouette
{
    const string Suffix = " (silhouette)";

    readonly GameObject wizard;
    readonly List<Renderer> copies = new();
    readonly Material material;
    bool shown;

    public WizardSilhouette(GameObject wizard, Shader shader)
    {
        this.wizard = wizard;
        if (shader == null) return;
        material = new Material(shader) { name = "Wizard silhouette" };
    }

    /// <summary>Whether the figure is drawn now.</summary>
    public bool Shown => shown;

    public void Show(bool on)
    {
        if (on == shown) return;
        shown = on;
        Clear();
        if (on && material != null) Copy();
    }

    void Copy()
    {
        foreach (var source in wizard.GetComponentsInChildren<Renderer>())
        {
            if (!source.enabled || source.name.EndsWith(Suffix, System.StringComparison.Ordinal)) continue;
            if (source is SkinnedMeshRenderer skinned) CopySkinned(skinned);
            else if (source is MeshRenderer && source.TryGetComponent<MeshFilter>(out var filter) && filter.sharedMesh != null) CopyPlain(source, filter.sharedMesh);
        }
    }

    void CopySkinned(SkinnedMeshRenderer source)
    {
        if (source.sharedMesh == null) return;
        var go = new GameObject(source.name + Suffix);
        go.transform.SetParent(source.transform.parent, false);
        go.transform.SetLocalPositionAndRotation(source.transform.localPosition, source.transform.localRotation);
        go.transform.localScale = source.transform.localScale;
        var copy = go.AddComponent<SkinnedMeshRenderer>();
        copy.sharedMesh = source.sharedMesh;
        copy.rootBone = source.rootBone;
        copy.bones = source.bones;
        copy.localBounds = source.localBounds;
        Finish(copy, source.sharedMesh.subMeshCount);
    }

    void CopyPlain(Renderer source, Mesh mesh)
    {
        var go = new GameObject(source.name + Suffix);
        go.transform.SetParent(source.transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        Finish(go.AddComponent<MeshRenderer>(), mesh.subMeshCount);
    }

    void Finish(Renderer copy, int subMeshes)
    {
        var materials = new Material[subMeshes];
        for (int i = 0; i < materials.Length; i++) materials[i] = material;
        copy.sharedMaterials = materials;
        copy.shadowCastingMode = ShadowCastingMode.Off;
        copy.receiveShadows = false;
        copies.Add(copy);
    }

    void Clear()
    {
        foreach (var c in copies)
        {
            if (c != null) Object.Destroy(c.gameObject);
        }
        copies.Clear();
    }

    public void Destroy()
    {
        Clear();
        if (material != null) Object.Destroy(material);
    }
}
