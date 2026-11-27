// Weather mods applied by hand, as rule patches through RuleSwitcher: Andrew's constant meteor showers and a
// storm that sets in at once. The showerstorm perf scenario (PerfStage) runs them in a player build, and the
// waterlook and showerstorm probes (Probes/) use the same patches.
public static class WeatherMods
{
    public const string Meteors = "{ \"title\": \"Constant meteor showers\", \"patch\": { \"sim\": { \"weather\": { \"calmPeriods\": 0, \"rampMinutes\": 0.02, \"weights\": { \"clear\": 1, \"cloudy\": 0, \"windy\": 0, \"fog\": 0, \"rain\": 0, \"storm\": 0, \"snow\": 0 }, \"meteors\": { \"showerChance\": 1, \"every\": 1 } } } } }";

    public const string Storm = "{ \"title\": \"Storm\", \"patch\": { \"sim\": { \"weather\": { \"calmPeriods\": 0, \"rampMinutes\": 0.02, \"weights\": { \"clear\": 0, \"cloudy\": 0, \"windy\": 0, \"fog\": 0, \"rain\": 0, \"storm\": 1, \"snow\": 0 } } } } }";
}
