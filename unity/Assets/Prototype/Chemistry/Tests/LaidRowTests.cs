// A map's laid rows (docs/design/the-hold.md, builders.md section 4): walls, towers, a gate and a road as rows, laid over
// the cells at the start. The valley's hold is checked by its numbers; a smaller one is a fixture, as a mod would bring it.
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    public class LaidRowTests
    {
        const string Hold = "hold#1", Shut = "timber-gate", Open = "timber-gate-open";
        const int Size = 192, Footprint = 32, HoldX = 120, HoldY = 352;

        static Rules Rules => ChemistryTests.R;
        static WorldBuildings Valley => WorldBuildings.Load(ChemistryTests.WorldDir());
        static JObject Row => Valley.FileRows.Single(r => (string)r["id"] == Hold);

        // The valley's corner the hold stands in, as grass, with the rows laid.
        static World Laid(out BuildingRow row, Rules rules = null)
        {
            var w = new World(rules ?? Rules, 576, 576, 7);
            for (int y = 318; y < 410; y++)
                for (int x = 80; x < 230; x++) w.Paint(x, y, "grass");
            Valley.LayInto(w);
            row = BuildingRow.Read(Hold, Row, w.Rules.Buildings);
            return w;
        }

        static List<BuildingCell> Cells(BuildingRow row, World w) => BuildingRaster.Cells(row, w.Rules.Buildings);

        // Every open cell reached on foot from here, north, east, south and west.
        static HashSet<(int x, int y)> Walked(World w, (int x, int y) from, int x0, int y0, int x1, int y1)
        {
            var seen = new HashSet<(int, int)> { from };
            var todo = new Queue<(int x, int y)>(seen);
            while (todo.Count > 0)
            {
                var (x, y) = todo.Dequeue();
                foreach (var n in new[] { (x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1) })
                    if (n.Item1 >= x0 && n.Item1 <= x1 && n.Item2 >= y0 && n.Item2 <= y1 && !w.IsBlocking(w.At(n.Item1, n.Item2)) && seen.Add(n)) todo.Enqueue(n);
            }
            return seen;
        }

        [Test]
        public void TheHoldIsAMapsRowAndPassesTheBuildingChecks()
        {
            string patch = new JObject { ["world"] = new JObject { ["buildings"] = new JObject { [Hold] = Row } } }.ToString();
            Assert.DoesNotThrow(() => Rules.WithMods(new[] { ModPatch.Parse(patch) }));
        }

        [Test]
        public void TheRowsCellsAreWhatTheWorldHolds()
        {
            var w = Laid(out var row);
            var cells = Cells(row, w);
            Assert.That(cells.Where(c => c.Part != BuildingPart.Road).Max(c => c.X) - row.X, Is.EqualTo(Footprint - 1));
            Assert.That(cells.Where(c => c.Part != BuildingPart.Road).Max(c => c.Y) - row.Y, Is.EqualTo(Footprint - 1));
            foreach (var c in cells) Assert.That(w.At(c.X, c.Y).T, Is.EqualTo(c.Terrain), $"({c.X}, {c.Y})");
            Assert.That(cells.Count(c => c.Terrain == "stone"), Is.GreaterThan(300));
            Assert.That(cells.Count(c => c.Terrain == Open), Is.EqualTo(3));
        }

        [Test]
        public void ARowWorldsWithoutItDontSayItStands()
        {
            var empty = new World(Rules, 576, 576, 7);
            Assert.That(Valley.RowsOn((x, y) => empty.At(x, y)?.T, empty.Rules.Buildings).Any(r => (string)r["id"] == Hold), Is.False);
            var w = Laid(out _);
            Assert.That(Valley.RowsOn((x, y) => w.At(x, y)?.T, w.Rules.Buildings).Any(r => (string)r["id"] == Hold));
        }

        [Test]
        public void AWorldTooSmallForARowIsLeftAlone()
        {
            var w = new World(Rules, HoldX + 13, 576, 7);
            Valley.LayInto(w);
            Assert.That(Enumerable.Range(HoldX, 13).All(x => w.At(x, HoldY + 3).T != "stone"), "part of it would fit, and none of it goes in");
        }

        [Test]
        public void ATreeUnderTheWallsIsGoneAndTheThingsDontMove()
        {
            var rules = Rules;
            var w = new World(rules, 576, 576, 7);
            w.Paint(HoldX + 4, HoldY + 15, "tree");
            w.Paint(HoldX + 12, HoldY + 6, "tree");
            int thingsWere = WorldThings.Load(ChemistryTests.WorldDir()).All.Count;
            Valley.LayInto(w);
            Assert.That(w.At(HoldX + 4, HoldY + 15).T, Is.Not.EqualTo("tree"));
            Assert.That(w.At(HoldX + 12, HoldY + 6).T, Is.Not.EqualTo("tree"));
            Assert.That(WorldThings.Load(ChemistryTests.WorldDir()).All.Count, Is.EqualTo(thingsWere));
        }

        [Test]
        public void TheGateSwingsAsOneByCommandBothWays()
        {
            var w = Laid(out var row);
            var gate = Cells(row, w).Where(c => c.Terrain == Open).ToList();
            w.ApplyEffect(new EffectCommand(EffectKind.Swing, gate[1].X, gate[1].Y, null));
            Assert.That(gate.All(c => w.At(c.X, c.Y).T == Shut));
            Assert.That(gate.All(c => w.IsBlocking(w.At(c.X, c.Y))));
            w.ApplyEffect(new EffectCommand(EffectKind.Swing, gate[0].X, gate[0].Y, null));
            Assert.That(gate.All(c => w.At(c.X, c.Y).T == Open));
        }

        [Test]
        public void ASwingOnAnythingElseDoesNothing()
        {
            var w = Laid(out var row);
            var stone = Cells(row, w).First(c => c.Terrain == "stone");
            w.ApplyEffect(new EffectCommand(EffectKind.Swing, stone.X, stone.Y, null));
            Assert.That(w.At(stone.X, stone.Y).T, Is.EqualTo("stone"));
        }

        [Test]
        public void TwoPeersSwingAlike()
        {
            var a = Laid(out var row);
            var b = Laid(out _);
            var gate = Cells(row, a).First(c => c.Terrain == Open);
            foreach (var w in new[] { a, b })
            {
                w.Step();
                w.ApplyEffect(new EffectCommand(EffectKind.Swing, gate.X, gate.Y, null));
                w.Step();
            }
            Assert.That(a.CellHash(), Is.EqualTo(b.CellHash()));
        }

        [Test]
        public void TheGateBurnsAndTheStoneDoesnt()
        {
            var w = Laid(out var row);
            var gate = Cells(row, w).Where(c => c.Terrain == Open).ToList();
            w.ApplyEffect(new EffectCommand(EffectKind.Swing, gate[0].X, gate[0].Y, null));
            Assert.That(w.Rules.Terrain[Shut].Burns);
            Assert.That(w.Rules.Terrain[Open].Burns, "an open gate burns as well as a shut one");
            Assert.That(w.Rules.Terrain["stone"].Burns, Is.False);
            for (int i = 0; i < 40 && gate.Any(c => w.At(c.X, c.Y).T == Shut); i++)
            {
                foreach (var c in gate.Where(c => w.At(c.X, c.Y).T == Shut)) w.ApplyEffect(new EffectCommand(EffectKind.Burn, c.X, c.Y, null));
                for (int t = 0; t < 30; t++) w.Step();
            }
            Assert.That(gate.All(c => w.At(c.X, c.Y).T == "rubble"), string.Join(", ", gate.Select(c => w.At(c.X, c.Y).T)));
            Assert.That(gate.All(c => !w.IsBlocking(w.At(c.X, c.Y))), "burnt through, it's a way in");
        }

        [Test]
        public void StoneBreaksToRubble() => Assert.That(Rules.Terrain["stone"].BreaksTo, Is.EqualTo("rubble"));

        [Test]
        public void AWizardWalksUpTheRoadThroughTheGateIntoTheKeepAndItsRoom()
        {
            var w = Laid(out var row);
            var cells = Cells(row, w);
            var foot = cells.Where(c => c.Part == BuildingPart.Road).OrderByDescending(c => c.X).First();
            var walked = Walked(w, (foot.X, foot.Y), 80, 318, 229, 409);
            Assert.That(walked.Contains((row.X + 12, row.Y + 12)), "the keep's attuning room");
            Assert.That(walked.Contains((row.X + 12, row.Y + 19)), "the keep's hall");
            Assert.That(walked.Contains((row.X + 22, row.Y + 8)), "the foot of the north stair");
            Assert.That(walked.Contains((row.X + 22, row.Y + 26)), "the foot of the south stair");
            w.ApplyEffect(new EffectCommand(EffectKind.Swing, row.X + 28, row.Y + 15, null));
            var shut = Walked(w, (foot.X, foot.Y), 80, 318, 229, 409);
            Assert.That(shut.Contains((row.X + 12, row.Y + 19)), Is.False, "a shut gate keeps the court shut");
        }

        [Test]
        public void TheRoadRunsDownToTheFootOfTheSlope()
        {
            var w = Laid(out var row);
            var road = Cells(row, w).Where(c => c.Part == BuildingPart.Road).ToList();
            Assert.That(road.Max(c => c.X), Is.GreaterThanOrEqualTo(190));
            Assert.That(road.All(c => c.Terrain == "dirt"));
            var byX = road.Select(c => c.X).Distinct().OrderBy(x => x).ToList();
            Assert.That(byX.Zip(byX.Skip(1), (a, b) => b - a).All(d => d == 1), "no gap in it");
        }

        // ---------- a mod's own, within the budgets ----------

        const string Second = @"{ ""world"": { ""buildings"": { ""second#1"": {
            ""at"": [40, 40], ""style"": ""stronghold"", ""level"": true,
            ""joins"": { ""a"": [4, 4], ""b"": [32, 4], ""c"": [32, 32], ""d"": [4, 32], ""r"": [34, 17], ""s"": [50, 17] },
            ""walls"": { ""n"": { ""from"": ""a"", ""to"": ""b"" }, ""e"": { ""from"": ""b"", ""to"": ""c"", ""openings"": [{ ""kind"": ""timber-gate"", ""at"": 12 }] },
                         ""s"": { ""from"": ""c"", ""to"": ""d"" }, ""w"": { ""from"": ""d"", ""to"": ""a"" } },
            ""towers"": { ""t1"": { ""at"": ""a"", ""radius"": 4 }, ""t2"": { ""at"": ""b"", ""radius"": 4 }, ""t3"": { ""at"": ""c"", ""radius"": 4 }, ""t4"": { ""at"": ""d"", ""radius"": 4 } },
            ""roads"": { ""out"": { ""through"": [""r"", ""s""], ""width"": 2, ""terrain"": ""dirt"" } },
            ""parts"": [{ ""kind"": ""stair"", ""on"": ""n"", ""at"": 4, ""length"": 12 }] } } } }";

        static World OpenWorld()
        {
            var rules = Rules;
            var w = new World(rules, Size, Size, 3);
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++) w.Paint(x, y, "grass");
            w.KeepStart();
            w.OpenRoom(ChangeMode.Open, "host", rules, ModBudgets.Load(ChemistryTests.RulesDir()));
            return w;
        }

        [Test]
        public void AModBuildsASecondOneFromTheSameRows()
        {
            var w = OpenWorld();
            var said = w.ApplyRoom(RoomCommand.Apply("host", "second", Second));
            Assert.That(said.Done, said.Reason);
            var row = w.BuildingRowOf("second#1");
            Assert.That(row.Towers.Count, Is.EqualTo(4));
            foreach (var c in w.BuildingCellsOf(row)) Assert.That(w.At(c.X, c.Y).T, Is.EqualTo(c.Terrain));
            Assert.That(w.BuildingCellsOf(row).Count(c => c.Terrain == Open), Is.EqualTo(3));
        }

        [TestCase("\"radius\": 4", "\"radius\": 1", "radius is in half cells")]
        [TestCase("\"at\": \"a\", \"radius\": 4", "\"at\": \"zz\", \"radius\": 4", "tower t1")]
        [TestCase("[\"r\", \"s\"]", "[\"r\", \"zz\"]", "goes through join zz")]
        [TestCase("\"terrain\": \"dirt\"", "\"terrain\": \"stone\"", "isn't ground")]
        [TestCase("\"width\": 2", "\"width\": 99", "width is whole cells")]
        public void ABadTowerOrRoadIsSaid(string was, string now, string said)
        {
            string patch = Second.Replace(was, now);
            Assert.That(patch, Is.Not.EqualTo(Second));
            var e = Assert.Catch(() => Rules.WithMods(new[] { ModPatch.Parse(patch) }));
            Assert.That(e.Message, Does.Contain(said));
        }
    }
}
