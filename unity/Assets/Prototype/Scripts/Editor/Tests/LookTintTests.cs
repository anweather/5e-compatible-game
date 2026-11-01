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
        string json = System.IO.File.ReadAllText(System.IO.Path.Combine(ChemGrid.FindRulesDir(), "mods", "blue-fireballs.json"));
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
    public void EveryShippedFireSpellIsDrawnAsAuthored()
    {
        var rules = Rules.Load(ChemGrid.FindRulesDir());
        foreach (string spell in new[] { "fire-bolt", "burning-hands", "fireball", "meteor" })
            Assert.That(Visuals.For(rules, spell).Tint.Any, Is.False, spell);
    }

    [Test]
    public void AnEffectFamilysOwnColourComesFromTheLooksFile()
    {
        foreach (var f in LookFamilies.Load(ChemGrid.FindRulesDir()).In("spells"))
        {
            var show = Visuals.families.First(e => e.family == f.Name);
            var own = f.Own != null ? new Color(f.Own[0], f.Own[1], f.Own[2]) : Color.white;
            Assert.That(show.own, Is.EqualTo(own), $"{f.Name}: run Prototype > Build Chemistry Yard");
        }
        Assert.That(Visuals.lookParticle, Is.Not.Null, "run Prototype > Build Chemistry Yard");
    }

    [Test]
    public void BlueFireballsTurnTheShowWithNoRebuild()
    {
        var plain = Rules.Load(ChemGrid.FindRulesDir());
        var blue = BlueFireballs(plain);
        Assert.That(Visuals.For(blue, "fireball").Tint.Any, Is.True);
        Assert.That(Visuals.For(blue, "fire-bolt").Tint.Any, Is.False, "only the spell the mod changes");
        Assert.That(Visuals.For(plain, "fireball").Tint.Any, Is.False, "back with the rules");
        Assert.That(blue.Fingerprint, Is.EqualTo(plain.Fingerprint), "only its look changed");
    }

    [Test]
    public void DressingTurnsAPackEffectAndLeavesItsSmoke()
    {
        var blast = Visuals.families.First(e => e.family == "blast");
        var look = Visuals.For(BlueFireballs(Rules.Load(ChemGrid.FindRulesDir())), "fireball");
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
        var look = Visuals.For(Rules.Load(ChemGrid.FindRulesDir()), "fireball");
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
        var plain = Rules.Load(ChemGrid.FindRulesDir());
        var big = plain.WithMods(new[] { ModPatch.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(ChemGrid.FindRulesDir(), "mods", "big-fireball.json"))) });
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
