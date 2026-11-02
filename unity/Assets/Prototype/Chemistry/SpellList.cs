// The spell list's contents (docs/SPELL_RADIAL.md), engine-free so it is tested
// in the core: every spell in the rules by its element, then all of them, then
// the ones the radial leaves out. View-only: nothing here touches shared state.
using System;
using System.Collections.Generic;
using System.Linq;

namespace Chem
{
    /// <summary>One tab of the spell list: its name, colour and spells, in spells.json order.</summary>
    public sealed class SpellTab
    {
        public SpellTab(string name, string color, IReadOnlyList<string> spellIds)
        {
            Name = name;
            Color = color;
            SpellIds = spellIds;
        }

        public string Name { get; }
        /// <summary>"#rrggbb".</summary>
        public string Color { get; }
        /// <summary>Never empty.</summary>
        public IReadOnlyList<string> SpellIds { get; }
    }

    /// <summary>The spell list's tabs for a rule set.</summary>
    public static class SpellList
    {
        public const string AllName = "All spells", OffWheelName = "Not on the wheel";

        /// <summary>
        /// The elements with spells, in order; a spell with no element goes in the last one, as
        /// on the radial. Then All spells, then Not on the wheel if the radial leaves any out.
        /// </summary>
        public static IReadOnlyList<SpellTab> From(Rules rules)
        {
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            var tabs = new List<SpellTab>();
            var ids = rules.Groups.Select(g => g.Id).ToList();
            foreach (var group in rules.Groups)
            {
                bool last = group.Id == ids[ids.Count - 1];
                var spells = rules.Spells
                    .Where(s => s.Group == group.Id || (last && !ids.Contains(s.Group)))
                    .Select(s => s.Id).ToList();
                if (spells.Count > 0) tabs.Add(new SpellTab(group.Name, group.Color, spells));
            }
            var all = rules.Spells.Select(s => s.Id).ToList();
            if (all.Count > 0) tabs.Add(new SpellTab(AllName, Rules.DefaultGroupColor, all));
            var offWheel = rules.Spells.Where(s => !s.Radial).Select(s => s.Id).ToList();
            if (offWheel.Count > 0) tabs.Add(new SpellTab(OffWheelName, Rules.DefaultGroupColor, offWheel));
            return tabs;
        }

        /// <summary>The first tab holding this spell, or 0.</summary>
        public static int TabOf(IReadOnlyList<SpellTab> tabs, string spellId)
        {
            for (int i = 0; i < tabs.Count; i++)
                if (tabs[i].SpellIds.Contains(spellId)) return i;
            return 0;
        }
    }
}
