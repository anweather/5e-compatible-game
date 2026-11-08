// act and undo_act (docs/design/world-acts.md): something that happens once, now, for everyone, with no mod. "Add 20 trees
// near me" asked twice is two acts and an unchanged mod list. It picks cells as scatter does, from the world it reads, and
// sends them as a room command (World.Acts.cs), so what's a rule stays a mod and what's an event stays out of the list.
// And superseded mods: a change the chat makes that says everything an earlier one of the same player's said
// (Green fire, then Blue fire) takes that one off, so they don't pile up, each hiding the last.
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

#pragma warning disable S107 // a tool's parameters are its schema (RulesTools)

namespace Chem.Mcp
{
    public sealed partial class Workbench
    {
        // Each act gets its own seed unless asked for one, so a second one near the first picks afresh.
        int actSeed;

        /// <summary>act(): count of a terrain, cover, thing or animal over open cells near something, or a spell cast there, now.</summary>
        public string Act(string what, int? count, string near, int? within, int? spacing, string side, string edge, int? seed, string[] onto, int? lasts, string peer = null, LandAsk landAsk = null)
        {
            var plan = PlanAct(what, count, near, within, spacing, side, edge, seed, onto, lasts, landAsk);
            if (plan.NoRoom != null) return plan.NoRoom;
            string said = Decide(RoomCommand.Act(PeerOf(peer), plan.Command.ToString(Newtonsoft.Json.Formatting.None)));
            if (Refused(said)) return said;
            return FormattableString.Invariant($"{said}\n{(plan.ActKindOf == World.ActLand ? "At" : "Near")} {plan.PlaceName}.{plan.Note} {Landed(said)}, for everyone, and it isn't a mod: nothing to apply or save.{AfterAct(plan.ActKindOf)}");
        }

        // What act will send, worked out from the world as it is: the ask, or what's wrong with the place.
        sealed class ActPlan
        {
            public JObject Command;
            public string What, ActKindOf, PlaceName, NoRoom;
            /// <summary>What was changed from the ask (a stamp's rise cut to its width), said before the verdict; "" for nothing.</summary>
            public string Note = "";
        }

        // One ask of act. A wrong ask throws; a place with no room is NoRoom, as it's no mistake of the asker's.
        ActPlan PlanAct(string what, int? count, string near, int? within, int? spacing, string side, string edge, int? seed, string[] onto, int? lasts, LandAsk landAsk)
        {
            what = Required(what, "what");
            if (what == World.ActLand) return PlanLand(near, side, edge, seed, landAsk ?? new LandAsk());
            string kind = ActKind(what) ?? throw new RulesToolException($"There is no terrain, cover, thing, animal or spell called {what}, and it isn't golf-ball or cup; list shows what there is.");
            int wanted = count ?? 1;
            double most = MostOf(kind);
            if (wanted < 1 || wanted > most) throw new RulesToolException(FormattableString.Invariant($"count is 1 to {most} for a {kind}."));
            if (kind == World.ActCast && InForce.Spell(what).Level > Budgets.Number("triggers.maxLevel", 7))
                throw new RulesToolException(FormattableString.Invariant($"{what} is past level {Budgets.Number("triggers.maxLevel", 7)}, the highest an act can cast."));
            if (lasts < 0 || lasts > Budgets.Number("place.maxLasts", 21600)) throw new RulesToolException("lasts is in ticks, up to an hour.");
            var picked = PickNear(wanted, string.IsNullOrWhiteSpace(near) ? "me" : near, within ?? 12, spacing ?? 0, side, edge, seed ?? ++actSeed, kind == World.ActCast ? null : onto, out string name);
            if (picked.Count == 0) return new ActPlan { What = what, ActKindOf = kind, PlaceName = name, NoRoom = FormattableString.Invariant($"No open cell within {within ?? 12} cells of {name}; try further out, or somewhere else.") };
            var ask = new JObject { ["kind"] = kind, ["what"] = what, ["wanted"] = wanted, ["cells"] = new JArray(picked.Select(c => new JArray(c.x, c.y))) };
            if (lasts > 0) ask["lasts"] = lasts.Value;
            if (onto?.Length > 0) ask["onto"] = new JArray(onto.Cast<object>().ToArray());
            return new ActPlan { What = what, Command = ask, ActKindOf = kind, PlaceName = name };
        }

