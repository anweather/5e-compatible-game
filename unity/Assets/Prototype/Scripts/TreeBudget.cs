using System.Collections.Generic;

// The tree budget (docs/GLOSSARY.md): how many trees TreeImpostors draws as their prefab and as impostors round the camera
// at most, so what the trees cost follows how many are drawn, not how many stand. Where the forest is dense (the Wide
// World's temperate band: ~5,000 trees within 150 m, against the nine's ~530) the prefabs and the impostors draw to a
// shorter distance, never nearer than a floor; where it's sparse they draw as far as ever. The forest itself is as
// generated: only how far out each way of drawing it goes changes. Pure, for the tests.
public static class TreeBudget
{
    /// <summary>The most trees that stand as their prefab: the nine's densest views stand ~530.</summary>
    public const int MostNear = 1000;

    /// <summary>Nearer than this (m) a tree is always its prefab: in a dense wood the trees in front hide the swap.</summary>
    public const float NearFloor = 60f;

    /// <summary>The most trees drawn round the camera, every way (prefabs and impostors, in sight or not).</summary>
    public const int MostAround = 40000;

    /// <summary>Out to this (m) the impostors always draw: the drawn tier and some.</summary>
    public const float FarFloor = 400f;

    /// <summary>
    /// How near the prefabs draw (m): <paramref name="near"/>, or nearer where more than <paramref name="most"/> trees stand
    /// within it (the distance of the most-th nearest), not under <paramref name="floor"/>. <paramref name="d2"/> is the
    /// squared distance of each tree within <paramref name="near"/> and some; it's reordered.
    /// </summary>
    public static float Near(List<float> d2, float near, int most = MostNear, float floor = NearFloor)
    {
        if (d2 == null || d2.Count <= most || most < 1) return near;
        float at = (float)System.Math.Sqrt(Nth(d2, most - 1));
        return System.Math.Min(near, System.Math.Max(floor, at));
    }

    /// <summary>
    /// How far the impostors draw (m): <paramref name="far"/>, or nearer where more than <paramref name="most"/> trees stand
    /// within it, counted a chunk at a time from the nearest (<paramref name="chunks"/>: each chunk's squared distance to its
    /// nearest point and its trees; reordered), not under <paramref name="floor"/> (nor past <paramref name="far"/>).
    /// </summary>
    public static float Far(List<(float d2, int trees)> chunks, float far, int most = MostAround, float floor = FarFloor)
    {
        if (chunks == null || chunks.Count == 0) return far;
        int total = 0;
        foreach (var c in chunks) total += c.trees;
        if (total <= most) return far;
        chunks.Sort((a, b) => a.d2.CompareTo(b.d2));
        int sum = 0;
        foreach (var (d2, trees) in chunks)
        {
            sum += trees;
            if (sum > most) return System.Math.Min(far, System.Math.Max(floor, (float)System.Math.Sqrt(d2)));
        }
        return far;
    }

    // The k-th smallest (from 0), by quickselect: the list is reordered.
    static float Nth(List<float> a, int k)
    {
        int lo = 0, hi = a.Count - 1;
        while (lo < hi)
        {
            float pivot = a[(lo + hi) >> 1];
            int i = lo, j = hi;
            while (i <= j)
            {
                while (a[i] < pivot) i++;
                while (a[j] > pivot) j--;
                if (i > j) break;
                (a[i], a[j]) = (a[j], a[i]);
                i++;
                j--;
            }
            if (k <= j) hi = j;
            else if (k >= i) lo = i;
            else return a[k];
        }
        return a[k];
    }
}
