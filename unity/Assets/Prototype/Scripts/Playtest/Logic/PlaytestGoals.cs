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
        /// <summary>Walk (and sprint and jump) to a place.</summary>
        Visit,
        /// <summary>Cast a spell at a cell near the wizard.</summary>
        Cast,
        /// <summary>Two spells in a row on the same cells (oil then fire, water then frost...).</summary>
        Combo,
        /// <summary>Pick up a nearby item and throw it.</summary>
        Throw,
        /// <summary>Walk to a seat and sit.</summary>
        Sit,
        /// <summary>Open a menu page and come back.</summary>
        Menu,
        /// <summary>Stand and look around (time for fire and weather to play out).</summary>
        Idle,
    }

    /// <summary>One goal: what, where or with what, and how many game seconds it may take.</summary>
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
        /// <summary>The place (Visit, Sit), the menu page (Menu), or null.</summary>
        public string Target { get; }
        public string Spell { get; }
        public string Spell2 { get; }
        public float Seconds { get; }

        /// <summary>The coverage key: what "done lately" counts.</summary>
        public string Key => Kind switch
        {
            GoalKind.Visit or GoalKind.Sit or GoalKind.Menu => $"{Kind}:{Target}",
            GoalKind.Cast => $"Cast:{Spell}",
            GoalKind.Combo => $"Combo:{Spell}+{Spell2}",
            _ => Kind.ToString(),
        };

        public override string ToString() => Kind switch
        {
            GoalKind.Visit => $"go to the {Target}",
            GoalKind.Cast => $"cast {Spell}",
            GoalKind.Combo => $"cast {Spell} then {Spell2}",
            GoalKind.Throw => "pick up something and throw it",
            GoalKind.Sit => "sit down",
            GoalKind.Menu => $"open the {Target} menu",
            _ => "stand and look around",
        };
    }

    /// <summary>What the bot can choose from, found in the game at the start of a run.</summary>
    public sealed class GoalOptions
    {
        public IReadOnlyList<string> Places { get; set; } = Array.Empty<string>();
        public IReadOnlyList<string> Spells { get; set; } = Array.Empty<string>();
        public IReadOnlyList<(string first, string then)> Combos { get; set; } = Array.Empty<(string, string)>();
        public IReadOnlyList<string> MenuPages { get; set; } = Array.Empty<string>();
        public bool HasSeats { get; set; }
        public bool HasItems { get; set; }
    }

    public static class GoalPicker
    {
        // How often each kind comes up, before coverage.
        static readonly (GoalKind kind, double weight)[] Kinds =
        {
            (GoalKind.Visit, 4), (GoalKind.Cast, 4), (GoalKind.Combo, 2), (GoalKind.Throw, 1),
            (GoalKind.Sit, 1), (GoalKind.Menu, 1), (GoalKind.Idle, 1),
        };

        /// <summary>
        /// The next goal: a kind by weight, then a target among those of that kind, each candidate
        /// weighted down by how often it was done lately (weight / (1 + times)). The same dice,
        /// options and coverage give the same goal.
        /// </summary>
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

        // Every goal the options allow; Idle always.
        static IEnumerable<Goal> Candidates(GoalOptions o)
        {
            foreach (var p in o.Places) yield return new Goal(GoalKind.Visit, p);
            foreach (var s in o.Spells) yield return new Goal(GoalKind.Cast, null, s);
            foreach (var (a, b) in o.Combos) yield return new Goal(GoalKind.Combo, null, a, b);
            if (o.HasItems) yield return new Goal(GoalKind.Throw, null);
            if (o.HasSeats) yield return new Goal(GoalKind.Sit, "seat");
            foreach (var m in o.MenuPages) yield return new Goal(GoalKind.Menu, m);
            yield return new Goal(GoalKind.Idle, null);
        }

        // A kind's weight is shared among its goals, so ten spells don't drown out one seat.
        static double KindWeight(GoalKind kind) => Kinds.First(k => k.kind == kind).weight;

        static int Count(Goal g, List<Goal> all) => all.Count(o => o.Kind == g.Kind);

        static int Times(IReadOnlyDictionary<string, int> done, string key) =>
            done != null && done.TryGetValue(key, out int n) ? n : 0;

        // How long it may take: long enough to walk somewhere, short for a cast.
        static Goal Seconds(Goal g, PlaytestRandom dice) => new Goal(g.Kind, g.Target, g.Spell, g.Spell2, g.Kind switch
        {
            GoalKind.Visit => 90f,
            GoalKind.Idle => dice.Range(5f, 20f),
            GoalKind.Menu => 8f,
            _ => 20f,
        });
    }
}
