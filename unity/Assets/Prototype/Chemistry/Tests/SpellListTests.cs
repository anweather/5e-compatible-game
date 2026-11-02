using System.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    /// <summary>The spell list's tabs: every spell by element, all of them, and the ones off the radial.</summary>
    public class SpellListTests
    {
        static Rules baseRules;

        static Rules Base => baseRules ??= Rules.Load(ChemistryTests.RulesDir());

        static Rules With(string mod) => Base.WithMods(new[] { ModPatch.Parse(mod.Replace('\'', '"')) });

        [Test]
        public void EverySpellIsUnderItsElementAndInAll()
        {
            var tabs = SpellList.From(Base);
            var all = tabs.Single(t => t.Name == SpellList.AllName);
            Assert.That(all.SpellIds, Is.EquivalentTo(Base.Spells.Select(s => s.Id)));
            var elements = tabs.Take(Base.Groups.Count).SelectMany(t => t.SpellIds);
            Assert.That(elements, Is.EquivalentTo(Base.Spells.Select(s => s.Id)));
        }

        [Test]
        public void TheOnesOffTheWheelHaveTheirOwnTab()
        {
            var tabs = SpellList.From(Base);
            var off = tabs.Single(t => t.Name == SpellList.OffWheelName);
            Assert.That(off.SpellIds, Is.EquivalentTo(Base.Spells.Where(s => !s.Radial).Select(s => s.Id)));
            Assert.That(off.SpellIds, Is.Not.Empty);
            Assert.That(tabs[tabs.Count - 1], Is.SameAs(off));
        }

        [Test]
        public void TheFirstTabsAreTheElementsInOrder()
        {
            var names = SpellList.From(Base).Take(2).Select(t => t.Name);
            Assert.That(names, Is.EqualTo(Base.Groups.Take(2).Select(g => g.Name)));
        }

        [Test]
        public void TabOfFindsTheElementFirst()
        {
            var tabs = SpellList.From(Base);
            Assert.That(tabs[SpellList.TabOf(tabs, "thunderwave")].Name, Is.EqualTo("Storm"));
            Assert.That(SpellList.TabOf(tabs, "no-such-spell"), Is.EqualTo(0));
        }

        [Test]
        public void ASpellWithNoElementGoesInTheLast()
        {
            var rules = With("{ 'spells': [ { 'id': 'meteor-swarm', 'name': 'Meteor Swarm', 'level': 9, 'shape': 'burst', 'element': 'fire' } ] }");
            string last = rules.Groups[rules.Groups.Count - 1].Name;
            Assert.That(SpellList.From(rules).Single(t => t.Name == last).SpellIds, Does.Contain("meteor-swarm"));
        }
    }
}
