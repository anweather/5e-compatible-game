using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using Chem;
using UnityEngine;

// The probe's waterlook scenario (issue #70): a look on water costs at its rule switch, never every
// frame. Andrew's "Purple Create Water" (a look by player 0) and "All water purple" (the water's own
// colour), the patches as his chat record kept them, go on and off through the room as Claude's
// apply puts them, over Hollowmere's square: first on the quiet village, then after his meteor mod's
// shower has burnt and cratered it and left water everywhere, as his evening went. On, off, on, off,
// each stretch with the camera still, reading the view's markers and counts a frame. The shower
// itself is the cost his play log showed (then a storm and rain): the stretches say so too.
public class WaterLookProbe : ProbeScenario
{
    const string ByMe = "{ \"title\": \"Purple Create Water\", \"patch\": { \"elements\": { \"water\": { \"look\": { \"scoped\": [ { \"by\": 0, \"color\": [0.6, 0.2, 0.85] } ] } } }, \"spells\": [ { \"id\": \"create-water\", \"look\": { \"color\": [0.6, 0.2, 0.85] } } ] } }";
    const string AllWater = "{ \"title\": \"All water purple\", \"patch\": { \"elements\": { \"water\": { \"look\": { \"color\": [0.6, 0.2, 0.85] } } }, \"spells\": [ { \"id\": \"create-water\", \"look\": { \"color\": [0.6, 0.2, 0.85] } }, { \"id\": \"flood\", \"look\": { \"color\": [0.6, 0.2, 0.85] } }, { \"id\": \"wave\", \"look\": { \"color\": [0.6, 0.2, 0.85] } } ] } }";
    // His "Constant meteor showers", in a clear stretch so the shower comes at once.
    const string Meteors = "{ \"title\": \"Constant meteor showers\", \"patch\": { \"sim\": { \"weather\": { \"calmPeriods\": 0, \"rampMinutes\": 0.02, \"weights\": { \"clear\": 1, \"cloudy\": 0, \"windy\": 0, \"fog\": 0, \"rain\": 0, \"storm\": 0, \"snow\": 0 }, \"meteors\": { \"showerChance\": 1, \"every\": 1 } } } } }";
    // Hollowmere's place and square in cells (world/villages.json, PerfStage).
    const int HollowmereX = 222, HollowmereY = 300, SquareX = 5, SquareY = 6;
    const float SettleAfterSwitch = 3f, Hold = 4f, ShowerSeconds = 60f, AfterShower = 15f;
    // A stretch with the looks on may cost no more a frame than the stretches round it with them off, by this or this share.
    const float MostExtraMs = 1f, MostExtraShare = 0.2f;

    static readonly string[] Markers = { "ChemGrid.Render", "ChemGrid.FinishRender", "ChemGrid.WaterMesh", "ChemGrid.SlickMesh", "ValleyGround.Flush" };

    RuleSwitcher switcher;

    protected override IEnumerator Run()
    {
        switcher = grid.GetComponent<RuleSwitcher>();
        Check("switcher_in_scene", switcher != null);
        if (switcher == null) yield break;
        PlaceCell(HollowmereX + SquareX, HollowmereY + SquareY);
        var at = wizard.transform.position;
        Cut(at + V(-16f, 11f, -16f), at);
        yield return Wait(SettleAfterSwitch);

        double quiet = 0, quietBoth = 0;
        yield return Stretch("quiet", ms => quiet = ms);
        yield return Looks(true);
        yield return Stretch("quiet_both", ms => quietBoth = ms);
        yield return Looks(false);
        Check("quiet_looks_cost_nothing_a_frame", Within(quietBoth, quiet), $"ChemGrid.Render {quiet:0.00} ms off, {quietBoth:0.00} ms on");

        switcher.Ask(RoomCommand.Apply(switcher.Me, "meteors", Meteors, "Constant meteor showers"));
        yield return Wait(ShowerSeconds);
        Metric("shower_burning_cells", BurningCells());
        switcher.Ask(RoomCommand.Revert(switcher.Me, "meteors"));
        yield return Wait(AfterShower);

        double off = 0, on = 0, offAgain = 0, onAgain = 0;
        yield return Stretch("shower_off", ms => off = ms);
        yield return Looks(true);
        yield return Stretch("shower_both", ms => on = ms);
        Check("water_drawn_purple", grid.CellWaterColor.r > grid.CellWaterColor.g, $"{grid.CellWaterColor}");
        yield return Looks(false);
        yield return Stretch("shower_off_again", ms => offAgain = ms);
        yield return Looks(true);
        yield return Stretch("shower_both_again", ms => onAgain = ms);
        yield return Looks(false);
        Check("shower_looks_cost_nothing_a_frame", Within((on + onAgain) / 2, (off + offAgain) / 2),
            $"ChemGrid.Render {off:0.00} and {offAgain:0.00} ms off, {on:0.00} and {onAgain:0.00} ms on");
        Check("shower_costs_more_than_quiet", (off + offAgain) / 2 > quiet, $"ChemGrid.Render {quiet:0.00} ms quiet, {(off + offAgain) / 2:0.00} ms after the shower");
    }

