// The rules in force on a running world: the base rules plus an ordered list
// of switched-on mods, and the effective rules they compose to. A value: a mod
// switched on or off, or a base file reloaded, makes a new RuleSet, checked
// before it can be used, and World.ApplyRules puts its Effective rules in
// force at a tick boundary (TickBatch). Nothing here knows about files or the
// network; the Unity side (ChemGrid.Rules.cs, RuleSwitcher) decides when.
using System;
using System.Collections.Generic;
using System.Linq;

namespace Chem
{
    /// <summary>
    /// A switched-on mod: its id (the file name in rules/mods/ without .json) and its JSON as
    /// text, so the exact patch can travel to other machines and into a late joiner's history.
    /// </summary>
    public readonly struct ActiveMod : IEquatable<ActiveMod>
    {
        public ActiveMod(string id, string json)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Json = json ?? "";
        }

        public string Id { get; }
        public string Json { get; }

        public bool Equals(ActiveMod other) => Id == other.Id && Json == other.Json;
        public override bool Equals(object obj) => obj is ActiveMod m && Equals(m);
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Id ?? "");
        public static bool operator ==(ActiveMod a, ActiveMod b) => a.Equals(b);
        public static bool operator !=(ActiveMod a, ActiveMod b) => !a.Equals(b);
    }

    public sealed class RuleSet
    {
        RuleSet(Rules baseRules, IReadOnlyList<ActiveMod> mods, Rules effective)
        {
            Base = baseRules;
            Mods = mods;
            Effective = effective;
        }

        /// <summary>The rules before any mod: rules/*.json plus the scene's tuning (see <see cref="BaseOf"/>).</summary>
        public Rules Base { get; }
        /// <summary>The switched-on mods, in the order they apply.</summary>
        public IReadOnlyList<ActiveMod> Mods { get; }
        /// <summary>Base with every mod applied: what the world runs on.</summary>
        public Rules Effective { get; }
        /// <summary>The effective rules' fingerprint, for the netcode checksum.</summary>
        public string Fingerprint => Effective.Fingerprint;

        /// <summary>
        /// The base rules for a scene: <paramref name="loaded"/> with the scene's own tuning (a mod,
        /// e.g. a longer fire fade for a big valley) applied, so the tuning is in the fingerprint
        /// and survives every later switch. Throws <see cref="ModException"/> if the tuning doesn't fit.
        /// </summary>
        public static Rules BaseOf(Rules loaded, ModPatch tuning) =>
            tuning == null ? loaded : loaded.WithMods(new[] { tuning });

        /// <summary>The rules with no mods switched on.</summary>
        public static RuleSet Plain(Rules baseRules) =>
            new RuleSet(baseRules ?? throw new ArgumentNullException(nameof(baseRules)), Array.Empty<ActiveMod>(), baseRules);

        /// <summary>
        /// <paramref name="baseRules"/> with <paramref name="mods"/> applied in order. Throws
        /// <see cref="ModException"/> listing every problem, each prefixed with the mod it came from
        /// when one mod's JSON can't be read, if they don't fit together.
        /// </summary>
        public static RuleSet Compose(Rules baseRules, IEnumerable<ActiveMod> mods)
        {
            if (baseRules == null) throw new ArgumentNullException(nameof(baseRules));
            var list = (mods ?? Enumerable.Empty<ActiveMod>()).ToArray();
            if (list.Length == 0) return Plain(baseRules);
            var patches = new List<ModPatch>(list.Length);
            foreach (var m in list)
            {
                try
                {
                    patches.Add(ModPatch.Parse(m.Json));
                }
                catch (ModException e)
                {
                    throw new ModException(e.Errors.Select(err => $"{m.Id}: {err}").ToList());
                }
            }
            return new RuleSet(baseRules, list, baseRules.WithMods(patches));
        }

        /// <summary>The same base with a different list of mods (checked; see <see cref="Compose"/>).</summary>
        public RuleSet WithMods(IEnumerable<ActiveMod> mods) => Compose(Base, mods);

        /// <summary>
        /// The same mods over new base rules, e.g. after rules/*.json changed. The new base is
        /// checked too, even with no mods on, since it came from files someone just edited.
        /// </summary>
        public RuleSet WithBase(Rules baseRules) =>
            Compose((baseRules ?? throw new ArgumentNullException(nameof(baseRules))).WithMods(Array.Empty<ModPatch>()), Mods);

        public bool IsOn(string id) => Mods.Any(m => m.Id == id);

        /// <summary>The mod list with <paramref name="mod"/> switched: off if its id is on, else on at the end.</summary>
        public IReadOnlyList<ActiveMod> Toggled(ActiveMod mod) =>
            IsOn(mod.Id) ? Mods.Where(m => m.Id != mod.Id).ToArray() : Mods.Append(mod).ToArray();

        /// <summary>The mod list with an active mod's JSON replaced (its file was edited); the order stays.</summary>
        public IReadOnlyList<ActiveMod> Replaced(ActiveMod mod) =>
            Mods.Select(m => m.Id == mod.Id ? mod : m).ToArray();
    }
}
