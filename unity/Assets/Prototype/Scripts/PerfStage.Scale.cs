using System.Collections;
using Chem;
using Newtonsoft.Json.Linq;
using UnityEngine;

// The scale probe's two additions (docs/perf/2026-11-10-scale.md), the same on the nine and on the scale island:
// walkout, a long walk east from the nine's spawn out of the nine (onto the grass on the island; to the map's edge on
// the nine), and -perf-switches, a sim-only rule switch then a look-changing one during a fixed-camera run, each timed
// over the frames after it (PERF LOOK SWITCH lines).
public partial class PerfStage
{
    // From the nine's spawn to its east edge, in cells (its spawn (292, 1480) in a 1728-wide map); the walk starts four
    // chunks in from it and walks a chunk a leg.
    const int SpawnToNineEastEdge = 1728 - 292, WalkOutStartIn = 4, WalkOutLegs = 16;
    const float SwitchesFromSeconds = 6f;

    IEnumerator WalkOut()
    {
        var spawn = grid.PlayerStart;
        int x0 = spawn.x + SpawnToNineEastEdge - WalkOutStartIn * ChunkCells;
        Teleport(CellGround(x0, spawn.y) + Vector3.up * 0.1f);
        var inputs = wizard.GetComponent<StarterAssets.StarterAssetsInputs>();
        inputs.cursorInputForLook = false;
        Debug.Log($"PERF WALKOUT from={x0},{spawn.y} to={Mathf.Min(x0 + WalkOutLegs * ChunkCells, World.W - 4)},{spawn.y} map_w={World.W}");
        while (!Go) yield return null;
        for (int leg = 1; leg <= WalkOutLegs && enabled; leg++)
        {
            int x = Mathf.Min(x0 + leg * ChunkCells, World.W - 4);
            yield return WalkTo(CellGround(x, spawn.y), inputs);
            PerfCounts.Add("walk.legs");
        }
        inputs.move = Vector2.zero;
        inputs.sprint = false;
        Debug.Log($"PERF WALKOUT done at_s={Time.realtimeSinceStartup:0} active_chunks={World.ActiveChunks.Count}");
    }

    Vector3 CellGround(int x, int y)
    {
        var at = grid.CellCenter(x, y);
        at.y = grid.GroundAt(at);
        return at;
    }

    // A sim-only switch (fire spreads a little more: nothing drawn changes), then a look switch (blue grass: every grass
    // cell's look), each on, held and off.
    IEnumerator Switches()
    {
        while (!Go) yield return null;
        yield return new WaitForSecondsRealtime(SwitchesFromSeconds);
        var switcher = grid.GetComponent<RuleSwitcher>();
        if (switcher == null)
        {
            Debug.Log("PERF LOOK SWITCH none: no RuleSwitcher");
            yield break;
        }
        string sim = "{ \"title\": \"Quicker fire\", \"patch\": { \"sim\": { \"fire\": { \"spreadChance\": 0.55 } } } }";
        yield return TimeSwitch(switcher, "sim", sim);
        var rules = JObject.Parse(World.Rules.ToJson());
        var grass = (JObject)(rules["terrain"]?[TerrainIds.Grass] ?? rules["materials"]["terrain"][TerrainIds.Grass]).DeepClone();
        grass["look"]["color"] = new JArray(0.25, 0.45, 1);
        yield return TimeSwitch(switcher, "grass", "{ \"title\": \"Blue grass\", \"patch\": { \"terrain\": { \"grass\": " + grass + " } } }");
    }
}