        /// <summary>call_act(): a named act, a trigger with on: called from a mod that's on, fired at a place: each of its rows as act would do it, one firing.</summary>
        public string CallAct(string id, string near, string side, string edge, string peer = null)
        {
            id = Required(id, "id");
            var named = InForce.TriggersOn(TriggerEvent.Called).FirstOrDefault(t => t.Id == id)
                ?? throw new RulesToolException($"There is no named act called {id}: it's a trigger with on: called in a mod that's on. list triggers shows them.");
            var rows = named.Do.Select(a => a.Act).ToList();
            var spot = SpotFor(id, rows, near, side, edge);
            var plans = rows.Select(row => spot != null ? PlanOfRow(row, spot.For(row), null, null) : PlanOfRow(row, near, side, edge)).ToList();
            var parts = plans.Where(p => p.NoRoom == null).ToList();
            string place = spot?.Name ?? plans[0].PlaceName;
            if (parts.Count == 0) return $"None of {id} landed at {place}, so nothing's done: {string.Join(" ", plans.Select(p => $"{p.What}: {p.NoRoom}"))}";
            int cells = parts.Sum(p => (p.Command["cells"] as JArray)?.Count ?? 0);
            double most = Budgets.Number("lay.maxCells", 256);
            if (cells > most) throw new RulesToolException(FormattableString.Invariant($"{id} asks for {cells} cells at once; a firing does at most {most}."));
            var sent = plans.Select(p => (object)(p.Command ?? new JObject { ["kind"] = p.ActKindOf, ["what"] = p.What, ["why"] = p.NoRoom }));
            var ask = new JObject { ["kind"] = World.ActCalled, ["what"] = id, ["parts"] = new JArray(sent.ToArray()) };
            string said = Decide(RoomCommand.Act(PeerOf(peer), ask.ToString(Newtonsoft.Json.Formatting.None)));
            if (Refused(said)) return said;
            string roomy = spot != null && !spot.Fits ? " No spot there had room for all of it, so this was the one with the most." : "";
            string notes = string.Concat(plans.Select(p => p.Note)) + (spot?.Moved ?? "");
            return $"{said}\nAt {place}.{roomy}{notes} {Landed(said)}, for everyone. undo_act takes back what can be, all at once: casts, animals and golf stay.";
        }

        // How a reply ends: only what really landed is called done.
        static string Landed(string said) => said.Contains(World.FellShort) ? "What landed is done; tell the player what didn't" : "It's done";

        // Where a firing at a place stands: one cell for all its rows but land, the cell its land rows stand on, and
        // whether every row has room there. Moved says how far the land went from where it was asked; "" if it didn't.
        sealed class Spot
        {
            public string Near, Land, Name, Moved = "";
            public (int x, int y) At;
            public bool Fits;

            /// <summary>Where a row of the firing stands.</summary>
            public string For(JObject row) => IsLand(row) ? Land : Near;
        }

        const int SpotsTried = 16, SpotsApart = 6, DefaultWithin = 12;

        static bool IsLand(JObject row) => (string)row["act"] == World.ActLand;

        static string CellName((int x, int y) c) => FormattableString.Invariant($"[{c.x}, {c.y}]");

        // One spot for a firing of several rows at a place of many cells, so its rows stand together. It comes from the
        // world's seed and the firing's id and number, never a peer; the first with room for every row, else the one with
        // room for most. Land goes where a lone act would stamp it, the place's middle, or as near it as it fits, at most
        // land.maxMove cells off: a stamp that fits nowhere so near isn't moved, and is refused there and says why (#156).
        // Null when it's fired at one cell, or has one row: each row is where it's asked already.
        Spot SpotFor(string id, List<JObject> rows, string near, string side, string edge)
        {
            if (rows.Count < 2) return null;
            var from = Targets(string.IsNullOrWhiteSpace(near) ? "me" : near, side, edge, out string name);
            if (from.Count < 2) return null;
            var others = rows.Where(r => !IsLand(r)).ToList();
            int reach = others.Select(r => (int?)r["within"] ?? DefaultWithin).DefaultIfEmpty(DefaultWithin).Max();
            return OnWorld(w =>
            {
                int salt = id.Aggregate(w.ActsDone, (hash, c) => unchecked(hash * 31 + c));
                var spots = OpenNear(w, DistanceMap(w, from, reach), SpotsTried, SpotsApart, salt, null);
                if (others.Count == rows.Count) return Best(w, rows, spots, from[0], name);
                var asked = Middle(from);
                var landFits = LandSpots(w, asked, salt).Where(c => rows.Where(IsLand).All(row => RowFits(w, row, c.x, c.y))).ToList();
                if (landFits.Count > 0)
                {
                    var spot = Best(w, rows, landFits, asked, name);
                    int off = Math.Max(Math.Abs(spot.At.x - asked.x), Math.Abs(spot.At.y - asked.y));
                    if (off > 0) spot.Moved = FormattableString.Invariant($" The land stands {off} cell{(off == 1 ? "" : "s")} off {CellName(asked)}, the nearest it fit.");
                    return spot;
                }
                var rest = Best(w, others, others.Count > 0 ? spots : new List<(int, int)> { asked }, asked, name);
                rest.Land = CellName(asked);
                rest.Fits = false;
                return rest;
            });
        }

