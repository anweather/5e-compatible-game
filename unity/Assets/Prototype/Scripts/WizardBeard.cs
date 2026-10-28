using System.Collections.Generic;
using UnityEngine;

// The wizard's beard (WizardOutfit; Andrew: a beard, not a cone): tapered tufts
// rooted along the jaw from ear to ear, each a little different in length,
// width, curl and shade (darker at the root, lighter at the tip), overlapping
// into one mass that comes to a point, a fork or a round end by the look's cut,
// with a moustache of two curled tufts over the mouth. One skinned mesh
// (SkinnedTube): the tufts hang on three short spring chains under the chin
// (BeardChain), so it sways and lies on the chest. Every difference comes from
// the look's seed. In the head's space: the chin is about 0.08 m in front of
// the head bone and 0.045 m below it.
public static class WizardBeard
{
    const float JawY = -0.045f, JawZ = 0.08f, JawHalf = 0.088f;
    // How far either side of the chin the tufts are rooted (degrees round the jaw).
    const float Reach = 78f;

    // The look's own small draw for the tufts' differences (as WizardLook draws, same on every platform).
    sealed class Draw
    {
        uint s;
        public Draw(int seed) => s = (uint)seed * 2654435761u + 7u;

        public float Next()
        {
            s = s * 1664525u + 1013904223u;
            return s / 4294967296f;
        }

        public float Jit(float amount) => (Next() * 2f - 1f) * amount;
    }

    // The tufts so far, and where they go: built in the head's space, kept in the host's.
    sealed class Tufts
    {
        public Transform Host, Head;
        public readonly List<SkinnedTube.Ring[]> Rings = new();
        // Each tuft's chain (-1: on the head only).
        public readonly List<int> Chains = new();
    }

    /// <summary>
    /// Builds the beard on <paramref name="host"/> (the wizard, posed idle) for this look: the mesh
    /// and the springs it hangs on (with their BeardChain), keeping out of spheres round these chest
    /// bones. Null when the look has no beard.
    /// </summary>
    public static (GameObject mesh, GameObject springs) Build(WizardLook look, Transform host, Transform head, Transform[] chest, Material material)
    {
        if (head == null || look.BeardLength <= 0f) return (null, null);
        var draw = new Draw(look.BeardJitter);
        float length = LengthOf(look);
        float spread = look.BeardCut == WizardLook.BeardStyle.Full ? 0.06f : 0.035f;
        float[] chainX = { -spread, 0f, spread };
        var springs = Springs(head, look.BeardCut, length, chainX, out var roots, out var joints);

        var tufts = new Tufts { Host = host, Head = head };
        for (int i = 0; i < look.BeardTufts; i++)
            Tuft(tufts, look, length, chainX, i / (look.BeardTufts - 1f) * 2f - 1f, draw);
        Moustache(tufts, look);

        var bones = new Transform[1 + joints.Length];
        bones[0] = head;
        System.Array.Copy(joints, 0, bones, 1, joints.Length);
        var mesh = SkinnedTube.MakeMany(host, "Outfit beard", tufts.Rings, 6, bones, (_, tube, ring) => Weigh(tufts.Chains[tube], ring), material);

        var spheres = new List<(Transform, Vector3, float)>();
        foreach (var bone in chest)
            if (bone != null) spheres.Add((bone, Vector3.zero, 0.13f));
        springs.gameObject.AddComponent<BeardChain>().Init(head, roots, joints, spheres.ToArray());
        return (mesh, springs.gameObject);
    }

    static float LengthOf(WizardLook look)
    {
        float t = Mathf.InverseLerp(0.10f, 0.36f, look.BeardLength);
        return look.BeardCut switch
        {
            WizardLook.BeardStyle.Long => Mathf.Lerp(0.24f, 0.40f, t),
            WizardLook.BeardStyle.Forked => Mathf.Lerp(0.20f, 0.32f, t),
            WizardLook.BeardStyle.Full => Mathf.Lerp(0.08f, 0.13f, t),
            _ => Mathf.Lerp(0.09f, 0.16f, t),
        };
    }

    // Three chains under the chin (left, middle, right), two joints each, in the head's space; a
    // forked beard's spreading toward the tips.
    static Transform Springs(Transform head, WizardLook.BeardStyle cut, float length, float[] chainX, out Vector3[] roots, out Transform[] joints)
    {
        var springs = new GameObject("Outfit beard springs").transform;
        springs.SetParent(head, false);
        roots = new Vector3[3];
        joints = new Transform[6];
        for (int c = 0; c < 3; c++)
        {
            roots[c] = new Vector3(chainX[c] * 0.6f, JawY, JawZ - 0.005f);
            for (int k = 0; k < 2; k++)
            {
                float f = k == 0 ? 0.5f : 1f;
                var joint = new GameObject("Beard spring").transform;
                joint.SetParent(springs, false);
                float x = cut == WizardLook.BeardStyle.Forked ? chainX[c] * (1f + f) : chainX[c];
                joint.localPosition = new Vector3(x, JawY - length * f, JawZ + 0.03f - 0.01f * f);
                joints[c * 2 + k] = joint;
            }
        }
        return springs;
    }

