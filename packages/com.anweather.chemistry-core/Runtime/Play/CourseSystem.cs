// The Gauntlet's system (docs/design/architecture-review.md step 9, D9): the spell zones and each player's run on the
// course, over the world's tick, rules and log. Port of sandbox/src/course.ts, line for line; the data is Course.cs.
// The world owns one (World.CourseSystem). It reads the world's tick, its rules (combos, hands, ticks a second) and
// writes the world's log; it changes only its own zones and runs, and only through a CourseCommand between ticks.
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Chem
{
    /// <summary>The spell zones and the course's runs (World.CourseSystem): world data, the same on every peer, in the checksum.</summary>
    public sealed class CourseSystem
    {
        readonly World world;
        readonly List<SpellZone> zones = new List<SpellZone>();
        readonly SortedDictionary<string, CourseRun> runs = new SortedDictionary<string, CourseRun>(StringComparer.Ordinal);

        internal CourseSystem(World world)
        {
            this.world = world;
        }

        /// <summary>The spell zones, in order (the course's booths among them).</summary>
        public IReadOnlyList<SpellZone> Zones => zones;

        /// <summary>The course laid out, or null.</summary>
        public CourseLayout Layout { get; private set; }

        /// <summary>Each player's run on the course, by player.</summary>
        public IReadOnlyDictionary<string, CourseRun> Runs => runs;

        /// <summary>Adds a spell zone anywhere in the valley (addZone in the TS). World data: every peer adds the same, before the first tick.</summary>
        public void AddZone(SpellZone zone)
        {
            if (zone != null) zones.Add(zone);
        }

        /// <summary>Lays the course out (setCourse in the TS): its booths become spell zones. World data, like AddZone.</summary>
        public void Set(CourseLayout course)
        {
            if (course == null || Layout != null) return;
            Layout = course;
            foreach (var z in course.Zones()) zones.Add(z);
        }

        /// <summary>The zone that smothers this spell cast at (x, y) (zoneAgainst in the TS), or null when it may go off there.</summary>
        public SpellZone ZoneAgainst(SpellDef s, int x, int y)
        {
            foreach (var z in zones)
                if (z.Area.Contains(x, y) && !Allows(z, s.Id)) return z;
            return null;
        }

        // A spell is allowed by id, or when a combo or a hand row of only the zone's elements casts it.
        bool Allows(SpellZone z, string spell)
        {
            if (z.Spells.Contains(spell)) return true;
            foreach (var c in world.Rules.Combos.All)
                if (c.Spell == spell && c.Id.Split('+').All(e => z.Elements.Contains(e))) return true;
            var (slot, _) = world.HandRules.Find(spell);
            return slot != null && slot != HandsDef.Empty && slot.Split('+').All(e => z.Elements.Contains(e));
        }

        /// <summary>
        /// A player stands on (x, y) (courseStep in the TS): the gate (re)starts their clock, a booth further on than
        /// their last is the checkpoint reached, and the bell stops the clock. Between ticks, like a cast.
        /// </summary>
        public void Apply(CourseCommand command)
        {
            world.FinishTick(); // commands land between whole ticks (StepSome)
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

        /// <summary>Ticks as seconds to a tenth, "42.5 s" (courseSeconds in the TS).</summary>
        public string Seconds(int ticks)
        {
            int tps = Math.Max(1, world.Rules.Sim.TicksPerSecond), tenths = (ticks * 10 + tps / 2) / tps;
            return $"{CourseRect.W(tenths / 10)}.{CourseRect.W(tenths % 10)} s";
        }

        /// <summary>
        /// Where a wizard downed at (x, y) gets up (courseRespawn in the TS): inside the course on a run, the last
        /// checkpoint reached (the gate before the first); otherwise null, the yard as ever.
        /// </summary>
        public (int x, int y)? Respawn(string player, int x, int y)
        {
            var course = Layout;
            if (course == null || player == null || !course.Area.Contains(x, y)) return null;
            if (!runs.TryGetValue(player, out var run) || !run.Running) return null;
            if (run.Checkpoint < 0) return (course.StartX, course.StartY);
            var b = course.Booths[run.Checkpoint];
            return (b.CheckX, b.CheckY);
        }

        /// <summary>The zones as text, "" with none (so worlds without any hash as they did; zoneState in the TS).</summary>
        public string ZoneText() => string.Concat(zones.Select(z =>
            $"|z{z.Id}:{CourseRect.Text(z.Area)}:{string.Join(",", z.Elements)}:{string.Join(",", z.Spells)}"));

        /// <summary>The players' runs as text, "" before anyone ran (courseState in the TS): player, checkpoint, start, best, last.</summary>
        public string Text() => string.Concat(runs.Values.Select(r =>
            $"|r{r.Player}:{CourseRect.W(r.Checkpoint)}:{CourseRect.W(r.Start)}:{CourseRect.W(r.Best)}:{CourseRect.W(r.Last)}"));

        /// <summary>FNV-1a of Text and ZoneText (both part of CastHash, so of the online checksum).</summary>
        public string Hash() => Fnv1a.Hash(Text() + ZoneText());

        // The runs into a save, best times and all; the zones and the course are world data, laid again on load.
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