        // The first spot with room for every row, else the one with room for most; the fallback when no spot is open.
        Spot Best(World w, List<JObject> rows, List<(int x, int y)> spots, (int x, int y) fallback, string name)
        {
            var best = (x: fallback.x, y: fallback.y, rows: -1);
            foreach (var (x, y) in spots)
            {
                int fit = rows.Count(row => RowFits(w, row, x, y));
                if (fit > best.rows) best = (x, y, fit);
                if (fit == rows.Count) break;
            }
            string at = CellName((best.x, best.y));
            return new Spot { Near = at, Land = at, At = (best.x, best.y), Name = $"{name}, round {at}", Fits = best.rows == rows.Count };
        }

        // Where a land row may stand: the cell asked first, then every open cell up to land.maxMove from it, nearest first.
        List<(int x, int y)> LandSpots(World w, (int x, int y) asked, int salt)
        {
            int most = (int)Budgets.Number("land.maxMove", 4);
            var distance = DistanceMap(w, new List<(int, int)> { asked }, most);
            var near = OpenNear(w, distance, (2 * most + 1) * (2 * most + 1), 0, salt, null).OrderBy(c => distance[w.Idx(c.x, c.y)]);
            return new[] { asked }.Concat(near.Where(c => c != asked)).ToList();
        }

        // A place's middle, as a lone land act takes it.
        static (int x, int y) Middle(List<(int x, int y)> from) => (from.Sum(c => c.x) / from.Count, from.Sum(c => c.y) / from.Count);

        // Whether all of a row can land round a cell.
        bool RowFits(World w, JObject row, int x, int y)
        {
            if ((string)row["act"] == World.ActLand)
            {
                var stamp = StampOf(AskOf(row), x, y, (int?)row["seed"] ?? 1);
                return SettledUnder(w, stamp) == null && w.LandRefusal(stamp) == null;
            }
            int wanted = (int?)row["count"] ?? 1;
            var distance = DistanceMap(w, new List<(int, int)> { (x, y) }, (int?)row["within"] ?? DefaultWithin);
            return OpenNear(w, distance, wanted, (int?)row["spacing"] ?? 0, 1, OntoOf(row)).Count >= wanted;
        }

        static LandAsk AskOf(JObject row) =>
            new LandAsk { Profile = (string)row["profile"] ?? LandStamp.Dome, Along = (string)row["along"], Radius = (int?)row["radius"], Rise = (int?)row["rise"], Rough = (int?)row["rough"] };

        static string[] OntoOf(JObject row) => (row["onto"] as JArray)?.Select(t => (string)t).ToArray();

        // A row of a called trigger, what act would be asked, with where it's being called.
        ActPlan PlanOfRow(JObject row, string near, string side, string edge)
        {
            return PlanAct((string)row["act"], (int?)row["count"], near, (int?)row["within"], (int?)row["spacing"], side, edge, (int?)row["seed"], OntoOf(row), (int?)row["lasts"], AskOf(row));
        }

        /// <summary>What a land stamp asks for: metres and cells as a player says them.</summary>
        public sealed class LandAsk
        {
            public string Profile = LandStamp.Dome, Along;
            public int? Radius, Rise, Rough;
        }

        static readonly Dictionary<string, (int x, int y)> Lines = new Dictionary<string, (int, int)>(StringComparer.OrdinalIgnoreCase)
        {
            ["north"] = (0, 1),
            ["east"] = (1, 0),
            ["northeast"] = (1, -1),
            ["southeast"] = (1, 1),
        };

