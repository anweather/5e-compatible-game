using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// The probe's cliffclip scenario: films the castle on its cliff from the town, then a walk at eye height from the castle's
// gate along its east wall onto the pad beyond, past the stable, to the cliff's lip, and the view down the face
// (castlecliff checks them). Needs --shots <folder>: ClipFilm's frames and sound.wav, for ffmpeg.
public class CastleCliffClipProbe : CastleCliffProbe
{
    const float TownSeconds = 7f, WalkSeconds = 15f, DownSeconds = 6f, EyeHeight = 1.8f;

    protected override IEnumerator Run()
    {
        if (string.IsNullOrEmpty(ScreenshotDir))
        {
            Check("needs_shots", false, "cliffclip films into --shots <folder>");
            yield break;
        }
        yield return Switch(At(10.5f));
        PlaceCell(126, 372);
        yield return Wait(3f);
        var film = new ClipFilm(this, ScreenshotDir);
        film.Roll();
        // From over the town: from the castle round to its cliff.
        for (float t = 0f; t < TownSeconds; t += Time.deltaTime)
        {
            float k = Mathf.SmoothStep(0f, 1f, t / TownSeconds);
            Look(226, Mathf.RoundToInt(Mathf.Lerp(326f, 318f, k)), Mathf.Lerp(26f, 34f, k), Mathf.RoundToInt(Mathf.Lerp(132f, 146f, k)), Mathf.RoundToInt(Mathf.Lerp(362f, 330f, k)), 6f);
            yield return null;
        }
        // The walk: out of the gate, north along the wall, then east to the lip, looking ahead and, at the turn, west over the stable.
        var points = new List<Vector3>();
        foreach (var c in Walk()) points.Add(grid.CellCenter(c.x, c.y));
        float length = 0f;
        for (int i = 1; i < points.Count; i++) length += Vector3.Distance(points[i - 1], points[i]);
        float top = grid.GroundAt(points[points.Count - 1]);
        var stable = grid.CellCenter(124, 326);
        for (float t = 0f; t < WalkSeconds; t += Time.deltaTime)
        {
            float s = Mathf.SmoothStep(0f, 1f, t / WalkSeconds) * (length - 3f);
            var at = Along(points, s);
            var ahead = Along(points, s + 9f);
            // Halfway along the wall the eye turns to the stable, and back to the way ahead before the turn east.
            float glance = Mathf.Clamp01(1f - Mathf.Abs(t / WalkSeconds - 0.55f) / 0.15f);
            var look = Vector3.Lerp(ahead, stable, glance * 0.8f);
            Cut(new Vector3(at.x, grid.GroundAt(at) + EyeHeight, at.z), new Vector3(look.x, top + EyeHeight - glance * 2f, look.z));
            yield return null;
        }
        // At the lip: out over it a little, clear of the boulders on it, and the eye tips down the face.
        var lip = Along(points, length - 3f);
        var east = (points[points.Count - 1] - points[points.Count - 2]).normalized;
        var south = new Vector3(-east.z, 0f, east.x);
        for (float t = 0f; t < DownSeconds; t += Time.deltaTime)
        {
            float k = Mathf.SmoothStep(0f, 1f, t / DownSeconds);
            var from = lip + east * (1f + 4f * k) + south * 4f * k + Vector3.up * (top + EyeHeight + 4f * k);
            var to = lip + east * Mathf.Lerp(40f, 14f, k) + south * 6f * k + Vector3.up * Mathf.Lerp(top - 2f, top - 34f, k);
            Cut(from, to);
            yield return null;
        }
        yield return film.Stop();
        Metric("frames", film.Frames);
        Metric("seconds", film.Seconds);
        Check("filmed", film.Saved >= film.Frames - 2 && film.Seconds >= TownSeconds + WalkSeconds + DownSeconds - 1f, $"{film.Saved}/{film.Frames} frames, {film.Seconds:0.0} s");
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
