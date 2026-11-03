using System.Globalization;
using System.Linq;
using Chem.Mcp;
using NUnit.Framework;

namespace Chem.Tests
{
    /// <summary>
    /// Grafts (bodies-and-surfaces.md sections 2 and 3, #73): a species row built from others' slots by "from",
    /// flattened by the core when it's read, so the view, the checks and describe see plain slots.
    /// </summary>
    public class GraftTests
    {
        static Workbench bench;
        static Workbench Bench => bench ??= new Workbench(ChemistryTests.Folders);

        static Rules With(Draft d) => Bench.Base.WithMods(new[] { ModPatch.Parse(d.Patch.ToString()) });

        static string Problems(Draft d) => Workbench.Report(Bench.Validate(d.Id));

        static string Problems(string patch) => Workbench.Report(ModValidator.Validate(Bench.Base, Bench.Budgets, patch));

        static string N(double d) => d.ToString("0.###", CultureInfo.InvariantCulture);

        // A slot in a line: id, part, what it hangs on, at, dir, scale, and whose coat it wears.
        static string Line(SlotDef s) =>
            $"{s.Id}={s.Part}{(s.On != null ? " on " + s.On : "")} at [{string.Join(",", s.At.Select(N))}] dir [{string.Join(",", s.Dir.Select(N))}]" +
            (s.Mirror ? " mirror" : "") + (System.Math.Abs(s.Scale - 1) > 1e-9 ? $" x{N(s.Scale)}" : "") + (s.Style > 0 ? $" style {s.Style}" : "") + (s.OwnCoatOf != null ? $" coat {s.OwnCoatOf}" : "");

        static string Slots(Rules r, string id) => string.Join("\n", r.Fauna.Species(id).Slots.Select(Line));

