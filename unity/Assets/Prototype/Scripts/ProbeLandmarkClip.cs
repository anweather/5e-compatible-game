using System.Collections;
using System.Linq;
using UnityEngine;

// Throwaway (landmark-valley-look, never merged): films a walk up to each of the three landmark villages written into
// the valley for J-080 phase 1b, then a rise over it. Needs --shots <folder>: ClipFilm's frames, for ffmpeg.
public abstract partial class LandmarkClipProbe : CastleCliffProbe
{
    const float WalkSeconds = 17f, RiseSeconds = 10f;
    // How high the walk's eye is over the ground: a wizard's, or a little over the roofs for a village up a slope.
    protected virtual float EyeHeight => 1.8f;
    // How high over its ground the rise ends looking.
    protected virtual float RiseLookUp => 0f;

    protected abstract Vector2Int From { get; }
    protected abstract Vector2Int To { get; }
    protected abstract Vector2Int Landmark { get; }
    protected abstract float LandmarkUp { get; }
    // The rise: from where, how high, looking at what cell.
    protected abstract Vector3Int RiseFrom { get; }
    protected abstract Vector2Int RiseAt { get; }
    // A walk along these cells in place of From to To (a village's way), or null.
    protected virtual Vector2Int[] Path => null;

    protected override IEnumerator Run()
    {
        if (string.IsNullOrEmpty(ScreenshotDir))
        {
            Check("needs_shots", false, "films into --shots <folder>");
            yield break;
        }
        yield return Switch(At(10.5f));
        PlaceCell(From.x - 10, From.y);
        yield return Wait(3f);
        var film = new ClipFilm(this, ScreenshotDir);
        film.Roll();
        var points = (Path ?? new[] { From, To }).Select(c => grid.CellCenter(c.x, c.y)).ToList();
        float length = 0f;
        for (int i = 1; i < points.Count; i++) length += Vector3.Distance(points[i - 1], points[i]);
        var b = points[points.Count - 1];
        var mark = grid.CellCenter(Landmark.x, Landmark.y);
        var markAt = new Vector3(mark.x, grid.GroundAt(mark) + LandmarkUp, mark.z);
        for (float t = 0f; t < WalkSeconds; t += Time.deltaTime)
        {
            var at = Along(points, Mathf.SmoothStep(0f, 1f, t / WalkSeconds) * length);
            Cut(new Vector3(at.x, grid.GroundAt(at) + EyeHeight, at.z), markAt);
            yield return null;
        }
        var end = new Vector3(b.x, grid.GroundAt(b) + EyeHeight, b.z);
        var high = grid.CellCenter(RiseFrom.x, RiseFrom.y);
        high.y = grid.GroundAt(high) + RiseFrom.z;
        var look = grid.CellCenter(RiseAt.x, RiseAt.y);
        look.y = grid.GroundAt(look) + RiseLookUp;
        for (float t = 0f; t < RiseSeconds; t += Time.deltaTime)
        {
            float k = Mathf.SmoothStep(0f, 1f, t / RiseSeconds);
            Cut(Vector3.Lerp(end, high, k), Vector3.Lerp(markAt, look, k));
            yield return null;
        }
        yield return film.Stop();
        var great = GameObject.Find("Great tree");
        Metric("great_tree", great != null ? 1 : 0);
        if (great != null)
        {
            var bounds = great.GetComponentsInChildren<Renderer>().Select(r => r.bounds).Aggregate((x, y) => { x.Encapsulate(y); return x; });
            Metric("great_tree_height_m", bounds.size.y);
            Metric("great_tree_width_m", bounds.size.x);
            Metric("great_tree_x", great.transform.position.x);
            Metric("great_tree_z", great.transform.position.z);
            var rs = great.GetComponentsInChildren<Renderer>(true);
            Metric("great_tree_active", great.activeInHierarchy ? 1 : 0);
            Metric("great_tree_renderers", rs.Length);
            Metric("great_tree_renderers_on", rs.Count(r => r.enabled && r.gameObject.activeInHierarchy));
            Metric("great_tree_lod", great.GetComponentInChildren<LODGroup>() != null ? 1 : 0);
            Metric("great_tree_y", great.transform.position.y);
            Metric("great_tree_scale", great.transform.lossyScale.y);
            Debug.Log("GREAT TREE " + string.Join(", ", rs.Select(r => r.name + ":" + r.GetType().Name + ":" + (r.sharedMaterial != null ? r.sharedMaterial.shader.name : "none") + ":" + r.isVisible)));
        }
        Metric("frames", film.Frames);
        Metric("seconds", film.Seconds);
        Check("filmed", film.Saved >= film.Frames - 2 && film.Seconds >= WalkSeconds + RiseSeconds - 1f, $"{film.Saved}/{film.Frames} frames, {film.Seconds:0.0} s");
    }
}

public abstract partial class LandmarkClipProbe
{
    // The point this far along a line of points, held at its end.
    static Vector3 Along(System.Collections.Generic.List<Vector3> points, float s)
    {
        for (int i = 1; i < points.Count; i++)
        {
            float leg = Vector3.Distance(points[i - 1], points[i]);
            if (s <= leg) return Vector3.Lerp(points[i - 1], points[i], s / leg);
            s -= leg;
        }
        return points[points.Count - 1];
    }
}

// The Greatbough by Elmscot: up to the tree from the east, then up over it and its village.
public sealed class LandmarkForestClipProbe : LandmarkClipProbe
{
    protected override Vector2Int From => new(536, 236);
    protected override Vector2Int To => new(494, 230);
    protected override Vector2Int Landmark => new(470, 226);
    protected override float LandmarkUp => 20f;
    protected override Vector3Int RiseFrom => new(560, 262, 28);
    protected override Vector2Int RiseAt => new(470, 226);
    protected override float RiseLookUp => 28f;
}

// Mount Hearth by Emberhold: out of the village on its shelf at the cone's foot and up the trail's switchbacks to the lookout.
public sealed class LandmarkVolcanoClipProbe : LandmarkClipProbe
{
    protected override Vector2Int From => new(156, 196);
    protected override Vector2Int To => new(143, 119);
    protected override Vector2Int Landmark => new(160, 106);
    protected override float LandmarkUp => 4f;
    protected override float EyeHeight => 6f;
    protected override Vector3Int RiseFrom => new(190, 228, 55);
    protected override Vector2Int RiseAt => new(150, 165);
    protected override Vector2Int[] Path => new Vector2Int[]
    {
        new(156, 196), new(159, 178), new(136, 170), new(117, 155), new(106, 135), new(122, 150), new(141, 157), new(159, 158),
        new(143, 150), new(131, 139), new(125, 125), new(135, 134), new(148, 137), new(160, 137), new(151, 131), new(145, 124), new(143, 119),
    };
}

// The Drowned Glass by Pikestead, on the lake's east shore: from the east between the houses down to the boathouses.
public sealed class LandmarkFishingClipProbe : LandmarkClipProbe
{
    protected override Vector2Int From => new(361, 353);
    protected override Vector2Int To => new(358, 419);
    protected override Vector2Int Landmark => new(300, 385);
    protected override float LandmarkUp => 1f;
    protected override Vector3Int RiseFrom => new(405, 440, 35);
    protected override Vector2Int RiseAt => new(362, 385);
    protected override Vector2Int[] Path => new Vector2Int[] { new(361, 353), new(367, 361), new(371, 369), new(374, 378), new(374, 386), new(373, 396), new(370, 404), new(365, 412), new(358, 419) };
}
