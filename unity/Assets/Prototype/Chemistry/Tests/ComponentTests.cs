using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    /// <summary>
    /// Components (Components.cs, rules/components.json): what entities and items are
    /// made of, entity rules that match on them ("has" / "lacks"), the old
    /// flammable / creature keys, and the checks (the same wording as validateRules in
    /// sandbox/src/rules.ts; the expected list is the one in sandbox/test/components.test.ts).
    /// </summary>
    public class ComponentTests
    {
        static Rules R => ChemistryTests.R;

        static Rules With(params string[] mods) => R.WithMods(mods.Select(m => ModPatch.Parse(m.Replace('\'', '"'))).ToList());

        static IReadOnlyList<string> ErrorsOf(string mod) => Assert.Throws<ModException>(() => With(mod)).Errors;

        [Test]
        public void EveryCreatureNamesAKnownMaterialThatGivesItNothingYet()
        {
            // docs/MATERIALS.md step 5, part 1: creatures name flesh, bone or chitin, which have no verbs
            // yet, so they still burn, freeze and soak as creatures (no MaterialBurn, no components).
            var creatures = R.Entities.Where(e => e.Value.Creature).ToList();
            Assert.That(creatures, Is.Not.Empty);
            foreach (var (id, def) in creatures.Select(e => (e.Key, e.Value)))
            {
                Assert.That(def.MaterialDef, Is.Not.Null, $"{id} names a known material");
                Assert.That(new[] { "flesh", "bone", "chitin" }, Does.Contain(def.Material), id);
                Assert.That(def.MaterialDef.Burns || def.MaterialDef.Tags.Count > 0, Is.False, $"{id}'s {def.Material} gives it nothing");
            }
        }

        [Test]
        public void ThingsCarryTheirComponents()
        {
            Assert.That(R.Components, Is.Ordered.Using((IComparer<string>)System.StringComparer.Ordinal));
            Assert.That(R.Components, Does.Contain(Rules.Flammable).And.Contain(Rules.Creature).And.Contain("heavy"));
            Assert.That(R.Component("nope"), Is.EqualTo(-1));

            Assert.That(R.EntityHas("crate", "flammable") && R.EntityHas("crate", "breakable"));
            Assert.That(R.EntityHas("boulder", "flammable"), Is.False);
            Assert.That(R.EntityHas("boulder", "heavy"));
            Assert.That(R.EntityHas("nobody", "heavy"), Is.False);
            Assert.That(R.ItemHas("bullseye-lantern", "brittle"));
            Assert.That(R.ItemHas("spyglass", "brittle"), Is.False);

            Assert.That(R.EntitiesWith("creature"), Is.EqualTo(new[] { "ash-crab", "bone-hound", "giant-spider", "goblin", "wizard" }));
            Assert.That(R.Entity("crate").Flammable && !R.Entity("crate").Creature, "Flammable and Creature follow the components");
            Assert.That(R.Entity("goblin").Creature && !R.Entity("goblin").Flammable);
        }

        /// <summary>
        /// Every entity's and item's component bits (bit i is the i-th name in sorted order),
        /// hashed with FNV-1a: the same golden as componentGolden in sandbox/test/components.test.ts,
        /// so both engines number the components alike and give each thing the same ones.
        /// </summary>
        [Test]
        public void ComponentBitsMatchTheSandbox()
        {
            static IEnumerable<string> Bits<T>(IReadOnlyDictionary<string, T> table, System.Func<T, ComponentSet> components) =>
                table.Keys.OrderBy(k => k, System.StringComparer.Ordinal)
                    .Select(k => k + ":" + components(table[k]).Bits.ToString(System.Globalization.CultureInfo.InvariantCulture));
            string s = string.Join(",", R.Components) + "|" + string.Join(";", Bits(R.Entities, e => e.Components)) + "|" + string.Join(";", Bits(R.Items, i => i.Components));
            uint h = 0x811c9dc5;
            foreach (char c in s) h = (h ^ c) * 0x01000193;
            Assert.That(h.ToString("x", System.Globalization.CultureInfo.InvariantCulture), Is.EqualTo("6c22a0dc"), s);
        }

        [Test]
        public void ThingsTakeWhatTheirMaterialGivesFromIt()
        {
            // docs/MATERIALS.md, step 4: a crate is flammable and breakable because it's timber, a lantern
            // brittle because it's glass; entities.json and items.json list only creature and heavy.
            Assert.That(R.EntityHas("crate", "flammable") && R.EntityHas("crate", "breakable") && R.Entity("crate").Flammable);
            Assert.That(R.ItemHas("bullseye-lantern", "brittle"));
            string Path(string file) => System.IO.Path.Combine(ChemistryTests.RulesDir(), file);
            var given = Rules.MaterialComponents.Select(m => m.component).ToList();
            foreach (var file in new[] { "entities.json", "items.json" })
                foreach (var p in Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(Path(file))).Properties())
                {
                    var listed = (p.Value["components"] as Newtonsoft.Json.Linq.JArray)?.Select(c => (string)c).ToList() ?? new List<string>();
                    Assert.That(listed.Intersect(given), Is.Empty, $"{file} {p.Name}: those come from its material");
                }
        }

        [Test]
        public void TheOldFlammableAndCreatureKeysStillLoad()
        {
            var r = With("{ 'entities': { 'hay': { 'name': 'Hay bale', 'hp': 2, 'flammable': true }, 'imp': { 'name': 'Imp', 'hp': 4, 'creature': true, 'components': ['floats'] } } }");
            Assert.That(r.EntityHas("hay", "flammable") && r.Entity("hay").Flammable);
            Assert.That(r.EntityHas("imp", "creature") && r.EntityHas("imp", "floats"));
        }

        [Test]
        public void EntityRulesMatchOnComponentsNotIds()
        {
            int Burning(Rules rules)
            {
                var w = new World(rules, 20, 9, 3, "stone");
                var r = w.SpawnRaider("giant-spider", 8, 4, 99);
                w.Cast("fire-bolt", 8, 4, 1);
                return r.StatusOf("burning");
            }
            Assert.That(Burning(R), Is.EqualTo(9), "a spider isn't flammable: it burns as a creature does");

            var oily = With("{ 'entities': { 'giant-spider': { 'name': 'Giant spider', 'hp': 12, 'components': ['creature', 'flammable'], 'raider': { 'ticksPerSquare': 5 } } } }");
            Assert.That(Burning(oily), Is.EqualTo(24), "given the flammable component, the flammable rule takes it");

            var furred = With(
                "{ 'components': { 'furred': 'Has fur, which burns long.' }," +
                "  'entities': { 'giant-spider': { 'name': 'Giant spider', 'hp': 12, 'components': ['creature', 'furred'], 'raider': { 'ticksPerSquare': 5 } } }," +
                "  'reactions': { 'addEntity': [{ 'id': 'e-fire-fur', 'element': 'fire', 'if': { 'has': ['creature', 'furred'] }, 'do': [{ 'status': { 'burning': 40 } }], 'stop': true }] } }");
            Assert.That(Burning(furred), Is.EqualTo(40), "a mod's own component, matched by a mod's own rule");
            Assert.That(furred.Component("furred"), Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        public void TheOldFlammableConditionIsHasOrLacks()
        {
            var r = With("{ 'reactions': { 'addEntity': [{ 'id': 'e-old', 'element': 'cold', 'if': { 'flammable': false }, 'do': [] }, { 'id': 'e-old2', 'element': 'cold', 'if': { 'flammable': true }, 'do': [] }] } }");
            var rules = r.EntityRulesFor("cold").ToList();
            var old = rules.First(x => x.Id == "e-old");
            Assert.That(old.If.Lacks.Has(r.Component(Rules.Flammable)) && old.If.Has.IsEmpty);
            Assert.That(rules.First(x => x.Id == "e-old2").If.Has.Has(r.Component(Rules.Flammable)));
        }

        [Test]
        public void TheChecksNameUnknownComponents()
        {
            var errors = ErrorsOf(
                "{ 'components': { 'shiny': '' }," +
                "  'entities': { 'imp': { 'name': 'Imp', 'components': ['creature', 'glowing'] }, 'elf': { 'name': 'Elf', 'components': 'creature' } }," +
                "  'items': { 'rock': { 'name': 'Rock', 'components': ['sharp'] } }," +
                "  'reactions': { 'addEntity': [{ 'id': 'e-x', 'element': 'fire', 'if': { 'has': 'wooden', 'lacks': [3] }, 'do': [] }] } }");
            Assert.That(errors, Is.EquivalentTo(new[]
            {
                "rule e-x: unknown component wooden",
                "rule e-x: lacks must be a component name or a list of them",
                "entity imp: unknown component glowing",
                "entity elf: components must be a list of component names",
                "item rock: unknown component sharp",
                "component shiny: needs a description",
            }));
            Assert.That(ErrorsOf("{ 'components': { 'shiny': 3 } }"), Is.EqualTo(new[] { "\"components\" must be an object of descriptions by name" }));
        }

        [Test]
        public void AtMost64Components()
        {
            var many = string.Join(", ", Enumerable.Range(0, 60).Select(i => $"'c{i}': 'x'"));
            Assert.That(ErrorsOf("{ 'components': { " + many + " } }"), Does.Contain($"components: at most 64, not {R.Components.Count + 60}"));
        }
    }
}
