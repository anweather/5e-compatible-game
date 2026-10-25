using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    /// <summary>
    /// Live mods: composing, checking, and switching rules on a running world. The
    /// expected orders and messages come from running the TS sandbox's composeRules
    /// and validateRules on the same patches (sandbox/src/rules.ts).
    /// </summary>
    public class LiveRulesTests
    {
        static Rules baseRules;

        static Rules Base => baseRules ??= Rules.Load(ChemistryTests.RulesDir());

        /// <summary>A mod written with ' for " so the JSON reads cleanly in C#.</summary>
        static ModPatch Mod(string json) => ModPatch.Parse(json.Replace('\'', '"'));

        static Rules With(params string[] mods) => Base.WithMods(mods.Select(Mod).ToList());

        static ModPatch Example(string name) =>
            ModPatch.Parse(File.ReadAllText(Path.Combine(ChemistryTests.RulesDir(), "mods", name + ".json")));

        static IEnumerable<string> Ids(IEnumerable<CellRule> rules) => rules.Select(r => r.Id);

        static IReadOnlyList<string> ErrorsOf(params string[] mods) =>
            Assert.Throws<ModException>(() => With(mods)).Errors;

        static void Run(World w, int ticks) { for (int i = 0; i < ticks; i++) w.Step(); }

        /// <summary>A grass field set alight along its west edge, so the fire is sure to take.</summary>
        static World Meadow(Rules rules, uint seed = 5)
        {
            var w = new World(rules, 40, 9, seed, "grass");
            for (int y = 0; y < w.H; y++) w.Apply(0, y, "fire");
            return w;
        }

        /// <summary>Indexes of squares that are burning or have burned (grass burns to dirt).</summary>
        static HashSet<int> Burnt(World w) =>
            new HashSet<int>(Enumerable.Range(0, w.Cells.Length).Where(i => w.Cells[i].Fire > 0 || w.Cells[i].T == "dirt"));

        const string NoSpread = "{ 'sim': { 'fire': { 'spreadChance': 0 } } }";

        // ---------- composing: parity with composeRules ----------

        [Test]
        public void LongSlidesModComposesLikeTheSandbox()
        {
            // The mod from net.test.ts "a mod switched on mid-game changes the rules ...".
            var r = With(@"{ 'sim': { 'push': { 'slideCells': 7 } }, 'reactions': { 'add': [
                { 'id': 'frost-on-grass', 'element': 'cold', 'if': { 'terrainTag': 'flammable' }, 'do': [{ 'extinguish': true }], 'log': 'Frost settles on the grass' } ] } }");
            Assert.That(Ids(r.CellRulesFor("cold")), Is.EqualTo(new[] { "frost-on-grass", "cold-fire", "cold-water", "cold-steam" }));
            Assert.That((int)JObject.Parse(r.ToJson())["sim"]["push"]["slideCells"], Is.EqualTo(7));
            Assert.That((int)JObject.Parse(Base.ToJson())["sim"]["push"]["slideCells"], Is.EqualTo(3), "the base rules are untouched");
            Assert.That(Base.CellRulesFor("cold")[0].Id, Is.EqualTo("cold-fire"));
        }

        [Test]
        public void NewRulesGoOnTopOfTheirElementAndReplacementsStayInPlace()
        {
            var r = With(@"{ 'elements': ['acid', 'fire'], 'reactions': { 'remove': ['fire-water'], 'add': [
                { 'id': 'steam-damps-fire', 'element': 'fire', 'if': { 'gas': 'steam' }, 'do': [{ 'extinguish': true }], 'stop': true },
                { 'id': 'acid-burns', 'element': 'acid', 'do': [{ 'setCover': null }] },
                { 'id': 'fire-ice', 'element': 'fire', 'if': { 'frozen': true }, 'do': [{ 'melt': true }], 'log': 'Replaced in place' } ] } }");
            Assert.That(Ids(r.CellRulesFor("fire")), Is.EqualTo(new[] { "steam-damps-fire", "fire-oil", "fire-ice", "fire-flammable" }));
            Assert.That(r.CellRulesFor("fire")[2].Log, Is.EqualTo("Replaced in place"));
            Assert.That(Ids(r.CellRulesFor("acid")), Is.EqualTo(new[] { "acid-burns" }));
            var elements = JObject.Parse(r.ToJson())["elements"].Select(e => (string)e);
            Assert.That(elements, Is.EqualTo(new[] { "fire", "cold", "water", "air", "earth", "lightning", "grease", "oil", "thunder", "acid" }));
        }

        [Test]
        public void StackedModsApplyInOrder()
        {
            var r = With(
                "{ 'terrain': { 'mud': { 'name': 'Mud', 'tags': ['ground'] }, 'grass': { 'name': 'Wet grass', 'tags': ['ground'] } }, 'sim': { 'fire': { 'spreadChance': 0.1 }, 'push': 5 } }",
                @"{ 'spells': [{ 'id': 'fireball', 'name': 'Fireball', 'level': 3, 'shape': 'burst', 'radius': 4, 'element': 'fire', 'blurb': 'b' },
                               { 'id': 'fire-bolt', 'name': 'Fire Bolt', 'level': 0, 'shape': 'point', 'element': 'fire', 'power': 3, 'blurb': 'x' }],
                    'sim': { 'fire': { 'heatChance': 0.2 }, 'push': { 'slideCells': 9 } },
                    'reactions': { 'remove': ['fire-oil'], 'addEntity': [{ 'id': 'acid-entity', 'element': 'fire', 'do': [{ 'status': { 'burning': 2 } }] }] } }");
            Assert.That(r.Spells.Select(s => s.Id).Last(), Is.EqualTo("fireball"), "a new spell goes last");
            Assert.That(r.Spells[0].Id, Is.EqualTo("fire-bolt"), "a replaced one stays where it was");
            Assert.That(r.Spell("fire-bolt").Power, Is.EqualTo(3));
            Assert.That(r.Sim.Fire.SpreadChance, Is.EqualTo(0.1));
            Assert.That(r.Sim.Fire.HeatChance, Is.EqualTo(0.2));
            Assert.That(r.Sim.Fire.BoilChance, Is.EqualTo(0.35), "sim is deep-merged");
            var json = JObject.Parse(r.ToJson());
            Assert.That(json["sim"]["push"].ToString(Newtonsoft.Json.Formatting.None), Is.EqualTo("{\"slideCells\":9}"), "a number replaced by an object");
            Assert.That(r.Terrain["grass"].Has("flammable"), Is.False, "a named entry is replaced whole");
            Assert.That(r.Terrain["grass"].Fuel, Is.Null);
            Assert.That((string)json["entityRules"][0]["id"], Is.EqualTo("acid-entity"));
            Assert.That(Ids(r.CellRulesFor("fire")), Is.EqualTo(new[] { "fire-ice", "fire-water", "fire-flammable" }));
        }

        [Test]
        public void ParseTakesABarePatchOrTitleAndPatch()
        {
            Assert.That(Mod("{ 'sim': {} }").Title, Is.Null);
            Assert.That(Mod("{ 'title': 'Long slides', 'patch': { 'sim': {} } }").Title, Is.EqualTo("Long slides"));
            Assert.That(Example("wildfire").Title, Is.EqualTo("Wildfire"));
        }

        [Test]
        public void FireFadeCellsCanBeSetByAMod()
        {
            Assert.That(Base.Sim.Fire.FadeCells, Is.EqualTo(0));
            Assert.That(Base.WithMods(new[] { Example("wildfire") }).Sim.Fire.FadeCells, Is.EqualTo(120));
            Assert.That(Base.WithMods(new[] { Example("big-fireball") }).Spell("fireball").Radius, Is.EqualTo(4));
        }

        [Test]
        public void EveryExampleModPassesTheChecks()
        {
            var mods = Directory.GetFiles(Path.Combine(ChemistryTests.RulesDir(), "mods"), "*.json")
                .Select(f => ModPatch.Parse(File.ReadAllText(f))).ToList();
            Assert.That(mods.Count, Is.GreaterThanOrEqualTo(3));
            foreach (var m in mods) Assert.DoesNotThrow(() => Base.WithMods(new[] { m }), m.Title);
            Assert.DoesNotThrow(() => Base.WithMods(mods), "and all together");
        }

        [Test]
        public void EveryExamplePatchInModdingDocPassesTheChecks()
        {
            // Mirrors the net.test.ts test of the same name.
            var md = File.ReadAllText(Path.Combine(ChemistryTests.RulesDir(), "..", "docs", "MODDING.md"));
            var blocks = Regex.Matches(md, "```json\n([\\s\\S]*?)```").Cast<Match>().Select(m => m.Groups[1].Value).ToList();
            Assert.That(blocks.Count, Is.GreaterThanOrEqualTo(4), "found the examples");
            foreach (var b in blocks) Assert.DoesNotThrow(() => Base.WithMods(new[] { ModPatch.Parse(b) }), b);
        }

        // ---------- checks: parity with validateRules, plus types and ranges ----------

        [Test]
        public void ABrokenModIsRefused()
        {
            // net.test.ts "a broken mod is refused and the rules stay as they were".
            var errors = ErrorsOf("{ 'reactions': { 'add': [{ 'id': 'x', 'element': 'fire', 'do': [{ 'teleportEverything': true }] }] } }");
            Assert.That(errors, Is.EqualTo(new[] { "rule x: unknown effect \"teleportEverything\" (new verbs need code; see docs/MODDING.md)" }));
        }

        [Test]
        public void CrossReferenceErrorsMatchTheSandbox()
        {
            var errors = ErrorsOf(@"{
                'terrain': { 'mud': { 'name': 'Mud', 'tags': ['ground', 'flammable'], 'burnsTo': 'lava', 'breaksTo': '' } },
                'machines': { 'dirt': { 'name': 'M', 'needs': 'steam', 'how': 'x' }, 'nowhere': { 'name': 'N', 'needs': 'wind', 'how': 'y' } },
                'entities': { 'keg': { 'name': 'Keg', 'hp': 1, 'mass': 1, 'flammable': true, 'explodes': { 'cover': 'goo', 'radius': 1, 'fire': 1, 'push': 1, 'damage': [1, 4] } } },
                'reactions': { 'add': [
                    { 'id': 'a', 'element': 'plasma', 'if': { 'cover': ['goo'], 'smell': true }, 'do': [{ 'setTerrain': 'lava', 'raise': 'x' }, { 'setCover': 'goo' }, { 'gas': 'nerve' }] },
                    { 'id': 'b', 'element': 'fire', 'do': 'melt' } ],
                  'addEntity': [{ 'id': 'a', 'element': 'cold', 'do': [] }] },
                'spells': [{ 'id': 's1', 'name': '', 'level': 1, 'shape': 'blob', 'element': 'plasma', 'blurb': '' }],
                'critters': [{ 'id': 'c1', 'name': 'C', 'glyph': 'c', 'color': '#000', 'mode': 'emit', 'element': 'plasma', 'blurb': '' }] }");
            // validateRules' output for the same patch, in its order.
            Assert.That(errors, Is.EqualTo(new[]
            {
                "terrain mud: burnsTo lava unknown",
                "terrain mud: flammable without fuel/flammability",
                "rule b: \"do\" must be a list",
                "rule a: unknown element plasma",
                "rule a: unknown condition \"smell\"",
                "rule a: setTerrain lava unknown",
                "rule a: raise x unknown",
                "rule a: cover goo unknown",
                "rule a: gas nerve unknown",
                "rule a: cover goo unknown",
                "rule a: duplicate id",
                "spell s1: unknown element plasma",
                "spell s1: unknown shape blob",
                "spell s1: needs a name",
                "critter c1: unknown element plasma",
                "machine dirt: terrain lacks the machine tag",
                "machine nowhere: no terrain with that id",
                "entity keg: explodes into unknown cover",
            }));
        }

        [Test]
        public void BadJsonAndWrongTypesAreRefused()
        {
            string ParseError(string json) => Assert.Throws<ModException>(() => Mod(json)).Errors[0];
            Assert.That(ParseError("{ 'sim': "), Does.StartWith("That isn't valid JSON"));
            Assert.That(ParseError("[]"), Does.StartWith("A patch is a JSON object"));
            Assert.That(ParseError("{ 'terrain': [] }"), Is.EqualTo("\"terrain\" must be an object of entries by id"));
            Assert.That(ParseError("{ 'reactions': { 'add': {} } }"), Is.EqualTo("\"reactions.add\" must be a list of rules"));
            Assert.That(ParseError("{ 'spells': [1] }"), Is.EqualTo("\"spells\" must be a list of spells"));
            Assert.DoesNotThrow(() => Mod("{ 'sim': null, 'whatever': 1 }"), "null and unknown sections are no change, as in the TS");
        }

        [Test]
        public void NumbersOfTheWrongTypeOrOutOfRangeAreRefused()
        {
            Assert.That(ErrorsOf("{ 'sim': { 'fire': { 'spreadChance': 'lots' } } }"), Is.EqualTo(new[] { "sim fire.spreadChance must be a number" }));
            Assert.That(ErrorsOf("{ 'sim': { 'fire': { 'spreadChance': -1 } } }"), Is.EqualTo(new[] { "sim fire.spreadChance must be at least 0" }));
            Assert.That(ErrorsOf("{ 'sim': { 'water': { 'unitsPerStep': 0 } } }"), Is.EqualTo(new[] { "sim water.unitsPerStep must be at least 1" }));
            Assert.That(ErrorsOf("{ 'covers': { 'tar': { 'name': 'Tar', 'tags': 'sticky', 'fuel': -3 } } }"),
                Is.EqualTo(new[] { "cover tar: tags must be a list of names", "cover tar: fuel must be at least 0" }));
            Assert.That(ErrorsOf("{ 'spells': [{ 'id': 'big', 'name': 'Big', 'shape': 'burst', 'radius': -2, 'blurb': '' }] }"),
                Is.EqualTo(new[] { "spell big: radius must be at least 0" }));
        }

        // ---------- switching rules on a running world ----------

        [Test]
        public void SwitchingFireSpreadMidGameChangesBehaviourFromThatTick()
        {
            var plain = Meadow(Base);
            var modded = Meadow(Base);
            Run(plain, 10);
            Run(modded, 10);
            modded.ApplyRules(With(NoSpread));
            Assert.That(modded.CellHash(), Is.EqualTo(plain.CellHash()), "switching keeps the cells");
            var atSwitch = Burnt(modded);
            Assert.That(modded.Cells.Count(c => c.Fire > 0), Is.GreaterThan(0), "the fire is still going");

            Run(plain, 200);
            Run(modded, 200);
            Assert.That(Burnt(modded), Is.EquivalentTo(atSwitch), "no new square caught after the switch");
            Assert.That(Burnt(plain).Count, Is.GreaterThan(atSwitch.Count + 20), "without the mod the fire keeps spreading");
        }

        [Test]
        public void SameModAtTheSameTickGivesTheSameWorldEverywhere()
        {
            string Play(bool mod)
            {
                // Each peer loads and composes on its own, as on separate machines.
                var own = Rules.Load(ChemistryTests.RulesDir());
                var w = Meadow(own, 3);
                Run(w, 12);
                if (mod) w.ApplyRules(own.WithMods(new[] { Example("wildfire") }));
                Run(w, 150);
                return w.CellHash();
            }
            Assert.That(Play(true), Is.EqualTo(Play(true)));
            Assert.That(Play(true), Is.Not.EqualTo(Play(false)), "and the mod made a difference");
        }

        [Test]
        public void TheFingerprintFollowsTheEffectiveRules()
        {
            var own = Rules.Load(ChemistryTests.RulesDir());
            Assert.That(own.Fingerprint, Is.EqualTo(Base.Fingerprint), "loaded twice, same rules");
            Assert.That(Base.WithMods(System.Array.Empty<ModPatch>()).Fingerprint, Is.EqualTo(Base.Fingerprint));
            Assert.That(With(NoSpread).Fingerprint, Is.Not.EqualTo(Base.Fingerprint));
            Assert.That(With("{ 'sim': { 'fire': { 'spreadChance': 0.5 } } }").Fingerprint, Is.EqualTo(Base.Fingerprint),
                "a mod that changes nothing leaves it alone");
            Assert.That(With("{ 'sim': { 'push': { 'collisionDamage': 2.0, 'slideCells': 3 } } }").Fingerprint, Is.EqualTo(Base.Fingerprint),
                "key order and 2 vs 2.0 don't matter");
            var a = "{ 'reactions': { 'add': [{ 'id': 'a', 'element': 'fire', 'do': [] }] } }";
            var b = "{ 'reactions': { 'add': [{ 'id': 'b', 'element': 'fire', 'do': [] }] } }";
            Assert.That(With(a, b).Fingerprint, Is.Not.EqualTo(With(b, a).Fingerprint), "mod order does");
        }

        [Test]
        public void SwitchingAModOffRestoresTheBaseRules()
        {
            var plain = Meadow(Base);
            var toggled = Meadow(Base);
            toggled.ApplyRules(With(NoSpread));
            toggled.ApplyRules(Base);
            Run(plain, 150);
            Run(toggled, 150);
            Assert.That(toggled.CellHash(), Is.EqualTo(plain.CellHash()));
            Assert.That(toggled.Rules.Fingerprint, Is.EqualTo(Base.Fingerprint));
        }

        [Test]
        public void SwitchingRulesRefreshesTheTerrainLookupCache()
        {
            var w = new World(Base, 3, 3, 1, "grass");
            Assert.That(w.Flammability(w.At(1, 1)), Is.GreaterThan(0), "grass looked up (and cached) under the base rules");
            w.ApplyRules(With("{ 'terrain': { 'grass': { 'name': 'Wet grass', 'tags': ['ground'] } } }"));
            Assert.That(w.Flammability(w.At(1, 1)), Is.EqualTo(0));
            w.Apply(1, 1, "fire");
            Assert.That(w.At(1, 1).Fire, Is.EqualTo(0), "wet grass doesn't catch");
        }

        [Test]
        public void ThingsTheNewRulesDontDefineAreClearedLikeTheSandbox()
        {
            var w = new World(With("{ 'terrain': { 'mud': { 'name': 'Mud', 'tags': ['ground'] } }, 'covers': { 'tar': { 'name': 'Tar', 'tags': [] } }, 'gases': { 'spores': { 'name': 'Spores' } } }"), 3, 1);
            w.Paint(0, 0, "mud");
            w.SetCover(1, 0, "tar");
            w.AddGas(w.At(2, 0), "spores", 3);
            w.ApplyRules(Base);
            Assert.That(w.At(0, 0).T, Is.EqualTo("dirt"));
            Assert.That(w.At(1, 0).Cover, Is.Null);
            Assert.That(w.At(2, 0).Gas, Is.Null);
            Assert.That(w.At(2, 0).GasAmt, Is.EqualTo(0));
        }
    }
}
