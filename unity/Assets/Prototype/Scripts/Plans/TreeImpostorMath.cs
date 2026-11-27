using UnityEngine;

// The octahedral impostor's directions (docs/design/tree-impostors.md), the same as TreeImpostor.hlsl's: the baker
// (TreeImpostorBaker) points its views with them and the tests check them. View (i, j) of an N x N grid is baked from
// Decode of the grid point mapped onto [-1, 1]^2, so the grid's corners and edges lie on the horizon.
public static class TreeImpostorMath
{
    /// <summary>A direction of the upper hemisphere (y below 0 is taken as 0) as a point of [-1, 1]^2.</summary>
    public static Vector2 Encode(Vector3 d)
    {
        d.y = Mathf.Max(d.y, 0f);
        d /= Mathf.Max(Mathf.Abs(d.x) + d.y + Mathf.Abs(d.z), 1e-5f);
        return new Vector2(d.x + d.z, d.z - d.x);
    }

    /// <summary>The direction a point of [-1, 1]^2 stands for (unit length, y at least 0).</summary>
    public static Vector3 Decode(Vector2 c)
    {
        float x = (c.x - c.y) * 0.5f, z = (c.x + c.y) * 0.5f;
        return new Vector3(x, 1f - Mathf.Abs(x) - Mathf.Abs(z), z).normalized;
    }

    /// <summary>The direction view (i, j) of an N x N grid looks from (toward its camera).</summary>
    public static Vector3 FrameDir(int i, int j, int frames)
    {
        float n1 = Mathf.Max(frames - 1, 1);
        return Decode(new Vector2(i / n1 * 2f - 1f, j / n1 * 2f - 1f));
    }

    /// <summary>The up a view from direction d is drawn with: the world's, or north for a view from straight above.</summary>
    public static Vector3 Up(Vector3 d) => Mathf.Abs(d.y) > 0.999f ? Vector3.forward : Vector3.up;
}
