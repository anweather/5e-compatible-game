// What a text playtester acts on: the core on the stand-in valley (Chem.Mcp.StandInValley), no Unity.
// Every command goes through Run, which appends it and a hash of the world after it to the session's
// log, so `Playtest.Text replay <log>` runs the same commands again and says if the world agreed.
// The clock is the wall's, and only tells the model when to stop: nothing in the world reads it.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Chem.Mcp;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Chem.PlaytestText
{
    public sealed class Desk
    {
        const int MostWait = 300, MostRadius = 10, ReachChunks = 2, ChunkCells = 64, MostLines = 12;
        const string Player = "tester";

        readonly ModBudgets budgets;
        readonly Rules rules;
        readonly StandInValley valley;
        readonly (int low, int high) landRange;
        readonly uint seed;
        readonly string logPath, findingsPath;
        readonly double minutes;
        readonly Stopwatch clock = Stopwatch.StartNew();
        World world;
        int seen, commands;
        (int x, int y) at;

        public Desk(GameFolders folders, uint seed, double minutes, string outDir)
        {
            this.seed = seed;
            this.minutes = minutes;
            budgets = ModBudgets.Load(folders);
            rules = Rules.Load(folders.Rules);
            valley = StandInValley.Load(folders, budgets) ?? throw new InvalidOperationException("No world files beside the rules.");
            landRange = MapFile.Load(folders.World).LandRange;
            if (outDir != null)
            {
                Directory.CreateDirectory(outDir);
                logPath = Path.Combine(outDir, "commands.jsonl");
                findingsPath = Path.Combine(outDir, "findings.jsonl");
            }
            Run("fresh", new JObject()); // in the log: a session of several segments replays as one
        }

        /// <summary>The world's state in one word: what a replay compares.</summary>
        public string State => $"{world.Tick}:{world.CellHash()}:{world.CastHash()}";

        /// <summary>Runs one command by name: its text, then the time line. An exception out of the core is an answer, not a stop.</summary>
        public string Run(string name, JObject args)
        {
            string answer;
            try
            {
                answer = Dispatch(name, args);
            }
            catch (Exception e) when (!(e is ArgumentException || e is FormatException))
            {
                answer = $"EXCEPTION {e.GetType().Name}: {e.Message}\n{string.Join("\n", (e.StackTrace ?? "").Split('\n').Take(4))}";
            }
            catch (ArgumentException e)
            {
                answer = "Refused: " + e.Message;
            }
            commands++;
            if (logPath != null)
                File.AppendAllText(logPath, new JObject { ["n"] = commands, ["name"] = name, ["args"] = args, ["state"] = State, ["chars"] = answer.Length }.ToString(Formatting.None) + "\n");
            return answer + "\n" + TimeLine();
        }

        string TimeLine()
        {
            double left = minutes - clock.Elapsed.TotalMinutes;
            string line = $"[#{commands} tick {world.Tick} at {at.x},{at.y} {left.ToString("0.0", CultureInfo.InvariantCulture)} min left]";
            return left > 0 ? line : line + " TIME IS UP: report anything confirmed with note, then stop.";
        }

        string Dispatch(string name, JObject a)
        {
            int I(string key, int fallback = 0) => (int?)a[key] ?? fallback;
            switch (name)
            {
                case "places": return Places();
                case "spells": return Spells();
                case "map": return Map(I("x", at.x), I("y", at.y), Math.Min(MostRadius, I("r", 6)));
                case "cell": return CellText(I("x", at.x), I("y", at.y));
                case "census": return Census(I("x", at.x), I("y", at.y), Math.Min(32, I("r", 8)));
                case "rules": return RulesOf((string)a["id"] ?? "");
                case "go": return Go(I("x"), I("y"));
                case "cast": return Cast((string)a["spell"], I("x", at.x), I("y", at.y), I("dir"), Math.Max(1, I("charge", 1)));
                case "wait": return Wait(Math.Max(1, Math.Min(MostWait, I("ticks", 30))));
                case "spawn": return Spawn((string)a["kind"], I("x"), I("y"));
                case "creatures": return Creatures();
                case "weather": return SetWeather((string)a["kind"], I("minutes", 5));
                case "fresh": return Fresh();
                default: throw new ArgumentException($"no command {name}");
            }
        }

        string Fresh()
        {
            world = new World(rules, valley.W, valley.H, seed);
            world.SetLandRange(landRange);
            world.MakeFrom(valley);
            world.KeepStart();
            world.OpenRoom(ChangeMode.Open, Player, rules, budgets);
            seen = world.Log.Count;
            var start = budgets.Places.All.First(p => p.Kind == "village").Cells().First();
            Reach(start.x, start.y);
            return $"A fresh world, seed {seed}, {world.W} x {world.H} cells. You stand at {at.x},{at.y}.";
        }

        // Chunks stay active round the tester, as they do round a player.
        void Reach(int x, int y)
        {
            at = (x, y);
            int cx = x / ChunkCells, cy = y / ChunkCells;
            for (int j = 0; j < world.H / ChunkCells; j++)
                for (int i = 0; i < world.W / ChunkCells; i++)
                    world.SetActive(i, j, Math.Abs(i - cx) <= ReachChunks && Math.Abs(j - cy) <= ReachChunks);
        }

        string Places() => string.Join("\n", budgets.Places.All.Select(p =>
        {
            var cells = p.Cells().ToList();
            return cells.Count == 0 ? $"{p.Id} {p.Kind}" : $"{p.Id} {p.Kind} about {(int)cells.Average(c => c.x)},{(int)cells.Average(c => c.y)}";
        }));

        string Spells() => string.Join("\n", rules.Spells.Select(s => $"{s.Id} L{s.Level} {s.Shape} {s.Element ?? s.Effect}: {Short(s.Blurb)}"));

        static string Short(string text) => (text ?? "").Length <= 70 ? text ?? "" : text.Substring(0, 70);

        // One character a cell: what a player would notice first there.
        char Glyph(Cell c, Dictionary<string, char> legend)
        {
            if (c.Fire > 0) return '*';
            if (world.IsBlocking(c)) return '#';
            if (c.Gas != null) return '%';
            if (c.Water > 0) return c.Frozen ? '=' : '~';
            if (c.Cover != null) return Letter(c.Cover, char.ToUpperInvariant, legend);
            return Letter(c.T, char.ToLowerInvariant, legend);
        }

        static char Letter(string id, Func<char, char> shape, Dictionary<string, char> legend)
        {
            if (legend.TryGetValue(id, out char have)) return have;
            char letter = id.Select(shape).FirstOrDefault(ch => char.IsLetter(ch) && !legend.ContainsValue(ch));
            if (letter == default) letter = '?';
            legend[id] = letter;
            return letter;
        }

        string Map(int x, int y, int r)
        {
            var legend = new Dictionary<string, char>(StringComparer.Ordinal);
            var text = new StringBuilder($"map round {x},{y}, north up; rows y {y - r} to {y + r}, columns x {x - r} to {x + r}\n");
            for (int j = y - r; j <= y + r; j++)
            {
                for (int i = x - r; i <= x + r; i++)
                {
                    var c = world.At(i, j);
                    char glyph = c == null ? ' ' : Glyph(c, legend);
                    text.Append(i == at.x && j == at.y ? '@' : glyph);
                }
                text.Append('\n');
            }
            return text + "* fire # blocked % gas ~ liquid = frozen; " + string.Join(" ", legend.Select(l => $"{l.Value}={l.Key}"));
        }

        string CellText(int x, int y)
        {
            var c = world.At(x, y);
            if (c == null) return $"{x},{y} is off the world";
            var parts = new List<string> { $"{x},{y} {c.T}", $"height {c.Height}" };
            if (c.Water > 0) parts.Add($"liquid {world.LiquidId(c)} {c.Water}{(c.Frozen ? " frozen" : "")}");
            if (c.Cover != null) parts.Add($"cover {c.Cover}");
            if (c.Fire > 0) parts.Add($"fire {c.Fire} fuel {c.Fuel}");
            if (c.Gas != null) parts.Add($"gas {c.Gas} {c.GasAmt}");
            if (c.Charge > 0) parts.Add($"charge {c.Charge}");
            if (c.Life > 0) parts.Add($"life {c.Life}");
            if (c.Heat > 0) parts.Add($"heat {c.Heat}");
            return string.Join(", ", parts);
        }

        string Census(int x, int y, int r)
        {
            var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
            void Count(string key) => counts[key] = counts.TryGetValue(key, out int n) ? n + 1 : 1;
            for (int j = y - r; j <= y + r; j++)
                for (int i = x - r; i <= x + r; i++)
                {
                    var c = world.At(i, j);
                    if (c == null) continue;
                    Count("t:" + c.T);
                    if (c.Cover != null) Count("cover:" + c.Cover);
                    if (c.Gas != null) Count("gas:" + c.Gas);
                    if (c.Fire > 0) Count("burning");
                    if (c.Water > 0) Count((c.Frozen ? "frozen:" : "liquid:") + world.LiquidId(c));
                }
            return string.Join(" ", counts.Select(k => $"{k.Key}={k.Value}"));
        }

        // The rows of the rules naming an id: what a player's expectation can be checked against.
        string RulesOf(string id)
        {
            var doc = JObject.Parse(rules.ToJson());
            var found = new List<string>();
            foreach (var table in new[] { "terrain", "covers", "gases", "liquids" })
                if (doc[table]?[id] is JObject row) found.Add($"{table}.{id}: {row.ToString(Formatting.None)}");
            foreach (var spell in (doc["spells"] as JArray ?? new JArray()).Where(s => (string)s["id"] == id)) found.Add("spell: " + spell.ToString(Formatting.None));
            foreach (var rule in (doc["cellRules"] as JArray ?? new JArray()).Where(r => r.ToString(Formatting.None).Contains('"' + id + '"'))) found.Add(rule.ToString(Formatting.None));
            if (found.Count == 0) return $"no rows name {id}";
            string text = string.Join("\n", found.Take(MostLines));
            return found.Count > MostLines ? text + $"\n...and {found.Count - MostLines} more" : text;
        }

        string Go(int x, int y)
        {
            var c = world.At(x, y);
            if (c == null) return $"{x},{y} is off the world";
            if (world.IsBlocking(c)) return $"{x},{y} is blocked: {CellText(x, y)}";
            Reach(x, y);
            return "You are there. " + CellText(x, y);
        }

        string Cast(string spell, int x, int y, int dir, int charge)
        {
            if (rules.Spell(spell ?? "") == null) throw new ArgumentException($"no spell {spell}");
            CastAsAPlayer(rules.Spell(spell), x, y, dir, charge);
            return "Cast. " + CellText(x, y) + NewLines();
        }

        // World.Cast's x, y is where a spell starts and its aim an offset, so the target goes through what the view
        // does (SpellCaster.Aim): a spell from the caster starts a step beside them, pointed at the cell.
        void CastAsAPlayer(SpellDef s, int x, int y, int dir, int charge)
        {
            if (s.Origin != "self")
            {
                world.Cast(s.Id, x, y, dir, charge);
                return;
            }
            if (s.Shape == "burst")
            {
                world.Cast(s.Id, at.x, at.y, dir, charge);
                return;
            }
            int aimX = x - at.x, aimY = y - at.y;
            if (aimX != 0 || aimY != 0) dir = World.DirOf(aimX, aimY);
            world.Cast(s.Id, at.x + World.DirX(dir), at.y + World.DirY(dir), dir, charge, aimX, aimY);
        }

        string Wait(int ticks)
        {
            double worst = 0;
            for (int t = 0; t < ticks; t++)
            {
                var step = Stopwatch.StartNew();
                world.Step();
                worst = Math.Max(worst, step.Elapsed.TotalMilliseconds);
            }
            return $"{ticks} ticks, worst {worst.ToString("0", CultureInfo.InvariantCulture)} ms." + NewLines();
        }

        string Spawn(string kind, int x, int y)
        {
            var r = world.SpawnRaider(kind ?? "", x, y, rules.Entity(kind ?? "")?.Hp ?? 0);
            return r == null ? $"No {kind} at {x},{y}: not an entity, or someone stands there." : $"{kind} {r.Id} at {x},{y}, {r.Hp} hp.";
        }

        string Creatures() => world.Raiders.Count == 0 ? "None." : string.Join("\n", world.Raiders.Select(r => $"{r.Kind} {r.Id} at {r.X},{r.Y} hp {r.Hp}/{r.MaxHp}"));

        string SetWeather(string kind, int minutes)
        {
            if (!Enum.TryParse<WeatherKind>(kind, true, out var weather)) throw new ArgumentException($"no weather {kind}; {string.Join(", ", Enum.GetNames<WeatherKind>())}");
            return world.ApplyRoom(RoomCommand.SetWeather(Player, weather, minutes)).Line + NewLines();
        }

        string NewLines()
        {
            var lines = world.Log.Skip(seen).Select(l => l.Text).ToList();
            seen = world.Log.Count;
            if (lines.Count == 0) return "";
            return "\nlog: " + string.Join(" | ", lines.Take(MostLines)) + (lines.Count > MostLines ? $" | ...{lines.Count - MostLines} more" : "");
        }

        /// <summary>Keeps a finding: nothing in the world changes, and it isn't a command of the log.</summary>
        public string Note(JObject finding)
        {
            finding["atCommand"] = commands;
            finding["state"] = State;
            if (findingsPath != null) File.AppendAllText(findingsPath, finding.ToString(Formatting.None) + "\n");
            return "Noted.\n" + TimeLine();
        }
    }
}
