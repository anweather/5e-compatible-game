using System.Collections.Generic;
using UnityEngine;

// Electric arcs: short jagged bright lines between two points, from a small pool
// of line renderers made once: Lightning leaving the hand, the charge running
// through water and metal (ChemGrid.Charge), a storm cloud striking. An arc can
// wait a moment before it shows, so a charge runs out from where it struck. Its
// line is drawn anew a few times as it plays, so it crackles, and it fades.
// Presentation only: what the charge reaches is the core's.
public class ArcFx : MonoBehaviour
{
    const int Pool = 48;
    // How far off its line an arc wanders (a share of its length, at most Wander m), how often it is redrawn.
    const float Jag = 0.14f, Wander = 0.9f, RedrawSeconds = 0.05f, MetresPerPoint = 0.7f;
    const int MostPoints = 18;
    static readonly Color Hot = new(2.2f, 2.5f, 3.4f, 1f), Cool = new(0.7f, 0.85f, 2.4f, 0.9f);

    sealed class Arc
    {
        public LineRenderer line;
        public Vector3 from, to;
        public float starts, ends, width, redraw;
    }

    static ArcFx instance;
    readonly List<Arc> arcs = new();
    Material material;
    int next;

    /// <summary>How many arcs are playing now, for the probe.</summary>
    public static int Playing { get; private set; }

    /// <summary>An arc from <paramref name="from"/> to <paramref name="to"/>, after <paramref name="delay"/> seconds, for <paramref name="seconds"/>.</summary>
    public static void Play(Vector3 from, Vector3 to, float width = 0.07f, float delay = 0f, float seconds = 0.35f)
    {
        if (instance == null) instance = new GameObject("Arcs").AddComponent<ArcFx>();
        var arc = instance.Take();
        arc.from = from;
        arc.to = to;
        arc.width = width;
        arc.starts = Time.time + delay;
        arc.ends = arc.starts + seconds;
        arc.redraw = 0f;
    }

    // A free line, or the one played longest ago.
    Arc Take()
    {
        if (arcs.Count < Pool)
        {
            var made = new Arc { line = MakeLine() };
            arcs.Add(made);
            return made;
        }
        foreach (var arc in arcs)
            if (!arc.line.enabled && Time.time >= arc.ends) return arc;
        next = (next + 1) % arcs.Count;
        return arcs[next];
    }

    LineRenderer MakeLine()
    {
        if (material == null) material = UrpMaterials.NewUnlitParticles("Arc", Color.white, additive: true);
        var line = new GameObject("Arc").AddComponent<LineRenderer>();
        line.transform.SetParent(transform, false);
        line.sharedMaterial = material;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.numCapVertices = 2;
        line.startColor = Hot;
        line.endColor = Cool;
        line.enabled = false;
        return line;
    }

    void Update()
    {
        Playing = 0;
        foreach (var arc in arcs)
        {
            bool on = Time.time >= arc.starts && Time.time < arc.ends;
            arc.line.enabled = on;
            if (!on) continue;
            Playing++;
            float k = 1f - (Time.time - arc.starts) / (arc.ends - arc.starts);
            // Two quick flickers as it fades.
            arc.line.widthMultiplier = arc.width * (0.4f + 0.6f * k) * (Mathf.Sin(Time.time * 70f) > -0.4f ? 1f : 0.45f);
            if (Time.time < arc.redraw) continue;
            arc.redraw = Time.time + RedrawSeconds;
            Jagged(arc);
        }
    }

    static void Jagged(Arc arc)
    {
        float length = Vector3.Distance(arc.from, arc.to);
        int points = Mathf.Clamp(Mathf.CeilToInt(length / MetresPerPoint) + 1, 3, MostPoints);
        arc.line.positionCount = points;
        float wander = Mathf.Min(length * Jag, Wander);
        for (int i = 0; i < points; i++)
        {
            float k = i / (points - 1f);
            var p = Vector3.Lerp(arc.from, arc.to, k);
            if (i > 0 && i < points - 1) p += Random.insideUnitSphere * (wander * (0.35f + Mathf.Sin(k * Mathf.PI) * 0.65f));
            arc.line.SetPosition(i, p);
        }
    }

    void OnDestroy()
    {
        if (material != null) Destroy(material);
        if (instance == this) instance = null;
    }
}
