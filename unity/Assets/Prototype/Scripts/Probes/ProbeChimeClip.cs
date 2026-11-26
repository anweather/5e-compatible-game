using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

// The probe's chimeclip scenario: the sound of the casts that used to chime, for Andrew's board
// (J-054): Scalding Steam, Mud, Rain Cloud and a d-pad cast, through the wizard's own controls on
// a virtual gamepad, with the HUD on and the mixed sound, into --shots: frames and sound.wav.
public class ChimeClipProbe : ProbeScenario
{
    const float AfterCast = 1.4f, MostCastSeconds = 5f, AimMetres = 10f;
    const int LandingsFilmed = 4;

    Gamepad pad;
    string me;
    int arcane;
    float farthest;
    readonly System.Collections.Generic.List<string> heard = new();

    void Played(string sound, Vector3 at)
    {
        heard.Add(sound);
        if (sound != SoundIds.Arcane) return;
        arcane++;
        farthest = Mathf.Max(farthest, Vector3.Distance(at, Camera.main.transform.position));
    }

    protected override IEnumerator Run()
    {
        if (string.IsNullOrEmpty(ScreenshotDir))
        {
            Check("needs_shots", false, "chimeclip films into --shots <folder>");
            yield break;
        }
        yield return Switch(At(12f) + ", 'weather': { 'calmPeriods': 0, 'rampMinutes': 0.02, 'weights': " +
            "{ 'clear': 1, 'cloudy': 0, 'windy': 0, 'fog': 0, 'rain': 0, 'storm': 0, 'snow': 0 } }");
        var controls = caster.controls;
        bool controlsWereOn = controls != null && controls.enabled, casterWasOn = caster.enabled;
        if (controls != null) controls.enabled = true;
        caster.enabled = true;
        pad = InputSystem.AddDevice<Gamepad>("Clip pad");
        EventLog.HudVisible = true;
        me = caster.CastPlayer;
        try
        {
            Place(13, 10);
            yield return Wait(1f);
            // Close by, as a player casts: out on the hills it lands past where a sound carries.
            var aim = wizard.transform.position + wizard.transform.forward * AimMetres;
            aim.y = grid.GroundAt(aim);
            caster.AimOverride = aim;
            var film = new ClipFilm(this, ScreenshotDir, withHud: true);
            Check("sound_renders", film.Roll());
            yield return Wait(0.6f);
            GameSounds.Played += Played;
            yield return Pair("scalding_steam", new GamepadState().WithButton(GamepadButton.LeftShoulder), new GamepadState().WithButton(GamepadButton.RightShoulder));
            yield return Pair("mud", new GamepadState().WithButton(GamepadButton.RightShoulder), new GamepadState { leftTrigger = 1f });
            yield return Pair("rain_cloud", new GamepadState { rightTrigger = 1f }, new GamepadState().WithButton(GamepadButton.RightShoulder));
            yield return Dpad();
            GameSounds.Played -= Played;
            yield return film.Stop();
            Check("landings_whoosh", arcane >= LandingsFilmed, $"arcane played {arcane} times, as far as {farthest:0.0} m from the camera; heard: {string.Join(" ", heard)}");
            Metric("seconds", film.Seconds);
            Metric("sound_seconds", film.SoundSeconds);
            Metric("sound_peak", film.Peak);
            Check("filmed", film.Saved > 0, $"{film.Saved} of {film.Frames} frames");
            Check("heard", film.Peak > 0.01f, $"peak {film.Peak:0.000}");
        }
        finally
        {
            caster.AimOverride = null;
            if (controls != null) controls.enabled = controlsWereOn;
            caster.enabled = casterWasOn;
            InputSystem.RemoveDevice(pad);
        }
    }

    // Two elements in the window, then the wait for the cast to land and its sound to die.
    IEnumerator Pair(string name, GamepadState first, GamepadState second)
    {
        int cast = caster.CastsStarted;
        yield return Tap(first);
        yield return Tap(second);
        yield return UntilLanded();
        Check($"{name}_cast", caster.CastsStarted > cast, $"{caster.CastsStarted - cast} casts started");
    }

    IEnumerator Dpad()
    {
        var combos = World.Rules.Combos;
        var right = new GamepadState().WithButton(GamepadButton.DpadRight);
        for (int i = 0; i < combos.Dpad.Count && World.SelectedDpad(me)?.Spell == null; i++) yield return Tap(right);
        int cast = caster.CastsStarted;
        yield return Tap(new GamepadState().WithButton(GamepadButton.DpadUp));
        yield return UntilLanded();
        Check("dpad_cast", caster.CastsStarted > cast, World.SelectedDpad(me)?.Name);
    }

    IEnumerator UntilLanded()
    {
        for (float waited = 0f; World.CastOf(me).Casting && waited < MostCastSeconds; waited += Time.deltaTime) yield return null;
        yield return Wait(AfterCast);
    }

    IEnumerator Tap(GamepadState held)
    {
        InputSystem.QueueStateEvent(pad, held);
        yield return null;
        yield return null;
        InputSystem.QueueStateEvent(pad, new GamepadState());
        yield return null;
        yield return null;
    }
}
