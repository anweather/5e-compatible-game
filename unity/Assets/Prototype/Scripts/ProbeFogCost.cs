using System.Collections;
using System.Linq;
using Chem;
using UnityEngine;

// The probe's fogcost scenario: what fog weather costs (BACKLOG 2, 9). The same
// view of the meadow in clear weather and then under fog weather, frame times over
// the same stretch of each, with how many cells carry its gas and how many FogBank
// draws. Lays no fog of its own: it's the weather's veil of a unit here and there.
public class FogCostProbe : ProbeScenario
{
    const float SampleSeconds = 6f, MostFogSeconds = 45f;
    float mean, p95, step, render;

    protected override IEnumerator Run()
    {
        Place(13, 10);
        Cut(V(40f, 10f, 1f), V(28f, 0f, 15f));
        yield return Switch(At(12f) + ", 'weather': { 'calmPeriods': 0, 'rampMinutes': 0.02, 'weights': " + Only("clear") + " }");
        yield return Wait(3f);
        yield return Sample();
        Metric("clear_mean_ms", mean);
        Metric("clear_p95_ms", p95);
        Metric("clear_step_ms", step);
        Metric("clear_render_ms", render);
        Shot("clear");
        yield return null;
        yield return null;

        float clearMean = mean;
        yield return Switch(At(12f) + ", 'weather': { 'calmPeriods': 0, 'rampMinutes': 0.02, 'weights': " + Only("fog") + " }");
        int gas = 0;
        float waited = 0f;
        for (; waited < MostFogSeconds; waited += 3f)
        {
            yield return Wait(3f);
            int now = World.Cells.Count(c => c.Gas == GasIds.Fog);
            if (now <= gas * 1.03f && now > 0) break;
            gas = now;
        }
        gas = World.Cells.Count(c => c.Gas == GasIds.Fog);
        Metric("fog_settle_s", waited);
        Metric("fog_cells", gas);
        Metric("cells_held", World.Cells.Count());
        int drawn = grid.Fog != null ? grid.Fog.SquaresShown : 0;
        Metric("fog_squares_drawn", drawn);
        Check("fog_lies", World.Weather.Kind == WeatherKind.Fog && gas > 500, $"{World.Weather.Kind}, {gas} cells of fog gas");
        Check("veil_not_drawn_cell_by_cell", drawn < gas / 10, $"{drawn} of {gas} cells have billboards (it's the sky's distance fog)");
        yield return Sample();
        Metric("fog_mean_ms", mean);
        Metric("fog_p95_ms", p95);
        Metric("fog_extra_ms", mean - clearMean);
        Metric("fog_step_ms", step);
        Metric("fog_render_ms", render);
        Shot("fog");
        yield return null;
        yield return null;
        Cut(V(26f, 2.4f, 4f), V(34f, 1f, 22f));
        yield return Wait(0.5f);
        Shot("fog-low");
        yield return null;
        yield return null;
    }

    IEnumerator Sample()
    {
        var frames = new System.Collections.Generic.List<float>();
        step = render = 0f;
        for (float t = 0f; t < SampleSeconds; t += Time.unscaledDeltaTime)
        {
            frames.Add(Time.unscaledDeltaTime * 1000f);
            step += (float)grid.StepMs;
            render += (float)grid.RenderMs;
            yield return null;
        }
        step /= frames.Count;
        render /= frames.Count;
        frames.Sort();
        mean = frames.Average();
        p95 = frames[(int)(frames.Count * 0.95f)];
    }

    static string Only(string kind) =>
        "{ " + string.Join(", ", new[] { "clear", "cloudy", "windy", "fog", "rain", "storm", "snow" }.Select(k => $"'{k}': {(k == kind ? 1 : 0)}")) + " }";
}
