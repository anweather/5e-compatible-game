// Temporary materials in a room (docs/design/primitives.md §4, step 2; J-062). A
// material a mod adds lives while its mod is on. When the mod goes, reverted or
// lapsed, every cell holding one is swept at that tick: a terrain turns into its
// lapse.into (else dirt), a cover or gas goes (or turns into its into), and its Life
// is cleared. The sweep goes over every loaded cell in row-major order, inactive
// chunks too, as ApplyRules' Sanitize already does, so no chunk wakes holding one.
// The room keeps a retired list (id, into, mod, tick; in the save and CastHash) so a
// cell that comes back later naming one (the dragon's starting state, an older save)
// turns into the same. A mod whose row gives lapse.after goes off by itself that
// many ticks after its switch, at the end of the tick, in room order, at most one a
// tick: every peer lapses it at the same tick, with no clock but the tick.
using System;
using System.Collections.Generic;
using System.Linq;

namespace Chem
{
    public sealed partial class World
    {
        RuleSet tempsFor;
        IReadOnlyList<TempMaterial> temps = Array.Empty<TempMaterial>();

        /// <summary>The room's temporary materials, in mod, table and patch order (TempMaterials); empty without a room.</summary>
        public IReadOnlyList<TempMaterial> TemporaryMaterials
        {
            get
            {
                if (RoomRules == null) return Array.Empty<TempMaterial>();
                if (!ReferenceEquals(tempsFor, RoomRules))
                {
                    temps = TempMaterials.Of(RoomRules);
                    tempsFor = RoomRules;
                }
                return temps;
            }
        }

        /// <summary>The tick a mod on lapses at: its switch plus the shortest lapse.after of its own materials; -1 if none lapses.</summary>
        public int LapsesAt(RoomMod mod)
        {
            int after = 0;
            foreach (var t in TemporaryMaterials)
                if (t.Mod == mod.Id && t.Lapse.After > 0 && (after == 0 || t.Lapse.After < after)) after = t.Lapse.After;
            return after == 0 ? -1 : mod.Tick + after;
        }

        // The end of a tick: the first mod in room order whose lapse is due goes off; another due waits a tick.
        void LapseDue()
        {
            if (Room == null || TemporaryMaterials.Count == 0) return;
            for (int i = 0; i < Room.Mods.Count; i++)
            {
                int at = LapsesAt(Room.Mods[i]);
                if (at < 0 || Tick < at) continue;
                Lapse(i);
                return;
            }
        }

        // A mod lapses: off as by a revert, the change mode aside. If the mods over it don't fit without it, they go too.
        void Lapse(int at)
        {
            var mod = Room.Mods[at];
            var gone = new List<RoomMod> { mod };
            string why = Compose(Room.Mods.Where((m, i) => i != at).Select(m => new ActiveMod(m.Id, m.Json)), "", out var next);
            if (why == null) why = OverlapProblem(next.Effective, "");
            if (why != null)
            {
                gone.AddRange(Room.Mods.Skip(at + 1));
                next = RoomRules.WithMods(Room.Mods.Take(at).Select(m => new ActiveMod(m.Id, m.Json)));
            }
            Room.Mods.RemoveAll(gone.Contains);
            PutInForce(next);
            Room.Found.RemoveWhere(t => ThingsNow.Find(new ThingId(t)) == null);
            string also = gone.Count == 1 ? "" : ", and " + string.Join(", ", gone.Skip(1).Select(m => m.Title)) + " with it";
            var verdict = new RoomVerdict(true, null, $"{mod.Title} lapsed{also} ({RoomPolicy.Words(Room.Mode)} room)");
            AddLog(verdict.Line, LogKind.Notable);
            foreach (var m in gone) RoomDecided?.Invoke(RoomCommand.Revert(m.By, m.Id), verdict);
        }

        /// <summary>Puts the room's rules in force: the temporary materials they drop are retired and swept first.</summary>
        void PutInForce(RuleSet next)
        {
            var gone = TemporaryMaterials.Where(t => !next.Effective.HasMaterial(t.Table, t.Id)).ToList();
            RoomRules = next;
            Room.Retired.RemoveAll(r => next.Effective.HasMaterial(r.Table, r.Id));
            Retire(gone);
            ApplyRules(next.Effective);
        }

        void Retire(List<TempMaterial> gone)
        {
            if (gone.Count == 0) return;
            foreach (var t in gone)
            {
                Room.Retired.RemoveAll(r => r.Table == t.Table && r.Id == t.Id);
                Room.Retired.Add(new RetiredMaterial { Table = t.Table, Id = t.Id, Into = t.Lapse.Into, Mod = t.Mod, Tick = Tick });
            }
            int keep = (int)(roomBudgets?.Number("materials.maxLive", 32) ?? 32);
            if (Room.Retired.Count > keep) Room.Retired.RemoveRange(0, Room.Retired.Count - keep);
            var into = Rules.MaterialTables.Select(table => gone.Where(t => t.Table == table).ToDictionary(t => t.Id, t => t.Lapse.Into)).ToArray();
            foreach (var c in Cells) Sweep(c, into[0], into[1], into[2]);
        }

        static void Sweep(Cell c, Dictionary<string, string> terrain, Dictionary<string, string> covers, Dictionary<string, string> gases)
        {
            if (terrain.TryGetValue(c.T, out var t))
            {
                c.T = t ?? "dirt";
                c.Life = 0;
            }
            if (c.Cover != null && covers.TryGetValue(c.Cover, out var cover))
            {
                c.Cover = cover;
                c.Life = 0;
                c.Slick = 0;
            }
            if (c.Gas != null && gases.TryGetValue(c.Gas, out var gas))
            {
                c.Gas = gas;
                if (gas == null) c.GasAmt = 0;
            }
        }

        /// <summary>What a retired terrain turns into now (Sanitize), or null if it isn't one or its into isn't in force.</summary>
        string RetiredInto(string terrain)
        {
            var r = Room?.Retired.LastOrDefault(x => x.Table == "terrain" && x.Id == terrain);
            return r?.Into != null && Rules.Terrain.ContainsKey(r.Into) ? r.Into : null;
        }

        /// <summary>Null if the rules hold no more temporary materials than a room may, else why not.</summary>
        string LiveProblem(RuleSet next, string title)
        {
            int live = TempMaterials.Of(next).Count;
            int max = (int)roomBudgets.Number("materials.maxLive", 32);
            return live > max ? $"{title} would make {live} temporary materials live; a room holds at most {max}" : null;
        }
    }
}
