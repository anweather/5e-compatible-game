using System;
using System.Collections.Generic;
using System.Linq;

// What the playtest bot sets out to do (playtest/DESIGN.md, section 2): a run
// is a string of goals, each picked by the run's dice, weighted towards what
// hasn't been done lately (the coverage the runner keeps between runs). The
// goals are names and numbers only; PlaytestPlayer carries them out in the
// game. Unity-free, so the choice is tested under dotnet test.
namespace Playtest
{
    public enum GoalKind
    {
        Visit,
        Cast,
        Combo,
        Elements,
        Chain,
        Throw,
        Sit,
        Menu,
        Idle,
    }

    public sealed class Goal
    {
        public Goal(GoalKind kind, string target, string spell = null, string spell2 = null, float seconds = 30f)
        {
            Kind = kind;
            Target = target;
            Spell = spell;
            Spell2 = spell2;
            Seconds = seconds;
        }

        public GoalKind Kind { get; }
        public string Target { get; }
        public string Spell { get; }
        public string Spell2 { get; }
        public float Seconds { get; }

        public string Key => Kind switch
        {
            GoalKind.Visit or GoalKind.Sit or GoalKind.Menu => $"{Kind}:{Target}",
            GoalKind.Cast => $"Cast:{Spell}",
            GoalKind.Combo => $"Combo:{Spell}+{Spell2}",
            GoalKind.Elements => $"Elements:{Target}",
            GoalKind.Chain => $"Chain:{Target}",
            _ => Kind.ToString(),
        };

        public override string ToString() => Kind switch
        {
            GoalKind.Visit => $"go to the {Target}",
            GoalKind.Cast => $"cast {Spell}",
            GoalKind.Combo => $"cast {Spell} then {Spell2}",
            GoalKind.Elements => $"cast {string.Join(" then ", Chains.ElementsOf(Target))} by elements",
            GoalKind.Chain => $"play the chain {Target}",
            GoalKind.Throw => "pick up something and throw it",
            GoalKind.Sit => "sit down",
            GoalKind.Menu => $"open the {Target} menu",
            _ => "stand and look around",
        };
    }

    public sealed class GoalOptions
    {
        public IReadOnlyList<string> Places { get; set; } = Array.Empty<string>();
        public IReadOnlyList<string> Spells { get; set; } = Array.Empty<string>();
        public IReadOnlyList<(string first, string then)> Combos { get; set; } = Array.Empty<(string, string)>();
        public IReadOnlyList<string> ElementCasts { get; set; } = Array.Empty<string>();
        public IReadOnlyList<string> ChainIds { get; set; } = Array.Empty<string>();
        public IReadOnlyList<string> MenuPages { get; set; } = Array.Empty<string>();
        public bool HasSeats { get; set; }
        public bool HasItems { get; set; }
    }

    public static class GoalPicker
    {
        static readonly (GoalKind kind, double weight)[] Kinds =
        {
            (GoalKind.Visit, 4), (GoalKind.Cast, 2), (GoalKind.Combo, 1), (GoalKind.Elements, 4), (GoalKind.Chain, 2), (GoalKind.Throw, 1),
            (GoalKind.Sit, 1), (GoalKind.Menu, 1), (GoalKind.Idle, 1),
        };

        public static Goal Next(PlaytestRandom dice, GoalOptions options, IReadOnlyDictionary<string, int> doneLately)
        {
            if (dice is null) throw new ArgumentNullException(nameof(dice));
            if (options is null) throw new ArgumentNullException(nameof(options));
            var candidates = Candidates(options).ToList();
            var weights = candidates.Select(g => KindWeight(g.Kind) / Count(g, candidates) / (1.0 + Times(doneLately, g.Key))).ToList();
            double roll = dice.Value() * weights.Sum();
            for (int i = 0; i < candidates.Count; i++)
            {
                roll -= weights[i];
                if (roll < 0) return Seconds(candidates[i], dice);
            }
            return Seconds(candidates[candidates.Count - 1], dice);
        }

        public static Goal ByKey(PlaytestRandom dice, GoalOptions options, string key)
        {
            if (dice is null) throw new ArgumentNullException(nameof(dice));
            if (options is null) throw new ArgumentNullException(nameof(options));
            var goal = Candidates(options).FirstOrDefault(g => g.Key == key);
            return goal == null ? null : Seconds(goal, dice);
        }

        static IEnumerable<Goal> Candidates(GoalOptions o)
        {
            foreach (var p in o.Places) yield return new Goal(GoalKind.Visit, p);
            foreach (var s in o.Spells) yield return new Goal(GoalKind.Cast, null, s);
            foreach (var (a, b) in o.Combos) yield return new Goal(GoalKind.Combo, null, a, b);
            foreach (var e in o.ElementCasts) yield return new Goal(GoalKind.Elements, e);
            foreach (var c in o.ChainIds) yield return new Goal(GoalKind.Chain, c);
            if (o.HasItems) yield return new Goal(GoalKind.Throw, null);
            if (o.HasSeats) yield return new Goal(GoalKind.Sit, "seat");
            foreach (var m in o.MenuPages) yield return new Goal(GoalKind.Menu, m);
            yield return new Goal(GoalKind.Idle, null);
        }

        static double KindWeight(GoalKind kind) => Kinds.First(k => k.kind == kind).weight;

        static int Count(Goal g, List<Goal> all) => all.Count(o => o.Kind == g.Kind);

        static int Times(IReadOnlyDictionary<string, int> done, string key) =>
            done != null && done.TryGetValue(key, out int n) ? n : 0;

        static Goal Seconds(Goal g, PlaytestRandom dice) => new Goal(g.Kind, g.Target, g.Spell, g.Spell2, g.Kind switch
        {
            GoalKind.Visit => 90f,
            GoalKind.Idle => dice.Range(5f, 20f),
            GoalKind.Menu => 8f,
            GoalKind.Chain => 40f,
            _ => 20f,
        });
    }
}
