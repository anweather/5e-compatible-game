using System.Linq;
using Chem.Mcp;
using NUnit.Framework;

namespace Chem.Tests
{
    /// <summary>add_species and set_species (#61, #72): animals Claude adds are fauna rows, so they move as their kind does.</summary>
    [Category("Simulation")]
    public class FaunaToolsTests
    {
        static Workbench bench;
        static Workbench Bench => bench ??= new Workbench(ChemistryTests.Folders);

        static Rules With(Draft d) => Bench.Base.WithMods(new[] { ModPatch.Parse(d.Patch.ToString()) });

        static string Problems(Draft d) => Workbench.Report(Bench.Validate(d.Id));

        static string Refusal(System.Action tool) => Assert.Throws<RulesToolException>(() => tool()).Message;

        [Test]
        public void ANewSpeciesIsAVariantThatBehavesAsItsKindDoes()
        {
            var d = Bench.AddSpecies(null, "giant-rabbit", "Giant rabbit", "rabbit", 3, "[0.9, 0.9, 0.95]", null, null, "{ \"play\": 0.6 }", null);
            Assert.That(Bench.Validate(d.Id), Is.Empty, Problems(d));
            var r = With(d);
            var giant = r.Fauna.Species("giant-rabbit");
            var rabbit = r.Fauna.Species("rabbit");
            Assert.That(r.Fauna.BodyOf("giant-rabbit").Id, Is.EqualTo("rabbit"), "its body, gait, wander and flee are the rabbit's");
            Assert.That((giant.VariantScale, giant.Play, giant.Active, giant.Graze), Is.EqualTo((3.0, 0.6, rabbit.Active, rabbit.Graze)), "its habits over the rabbit's");
            Assert.That((giant.Spawns, giant.Density), Is.EqualTo((true, rabbit.Density / 2)), "it comes out in the wilds");
            Assert.That(giant.Coat.Base, Is.EqualTo(new[] { 0.9, 0.9, 0.95 }));
            Assert.That(giant.Coat.Accent, Is.EqualTo(rabbit.Coat.Accent), "the colour given, the rest the rabbit's");
            Assert.That(r.Fingerprint, Is.EqualTo(Bench.Base.Fingerprint), "fauna is the view's: no cell, no golden moves");
            Assert.That(r.Fauna.SpeciesIds.Last(), Is.EqualTo("giant-rabbit"));
        }

        [Test]
        public void DensityZeroNeverComesOutOnItsOwn()
        {
            var d = Bench.AddSpecies(null, "pale-deer", "Pale deer", "deer", null, null, null, null, null, 0);
            Assert.That(With(d).Fauna.Species("pale-deer").Spawns, Is.False);
            Assert.That(With(d).Fauna.Species("pale-deer").Coat.Base, Is.EqualTo(Bench.Base.Fauna.Species("deer").Coat.Base), "no colour given: the deer's coat");
        }

        [Test]
        public void SetSpeciesScalesARowAgainstItsNormalSize()
        {
            var d = Bench.SetSpecies(null, "deer", 3, null);
            Assert.That(Bench.Validate(d.Id), Is.Empty, Problems(d));
            Assert.That(With(d).Fauna.Species("deer").Size, Is.EqualTo(new[] { 2.7, 3.3 }), "a species of its own is sized by size");
            Assert.That(Bench.SetSpecies(d.Id, "deer", 1, null).Patch["fauna"]["deer"]["size"].ToString(Newtonsoft.Json.Formatting.None), Is.EqualTo("[0.9,1.1]"), "1 puts it back, not three times again");
            var hare = Bench.SetSpecies(null, "crystal-hare", 2, null);
            Assert.That(With(hare).Fauna.Species("crystal-hare").VariantScale, Is.EqualTo(2.2), "a variant by its scale");
            var own = new Workbench(ChemistryTests.Folders);
            string applied = own.Apply(own.SetSpecies(null, "deer", 3, null).Id, "Ana", "Big deer");
            Assert.That(applied, Does.Contain("size"), applied);
            Assert.That(own.List("fauna"), Does.Contain("deer: Deer"));
        }

        [Test]
        public void TheChecksRefuseABadRowAndAGiantPastTheBudget()
        {
            double max = Bench.Budgets.Number("things.maxScale", 20);
            Assert.That(Problems(Bench.SetSpecies(null, "deer", max * 2.5, null)), Does.Contain($"more than {max} times its normal size"));
            Assert.That(Problems(Bench.AddSpecies(null, "huge-hare", "Huge hare", "rabbit", max * 2.5, null, null, null, null, null)), Does.Contain("Species huge-hare has a scale of"));
            Assert.That(Problems(Bench.SetSpecies(null, "turkey", null, "{ \"active\": \"sometimes\" }")), Does.Contain("active must be one of"), "Rules.CheckFauna, as for any row");
            Assert.That(Problems(Bench.SetSpecies(null, "wolf", null, "{ \"hunts\": [\"unicorn\"] }")), Does.Contain("hunts unknown species unicorn"));
        }

        [Test]
        public void TheToolsSayWhatsWrongInOneSentence()
        {
            Assert.That(Refusal(() => Bench.AddSpecies(null, "x", "X", "unicorn", null, null, null, null, null, null)), Does.Contain("no species unicorn"));
            Assert.That(Refusal(() => Bench.AddSpecies(null, "deer", "Deer", "rabbit", null, null, null, null, null, null)), Does.Contain("already a species called deer"));
            Assert.That(Refusal(() => Bench.AddSpecies(null, "x", "X", "rabbit", null, null, null, null, "{ \"flee\": 2 }", null)), Does.Contain("wanders, flees and keeps company as rabbit does"));
            Assert.That(Refusal(() => Bench.AddSpecies(null, "x", "X", "rabbit", -1, null, null, null, null, null)), Does.Contain("scale is a number above 0"));
            Assert.That(Refusal(() => Bench.AddSpecies(null, "x", "X", "rabbit", null, "red", null, null, null, null)), Does.Contain("[r, g, b]"));
            Assert.That(Refusal(() => Bench.SetSpecies(null, "deer", null, null)), Does.Contain("give one"));
            Assert.That(Bench.SetSpecies(null, "deer", null, "{ \"flee\": 2 }").Patch["fauna"]["deer"]["behave"]["flee"].ToString(), Is.EqualTo("2"), "a species of its own takes flee");
        }

        [Test]
        public void ListAndOutsideRulesKnowTheFauna()
        {
            Assert.That(Bench.List("fauna"), Does.Contain("turkey: Turkey"));
            Assert.That(Bench.List("species"), Does.StartWith("fauna ("));
            Assert.That(Workbench.Outside(), Does.Not.Contain("Fauna size"));
        }
    }
}
