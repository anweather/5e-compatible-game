// Mixes in a room (docs/design/primitives.md §4, P5; J-062; Rules.Mix.cs). Two rows sharing a
// `mix` group that meet, a liquid touching another or a cover laid on another, need no
// reaction row: the room derives their mix, once, and from then on they make it. A new pair is
// asked for where it meets and made at the end of that tick, in the order the tick met them, so
// every peer appends it at the same tick; a meeting between ticks (a cast) makes it at once.
// Until then the liquid stops as at a wall, and a cover waits to land. Past the room's cap
// (budgets.json materials.maxMixes) neither happens: the arriving one stops as at a wall.
// A mix lasts while any parent is in force, lapsing with the later one into what that one lapses
// into (World.Lapse.cs). Without a room there's no list to hold one: rows of a group are walls.
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Chem
{
    public sealed partial class World
    {
        /// <summary>The derived mixes a room holds at most when its budgets don't say.</summary>
        public const int DefaultMaxMixes = 16;

        // The pairs met this tick, in the order they met, and the covers waiting on them.
        readonly List<(string table, string a, string b)> mixWanted = new List<(string table, string a, string b)>();
        readonly List<(Cell cell, string a, string b)> mixLanding = new List<(Cell cell, string a, string b)>();

        // Mixes whose row can't go in the rules: every peer finds the same, so none is asked for again.
        readonly HashSet<string> mixRefused = new HashSet<string>(StringComparer.Ordinal);

        int MaxMixes => (int)(roomBudgets?.Number("materials.maxMixes", DefaultMaxMixes) ?? DefaultMaxMixes);

        // Whether a mix not in the rules yet could still be made.
        bool MayMix(string id) => Room.Mixes.Count < MaxMixes && !mixRefused.Contains(id);

        /// <summary>Whether liquid l, running onto a cell holding another, mixes with it: one group, and the mix is there or can be made.</summary>
        bool Mixes(LiquidDef l, Cell there)
        {
            var other = Rules.Liquid(there.Liquid);
            if (Room == null || l.Mix == null || l.Mix != other.Mix) return false;
            string id = Rules.MixId(l.Id, other.Id);
            return Rules.LiquidIndex(id) >= 0 || MayMix(id);
        }

        // Pools that settle side by side never run onto each other, so touching counts: the first neighbour, in
        // N, E, S, W order, of its group that isn't its parent or child becomes their mix, and so does this cell.
        // A mix beside a parent is left alone, so a band of it stands between two pools rather than eating both.
        // True if this cell changed kind.
        bool Contact(Chunk ch, int i, Cell c, LiquidDef l)
        {
            for (int d = 0; d < 4; d++)
            {
                var n = Chunk.Next(ch, i, d, out var nch, out _);
                if (n == null || !nch.Active || n.Water == 0 || n.Frozen || n.Liquid == c.Liquid || !Mixes(l, n)) continue;
                string met = Rules.Liquid(n.Liquid).Id, id = Rules.MixId(l.Id, met);
                if (id == l.Id || id == met) continue;
                int kind = MeetLiquid(l, n, ch, nch);
                if (kind < 0) return false;
                c.Liquid = kind;
                n.Liquid = kind;
                WaterMoved(ch);
                WaterMoved(nch);
                return true;
            }
            return false;
        }

        // Liquid l meets the liquid on a cell it mixes with: the kind they make, or -1 if it's only been
        // asked for, and it stops for now. The chunks count as moved, so the still-water pass doesn't skip the next try.
        int MeetLiquid(LiquidDef l, Cell there, Chunk ch, Chunk other)
        {
            var met = Rules.Liquid(there.Liquid);
            int kind = Rules.LiquidIndex(Rules.MixId(l.Id, met.Id));
            if (kind >= 0) return kind;
            WaterMoved(ch);
            WaterMoved(other);
            WantMix(Rules.LiquidsKey, l.Id, met.Id);
            return Rules.LiquidIndex(Rules.MixId(l.Id, met.Id));
        }

        // What laying a cover on a cell puts there: itself, unless the cell's cover is of its group: then
        // their mix, or null if it isn't made yet (it lands at the tick's end) or can't be (it stops).
        string LaidOver(Cell c, string coverId)
        {
            if (Room == null || c.Cover == null || coverId == null || c.Cover == coverId) return coverId;
            if (!Rules.Covers.TryGetValue(c.Cover, out var there) || !Rules.Covers.TryGetValue(coverId, out var laid)) return coverId;
            if (laid.Mix == null || laid.Mix != there.Mix) return coverId;
            string id = Rules.MixId(there.Id, laid.Id);
            if (Rules.Covers.ContainsKey(id)) return id;
            if (!MayMix(id)) return null;
            mixLanding.Add((c, there.Id, laid.Id));
            WantMix("covers", there.Id, laid.Id);
            return null;
        }

        void WantMix(string table, string a, string b)
        {
            if (!mixWanted.Contains((table, a, b))) mixWanted.Add((table, a, b));
            if (stage == TickStage.None) MixDue();
        }

        // The end of a tick: what met is made, in one switch, and the covers waiting land.
        void MixDue()
        {
            if (mixWanted.Count == 0) return;
            var made = mixWanted.Select(w => MakeMix(w.table, w.a, w.b)).Where(name => name != null).ToList();
            mixWanted.Clear();
            if (made.Count > 0) PutInForce(RoomRules.Unmixed);
            foreach (string name in made) AddLog($"{name} mix", LogKind.Notable);
            foreach (var (cell, a, b) in mixLanding)
            {
                string id = Rules.MixId(a, b);
                if ((cell.Cover == a || cell.Cover == b) && Rules.Covers.ContainsKey(id)) PutCover(cell, id);
            }
            mixLanding.Clear();
        }

        // Adds the mix of two rows to the room's list: its name, or null if it's there, past the cap or won't fit the rules.
        string MakeMix(string table, string a, string b)
        {
            string id = Rules.MixId(a, b);
            if (Room.Mixes.Any(m => m.Table == table && m.Id == id) || !MayMix(id)) return null;
            var row = Rules.DeriveMix(table, a, Rules.MaterialRow(table, a), b, Rules.MaterialRow(table, b));
            var mix = new RoomMix { Table = table, Id = id, Row = row.ToString(Newtonsoft.Json.Formatting.None), Parents = Rules.MixParents(id), Tick = Tick };
            if (!Fits(RoomRules.Unmixed, Room.Mixes.Append(mix)))
            {
                mixRefused.Add(id);
                return null;
            }
            Room.Mixes.Add(mix);
            return (string)row["name"];
        }

        static bool Fits(RuleSet plain, IEnumerable<RoomMix> mixes)
        {
            try
            {
                plain.WithMixes(mixes);
                return true;
            }
            catch (ModException)
            {
                return false;
            }
        }

        // The rules with the room's mixes over them. A mix with no parent left in force goes, and so does one
        // that no longer fits (it named what a mod took with it): each lapses as its last parent did.
        RuleSet MixedOver(RuleSet plain, List<TempMaterial> gone)
        {
            if (Room.Mixes.Count == 0) return plain;
            var kept = new List<RoomMix>();
            foreach (var m in Room.Mixes)
                if (m.Parents.Any(p => plain.Effective.HasMaterial(m.Table, p)) && Fits(plain, kept.Append(m))) kept.Add(m);
                else gone.Add(new TempMaterial(m.Table, m.Id, MixOwner, new MaterialLapse(LastParent(m).Lapse.Into, 0)));
            Room.Mixes.Clear();
            Room.Mixes.AddRange(kept);
            return plain.WithMixes(kept);
        }

        /// <summary>What the retired list says added a mix: no mod did.</summary>
        public const string MixOwner = "mix";

        TempMaterial LastParent(RoomMix m) => TemporaryMaterials.LastOrDefault(t => t.Table == m.Table && m.Parents.Contains(t.Id));

        static JArray SavedMix(RoomMix m) => new JArray(m.Table, m.Id, new JArray(m.Parents), m.Row, m.Tick);

        static RoomMix LoadedMix(JToken m) =>
            new RoomMix { Table = (string)m[0], Id = (string)m[1], Parents = m[2].Select(p => (string)p).ToList(), Row = (string)m[3], Tick = (int)m[4] };
    }
}
