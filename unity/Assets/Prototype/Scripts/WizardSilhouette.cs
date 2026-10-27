using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// The local wizard seen through what can't be dithered away (the terrain's
// trees, a hill, a pack's rock), for OccluderFade: a copy of each of its
// skinned meshes on the same bones, drawn with the WizardSilhouette shader
// (a dithered see-through figure, only where something stands in front of
// it), switched on only while something occludes. One extra skinned draw.
public sealed class WizardSilhouette
{
    readonly List<SkinnedMeshRenderer> copies = new();
    readonly Material material;
    bool shown;

    public WizardSilhouette(GameObject wizard, Shader shader)
    {
        if (shader == null) return;
        material = new Material(shader) { name = "Wizard silhouette" };
        foreach (var source in wizard.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            if (source.sharedMesh == null) continue;
            var go = new GameObject(source.name + " (silhouette)");
            go.transform.SetParent(source.transform.parent, false);
            go.transform.SetLocalPositionAndRotation(source.transform.localPosition, source.transform.localRotation);
            go.transform.localScale = source.transform.localScale;
            var copy = go.AddComponent<SkinnedMeshRenderer>();
            copy.sharedMesh = source.sharedMesh;
            copy.rootBone = source.rootBone;
            copy.bones = source.bones;
            copy.localBounds = source.localBounds;
            var materials = new Material[source.sharedMesh.subMeshCount];
            for (int i = 0; i < materials.Length; i++) materials[i] = material;
            copy.sharedMaterials = materials;
            copy.shadowCastingMode = ShadowCastingMode.Off;
            copy.receiveShadows = false;
            copy.enabled = false;
            copies.Add(copy);
        }
    }

    /// <summary>Whether the figure is drawn now.</summary>
    public bool Shown => shown;

    public void Show(bool on)
    {
        if (on == shown) return;
        shown = on;
        foreach (var c in copies)
        {
            if (c != null) c.enabled = on;
        }
    }

    public void Destroy()
    {
        foreach (var c in copies)
        {
            if (c != null) Object.Destroy(c.gameObject);
        }
        copies.Clear();
        if (material != null) Object.Destroy(material);
    }
}
