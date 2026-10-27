using UnityEngine;

// The ambient creatures' meshes (AmbientLife, D12), built with MeshKit, a few
// frames each so they can flap, scuttle and swim by swapping frames: a gull
// (wings up, level and down), a beach crab (legs one way and the other) and a
// fish (tail left and right). Each faces +z, its middle at the origin.
public static class CreatureMesher
{
    static Color Rgb(float r, float g, float b) => new(r, g, b, 0f);

    /// <summary>A gull with its wings at <paramref name="flap"/> (-1 down, 0 level, 1 up).</summary>
    public static Mesh Gull(float flap)
    {
        var kit = new MeshKit { Occlusion = 0f };
        var white = Rgb(0.95f, 0.95f, 0.93f);
        var grey = Rgb(0.62f, 0.65f, 0.7f);
        var tip = Rgb(0.15f, 0.15f, 0.17f);
        kit.Cylinder(new Vector3(0f, 0f, -0.28f), new Vector3(0f, 0.02f, 0.22f), 0.075f, 6, white);
        kit.Cone(new Vector3(0f, 0.02f, 0.22f), 0.07f, 0.01f, 6, white);
        kit.Box(new Vector3(0f, 0.04f, 0.3f), new Vector3(0.1f, 0.1f, 0.1f), white);
        kit.Box(new Vector3(0f, 0.03f, 0.39f), new Vector3(0.025f, 0.025f, 0.09f), Rgb(0.95f, 0.75f, 0.2f));
        kit.Box(new Vector3(0f, 0f, -0.36f), new Vector3(0.12f, 0.02f, 0.12f), white);
        // Each wing in two parts, bending at the wrist; flap raises or lowers the whole wing.
        for (int side = -1; side <= 1; side += 2)
        {
            float lift = flap * 0.35f;
            var shoulder = new Vector3(side * 0.06f, 0.03f, 0.05f);
            var wrist = shoulder + new Vector3(side * 0.42f, lift, -0.04f);
            var end = wrist + new Vector3(side * 0.38f, lift * 0.8f - 0.03f, -0.12f);
            kit.Sheet(shoulder + Vector3.forward * 0.1f, wrist + Vector3.forward * 0.06f, wrist + Vector3.back * 0.12f, shoulder + Vector3.back * 0.14f, grey);
            kit.Sheet(wrist + Vector3.forward * 0.06f, end, end + Vector3.back * 0.06f, wrist + Vector3.back * 0.12f, tip);
        }
        return kit.ToMesh("Gull");
    }

    /// <summary>A crab with its legs in step <paramref name="step"/> (0 or 1).</summary>
    public static Mesh Crab(int step)
    {
        var kit = new MeshKit { Occlusion = 0f };
        var shell = Rgb(0.85f, 0.35f, 0.2f);
        var leg = Rgb(0.7f, 0.28f, 0.16f);
        kit.Box(new Vector3(0f, 0.05f, 0f), new Vector3(0.18f, 0.06f, 0.13f), shell);
        kit.Box(new Vector3(0f, 0.085f, 0f), new Vector3(0.13f, 0.02f, 0.09f), shell * 1.1f);
        for (int side = -1; side <= 1; side += 2)
        {
            // Eyes on stalks, and a claw each side out front.
            kit.Box(new Vector3(side * 0.03f, 0.11f, 0.06f), new Vector3(0.015f, 0.04f, 0.015f), Rgb(0.1f, 0.1f, 0.1f));
            kit.Box(new Vector3(side * 0.11f, 0.05f, 0.11f), new Vector3(0.06f, 0.04f, 0.07f), shell);
            for (int k = 0; k < 3; k++)
            {
                float z = -0.04f + k * 0.04f;
                float swing = ((k + step) % 2 == 0 ? 1f : -1f) * 0.02f;
                kit.Box(new Vector3(side * 0.14f, 0.035f, z + swing), new Vector3(0.1f, 0.015f, 0.015f), leg, Quaternion.Euler(0f, 0f, side * -35f));
            }
        }
        return kit.ToMesh("Crab");
    }

    /// <summary>A small fish with its tail swung to <paramref name="swing"/> (-1 to 1).</summary>
    public static Mesh Fish(float swing, Color body)
    {
        var kit = new MeshKit { Occlusion = 0f };
        var belly = new Color(Mathf.Min(1f, body.r + 0.3f), Mathf.Min(1f, body.g + 0.3f), Mathf.Min(1f, body.b + 0.3f), 0f);
        kit.Cylinder(new Vector3(0f, 0f, -0.12f), new Vector3(0f, 0f, 0.1f), 0.045f, 6, body);
        kit.Cone(new Vector3(0f, 0f, 0.1f), 0.045f, 0.001f, 6, body);
        kit.Box(new Vector3(0f, -0.02f, 0f), new Vector3(0.05f, 0.02f, 0.16f), belly);
        var tailRoot = new Vector3(0f, 0f, -0.13f);
        var sway = new Vector3(swing * 0.05f, 0f, 0f);
        kit.Tri(tailRoot, tailRoot + new Vector3(0f, 0.06f, -0.09f) + sway, tailRoot + new Vector3(0f, -0.06f, -0.09f) + sway, body * 0.85f, Vector3.right);
        kit.Tri(tailRoot, tailRoot + new Vector3(0f, 0.06f, -0.09f) + sway, tailRoot + new Vector3(0f, -0.06f, -0.09f) + sway, body * 0.85f, Vector3.left);
        return kit.ToMesh("Fish");
    }
}
