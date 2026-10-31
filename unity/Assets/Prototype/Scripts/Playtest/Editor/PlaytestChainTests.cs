using System.IO;
using System.Linq;
using Chem;
using NUnit.Framework;
using Playtest;

// The playtest bot's casts by elements against the real rules (playtest/DESIGN.md,
// section 2): the 20 casts it chooses from are the table in docs/SPELL_COMBOS.md,
// and each chain, played the way the bot plays it (the setup aimed at a cell a
// few ahead, the trigger at what the setup left), passes its own check. So a
// chain the bot reports as doing nothing is the game, not the bot's idea of
// the rules. Run by Unity's EditMode runner and by dotnet test core.
public class PlaytestChainTests
{
    static Rules rules;

    static Rules R => rules ??= Rules.Load(FindRules());

    // The repo's rules/: next to the Unity project, or up from the test binary under dotnet test.
    static string FindRules()
    {
#if UNITY_5_3_OR_NEWER
        return Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, "../../rules"));
#else
        var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "rules", "spells.json"))) dir = dir.Parent;
        return Path.Combine(dir?.FullName ?? ".", "rules");
#endif
    }

    // The wizard stands at Me facing east; the bot aims a few cells ahead, at Target.
    static readonly (int x, int y) Me = (2, 5), Target = (8, 5);
    const int East = 1, Ticks = 40;

    // Casts a combo the way SpellCaster aims one from the crosshair: a line from beside the
    // wizard towards the cell, a burst round the wizard if it's cast from them, else on the cell.
    static void CastCombo(World w, string combo, (int x, int y) at, int charge = 1)
    {
        var def = R.Combos.Combo(combo);
        var spell = R.Spell(def.SpellAt(charge, w.MaxChargeOf(def)));
        if (spell.Origin != "self") w.Cast(spell.Id, at.x, at.y, East, charge);
        else if (spell.Shape == "burst") w.Cast(spell.Id, Me.x, Me.y, East, charge);
        else w.Cast(spell.Id, Me.x + 1, Me.y, East, charge);
    }

    [Test]
    public void TheBotChoosesFromTheTwentyCastsByElements()
    {
        var casts = Chains.ElementCasts(R.Combos).ToList();
        Assert.That(casts, Has.Count.EqualTo(20), "4 singles and 16 ordered pairs (docs/SPELL_COMBOS.md)");
        Assert.That(casts.Take(4), Is.EqualTo(new[] { "fire", "water", "earth", "air" }));
        Assert.That(casts, Does.Contain("water+earth").And.Contain("earth+water"), "ordered: two different casts");
        Assert.That(Chains.ElementsOf("air+fire"), Is.EqualTo(new[] { "air", "fire" }));
        Assert.That(Chains.Possible(R.Combos).Select(c => c.Id), Is.EqualTo(Chains.All.Select(c => c.Id)), "every chain's casts exist");
    }

    [TestCaseSource(nameof(ChainIds))]
    public void EachChainDoesWhatItsCheckLooksFor(string id)
    {
        var chain = Chains.ById(id);
        var w = new World(R, 16, 11, 3, "grass");
        CastCombo(w, chain.Setup, Target);
        (int x, int y)? aim = null;
        for (int i = 0; i < Ticks && aim == null; i++)
        {
            w.Step();
            aim = chain.Find(w, Target.x, Target.y);
        }
        Assert.That(aim, Is.Not.Null, $"{chain.Setup} left what {chain.Name} needs near the cell");
        int since = w.Tick;
        CastCombo(w, chain.Trigger, aim.Value, chain.TriggerCharge);
        bool done = chain.Done(w, aim.Value.x, aim.Value.y, since);
        for (int i = 0; i < Ticks && !done; i++)
        {
            w.Step();
            done = chain.Done(w, aim.Value.x, aim.Value.y, since);
        }
        Assert.That(done, $"{chain.Name}: {chain.Expect}");
    }

    [TestCaseSource(nameof(ChainIds))]
    public void NoChainsCheckPassesWithoutItsTrigger(string id)
    {
        // A fire left alone burns out, so "the fire is out" alone would pass: each check looks for what only the trigger does.
        var chain = Chains.ById(id);
        var w = new World(R, 16, 11, 3, "grass");
        CastCombo(w, chain.Setup, Target);
        for (int i = 0; i < Ticks * 2; i++)
        {
            w.Step();
            Assert.That(chain.Done(w, Target.x, Target.y, 0), Is.False, $"tick {i}");
        }
    }

    static string[] ChainIds() => Chains.All.Select(c => c.Id).ToArray();
}