        // One stamp at a time, where it's asked for, off buildings and settled ground; water and the terrain's range the world refuses itself.
        ActPlan PlanLand(string near, string side, string edge, int? seed, LandAsk ask)
        {
            var from = Targets(string.IsNullOrWhiteSpace(near) ? "me" : near, side, edge, out string name);
            var (x, y) = Middle(from);
            var stamp = StampOf(ask, x, y, seed ?? ++actSeed);
            string settled = OnWorld(w => SettledUnder(w, stamp));
            if (settled != null) return new ActPlan { What = World.ActLand, ActKindOf = World.ActLand, PlaceName = name, NoRoom = $"That would move the ground under {settled}; try further off." };
            return new ActPlan { What = World.ActLand, ActKindOf = World.ActLand, PlaceName = name, Note = SteepNote(ask), Command = new JObject { ["kind"] = World.ActLand, ["what"] = World.ActLand, ["stamp"] = stamp.Row } };
        }

        // The stamp a land ask makes at a cell; a wrong ask throws.
        LandStamp StampOf(LandAsk ask, int x, int y, int seed)
        {
            int radius = ask.Radius ?? 8, rise = ask.Rise ?? 0;
            double mostRadius = Budgets.Number("land.maxRadius", 32), mostRise = Budgets.Number("land.maxRise", 100);
            if (radius < 1 || radius > mostRadius) throw new RulesToolException(FormattableString.Invariant($"radius is 1 to {mostRadius} cells."));
            if (rise == 0 || Math.Abs(rise) > mostRise) throw new RulesToolException(FormattableString.Invariant($"rise is in metres, up to {mostRise} either way, and not 0; no land goes below {OnWorld(w => w.LandRange.low) / LandUnitsPerMetre} m."));
            var line = (x: 0, y: 0);
            if (LandStamp.NeedsLine(ask.Profile) && (ask.Along == null || !Lines.TryGetValue(ask.Along, out line)))
                throw new RulesToolException($"A {ask.Profile} needs along: {string.Join(", ", Lines.Keys)}.");
            rise = Math.Sign(rise) * Math.Min(Math.Abs(rise), SteepestRise(radius));
            var stamp = LandStamp.Make(x, y, radius, rise * LandUnitsPerMetre, ask.Profile, line.x, line.y, ask.Rough ?? (ask.Profile == LandStamp.Flat ? 0 : LandRoughness), seed);
            if (!stamp.Known) throw new RulesToolException("profile is flat, dome, cone, ridge, canyon or cliff, and rough is 0 to 100.");
            return stamp;
        }

        const int LandUnitsPerMetre = 8;

        // The most a stamp this wide rises, in whole metres: land.steepest a cell of radius, so its sides look climbable
        // (#156: a 100 m mountain 12 cells across, 30 m canyon walls at radius 6). Never below 1 m.
        int SteepestRise(int radius) => Math.Max(1, (int)Math.Floor(Budgets.Number("land.steepest", 1.5) * radius));

        // What the reply says when a stamp's rise was cut to its width; "" when it wasn't.
        string SteepNote(LandAsk ask)
        {
            int radius = ask.Radius ?? 8, rise = Math.Abs(ask.Rise ?? 0), most = SteepestRise(radius);
            if (rise <= most) return "";
            double steepest = Budgets.Number("land.steepest", 1.5);
            int widest = (int)Budgets.Number("land.maxRadius", 32), needs = (int)Math.Ceiling(rise / steepest);
            string wider = needs <= widest
                ? $"radius {needs} rises the {rise} m asked"
                : $"{rise} m needs radius {needs}, past the {widest} a stamp may have, so the tallest one stamp rises is {SteepestRise(widest)} m, at radius {widest}";
            return FormattableString.Invariant($" {rise} m is too steep for a radius of {radius}, so it rises {most} m: at most {steepest} m a cell of radius, so its sides stay climbable. A wider radius rises higher: {wider}. Tell the player what they got.");
        }

        // How uneven land is when nobody says: nothing the land makes is a smooth slab, and a flat is asked for as flat.
        const int LandRoughness = 40;

