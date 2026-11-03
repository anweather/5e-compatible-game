using System.Linq;
using Chem;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

// How a spell's look turns its effects (LookTint): the sums, the families, and that an effect of
// its own colour is left exactly as authored. The probe's lookfx scenario sees it drawn.
public class LookTintTests
{
    static readonly Color FireOrange = new(1f, 0.42f, 0.1f), Blue = new(0.2f, 0.45f, 1f);

    static SpellVisuals Visuals => AssetDatabase.LoadAssetAtPath<SpellVisuals>("Assets/Prototype/Settings/SpellVisuals.asset");

    static Rules BlueFireballs(Rules rules)
    {
        string json = System.IO.File.ReadAllText(System.IO.Path.Combine(ChemGrid.Folders.ShippedMods, "blue-fireballs.json"));
        return rules.WithMods(new[] { ModPatch.Parse(json) });
    }

    [Test]
    public void AHueTurnsToTheLooksHue()
    {
        var turned = LookTint.Between(FireOrange, Blue).Of(FireOrange);
        Color.RGBToHSV(turned, out float hue, out _, out _);
        Color.RGBToHSV(Blue, out float wanted, out _, out _);
        Assert.That(Mathf.Abs(hue - wanted), Is.LessThan(0.03f), $"{turned}");
        Assert.That(turned.b, Is.GreaterThan(turned.r));
    }

    [Test]
    public void GreyStaysGrey()
    {
        var grey = LookTint.Between(FireOrange, Blue).Of(new Color(0.4f, 0.4f, 0.4f, 0.5f));
        Assert.That(grey.r, Is.EqualTo(0.4f).Within(1e-3f));
        Assert.That(grey.b, Is.EqualTo(0.4f).Within(1e-3f));
        Assert.That(grey.a, Is.EqualTo(0.5f));
    }

    [Test]
    public void ALookOfTheSameHueOrNoColourTurnsNothing()
    {
        Assert.That(LookTint.Between(FireOrange, new Color(1f, 0.5f, 0.15f)).Any, Is.False, "Fire Bolt's orange");
        Assert.That(LookTint.Between(FireOrange, Color.white).Any, Is.False, "white asks for no colour");
        Assert.That(LookTint.Between(Color.white, Blue).Any, Is.False, "a family with no colour of its own");
        var c = new Color(0.31f, 0.62f, 0.93f);
        Assert.That(default(LookTint).Of(c), Is.EqualTo(c));
    }

    [Test]
    public void EveryShippedSpellIsDrawnAsAuthored()
    {
        var rules = Rules.Load(ChemGrid.Folders.Rules);
        foreach (var spell in rules.Spells)
            Assert.That(Visuals.For(rules, spell.Id)?.Tint.Any ?? false, Is.False, spell.Id);
        Assert.That(Visuals.lookParticle, Is.Not.Null, "run Prototype > Build Chemistry Yard");
    }

    [Test]
    public void PaleArtIsWashedInTheLookWhereItsFamilySays()
    {
        var pink = new Color(1f, 0.1f, 0.6f);
        var washed = LookTint.Between(new Color(0.3f, 0.6f, 1f), pink, 1f).Of(Color.white);
        Assert.That(washed.r, Is.GreaterThan(washed.g * 3f), $"{washed}");
        var grey = LookTint.Between(FireOrange, pink).Of(new Color(0.4f, 0.4f, 0.4f));
        Assert.That(grey.r, Is.EqualTo(grey.g).Within(1e-3f), "with no wash grey stays grey");
        Assert.That(LookTint.Between(Color.white, pink, 1f).Any, Is.True, "pale art takes a wash though it has no hue");
        Assert.That(LookTint.Between(Color.white, pink).Any, Is.False);
    }

    [Test]
    public void BlueFireballsTurnTheShowWithNoRebuild()
    {
        var plain = Rules.Load(ChemGrid.Folders.Rules);
        var blue = BlueFireballs(plain);
        Assert.That(Visuals.For(blue, "fireball").Tint.Any, Is.True);
        Assert.That(Visuals.For(blue, "fire-bolt").Tint.Any, Is.False, "only the spell the mod changes");
        Assert.That(Visuals.For(plain, "fireball").Tint.Any, Is.False, "back with the rules");
        Assert.That(blue.Fingerprint, Is.EqualTo(plain.Fingerprint), "only its look changed");
    }

    static Rules Mod(Rules rules, string patch) =>
        rules.WithMods(new[] { ModPatch.Parse("{ \"title\": \"t\", \"idea\": \"i\", \"patch\": " + patch + " }") });

