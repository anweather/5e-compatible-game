using System.Collections;
using System.Linq;
using UnityEngine;

// The probe's groundbuild scenario: what the switch (GroundSwitch) put on the terrains and what it cost to start a world.
// Run it off and on and compare: echo on > unity/Temp/ground-builder (rm it for off), then tools/probe.sh groundbuild.
public class GroundBuildProbe : ProbeScenario
{
    protected override IEnumerator Run()
    {
        yield return Wait(1f);
        var valley = FindAnyObjectByType<ValleyChemistry>();
        string mode = GroundSwitch.Mode;
        var terrains = Terrain.activeTerrains;
        int built = terrains.Count(t => t.terrainData.name.StartsWith("Built chunk", System.StringComparison.Ordinal));
        Metric("mode_is_on", mode == GroundSwitch.Off ? 0 : 1);
        Metric("built_chunks", built);
        Metric("bind_ms", valley.Ground.BindMs);
        Check("the_chunks_are_as_switched", mode == GroundSwitch.Off ? built == 0 : built == terrains.Length, $"{mode}: {built} of {terrains.Length} built");
        Check("colliders_match", terrains.All(t => t.GetComponent<TerrainCollider>().terrainData == t.terrainData));
        Check("terrain_draws_rock_from_the_sides", terrains.All(t => t.materialTemplate.shader.name == "Prototype/TerrainRock"));
    }
}
