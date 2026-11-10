using System.Collections;
using System.Linq;
using Chem;
using UnityEngine;

// The probe's act scenario (docs/design/world-acts.md): acts through the real MCP server in a running game, as Claude
// sends them. Twenty trees near the wizard, twice, leave the mod list as it was and stand on the cells; animals come
// out in ValleyFauna; undo_act takes the trees back; and a species recoloured by set_species is built again in its new coat.
public class ActProbe : ProbeScenario
{
    const int Trees = 20;
    const float Settle = 1.5f;
    const string Blue = "[0.1, 0.2, 0.9]";

    string said;

    IEnumerator Say(string tool, string args)
    {
        var call = RoomMcpProbe.Post(RoomMcpProbe.Tool(tool, args));
        yield return RoomMcpProbe.Until(call);
        said = call.Result.body ?? "";
        Check(tool, call.Result.status == 200 && !said.Contains("\"isError\":true"), said.Length > 240 ? said.Substring(0, 240) : said);
    }

    int TreeCells => grid.World.AllCells().Count(c => c.T == TerrainIds.Tree);

    protected override IEnumerator Run()
    {
        var server = RulesMcpServer.Instance;
        if (!server.Serving) server.Serving = true;
        yield return Wait(Settle);
        var fauna = FindAnyObjectByType<ValleyFauna>();
        int mods = grid.World.Room.Mods.Count, trees = TreeCells;

        yield return Say("act", $"{{\"what\":\"tree\",\"count\":{Trees},\"spacing\":2}}");
        yield return Say("act", $"{{\"what\":\"tree\",\"count\":{Trees},\"spacing\":2}}");
        yield return Wait(Settle);
        Check("forty_trees_stand", TreeCells - trees == 2 * Trees, $"{TreeCells - trees} more trees");
        Check("no_new_mods", grid.World.Room.Mods.Count == mods, $"{grid.World.Room.Mods.Count} mods, were {mods}");
        Shot("trees");

        int summoned = grid.World.LastSummonN, animals = fauna != null ? fauna.Count : 0;
        yield return Say("act", "{\"what\":\"deer\",\"count\":3}");
        yield return Wait(Settle);
        Check("the_deer_are_summoned", grid.World.LastSummonN > summoned, $"last {grid.World.LastSummonN}, was {summoned}");
        Check("the_view_brings_them_out", fauna != null && fauna.Count > animals, $"{(fauna != null ? fauna.Count : 0)} out, were {animals}");

        yield return Say("undo_act", "{}");
        Check("a_summon_cant_be_undone", said.Contains("can't be taken back"), said.Length > 200 ? said.Substring(0, 200) : said);
        yield return Say("undo_act", "{\"act\":2}");
        yield return Wait(Settle);
        Check("undo_takes_the_second_trees", TreeCells - trees == Trees, $"{TreeCells - trees} more trees");

        yield return Say("set_species", $"{{\"id\":\"turkey\",\"color\":\"{Blue}\"}}");
        string draft = RoomMcpProbe.DraftIn(said) ?? "";
        yield return Say("apply", $"{{\"mod\":\"{draft}\",\"title\":\"Blue turkeys\"}}");
        yield return Wait(Settle);
        Check("only_the_turkey_is_built_again", fauna != null && fauna.Rebuilt == 1, $"{(fauna != null ? fauna.Rebuilt : 0)} bodies built at the switch");
        var coat = grid.World.Rules.Fauna.Species("turkey")?.Coat;
        Check("the_turkey_is_blue", coat != null && coat.Base[2] > 0.8, coat != null ? string.Join(",", coat.Base) : "no row");
    }
}