        // What stands on a stamp's cells: a building, or the ground of a settled place (SettledKinds).
        string SettledUnder(World w, LandStamp s)
        {
            var built = new HashSet<(int, int)>();
            var names = new Dictionary<(int, int), string>();
            foreach (var row in w.BuildingsNow)
                foreach (var c in w.BuildingCellsOf(row))
                {
                    built.Add((c.X, c.Y));
                    names[(c.X, c.Y)] = row.Id;
                }
            for (int y = s.Y - s.Radius; y <= s.Y + s.Radius; y++)
                for (int x = s.X - s.Radius; x <= s.X + s.Radius; x++)
                {
                    if ((x - s.X) * (x - s.X) + (y - s.Y) * (y - s.Y) > s.Radius * s.Radius) continue;
                    if (built.Contains((x, y))) return names[(x, y)];
                    var place = SettledPlace(x, y);
                    if (place != null) return place.Name;
                }
            return null;
        }

        static string AfterAct(string kind)
        {
            if (kind == World.ActGolf) return " Any spell that lands on the ball moves it, and players at the course take turns.";
            if (kind == World.ActLand) return " undo_act takes it back. Land stops nothing yet: raiders, sight and spells pass over it.";
            return kind == World.ActThing || kind == World.ActTerrain || kind == World.ActCover ? " undo_act takes it back." : "";
        }

        /// <summary>undo_act(): takes back an act that laid ground or placed things, the last of the player's if none is named.</summary>
        public string UndoAct(int? act, string peer = null) =>
            Decide(RoomCommand.Undo(PeerOf(peer), act));

        string ActKind(string what)
        {
            if (what == World.GolfBall || what == World.GolfCup) return World.ActGolf;
            if (InForce.Terrain.ContainsKey(what)) return World.ActTerrain;
            if (InForce.Covers.ContainsKey(what)) return World.ActCover;
            if (InForce.Entities.ContainsKey(what) || InForce.Items.ContainsKey(what)) return World.ActThing;
            if (InForce.Fauna.Species(what) != null) return World.ActAnimal;
            return InForce.Spell(what) != null ? World.ActCast : null;
        }

        double MostOf(string kind)
        {
            switch (kind)
            {
                case World.ActGolf: return 1;
                case World.ActCast: return Budgets.Number("triggers.maxPerTick", 4);
                case World.ActAnimal: return Budgets.Number("place.maxSummoned", 24);
                case World.ActThing: return Budgets.Number("place.maxLive", 64);
                default: return Budgets.Number("lay.maxCells", 256);
            }
        }

        // ---------- superseded mods ----------

        // The earlier mods this one says all of: the chat's own, by the same player. Each goes, and the line says so.
        string Superseded(string json, string id, string peer)
        {
            var now = Now();
            if (now == null) return "";
            var says = Says(json);
            if (says.Count == 0) return "";
            var ours = new HashSet<string>(drafts.Values.Select(d => d.OnAs).Where(on => on != null), StringComparer.Ordinal);
            var gone = now.Room.Mods.Where(m => m.Id != id && m.By == peer && ours.Contains(m.Id)).Where(m => Says(m.Json) is var old && old.Count > 0 && old.IsSubsetOf(says))
                .Select(m => (m.Id, m.Title)).ToList();
            var taken = new List<string>();
            foreach (var (mod, title) in gone)
                if (!Refused(Decide(RoomCommand.Revert(peer, mod)))) taken.Add(title);
            return taken.Count == 0 ? "" : $"\nIt says all that {string.Join(" and ", taken)} said, so that's off now: one change, not a pile.";
        }

        // Everything a mod says, a value to a line: where it sits, and for a list that isn't a colour or rows by id what's
        // in it, as two mods adding to one list add different things.
        static HashSet<string> Says(string json)
        {
            var lines = new HashSet<string>(StringComparer.Ordinal);
            Say(ModPatch.Parse(json).Json, "", lines);
            return lines;
        }

        static void Say(JToken token, string at, HashSet<string> lines)
        {
            if (token is JObject o)
            {
                foreach (var p in o.Properties()) Say(p.Value, at + "/" + p.Name, lines);
                return;
            }
            if (token is JArray rows && rows.Count > 0 && rows.All(r => r is JObject row && row["id"] != null))
            {
                // Rows by id, as a spell's are: one's look is the same line in two mods.
                foreach (var row in rows) Say(row, at + "/" + (string)row["id"], lines);
                return;
            }
            bool numbers = token is JArray list && list.All(v => v.Type == JTokenType.Integer || v.Type == JTokenType.Float);
            lines.Add(token is JArray && !numbers ? at + "=" + token.ToString(Newtonsoft.Json.Formatting.None) : at);
        }
    }
}
