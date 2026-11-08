using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Chem;
using UnityEngine;

// The probe's cliffclip scenario: films the castle cliff from the town, then a walk at eye height down the canyon's path
// round its dog-leg (castlecliff checks them). Needs --shots <folder>: ClipFilm's frames and sound.wav, for ffmpeg.
public class CastleCliffClipProbe : CastleCliffProbe
{
    const float TownSeconds = 8f, WalkSeconds = 18f, EyeHeight = 2.4f, LookAhead = 9f;

    protected override IEnumerator Run()
    {
        if (string.IsNullOrEmpty(ScreenshotDir))
        {
            Check("needs_shots", false, "cliffclip films into --shots <folder>");
            yield break;
        }
        yield return Switch(At(10.5f));
        PlaceCell(222, 330);
        yield return Wait(3f);
        var film = new ClipFilm(this, ScreenshotDir);
        film.Roll();
        // From over the town: from the castle round to the cliff.
        for (float t = 0f; t < TownSeconds; t += Time.deltaTime)
        {
            float k = Mathf.SmoothStep(0f, 1f, t / TownSeconds);
            Look(226, 318, 30f, Mathf.RoundToInt(Mathf.Lerp(160f, 150f, k)), Mathf.RoundToInt(Mathf.Lerp(384f, 326f, k)), 5f);
            yield return null;
        }
        // Down the canyon's floor, from its mouth round the bend.
        // Along its trail, the middle of its floor, clear of the scree at the walls' feet.
        var points = new List<Vector3>();
        var canyon = grid.World.MapStampsLaid.First(st => st.Profile == LandStamp.Canyon);
        int step = 0;
        foreach (var (x, y) in canyon.Trail())
            if (step++ % 8 == 0 && x <= CanyonPath[0].x) points.Add(grid.CellCenter(x, y));
        float length = 0f;
        for (int i = 1; i < points.Count; i++) length += Vector3.Distance(points[i - 1], points[i]);
        PlaceCell(222, 330);
        for (float t = 0f; t < WalkSeconds; t += Time.deltaTime)
        {
            float s = Mathf.SmoothStep(0f, 1f, t / WalkSeconds) * (length - LookAhead);
            var at = Along(points, s);
            var ahead = Along(points, s + LookAhead);
            Cut(new Vector3(at.x, grid.GroundAt(at) + EyeHeight, at.z), new Vector3(ahead.x, grid.GroundAt(ahead) + EyeHeight, ahead.z));
            yield return null;
        }
        yield return film.Stop();
        Metric("frames", film.Frames);
        Metric("seconds", film.Seconds);
        Check("filmed", film.Saved >= film.Frames - 2 && film.Seconds >= TownSeconds + WalkSeconds - 1f, $"{film.Saved}/{film.Frames} frames, {film.Seconds:0.0} s");
    }

    // The point this far along a line of points, held at its end.
    static Vector3 Along(List<Vector3> points, float s)
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
