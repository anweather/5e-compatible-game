using System.Collections;
using System.IO;
using System.Linq;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

// The probe's uxclip scenario: short films of the HUD's and the camera's feel, for Andrew's
// board, of what shots can't show: Back from the grimoire (#10), the cast bar filling (#9),
// and the camera as the wizard walks into a house (#20). Through the wizard's own controls
// on a virtual gamepad, with the HUD on, each take into a folder under --shots.
public class UxClipProbe : ProbeScenario
{
    // How far between two ticks the casts are pressed, in ticks (ComboProbe.BarRunsEven).
    const float LateInTheTick = 0.8f;
    const int Casts = 3;
    const float Pitch = 12f, WalkSeconds = 3.5f, HoldSeconds = 1.5f, StartOut = 6f;

    Gamepad pad;
    PlayerControls controls;
    string me;

    protected override IEnumerator Run()
    {
        if (string.IsNullOrEmpty(ScreenshotDir))
        {
            Check("needs_shots", false, "uxclip films into --shots <folder>");
            yield break;
        }
        yield return Switch(At(12f) + ", 'weather': { 'calmPeriods': 0, 'rampMinutes': 0.02, 'weights': " +
            "{ 'clear': 1, 'cloudy': 0, 'windy': 0, 'fog': 0, 'rain': 0, 'storm': 0, 'snow': 0 } }");
        controls = caster.controls;
        bool controlsWereOn = controls != null && controls.enabled, casterWasOn = caster.enabled;
        if (controls != null) controls.enabled = true;
        caster.enabled = true;
        pad = InputSystem.AddDevice<Gamepad>("Clip pad");
        EventLog.HudVisible = true;
        me = caster.CastPlayer;
        try
        {
            Place(8, 12);
            yield return Wait(1.5f);
            yield return Take("grimoire", GrimoireBack());
            yield return Take("castbar", CastBar());
            yield return Take("house", House());
        }
        finally
        {
            if (controls != null) controls.enabled = controlsWereOn;
            caster.enabled = casterWasOn;
            InputSystem.RemoveDevice(pad);
        }
    }

    IEnumerator Take(string name, IEnumerator scene)
    {
        var film = new ClipFilm(this, Path.Combine(ScreenshotDir, name), withHud: true);
        film.Roll();
        yield return scene;
        yield return film.Stop();
        Metric($"{name}_seconds", film.Seconds);
        Check($"{name}_filmed", film.Saved > 0, $"{film.Saved} of {film.Frames} frames");
    }

    // The d-pad's grimoire and Back, then the menu's button to the grimoire and Back.
    IEnumerator GrimoireBack()
    {
        var menu = PauseMenu.Instance;
        yield return Wait(0.7f);
        yield return Press(GamepadButton.DpadDown);
        yield return Wait(1.3f);
        yield return Press(GamepadButton.East);
        yield return Wait(0.1f);
        Check("dpad_grimoire_back_to_play", !menu.Open, $"open {menu.Open}, page {menu.CurrentPage}");
        yield return Wait(1.2f);
        menu.SetOpen(false);
    }

    // Casts pressed just before a tick, when the first tick comes almost at once.
    IEnumerator CastBar()
    {
        for (int i = 0; i < Casts; i++)
        {
            int tick = World.Tick;
            while (World.Tick == tick) yield return null;
            yield return Wait(LateInTheTick / World.Rules.Sim.TicksPerSecond);
            yield return Press(GamepadButton.RightShoulder);
            while (World.CastOf(me).Casting) yield return null;
            yield return Wait(0.8f);
        }
    }

    // Into a house from out in the open, and held against its wall, as a player walks.
    IEnumerator House()
    {
        var village = FindObjectsByType<Village>(FindObjectsSortMode.None).First(v => v.villageName == "Hollowmere");
        var house = village.GetComponentsInChildren<VillageHouse>()[0];
        var root = wizard.transform.Find(WizardRig.CameraRoot);
        var centre = house.transform.position;
        var side = house.transform.forward;
        var start = centre + side * (house.depth / 2f + StartOut);
        start.y = grid.GroundAt(start) + 0.1f;
        Teleport(start);
        var dir = (centre - start).normalized;
        float shortest = float.MaxValue;
        for (float t = 0f; t < WalkSeconds + HoldSeconds; t += Time.deltaTime)
        {
            puppet.AimCamera(centre, Pitch, 1000f);
            var cam = Camera.main.transform;
            var flat = Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized;
            puppet.Move(new Vector2(Vector3.Dot(dir, cam.right), Vector3.Dot(dir, flat)).normalized);
            yield return null;
            if (t > 0.5f) shortest = Mathf.Min(shortest, Vector3.Distance(cam.position, root.position));
        }
        puppet.Move(Vector2.zero);
        Metric("house_arm_shortest_m", shortest);
        Metric("house_camera_to_wizard_m", Vector3.Distance(Camera.main.transform.position, wizard.transform.position));
    }

    IEnumerator Press(GamepadButton button)
    {
        InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(button));
        yield return null;
        yield return null;
        InputSystem.QueueStateEvent(pad, new GamepadState());
        yield return null;
        yield return null;
    }
}
