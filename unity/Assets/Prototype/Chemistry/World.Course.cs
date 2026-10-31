// The Gauntlet's rules (docs/BACKLOG.md 25, docs/handoffs/obstacle-course-brief.md): spell zones, and an obstacle
// course's gate, checkpoints and bell, with a clock and a best time per player. Port of sandbox/src/course.ts, line
// for line.
//
// A spell zone is a block of cells that allows only some elements (every element of the combo that casts the spell)
// or some spells by id: a cast landing in it with anything else fizzles. Zones are world data, the same on every
// peer, and in the checksum. The course (world/gauntlet.json) is a gate, booths in a row (each a zone with a
// checkpoint cell) and a bell. A CourseCommand says a player stands on a cell (the view sends one as its wizard steps
// onto a new cell inside the course): the gate starts the player's clock, each booth further on is the checkpoint
// reached, and the bell stops the clock and keeps the best time. A wizard downed inside the course gets up at its
// last checkpoint (CourseRespawn), not the yard. All whole numbers, in ticks.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Chem
{
    /// <summary>The course's blocks of cells (Village.cs's CellRect), written [x0, y0, x1, y1] with the corners inclusive.</summary>
    static class CourseRect
    {
        internal static CellRect Parse(JToken t, string what)
        {
            if (!(t is JArray a) || a.Count != 4) throw new FormatException($"{CourseLayout.FileName}: {what} needs [x0, y0, x1, y1]");
            int x0 = (int)a[0], y0 = (int)a[1], x1 = (int)a[2], y1 = (int)a[3];
            return new CellRect(Math.Min(x0, x1), Math.Min(y0, y1), Math.Abs(x1 - x0) + 1, Math.Abs(y1 - y0) + 1);
        }

        internal static string Text(CellRect r) => string.Join(":", W(r.X), W(r.Y), W(r.Right - 1), W(r.Bottom - 1));

        internal static string W(int n) => n.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>An area that allows only some elements or spells: anything else cast into it fizzles.</summary>
    public sealed class SpellZone
    {
        public string Id, Name;
        public CellRect Area;
        /// <summary>Elements allowed: a spell is, if a combo of only these elements casts it.</summary>
        public IReadOnlyList<string> Elements = Array.Empty<string>();
        /// <summary>Spells allowed by id, whatever their elements (Misty Step has none).</summary>
        public IReadOnlyList<string> Spells = Array.Empty<string>();
    }

    /// <summary>One booth of the course: its zone (its area and what it allows) and the cell a wizard downed past it gets up on.</summary>
    public sealed class CourseBooth
    {
        public SpellZone Zone;
        public int CheckX, CheckY;
    }

    /// <summary>world/gauntlet.json: the course's area, its gate, its booths in order and its bell.</summary>
    public sealed class CourseLayout
    {
        public const string FileName = "gauntlet.json";

        public string Name;
        /// <summary>Downed inside this, a wizard on a run gets up at its last checkpoint.</summary>
        public CellRect Area, Gate, Bell;
        /// <summary>Where a wizard downed before the first booth gets up: the gate's middle.</summary>
        public int StartX, StartY;
        public IReadOnlyList<CourseBooth> Booths = Array.Empty<CourseBooth>();

        /// <summary>The course in world/gauntlet.json, or null without the file.</summary>
        public static CourseLayout Load(string worldDir)
        {
            string path = Path.Combine(worldDir, FileName);
            return File.Exists(path) ? FromJson(File.ReadAllText(path)) : null;
        }

        /// <summary>The course in the file's text. Throws <see cref="FormatException"/> for one that can't be read.</summary>
        public static CourseLayout FromJson(string json)
        {
            var o = JObject.Parse(json);
            var gate = CourseRect.Parse(o["gate"], "the gate");
            var booths = new List<CourseBooth>();
            foreach (var b in (o["booths"] as JArray ?? new JArray()).OfType<JObject>())
            {
                string id = (string)b["id"] ?? $"booth {booths.Count + 1}";
                if (!(b["checkpoint"] is JArray cp) || cp.Count != 2) throw new FormatException($"{FileName}: {id} needs \"checkpoint\": [x, y]");
                var allow = b["allow"] as JObject ?? new JObject();
                var zone = new SpellZone
                {
                    Id = id,
                    Name = (string)b["name"] ?? id,
                    Area = CourseRect.Parse(b["area"], id),
                    Elements = (allow["elements"] as JArray ?? new JArray()).Select(e => (string)e).ToList(),
                    Spells = (allow["spells"] as JArray ?? new JArray()).Select(e => (string)e).ToList(),
                };
                booths.Add(new CourseBooth { Zone = zone, CheckX = (int)cp[0], CheckY = (int)cp[1] });
            }
            return new CourseLayout
            {
                Name = (string)o["name"] ?? "the course",
                Area = CourseRect.Parse(o["area"], "the area"),
                Gate = gate,
                Bell = CourseRect.Parse(o["bell"], "the bell"),
                StartX = gate.X + (gate.W - 1) / 2,
                StartY = gate.Y + (gate.H - 1) / 2,
                Booths = booths,
            };
        }

        /// <summary>The booths' zones, in order.</summary>
        public IEnumerable<SpellZone> Zones() => Booths.Select(b => b.Zone);

        /// <summary>What's wrong with the course against these rules: elements and spells the rules don't know, pieces outside its area.</summary>
        public IEnumerable<string> Check(Rules rules)
        {
            foreach (var z in Zones())
            {
                foreach (var e in z.Elements.Where(e => !rules.Combos.Elements.Contains(e))) yield return $"{FileName}: {z.Id} allows unknown element {e}";
                foreach (var s in z.Spells.Where(s => rules.Spell(s) == null)) yield return $"{FileName}: {z.Id} allows unknown spell {s}";
            }
            foreach (var b in Booths)
                if (!Area.Contains(b.CheckX, b.CheckY)) yield return $"{FileName}: {b.Zone.Id}'s checkpoint is outside the course";
            if (!Area.Contains(Gate.X, Gate.Y) || !Area.Contains(Bell.X, Bell.Y)) yield return $"{FileName}: the gate and the bell must be inside the course";
        }
    }

    /// <summary>A player stands on a cell (the view sends one as its wizard steps onto a new cell inside the course).</summary>
    public readonly struct CourseCommand
    {
        public CourseCommand(string player, int x, int y)
        {
            Player = player;
            X = x;
            Y = y;
        }

        public string Player { get; }
        public int X { get; }
        public int Y { get; }
    }

    /// <summary>
    /// One player's run: the last checkpoint reached (-1 the gate), the tick the clock started (-1 when it isn't
    /// running), the best time and the last, in ticks (0 for none yet).
    /// </summary>
    public sealed class CourseRun
    {
        public string Player;
        public int Checkpoint = -1, Start = -1, Best, Last;
        public bool Running => Start >= 0;
    }

    public sealed partial class World
    {
        readonly List<SpellZone> zones = new List<SpellZone>();
        readonly SortedDictionary<string, CourseRun> courseRuns = new SortedDictionary<string, CourseRun>(StringComparer.Ordinal);

        /// <summary>The spell zones, in order (the course's booths among them).</summary>
        public IReadOnlyList<SpellZone> Zones => zones;

        /// <summary>The course laid out, or null.</summary>
        public CourseLayout Course { get; private set; }

        /// <summary>Each player's run on the course, by player.</summary>
        public IReadOnlyDictionary<string, CourseRun> CourseRuns => courseRuns;

        /// <summary>Adds a spell zone anywhere in the valley (addZone in the TS). World data: every peer adds the same, before the first tick.</summary>
        public void AddZone(SpellZone zone)
        {
            if (zone != null) zones.Add(zone);
        }

        /// <summary>Lays the course out (setCourse in the TS): its booths become spell zones. World data, like AddZone.</summary>
        public void SetCourse(CourseLayout course)
        {
            if (course == null || Course != null) return;
            Course = course;
            foreach (var z in course.Zones()) zones.Add(z);
        }

        /// <summary>The zone that smothers this spell cast at (x, y) (zoneAgainst in the TS), or null when it may go off there.</summary>
        public SpellZone ZoneAgainst(SpellDef s, int x, int y)
        {
            foreach (var z in zones)
                if (z.Area.Contains(x, y) && !Allows(z, s.Id)) return z;
            return null;
        }

        // A spell is allowed by id, or when a combo of only the zone's elements casts it (or holds into it).
        bool Allows(SpellZone z, string spell)
        {
            if (z.Spells.Contains(spell)) return true;
            foreach (var c in Rules.Combos.All)
                if ((c.Spell == spell || c.Held == spell) && c.Id.Split('+').All(e => z.Elements.Contains(e))) return true;
            return false;
        }

        /// <summary>
        /// A player stands on (x, y) (courseStep in the TS): the gate (re)starts their clock, a booth further on than
        /// their last is the checkpoint reached, and the bell stops the clock. Between ticks, like a cast.
        /// </summary>
        public void ApplyCourse(CourseCommand command)
        {
            FinishTick(); // commands land between whole ticks (StepSome)
            var course = Course;
            if (course == null || command.Player == null || !course.Area.Contains(command.X, command.Y)) return;
            if (!courseRuns.TryGetValue(command.Player, out var run)) courseRuns[command.Player] = run = new CourseRun { Player = command.Player };
            if (course.Gate.Contains(command.X, command.Y))
            {
                if (!run.Running) AddLog($"{command.Player} is through the gate: the clock is running", LogKind.Notable);
                run.Start = Tick;
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
                AddLog($"{command.Player} reaches the checkpoint at the {course.Booths[i].Zone.Name}");
                return;
            }
        }

        void RingBell(CourseRun run)
        {
            int time = Math.Max(1, Tick - run.Start);
            bool best = run.Best == 0 || time < run.Best;
            run.Start = -1;
            run.Last = time;
            if (best) run.Best = time;
            AddLog(best
                ? $"{run.Player} rings the bell in {Seconds(time)}: a new best!"
                : $"{run.Player} rings the bell in {Seconds(time)} (best {Seconds(run.Best)})", LogKind.Notable);
        }

        /// <summary>Ticks as seconds to a tenth, "42.5 s" (courseSeconds in the TS).</summary>
        public string Seconds(int ticks)
        {
            int tps = Math.Max(1, Rules.Sim.TicksPerSecond), tenths = (ticks * 10 + tps / 2) / tps;
            return $"{CourseRect.W(tenths / 10)}.{CourseRect.W(tenths % 10)} s";
        }

        /// <summary>
        /// Where a wizard downed at (x, y) gets up (courseRespawn in the TS): inside the course on a run, the last
        /// checkpoint reached (the gate before the first); otherwise null, the yard as ever.
        /// </summary>
        public (int x, int y)? CourseRespawn(string player, int x, int y)
        {
            var course = Course;
            if (course == null || player == null || !course.Area.Contains(x, y)) return null;
            if (!courseRuns.TryGetValue(player, out var run) || !run.Running) return null;
            if (run.Checkpoint < 0) return (course.StartX, course.StartY);
            var b = course.Booths[run.Checkpoint];
            return (b.CheckX, b.CheckY);
        }

        /// <summary>The zones as text, "" with none (so worlds without any hash as they did; zoneState in the TS).</summary>
        public string ZoneText() => string.Concat(zones.Select(z =>
            $"|z{z.Id}:{CourseRect.Text(z.Area)}:{string.Join(",", z.Elements)}:{string.Join(",", z.Spells)}"));

        /// <summary>The players' runs as text, "" before anyone ran (courseState in the TS): player, checkpoint, start, best, last.</summary>
        public string CourseText() => string.Concat(courseRuns.Values.Select(r =>
            $"|r{r.Player}:{CourseRect.W(r.Checkpoint)}:{CourseRect.W(r.Start)}:{CourseRect.W(r.Best)}:{CourseRect.W(r.Last)}"));

        /// <summary>FNV-1a of CourseText and ZoneText (both part of CastHash, so of the online checksum).</summary>
        public string CourseHash() => Fnv1a.Hash(CourseText() + ZoneText());

        // The runs into a save, best times and all; the zones and the course are world data, laid again on load.
        void SaveCourse(JObject save)
        {
            if (courseRuns.Count > 0) save["course"] = new JArray(courseRuns.Values.Select(r => new JArray(r.Player, r.Checkpoint, r.Start, r.Best, r.Last)));
        }

        void LoadCourse(JObject s)
        {
            if (!(s["course"] is JArray saved)) return;
            foreach (var r in saved.OfType<JArray>())
                courseRuns[(string)r[0]] = new CourseRun { Player = (string)r[0], Checkpoint = (int)r[1], Start = (int)r[2], Best = (int)r[3], Last = (int)r[4] };
        }
    }
}