        // Section 2's centaur, as written there.
        const string Centaur = @"{ ""fauna"": { ""centaur"": { ""name"": ""Centaur"", ""size"": [0.95, 1.05], ""body"": { ""from"": ""deer"" },
            ""from"": [
              { ""species"": ""deer"", ""drop"": [""neck"", ""head"", ""ears"", ""eyes""] },
              { ""species"": ""human"", ""take"": [""human-torso""], ""at"": [0, 0.45, 0.8], ""scale"": 1.1, ""coat"": ""own"" } ],
            ""move"": { ""from"": ""deer"" }, ""behave"": { ""from"": ""deer"", ""flee"": 8 }, ""spawn"": { ""density"": 0.05 } } } }";

        static Rules Patched(string patch) => Bench.Base.WithMods(new[] { ModPatch.Parse(patch) });

        [Test]
        public void TheCentaurFlattensToDeerLegsUnderAHumanTorso()
        {
            Assert.That(Problems(Centaur), Does.StartWith("No problems"));
            var r = Patched(Centaur);
            Assert.That(Slots(r, "centaur"), Is.EqualTo(string.Join("\n",
                "hind=hoof-hindleg at [0.55,-0.35,-0.75] dir [90,0,0] mirror",
                "fore=hoof-foreleg at [0.55,-0.45,0.72] dir [90,0,0] mirror",
                "tail=stub-tail at [0,0.55,-1] dir [-40,180,0]",
                "human-torso=human-torso at [0,0.45,0.8] dir [-90,0,0] x1.1 coat human",
                "head=human-head on human-torso at [0,0,0.022] dir [0,0,0] x1.1 coat human",
                "hair-none=hair-none on head at [0,0,0] dir [0,0,0] x1.1 style 9 coat human",
                "eyes=human-eye on head at [0.042,-0.105,-0.11] dir [90,0,0] mirror x1.1 coat human",
                "arms=human-arm on human-torso at [0.209,0.011,-0.099] dir [180,0,0] mirror x1.1 coat human",
                "human-hair=human-hair on head at [0,0.009,-0.031] dir [0,0,0] x1.1 style 1 coat human",
                "human-hair-2=human-hair on head at [0,0.009,-0.031] dir [0,0,0] x1.1 style 1 coat human",
                "hair-long=hair-long on head at [0,0.066,-0.066] dir [200,0,0] x1.1 style 1 coat human",
                "hair-bun=hair-bun on head at [0,0.077,0.022] dir [0,0,0] x1.1 style 1 coat human",
                "human-tail=hair-ponytail on head at [0,0.11,0] dir [210,0,0] x1.1 style 1 coat human",
                "hair-none-2=hair-none on head at [0,0,0] dir [0,0,0] x1.1 style 1 coat human",
                "hair-none-3=hair-none on head at [0,0,0] dir [0,0,0] x1.1 style 2 coat human",
                "hair-none-4=hair-none on head at [0,0,0] dir [0,0,0] x1.1 style 2 coat human",
                "hair-none-5=hair-none on head at [0,0,0] dir [0,0,0] x1.1 style 2 coat human",
                "beard-short=beard-short on head at [0,-0.094,-0.165] dir [0,180,0] x1.1 style 2 coat human")),
                "the deer's legs and tail keep their places; the torso brings head, eyes, hair and arms; the ponytail's id clashes with the deer's tail");
            var centaur = r.Fauna.Species("centaur");
            var deer = r.Fauna.Species("deer");
            Assert.That((centaur.Length, centaur.Radius, centaur.Walk, centaur.Run, centaur.Wander, centaur.Flee), Is.EqualTo((deer.Length, deer.Radius, deer.Walk, deer.Run, deer.Wander, 8.0)),
                "body and move from the deer; behave from the deer with flee over it");
            Assert.That(centaur.Coat.Base, Is.EqualTo(deer.Coat.Base), "no coat of its own: its first donor's");
            Assert.That(r.Fingerprint, Is.EqualTo(Bench.Base.Fingerprint), "fauna is the view's: no golden moves");
            var drafted = Bench.AddSpecies(null, "centaur", "Centaur", null, null, null, null, null, null, 0.05,
                @"[{ ""species"": ""deer"", ""drop"": [""neck"", ""head"", ""ears"", ""eyes""] }, { ""species"": ""human"", ""take"": [""human-torso""], ""at"": [0, 0.45, 0.8], ""scale"": 1.1, ""coat"": ""own"" }]");
            Assert.That(Slots(With(drafted), "centaur"), Is.EqualTo(Slots(r, "centaur")), "add_species writes the same graft");
            Assert.That(Bench.Describe("fauna", "centaur", drafted.Id), Does.Contain("arms (left): human-arm").And.Contain("coat own (human)").And.Contain("4 legs in 2 pairs, 2 arms, 1 head"));
        }

        [Test]
        public void TheFaunIsAHumanOnDeerHindLegs()
        {
            var d = Bench.AddSpecies(null, "faun", "Faun", null, null, null, null, null, null, null,
                @"[{ ""species"": ""human"", ""drop"": [""legs""] }, { ""species"": ""deer"", ""take"": [""hind""], ""at"": [0, -0.3, 0] }]");
            Assert.That(Bench.Validate(d.Id), Is.Empty, Problems(d));
            var faun = With(d).Fauna.Species("faun");
            Assert.That(faun.Slots.Select(s => s.Id), Does.Not.Contain("legs"));
            Assert.That(Line(faun.Slots[faun.Slots.Count - 1]), Is.EqualTo("hind=hoof-hindleg at [0.55,-0.3,0] dir [90,0,0] mirror"), "the pair keeps its spread across, and goes where at says");
            Assert.That(faun.Slots.Count, Is.EqualTo(16), "the human's 16 slots less its legs, and the deer's hind pair");
            Assert.That((faun.Upright, faun.Walk), Is.EqualTo((With(d).Fauna.Species("human").Upright, With(d).Fauna.Species("human").Walk)), "add_species moves it as its first donor");
            string described = Bench.Describe("fauna", "faun", d.Id);
            TestContext.WriteLine(described);
            Assert.That(described, Does.Contain("2 legs").And.Contain("hind (left): hoof-hindleg").And.Contain("A graft, flattened from human, deer"));
        }

        [Test]
        public void TheSixLeggedBearTakesItsForeLegsTwice()
        {
            var d = Bench.AddSpecies(null, "six-legged-bear", "Six-legged bear", null, null, null, null, null, null, null,
                @"[{ ""species"": ""black-bear"" }, { ""species"": ""black-bear"", ""take"": [""fore""], ""at"": [0, -0.45, 0] }]");
            Assert.That(Bench.Validate(d.Id), Is.Empty, Problems(d));
            var r = With(d);
            Assert.That(Slots(r, "six-legged-bear"), Is.EqualTo(Slots(r, "black-bear") + "\n" + "black-bear-fore=bear-foreleg at [0.55,-0.45,0] dir [90,0,0] mirror"),
                "the bear whole, then its fore pair again at mid body, its id prefixed by the donor's");
            Assert.That(Bench.Describe("fauna", "six-legged-bear", d.Id), Does.Contain("6 legs in 3 pairs"));
            var grizzly = Bench.AddSpecies(null, "six-legged-grizzly", "Six-legged grizzly", null, null, null, null, null, null, null,
                @"[{ ""species"": ""grizzly-bear"" }, { ""species"": ""grizzly-bear"", ""take"": [""fore""], ""at"": [0, -0.45, 0], ""coat"": ""own"" }]");
            Assert.That(With(grizzly).Fauna.Species("six-legged-grizzly").Slots[^1].OwnCoatOf, Is.EqualTo("grizzly-bear"), "a variant donor gives its base's slots, worn in its own coat");
        }

        [Test]
        public void SetSpeciesGraftsOntoARowKeepingItsOwnSlots()
        {
            var d = Bench.SetSpecies(null, "black-bear", null, null, @"[{ ""species"": ""black-bear"", ""take"": [""fore""], ""at"": [0, -0.45, 0] }]");
            Assert.That(Problems(d), Does.Contain("goes round"), "a row can't be its own donor");
            var deer = Bench.SetSpecies(null, "deer", null, null, @"[{ ""species"": ""black-bear"", ""take"": [""fore""], ""at"": [0, -0.45, 0] }]");
            Assert.That(Bench.Validate(deer.Id), Is.Empty, Problems(deer));
            Assert.That(With(deer).Fauna.Species("deer").Slots.Select(s => s.Id).Last(), Is.EqualTo("black-bear-fore"), "the deer's own fore first, the bear's prefixed");
            Assert.That(With(deer).Fauna.SameRows(Bench.Base.Fauna, "deer", new System.Collections.Generic.HashSet<string>()), Is.False, "the view rebuilds it");
        }

        [Test]
        public void TheChecksRefuseWhatCantBeBuilt()
        {
            string Graft(string from) => $@"{{ ""fauna"": {{ ""x"": {{ ""name"": ""X"", ""from"": {from} }} }} }}";
            Assert.That(Problems(Graft(@"[{ ""species"": ""unicorn"" }]")), Does.Contain("from names unknown species unicorn"));
            Assert.That(Problems(Graft(@"[{ ""species"": ""deer"", ""take"": [""wings""] }]")), Does.Contain("deer has no part wings"));
            Assert.That(Problems(Graft(@"[{ ""species"": ""deer"", ""take"": [""hind""], ""drop"": [""fore""] }]")), Does.Contain("not both"));
            Assert.That(Problems(Graft(@"[{ ""species"": ""deer"", ""coat"": ""blue"" }]")), Does.Contain("coat can only be own"));
            Assert.That(Problems(Graft(@"[{ ""species"": ""deer"", ""at"": [0, 1] }]")), Does.Contain("at must be [x, y, z]"));
            Assert.That(Problems(@"{ ""fauna"": { ""x"": { ""name"": ""X"", ""from"": [{ ""species"": ""deer"" }], ""parts"": { ""horn"": { ""part"": ""no-such-part"" } } } } }"), Does.Contain("unknown part no-such-part"));
            Assert.That(Problems(@"{ ""fauna"": { ""x"": { ""name"": ""X"", ""from"": [{ ""species"": ""deer"" }], ""parts"": { ""horn"": { ""part"": ""eye"", ""on"": ""head"" } } } } }"), Does.Contain("hangs from head, which isn't a part before it"),
                "a row's own slots come first, so they can't hang on a donor's");
            Assert.That(Problems(@"{ ""fauna"": { ""a"": { ""name"": ""A"", ""from"": [{ ""species"": ""b"" }] }, ""b"": { ""name"": ""B"", ""from"": [{ ""species"": ""a"" }] } } }"), Does.Contain("from goes round"));
            Assert.That(Problems(@"{ ""fauna"": { ""x"": { ""name"": ""X"", ""variantOf"": ""deer"", ""from"": [{ ""species"": ""deer"" }] } } }"), Does.Contain("is a variant"));
            Assert.That(Problems(@"{ ""fauna"": { ""x"": { ""name"": ""X"", ""move"": { ""from"": ""unicorn"" } } } }"), Does.Contain("move from names unknown species unicorn"));
        }

        [Test]
        public void TheBudgetsRefuseEachCapPassed()
        {
            string Graft(string id, string from) => $@"""{id}"": {{ ""name"": ""X"", ""from"": {from} }}";
            Assert.That(Bench.Budgets.Number("fauna.maxPerMod", 0), Is.EqualTo(4));
            string five = "{ \"fauna\": { " + string.Join(", ", Enumerable.Range(1, 5).Select(i => Graft($"x{i}", @"[{ ""species"": ""deer"" }]"))) + " } }";
            Assert.That(Problems(five), Does.Contain("adds 5 species; a mod may add at most 4"));
            string four = @"[{ ""species"": ""deer"" }, { ""species"": ""deer"", ""take"": [""tail""] }, { ""species"": ""deer"", ""take"": [""tail""] }, { ""species"": ""deer"", ""take"": [""tail""] }]";
            Assert.That(Problems("{ \"fauna\": { " + Graft("x", four) + " } }"), Does.Contain("grafts from 4 donors; a graft has at most 3"));
            string many = @"[{ ""species"": ""human"" }, { ""species"": ""human"" }, { ""species"": ""human"" }]";
            Assert.That(Problems("{ \"fauna\": { " + Graft("x", many) + " } }"), Does.Contain("48 slots once flattened; a body has at most 40"));
            Assert.That(Problems("{ \"fauna\": { " + Graft("x", @"[{ ""species"": ""deer"", ""scale"": 5 }]") + " } }"), Does.Contain("at a scale of 5; a donor's scale is 0.25 to 4"));
            Assert.That(Problems("{ \"fauna\": { " + Graft("x", @"[{ ""species"": ""deer"", ""scale"": 0.2 }]") + " } }"), Does.Contain("at a scale of 0.2"));
            Assert.That(Problems("{ \"fauna\": { " + Graft("x", @"[{ ""species"": ""deer"", ""scale"": 4 }, { ""species"": ""human"" }]") + " } }"), Does.StartWith("No problems"), "at the caps is fine");
        }

        [Test]
        public void TheToolsRefuseABadFromInOneSentence()
        {
            static string Refusal(System.Action tool) => Assert.Throws<RulesToolException>(() => tool()).Message;
            Assert.That(Refusal(() => Bench.AddSpecies(null, "x", "X", "deer", null, null, null, null, null, null, @"[{ ""species"": ""deer"" }]")), Does.Contain("not both"));
            Assert.That(Refusal(() => Bench.AddSpecies(null, "x", "X", null, null, null, null, null, null, null, @"{ ""species"": ""deer"" }")), Does.Contain("JSON list of donors"));
            Assert.That(Refusal(() => Bench.AddSpecies(null, "x", "X", null, null, null, null, null, null, null, @"[{ ""species"": ""unicorn"" }]")), Does.Contain("no species unicorn"));
            Assert.That(Refusal(() => Bench.SetSpecies(null, "grizzly-bear", null, null, @"[{ ""species"": ""deer"" }]")), Does.Contain("built on black-bear"));
        }
    }
}
