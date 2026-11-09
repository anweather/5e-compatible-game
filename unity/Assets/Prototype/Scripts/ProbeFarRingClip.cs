using System.Collections;
using System.Linq;
using Chem;
using UnityEngine;

// The probe's farringclip scenario, on the nine (unity/Temp/map = nine): films the far ring (FarRing) in clear daylight
// from over the valley's spawn: the eye looks over the valley, turns to the tallest far landmark the plan has (the volcano
// or the spire where the plan places one; the nine's seed valley-1 has neither, so its village's great tree), then
// glides toward it past its seenFrom, so the haze, the known chunks' land beyond the generated ring and the landmark
// coming out of the sky through the fog are all in it. The wizard keeps under the eye, so the rings follow it.
// Needs --shots <folder>: ClipFilm's frames and sound.wav, for ffmpeg, and a still of the last view with the landmarks'
// haze lifted. Checks: the far ring up with known chunks drawn, a far landmark to film, shown by the end, and it filmed.
public class FarRingClipProbe : ProbeScenario
{
    const float Settle = 4f, Open = 4f, Turning = 6f, Gliding = 12f, Hold = 4f, Up = 45f;

    // The glide: from over the spawn to past the landmark's seenFrom, over the highest ground on the way.
    struct Glide
    {
        public Vector3 Start, End, Top, Away;
        public float Highest;
    }

    protected override IEnumerator Run()
    {
        if (string.IsNullOrEmpty(ScreenshotDir))
        {
            Check("needs_shots", false, "farringclip films into --shots <folder>");
            yield break;
        }
        yield return Switch(At(11f) + ", 'weather': { 'calmPeriods': 0, 'rampMinutes': 0.02, 'weights': " +
                            "{ 'clear': 1, 'cloudy': 0, 'windy': 0, 'fog': 0, 'rain': 0, 'storm': 0, 'snow': 0 } }");
        for (float t = 0f; t < 20f && !RingUp(FarRing.Instance); t += Time.deltaTime) yield return null;
        var far = FarRing.Instance;
        Check("far_ring_up", RingUp(far), far == null ? "no far ring (not the nine?)" : $"{far.KnownDrawn} known chunks drawn");
        if (!RingUp(far)) yield break;
        yield return Wait(Settle);

        var start = wizard.transform.position + Vector3.up * Up;
        var target = Target(far, start);
        if (target.landmark == null) yield break;
        var g = Plan(target, start);

        var film = new ClipFilm(this, ScreenshotDir);
        film.Roll();
        yield return Turn(g.Start, g.Away, g.Away, Open);
        yield return Turn(g.Start, g.Away, g.Top, Turning);
        yield return GlideOn(g);
        Check("landmark_shown", far.Landmarks.Any(l => l.landmark == target.landmark && l.shown), Describe(target, g.End));
        // Whether the land stands between the eye and the landmark's top at the end (a terrain's collider is there drawn or not).
        var eyeAtEnd = Camera.main.transform.position;
        Metric("landmark_hidden_by_land_at_m", Physics.Linecast(eyeAtEnd, g.Top, out var hit) ? hit.distance : -1f);
        Metric("landmark_off_m", Vector3.Distance(eyeAtEnd, g.Top));
        yield return Turn(g.End, g.Top, g.Top, Hold);
        yield return film.Stop();
        Metric("frames", film.Frames);
        Metric("seconds", film.Seconds);
        const float seconds = Open + Turning + Gliding + Hold;
        Check("filmed", film.Saved >= film.Frames - 2 && film.Seconds >= seconds - 1f, $"{film.Saved}/{film.Frames} frames, {film.Seconds:0.0} s");
        yield return Bare(far);
    }

    static bool RingUp(FarRing far) => far != null && far.IsReady && far.KnownDrawn > 0;

    // The volcano (the nine's must), else the spire, else the plan's tallest landmark; with the ring's metrics.
    (FarLandmark landmark, Vector3 foot, bool shown) Target(FarRing far, Vector3 start)
    {
        var all = far.Landmarks.ToList();
        var target = all.FirstOrDefault(l => l.landmark.Type == "volcano");
        if (target.landmark == null) target = all.FirstOrDefault(l => l.landmark.Type == "spire");
        if (target.landmark == null) target = all.Where(l => l.landmark.From == "landmark").OrderByDescending(l => l.landmark.Height).FirstOrDefault();
        Check("landmark_to_film", target.landmark != null, target.landmark == null ? "the plan has no far landmark" : Describe(target, start));
        Metric("landmarks", all.Count);
        Metric("landmarks_shown_at_spawn", far.LandmarksShown);
        Metric("known_drawn", far.KnownDrawn);
        Metric("known_build_worker_ms", (float)FarRing.LastBuild.workerMs);
        Metric("known_build_main_ms", (float)FarRing.LastBuild.mainMs);
        if (target.landmark != null) Metric("landmark_foot_over_ground_m", target.foot.y - GroundRound(target.foot, target.landmark.Radius));
        return target;
    }

