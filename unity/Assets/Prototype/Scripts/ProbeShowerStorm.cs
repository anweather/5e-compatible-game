using System.Collections;
using Chem;

// The probe's showerstorm scenario (issue #70, as Andrew's evening went on): over Hollowmere's square,
// a quiet stretch, then a minute of his meteor mod's shower, then a storm and its rain for a few
// minutes, the camera still. Each stretch logs the view's markers, the tick's stages and the view's
// counts a frame (WaterLookProbe.Stretch), so what grows with the storm and the chunks it wakes shows.
// Checks that the storm came, that redrawn tiles keep their noise and that their points drawn from
// kept cells are what the delegates drew; the rest is for a
// performance pass (docs/perf/2026-11-03-water.md).
public class ShowerStormProbe : WaterLookProbe
{
    public const string Storm = "{ \"title\": \"Storm\", \"patch\": { \"sim\": { \"weather\": { \"calmPeriods\": 0, \"rampMinutes\": 0.02, \"weights\": { \"clear\": 0, \"cloudy\": 0, \"windy\": 0, \"fog\": 0, \"rain\": 0, \"storm\": 1, \"snow\": 0 } } } } }";
    static readonly float[] StormStretchesAt = { 30f, 90f, 150f };
    // Noise points worked out per tile cell drawn: about 8 when every redraw works it all out again.
    const long MostNoisePerCell = 2;
    const float KeptCheckSeconds = 10f;

    protected override IEnumerator Run()
    {
        switcher = grid.GetComponent<RuleSwitcher>();
        Check("switcher_in_scene", switcher != null);
        if (switcher == null) yield break;
        PlaceCell(HollowmereX + SquareX, HollowmereY + SquareY);
        var at = wizard.transform.position;
        Cut(at + V(-16f, 11f, -16f), at);
        yield return Wait(SettleAfterSwitch);
        yield return Stretch("quiet", _ => { });

        switcher.Ask(RoomCommand.Apply(switcher.Me, "meteors", Meteors, "Constant meteor showers"));
        yield return Wait(ShowerSeconds);
        switcher.Ask(RoomCommand.Revert(switcher.Me, "meteors"));
        yield return Wait(AfterShower);
        yield return Stretch("shower", _ => { });

        switcher.Ask(RoomCommand.Apply(switcher.Me, "storm", Storm, "Storm"));
        float since = 0f;
        long boxCells = 0, noisePoints = 0;
        foreach (float due in StormStretchesAt)
        {
            yield return Wait(due - since);
            yield return Stretch($"storm_{due:0}s", _ => { });
            since = due + Hold;
            boxCells += LastCounts.TryGetValue("tile.box_cells", out long cells) ? cells : 0;
            noisePoints += LastCounts.TryGetValue("tile.noise_points", out long points) ? points : 0;
        }
        // A tile works its lattice's noise out once and keeps it (CellContour): before, every redraw
        // worked out about 8 points a cell, most of an outline's cost, and the storm redraws all the time.
        Check("redrawn_tiles_keep_their_noise", boxCells > 0 && noisePoints < boxCells * MostNoisePerCell,
            $"{noisePoints} noise points over {boxCells} tile cells drawn in the storm");
        // A tile's points from its cells read once (CellWaterMesh.KeptCells) are what the delegates
        // drew: for a while every tile is drawn both ways and compared bit for bit.
        var (drawnBefore, differentBefore, verticesBefore, keptBefore, perPointBefore) = CellWaterMesh.KeptPointsChecked;
        CellWaterMesh.CheckKeptPoints = CellWaterMesh.KeptPoints;
        try
        {
            yield return Wait(KeptCheckSeconds);
        }
        finally
        {
            CellWaterMesh.CheckKeptPoints = false;
        }
        var (drawn, different, vertices, keptMs, perPointMs) = CellWaterMesh.KeptPointsChecked;
        double perVertex = 1000.0 / System.Math.Max(1, vertices - verticesBefore);
        Check("kept_points_draw_what_delegates_did", drawn > drawnBefore && different == differentBefore,
            $"{drawn - drawnBefore} tiles drawn both ways in the storm, {different - differentBefore} different; points " +
            $"{(keptMs - keptBefore) * perVertex:0.000} us a vertex kept, {(perPointMs - perPointBefore) * perVertex:0.000} through delegates");
        Check("storm_came", grid.World.Weather.Kind is WeatherKind.Storm or WeatherKind.Rain, grid.World.Weather.Kind.ToString());
        switcher.Ask(RoomCommand.Revert(switcher.Me, "storm"));
    }
}