    static bool Within(double on, double off) => on <= off + System.Math.Max(MostExtraMs, off * MostExtraShare);

    // Both looks on through the room, as Claude's apply puts them, or both off.
    IEnumerator Looks(bool on)
    {
        if (on)
        {
            switcher.Ask(RoomCommand.Apply(switcher.Me, "purple-create-water", ByMe, "Purple Create Water"));
            switcher.Ask(RoomCommand.Apply(switcher.Me, "all-water-purple", AllWater, "All water purple"));
        }
        else
        {
            switcher.Ask(RoomCommand.Revert(switcher.Me, "all-water-purple"));
            switcher.Ask(RoomCommand.Revert(switcher.Me, "purple-create-water"));
        }
        yield return Wait(SettleAfterSwitch);
    }

    // A stretch with the camera still: each marker's mean a frame, the frame's, and the view's counts
    // a frame (PerfCounts), as metrics and one log line each; ChemGrid.Render's mean goes to <paramref name="render"/>.
    IEnumerator Stretch(string name, System.Action<double> render)
    {
        var recorders = new Unity.Profiling.ProfilerRecorder[Markers.Length];
        for (int i = 0; i < Markers.Length; i++) recorders[i] = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Scripts, Markers[i]);
        var wide = new PerfMarkers(wide: true);
        var means = new double[Markers.Length];
        var counts = new SortedDictionary<string, long>();
        int frames = 0;
        double frameMs = 0;
        PerfCounts.On = true;
        yield return null;
        for (float t = 0f; t < Hold; t += Time.unscaledDeltaTime)
        {
            PerfCounts.NextFrame();
            yield return null;
            frames++;
            frameMs += Time.unscaledDeltaTime * 1000.0;
            for (int i = 0; i < Markers.Length; i++) means[i] += recorders[i].LastValue / 1e6;
            wide.Sample();
            foreach (var kv in PerfCounts.Frame) counts[kv.Key] = (counts.TryGetValue(kv.Key, out long had) ? had : 0) + kv.Value;
        }
        PerfCounts.On = false;
        PerfCounts.NextFrame();
        frames = Mathf.Max(1, frames);
        var inv = CultureInfo.InvariantCulture;
        var line = new System.Text.StringBuilder($"PROBE WATERLOOK {name} frames={frames} chunks={World.ActiveChunks.Count} frame={(frameMs / frames).ToString("0.00", inv)}");
        for (int i = 0; i < Markers.Length; i++)
        {
            means[i] /= frames;
            Metric($"{name}_{Markers[i]}_ms", means[i]);
            line.Append(' ').Append(Markers[i]).Append('=').Append(means[i].ToString("0.00", inv));
            recorders[i].Dispose();
        }
        Metric($"{name}_frame_ms", frameMs / frames);
        foreach (string key in new[] { "water.wet_cells", "water.dirty_tiles", "water.tiles_drawn", "water.changed" })
            Metric($"{name}_{key}", counts.TryGetValue(key, out long n) ? (double)n / frames : 0);
        Debug.Log(line.Append(wide.WindowSummary()).ToString());
        var countLine = new System.Text.StringBuilder($"PROBE WATERLOOK {name} counts a frame:");
        foreach (var kv in counts) countLine.Append(' ').Append(kv.Key).Append('=').Append(((double)kv.Value / frames).ToString("0.0", inv));
        Debug.Log(countLine.ToString());
        wide.Dispose();
        render(means[0]);
    }
}
