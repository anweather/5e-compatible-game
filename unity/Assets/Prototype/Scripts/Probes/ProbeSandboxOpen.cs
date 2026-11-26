using System.Collections;
using System.Linq;
using Chem;

// The probe's sandbox scenario: on whatever map is played (the valley, the nine), outside a Hold run, a released player whose
// save holds no points has everything (be205905, Andrew 2026-11-23): both hands, every gesture, element and kit spell that a
// row of rules/hold.json gives. A dev build and the Editor bring everything, so this plays as a save with none would
// (PlayAsIf). Nothing here is in the game.
public class SandboxOpenProbe : ProbeScenario
{
    const float MostWait = 10f;

    protected override IEnumerator Run()
    {
        var world = grid.World;
        var keeper = grid.GetComponent<ProgressKeeper>();
        string me = caster.CastPlayer;
        Check("not_in_a_run", !world.HoldRunning, $"map {ChemGrid.MapId}");
        if (keeper == null)
        {
            Check("has_a_progress_keeper", false);
            yield break;
        }
        keeper.PlayAsIf(0);
        for (float t = 0f; t < MostWait && world.PointsOf(me) != 0; t += UnityEngine.Time.unscaledDeltaTime) yield return null;
        Check("plays_as_a_save_with_none", world.PointsOf(me) == 0, $"{world.PointsOf(me)} points");

        var rows = world.Rules.Sim.Hold.Unlocks;
        Metric("rows", rows.Count);
        var shut = rows.Where(u =>
            (u.Hand != null && !world.HandOpen(me, u.Hand == "left" ? HandsDef.Left : HandsDef.Right))
            || (u.Gesture != HandsDef.NoGesture && !world.GestureOpen(me, u.Gesture))
            || (u.Element != null && !world.ElementOpen(me, u.Element))
            || (u.Kit != null && !world.KitOpen(me, u.Kit))).Select(u => u.Id).ToList();
        Check("everything_open", rows.Count > 0 && shut.Count == 0, shut.Count == 0 ? $"all {rows.Count} rows open on {ChemGrid.MapId}" : "shut: " + string.Join(", ", shut));
        Check("both_hands_open", world.HandOpen(me, HandsDef.Left) && world.HandOpen(me, HandsDef.Right),
            $"left {world.HandOpen(me, HandsDef.Left)}, right {world.HandOpen(me, HandsDef.Right)}");
    }
}
