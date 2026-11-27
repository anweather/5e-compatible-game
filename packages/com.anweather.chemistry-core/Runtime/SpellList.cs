// The spell list's contents (docs/SPELL_RADIAL.md), engine-free so it is tested
// in the core: every spell in the rules by its element, then all of them, then
// the ones the radial leaves out. View-only: nothing here touches shared state.
using System;
using System.Collections.Generic;
using System.Linq;

namespace Chem
{
    public sealed class SpellTab
    {
        public SpellTab(string name, string color, IReadOnlyList<string> spellIds)
        {
            Name = name;
            Color = color;
            SpellIds = spellIds;
        }

        public string Name { get; }
        public string Color { get; }
        public IReadOnlyList<string> SpellIds { get; }
    }

    public static class SpellList
    {
        public const string AllName = "All spells", OffWheelName = "Not on the wheel";

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

        public static int TabOf(IReadOnlyList<SpellTab> tabs, string spellId)
        {
            for (int i = 0; i < tabs.Count; i++)
                if (tabs[i].SpellIds.Contains(spellId)) return i;
            return 0;
        }
    }
}
