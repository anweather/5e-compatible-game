using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Chem.Mcp;
using NUnit.Framework;

namespace Chem.Tests
{
    /// <summary>fauna-read (bodies-and-surfaces.md sections 1, 3 and 4; #80 parts 3 and 4): Claude reads the bodies it can't see.</summary>
    [Category("Simulation")]
    public class FaunaReadTests
    {
        static Workbench bench;
        static Workbench Bench => bench ??= new Workbench(ChemistryTests.Folders);

        // "...; 1.4 m tall, 1.6 m long, ..." as (tall, long).
        static (double tall, double length) Size(string described)
        {
            var m = Regex.Match(described, @"([\d.]+) m tall, ([\d.]+) m long");
            Assert.That(m.Success, described);
            return (double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture));
        }

        [Test]
        public void ListPartsAndFaunaSayHowEachIsBuilt()
        {
            var parts = Bench.List("parts");
            Assert.That(parts, Does.StartWith($"parts ({Bench.Base.Fauna.PartIds.Count()}):"));
            Assert.That(parts, Does.Contain("hoof-hindleg (leg): ").And.Contain("worn by deer"));
            Assert.That(Bench.List("part"), Is.EqualTo(parts));
            var fauna = Bench.List("fauna");
            Assert.That(fauna, Does.Contain("deer: Deer, 7 slots, 0.7 a hectare"));
            Assert.That(fauna, Does.Contain("crystal-hare: ").And.Contain("a variant of"));
        }

        [Test]
        public void DescribeFaunaFlattensTheSlotsAndSaysTheShape()
        {
            var deer = Bench.Describe("fauna", "deer");
            Assert.That(deer, Does.Contain("Shape: 4 legs in 2 pairs, 1 head, 1 tail;"), deer);
            Assert.That(deer, Does.Contain("- hind (right): hoof-hindleg (leg), on the body at [0.55, -0.35, -0.75] body units, dir [90, 0, 0];"));
            Assert.That(deer, Does.Contain("- hind (left): hoof-hindleg (leg)"));
            Assert.That(deer, Does.Contain("- head: deer-head (head), on neck, [0, 0, 0] m from its end, dir [55, 0, 0];"));
            Assert.That(deer, Does.Contain("- ears (left): deer-ear (ear), on head"), "a slot on a slot: one copy per copy of what it hangs on");
            var (tall, length) = Size(deer);
            Assert.That(tall, Is.InRange(1.2, 2.2), "a deer to the tips of its ears");
            Assert.That(length, Is.InRange(1.2, 2.2));

            var human = Bench.Describe("fauna", "human");
            Assert.That(human, Does.Contain("Shape: 2 legs, 2 arms, 1 head;"), "one hairstyle shown, so the ponytail isn't a tail");
            Assert.That(Size(human).tall, Is.InRange(1.5, 2.0), human);
            Assert.That(human, Does.Contain("- human-hair-2: human-hair (feature), on head").And.Contain("style 1 (another option)"));
        }

        [Test]
        public void AVariantSaysWhatItTakesFromItsBody()
        {
            var hare = Bench.Describe("fauna", "crystal-hare");
            var body = Bench.Base.Fauna.BodyOf("crystal-hare");
            Assert.That(hare, Does.Contain($"its body, slots and gait are {body.Id}'s, and it wanders {body.Wander.ToString(CultureInfo.InvariantCulture)} m"));
            Assert.That(hare, Does.Contain("Shape: 4 legs in 2 pairs"));
        }

        [Test]
        public void DescribeWithADraftReadsTheDraftsNewSpecies()
        {
            var d = Bench.AddSpecies(null, "giant-rabbit", "Giant rabbit", "rabbit", 4, null, null, null, null, null);
            var giant = Bench.Describe("fauna", "giant-rabbit", d.Id);
            Assert.That(giant, Does.Contain("\"variantOf\": \"rabbit\"").And.Contain("A variant of rabbit"));
            Assert.That(Size(giant).tall, Is.EqualTo(Size(Bench.Describe("fauna", "rabbit")).tall * 4).Within(0.2));
        }

        [Test]
        public void DescribePartsSaysItsSegmentsAndWhoWearsIt()
        {
            var hoof = Bench.Describe("parts", "hoof-hindleg");
            Assert.That(hoof, Does.StartWith("part hoof-hindleg:"));
            Assert.That(hoof, Does.Contain("Role leg: ").And.Contain("deer.hind"));
            Assert.That(Assert.Throws<RulesToolException>(() => Bench.Describe("parts", "wing")).Message, Does.Contain("no part called wing"));
        }

        [Test]
        public void OutsideRulesSaysAGraftsColoursAreNotDrawnYet()
        {
            var outside = Workbench.Outside();
            Assert.That(outside, Does.Not.Contain("centaur"), "add_species takes from now (GraftTests)");
            Assert.That(outside, Does.Contain("one coat").And.Contain("coat own isn't drawn yet"));
        }

        // #80 part 3
        [Test]
        public void WhatsOnSaysANewSpeciesAndAChangedPart()
        {
            var b = new Workbench(ChemistryTests.Folders);
            b.Apply(b.AddSpecies(null, "giant-rabbit", "Giant rabbit", "rabbit", 3, null, null, null, null, null).Id, "Ana", "Giant rabbits");
            var text = b.WhatsOn();
            Assert.That(text, Does.Contain("1. Giant rabbits: a new species, Giant rabbit."), text);
            Assert.That(b.List("fauna"), Does.Contain("giant-rabbit: Giant rabbit, a variant of rabbit"), "list reads the mods on too");
            Assert.That(b.Describe("fauna", "giant-rabbit"), Does.Contain("Giant rabbits (").And.Contain("added it"));
            b.Apply(b.SetSpecies(null, "deer", 2, null).Id, "Bo", "Big deer");
            Assert.That(b.WhatsOn(), Does.Contain("2. Big deer: Deer: size from"));
        }

        // #80 part 4
        [Test]
        public void AVariantKeepsOnlyTheHabitsItsReaderTakes()
        {
            var d = Bench.AddSpecies(null, "giant-deer", "Giant deer", "deer", 2, null, null, null, "{ \"play\": 0.5 }", null);
            var behave = d.Patch["fauna"]["giant-deer"]["behave"];
            Assert.That(behave["wander"] ?? behave["idle"] ?? behave["flee"] ?? behave["group"], Is.Null, "the deer's, read from the deer: not copied to go stale");
            Assert.That((string)behave["active"], Is.EqualTo("day"), "its own habits start as the deer's");
            Assert.That((double)behave["play"], Is.EqualTo(0.5));
            var r = Bench.Base.WithMods(new[] { ModPatch.Parse(d.Patch.ToString()) });
            var deer = r.Fauna.Species("deer");
            Assert.That(r.Fauna.BodyOf("giant-deer").Flee, Is.EqualTo(deer.Flee));
            Assert.That(r.Fauna.Species("giant-deer").Graze, Is.EqualTo(deer.Graze));
        }
    }
}
