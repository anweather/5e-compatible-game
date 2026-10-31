using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

// The probe's punchclip scenario, for Andrew's verdict on the punch's strength (#18):
// the wizard punches a crate on F through its own controls, on dirt, filmed from the
// side so the distance shows, then turns and punches the air the other way. Needs
// --shots <folder>: the frames and sound.wav (ClipFilm). Metrics say where the crate's
// back face began in the frame, for the start line drawn on the clip.
public class PunchClipProbe : SpellLooksProbe
{
    protected override IEnumerator Run()
    {
        if (string.IsNullOrEmpty(ScreenshotDir))
        {
            Check("needs_shots", false, "punchclip films into --shots <folder>");
            yield break;
        }
        var controls = caster.controls;
        bool controlsWereOn = controls != null && controls.enabled, casterWasOn = caster.enabled;
        if (controls != null) controls.enabled = true;
        caster.enabled = true;
        var keys = InputSystem.AddDevice<Keyboard>("Punch keys");
        var o = Spot(20, 7);
        Patch(o, 20, 7, TerrainIds.Dirt);
        var stand = new Vector2Int(o.x + 5, o.y + 3);
        PlaceCell(stand.x, stand.y);
        var ahead = grid.CellCenter(stand.x, stand.y) + Vector3.right;
        puppet.Face(ahead + Vector3.right * 5f);
        yield return Wait(0.3f);
        var crate = NearestCrate(ahead);
        Check("crate_near", crate != null, "a crate");
        if (crate != null)
        {
            Drop(crate, new Vector3(ahead.x, grid.GroundAt(ahead) + 0.6f, ahead.z));
            yield return Wait(0.8f);
            var between = ahead + Vector3.right * 1.3f;
            Cut(between + new Vector3(0f, 1.2f, -6.4f), between + Vector3.up * 0.6f);
            yield return Wait(0.3f);
            var back = Camera.main.WorldToViewportPoint(crate.transform.position + Vector3.left * crate.GetComponent<Collider>().bounds.extents.x);
            Metric("line_x", back.x);
            Metric("line_foot_y", 1f - back.y);

            var film = new ClipFilm(this, ScreenshotDir);
            var from = crate.transform.position;
            Check("sound_renders_offline", film.Roll(), "AudioRenderer.Start");
            yield return Wait(1f);
            yield return Press(keys);
            yield return Wait(2.2f);
            float went = Vector3.Distance(from, crate.transform.position);
            // A miss: turned away, nothing in reach.
            puppet.Face(ahead + Vector3.left * 8f);
            yield return Wait(0.7f);
            yield return Press(keys);
            yield return Wait(1.1f);
            yield return film.Stop();
            Metric("crate_m", went);
            Metric("seconds", film.Seconds);
            Metric("sound_peak", film.Peak);
            Check("punch_moved_it", went > 0.1f, $"it went {went:0.00} m");
            Check("frames_saved", film.Saved > 50, $"{film.Saved} of {film.Frames} frames");
        }
        InputSystem.RemoveDevice(keys);
        if (controls != null) controls.enabled = controlsWereOn;
        caster.enabled = casterWasOn;
    }

    static IEnumerator Press(Keyboard keys)
    {
        InputSystem.QueueStateEvent(keys, new KeyboardState(Key.F));
        yield return null;
        yield return null;
        InputSystem.QueueStateEvent(keys, new KeyboardState());
    }
}
