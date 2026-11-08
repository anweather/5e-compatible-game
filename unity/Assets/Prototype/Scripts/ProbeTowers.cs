using System.Collections;
using System.Linq;
using Chem;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

// The probe's towers scenario (hold-batch 4): attuning at the castle's towers, on a virtual pad. In a run, at each tower a hand's
// trigger attunes it to the tower's element in its moment (attuneTicks), the HUD says which tower it is, the same trigger then
// casts, and away from the towers a run attunes nothing. Each tower shows its element round it (HoldTowers). With --shots, each
// tower from above and a wizard attuning at one. Nothing here is in the game.
public class TowersProbe : ProbeScenario
{
    const float MostWait = 10f;

    Gamepad pad;
    World Core => grid.World;
    string Me => caster.CastPlayer;

    void Stand((int x, int y) cell)
    {
        var p = grid.CellCenter(cell.x, cell.y);
        p.y = grid.GroundAt(p);
        Teleport(p + Vector3.up * 0.05f);
    }

    IEnumerator Trigger(int hand)
    {
        InputSystem.QueueStateEvent(pad, hand == HandsDef.Left ? new GamepadState { leftTrigger = 1f } : new GamepadState { rightTrigger = 1f });
        yield return null;
        yield return null;
        InputSystem.QueueStateEvent(pad, new GamepadState());
        yield return null;
    }

    protected override IEnumerator Run()
    {
        var site = Core.HoldSite;
        Check("four_towers", site != null && site.Towers.Count == 4 && site.Towers.Select(t => t.Element).Distinct().Count() == 4, site == null ? "no hold" : string.Join(",", site.Towers.Select(t => t.Element)));
        if (site == null) yield break;
        var show = grid.GetComponent<HoldShow>();
        var looks = show != null ? show.Towers : null;
        Check("each_tower_shows_its_element", looks != null && looks.Shown.Count == site.Towers.Count, looks == null ? "none" : $"{looks.Shown.Count}");
        var controls = caster.controls;
        controls.enabled = true;
        caster.enabled = true;
        EventLog.HudVisible = true;
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        pad = InputSystem.AddDevice<Gamepad>("Towers pad");
        try
        {
            Stand(site.Plate);
            for (float t = 0f; t < MostWait && !Core.HoldRunning; t += Time.deltaTime) yield return null;
            Check("a_run_starts", Core.HoldRunning);
            if (!Core.HoldRunning) yield break;
            Core.Hold.Health = int.MaxValue / 2;
            Core.Hold.ToSpawn = 0;

            // Away from the towers a run attunes nothing: the trigger casts.
            Stand(site.Rise);
            yield return Wait(0.5f);
            string before = Core.HandsOf(Me).Holds[HandsDef.Right];
            yield return Trigger(HandsDef.Right);
            yield return Wait(1f);
            Check("away_from_the_towers_nothing_attunes", Core.HandsOf(Me).Holds[HandsDef.Right] == before, Core.HandsOf(Me).Holds[HandsDef.Right]);

            foreach (var tower in site.Towers.Where(t => Core.ElementOpen(Me, t.Element)))
                yield return AtTower(tower);
        }
        finally
        {
            InputSystem.RemoveDevice(pad);
            caster.enabled = false;
        }
    }

    // At the tower, from above: the right hand takes its element in a moment, then the same trigger casts.
    IEnumerator AtTower(HoldTower tower)
    {
        yield return Wait(1f); // any cast under way ends
        var ground = Core.TowerGround(tower)[0];
        Stand(ground);
        var spot = grid.CellCenter(ground.x, ground.y);
        spot.y = grid.GroundAt(spot);
        // From the courtyard side, up a little, with the tower behind the wizard.
        var middle = grid.CellCenter(tower.X, tower.Y);
        var away = new Vector3(spot.x - middle.x, 0f, spot.z - middle.z).normalized;
        Cut(spot + away * 9f + Vector3.up * 6f, spot + Vector3.up);
        yield return Wait(0.6f);
        string name = tower.Element;
        Check($"at_the_{name}_tower", Core.TowerOf(Me)?.Element == name, grid.CellAt(wizard.transform.position).ToString());
        Check($"the_hud_says_the_{name}_tower", HoldShow.TowerLine(Core, Me).Contains($"{name} tower"), HoldShow.TowerLine(Core, Me));
        if (Core.HandsOf(Me).Holds[HandsDef.Right] != name)
        {
            int at = Core.Tick;
            yield return Trigger(HandsDef.Right);
            for (float t = 0f; t < MostWait && Core.HandsOf(Me).Holds[HandsDef.Right] != name; t += Time.deltaTime) yield return null;
            Check($"the_{name}_tower_attunes_the_right_hand", Core.HandsOf(Me).Holds[HandsDef.Right] == name, Core.HandsOf(Me).Holds[HandsDef.Right]);
            Metric($"{name}_attune_ticks", Core.Tick - at);
            Shot($"tower-{name}");
        }
        for (float t = 0f; t < MostWait && (Core.HandCooling(Me, HandsDef.Right) > 0 || Core.HandsOf(Me).Busy); t += Time.deltaTime) yield return null;
        var heard = new FiredCastsHeard();
        heard.StartFrom(Core);
        yield return Trigger(HandsDef.Right);
        bool fired = false;
        for (float t = 0f; t < MostWait && !fired; t += Time.deltaTime)
        {
            fired = heard.Take(Core).Any(f => f.Player == Me);
            yield return null;
        }
        Check($"then_the_trigger_casts_at_the_{name}_tower", fired && Core.HandsOf(Me).Holds[HandsDef.Right] == name);
    }
}
