using System.Collections;
using System.Linq;
using Chem;
using Newtonsoft.Json.Linq;
using Unity.Mathematics;
using UnityEngine;

// The probe's faunarows scenario (docs/FAST_LOOP.md): a row changed in a running game.
// A deer and a fox stand in the meadow; a mod changes the deer's row (a red coat, twice as
// fast) and only the deer's body is built again, the animals stay where they are with their
// seeds, and the room's fingerprint doesn't move. Switching it off brings the deer back.
public class FaunaRowsProbe : ProbeScenario
{
    const string Mod = "red-deer";

    protected override IEnumerator Run()
    {
        var fauna = FindAnyObjectByType<ValleyFauna>();
        Check("fauna_built", fauna != null && fauna.SpeciesCount >= 50, $"{(fauna != null ? fauna.SpeciesCount : 0)} species from the rows");
        if (fauna == null) yield break;

        var w = wizard.transform.position;
        int deerKind = fauna.SpeciesIndex("deer"), foxKind = fauna.SpeciesIndex("red-fox");
        int deer = fauna.Id(fauna.Spawn(deerKind, Ground(w + new Vector3(1.5f, 0f, 5f)), math.PI));
        int fox = fauna.Id(fauna.Spawn(foxKind, Ground(w + new Vector3(-1.5f, 0f, 5f)), math.PI));
        fauna.Puppet(fauna.IndexOf(deer), math.PI, 0f);
        fauna.Puppet(fauna.IndexOf(fox), math.PI, 0f);
        yield return Wait(0.5f);
        Shot("before");

        var plain = grid.CurrentRules;
        string print = plain.Fingerprint;
        var oldDeer = fauna.Body(deerKind);
        var oldFox = fauna.Body(foxKind);
        var seed = fauna.State(fauna.IndexOf(deer)).Seed;
        var at = fauna.State(fauna.IndexOf(deer)).Position;
        int animals = fauna.Count;

        var row = (JObject)JObject.Parse(World.Rules.ToJson())["fauna"]["deer"].DeepClone();
        row["coat"]["base"] = "#c02010";
        row["move"]["walk"] = 2.6;
        var patch = new JObject { ["fauna"] = new JObject { ["deer"] = row } };
        grid.QueueRules(plain.WithMods(new[] { new ActiveMod(Mod, new JObject { ["patch"] = patch }.ToString()) }));
        yield return Wait(0.5f);
        Metric("rebuilt", fauna.Rebuilt);
        Metric("read_rows_ms", (float)fauna.ReadRowsMs);
        Check("only_the_deer", fauna.Rebuilt == 1 && !ReferenceEquals(fauna.Body(deerKind), oldDeer) && ReferenceEquals(fauna.Body(foxKind), oldFox),
            $"{fauna.Rebuilt} bodies built in {fauna.ReadRowsMs:F2} ms");
        Check("new_row_in_force", math.abs(fauna.Body(deerKind).WalkSpeed - 2.6f) < 1e-4f, $"walks at {fauna.Body(deerKind).WalkSpeed}");
        int index = fauna.IndexOf(deer);
        Check("keeps_place_and_seed", index >= 0 && fauna.State(index).Seed == seed && math.distance(fauna.State(index).Position.xz, at.xz) < 0.01f,
            $"{fauna.Count} animals, were {animals}");
        Check("same_fingerprint", grid.CurrentRules.Fingerprint == print, grid.CurrentRules.Fingerprint);
        Shot("red");

        grid.QueueRules(plain);
        yield return Wait(0.5f);
        Check("switched_off", fauna.Rebuilt == 1 && math.abs(fauna.Body(deerKind).WalkSpeed - oldDeer.WalkSpeed) < 1e-4f, $"{fauna.Rebuilt} bodies built");
        Shot("after");
    }

    float3 Ground(Vector3 p) => new(p.x, grid.GroundAt(p), p.z);
}