    // The ground round a landmark's foot (m): the middle of 16 samples on the ring at its radius, outside its own stamp
    // (the ground at its middle is a volcano's crater floor, 50 m up its cone).
    float GroundRound(Vector3 foot, float radius)
    {
        var heights = new float[16];
        for (int s = 0; s < 16; s++)
        {
            float a = s * Mathf.PI / 8f;
            heights[s] = grid.GroundAt(foot + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius);
        }
        System.Array.Sort(heights);
        return (heights[7] + heights[8]) / 2f;
    }

    Glide Plan((FarLandmark landmark, Vector3 foot, bool shown) target, Vector3 start)
    {
        var top = target.foot + Vector3.up * (target.landmark.Height * 0.6f);
        // It ends at 0.7 of the landmark's seenFrom from it (past sight, so it's still a far mesh).
        float seen = target.landmark.SeenFrom * 96f, off = new Vector2(top.x - start.x, top.z - start.z).magnitude;
        var end = start + Flat(top - start) * Mathf.Max(0f, off - 0.7f * seen);
        // Over the highest ground on the way and on to the landmark, so the glide clears the hills and the view of it does,
        // and well over the landmark's own ground, so the land round it doesn't stand in front of it.
        float highest = start.y;
        for (int s = 0; s <= 96; s++)
            highest = Mathf.Max(highest, grid.GroundAt(Vector3.Lerp(start, target.foot, s / 96f)) + Up * 0.7f);
        highest = Mathf.Max(highest, target.foot.y + target.landmark.Height * 1.6f);
        end.y = highest;
        start.y = Mathf.Max(start.y, Mathf.Lerp(start.y, highest, 0.5f));
        var away = start + Quaternion.Euler(0f, -110f, 0f) * Flat(top - start) * 400f + Vector3.down * (Up * 0.8f);
        return new Glide { Start = start, End = end, Top = top, Away = away, Highest = highest };
    }

    IEnumerator GlideOn(Glide g)
    {
        for (float t = 0f; t < Gliding; t += Time.deltaTime)
        {
            float k = Mathf.SmoothStep(0f, 1f, t / Gliding);
            var eye = Vector3.Lerp(g.Start, g.End, k);
            eye.y = Mathf.Max(Mathf.Lerp(g.Start.y, g.Highest, Mathf.Clamp01(k * 3f)), grid.GroundAt(eye) + Up * 0.5f);
            Cut(eye, g.Top);
            // The wizard keeps under the eye, so the generated and known rings follow it as they would a walk.
            Teleport(new Vector3(eye.x, grid.GroundAt(eye) + 0.05f, eye.z));
            yield return null;
        }
    }

    // A still of the same view with the landmarks' own haze lifted, so a tuning pass can see where they stand.
    IEnumerator Bare(FarRing far)
    {
        far.enabled = false;
        Shader.SetGlobalFloat("_FarHazeDensity", 0f);
        foreach (var r in far.GetComponentsInChildren<MeshRenderer>())
        {
            if (!r.name.StartsWith("Far ", System.StringComparison.Ordinal)) continue;
            var block = new MaterialPropertyBlock();
            r.GetPropertyBlock(block);
            block.SetFloat("_FadeMetres", 1f);
            block.SetFloat("_HazeAtSeen", 0.0001f);
            r.SetPropertyBlock(block);
        }
        yield return Wait(0.5f);
        Shot("landmarks-bare");
        yield return Wait(0.5f);
        far.enabled = true;
    }

    static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z).normalized;

    static string Describe((FarLandmark landmark, Vector3 foot, bool shown) l, Vector3 eye) =>
        $"{l.landmark.Name ?? l.landmark.Type} {new Vector2(l.foot.x - eye.x, l.foot.z - eye.z).magnitude:0} m off, seenFrom {l.landmark.SeenFrom}";

    // The eye turns, eased, from looking at one point to another.
    IEnumerator Turn(Vector3 eye, Vector3 from, Vector3 to, float seconds)
    {
        var a = Quaternion.LookRotation(from - eye);
        var b = Quaternion.LookRotation(to - eye);
        for (float t = 0f; t < seconds; t += Time.deltaTime)
        {
            var look = Quaternion.Slerp(a, b, Mathf.SmoothStep(0f, 1f, t / seconds));
            Cut(eye, eye + look * Vector3.forward * 100f);
            yield return null;
        }
    }
}
