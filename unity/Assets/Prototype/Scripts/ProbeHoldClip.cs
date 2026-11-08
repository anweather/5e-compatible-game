using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Chem;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

// The probe's holdclip scenario: the hold-batch clip, with the HUD, on a virtual pad. In a run, at the water tower the right
// trigger attunes the hand to water and then casts (the HUD names the tower, the wave timer counts down); then high in the open
// keep, raiders strike the crystal at the heart while the keep's health is stepped down, so it cracks stage by stage and
// shatters. The setup between is cut. Needs --shots <folder>: frames, sound.wav and timeline.txt. The raiders are put down in the
// core directly, as only a solo probe may; nothing here is in the game.
public class HoldClipProbe : ProbeScenario
{
    const float MostWait = 10f;

    Gamepad pad;
    ClipFilm film;
    readonly List<string> timeline = new();

    World Core => grid.World;
    string Me => caster.CastPlayer;

    IEnumerator Trigger()
    {
        InputSystem.QueueStateEvent(pad, new GamepadState { rightTrigger = 1f });
        yield return null;
        yield return null;
        InputSystem.QueueStateEvent(pad, new GamepadState());
    }

    void Note(string what) => timeline.Add(string.Format(CultureInfo.InvariantCulture, "{0:0.000} {1}", film != null ? film.Seconds : 0f, what));

    protected override IEnumerator Run()
    {
        if (string.IsNullOrEmpty(ScreenshotDir))
        {
            Check("needs_shots", false, "holdclip films into --shots <folder>");
            yield break;
        }
        var site = Core.HoldSite;
        if (site == null) yield break;
        var controls = caster.controls;
        controls.enabled = true;
        caster.enabled = true;
        EventLog.HudVisible = true;
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        pad = InputSystem.AddDevice<Gamepad>("Hold clip pad");
        try
        {
            StandOn(site.Plate);
            for (float t = 0f; t < MostWait && !Core.HoldRunning; t += Time.deltaTime) yield return null;
            Check("a_run_starts", Core.HoldRunning);
            if (!Core.HoldRunning) yield break;
            Core.Hold.ToSpawn = 0; // the takes stage their own raiders
            film = new ClipFilm(this, ScreenshotDir, withHud: true) { Keeping = false };
            film.Roll();
            yield return AtTheTower(site);
            yield return TheCrystal(site);
            yield return film.Stop();
            File.WriteAllLines(Path.Combine(ScreenshotDir, "timeline.txt"), timeline);
            Metric("seconds", film.Seconds);
            Check("frames_saved", film.Saved > 300, $"{film.Saved} of {film.Frames} frames");
            Check("short_enough", film.Seconds <= 30f, $"{film.Seconds:0.0} s");
        }
        finally
        {
            InputSystem.RemoveDevice(pad);
            caster.enabled = false;
        }
    }

    // The water tower, over the wizard's shoulder: the HUD's tower line and timer, a trigger that attunes, then one that casts.
    IEnumerator AtTheTower(WorldHold site)
    {
        var tower = site.Towers.First(t => t.Element == ElementIds.Water);
        var ground = Core.TowerGround(tower)[0];
        StandOn(ground);
        var spot = grid.CellCenter(ground.x, ground.y);
        spot.y = grid.GroundAt(spot);
        var middle = grid.CellCenter(tower.X, tower.Y);
        var away = new Vector3(spot.x - middle.x, 0f, spot.z - middle.z).normalized;
        Cut(spot + away * 6f + Vector3.up * 3.5f + Vector3.Cross(away, Vector3.up) * 2f, spot + Vector3.up * 1.2f);
        caster.AimOverride = spot + away * 8f;
        yield return Wait(0.8f);
        film.Keeping = true;
        Note("at the water tower");
        yield return Wait(1.5f);
        Note("right trigger: attunes to water");
        yield return Trigger();
        for (float t = 0f; t < MostWait && Core.HandsOf(Me).Holds[HandsDef.Right] != ElementIds.Water; t += Time.deltaTime) yield return null;
        Check("the_tower_attunes", Core.HandsOf(Me).Holds[HandsDef.Right] == ElementIds.Water, Core.HandsOf(Me).Holds[HandsDef.Right]);
        yield return Wait(1.2f);
        Note("right trigger: casts");
        yield return Trigger();
        yield return Wait(2.5f);
        caster.AimOverride = null;
        film.Keeping = false;
    }

    // High in the open keep, from the plate's side: raiders striking the crystal, the keep's health stepped down.
    IEnumerator TheCrystal(WorldHold site)
    {
        var show = grid.GetComponent<HoldShow>();
        var crystal = show != null ? show.Crystal : null;
        Check("a_crystal_at_the_heart", crystal != null);
        if (crystal == null) yield break;
        StandOn(site.Rise);
        var (hx, hy) = site.Heart;
        var heart = grid.CellCenter(hx, hy);
        heart.y = grid.GroundAt(heart);
        var (px, py) = site.Plate;
        var plate = grid.CellCenter(px, py);
        var toward = new Vector3(plate.x - heart.x, 0f, plate.z - heart.z).normalized;
        Cut(heart + toward * 9f + Vector3.up * 17f + Vector3.Cross(toward, Vector3.up) * 3f, heart + Vector3.up * 1.5f); // at the height of the keep's walls
        int put = 0;
        foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (1, 1) })
            if (Core.SpawnRaider(RaiderIds.BoneHound, hx + dx, hy + dy, 4000) is { } r)
            {
                r.Wait = put * 2;
                put++;
            }
        int full = Core.Hold.Health;
        yield return Wait(0.5f);
        film.Keeping = true;
        Note("the crystal, struck");
        yield return Wait(3f);
        for (int stage = 1; stage <= HoldCrystal.Stages; stage++)
        {
            Core.Hold.Health = full * (HoldCrystal.Stages + 1 - stage) / (HoldCrystal.Stages + 1) - 1;
            Note($"crack {stage}");
            yield return Wait(2.6f);
        }
        Core.Hold.Health = 2;
        for (float t = 0f; t < MostWait && Core.HoldRunning; t += Time.deltaTime) yield return null;
        Note("shattered");
        yield return Wait(2.5f);
        Check("it_cracked_and_shattered", crystal.Shattered && crystal.Blows > 0, $"blows {crystal.Blows}, shattered {crystal.Shattered}");
    }
}
