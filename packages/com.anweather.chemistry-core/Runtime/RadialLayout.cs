// The spell radial's shape (docs/SPELL_RADIAL.md), engine-free so it is tested
// in the core: ring 1 is one wedge per spell group (groups.json), ring 2 is the
// highlighted group's spells as an arc around its wedge. Angles are degrees
// clockwise from straight up, for a vector with x to the right and y up (a
// stick, or a mouse delta with y flipped from GUI space). View-only: nothing
// here touches shared state, so doubles are fine.
using System;
using System.Collections.Generic;
using System.Linq;

namespace Chem
{
    /// <summary>One wedge of ring 1: a spell group and its spells, in spells.json order.</summary>
    public sealed class RadialGroup
    {
        public RadialGroup(string id, string name, string color, IReadOnlyList<string> spellIds)
        {
            Id = id;
            Name = name;
            Color = color;
            SpellIds = spellIds;
        }

        public string Id { get; }
        public string Name { get; }
        /// <summary>"#rrggbb".</summary>
        public string Color { get; }
        /// <summary>Never empty.</summary>
        public IReadOnlyList<string> SpellIds { get; }
    }

    /// <summary>Which spells go in which wedge, in radial order.</summary>
    public sealed class RadialLayout
    {
        public RadialLayout(IReadOnlyList<RadialGroup> groups)
        {
            Groups = groups ?? throw new ArgumentNullException(nameof(groups));
        }

        /// <summary>The wedges clockwise from the top; only groups with at least one spell.</summary>
        public IReadOnlyList<RadialGroup> Groups { get; }

        public int Count => Groups.Count;

        /// <summary>
        /// The radial for a rule set: its groups in order, each with its spells in spells.json
        /// order. Groups with no spells are left out. A spell with no group (an older mod's)
        /// goes in the last group; with no groups at all, every spell goes in one "Spells" wedge.
        /// </summary>
        public static RadialLayout From(Rules rules)
        {
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            var defs = rules.Groups.Count > 0
                ? rules.Groups
                : new[] { new GroupDef { Id = "", Name = "Spells", Color = Rules.DefaultGroupColor } };
            var last = defs[defs.Count - 1].Id;
            var spellsOf = defs.ToDictionary(g => g.Id, _ => new List<string>(), StringComparer.Ordinal);
            foreach (var s in rules.Spells.Where(s => s.Radial))
                spellsOf[s.Group != null && spellsOf.ContainsKey(s.Group) ? s.Group : last].Add(s.Id);
            return new RadialLayout(defs
                .Where(g => spellsOf[g.Id].Count > 0)
                .Select(g => new RadialGroup(g.Id, g.Name, g.Color, spellsOf[g.Id]))
                .ToList());
        }

        /// <summary>The index of the wedge with this group id, or -1.</summary>
        public int IndexOf(string groupId)
        {
            for (int i = 0; i < Groups.Count; i++)
                if (Groups[i].Id == groupId) return i;
            return -1;
        }

        /// <summary>The index of the wedge holding this spell, or -1.</summary>
        public int GroupOfSpell(string spellId)
        {
            for (int i = 0; i < Groups.Count; i++)
                if (Groups[i].SpellIds.Contains(spellId)) return i;
            return -1;
        }
    }

    /// <summary>The radial's angles: pure functions.</summary>
    public static class RadialGeometry
    {
        /// <summary>Degrees clockwise from up, in [0, 360), of a vector with y up. (0, 0) is 0.</summary>
        public static double AngleOf(double right, double up)
        {
            double a = Math.Atan2(right, up) * 180 / Math.PI;
            if (a < 0) a += 360;
            return a >= 360 ? a - 360 : a;
        }

        /// <summary>The signed turn from one angle to another, in (-180, 180].</summary>
        public static double DeltaAngle(double from, double to)
        {
            double d = (to - from) % 360;
            if (d <= -180) d += 360;
            else if (d > 180) d -= 360;
            return d;
        }

        public static double WedgeWidth(int count) => 360.0 / count;

        /// <summary>Wedge 0 is centred straight up; the rest follow clockwise.</summary>
        public static double WedgeCentre(int index, int count) => index * WedgeWidth(count);

        /// <summary>
        /// The wedge an angle falls in. Each wedge runs from half a width before its centre
        /// (included) to half a width after (excluded), so a boundary belongs to the clockwise wedge.
        /// </summary>
        public static int WedgeAt(double angle, int count)
        {
            if (count <= 0) return -1;
            double w = WedgeWidth(count);
            double a = (angle % 360 + 360 + w / 2) % 360;
            return Math.Min(count - 1, (int)Math.Floor(a / w));
        }

        /// <summary>
        /// How many degrees a wedge's spell arc spans: slotDegrees per spell, but never narrower
        /// than the wedge itself (so pointing anywhere in the wedge points into its arc) or wider
        /// than maxArc.
        /// </summary>
        public static double ArcSpan(int spellCount, int wedgeCount, RadialTuning t) =>
            Math.Min(t.MaxArcDegrees, Math.Max(WedgeWidth(wedgeCount), spellCount * t.SlotDegrees));

        /// <summary>The centre angle of one spell in a wedge's arc, first spell counter-clockwise-most.</summary>
        public static double SlotCentre(int wedge, int wedgeCount, int slot, int spellCount, RadialTuning t)
        {
            double slotWidth = ArcSpan(spellCount, wedgeCount, t) / spellCount;
            double a = WedgeCentre(wedge, wedgeCount) + (slot - (spellCount - 1) / 2.0) * slotWidth;
            return (a % 360 + 360) % 360;
        }

        /// <summary>
        /// The spell in a wedge's arc at an angle, or -1 outside the arc. Slots run from their
        /// counter-clockwise edge (included) to the clockwise one (excluded); both arc ends are in.
        /// </summary>
        public static int SlotAt(double angle, int wedge, int wedgeCount, int spellCount, RadialTuning t)
        {
            if (spellCount <= 0) return -1;
            double span = ArcSpan(spellCount, wedgeCount, t);
            double d = DeltaAngle(WedgeCentre(wedge, wedgeCount), angle);
            if (Math.Abs(d) > span / 2) return -1;
            return Math.Min(spellCount - 1, (int)Math.Floor((d + span / 2) / (span / spellCount)));
        }
    }
}
