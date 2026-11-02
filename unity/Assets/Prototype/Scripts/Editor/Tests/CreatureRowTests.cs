using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Chem;
using NUnit.Framework;
using Punchy.Fauna;
using UnityEngine;

// Creatures as rows (docs/design/builders.md, section 2). The asset test is the proof
// that the export changed no number: it goes with the assets.
public class CreatureRowTests
{
    static Rules Base() => Rules.Load(ChemGrid.FindRulesDir());

    [Test]
    public void EveryBodyBuiltFromItsRowMatchesTheOneBuiltFromItsAsset()
    {
        var tables = Base().Fauna;
        var assets = CreatureRowExport.SpeciesAssets();
        Assert.That(tables.SpeciesIds, Is.EqualTo(assets.Select(a => a.id)));
        foreach (var (id, asset) in assets) Same(id, AssetBody.Build(asset), CreatureBody.Build(tables, id));
    }

    [Test]
    public void HuntingMatchesTheSpawnersOldList()
    {
        var bodies = new CreatureBodies();
        bodies.Refresh(Base());
        var assets = CreatureRowExport.SpeciesAssets().ToDictionary(a => a.id, a => a.asset);
        var listed = assets.Keys.Where(id => bodies[bodies.IndexOf(id)].Spawns || id is "human" or "elf").ToList();
        foreach (var id in listed)
        {
            var was = assets[id];
            bool hunted = listed.Any(h => h != id && assets[h].Hunts.Exists(x => x != null && x.Body == was.Body));
            var body = bodies[bodies.IndexOf(id)];
            Assert.That(body.Hunted, Is.EqualTo(hunted), id + " hunted");
            var prey = listed.Where(q => q != id && was.Hunts.Exists(x => x != null && x.Body == assets[q].Body)).Select(bodies.IndexOf).OrderBy(i => i);
            Assert.That(body.Prey.Where(i => listed.Contains(bodies[i].Id)), Is.EqualTo(prey), id + " prey");
        }
    }

    [Test]
    public void ARowChangeRebuildsOnlyThatSpecies()
    {
        var plain = Base();
        var bodies = new CreatureBodies();
        bodies.Refresh(plain);
        var fox = bodies[bodies.IndexOf("red-fox")];
        var deer = bodies[bodies.IndexOf("deer")];
        var row = JsonRow(plain, "deer").Replace("\"walk\": 1.3", "\"walk\": 2.6");

        bodies.Refresh(plain.WithMods(new[] { ModPatch.Parse($"{{ \"fauna\": {{ \"deer\": {row} }} }}") }));

        Assert.That(bodies.Rebuilt, Is.EqualTo(1));
        Assert.That(bodies[bodies.IndexOf("red-fox")], Is.SameAs(fox));
        Assert.That(bodies[bodies.IndexOf("deer")], Is.Not.SameAs(deer));
        Assert.That(bodies[bodies.IndexOf("deer")].WalkSpeed, Is.EqualTo(2.6f));
    }

    [Test]
    public void APartChangeRebuildsTheSpeciesThatWearIt()
    {
        var plain = Base();
        var bodies = new CreatureBodies();
        bodies.Refresh(plain);
        var wearers = plain.Fauna.SpeciesIds.Count(id => plain.Fauna.BodyOf(id).Slots.Any(s => s.Part == "deer-ear"));
        var row = Newtonsoft.Json.Linq.JObject.Parse(plain.ToJson())["parts"]["deer-ear"];
        row["spring"] = 0.123;
        var mod = ModPatch.Parse($"{{ \"parts\": {{ \"deer-ear\": {row} }} }}");

        bodies.Refresh(plain.WithMods(new[] { mod }));

        Assert.That(wearers, Is.GreaterThan(0));
        Assert.That(bodies.Rebuilt, Is.EqualTo(wearers), "a variant wears what it's based on");
    }

