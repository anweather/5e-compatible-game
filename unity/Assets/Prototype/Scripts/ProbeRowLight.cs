using System.Collections;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

// The probe's rowlight scenario: a stucco house and a stone one (the base terrain a castle is built of)
// through the MCP, beside a village house as the yardstick, by day, at dusk and at night. With --shots
// each is filmed close, for how it's lit and for anything flickering low on its walls.
public class RowLightProbe : RowHousesProbe
{
    const int StuccoX = 235, StuccoY = 390, StoneX = 297, StoneY = 352;
    const string StoneHouse = "probe-stone#1", Gatehouse = "probe-gate#1";
    const float Close = 12f, ClockWait = 4f;
    static readonly string[] Times = { "noon", "dusk", "23:00" };
    static readonly int LampGlow = Shader.PropertyToID("_LampGlow");

    protected override IEnumerator Run()
    {
        houses = grid.GetComponent<RowHouses>();
        Check("row_houses_here", houses != null && RulesMcpServer.Url != null, RulesMcpServer.Url);
        if (houses == null || RulesMcpServer.Url == null) yield break;
        Stand(StuccoX + 7, StuccoY + 12);
        yield return Tool("apply", new JObject { ["mod"] = TwoHouses(), ["title"] = Title });
        yield return Until(() => houses.Root(House) != null && houses.Root(StoneHouse) != null);
        Check("both_go_in", houses.Root(House) != null && houses.Root(StoneHouse) != null, answer);
        if (houses.Root(StoneHouse) == null) yield break;
        NothingGlows(House);
        NothingGlows(StoneHouse);
        var cell = new Vector2Int(StoneX, StoneY);
        Check("a_stone_wall_draws_no_block_inside_it", grid.transform.Cast<Transform>().All(t => t.name != $"stone {cell.x},{cell.y}"), "the cell's own dressing");
        var village = grid.World.BuildingsNow.First(r => !r.Id.StartsWith("cabin", System.StringComparison.Ordinal) && r.Id != House && r.Id != StoneHouse);
        foreach (string time in Times)
        {
            yield return Tool("set_time", new JObject { ["time"] = time });
            yield return Wait(ClockWait);
            string label = time.Replace(":", "");
            var panes = houses.Root(House).GetComponent<WindowPanes>();
            float glow = Shader.GetGlobalFloat(LampGlow);
            Check($"windows_lit_{label}", panes.LampsLit > 0 && (time == "noon" ? glow < 0.05f : glow > 0.4f),
                $"{panes.LampsLit} of {panes.panes.Length} panes with a lamp, lamps at {glow:0.00}");
            yield return Look("stucco-" + label, StuccoX + 7, StuccoY + 8, Close);
            yield return Look("stone-" + label, StoneX + 3, StoneY + 6, Close);
            yield return Look("village-" + label, village.X + 3, village.Y + 6, Close);
        }
        yield return RevertAll();
    }

    void NothingGlows(string id)
    {
        var alpha = houses.Root(id).GetComponentsInChildren<MeshFilter>().Where(f => f.name != "Panes").SelectMany(f => f.sharedMesh.colors).Max(c => c.a);
        Check($"no_wall_of_{id}_glows", alpha <= 0f, $"vertex alpha up to {alpha:0.00}: VillageLit reads it as a lit window");
    }

    static string TwoHouses()
    {
        var mod = JObject.Parse(ModJson(StuccoX, StuccoY));
        mod["world"]["buildings"][StoneHouse] = JObject.Parse(
            "{ \"at\": [" + StoneX + ", " + StoneY + "], \"seed\": 7, \"storeys\": 2, \"fill\": \"hollow\", \"floor\": { \"kind\": \"slab\", \"height\": 0.15 }," +
            " \"wall\": { \"material\": \"stone\", \"height\": 3, \"thickness\": 0.45 }," +
            " \"joins\": { \"a\": [0, 0], \"b\": [12, 0], \"c\": [12, 10], \"d\": [0, 10] }," +
            " \"walls\": { \"n\": { \"from\": \"a\", \"to\": \"b\" }, \"e\": { \"from\": \"b\", \"to\": \"c\" }," +
            " \"s\": { \"from\": \"c\", \"to\": \"d\", \"openings\": [{ \"kind\": \"door\", \"at\": 5 }, { \"kind\": \"window\", \"at\": 8 }] }, \"w\": { \"from\": \"d\", \"to\": \"a\" } }," +
            " \"roofs\": { \"main\": { \"over\": [\"a\", \"b\", \"c\", \"d\"], \"shape\": \"gable\", \"pitch\": [6, 12], \"overhang\": 0.4, \"cover\": \"slate\" } } }");
        // A gatehouse back to back with its wall: the nearest a castle's parts can come, since the core refuses a shared cell.
        mod["world"]["buildings"][Gatehouse] = JObject.Parse(
            "{ \"at\": [" + (StoneX + 2) + ", " + (StoneY + 5) + "], \"seed\": 8, \"fill\": \"hollow\", \"wall\": { \"material\": \"stone\", \"height\": 4, \"thickness\": 0.6 }," +
            " \"joins\": { \"a\": [0, 0], \"b\": [4, 0], \"c\": [4, 4], \"d\": [0, 4] }," +
            " \"walls\": { \"n\": { \"from\": \"a\", \"to\": \"b\" }, \"e\": { \"from\": \"b\", \"to\": \"c\" }, \"s\": { \"from\": \"c\", \"to\": \"d\" }, \"w\": { \"from\": \"d\", \"to\": \"a\" } } }");
        return mod.ToString(Newtonsoft.Json.Formatting.None);
    }
}
