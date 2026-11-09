using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

// The probe's crossing scenario (view-rebuild): the world rebuilt from the map, as an online reset does, then the wizard
// runs north on a virtual pad across chunks as they draw. Metrics: what the rebuild took, the worst frame while running
// and how many frames were over 50 ms. Checks: it ran into new chunks and no frame stalled past the budget, no console
// errors. Frame times are the Editor's, which are slower than a player's.
public class CrossingProbe : ProbeScenario
{
    const float RunSeconds = 24f, HitchMs = 50f, WorstBudgetMs = 100f;
    Gamepad pad;

    protected override IEnumerator Run()
    {
        RadialPad();
        pad = InputSystem.AddDevice<Gamepad>("ProbePad");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        grid.ResetWorld();
        Metric("rebuild_ms", sw.Elapsed.TotalMilliseconds);
        yield return null;
        var from = grid.ChunkAt(wizard.transform.position);
        var up = new Vector2(0f, 1f);
        Hold(up, GamepadButton.LeftStick);
        yield return null;
        yield return null;
        Hold(up);
        var frames = new List<float>();
        var chunks = new HashSet<Vector2Int> { from };
        for (float t = 0f; t < RunSeconds; t += Time.unscaledDeltaTime)
        {
            yield return null;
            frames.Add(Time.unscaledDeltaTime * 1000f);
            chunks.Add(grid.ChunkAt(wizard.transform.position));
        }
        Hold(Vector2.zero);
        // The first frame after the rebuild is the rebuild's own.
        if (frames.Count > 0) frames.RemoveAt(0);
        float worst = 0f;
        int over = 0;
        foreach (var f in frames)
        {
            worst = Mathf.Max(worst, f);
            if (f > HitchMs) over++;
        }
        frames.Sort();
        Metric("worst_frame_ms", worst);
        Metric("frames_over_50ms", over);
        Metric("median_frame_ms", frames.Count > 0 ? frames[frames.Count / 2] : 0f);
        Metric("chunks_crossed", chunks.Count - 1);
        Check("ran_into_new_chunks", chunks.Count > 1, $"{chunks.Count} chunks from {from}");
        Check("no_stall", worst < WorstBudgetMs, $"worst frame {worst:0} ms, {over} over {HitchMs:0} ms");
    }

    protected override void OnDestroy()
    {
        if (pad != null) InputSystem.RemoveDevice(pad);
        base.OnDestroy();
    }

    void Hold(Vector2 stick, GamepadButton? button = null)
    {
        var state = new GamepadState { leftStick = stick };
        if (button.HasValue) state = state.WithButton(button.Value);
        InputSystem.QueueStateEvent(pad, state);
    }
}