    static Rules ElementIn(Rules rules, string element, string colour) =>
        Mod(rules, $"{{ \"elements\": {{ \"{element}\": {{ \"look\": {{ \"color\": {colour} }} }} }} }}");

    static void IsColour(Color drawn, float r, float g, float b, string spell)
    {
        Assert.That(drawn.r, Is.EqualTo(r).Within(1e-4f), spell);
        Assert.That(drawn.g, Is.EqualTo(g).Within(1e-4f), spell);
        Assert.That(drawn.b, Is.EqualTo(b).Within(1e-4f), spell);
    }

    [Test]
    public void ASpellThatChoseNoColourFollowsItsElement()
    {
        var plain = Rules.Load(ChemGrid.Folders.Rules);
        var blue = ElementIn(plain, "fire", "[0.2, 0.45, 1]");
        var fireball = Visuals.For(blue, "fireball", plain);
        Assert.That(fireball.Tint.Any, Is.True, "its whole show turns");
        Assert.That(fireball.ChoseColour, Is.False);
        Assert.That(fireball.KeepsFire, Is.False, "its fire is the element's, with no note of its own");
        IsColour(fireball.glow, 0.2f, 0.45f, 1f, "fireball");
        Assert.That(Visuals.For(blue, "fire-bolt", plain).Tint.Any, Is.True, "every spell of the element");
        Assert.That(Visuals.For(blue, "ray-of-frost", plain).Tint.Any, Is.False, "not another element's");
        IsColour(Visuals.For(blue, "misty-step", plain).glow, 0.85f, 0.88f, 0.95f, "a spell with no element keeps its own");
    }

    [Test]
    public void EverySpellOfAnElementFollowsIt()
    {
        var plain = Rules.Load(ChemGrid.Folders.Rules);
        foreach (var spell in plain.Spells.Where(s => plain.ElementLook(s.Element) != null))
        {
            var pink = ElementIn(plain, spell.Element, "[1, 0.1, 0.6]");
            IsColour(Visuals.For(pink, spell.Id, plain).glow, 1f, 0.1f, 0.6f, spell.Id);
        }
    }

    [Test]
    public void ASpellThatChoseAColourKeepsItAmongItsElements()
    {
        var plain = Rules.Load(ChemGrid.Folders.Rules);
        var both = ElementIn(BlueFireballs(plain), "fire", "[0.2, 0.9, 0.3]");
        var fireball = Visuals.For(both, "fireball", plain);
        Assert.That(fireball.ChoseColour, Is.True);
        Assert.That(fireball.KeepsFire, Is.True);
        IsColour(fireball.glow, 0.2f, 0.45f, 1f, "fireball");
        Assert.That(fireball.Tint.Turn, Is.EqualTo(Visuals.For(BlueFireballs(plain), "fireball", plain).Tint.Turn), "the element changes nothing of it");
    }

    [Test]
    public void AColourOfTheArtsHueIsStillAChoice()
    {
        var plain = Rules.Load(ChemGrid.Folders.Rules);
        var orange = Mod(ElementIn(plain, "fire", "[0.2, 0.45, 1]"), "{ \"spells\": [ " + Moved(plain, "fireball", "[1, 0.5, 0.05]") + " ] }");
        var fireball = Visuals.For(orange, "fireball", plain);
        Assert.That(fireball.ChoseColour, Is.True);
        Assert.That(fireball.KeepsFire, Is.True, "orange among blue fire");
        Assert.That(fireball.Tint.Any, Is.False, "as authored");
    }

    [Test]
    public void APaleColourAsksForNoneSoTheElementTakesOver()
    {
        var plain = Rules.Load(ChemGrid.Folders.Rules);
        var pale = Mod(plain, "{ \"spells\": [ " + Moved(plain, "fireball", "[1, 1, 1]") + " ] }");
        Assert.That(Visuals.For(pale, "fireball", plain).ChoseColour, Is.False);
        IsColour(Visuals.For(pale, "fireball", plain).glow, 1f, 1f, 1f, "the element hasn't moved: as a look's colour always was");
        IsColour(Visuals.For(ElementIn(pale, "fire", "[0.2, 0.45, 1]"), "fireball", plain).glow, 0.2f, 0.45f, 1f, "fireball");
    }

    [Test]
    public void ANewSpellWithNoColourTakesItsElementsAsItStands()
    {
        var plain = Rules.Load(ChemGrid.Folders.Rules);
        var entry = Newtonsoft.Json.Linq.JObject.Parse(Moved(plain, "fireball", "[1, 1, 1]"));
        entry["id"] = "cold-ball";
        entry["element"] = "cold";
        ((Newtonsoft.Json.Linq.JObject)entry["look"]).Remove("color");
        var look = Visuals.For(Mod(plain, "{ \"spells\": [ " + entry + " ] }"), "cold-ball", plain);
        var cold = plain.ElementLook("cold");
        IsColour(look.glow, cold.R, cold.G, cold.B, "cold-ball");
        Assert.That(look.ChoseColour, Is.False);
    }

