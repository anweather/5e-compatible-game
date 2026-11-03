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
// Another mod may name a material a mod on adds (a wing naming its house's clay
// tile, #86). When the owner goes, reverted or lapsed, every mod naming one of its
// materials goes with it at that tick, and so does every mod naming one of theirs:
// one pass in room order, since a mod naming another's material went on after it.
// They go in one switch, logged once, the owner first and then its dependents in
// reverse of the order they went on (the last on first off).
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

        // A mod lapses: off as by a revert, the change mode aside, its dependents with it. If the mods over it
        // don't fit without them, every mod over it goes too.
        void Lapse(int at)
        {
            var mod = Room.Mods[at];
            var gone = WithDependents(at);
            string why = Compose(Without(gone), "", out var next);
            if (why == null) why = OverlapProblem(next.Effective, "");
            if (why != null)
            {
                gone = Room.Mods.Skip(at + 1).Reverse().Prepend(mod).ToList();
                next = RoomRules.WithMods(Room.Mods.Take(at).Select(m => new ActiveMod(m.Id, m.Json)));
            }
            TakeOff(gone, next);
            var verdict = new RoomVerdict(true, null, $"{mod.Title} lapsed{AlsoGone(gone)} ({RoomPolicy.Words(Room.Mode)} room)");
            AddLog(verdict.Line, LogKind.Notable);
            foreach (var m in gone) RoomDecided?.Invoke(RoomCommand.Revert(m.By, m.Id), verdict);
        }

        /// <summary>
        /// The mod at this place in the room and every mod that goes with it (#86): each later one naming a
        /// material one of them adds, chains too. The mod first, then its dependents, the last on first.
        /// </summary>
        public List<RoomMod> WithDependents(int at)
        {
            var owners = new HashSet<string>(StringComparer.Ordinal) { Room.Mods[at].Id };
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var dependents = new List<RoomMod>();
            for (int i = at + 1; i < Room.Mods.Count; i++)
            {
                ids.UnionWith(TemporaryMaterials.Where(t => owners.Contains(t.Mod)).Select(t => t.Id));
                if (TempMaterials.Named(Room.Mods[i].Json, ids).Count == 0) continue;
                owners.Add(Room.Mods[i].Id);
                dependents.Add(Room.Mods[i]);
            }
            dependents.Reverse();
            return dependents.Prepend(Room.Mods[at]).ToList();
        }

        // The mods on but these, in room order.
        IEnumerable<ActiveMod> Without(List<RoomMod> gone) => Room.Mods.Where(m => !gone.Contains(m)).Select(m => new ActiveMod(m.Id, m.Json));

        // These mods go off together, in one switch: the rules without them in force, their materials swept.
        void TakeOff(List<RoomMod> gone, RuleSet next)
        {
            Room.Mods.RemoveAll(gone.Contains);
            PutInForce(next);
            Room.Found.RemoveWhere(t => ThingsNow.Find(new ThingId(t)) == null);
        }

        // ", and A wing, A porch with it" for the mods that went with the first; "" for none.
        static string AlsoGone(List<RoomMod> gone) =>
            gone.Count == 1 ? "" : ", and " + string.Join(", ", gone.Skip(1).Select(m => m.Title)) + " with it";

        /// <summary>
        /// For a patch the checks refuse: why, if it names a material a mod once added that isn't in force now (Room.Retired
        /// keeps who added it), else null. A wing can't go on before its house; a mod adding its own material again passes.
        /// </summary>
        string OwnerProblem(string title, string json)
        {
            var gone = new HashSet<string>(Room.Retired.Where(r => !Rules.HasMaterial(r.Table, r.Id)).Select(r => r.Id), StringComparer.Ordinal);
            var named = TempMaterials.Named(json, gone);
            if (named.Count == 0) return null;
            string id = named.Min;
            string owner = Room.Retired.Last(r => r.Id == id).Mod;
            return $"{title} names {id}, a material of the mod {owner}, which isn't on; switch {owner} on first";
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
