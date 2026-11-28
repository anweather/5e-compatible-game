// The Gauntlet's system (docs/design/architecture-review.md step 9, D9): each player's run on the course, over the
// world's tick, rules and log. Port of sandbox/src/course.ts, line for line; the data is Course.cs. Chem.Play's (PlayModes
// owns one): the world keeps the spell zones, the course and the runs (World.Play.cs, with ZoneAgainst, their text and
// save), and calls Apply here for a CourseCommand between ticks. It writes only the runs, the zones and the log.
using System;
using System.Collections.Generic;

namespace Chem
{
    public sealed class CourseSystem
    {
        readonly World world;

        internal CourseSystem(World world)
        {
            this.world = world;
        }

        public IReadOnlyList<SpellZone> Zones => world.Zones;

        public CourseLayout Layout => world.CourseLayout;

        public IReadOnlyDictionary<string, CourseRun> Runs => world.Runs;

        public void AddZone(SpellZone zone)
        {
            if (zone != null) world.Zones.Add(zone);
        }

        public void Set(CourseLayout course)
        {
            if (course == null || Layout != null) return;
            world.CourseLayout = course;
            foreach (var z in course.Zones()) world.Zones.Add(z);
        }

        public SpellZone ZoneAgainst(SpellDef s, int x, int y) => world.ZoneAgainst(s, x, y);

        public void Apply(CourseCommand command)
        {
            world.FinishTick();
            var course = Layout;
            var runs = world.Runs;
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
            if (!world.Runs.TryGetValue(player, out var run) || !run.Running) return null;
            if (run.Checkpoint < 0) return (course.StartX, course.StartY);
            var b = course.Booths[run.Checkpoint];
            return (b.CheckX, b.CheckY);
        }

        public string ZoneText() => world.ZoneText();

        public string Text() => world.CourseText();

        public string Hash() => world.CourseHash();
    }
}
