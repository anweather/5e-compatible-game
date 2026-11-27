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
// last checkpoint (CourseRespawn), not the yard. All whole numbers, in ticks. This file is the course's data;
// CourseSystem.cs is its system, which the world owns.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Chem
{
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

    public sealed class SpellZone
    {
        public string Id, Name;
        public CellRect Area;
        public IReadOnlyList<string> Elements = Array.Empty<string>();
        public IReadOnlyList<string> Spells = Array.Empty<string>();
    }

    public sealed class CourseBooth
    {
        public SpellZone Zone;
        public int CheckX, CheckY;
    }

    public sealed class CourseLayout
    {
        public const string FileName = MapFile.CourseName;

        public string Name;
        public CellRect Area, Gate, Bell;
        public int StartX, StartY;
        public IReadOnlyList<CourseBooth> Booths = Array.Empty<CourseBooth>();

        public static CourseLayout Load(string worldDir)
        {
            string path = Path.Combine(worldDir, FileName);
            return File.Exists(path) ? FromJson(File.ReadAllText(path)) : null;
        }

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

        public IEnumerable<SpellZone> Zones() => Booths.Select(b => b.Zone);

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

    public sealed class CourseRun
    {
        public string Player;
        public int Checkpoint = -1, Start = -1, Best, Last;
        public bool Running => Start >= 0;
    }
}
