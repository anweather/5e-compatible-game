using System.Collections;
using System.IO;
using System.Linq;
using Chem;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

// The probe's progress scenario (docs/design/the-hold.md, phase 5): F7 plays as the save says, and from a fresh save the locked hand
// and gesture say so, attuning offers only what's unlocked, a wave beaten brings the offer in words, the pad picks one, Tough shows
// on the health bar, the run's end says what was earned, the save holds it, and a reset keeps it. Wave 1 is beaten and the keep
// falls by the probe's hand (raiders burning, the keep at 1). Run it again after leaving Play: the points are still there.
// Nothing here is in the game.
public class ProgressProbe : ProbeScenario
{
    const float MostWait = 10f, MarchWait = 240f;
    const int WallX = 21, WallY = 3;

    Gamepad pad;
    World Core => grid.World;
    string Me => caster.CastPlayer;

    void Stand((int x, int y) cell)
    {
        var p = grid.CellCenter(cell.x, cell.y);
        p.y = grid.GroundAt(p);
        Teleport(p + Vector3.up * 0.05f);
    }

    IEnumerator Press(GamepadState state)
    {
        InputSystem.QueueStateEvent(pad, state);
        yield return null;
        yield return null;
        InputSystem.QueueStateEvent(pad, new GamepadState());
        yield return Wait(0.3f);
    }

