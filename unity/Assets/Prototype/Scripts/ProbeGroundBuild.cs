using System.Collections;
using System.Linq;
using UnityEngine;

// The probe's groundbuild scenario: the chunk terrains the scene started with (MapTerrains: the scene holds none) and what
// it cost to start a world. For the land alone: echo land > unity/Temp/ground-builder (rm it after), then tools/probe.sh groundbuild.
public class GroundBuildProbe : ProbeScenario
{
    protected override IEnumerator Run()
    {
        yield return Wait(1f);
        var valley = FindAnyObjectByType<ValleyChemistry>();
        string mode = GroundSwitch.Mode;
        var terrains = Terrain.activeTerrains;
        int built = terrains.Count(t => t.terrainData.name.StartsWith("Built chunk", System.StringComparison.Ordinal));
        Metric("mode_is_land", mode == GroundSwitch.Land ? 1 : 0);
        Metric("built_chunks", built);
        Metric("bind_ms", valley.Ground.BindMs);
        Check("the_chunks_are_built", terrains.Length == 81 && built == terrains.Length, $"{mode}: {built} of {terrains.Length} built");
        Check("the_scene_holds_no_ground", terrains.All(t => t.GetComponentInParent<MapTerrains>() != null), "every terrain is one MapTerrains made");
        Check("colliders_match", terrains.All(t => t.GetComponent<TerrainCollider>().terrainData == t.terrainData));
        Check("terrain_draws_rock_from_the_sides", terrains.All(t => t.materialTemplate.shader.name == "Prototype/TerrainRock"));
    }
}
