// The Gauntlet's system (docs/design/architecture-review.md step 9, D9): each player's run on the course, over the
// world's tick, rules and log. Port of sandbox/src/course.ts, line for line; the data is Course.cs. Chem.Play's (PlayModes
// owns one, with its state): the spell zones, the course and the runs, their text (after the modes' slots in the casts
// checksum text) and their save ("course", after the dragon). The world asks it for a zone against a cast and calls Apply
// here for a CourseCommand between ticks. It writes only the runs, the zones and the log.
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Chem
{
    public sealed class CourseSystem
    {
        readonly World world;
        readonly List<SpellZone> zones = new List<SpellZone>();
        readonly SortedDictionary<string, CourseRun> runs = new SortedDictionary<string, CourseRun>(StringComparer.Ordinal);

        internal CourseSystem(World world)
        {
            this.world = world;
        }

        public IReadOnlyList<SpellZone> Zones => zones;

        public CourseLayout Layout { get; private set; }

        public IReadOnlyDictionary<string, CourseRun> Runs => runs;

        public void AddZone(SpellZone zone)
        {
            if (zone != null) zones.Add(zone);
        }

        public void Set(CourseLayout course)
        {
            if (course == null || Layout != null) return;
            Layout = course;
            foreach (var z in course.Zones()) zones.Add(z);
        }

        public SpellZone ZoneAgainst(SpellDef s, int x, int y)
        {
            if (s == null) throw new ArgumentNullException(nameof(s));
            foreach (var z in zones)
                if (z.Area.Contains(x, y) && !Allows(z, s.Id)) return z;
            return null;
        }

        bool Allows(SpellZone z, string spell)
        {
            if (z.Spells.Contains(spell)) return true;
            foreach (var c in world.Rules.Combos.All)
                if (c.Spell == spell && c.Id.Split('+').All(e => z.Elements.Contains(e))) return true;
            var (slot, _) = world.HandRules.Find(spell);
            return slot != null && slot != HandsDef.Empty && slot.Split('+').All(e => z.Elements.Contains(e));
        }

        public void Apply(CourseCommand command)
        {
            world.FinishTick();
            var course = Layout;
            if (course == null || command.Player == null || !course.Area.Contains(command.X, command.Y)) return;
            if (!runs.TryGetValue(command.Player, out var run)) runs[command.Player] = run = new CourseRun { Player = command.Player };
            if (course.Gate.Contains(command.X, command.Y))
            {
                if (!run.Running) world.AddLog($"{command.Player} is through the gate: the clock is running", LogKind.Notable);
                run.Start = world.Tick;
                run.Checkpoint = -1;
                return;
            }
            if (!run.Running) return;
            if (course.Bell.Contains(command.X, command.Y))
            {
                RingBell(run);
                return;
            }
            for (int i = run.Checkpoint + 1; i < course.Booths.Count; i++)
            {
                if (!course.Booths[i].Zone.Area.Contains(command.X, command.Y)) continue;
                run.Checkpoint = i;
                world.AddLog($"{command.Player} reaches the checkpoint at the {course.Booths[i].Zone.Name}");
                return;
            }
        }

        void RingBell(CourseRun run)
        {
            int time = Math.Max(1, world.Tick - run.Start);
            bool best = run.Best == 0 || time < run.Best;
            run.Start = -1;
            run.Last = time;
            if (best) run.Best = time;
            world.AddLog(best
                ? $"{run.Player} rings the bell in {Seconds(time)}: a new best!"
                : $"{run.Player} rings the bell in {Seconds(time)} (best {Seconds(run.Best)})", LogKind.Notable);
        }

        public string Seconds(int ticks)
        {
            int tps = Math.Max(1, world.Rules.Sim.TicksPerSecond), tenths = (ticks * 10 + tps / 2) / tps;
            return $"{CourseRect.W(tenths / 10)}.{CourseRect.W(tenths % 10)} s";
        }

        public (int x, int y)? Respawn(string player, int x, int y)
        {
            var course = Layout;
            if (course == null || player == null || !course.Area.Contains(x, y)) return null;
            if (!runs.TryGetValue(player, out var run) || !run.Running) return null;
            if (run.Checkpoint < 0) return (course.StartX, course.StartY);
            var b = course.Booths[run.Checkpoint];
            return (b.CheckX, b.CheckY);
        }

        public string ZoneText() => string.Concat(zones.Select(z =>
            $"|z{z.Id}:{CourseRect.Text(z.Area)}:{string.Join(",", z.Elements)}:{string.Join(",", z.Spells)}"));

        public string Text() => string.Concat(runs.Values.Select(r =>
            $"|r{r.Player}:{CourseRect.W(r.Checkpoint)}:{CourseRect.W(r.Start)}:{CourseRect.W(r.Best)}:{CourseRect.W(r.Last)}"));

        public string Hash() => Fnv1a.Hash(Text() + ZoneText());

        internal void Save(JObject save)
        {
            if (runs.Count > 0) save["course"] = new JArray(runs.Values.Select(r => new JArray(r.Player, r.Checkpoint, r.Start, r.Best, r.Last)));
        }

        internal void Load(JObject s)
        {
            if (!(s["course"] is JArray saved)) return;
            foreach (var r in saved.OfType<JArray>())
                runs[(string)r[0]] = new CourseRun { Player = (string)r[0], Checkpoint = (int)r[1], Start = (int)r[2], Best = (int)r[3], Last = (int)r[4] };
        }
    }
}
