using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

// The probe's punchclip scenario, for Andrew's verdict on the punch's strength (#18):
// the wizard punches a crate on F through its own controls, on dirt, filmed from the
// side so the distance shows. Needs --shots <folder>: the frames and sound.wav (ClipFilm).
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
        var o = Spot(8, 5);
        Patch(o, 8, 5, TerrainIds.Dirt);
        var stand = new Vector2Int(o.x + 3, o.y + 2);
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
            var between = ahead + Vector3.right * 0.3f;
            Cut(between + new Vector3(0f, 1.1f, -4.6f), between + Vector3.up * 0.6f);
            yield return Wait(0.3f);

            var film = new ClipFilm(this, ScreenshotDir);
            var from = crate.transform.position;
            Check("sound_renders_offline", film.Roll(), "AudioRenderer.Start");
            yield return Wait(1f);
            InputSystem.QueueStateEvent(keys, new KeyboardState(Key.F));
            yield return null;
            yield return null;
            InputSystem.QueueStateEvent(keys, new KeyboardState());
            yield return Wait(2.6f);
            float went = Vector3.Distance(from, crate.transform.position);
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
}