    // One tuft, rooted at <paramref name="u"/> along the jaw (-1 the left ear, 1 the right).
    static void Tuft(Tufts tufts, WizardLook look, float length, float[] chainX, float u, Draw draw)
    {
        var cut = look.BeardCut;
        bool goatee = cut == WizardLook.BeardStyle.Goatee;
        Vector3 anchor;
        float side;
        if (goatee)
        {
            anchor = new Vector3(0.035f * u, JawY + draw.Jit(0.005f), JawZ);
            side = Mathf.Abs(u);
        }
        else
        {
            float angle = (u * Reach + draw.Jit(4f)) * Mathf.Deg2Rad;
            anchor = new Vector3(JawHalf * Mathf.Sin(angle), JawY + 0.05f * (1f - Mathf.Cos(angle)) + draw.Jit(0.005f), JawZ * Mathf.Cos(angle) + 0.005f);
            side = Mathf.Abs(Mathf.Sin(angle));
        }
        // Shorter at the sides of a long or forked beard; a full one is rounder, a goatee a little tapered.
        float own = length * (1f - Shorter(cut) * side) * (1f + draw.Jit(0.12f));
        int chain = ChainOf(cut, chainX, anchor.x, draw);
        float tipX = cut switch
        {
            WizardLook.BeardStyle.Forked => chainX[chain] * 2f + draw.Jit(0.01f),
            WizardLook.BeardStyle.Full => anchor.x * 0.7f + draw.Jit(0.01f),
            _ => anchor.x * 0.3f + draw.Jit(0.012f),
        };
        var tip = new Vector3(tipX, JawY - own, JawZ + 0.02f + draw.Jit(0.01f));
        // Bulging forward over the chin, with a little curl.
        var curl = new Vector3(draw.Jit(0.015f), 0f, draw.Jit(0.008f));
        var path = new[] { anchor, Vector3.Lerp(anchor, tip, 0.3f) + new Vector3(0f, 0f, 0.025f), Vector3.Lerp(anchor, tip, 0.65f) + new Vector3(0f, 0f, 0.015f) + curl, tip };
        float w = Mathf.Lerp(0.024f, 0.034f, draw.Next()) * (goatee ? 0.8f : 1f);
        float b = 1f + draw.Jit(0.07f);
        Add(tufts, path, new[] { w * 0.9f, w * 1.15f, w * 0.75f, 0.004f },
            new[] { Shade(look, 0.75f * b, 0f), Shade(look, 0.92f * b, 0f), Shade(look, b, 0.05f), Shade(look, b, 0.2f) }, 0.7f, chain);
    }

    static float Shorter(WizardLook.BeardStyle cut) => cut switch
    {
        WizardLook.BeardStyle.Long => 0.45f,
        WizardLook.BeardStyle.Forked => 0.35f,
        WizardLook.BeardStyle.Full => 0.25f,
        _ => 0.2f,
    };

    // The chain a tuft hangs on: a forked beard's tufts go to the fork on their side; others to the
    // chain nearest where they end.
    static int ChainOf(WizardLook.BeardStyle cut, float[] chainX, float x, Draw draw)
    {
        if (cut == WizardLook.BeardStyle.Forked) return x + draw.Jit(0.001f) < 0f ? 0 : 2;
        float towards = x * (cut == WizardLook.BeardStyle.Full ? 0.7f : 0.4f);
        int best = 0;
        for (int i = 1; i < chainX.Length; i++)
            if (Mathf.Abs(chainX[i] - towards) < Mathf.Abs(chainX[best] - towards)) best = i;
        return best;
    }

    // From under the nose, out and down round the mouth into the beard, on the head alone.
    static void Moustache(Tufts tufts, WizardLook look)
    {
        foreach (float side in new[] { -1f, 1f })
            Add(tufts,
                new[] { new Vector3(side * 0.008f, -0.015f, 0.098f), new Vector3(side * 0.035f, -0.022f, 0.097f), new Vector3(side * 0.055f, -0.04f, 0.088f), new Vector3(side * 0.06f, -0.065f, 0.082f) },
                new[] { 0.012f, 0.015f, 0.011f, 0.004f },
                new[] { Shade(look, 0.8f, 0f), Shade(look, 0.95f, 0f), Shade(look, 1f, 0.08f), Shade(look, 1f, 0.18f) }, 0.8f, -1);
    }

    // A tuft along a path in the head's space, as rings in the host's.
    static void Add(Tufts tufts, Vector3[] path, float[] radii, Color[] shades, float depth, int chain)
    {
        var rings = new SkinnedTube.Ring[path.Length];
        for (int i = 0; i < path.Length; i++)
        {
            var along = path[Mathf.Min(i + 1, path.Length - 1)] - path[Mathf.Max(i - 1, 0)];
            rings[i] = new SkinnedTube.Ring
            {
                at = tufts.Host.InverseTransformPoint(tufts.Head.TransformPoint(path[i])),
                along = tufts.Host.InverseTransformDirection(tufts.Head.TransformDirection(along)),
                half = radii[i],
                depth = depth,
                colour = shades[i],
            };
        }
        tufts.Rings.Add(rings);
        tufts.Chains.Add(chain);
    }

    // The beard's colour darkened (by < 1) or lightened, and toward white; hair doesn't glow.
    static Color Shade(WizardLook look, float by, float toWhite)
    {
        var c = Color.Lerp(look.Beard * by, Color.white, toWhite);
        return new Color(c.r, c.g, c.b, 0f);
    }

    // The root on the head; along the beard, from the head to its chain's joints.
    static BoneWeight Weigh(int chain, int ring)
    {
        if (chain < 0) return SkinnedTube.Blend(0, 0, 0f);
        int first = 1 + chain * 2;
        return ring switch
        {
            0 => SkinnedTube.Blend(0, first, 0f),
            1 => SkinnedTube.Blend(0, first, 0.35f),
            2 => SkinnedTube.Blend(first, first + 1, 0.4f),
            _ => SkinnedTube.Blend(first, first + 1, 1f),
        };
    }
}
