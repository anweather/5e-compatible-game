using System.Collections;
using System.Linq;
using Chem;
using UnityEngine;

// The probe's landshapes scenario: a canyon and a cliff through the real MCP server, with the wizard on them. The ground
// goes from under the feet and comes back, the walls are walls, the rocks
// grow on the faces and go with an undo, and the terrain draws with the rock shader.
public class LandShapesProbe : ProbeScenario
{
    // Open ground the size of a full stamp, clear of what stamps keep off.
    const int X = 110, Y = 450, Reach = 32, Wall = 36;
    const float EaseSeconds = 40f;

    string said;

    IEnumerator Say(string tool, string args)
    {
        var call = RoomMcpProbe.Post(RoomMcpProbe.Tool(tool, args));
        yield return RoomMcpProbe.Until(call);
        said = call.Result.body ?? "";
        Check(tool, call.Result.status == 200 && !said.Contains("\"isError\":true"), said.Length > 240 ? said.Substring(0, 240) : said);
    }

    IEnumerator Land(string profile, int wall)
    {
        yield return Say("act", $"{{\"what\":\"land\",\"near\":\"[{X}, {Y}]\",\"radius\":{Reach},\"rise\":{wall},\"profile\":\"{profile}\",\"along\":\"east\"}}");
        Check($"{profile}_moved_the_land", said.Contains("moved the land"), said.Length > 200 ? said.Substring(0, 200) : said);
        yield return Wait(EaseSeconds);
    }

    IEnumerator Undone(string name, LandRocks rocks)
    {
        yield return Say("undo_act", "{}");
        yield return Wait(EaseSeconds);
        Check($"{name}_rocks_go_with_it", rocks.Count == 0, $"{rocks.Count} rocks left");
    }

    int Steepest(World w) =>
        Enumerable.Range(Y - Reach, 2 * Reach).Max(y => Enumerable.Range(X - Reach, 2 * Reach).Max(x => w.LandDrop(x, y)));

    protected override IEnumerator Run()
    {
        var server = RulesMcpServer.Instance;
        if (!server.Serving) server.Serving = true;
        var valley = FindAnyObjectByType<ValleyChemistry>();
        var world = grid.World;
        yield return Wait(1f);
        Check("terrain_draws_rock_from_the_sides", Terrain.activeTerrains[0].materialTemplate.shader.name == "Prototype/TerrainRock", Terrain.activeTerrains[0].materialTemplate.shader.name);

        // On the spot: the floor drops under the wizard's feet.
        PlaceCell(X, Y);
        yield return Wait(1f);
        float before = valley.Ground.ShownGround(X, Y);
        yield return Land(LandStamp.Canyon, Wall);
        Metric("canyon_floor_m", valley.Ground.ShownGround(X, Y));
        Metric("canyon_steepest_m_a_cell", Steepest(world) / 8f);
        Metric("canyon_rocks", valley.Rocks.Count);
        Check("the_floor_is_down", valley.Ground.ShownGround(X, Y) < before - 10f, $"{valley.Ground.ShownGround(X, Y):0.0} m, from {before:0.0} m");
        Check("the_walls_are_walls", Steepest(world) >= 40, $"{Steepest(world) / 8f:0.0} m in a cell");
        Check("rocks_grow_on_the_faces", valley.Rocks.Count > 100, $"{valley.Rocks.Count} rocks");
        Shot("canyon");
        yield return Undone("canyon", valley.Rocks);

        // Beside it: a face to stand under.
        PlaceCell(X, Y + 16);
        yield return Wait(1f);
        yield return Land(LandStamp.Cliff, Wall - 6);
        Metric("cliff_rocks", valley.Rocks.Count);
        Check("the_cliff_grows_rocks", valley.Rocks.Count > 50, $"{valley.Rocks.Count} rocks");
        Shot("cliff");
        yield return Undone("cliff", valley.Rocks);
        Check("the_land_is_back", world.LandHash() == "", world.LandHash());
    }
}
