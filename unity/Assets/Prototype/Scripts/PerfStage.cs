using System.Collections;
using System.Linq;
using UnityEngine;

// Sets the scene for a PerfScenario: noon, the weather held to one kind by a
// rule switch (the mod the probes use over sim.json), and the camera cut to a
// fixed shot, so every run films the same thing. The meadow, storm, snow and
// fog stand on the `perf` probe's camera; the wildfire is the `wildfire` probe's
// (the Wildfire mod on, Fire Bolts into the meadow north of the yard); the
// village films Hollowmere's square with the wizard standing in it. Ready says
// when it's done; the bot's route needs none of this.
public class PerfStage : ShowcaseScript
{
    // Hollowmere's place and square in cells (world/villages.json).
    const int HollowmereX = 222, HollowmereY = 300, SquareX = 5, SquareY = 6;
    static readonly int[] BoltsAt = { 2, 8, 14, 20, 25 };
    const float BoltGap = 0.4f, Noon = 12f;

    /// <summary>The scenario's name. Set before Start.</summary>
    public string Scenario { get; set; }
    /// <summary>Whether the scene is set.</summary>
    public bool Ready { get; private set; }

    protected override IEnumerator Play()
    {
        yield return Sky(Scenario is "storm" or "snow" or "fog" ? Scenario : "clear");
        if (Scenario == "wildfire") yield return Wildfire();
        else if (Scenario == "village") Hollowmere();
        else Cut(V(40f, 10f, 1f), V(28f, 0f, 15f));
        Ready = true;
    }

    // Noon, this weather and no other, ramped in at once.
    IEnumerator Sky(string only)
    {
        var kinds = new[] { "clear", "cloudy", "windy", "fog", "rain", "storm", "snow" };
        var weights = string.Join(", ", kinds.Select(k => $"'{k}': {(k == only ? 1 : 0)}"));
        return Switch(At(Noon) + ", 'weather': { 'calmPeriods': 0, 'rampMinutes': 0.02, 'weights': { " + weights + " } }");
    }

    // After Sky: a rule switch replaces the mods, a Toggle adds to them.
    IEnumerator Wildfire()
    {
        if (grid.TryGetComponent<RuleSwitcher>(out var switcher)) switcher.Toggle("wildfire");
        Place(13, 10);
        Cut(Yard(13, 4) + V(0f, 30f, -26f), Yard(13, 0));
        foreach (int x in BoltsAt) yield return Cast(SpellIds.FireBolt, x, 0, 0, BoltGap);
    }

    void Hollowmere()
    {
        int x = HollowmereX + SquareX, y = HollowmereY + SquareY;
        PlaceCell(x, y);
        var at = wizard.transform.position;
        Cut(at + V(-16f, 11f, -16f), at);
    }
}
