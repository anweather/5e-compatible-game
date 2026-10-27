using UnityEngine;

// Currents in the valley's water (docs/WORLD_LIFE.md): the river runs
// downstream along its course, fastest mid-channel; the sea's surf surges up
// the beach as each breaker comes in (in time with the Ocean shader's swell)
// and drifts gently shoreward further out. The still lake has none. ValleyBuilder
// bakes the river's line and the coast in. Anyone in the water can ask At(p):
// CurrentRider pushes the local wizard with it, PhysicsProp drifts floating
// things on it. Physics only, never the chemistry (D4): each machine pushes
// its own wizard, and the host its props.
public class WaterCurrents : MonoBehaviour
{
    public static WaterCurrents Instance { get; private set; }

    public ChemGrid grid;
    [Tooltip("The river's line from the falls down to the lake (x, z), downstream.")]
    public Vector2[] river = System.Array.Empty<Vector2>();
    public float riverHalfWidth = 4.5f;
    [Tooltip("How fast the river runs mid-channel (m/s).")]
    public float riverSpeed = 1.6f;

    [Header("Sea (match the Ocean material)")]
    public Vector2 shoreOrigin;
    public Vector2 seaward = new(0.62f, -0.78f);
    public float seaLevel = -0.6f;
    public float wavelength = 18f, waveSpeed = 4.2f;
    [Tooltip("How hard a breaker shoves up the beach (m/s at its peak), and the steady drift ashore further out.")]
    public float surge = 2.2f, drift = 0.25f;

    void OnEnable() => Instance = this;

    void OnDisable()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>The current at a point in the water (m/s, level); zero out of the water or in still water.</summary>
    public Vector3 At(Vector3 p)
    {
        if (grid == null || grid.World == null) return Vector3.zero;
        var cell = grid.CellAt(p);
        var c = grid.World.At(cell.x, cell.y);
        if (c == null || c.Water <= 0 || c.Frozen) return Vector3.zero;
        if (c.T == TerrainIds.River) return River(new Vector2(p.x, p.z));
        if (grid.valley != null && grid.valley.IsLake(c, cell.x, cell.y) && AtSea(p)) return Sea(p, cell);
        return Vector3.zero;
    }

    // Downstream along the nearest stretch of the course, slowing toward the banks.
    Vector3 River(Vector2 p)
    {
        if (river.Length < 2) return Vector3.zero;
        float best = float.MaxValue;
        Vector2 along = Vector2.zero;
        for (int i = 0; i + 1 < river.Length; i++)
        {
            var a = river[i];
            var ab = river[i + 1] - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            float d = (p - (a + ab * t)).sqrMagnitude;
            if (d >= best) continue;
            best = d;
            along = ab.normalized;
        }
        float edge = Mathf.Clamp01(Mathf.Sqrt(best) / riverHalfWidth);
        float speed = riverSpeed * (1f - 0.7f * edge * edge);
        return new Vector3(along.x, 0f, along.y) * speed;
    }

    // The sea is the water past the coast's hill: anything seaward of a line well inland of the shore.
    bool AtSea(Vector3 p) => Vector2.Dot(new Vector2(p.x, p.z) - shoreOrigin, seaward.normalized) > -60f;

    // Ashore in pulses as each breaker comes in over the shallows; a steady drift ashore further out.
    Vector3 Sea(Vector3 p, Vector2Int cell)
    {
        var toShore = -seaward.normalized;
        float ground = grid.valley != null ? grid.valley.BaseHeight(cell.x, cell.y) : seaLevel - 5f;
        float depth = seaLevel - ground;
        float k = 2f * Mathf.PI / wavelength;
        float phase = k * (Vector2.Dot(toShore, new Vector2(p.x, p.z)) - waveSpeed * Time.timeSinceLevelLoad);
        float pulse = Mathf.Max(0f, Mathf.Sin(phase - 1.2f));
        float zone = Mathf.Clamp01((4.5f - depth) / 3f);
        float speed = drift + surge * pulse * zone;
        return new Vector3(toShore.x, 0f, toShore.y) * speed;
    }
}
