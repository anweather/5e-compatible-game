using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// The probe's wildfire scenario (docs/FAST_LOOP.md): a lot of fire, for frame
// times. The Wildfire mod on, Fire Bolts into the meadow around the yard, and
// 20 s of it burning with the camera on it. Reports the frame times while it
// burns, how many squares burn at most, and where a frame's time goes (the
// grid's step, drawing the cells, the fire lights) and how many frames are
// slow (over 20 ms), how slow on average, and the slowest. For what a slow
// frame spent its time on, record the run in the Profiler (its ChemGrid and
// ValleyGround markers). Checks the fire spread and that frames stay under the
// p95 budget.
public class WildfireProbe : ProbeScenario
{
    const float BurnSeconds = 20f;
    /// <summary>The p95 frame time a big fire may take in Editor Play mode (60 fps is 16.7 ms).</summary>
    const float P95BudgetMs = 33f;
    /// <summary>A frame slower than this counts as slow (under 50 fps).</summary>
    const float SlowMs = 20f;

    protected override IEnumerator Run()
    {
        var switcher = grid.GetComponent<RuleSwitcher>();
        Check("wildfire_on", switcher != null && switcher.Toggle("wildfire") == null, "the Wildfire mod switched on");
        yield return Wait(0.5f);

        Place(13, 10);
        Cut(Yard(13, 4) + V(0f, 30f, -26f), Yard(13, 0));
        // Bolts north into the meadow beyond the yard, spread along it.
        foreach (int x in new[] { 2, 8, 14, 20, 25 })
        {
            yield return CastFromBar(SpellIds.FireBolt, x, 0, 0, 0.4f);
        }

        var frames = new List<float>();
        double step = 0, render = 0, lights = 0, surfaces = 0, trees = 0, ground = 0, worstTrees = 0, worstGround = 0, worstRender = 0;
        int peak = 0;
        for (float t = 0f; t < BurnSeconds; t += Time.unscaledDeltaTime)
        {
            frames.Add(Time.unscaledDeltaTime * 1000f);
            step += grid.StepMs;
            render += grid.RenderMs;
            lights += grid.LightsMs;
            surfaces += grid.SurfacesMs;
            worstRender = System.Math.Max(worstRender, grid.RenderMs);
            if (grid.valley != null)
            {
                trees += grid.valley.TreesMs;
                ground += grid.valley.GroundMs;
                worstTrees = System.Math.Max(worstTrees, grid.valley.TreesMs);
                worstGround = System.Math.Max(worstGround, grid.valley.GroundMs);
            }
            if (frames.Count % 30 == 1) peak = Mathf.Max(peak, BurningCells());
            if (t < 6f && t + Time.unscaledDeltaTime >= 6f) Shot("mid-burn");
            yield return null;
        }
        Shot("burning");

        float slowest = frames.Max();
        var slow = frames.Where(f => f > SlowMs).ToList();
        Metric("slow_frames", slow.Count);
        Metric("slow_mean_ms", slow.Count > 0 ? slow.Average() : 0);
        Metric("worst_frame_ms", slowest);
        frames.Sort();
        float p95 = frames[(int)(frames.Count * 0.95f)];
        float mean = frames.Average();
        Metric("burning_peak", peak);
        Metric("burn_mean_ms", mean);
        Metric("burn_p95_ms", p95);
        Metric("burn_step_ms", step / frames.Count);
        Metric("burn_render_ms", render / frames.Count);
        Metric("burn_lights_ms", lights / frames.Count);
        Metric("burn_surfaces_ms", surfaces / frames.Count);
        Metric("burn_trees_ms", trees / frames.Count);
        Metric("burn_ground_ms", ground / frames.Count);
        Metric("worst_render_ms", worstRender);
        Metric("worst_trees_ms", worstTrees);
        Metric("worst_ground_ms", worstGround);
        Metric("fire_objects", grid.transform.GetComponentsInChildren<ParticleSystem>().Length);
        Check("fire_spread", peak > 150, $"{peak} squares burning at most");
        Check("frames_within_budget", p95 <= P95BudgetMs, $"p95 {p95:0.0} ms (budget {P95BudgetMs} ms), mean {mean:0.0} ms, {peak} squares burning");
    }
}
