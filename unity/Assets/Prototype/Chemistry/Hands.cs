// A wizard's hands in the Hold (docs/design/the-hold.md, phase 3): rules/hold.json "hands". The table says what the elements in
// the hands cast, plain and modified; nothing in code names a spell. World.Hands.cs runs it.
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Chem
{
    /// <summary>A hand spell: the spell, and the ticks of its cast.</summary>
    public sealed class HandSpellDef
    {
        public string Spell;
        public int CastTicks;
    }

    /// <summary>rules/hold.json "hands".</summary>
    public sealed class HandsDef
    {
        public const int Left = 0, Right = 1, Count = 2;

        readonly Dictionary<string, (HandSpellDef plain, HandSpellDef modified)> spells = new Dictionary<string, (HandSpellDef, HandSpellDef)>(StringComparer.Ordinal);

        /// <summary>What each hand holds at first, by hand.</summary>
        public IReadOnlyList<string> Start { get; private set; } = new string[Count];
        /// <summary>Ticks after the first hand in which the second joins it.</summary>
        public int Window { get; private set; }
        /// <summary>A one-hand spell's cooldown on its hand, and a two-hand spell's on both.</summary>
        public int HandCooldown { get; private set; }
        public int BothCooldown { get; private set; }
        /// <summary>The D-pad's list during a run.</summary>
        public IReadOnlyList<DpadSpellDef> Kit { get; private set; } = Array.Empty<DpadSpellDef>();
        /// <summary>Every spell in the table.</summary>
        public IEnumerable<HandSpellDef> All => spells.Values.SelectMany(s => new[] { s.plain, s.modified });

        /// <summary>What one element casts, or two (second null for one hand), in either order; null if the table has no such row.</summary>
        public HandSpellDef Spell(string first, string second, bool modified)
        {
            if (!spells.TryGetValue(CombosDef.IdOf(first, second), out var row) && !spells.TryGetValue($"{second}+{first}", out row)) return null;
            return modified ? row.modified : row.plain;
        }

        internal static HandsDef Parse(JToken t)
        {
            var h = new HandsDef();
            if (!(t is JObject o)) return h;
            h.Start = new[] { (string)o["start"]?["left"], (string)o["start"]?["right"] };
            h.Window = (int?)o["window"] ?? 0;
            h.HandCooldown = (int?)o["handCooldown"] ?? 0;
            h.BothCooldown = (int?)o["bothCooldown"] ?? 0;
            h.Kit = (o["kit"] as JArray ?? new JArray()).OfType<JObject>().Select(k => new DpadSpellDef { Name = (string)k["name"], Spell = (string)k["spell"], CastTicks = (int?)k["castTicks"] ?? 0 }).ToList();
            foreach (var row in (o["spells"] as JObject)?.Properties() ?? Enumerable.Empty<JProperty>())
                h.spells[row.Name] = (Row(row.Value["plain"]), Row(row.Value["modified"]));
            return h;
        }

        static HandSpellDef Row(JToken t) => t == null ? null : new HandSpellDef { Spell = (string)t["spell"], CastTicks = (int?)t["castTicks"] ?? 0 };
    }

    /// <summary>One wizard's hands: what they hold, when each casts again, and the cast under way. Integers and ids only.</summary>
    public sealed class PlayerHands
    {
        public string Player;
        /// <summary>By hand: its element, and the tick it can cast from.</summary>
        public readonly string[] Element = new string[HandsDef.Count];
        public readonly int[] Ready = new int[HandsDef.Count];
        /// <summary>The hand a cast under way began with, or -1.</summary>
        public int First = -1;
        /// <summary>The other hand joined it; a hand was modified.</summary>
        public bool Both, Modified;

        public bool Casting => First >= 0;
    }

    public sealed partial class Rules
    {
        sealed partial class Checker
        {
            // Every element has its rows, and every pair, in one order or the other.
            public void Hands()
            {
                if (!(r["hold"]?["hands"] is JObject h)) return;
                var names = Strings(r["combos"]?["elements"]);
                foreach (var side in new[] { "left", "right" })
                    if (!names.Contains((string)h["start"]?[side] ?? "")) Errors.Add($"hands: start {side} must be an element");
                foreach (var key in new[] { "window", "handCooldown", "bothCooldown" })
                    if (!WholeFromOne(h[key])) Errors.Add($"hands: {key} must be a whole number from 1");
                for (int i = 0; i < names.Count; i++)
                {
                    HandRow(h, names[i], null, (int?)h["window"] ?? 0);
                    for (int j = i; j < names.Count; j++) HandRow(h, names[i], names[j], 0);
                }
                foreach (var k in (h["kit"] as JArray ?? new JArray()).OfType<JObject>())
                {
                    if (!SpellKnown(k["spell"])) Errors.Add($"hands: kit: unknown spell {Show(k["spell"])}");
                    if (!WholeFromOne(k["castTicks"])) Errors.Add($"hands: kit {Show(k["spell"])}: castTicks must be a whole number from 1");
                }
            }

            void HandRow(JObject hands, string first, string second, int leastTicks)
            {
                string id = CombosDef.IdOf(first, second);
                var row = Entry(hands["spells"], id) ?? Entry(hands["spells"], $"{second}+{first}");
                foreach (var form in new[] { "plain", "modified" })
                {
                    var spell = Entry(row, form);
                    if (!SpellKnown(spell?["spell"])) Errors.Add($"hands: {id} {form}: unknown spell {Show(spell?["spell"])}");
                    if (!WholeFromOne(spell?["castTicks"])) Errors.Add($"hands: {id} {form}: castTicks must be a whole number from 1");
                    else if ((int)spell["castTicks"] < leastTicks) Errors.Add($"hands: {id} {form}: castTicks must be at least the window");
                }
            }
        }
    }
}
