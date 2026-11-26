// The world's things under the rules in force (docs/design/world-tools.md G5, G6): the
// file's and the ones the mods on add, as WorldThings.With works them out. Where a mod's
// thing may stand is checked on every peer at the switch, as a building's site is. Things
// aren't on cells, so none of this reaches the cell hash; the rows are in the rules' fingerprint.
using System.Collections.Generic;
using System.Globalization;

namespace Chem
{
    public sealed partial class World
    {
        WorldThings thingsNow;
        Rules thingsFor;
        int thingsPlaced = -1;

        public WorldThings ThingsNow
        {
            get
            {
                if (!ReferenceEquals(thingsFor, Rules) || thingsPlaced != placedVersion)
                {
                    thingsNow = (roomBudgets?.Things ?? WorldThings.Empty).With(Rules, Seed).Plus(placedThings);
                    thingsFor = Rules;
                    thingsPlaced = placedVersion;
                }
                return thingsNow;
            }
        }

        string ThingSiteProblem(Rules next, string title, ISet<(string, int, int)> leaving)
        {
            if (next.ModThings.Count == 0) return null;
            var had = new Dictionary<(string, int, int), int>();
            foreach (var t in Rules.ModThings)
            {
                had.TryGetValue((t.Kind, t.X, t.Y), out int n);
                had[(t.Kind, t.X, t.Y)] = n + 1;
            }
            Dictionary<(int, int), string> buildings = null;
            var placed = new HashSet<(int, int)>();
            foreach (var t in next.ModThings)
            {
                if (had.TryGetValue((t.Kind, t.X, t.Y), out int n) && n > 0)
                {
                    had[(t.Kind, t.X, t.Y)] = n - 1;
                    continue;
                }
                if (buildings == null)
                {
                    NoteBaseBuildings();
                    buildings = OtherBuildings(next, null);
                }
                string why = placed.Add((t.X, t.Y)) ? CellProblem(t.X, t.Y, buildings, leaving) : "the same mod puts another thing there";
                if (why != null) return string.Format(CultureInfo.InvariantCulture, "{0} can't put a {1} at cell ({2}, {3}): {4}", title ?? "that mod", t.Kind, t.X, t.Y, why);
            }
            return null;
        }

        HashSet<(string, int, int)> LeavingThings(Rules next)
        {
            var leaving = new HashSet<(string, int, int)>();
            foreach (var t in Rules.ModThings) leaving.Add((t.Kind, t.X, t.Y));
            foreach (var t in next.ModThings) leaving.Remove((t.Kind, t.X, t.Y));
            return leaving;
        }

        RoomVerdict Found(RoomCommand c)
        {
            var thing = ulong.TryParse(c.Mod, NumberStyles.None, CultureInfo.InvariantCulture, out ulong id) ? ThingsNow.Find(new ThingId(id)) : null;
            if (thing == null || !thing.FromMod || c.Peer.Length == 0 || !Room.Found.Add(thing.Id.Value))
                return new RoomVerdict(false, "that isn't a first find", null);
            string name = Rules.Item(thing.Kind)?.Name ?? Rules.Entity(thing.Kind)?.Name ?? thing.Kind;
            var verdict = new RoomVerdict(true, null, $"{c.Peer} found the {char.ToLowerInvariant(name[0])}{name.Substring(1)}");
            AddLog(verdict.Line, LogKind.Notable);
            RoomDecided?.Invoke(c, verdict);
            return verdict;
        }
    }
}