    [Test]
    public void AHuntAddedByAModIsHeardWithoutARebuildOfThePrey()
    {
        var plain = Base();
        var bodies = new CreatureBodies();
        bodies.Refresh(plain);
        var heron = bodies[bodies.IndexOf("heron")];
        Assert.That(heron.Hunted, Is.False);
        var row = JsonRow(plain, "wolf").Replace("\"hunts\": [", "\"hunts\": [\n \"heron\",");

        bodies.Refresh(plain.WithMods(new[] { ModPatch.Parse($"{{ \"fauna\": {{ \"wolf\": {row} }} }}") }));

        int family = plain.Fauna.SpeciesIds.Count(id => plain.Fauna.BodyOf(id).Id == "wolf");
        Assert.That(bodies.Rebuilt, Is.EqualTo(family), "the wolf and its variants, nothing else");
        Assert.That(bodies[bodies.IndexOf("heron")], Is.SameAs(heron));
        Assert.That(heron.Hunted, Is.True);
    }

    [Test]
    public void ASpeciesAModTookAwayLeavesItsPlace()
    {
        var plain = Base();
        var row = JsonRow(plain, "deer");
        var with = plain.WithMods(new[] { ModPatch.Parse($"{{ \"fauna\": {{ \"elk\": {row} }} }}") });
        var bodies = new CreatureBodies();
        bodies.Refresh(with);
        int elk = bodies.IndexOf("elk");
        Assert.That(bodies[elk], Is.Not.Null);

        bodies.Refresh(plain);

        Assert.That(bodies[elk], Is.Null);
        Assert.That(bodies.IndexOf("elk"), Is.EqualTo(elk));
    }

    // What a row change costs, for the report: composing and checking the rules with the mod (with and
    // without the creature tables, for what they add), then in the view one body built, every row compared
    // and the hunting set again. Asserts only that the view's part is well under a frame.
    [Test]
    public void ALiveChangeCostsLessThanAFrame()
    {
        var plain = Base();
        var row = JsonRow(plain, "deer").Replace("\"walk\": 1.3", "\"walk\": 2.6");
        var patched = plain.WithMods(new[] { ModPatch.Parse($"{{ \"fauna\": {{ \"deer\": {row} }} }}") });
        var bodies = new CreatureBodies();
        bodies.Refresh(plain);
        var times = new List<double>();
        var clock = new Stopwatch();
        for (int i = 0; i < 41; i++)
        {
            var next = i % 2 == 0 ? patched : plain;
            clock.Restart();
            bodies.Refresh(next);
            clock.Stop();
            if (i > 0) times.Add(clock.Elapsed.TotalMilliseconds);
        }
        double refresh = Median(times);
        var mod = new[] { ModPatch.Parse($"{{ \"fauna\": {{ \"deer\": {row} }} }}") };
        var sim = new[] { ModPatch.Parse("{ \"sim\": { \"conductLimit\": 9 } }") };
        var bare = Rules.FromJson(Directory.GetFiles(ChemGrid.FindRulesDir(), "*.json")
            .Where(f => Path.GetFileName(f) != "fauna.json" && Path.GetFileName(f) != "parts.json")
            .ToDictionary(f => Path.GetFileName(f), File.ReadAllText));
        double compose = Timed(() => plain.WithMods(mod));
        double composeBare = Timed(() => bare.WithMods(sim));
        double build = Timed(() => CreatureBody.Build(patched.Fauna, "deer"));
        UnityEngine.Debug.Log($"A live row change: compose {compose:F3} ms ({composeBare:F3} without the creature tables), view {refresh:F3} ms, of which one body {build:F3} ms");
        Assert.That(refresh, Is.LessThan(8.0));
    }

    static double Median(List<double> times)
    {
        times.Sort();
        return times[times.Count / 2];
    }

    static double Timed(System.Action act)
    {
        var times = new List<double>();
        for (int i = 0; i < 41; i++)
        {
            var clock = Stopwatch.StartNew();
            act();
            if (i > 0) times.Add(clock.Elapsed.TotalMilliseconds);
        }
        return Median(times);
    }

