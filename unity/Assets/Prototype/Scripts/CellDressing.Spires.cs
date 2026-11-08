using Chem;
using UnityEngine;

// The spire look family (rules/looks.json; Earth Spires' pillars): a cluster of
// jagged rock spires bursting out of the cell, one tall and crooked, a few lower
// ones leaning out round it and broken shards at their feet (RockSpire's meshes).
// The look's height is how high the cell stands for walking; the tallest point
// reaches two to three times that. A cell the view was told to raise
// (ChemGrid.RiseSpires) bursts up out of the ground (SpireRise) instead of
// standing there at once.
public sealed partial class CellDressing
{
    const string SpireFamily = "spire";
    const float SpireSmoothness = 0.15f;

    void DressSpires(Transform root, LookDef look, Color color, Vector3 surface, int x, int y)
    {
        var (cluster, _) = Spires(root, look.Height, color, surface, x, y, false);
        grid.RiseSpires(cluster, x, y);
    }

    /// <summary>
    /// A cluster of spires <paramref name="height"/> high at the base under a new child of <paramref name="root"/>, and how
    /// high its tallest point stands. One <paramref name="centred"/> has its tallest spire upright in the cell's middle, for
    /// a raider to ride up on (ChemGrid.SpireUnder).
    /// </summary>
    public (Transform cluster, float tip) Spires(Transform root, float height, Color color, Vector3 surface, int x, int y, bool centred)
    {
        var rng = new System.Random(x * 48611 + y * 96769 + 7);
        var cluster = new GameObject("Spires").transform;
        cluster.SetParent(root, false);
        cluster.position = surface;
        float tall = height * (2.1f + 0.8f * (float)rng.NextDouble());
        var main = centred ? Vector3.zero : new Vector3(Jitter(rng, 0.15f), 0f, Jitter(rng, 0.15f)) * CellSize;
        Spike(cluster, new SpikeAt(main, tall, CellSize * (0.55f + 0.15f * (float)rng.NextDouble()), centred ? 0f : Jitter(rng, 6f), 0f), rng, color, (x, y), 0);
        int round = 2 + rng.Next(2);
        float start = (float)rng.NextDouble() * 360f;
        for (int i = 0; i < round; i++)
        {
            float a = start + i * 360f / round + Jitter(rng, 30f);
            var off = Quaternion.Euler(0f, a, 0f) * Vector3.forward * (CellSize * (0.28f + 0.1f * (float)rng.NextDouble()));
            Spike(cluster, new SpikeAt(main + off, height * (0.8f + 0.8f * (float)rng.NextDouble()), CellSize * (0.3f + 0.15f * (float)rng.NextDouble()), 12f + 16f * (float)rng.NextDouble(), a), rng, color, (x, y), i + 1);
        }
        for (int i = 0; i < 3; i++)
        {
            float a = (float)rng.NextDouble() * 360f;
            var off = Quaternion.Euler(0f, a, 0f) * Vector3.forward * (CellSize * (0.38f + 0.1f * (float)rng.NextDouble()));
            Spike(cluster, new SpikeAt(main + off, height * (0.25f + 0.25f * (float)rng.NextDouble()), CellSize * 0.2f, 30f + 20f * (float)rng.NextDouble(), a), rng, color, (x, y), i + 5);
        }
        return (cluster, tall - 0.15f);
    }

    // Where one spire stands in its cluster, how tall and wide, and how far it leans out (degrees) which way.
    readonly struct SpikeAt
    {
        public readonly Vector3 At;
        public readonly float Tall, Wide, Lean, Way;

        public SpikeAt(Vector3 at, float tall, float wide, float lean, float way)
        {
            At = at;
            Tall = tall;
            Wide = wide;
            Lean = lean;
            Way = way;
        }
    }

    // One spire under the cluster: its base a little under the ground.
    void Spike(Transform cluster, SpikeAt s, System.Random rng, Color color, (int x, int y) cell, int n)
    {
        var go = new GameObject("Spire");
        go.transform.SetParent(cluster, false);
        go.transform.SetLocalPositionAndRotation(s.At + Vector3.down * 0.15f,
            Quaternion.Euler(0f, s.Way, 0f) * Quaternion.Euler(s.Lean, 0f, 0f) * Quaternion.Euler(0f, rng.Next(360), 0f));
        go.transform.localScale = new Vector3(s.Wide, s.Tall + 0.15f, s.Wide * (0.8f + 0.4f * (float)rng.NextDouble()));
        go.AddComponent<MeshFilter>().sharedMesh = RockSpire.Spire(rng.Next());
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = materials.Lit("Spire rock", Color.white, SpireSmoothness);
        // Each spire its own shade of the look, the low ones darker, so the cluster reads as broken rock.
        Shade(r, color * (n == 0 ? 1f : 0.82f + 0.12f * (float)rng.NextDouble()), cell.x + n * 31, cell.y + n * 17);
    }
}