    protected override IEnumerator Run()
    {
        var world = Core;
        var site = world.HoldSite;
        var keeper = grid.GetComponent<ProgressKeeper>();
        Check("the_map_has_a_hold", site != null && keeper != null);
        if (site == null || keeper == null) yield break;
        caster.enabled = true;
        caster.controls.enabled = true;
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        pad = InputSystem.AddDevice<Gamepad>("Progress pad");
        EventLog.HudVisible = true;
        int saved = keeper.Saved;
        Metric("saved_at_start", saved);

        // A dev build has everything first; F7 then must bring the save's points, never write everything into it.
        for (float t = 0f; t < MostWait && world.PointsOf(Me) != World.AllPoints; t += Time.deltaTime) yield return null;
        Check("a_dev_build_has_everything", world.PointsOf(Me) == World.AllPoints, $"{world.PointsOf(Me)}");
        keeper.PlayFromTheSave(true);
        for (float t = 0f; t < MostWait && world.PointsOf(Me) != saved; t += Time.deltaTime) yield return null;
        yield return Wait(1.5f);
        Check("f7_brings_the_save", world.PointsOf(Me) == saved, $"{world.PointsOf(Me)} of {saved}");
        Check("f7_leaves_the_save_alone", keeper.Saved == saved, $"{keeper.Saved}, was {saved}");
        Check("the_page_says_the_points", keeper.PageText().StartsWith($"Points: {saved}", System.StringComparison.Ordinal), keeper.PageText());
        var first = world.Rules.Sim.Hold.Unlocks[0];
        bool fresh = saved < first.Cost;

        Stand(site.Plate);
        for (float t = 0f; t < MostWait && !world.InAttuning(Me); t += Time.deltaTime) yield return null;
        Check("in_the_attuning_room", world.InAttuning(Me));
        string line = HoldShow.AttuningLine(world, Me);
        var locked = world.Rules.Combos.Elements.Where(e => !world.ElementOpen(Me, e)).ToList();
        string yours = line.Substring(line.IndexOf("Yours: ", System.StringComparison.Ordinal) + "Yours: ".Length).Split(';')[0];
        Check("the_room_shows_only_whats_yours", locked.All(e => !yours.Split(',').Select(s => s.Trim()).Contains(e)), line);
        for (int i = 0; i < 3; i++)
        {
            yield return Press(new GamepadState { rightTrigger = 1f });
            Check($"attuning_never_holds_a_locked_element_{i}", world.ElementOpen(Me, world.HandsOf(Me).Holds[HandsDef.Right]), world.HandsOf(Me).Holds[HandsDef.Right]);
        }
        for (int i = 0; i < 3 && world.HandsOf(Me).Holds[HandsDef.Right] == null; i++) yield return Press(new GamepadState { rightTrigger = 1f });
        if (fresh)
        {
            yield return Press(new GamepadState { leftTrigger = 1f });
            Check("a_locked_hand_says_so", caster.LockedLine?.Contains("left hand is locked") == true, caster.LockedLine);
            Check("the_hud_says_locked", grid.GetComponent<HoldShow>().HandsLine(Me).Contains("Left: locked"), grid.GetComponent<HoldShow>().HandsLine(Me));
        }
        Shot("attuning");
        yield return Wait(0.3f);

        for (float t = 0f; t < MostWait && !world.HoldRunning; t += Time.deltaTime) yield return null;
        Check("a_run_starts", world.HoldRunning);
        if (!world.HoldRunning) yield break;
        var row = world.BuildingRowOf(site.Castle);
        Stand((row.X + WallX, row.Y + WallY)); // the hands probe's cell on the wall, out of the room
        for (float t = 0f; t < MostWait && world.InAttuning(Me); t += Time.deltaTime) yield return null;
        yield return Wait(0.5f);
        if (fresh)
        {
            yield return Press(new GamepadState().WithButton(GamepadButton.RightShoulder));
            Check("a_locked_gesture_says_so", caster.LockedLine?.Contains("throw is locked") == true, caster.LockedLine);
            Check("and_nothing_is_cast", world.CastOf(Me)?.Casting != true);
        }

        // Wave 1 beaten by the probe: its raiders burn.
        for (float t = 0f; t < MarchWait && world.Hold.Wave == 1; t += Time.deltaTime)
        {
            foreach (var r in world.Raiders)
            {
                r.Hp = 1;
                r.Status["burning"] = 30;
            }
            yield return null;
        }
        Check("wave_1_is_beaten", world.Hold.Wave == 2, $"wave {world.Hold.Wave}");
        var run = world.RunOf(Me);
        yield return null;
        string offer = keeper.OfferText();
        Check("the_offer_is_in_words", run?.Offer.Count == world.Rules.Sim.Hold.Offer && offer.Split('\n').Length == run.Offer.Count + 1 && offer.Contains("picked for you in"), offer);
        Check("the_dpad_is_the_offers", caster.controls.OfferPad);
        Shot("offer");
        yield return Wait(0.3f);
        if (run == null || run.Offer.Count < 2) yield break;
        if (!run.Offer.Contains("tough")) run.Offer[1] = "tough"; // the probe's hand: Tough is the one to see
        var vitals = wizard.GetComponent<Vitals>();
        float before = vitals.HealthMax;
        yield return Press(new GamepadState().WithButton(GamepadButton.DpadRight));
        yield return Press(new GamepadState().WithButton(GamepadButton.DpadUp));
        for (float t = 0f; t < MostWait && run.Picks.Count == 0; t += Time.deltaTime) yield return null;
        Check("the_pad_picks_the_second", run.Picks.SequenceEqual(new[] { "tough" }), string.Join(",", run.Picks));
        Check("the_offer_closes", run.Offer.Count == 0 && keeper.OfferText().Length == 0 && !caster.controls.OfferPad);
        yield return Wait(0.3f);
        int tough = world.Rules.Sim.Hold.Upgrade("tough").Health;
        Check("tough_raises_health", Mathf.Approximately(vitals.HealthMax, before + tough) && vitals.ExtraHealth == tough, $"{before} to {vitals.HealthMax}");
        Check("the_picks_are_listed", keeper.PicksText(world, Me).Contains("Tough: " + tough + " more health"), keeper.PicksText(world, Me));
        Shot("picks");

        // The keep falls to the next raider at it.
        world.Hold.Health = 1;
        for (float t = 0f; t < MarchWait && world.HoldRunning; t += Time.deltaTime) yield return null;
        Check("the_keep_falls", !world.HoldRunning, $"wave {world.Hold.Wave}");
        yield return null;
        int earned = (world.Hold.Wave - 1) * world.Rules.Sim.Hold.PointsPerWave;
        Check("the_end_says_what_was_earned", keeper.Summary.Contains($"+{earned} point") && keeper.Summary.Contains("Next:"), keeper.Summary);
        Check("health_goes_with_the_run", Mathf.Approximately(vitals.HealthMax, before), $"{vitals.HealthMax}");
        yield return Wait(1.5f);
        Check("the_save_holds_it", keeper.Saved == saved + earned && File.ReadAllText(Path.Combine(ChemGrid.Folders.PlayerData, ProgressFile.FileName)).Contains($"\"points\": {saved + earned}"), $"{keeper.Saved}");
        Shot("end");

        grid.ResetWorld();
        for (float t = 0f; t < MostWait && grid.World?.PointsOf(Me) != saved + earned; t += Time.deltaTime) yield return null;
        Check("a_reset_keeps_the_points", grid.World?.PointsOf(Me) == saved + earned, $"{grid.World?.PointsOf(Me)}");

        keeper.PlayFromTheSave(false);
        for (float t = 0f; t < MostWait && grid.World?.PointsOf(Me) != World.AllPoints; t += Time.deltaTime) yield return null;
        Check("f7_again_is_everything", grid.World?.PointsOf(Me) == World.AllPoints);
        InputSystem.RemoveDevice(pad);
        caster.enabled = false;
    }
}
