using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Chem;
using NUnit.Framework;
using Punchy.Fauna;

// Creatures as rows (docs/design/builders.md, section 2): a change to a row builds again only what it touches.
public class CreatureRowTests
{
    static Rules Base() => Rules.Load(ChemGrid.Folders.Rules);

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
    // and the hunting set again. Asserts only that the view's part is well under a frame (about 2.8 ms on the Mac's Editor
    // alone): the best of up to RefreshRounds rounds, each after a settled heap, so a loaded Editor (a long suite's garbage,
    // another job, #177) can't fail it alone, and a view that has really grown slower fails every round. A timing test runs
    // on an idle Editor (docs/TESTING_LAYERS.md): the sweep's, or by its name; either way with test run --include_explicit true.
    [Test]
    [Explicit("Timing: run on an idle Editor, with test run --include_explicit true (the sweep's)")]
    [Category("Timing")]
    public void ALiveChangeCostsLessThanAFrame()
    {
        var plain = Base();
        var row = JsonRow(plain, "deer").Replace("\"walk\": 1.3", "\"walk\": 2.6");
        var patched = plain.WithMods(new[] { ModPatch.Parse($"{{ \"fauna\": {{ \"deer\": {row} }} }}") });
        var bodies = new CreatureBodies();
        bodies.Refresh(plain);
        var rounds = new List<double>();
        for (int r = 0; r < RefreshRounds && (rounds.Count == 0 || rounds.Min() >= FrameBudgetMs); r++)
        {
            Settle();
            rounds.Add(MedianRefresh(bodies, plain, patched));
        }
        double refresh = rounds.Min();
        var mod = new[] { ModPatch.Parse($"{{ \"fauna\": {{ \"deer\": {row} }} }}") };
        var sim = new[] { ModPatch.Parse("{ \"sim\": { \"conductLimit\": 9 } }") };
        var bare = Rules.FromJson(Directory.GetFiles(ChemGrid.Folders.Rules, "*.json")
            .Where(f => Path.GetFileName(f) != "fauna.json" && Path.GetFileName(f) != "parts.json")
            .ToDictionary(f => Path.GetFileName(f), File.ReadAllText));
        double compose = Timed(() => plain.WithMods(mod));
        double composeBare = Timed(() => bare.WithMods(sim));
        double build = Timed(() => CreatureBody.Build(patched.Fauna, "deer"));
        string tries = string.Join(", ", rounds.Select(t => t.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)));
        UnityEngine.Debug.Log($"A live row change: compose {compose:F3} ms ({composeBare:F3} without the creature tables), view {refresh:F3} ms (rounds {tries}), of which one body {build:F3} ms");
        Assert.That(refresh, Is.LessThan(FrameBudgetMs), $"the view's refresh, best of the rounds {tries} ms");
    }

    const double FrameBudgetMs = 8.0;
    const int RefreshRounds = 3;

    // The median of 40 refreshes, the row changed and changed back by turns.
    static double MedianRefresh(CreatureBodies bodies, Rules plain, Rules patched)
    {
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
        return Median(times);
    }

    // Earlier tests' garbage collected now, not in the middle of a timed refresh: a timing test's only use of the collector.
#pragma warning disable S1215
    static void Settle()
    {
        System.GC.Collect();
        System.GC.WaitForPendingFinalizers();
        System.GC.Collect();
    }
#pragma warning restore S1215

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
}
