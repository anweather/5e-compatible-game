using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Chem;
using UnityEngine;

// The probe's hitch scenario: the two frame-long stalls of Andrew's 2026-11-08 play log, timed here. A world built
// again from the map (what start-up, hosting and an online reset each do: 1.9 to 2.9 s a frame in his player) three
// times over, and a mod switched on as chat applies one (100 to 200 ms a frame, most of it the switch's checks and
// compose). Budgets are the Editor's, which runs these slower than a player does.
public class HitchProbe : ProbeScenario
{
    const float RebuildBudgetMs = 1300f, ComposeBudgetMs = 150f;
    const string Pink = "hitch-pink-fire";
    const string PinkFire = "{ \"title\": \"Pink fire\", \"patch\": { \"elements\": { \"fire\": { \"look\": { \"color\": [1.0, 0.4, 0.75] } } }, "
        + "\"spells\": [ { \"id\": \"fireball\", \"look\": { \"color\": [1.0, 0.4, 0.75] } }, { \"id\": \"fire-bolt\", \"look\": { \"color\": [1.0, 0.4, 0.75] } } ] } }";

    protected override IEnumerator Run()
    {
        yield return Wait(2f);
        var rebuilds = new List<double>();
        var blockersBefore = BlockersObject();
        for (int k = 0; k < 3; k++)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            grid.ResetWorld();
            rebuilds.Add(sw.Elapsed.TotalMilliseconds);
            yield return Wait(1.5f);
        }
        rebuilds.Sort();
        double median = rebuilds[1];
        Metric("rebuild_ms_best", rebuilds[0]);
        Metric("rebuild_ms_median", median);
        Check("rebuild_within_budget", median < RebuildBudgetMs, $"median {median:0} ms of {string.Join(", ", rebuilds.Select(r => r.ToString("0")))} (budget {RebuildBudgetMs:0})");
        var blockersAfter = BlockersObject();
        Check("rebuild_keeps_the_blockers", blockersBefore != null && blockersBefore == blockersAfter,
            "a rebuilt world takes the old one's blocker boxes rather than making them again");

        var switcher = grid.GetComponent<RuleSwitcher>();
        var composes = new List<double>();
        float worst = 0f;
        string refused = null;
        bool on = true;
        for (int k = 0; k < 3; k++)
        {
            refused ??= switcher.Ask(RoomCommand.Apply(switcher.Me, Pink, PinkFire, "Pink fire"));
            for (float t = 0f; t < 1.5f; t += Time.unscaledDeltaTime)
            {
                yield return null;
                worst = Mathf.Max(worst, Time.unscaledDeltaTime * 1000f);
            }
            on &= World.Room?.Mods.Any(m => m.Id == Pink) == true;
            composes.Add(grid.ComposeMs);
            switcher.Ask(RoomCommand.Revert(switcher.Me, Pink));
            yield return Wait(1f);
        }
        composes.Sort();
        double compose = composes[1];
        Check("pink_fire_on", refused == null && on, refused ?? switcher.LastError);
        Metric("switch_compose_ms_median", compose);
        Metric("switch_worst_frame_ms", worst);
        Check("switch_compose_within_budget", compose < ComposeBudgetMs, $"median {compose:0} ms of {string.Join(", ", composes.Select(c => c.ToString("0")))} (budget {ComposeBudgetMs:0})");
    }

    // The grid's Blockers object (CellBlockers' parent), by name: the same one after a rebuild when the boxes are kept.
    GameObject BlockersObject()
    {
        foreach (Transform child in grid.transform)
            if (child.name == "Blockers") return child.gameObject;
        return null;
    }
}
