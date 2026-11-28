// Progress between runs of the Hold (docs/design/the-hold.md, phase 5): each wizard's points, brought by a command so every
// peer agrees, and what they unlock. A wizard with none brought has everything, as does anyone in a moment, so a test, a bot
// or a playtest isn't held back; the locks hold only in a run (the Hold's), so the valley stays a sandbox. Chem.Play's
// (PlayModes owns one, with the Hold's run): the points are its state, saved as "progress" after the Hold's and in the
// casts checksum text before the hands. The world asks it what's open to a wizard's hands (World.Progress.cs), and the
// Hold earns points as a run ends and lets go what's locked as one starts.
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Chem
{
    public sealed class ProgressSystem
    {
        public const int AllPoints = 1000000;

        readonly World world;
        readonly HoldSystem hold;
        readonly SortedDictionary<string, int> points = new SortedDictionary<string, int>(StringComparer.Ordinal);

        internal ProgressSystem(World world, HoldSystem hold)
        {
            this.world = world;
            this.hold = hold;
        }

        public int? PointsOf(string player) => points.TryGetValue(player, out int p) ? p : (int?)null;

        bool NothingHeld(string player) => !points.ContainsKey(player) || hold.State?.Moment != null;

        internal bool AllOpen(string player) => NothingHeld(player) || !hold.Running;

        public IEnumerable<UnlockDef> UnlocksOf(string player) => Through(world.Rules.Sim.Hold.Unlocks, PointsOf(player) ?? AllPoints);

        internal static IEnumerable<UnlockDef> Through(IEnumerable<UnlockDef> rows, int left)
        {
            foreach (var row in rows)
            {
                if ((left -= row.Cost) < 0) yield break;
                yield return row;
            }
        }

        public int PointsAway(string player, UnlockDef row)
        {
            if (NothingHeld(player)) return 0;
            int through = 0;
            foreach (var u in world.Rules.Sim.Hold.Unlocks)
            {
                through += u.Cost;
                if (u == row) return Math.Max(0, through - (PointsOf(player) ?? AllPoints));
            }
            return 0;
        }

        public UnlockDef NextUnlock(string player) => world.Rules.Sim.Hold.Unlocks.FirstOrDefault(u => PointsAway(player, u) > 0);

        internal bool Opens(string player, Func<UnlockDef, bool> gives) =>
            AllOpen(player) || !world.Rules.Sim.Hold.Unlocks.Any(gives) || UnlocksOf(player).Any(gives);

        internal bool Bring(string player, int brought)
        {
            if (brought < 0) return false;
            points[player] = brought;
            world.LetGoLocked(player);
            return true;
        }

        internal void LetGoLockedAll()
        {
            foreach (var player in points.Keys.ToList()) world.LetGoLocked(player);
        }

        internal void Earn(HoldParams p, int survived)
        {
            foreach (var player in points.Keys.ToList())
                points[player] = (int)Math.Min(AllPoints, (long)points[player] + (long)survived * p.PointsPerWave);
        }

        public string Text() => string.Concat(points.Select(kv => $"|G{kv.Key}:{World.Whole(kv.Value)}"));

        internal void Save(JObject save)
        {
            if (points.Count > 0) save["progress"] = new JObject(points.Select(kv => new JProperty(kv.Key, kv.Value)));
        }

        internal void Load(JObject save)
        {
            foreach (var p in (save["progress"] as JObject)?.Properties() ?? Enumerable.Empty<JProperty>()) points[p.Name] = (int)p.Value;
        }
    }
}
