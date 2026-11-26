using System.Collections;
using System.Linq;
using UnityEngine;

// The probe's stampclip scenario: films ten crates riding a dome stamp as it rises (#147), for the stamp-looks
// clip. The dome is 15 cells across but only 4 m high: a face of 5 m or more is a crag (#148), and its boulders
// hide the crates from any angle. It films with ClipFilm (a fixed 30 fps clock, frames read back from the camera's own
// RenderTexture) rather than the Recorder package, which stalled the stamplooks run it was filming.
// Needs --shots <folder>: the frames and sound.wav, for ffmpeg.
public class StampClipProbe : LandShapesProbe
{
    const int Crates = 10, DomeRadius = 15, DomeRise = 4;
    const float Seconds = 25f;

    protected override IEnumerator Run()
    {
        if (string.IsNullOrEmpty(ScreenshotDir))
        {
            Check("needs_shots", false, "stampclip films into --shots <folder>");
            yield break;
        }
        var server = RulesMcpServer.Instance;
        if (!server.Serving) server.Serving = true;
        var valley = FindAnyObjectByType<ValleyChemistry>();
        PlaceCell(X - 28, Y - 28);
        yield return Wait(1f);

        var centre = grid.CellCenter(X, Y);
        centre.y = valley.GroundAt(centre);
        yield return Say("act", $"{{\"what\":\"crate\",\"count\":{Crates},\"near\":\"[{X}, {Y}]\",\"within\":12,\"spacing\":3}}");
        yield return Wait(3f);
        var crates = FindObjectsByType<PhysicsProp>(FindObjectsSortMode.None)
            .Where(p => p.name.StartsWith("crate", System.StringComparison.Ordinal) &&
                        new Vector2(p.transform.position.x - centre.x, p.transform.position.z - centre.z).magnitude < 25f)
            .ToList();
        Check("crates_placed", crates.Count >= Crates / 2, $"{crates.Count} crates");
        float before = crates.Count > 0 ? crates.Average(c => c.transform.position.y) : 0f;

        Cut(centre + new Vector3(-30f, 16f, -30f), centre + Vector3.up * 2f);
        yield return Wait(0.5f);
        var film = new ClipFilm(this, ScreenshotDir);
        film.Roll();
        yield return Wait(1.5f);
        yield return Say("act", $"{{\"what\":\"land\",\"near\":\"[{X}, {Y}]\",\"radius\":{DomeRadius},\"rise\":{DomeRise},\"profile\":\"dome\"}}");
        Check("dome_moved_the_land", said.Contains("moved the land"), said.Length > 200 ? said.Substring(0, 200) : said);
        yield return Wait(Seconds - 1.5f);
        yield return film.Stop();

        float highest = crates.Count > 0 ? crates.Max(c => c.transform.position.y) : 0f;
        Metric("frames", film.Frames);
        Metric("frames_saved", film.Saved);
        Metric("seconds", film.Seconds);
        Metric("crates_rose_most_m", highest - before);
        Check("filmed", film.Saved >= film.Frames - 2 && film.Seconds >= Seconds - 1f, $"{film.Saved}/{film.Frames} frames, {film.Seconds:0.0} s");
        Check("crates_rode_up", highest - before > 2f, $"highest crate {highest - before:0.0} m above where they stood");

        yield return Say("undo_act", "{}");
        yield return Say("undo_act", "{}");
    }
}
