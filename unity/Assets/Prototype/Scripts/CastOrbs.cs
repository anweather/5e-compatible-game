using UnityEngine;

// The loaded elements in the wizard's hands (docs/SPELL_COMBOS.md): a small
// glowing orb in each, built from two simple spheres, and the slam as a cast goes
// off: they rush together in front of the wizard and pop. Only the show; the
// core says what's loaded (SpellCaster.Combos).
public sealed class CastOrbs
{
    const float Size = 0.16f, SlamSeconds = 0.16f, SlamReach = 0.45f, Glow = 0.9f;

    readonly Transform owner;
    readonly CastPose pose;
    readonly Renderer[] orbs = new Renderer[2];
    readonly Material[] materials = new Material[2];
    readonly string[] showing = new string[2];
    float slamAt = -1f;

    public CastOrbs(Transform owner, CastPose pose)
    {
        this.owner = owner;
        this.pose = pose;
        for (int i = 0; i < 2; i++)
        {
            var orb = Primitives.Visual(PrimitiveType.Sphere, "Cast orb");
            orb.transform.SetParent(owner, false);
            orb.transform.localScale = Vector3.one * Size;
            orbs[i] = orb.GetComponent<Renderer>();
            orbs[i].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            materials[i] = UrpMaterials.NewLit("Cast orb", Color.white, emission: Color.white);
            orbs[i].sharedMaterial = materials[i];
            orb.SetActive(false);
        }
    }

    /// <summary>The orbs slam together now.</summary>
    public void Slam() => slamAt = Time.time;

    /// <summary>Each frame: what's loaded (null for an empty hand), placed in the hands, or the slam.</summary>
    public void Show(string first, string second)
    {
        if (pose == null) return;
        var (right, left) = (pose.HandPosition, pose.LeftHandPosition);
        float slam = slamAt < 0f ? 1f : (Time.time - slamAt) / SlamSeconds;
        if (slam < 1f)
        {
            var middle = (right + left) / 2f + owner.forward * SlamReach;
            Place(0, Vector3.Lerp(right, middle, slam), 1f + slam);
            Place(1, Vector3.Lerp(left, middle, slam), 1f + slam);
            return;
        }
        slamAt = -1f;
        Hold(0, first, right);
        Hold(1, second, left);
    }

    void Hold(int i, string element, Vector3 at)
    {
        orbs[i].gameObject.SetActive(element != null);
        if (element == null)
        {
            showing[i] = null;
            return;
        }
        if (element != showing[i])
        {
            showing[i] = element;
            var color = HudDraw.ElementColor(element);
            UrpMaterials.Configure(materials[i], color, null, color * Glow);
        }
        Place(i, at, 1f);
    }

    void Place(int i, Vector3 at, float scale)
    {
        orbs[i].transform.position = at;
        orbs[i].transform.localScale = Vector3.one * (Size * scale);
    }

    public void Destroy()
    {
        for (int i = 0; i < 2; i++)
        {
            if (orbs[i] != null) Object.Destroy(orbs[i].gameObject);
            Object.Destroy(materials[i]);
        }
    }
}
