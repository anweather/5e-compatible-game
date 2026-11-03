using System.Linq;
using Chem;
using NUnit.Framework;
using UnityEditor;

// The view's half of the look families (rules/looks.json): every family the list says is drawn has
// a drawer, so ModValidator never passes a look the game can't show.
public class LookViewTests
{
    static LookFamilies Families => LookFamilies.Load(ChemGrid.Folders.Rules);

    [Test]
    public void EveryTerrainFamilyHasADrawer()
    {
        foreach (var f in Families.In("terrain"))
            Assert.That(CellDressing.Draws(f.Name), Is.EqualTo(f.Drawn), f.Name);
    }

    [Test]
    public void EveryEffectFamilyHasItsShow()
    {
        var visuals = AssetDatabase.LoadAssetAtPath<SpellVisuals>("Assets/Prototype/Settings/SpellVisuals.asset");
        Assert.That(visuals, Is.Not.Null);
        foreach (var f in Families.In("spells"))
        {
            var show = visuals.families.FirstOrDefault(e => e.family == f.Name);
            Assert.That(show, Is.Not.Null, $"{f.Name}: run Prototype > Build Chemistry Yard");
            Assert.That(show.impact != null, Is.EqualTo(f.Impact != null), f.Name + " impact");
            Assert.That(show.projectile != null, Is.EqualTo(f.Projectile != null), f.Name + " projectile");
        }
    }

    [Test]
    public void ASpellsShowComesFromTheRulesInForce()
    {
        var visuals = AssetDatabase.LoadAssetAtPath<SpellVisuals>("Assets/Prototype/Settings/SpellVisuals.asset");
        var rules = Rules.Load(ChemGrid.Folders.Rules);
        var fireball = visuals.For(rules, "fireball");
        Assert.That(fireball.family, Is.EqualTo("blast"));
        Assert.That(fireball.ScaleFor(rules.Spell("fireball"), 1), Is.EqualTo(1f));
        var big = rules.WithMods(new[] { ModPatch.Parse("{ 'spells': [ { 'id': 'fireball', 'group': 'fire', 'name': 'Fireball', 'level': 3, 'shape': 'burst', 'radius': 4, 'element': 'fire' } ] }".Replace('\'', '"')) });
        Assert.That(visuals.For(big, "fireball").ScaleFor(big.Spell("fireball"), 1), Is.EqualTo(2f));
        Assert.That(visuals.For(rules, "not-a-spell"), Is.Null);
    }
}
