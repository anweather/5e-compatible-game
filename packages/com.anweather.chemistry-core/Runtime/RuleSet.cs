// The rules in force on a running world: the base rules plus an ordered list
// of switched-on mods, and the effective rules they compose to. A value: a mod
// switched on or off, or a base file reloaded, makes a new RuleSet, checked
// before it can be used, and World.ApplyRules puts its Effective rules in
// force at a tick boundary (TickBatch). Nothing here knows about files or the
// network; the Unity side (ChemGrid.Rules.cs, RuleSwitcher) decides when.
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Chem
{
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

        public Rules Base { get; }
        public IReadOnlyList<ActiveMod> Mods { get; }
        public Rules Effective { get; }
        internal RuleSet Unmixed => unmixed ?? this;

        RuleSet unmixed;

        public string Fingerprint => Effective.Fingerprint;

        public static Rules BaseOf(Rules loaded, ModPatch tuning) =>
            tuning == null ? loaded : loaded.WithMods(new[] { tuning });

        internal RuleSet WithMixes(IEnumerable<RoomMix> mixes)
        {
            var plain = Unmixed;
            var patch = new JObject();
            foreach (var m in mixes)
            {
                if (!(patch[m.Table] is JObject rows)) patch[m.Table] = rows = new JObject();
                rows[m.Id] = JObject.Parse(m.Row);
            }
            if (patch.Count == 0) return plain;
            return new RuleSet(plain.Base, plain.Mods, plain.Effective.WithMods(new[] { ModPatch.Parse(patch.ToString()) })) { unmixed = plain };
        }

        public static RuleSet Plain(Rules baseRules) =>
            new RuleSet(baseRules ?? throw new ArgumentNullException(nameof(baseRules)), Array.Empty<ActiveMod>(), baseRules);

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

        public RuleSet WithMods(IEnumerable<ActiveMod> mods) => Compose(Base, mods);

        internal RuleSet Adding(ActiveMod mod, Rules composed) => new RuleSet(Base, Mods.Append(mod).ToArray(), composed);

        public RuleSet WithBase(Rules baseRules) =>
            Compose((baseRules ?? throw new ArgumentNullException(nameof(baseRules))).WithMods(Array.Empty<ModPatch>()), Mods);

        public bool IsOn(string id) => Mods.Any(m => m.Id == id);

        public IReadOnlyList<ActiveMod> Toggled(ActiveMod mod) =>
            IsOn(mod.Id) ? Mods.Where(m => m.Id != mod.Id).ToArray() : Mods.Append(mod).ToArray();

        public IReadOnlyList<ActiveMod> Replaced(ActiveMod mod) =>
            Mods.Select(m => m.Id == mod.Id ? mod : m).ToArray();
    }
}
