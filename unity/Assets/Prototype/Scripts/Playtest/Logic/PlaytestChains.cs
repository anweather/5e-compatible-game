using System;
using System.Collections.Generic;
using System.Linq;
using Chem;

// The playtest bot's chains (playtest/DESIGN.md, section 2): a cast by
// elements that sets something up, then one that triggers it, the way
// docs/SPELL_COMBOS.md means the table to be played (Forge on mud, fire into
// dust, Lightning into a puddle...). Each says what the setup must leave in the
// cells, where the trigger aims, and what the cells must show after it: the
// rules' result, read from the world, not from what the view drew. Unity-free,
// so every chain is played against the real rules under dotnet test.
namespace Playtest
{
    public sealed class Chain
    {
        public const int Near = 3;

        public Chain(string id, string name, string setup, string trigger, Func<Cell, bool> needs, string expect, Func<World, int, int, int, bool> done)
        {
            Id = id;
            Name = name;
            Setup = setup;
            Trigger = trigger;
            Needs = needs;
            Expect = expect;
            Done = done;
        }

        public string Id { get; }
        public string Name { get; }
        public string Setup { get; }
        public string Trigger { get; }
        public Func<Cell, bool> Needs { get; }
        public string Expect { get; }
        public Func<World, int, int, int, bool> Done { get; }
        public bool WalkAcross { get; set; }
        public bool UnderProp { get; set; }
        public int TriggerCharge { get; set; } = 1;

        public (int x, int y)? Find(World w, int x, int y)
        {
            if (w is null) throw new ArgumentNullException(nameof(w));
            (int x, int y)? best = null;
            int bestD = int.MaxValue;
            foreach (var (cx, cy) in Around(w, x, y, Near))
            {
                int d = (cx - x) * (cx - x) + (cy - y) * (cy - y);
                if (d < bestD && Needs(w.At(cx, cy)))
                {
                    best = (cx, cy);
                    bestD = d;
                }
            }
            return best;
        }

        public override string ToString() => $"{Name} ({Setup}, then {Trigger})";

        internal static IEnumerable<(int x, int y)> Around(World w, int x, int y, int r)
        {
            for (int cy = y - r; cy <= y + r; cy++)
                for (int cx = x - r; cx <= x + r; cx++)
                    if (w.InBounds(cx, cy)) yield return (cx, cy);
        }

        internal static bool AnyNear(World w, int x, int y, int r, Func<Cell, bool> what) => Around(w, x, y, r).Any(p => what(w.At(p.x, p.y)));
    }

    public static class Chains
    {
        public const string DustFlash = "The dust goes up in a flash";
        public const string Douse = "Water douses the fire in a hiss of steam";

        public static readonly IReadOnlyList<Chain> All = new[]
        {
            new Chain("forge-on-mud", "Forge on mud", "water+earth", "earth+fire",
                c => c.T == "mud", "the mud bakes to brick",
                (w, x, y, _) => Chain.AnyNear(w, x, y, 1, c => c.T == "brick")),
            new Chain("fire-into-dust", "Fire into a dust cloud", "earth+air", "fire",
                c => c.Gas == "dust" && c.GasAmt > 0, "the dust goes up in a flash",
                (w, _, _, since) => w.Log.Any(l => l.Tick >= since && l.Text == DustFlash)),
            new Chain("lightning-into-puddle", "Lightning into a puddle", "water", "air+fire",
                c => c.Water > 0 && !c.Frozen, "the charge runs through the water",
                (w, x, y, _) => Chain.AnyNear(w, x, y, Chain.Near, c => c.Water > 0 && c.Charge > 0)),
            new Chain("frost-bridge", "Frost on water, then walk across", "water", "water+air",
                c => c.Water > 0 && !c.Frozen, "the water freezes into ice to walk on",
                (w, x, y, _) => Chain.AnyNear(w, x, y, 1, c => c.Water > 0 && c.Frozen)) { WalkAcross = true },
            new Chain("geyser-under-prop", "Geyser under a prop", "water", "water+fire",
                c => c.Water > 0 && !c.Frozen, "the water jets up as steam and throws the prop",
                (w, x, y, _) => Chain.AnyNear(w, x, y, 1, c => c.Gas == "steam")) { UnderProp = true },
            new Chain("wave-over-fire", "Wave over a fire", "fire", "water+water",
                c => c.Fire > 0, "the wave douses the fire",
                (w, x, y, since) => w.Log.Any(l => l.Tick >= since && l.Text == Douse) && !Chain.AnyNear(w, x, y, 1, c => c.Fire > 0)),
            new Chain("growth-on-burnt", "Plant Growth held on burnt ground", "fire+fire", "earth+water",
                c => c.Cover == "ash" && c.Fire == 0, "saplings take root in the ash",
                (w, x, y, _) => Chain.AnyNear(w, x, y, Chain.Near * 2, c => c.T == "sapling")) { TriggerCharge = World.MaxCharge },
        };

        public static int ChargeFor(PlaytestRandom dice) => dice.Range(0, 2) == 0 ? 1 : dice.Range(2, World.MaxCharge + 1);

        public static Chain ById(string id) => All.FirstOrDefault(c => c.Id == id);

        public static IEnumerable<string> ElementCasts(CombosDef combos)
        {
            if (combos == null) yield break;
            foreach (var a in combos.Elements)
                if (combos.Combo(CombosDef.IdOf(a, null)) != null) yield return CombosDef.IdOf(a, null);
            foreach (var a in combos.Elements)
                foreach (var b in combos.Elements)
                    if (combos.Combo(CombosDef.IdOf(a, b)) != null) yield return CombosDef.IdOf(a, b);
        }

        public static string[] ElementsOf(string combo) => (combo ?? "").Split('+');

        public static IEnumerable<Chain> Possible(CombosDef combos) =>
            combos == null ? Enumerable.Empty<Chain>() : All.Where(c => combos.Combo(c.Setup) != null && combos.Combo(c.Trigger) != null);
    }
}
