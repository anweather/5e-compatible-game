// Buildings from parts (Buildings.cs, World.Buildings.cs; docs/design/builders.md
// section 4): the rasteriser, the villages' cells as rows, and a mod's "world"
// section through the room. The stucco house is a test fixture only (J-055): its
// walls are the rules' stucco-wall, but no such row is in the world.
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    public class BuildingTests
    {
        internal const string TileTerrain = "{ \"name\": \"Clay tile\", \"tags\": [\"roof\"], \"look\": { \"family\": \"ground\", \"color\": [0.66, 0.3, 0.2] } }";
        internal const string SouthwestStyle = "{ \"fill\": \"hollow\", \"floor\": { \"kind\": \"slab\", \"height\": 0.15 }, \"wall\": { \"material\": \"stucco-wall\", \"height\": 2.9, \"thickness\": 0.3, \"finish\": { \"pattern\": \"smooth\", \"color\": \"#d6b48a\" } }, \"roof\": { \"shape\": \"hip\", \"pitch\": [4, 12], \"overhang\": 0.45, \"cover\": \"clay-tile\" } }";

        // builders.md's house, at a cell of a test yard rather than the valley's.
        internal static string StuccoRow(int x, int y) =>
            "{ \"id\": \"lakeside#1\", \"at\": [" + x + ", " + y + "], \"style\": \"southwest\", \"seed\": 41, \"storeys\": 1, \"fill\": \"hollow\"," +
            " \"floor\": { \"kind\": \"slab\", \"height\": 0.15 }," +
            " \"wall\": { \"material\": \"stucco-wall\", \"height\": 2.9, \"thickness\": 0.3, \"finish\": { \"pattern\": \"smooth\", \"color\": \"#d6b48a\" } }," +
            " \"joins\": { \"a\": [8, 0], \"b\": [28, 0], \"c\": [28, 16], \"d\": [18, 16], \"e\": [18, 14], \"f\": [14, 14], \"g\": [14, 16], \"h\": [8, 16], \"i\": [0, 16], \"j\": [0, 8], \"k\": [8, 8] }," +
            " \"walls\": {" +
            " \"back\": { \"from\": \"a\", \"to\": \"b\", \"openings\": [{ \"kind\": \"wide-window\", \"at\": 4 }, { \"kind\": \"window\", \"at\": 14 }] }," +
            " \"east\": { \"from\": \"b\", \"to\": \"c\", \"openings\": [{ \"kind\": \"window\", \"at\": 5 }, { \"kind\": \"window\", \"at\": 11 }] }," +
            " \"front-e\": { \"from\": \"c\", \"to\": \"d\", \"openings\": [{ \"kind\": \"wide-window\", \"at\": 3 }] }," +
            " \"recess-e\": { \"from\": \"d\", \"to\": \"e\" }, \"recess-w\": { \"from\": \"f\", \"to\": \"g\" }," +
            " \"entry\": { \"from\": \"e\", \"to\": \"f\", \"openings\": [{ \"kind\": \"door\", \"at\": 1, \"finish\": { \"color\": \"#3a6f9a\" } }] }," +
            " \"arch\": { \"from\": \"g\", \"to\": \"d\", \"openings\": [{ \"kind\": \"arch\", \"at\": 0, \"width\": 3, \"shape\": \"round\" }] }," +
            " \"front-w\": { \"from\": \"g\", \"to\": \"h\", \"openings\": [{ \"kind\": \"window\", \"at\": 2 }] }," +
            " \"party\": { \"from\": \"h\", \"to\": \"k\", \"openings\": [{ \"kind\": \"door\", \"at\": 3 }] }," +
            " \"west\": { \"from\": \"k\", \"to\": \"a\", \"openings\": [{ \"kind\": \"window\", \"at\": 3 }] }," +
            " \"garage\": { \"from\": \"h\", \"to\": \"i\", \"openings\": [{ \"kind\": \"double-garage\", \"at\": 1 }] }," +
            " \"g-west\": { \"from\": \"i\", \"to\": \"j\" }, \"g-back\": { \"from\": \"j\", \"to\": \"k\" } }," +
            " \"roofs\": { \"main\": { \"over\": [\"a\", \"b\", \"c\", \"i\", \"j\", \"k\"], \"shape\": \"hip\", \"pitch\": [4, 12], \"overhang\": 0.45, \"cover\": \"clay-tile\" } }," +
            " \"parts\": [{ \"kind\": \"chimney\", \"on\": \"east\", \"at\": 8 }] }";

        internal static string StuccoMod(int x, int y) =>
            "{ \"title\": \"A house\", \"patch\": { \"terrain\": { \"clay-tile\": " + TileTerrain + " }," +
            " \"buildings\": { \"styles\": { \"southwest\": " + SouthwestStyle + " } }," +
            " \"world\": { \"buildings\": { \"lakeside#1\": " + StuccoRow(x, y) + " } } } }";

        static ModBudgets budgets;
        static ModBudgets Budgets => budgets ??= ModBudgets.Load(ChemistryTests.RulesDir());
        static Rules R => ChemistryTests.R;

        static World Yard(string fill = "grass")
        {
            var w = new World(R, 128, 128, 7, fill);
            w.KeepStart();
            w.OpenRoom(ChangeMode.Open, "Ana", R, Budgets);
            return w;
        }

        // Test yards are small; the world files' things are in the valley's far chunks, so the budgets' things stay clear of it except where a test says.
        static World Up(World w, string json) { w.ApplyRoom(RoomCommand.Apply("Ana", "house", json)); return w; }

        static Rules Stucco => R.WithMods(new[] { ModPatch.Parse(StuccoMod(20, 20)) });

        static BuildingRow Row() => Stucco.ModBuildings.Single();

        static Dictionary<(int, int), BuildingCell> Cells(BuildingRow row, Rules rules) =>
            BuildingRaster.Cells(row, rules.Buildings).ToDictionary(c => (c.X, c.Y));

        // ---------- the rasteriser ----------

        [Test]
        public void TheRulesTablesRead()
        {
            Assert.That(R.Buildings.Openings["door"].Becomes, Is.EqualTo("door"));
            Assert.That(R.Terrain.ContainsKey("door"), "a door is a terrain that burns or breaks to a gap");
            Assert.That(R.Buildings.Row("styles", "plain"), Is.Not.Null);
            Assert.That(R.WithMods(System.Array.Empty<ModPatch>()).Fingerprint, Is.EqualTo(R.Fingerprint), "the base rules check, with their tables");
        }

        [Test]
        public void AGoldenPinsOneBuildingRow()
        {
            var cells = BuildingRaster.Cells(Row(), Stucco.Buildings);
            Assert.That(BuildingRaster.Digest(cells), Is.EqualTo(BuildingGolden), "the stucco house's cells changed: every peer must lay the same house");
            Assert.That(cells.Count, Is.EqualTo(94));
        }

        const uint BuildingGolden = 4222984142;

        [Test]
        public void TheStuccoHouseIsWallsDoorsPanesAndAFloor()
        {
            var cells = Cells(Row(), Stucco);
            // Half cells from at are cells at half that: the house's north-west corner cell.
            Assert.That(cells[(24, 20)].Terrain, Is.EqualTo("stucco-wall"), "the main house's back wall starts at join a");
            Assert.That(cells[(27, 25)].Terrain, Is.EqualTo("rock"), "a hollow house has a floor inside (the slab's terrain)");
            Assert.That(cells.Values.Count(c => c.Terrain == "door"), Is.EqualTo(1 + 1 + 3), "the front door, the party door and three cells of garage door");
            Assert.That(cells.Values.Count(c => c.Terrain == "pane"), Is.EqualTo(2 + 1 + 2 + 2 + 1 + 1), "each window's cells, as the design's table counts them");
            Assert.That(cells.ContainsKey((28, 27)), Is.False, "the recessed entry's pocket is outside: the arch lets the flood in");
            Assert.That(cells.ContainsKey((28, 28)), Is.False, "and the arch itself leaves no cell");
            Assert.That(cells.ContainsKey((19, 20)), Is.False);
        }

        [Test]
        public void SolidFillIsOneBlock()
        {
            var row = JObject.Parse(StuccoRow(20, 20));
            row["fill"] = "solid";
            var rules = R.WithMods(new[] { ModPatch.Parse("{ \"terrain\": { \"clay-tile\": " + TileTerrain + " }, \"buildings\": { \"styles\": { \"southwest\": " + SouthwestStyle + " } }, \"world\": { \"buildings\": { \"lakeside#1\": " + row.ToString() + " } } }") });
            var cells = Cells(rules.ModBuildings.Single(), rules);
            Assert.That(cells[(27, 25)].Terrain, Is.EqualTo("stucco-wall"));
            Assert.That(cells.Values.Any(c => c.Terrain == "rock"), Is.False);
        }

        [Test]
        public void AWallThroughCellCentresTakesThoseAndADiagonalIsGapFree()
        {
            string Mod(string joins, string walls) => "{ \"world\": { \"buildings\": { \"t#1\": { \"at\": [10, 10], \"style\": \"plain\", \"joins\": " + joins + ", \"walls\": " + walls + " } } } }";
            var straight = R.WithMods(new[] { ModPatch.Parse(Mod("{ \"a\": [1, 1], \"b\": [9, 1] }", "{ \"w\": { \"from\": \"a\", \"to\": \"b\" } }")) });
            Assert.That(BuildingRaster.Cells(straight.ModBuildings[0], straight.Buildings).Select(c => (c.X, c.Y)),
                Is.EqualTo(new[] { (10, 10), (11, 10), (12, 10), (13, 10), (14, 10) }), "centres at half cell 1: cells 0 to 4 along row 0");
            var diamond = R.WithMods(new[] { ModPatch.Parse(Mod(
                "{ \"n\": [8, 0], \"e\": [16, 8], \"s\": [8, 16], \"w\": [0, 8] }",
                "{ \"ne\": { \"from\": \"n\", \"to\": \"e\" }, \"se\": { \"from\": \"e\", \"to\": \"s\" }, \"sw\": { \"from\": \"s\", \"to\": \"w\" }, \"nw\": { \"from\": \"w\", \"to\": \"n\" } }")) });
            var cells = Cells(diamond.ModBuildings[0], diamond);
            Assert.That(cells[(14, 14)].Part, Is.EqualTo(BuildingPart.Fill), "a 45° outline holds: the flood doesn't get in");
            Assert.That(cells[(14, 14)].Terrain, Is.EqualTo("rock"));
        }

        [Test]
        public void AStylesRecipeLaysWallsFromTheSeed()
        {
            string Mod(int seed) => "{ \"buildings\": { \"styles\": { \"hut\": { \"wall\": { \"material\": \"logwall\" }, \"recipe\": { \"width\": [3, 5], \"depth\": [4, 6], \"door\": \"door\", \"window\": \"window\" } } } }, \"world\": { \"buildings\": { \"t#1\": { \"at\": [10, 10], \"style\": \"hut\", \"seed\": " + seed + " } } } }";
            var a = R.WithMods(new[] { ModPatch.Parse(Mod(3)) });
            var b = R.WithMods(new[] { ModPatch.Parse(Mod(3)) });
            Assert.That(BuildingRaster.Digest(BuildingRaster.Cells(a.ModBuildings[0], a.Buildings)), Is.EqualTo(BuildingRaster.Digest(BuildingRaster.Cells(b.ModBuildings[0], b.Buildings))));
            Assert.That(BuildingRaster.Cells(a.ModBuildings[0], a.Buildings).Any(c => c.Terrain == "door"));
        }

        [Test]
        public void AGoldenPinsAWingAdded()
        {
            var row = JObject.Parse(StuccoRow(20, 20));
            Wing.Add(row, Side.North, 4, 3, Stucco.Buildings);
            var rules = R.WithMods(new[] { ModPatch.Parse(StuccoMod(20, 20).Replace(StuccoRow(20, 20), row.ToString())) });
            var cells = Cells(rules.ModBuildings.Single(), rules);
            Assert.That(BuildingRaster.Digest(BuildingRaster.Cells(rules.ModBuildings.Single(), rules.Buildings)), Is.EqualTo(WingGolden));
            Assert.That(cells.Keys.Min(c => c.Item2), Is.EqualTo(17), "three cells out from the back wall, on row 20");
            Assert.That(cells[(28, 18)].Terrain, Is.EqualTo("rock"), "the wing has its own floor, and it's inside");
            Assert.That(row["walls"]["back"]["openings"].Any(o => (string)o["kind"] == "door"), "a door through the wall it meets");
            Assert.That(cells.Values.Count(c => c.Terrain == "pane"), Is.LessThanOrEqualTo(10), "the windows under it go");
            Assert.That(((JArray)row["roofs"]["main"]["over"]).Count, Is.EqualTo(10), "the roof's outline grows round it");
        }

        const uint WingGolden = 1574690118;

        // ---------- villages as rows ----------

        static IEnumerable<VillagePlan> Plans()
        {
            for (int seed = 0; seed < 40; seed++) yield return VillageLayout.Lay(new VillageSpec { Name = "Test", X = 200, Y = 200, Seed = seed, Houses = 12 }, (x, y) => true);
            foreach (var spec in VillageLayout.Load(ChemistryTests.WorldDir())) yield return VillageLayout.Lay(spec, (x, y) => true);
        }

        [Test]
        public void AVillagesRowsLayExactlyItsCells()
        {
            foreach (var plan in Plans())
            {
                var expected = new Dictionary<(int, int), string>();
                foreach (var b in plan.Buildings)
                {
                    foreach (var c in b.Footprint.Cells()) expected[c] = b.WallMaterial;
                    foreach (var win in b.Windows) expected[(win.X, win.Y)] = plan.Spec.Materials.Window;
                }
                var made = new Dictionary<(int, int), string>();
                foreach (var row in VillageRows.Of(plan))
                    foreach (var c in BuildingRaster.Cells(BuildingRow.Read((string)row["id"], row, R.Buildings), R.Buildings))
                        made.Add((c.X, c.Y), c.Terrain);
                Assert.That(made, Is.EquivalentTo(expected), $"{plan.Spec.Name} {plan.Spec.Seed}");
            }
        }

        [Test]
        public void TheCellsSayWhichPlotsWereRefused()
        {
            for (int seed = 0; seed < 40; seed++)
            {
                var spec = new VillageSpec { Name = "Test", X = 200, Y = 200, Seed = seed, Houses = 12 };
                // Land that refuses a stripe of plots, as a lake would.
                var baked = VillageLayout.Lay(spec, (x, y) => (x + y / 3) % 37 > 5);
                var cells = new Dictionary<(int, int), string>();
                foreach (var b in baked.Buildings)
                {
                    foreach (var c in b.Footprint.Cells()) cells[c] = b.WallMaterial;
                    foreach (var win in b.Windows) cells[(win.X, win.Y)] = spec.Materials.Window;
                }
                var read = WorldBuildings.LaidPlan(spec, (x, y) => cells.TryGetValue((x, y), out var t) ? t : "grass");
                Assert.That(read.Digest(), Is.EqualTo(baked.Digest()), $"seed {seed}");
            }
        }

        [Test]
        public void TheCabinRowsAreWhereValleyShapePutsThem()
        {
            var rows = WorldBuildings.Load(ChemistryTests.WorldDir()).FileRows.Where(r => r["built"] != null).ToList();
            Assert.That(rows.Select(r => (string)r["id"]), Is.EqualTo(new[] { "cabin#1", "cabin#2", "cabin#3", "cabin#4" }));
            foreach (var row in rows)
            {
                var read = BuildingRow.Read((string)row["id"], row, R.Buildings);
                var cells = BuildingRaster.Cells(read, R.Buildings);
                double mx = (double)row["site"]["metres"][0], mz = (double)row["site"]["metres"][1], dx = (double)row["site"]["door"][0], dz = (double)row["site"]["door"][1];
                // ValleyShape.InCabin and OnCabinWindow, on each cell centre round the site.
                var expected = new Dictionary<(int, int), string>();
                double fx = -dx, fz = -dz, rx = fz, rz = -fx;
                double wx = rx * -1.6 + fx * -2.45, wz = rz * -1.6 + fz * -2.45;
                int ax = (int)(mx / 1.5) + 279, ay = 330 - (int)(mz / 1.5);
                for (int y = ay - 8; y <= ay + 8; y++)
                    for (int x = ax - 8; x <= ax + 8; x++)
                    {
                        double px = (x - 279) * 1.5, pz = (330 - y) * 1.5, vx = px - mx, vz = pz - mz;
                        if (System.Math.Abs(vx - wx) <= 0.75 && System.Math.Abs(vz - wz) <= 0.75) expected[(x, y)] = "pane";
                        else if (System.Math.Abs(vx * rx + vz * rz) <= 3.3 && System.Math.Abs(vx * fx + vz * fz) <= 2.55) expected[(x, y)] = "logwall";
                    }
                Assert.That(cells.ToDictionary(c => (c.X, c.Y), c => c.Terrain), Is.EquivalentTo(expected), (string)row["id"]);
            }
        }

        // ---------- through the room ----------

        [Test]
        public void AModBuildsAtTheSwitchTickOnEveryPeer()
        {
            var a = Yard();
            var b = Yard();
            string before = a.CellHash();
            string json = StuccoMod(20, 20);
            var verdict = a.ApplyRoom(RoomCommand.Apply("Ana", "house", json));
            b.ApplyRoom(RoomCommand.Apply("Ana", "house", json));
            Assert.That(verdict.Done, Is.True, verdict.Line);
            Assert.That(a.At(24, 20).T, Is.EqualTo("stucco-wall"));
            Assert.That(a.CellHash(), Is.Not.EqualTo(before), "it's in the cell hash");
            Assert.That(b.CellHash(), Is.EqualTo(a.CellHash()));
            for (int i = 0; i < 20; i++)
            {
                a.Step();
                b.Step();
            }
            Assert.That(b.CellHash(), Is.EqualTo(a.CellHash()));
            Assert.That(a.At(24, 20).T, Is.EqualTo("stucco-wall"), "still standing");
        }

        [Test]
        public void AHouseIsPinnedInTheCellHash()
        {
            var w = Up(Yard(), StuccoMod(20, 20));
            Assert.That(w.CellHash(), Is.EqualTo(HouseHash), "a house laid in a yard");
        }

        const string HouseHash = "71159ef7";

        [Test]
        public void ARevertLaysTheBaseAgainAndHashesAsBefore()
        {
            var w = Yard();
            string before = w.CellHash();
            Up(w, StuccoMod(20, 20));
            Assert.That(w.ApplyRoom(RoomCommand.Revert("Ana", "house")).Done, Is.True);
            Assert.That(w.At(24, 20).T, Is.EqualTo("grass"));
            Assert.That(w.CellHash(), Is.EqualTo(before));
        }

        [Test]
        public void ChangedCellsAreLaidFresh()
        {
            var w = Yard();
            var c = w.At(24, 20);
            c.Fire = 2;
            c.Fuel = 5;
            c.Cover = "oil";
            Up(w, StuccoMod(20, 20));
            Assert.That(c.Fire, Is.EqualTo(0));
            Assert.That(c.Cover, Is.Null);
        }

        [Test]
        public void ALateJoinerGetsTheSameCellsFromTheModList()
        {
            var host = Yard();
            Up(host, StuccoMod(20, 20));
            var joiner = new World(R, 128, 128, 7, "grass");
            joiner.KeepStart();
            joiner.OpenRoom(host.Room.Mode, host.Room.Host, R, Budgets, host.Room.Mods);
            Assert.That(joiner.CellHash(), Is.EqualTo(host.CellHash()));
            Assert.That(joiner.Rules.Fingerprint, Is.EqualTo(host.Rules.Fingerprint));
            Assert.That(joiner.BuildingRowOf("lakeside#1"), Is.Not.Null);
        }

        sealed class Meadow : IChunkSource
        {
            public string Version => "buildings-meadow";
            public void Make(int x, int y, Cell cell) => cell.T = "grass";
        }

        [Test]
        public void TheSaveKeepsItAndARevertAfterLoadPutsItBack()
        {
            var source = new Meadow();
            var w = new World(R, 128, 128, 7);
            w.MakeFrom(source);
            w.KeepStart();
            w.OpenRoom(ChangeMode.Open, "Ana", R, Budgets);
            string before = w.CellHash();
            Up(w, StuccoMod(20, 20));
            for (int i = 0; i < 5; i++) w.Step();
            var loaded = World.Load(w.Save(source), R, source, Budgets);
            Assert.That(loaded.CellHash(), Is.EqualTo(w.CellHash()));
            Assert.That(loaded.CastHash(), Is.EqualTo(w.CastHash()));
            Assert.That(loaded.At(24, 20).T, Is.EqualTo("stucco-wall"));
            Assert.That(loaded.BuildingRowOf("lakeside#1"), Is.Not.Null);
            for (int i = 0; i < 5; i++)
            {
                w.Step();
                loaded.Step();
            }
            Assert.That(loaded.CellHash(), Is.EqualTo(w.CellHash()));
            Assert.That(loaded.ApplyRoom(RoomCommand.Revert("Ana", "house")).Done, Is.True);
            Assert.That(loaded.CellHash(), Is.EqualTo(before));
        }

        [Test]
        public void TheDragonPutsTheHouseBack()
        {
            var w = Up(Yard(), StuccoMod(20, 20));
            string laid = w.CellHash();
            Assert.That(w.ChangedCount, Is.EqualTo(0), "a mod's building isn't damage");
            w.At(24, 20).T = "rubble";
            for (int i = 0; i < 3000 && w.CellHash() != laid; i++)
            {
                if (i == 0) w.ApplyDragon(new DragonCommand());
                w.Step();
            }
            Assert.That(w.At(24, 20).T, Is.EqualTo("stucco-wall"));
        }

        [Test]
        public void TheDoorBurnsOrBreaksToAGap()
        {
            var door = R.Terrain["door"];
            Assert.That(door.Blocking);
            Assert.That(door.BurnsTo, Is.EqualTo("rubble"));
            Assert.That(door.BreaksTo, Is.EqualTo("rubble"));
        }

        [Test]
        public void TheSiteCheckRefusesWaterThingsBuildingsAndWhatIsntGround()
        {
            var wet = Yard();
            wet.At(25, 21).Water = 8;
            var verdict = wet.ApplyRoom(RoomCommand.Apply("Ana", "house", StuccoMod(20, 20)));
            Assert.That(verdict.Done, Is.False);
            Assert.That(verdict.Reason, Does.Contain("water"));
            Assert.That(wet.Room.Mods, Is.Empty);
            Assert.That(wet.At(24, 20).T, Is.EqualTo("grass"), "a refusal lays nothing");

            var tree = Yard();
            tree.At(25, 21).T = "tree";
            Assert.That(tree.ApplyRoom(RoomCommand.Apply("Ana", "house", StuccoMod(20, 20))).Reason, Does.Contain("not open ground"));

            var built = Up(Yard(), StuccoMod(20, 20));
            // Its own tile: a mod may not name another mod's temporary material (World.Lapse.cs).
            string second = StuccoMod(30, 22).Replace("lakeside#1", "lakeside#2").Replace("clay-tile", "slate-tile");
            var overlap = built.ApplyRoom(RoomCommand.Apply("Ana", "second", second));
            Assert.That(overlap.Done, Is.False);
            Assert.That(overlap.Reason, Does.Contain("lakeside#1 stands there"));

            var valley = Budgets.Things.All[0];
            var big = new World(R, 576, 576, 7, "grass");
            big.KeepStart();
            big.OpenRoom(ChangeMode.Open, "Ana", R, Budgets);
            int ax = valley.CellX - 5, ay = valley.CellY - 1;
            Assert.That(big.ApplyRoom(RoomCommand.Apply("Ana", "on-thing", "{ \"world\": { \"buildings\": { \"t#1\": { \"at\": [" + ax + ", " + ay + "], \"style\": \"plain\", \"joins\": { \"a\": [0, 0], \"b\": [20, 0], \"c\": [20, 8], \"d\": [0, 8] }, \"walls\": { \"n\": { \"from\": \"a\", \"to\": \"b\" }, \"e\": { \"from\": \"b\", \"to\": \"c\" }, \"s\": { \"from\": \"c\", \"to\": \"d\" }, \"w\": { \"from\": \"d\", \"to\": \"a\" } } } } } }")).Reason,
                Does.Contain("stands there"));
        }

        [Test]
        public void ARaiderOnANewWallIsPushedOutAndLogged()
        {
            var a = Yard();
            var b = Yard();
            foreach (var w in new[] { a, b })
            {
                w.SpawnRaider("goblin", 24, 20, 5);
                w.SpawnRaider("goblin", 27, 25, 5);
                Up(w, StuccoMod(20, 20));
            }
            Assert.That(a.Raiders.All(r => !a.IsBlocking(a.At(r.X, r.Y))));
            Assert.That(a.Raiders.All(r => a.BuildingCellsOf(a.BuildingRowOf("lakeside#1")).All(c => c.X != r.X || c.Y != r.Y)), "out of the walls and off its floor");
            Assert.That(a.Log.Count(l => l.Text.Contains("pushed out of the new walls")), Is.EqualTo(2));
            Assert.That(a.Raiders.Select(r => (r.X, r.Y)), Is.EqualTo(b.Raiders.Select(r => (r.X, r.Y))));
        }

        [Test]
        public void TheViewHearsWhatChangedAndCanPushAPlayer()
        {
            var w = Yard();
            BuildingsChange heard = null;
            var fresh = new List<ChunkFresh>();
            w.BuildingsChanged += c => heard = c;
            w.ChunkFreshened += fresh.Add;
            Up(w, StuccoMod(20, 20));
            Assert.That(heard.Ids, Is.EqualTo(new[] { "lakeside#1" }));
            Assert.That(heard.Covers(24, 20), Is.True);
            Assert.That(heard.Covers(5, 5), Is.False);
            Assert.That(fresh, Is.Not.Empty, "the chunks laid on are redrawn as the dragon's are");
            var to = w.NearestFree(24, 20);
            Assert.That(w.IsBlocking(w.At(to.Value.x, to.Value.y)), Is.False);
            Assert.That(w.BuildingCellsOf(w.BuildingRowOf("lakeside#1")).Any(c => c.X == to.Value.x && c.Y == to.Value.y), Is.False);
        }

        [Test]
        public void ALookOnlyEditMovesNeitherHashNorFingerprint()
        {
            var plain = R.WithMods(new[] { ModPatch.Parse(StuccoMod(20, 20)) });
            var row = JObject.Parse(StuccoRow(20, 20));
            row["wall"]["finish"]["color"] = "#aa0000";
            row["wall"]["height"] = 5.5;
            row["roofs"]["main"]["shape"] = "flat";
            row["storeys"] = 2;
            row["parts"] = new JArray();
            var painted = R.WithMods(new[] { ModPatch.Parse(StuccoMod(20, 20).Replace(StuccoRow(20, 20), row.ToString())) });
            Assert.That(painted.Fingerprint, Is.EqualTo(plain.Fingerprint));
            var w = Up(Yard(), StuccoMod(20, 20));
            string laid = w.CellHash();
            w.ApplyRoom(RoomCommand.Revert("Ana", "house"));
            w.ApplyRoom(RoomCommand.Apply("Ana", "house", StuccoMod(20, 20).Replace(StuccoRow(20, 20), row.ToString())));
            Assert.That(w.CellHash(), Is.EqualTo(laid));
        }

        [Test]
        public void TheChecksSayWhatIsWrongWithARow()
        {
            string Bad(string row) => string.Join(" ", ModValidator.Validate(R, Budgets, "{ \"world\": { \"buildings\": { \"t#1\": " + row + " } } }").Select(p => p.Message));
            const string walls = "\"joins\": { \"a\": [0, 0], \"b\": [8, 0] }, \"walls\": { \"n\": { \"from\": \"a\", \"to\": \"b\"";
            Assert.That(Bad("{ \"style\": \"plain\", " + walls + " } } }"), Does.Contain("at"));
            Assert.That(Bad("{ \"at\": [1, 1], \"style\": \"nope\", " + walls + " } } }"), Does.Contain("unknown style"));
            Assert.That(Bad("{ \"at\": [1, 1], \"style\": \"plain\", \"wall\": { \"material\": \"grass\" }, " + walls + " } } }"), Does.Contain("doesn't block"));
            Assert.That(Bad("{ \"at\": [1, 1], \"style\": \"plain\", \"fill\": \"foam\", " + walls + " } } }"), Does.Contain("solid or hollow"));
            Assert.That(Bad("{ \"at\": [1, 1], \"style\": \"plain\", " + walls + ", \"openings\": [{ \"kind\": \"gate\" }] } } }"), Does.Contain("unknown opening"));
            Assert.That(Bad("{ \"at\": [1, 1], \"style\": \"plain\", " + walls + ", \"openings\": [{ \"kind\": \"door\", \"at\": 0 }] } } }"), Does.Contain("clear of a join"));
            Assert.That(Bad("{ \"at\": [1, 1], \"style\": \"plain\", \"joins\": { \"a\": [0, 0], \"b\": [8, 3] }, \"walls\": { \"n\": { \"from\": \"a\", \"to\": \"b\" } } }"), Does.Contain("45°"));
            Assert.That(Bad("{ \"at\": [1, 1], \"style\": \"plain\", \"joins\": { \"a\": [0, 0], \"b\": [80, 0] }, \"walls\": { \"n\": { \"from\": \"a\", \"to\": \"b\" } } }"), Does.Contain("at most 24"));
            Assert.That(Bad("{ \"at\": [1, 1], \"style\": \"plain\", \"storeys\": 9, " + walls + " } } }"), Does.Contain("at most 3"));
            Assert.That(Bad("{ \"at\": [1, 1], \"style\": \"plain\", " + walls + " } }, \"roofs\": { \"r\": { \"over\": [\"a\", \"b\", \"z\"], \"cover\": \"grass\" } } }"), Does.Contain("join z").And.Contain("isn't a roof"));
            var five = string.Join(", ", Enumerable.Range(1, 5).Select(i => "\"t#" + i + "\": { \"at\": [" + (i * 10) + ", 1], \"style\": \"plain\", \"joins\": { \"a\": [0, 0], \"b\": [8, 0] }, \"walls\": { \"n\": { \"from\": \"a\", \"to\": \"b\" } } }"));
            Assert.That(string.Join(" ", ModValidator.Validate(R, Budgets, "{ \"world\": { \"buildings\": { " + five + " } } }").Select(p => p.Message)), Does.Contain("at most 4"));
        }

        [Test]
        public void ALockedRoomBuildsNothing()
        {
            var w = new World(R, 64, 64, 7, "grass");
            w.OpenRoom(ChangeMode.Locked, "Ana", R, Budgets);
            string before = w.CellHash();
            Assert.That(w.ApplyRoom(RoomCommand.Apply("Ana", "house", StuccoMod(20, 20))).Done, Is.False);
            Assert.That(w.CellHash(), Is.EqualTo(before));
        }

        [Test]
        public void AWorldWithoutSuchModsHashesAsItDid()
        {
            var a = new World(R, 64, 64, 7, "grass");
            string plain = a.CellHash();
            a.OpenRoom(ChangeMode.Open, "Ana", R, Budgets);
            a.ApplyRoom(RoomCommand.Apply("Ana", "big-fireball", File.ReadAllText(Path.Combine(ChemistryTests.RulesDir(), "mods", "big-fireball.json"))));
            Assert.That(a.CellHash(), Is.EqualTo(plain));
        }
    }
}
