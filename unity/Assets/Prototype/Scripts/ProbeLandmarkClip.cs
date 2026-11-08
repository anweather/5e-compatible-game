using System.Collections;
using UnityEngine;

// Throwaway (landmark-valley-look, never merged): films a walk up to each of the three landmark villages written into
// the valley for J-080 phase 1b, then a rise over it. Needs --shots <folder>: ClipFilm's frames, for ffmpeg.
public abstract class LandmarkClipProbe : CastleCliffProbe
{
    const float WalkSeconds = 17f, RiseSeconds = 10f, EyeHeight = 1.8f;

    protected abstract Vector2Int From { get; }
    protected abstract Vector2Int To { get; }
    protected abstract Vector2Int Landmark { get; }
    protected abstract float LandmarkUp { get; }
    // The rise: from where, how high, looking at what cell.
    protected abstract Vector3Int RiseFrom { get; }
    protected abstract Vector2Int RiseAt { get; }

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
        var a = grid.CellCenter(From.x, From.y);
        var b = grid.CellCenter(To.x, To.y);
        var mark = grid.CellCenter(Landmark.x, Landmark.y);
        var markAt = new Vector3(mark.x, grid.GroundAt(mark) + LandmarkUp, mark.z);
        for (float t = 0f; t < WalkSeconds; t += Time.deltaTime)
        {
            var at = Vector3.Lerp(a, b, Mathf.SmoothStep(0f, 1f, t / WalkSeconds));
            Cut(new Vector3(at.x, grid.GroundAt(at) + EyeHeight, at.z), markAt);
            yield return null;
        }
        var end = new Vector3(b.x, grid.GroundAt(b) + EyeHeight, b.z);
        var high = grid.CellCenter(RiseFrom.x, RiseFrom.y);
        high.y = grid.GroundAt(high) + RiseFrom.z;
        var look = grid.CellCenter(RiseAt.x, RiseAt.y);
        look.y = grid.GroundAt(look);
        for (float t = 0f; t < RiseSeconds; t += Time.deltaTime)
        {
            float k = Mathf.SmoothStep(0f, 1f, t / RiseSeconds);
            Cut(Vector3.Lerp(end, high, k), Vector3.Lerp(markAt, look, k));
            yield return null;
        }
        yield return film.Stop();
        Metric("frames", film.Frames);
        Metric("seconds", film.Seconds);
        Check("filmed", film.Saved >= film.Frames - 2 && film.Seconds >= WalkSeconds + RiseSeconds - 1f, $"{film.Saved}/{film.Frames} frames, {film.Seconds:0.0} s");
    }
}

// The Greatbough by Elmscot: up to the tree from the east, then up over it and its village.
public sealed class LandmarkForestClipProbe : LandmarkClipProbe
{
    protected override Vector2Int From => new(536, 236);
    protected override Vector2Int To => new(494, 230);
    protected override Vector2Int Landmark => new(470, 226);
    protected override float LandmarkUp => 20f;
    protected override Vector3Int RiseFrom => new(520, 300, 60);
    protected override Vector2Int RiseAt => new(470, 245);
}

// Mount Hearth by Emberhold: up the climbing way from the foot towards the crater.
public sealed class LandmarkVolcanoClipProbe : LandmarkClipProbe
{
    protected override Vector2Int From => new(161, 214);
    protected override Vector2Int To => new(157, 132);
    protected override Vector2Int Landmark => new(160, 106);
    protected override float LandmarkUp => 40f;
    protected override Vector3Int RiseFrom => new(215, 200, 110);
    protected override Vector2Int RiseAt => new(160, 112);
}

// The Drowned Glass by Pikestead, on the lake's east shore: from the east between the houses down to the boathouses.
public sealed class LandmarkFishingClipProbe : LandmarkClipProbe
{
    protected override Vector2Int From => new(408, 383);
    protected override Vector2Int To => new(368, 382);
    protected override Vector2Int Landmark => new(320, 385);
    protected override float LandmarkUp => 0f;
    protected override Vector3Int RiseFrom => new(410, 410, 45);
    protected override Vector2Int RiseAt => new(360, 385);
}