    static string JsonRow(Rules rules, string species) => Newtonsoft.Json.Linq.JObject.Parse(rules.ToJson())["fauna"][species].ToString();

    // ---------- every number, exactly ----------

    static void Same(string id, CreatureBody a, CreatureBody b)
    {
        Assert.That(b.BodyLength, Is.EqualTo(a.BodyLength), id);
        Assert.That(b.RearRadius, Is.EqualTo(a.RearRadius), id);
        Assert.That(b.FrontRadius, Is.EqualTo(a.FrontRadius), id);
        Assert.That(b.BodyFlatten, Is.EqualTo(a.BodyFlatten), id);
        Assert.That(b.WalkSpeed, Is.EqualTo(a.WalkSpeed), id);
        Assert.That(b.RunSpeed, Is.EqualTo(a.RunSpeed), id);
        Assert.That(b.WalkStride, Is.EqualTo(a.WalkStride), id);
        Assert.That(b.RunStride, Is.EqualTo(a.RunStride), id);
        Assert.That(b.StandHeight, Is.EqualTo(a.StandHeight), id);
        Assert.That(b.Bob, Is.EqualTo(a.Bob), id);
        Assert.That(b.BobsPerCycle, Is.EqualTo(a.BobsPerCycle), id);
        Assert.That(b.RunRock, Is.EqualTo(a.RunRock), id);
        Assert.That(b.Upright, Is.EqualTo(a.Upright), id);
        Assert.That(b.WanderRadius, Is.EqualTo(a.WanderRadius), id);
        Assert.That(b.FleeDistance, Is.EqualTo(a.FleeDistance), id);
        Assert.That(b.TurnRate, Is.EqualTo(a.TurnRate), id);
        Assert.That(b.IdleTime, Is.EqualTo(a.IdleTime), id);
        Assert.That(b.Size, Is.EqualTo(a.Size), id);
        Assert.That(b.Active, Is.EqualTo(a.Active), id);
        Assert.That(b.Grazing, Is.EqualTo(a.Grazing), id);
        Assert.That(b.Thirst, Is.EqualTo(a.Thirst), id);
        Assert.That(b.Playfulness, Is.EqualTo(a.Playfulness), id);
        Assert.That(b.Laziness, Is.EqualTo(a.Laziness), id);
        Assert.That(b.Hunger, Is.EqualTo(a.Hunger), id);
        Assert.That(b.ChaseTime, Is.EqualTo(a.ChaseTime), id);
        Assert.That(b.Parts, Is.EqualTo(a.Parts), id);
        Assert.That(b.Poses, Is.EqualTo(a.Poses), id);
        Assert.That(b.Segments.Length, Is.EqualTo(a.Segments.Length), id);
        for (int i = 0; i < a.Segments.Length; i++)
        {
            var (x, y) = (a.Segments[i], b.Segments[i]);
            if (x.Fixed == 0) x.FixedColor = y.FixedColor; // unread unless the segment has a colour of its own
            else Assert.That(y.FixedColor.xyz, Is.EqualTo(x.FixedColor.xyz), id);
            x.FixedColor.w = y.FixedColor.w;
            Assert.That(y, Is.EqualTo(x), $"{id} segment {i}");
        }
        Assert.That(b.Coats.Length, Is.EqualTo(a.Coats.Length), id);
        for (int i = 0; i < a.Coats.Length; i++)
        {
            Assert.That(b.Coats[i].Base.xyz, Is.EqualTo(a.Coats[i].Base.xyz), id);
            Assert.That(b.Coats[i].Accent.xyz, Is.EqualTo(a.Coats[i].Accent.xyz), id);
            Assert.That(b.Coats[i].AccentGlow, Is.EqualTo(a.Coats[i].AccentGlow), id);
            Assert.That(b.Coats[i].Weight, Is.EqualTo(a.Coats[i].Weight), id);
        }
    }
}