    [Test]
    public void TheRulesAsTheyShipChangeNoShow()
    {
        var plain = Rules.Load(ChemGrid.Folders.Rules);
        foreach (var spell in plain.Spells.Where(s => s.Look != null))
        {
            var look = Visuals.For(plain, spell.Id, plain);
            if (look == null) continue;
            IsColour(look.glow, spell.Look.R, spell.Look.G, spell.Look.B, spell.Id);
            Assert.That(look.Tint.Any, Is.False, spell.Id);
            Assert.That(look.ChoseColour, Is.False, spell.Id);
        }
    }

    // A spell's whole entry with its colour moved, as set_spell writes it.
    static string Moved(Rules rules, string spell, string colour)
    {
        var entry = Newtonsoft.Json.Linq.JObject.Parse(rules.ToJson())["spells"].First(s => (string)s["id"] == spell).DeepClone();
        entry["look"]["color"] = Newtonsoft.Json.Linq.JArray.Parse(colour);
        return entry.ToString();
    }

    [Test]
    public void DressingTurnsAPackEffectAndLeavesItsSmoke()
    {
        var blast = Visuals.families.First(e => e.family == "blast");
        var look = Visuals.For(BlueFireballs(Rules.Load(ChemGrid.Folders.Rules)), "fireball");
        var fx = Object.Instantiate(blast.impact);
        try
        {
            look.Dress(fx);
            var names = fx.GetComponentsInChildren<ParticleSystemRenderer>(true).SelectMany(r => r.sharedMaterials).Where(m => m != null).Select(m => m.shader.name).ToList();
            Assert.That(names, Does.Contain("Prototype/LookParticle"), "the fire is turned");
            Assert.That(names.Any(n => n.Contains("Particles/Lit") || n.Contains("Particles/Simple")), Is.True, "lit smoke stays on the pack's shader");
        }
        finally
        {
            Object.DestroyImmediate(fx);
        }
    }

    [Test]
    public void APlainShowKeepsThePacksMaterials()
    {
        var blast = Visuals.families.First(e => e.family == "blast");
        var look = Visuals.For(Rules.Load(ChemGrid.Folders.Rules), "fireball");
        var fx = Object.Instantiate(blast.impact);
        try
        {
            var before = fx.GetComponentsInChildren<ParticleSystemRenderer>(true).SelectMany(r => r.sharedMaterials).ToList();
            look.Dress(fx);
            Assert.That(fx.GetComponentsInChildren<ParticleSystemRenderer>(true).SelectMany(r => r.sharedMaterials), Is.EqualTo(before));
        }
        finally
        {
            Object.DestroyImmediate(fx);
        }
    }

    [Test]
    public void ABurstScalesWithItsShow()
    {
        var plain = Rules.Load(ChemGrid.Folders.Rules);
        var big = plain.WithMods(new[] { ModPatch.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(ChemGrid.Folders.ShippedMods, "big-fireball.json"))) });
        float Size(Rules rules, int charge) => SpellVisuals.Entry.BurstSize(Visuals.For(rules, "fireball"), rules.Spell("fireball"), charge);
        Assert.That(Size(plain, 1), Is.EqualTo(1f), "a plain Fireball's debris as it always was");
        Assert.That(Size(plain, 3), Is.EqualTo(2f), "held, as it always was");
        Assert.That(Size(big, 1), Is.EqualTo(2f), "the rules' bigger blast throws it further");
        Assert.That(SpellVisuals.Entry.BurstSize(null, plain.Spell("forge"), 3), Is.EqualTo(2f), "no show: the charge alone");
    }

    [Test]
    public void APooledBurstForgetsTheLastLook()
    {
        BurstFx.Play(BurstFx.Kind.Debris, Vector3.zero, 1f, null, null, Blue);
        var bursts = GameObject.Find("Bursts");
        try
        {
            var debris = bursts.GetComponentsInChildren<ParticleSystem>(true).First(p => p.name == "Debris");
            Assert.That(debris.main.startColor.colorMin.b, Is.GreaterThan(debris.main.startColor.colorMin.r), "in the look");
            debris.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            BurstFx.Play(BurstFx.Kind.Debris, Vector3.zero);
            Assert.That(debris.main.startColor.colorMin.r, Is.GreaterThan(debris.main.startColor.colorMin.b), "back to its own");
        }
        finally
        {
            Object.DestroyImmediate(bursts);
        }
    }
}
