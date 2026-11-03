using System.Collections;
using Chem;

// The probe's showerstorm scenario (issue #70, as Andrew's evening went on): over Hollowmere's square,
// a quiet stretch, then a minute of his meteor mod's shower, then a storm and its rain for a few
// minutes, the camera still. Each stretch logs the view's markers, the tick's stages and the view's
// counts a frame (WaterLookProbe.Stretch), so what grows with the storm and the chunks it wakes shows.
// Checks only that it ran: the numbers are for a performance pass (docs/perf/).
public class ShowerStormProbe : WaterLookProbe
{
    public const string Storm = "{ \"title\": \"Storm\", \"patch\": { \"sim\": { \"weather\": { \"calmPeriods\": 0, \"rampMinutes\": 0.02, \"weights\": { \"clear\": 0, \"cloudy\": 0, \"windy\": 0, \"fog\": 0, \"rain\": 0, \"storm\": 1, \"snow\": 0 } } } } }";
    static readonly float[] StormStretchesAt = { 30f, 90f, 150f };

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
        foreach (float due in StormStretchesAt)
        {
            yield return Wait(due - since);
            yield return Stretch($"storm_{due:0}s", _ => { });
            since = due + Hold;
        }
        Check("storm_came", grid.World.Weather.Kind is WeatherKind.Storm or WeatherKind.Rain, grid.World.Weather.Kind.ToString());
        switcher.Ask(RoomCommand.Revert(switcher.Me, "storm"));
    }
}
