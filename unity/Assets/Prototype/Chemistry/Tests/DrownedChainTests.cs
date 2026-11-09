// The Drowned Chain, the second map (docs/design/maps-as-data.md, phase 8): a folder in world/maps/ that MapCheck passes,
// a world built from it as ChemGrid.MapWorld builds one, with no ground/ and none of the valley's optional files, that runs
// the same on every build, and an identity that turns away a peer with only the valley.
using System;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    [Category("Simulation")]
    public class DrownedChainTests
    {
        const string Id = "drowned-chain";
        const int Ticks = 120;

        // The folder's hash (MapIdentity.Hash): moves with any change to its files, on purpose; set it again when they change.
        const string FolderHash = "90740b0414af4dea";
        // The world's cells as built, and after the casts below and Ticks ticks.
        const string BuiltHash = "226107d1";
        const string RunHash = "a2df7c5d";

        static Rules R => ChemistryTests.R;
        static string Dir => Path.Combine(ChemistryTests.Folders.Maps, Id);

        // The characters cells.txt uses, as ChemGrid.Legend has them.
        static void PaintCell(World w, int x, int y, char ch)
        {
            switch (ch)
            {
                case 'w':
                    w.Paint(x, y, "pit");
                    w.AddWater(x, y, 4);
                    break;
                case '.':
                    w.Paint(x, y, "dirt");
                    break;
                case ',':
                case '@':
                    w.Paint(x, y, "grass");
                    break;
                default:
                    throw new FormatException($"cells.txt has '{ch}' at ({x}, {y}), which this map doesn't use");
            }
        }

        // The world from the folder as ChemGrid.MapWorld makes it: the cells, the land, then the optional files laid over
        // them (puzzles, stamps, rows, golf, the Hold), each simply absent here; its reach the chunks round the spawn.
        static World Build()
        {
            var map = MapFile.Load(Dir);
            var rows = MapFile.ReadCells(Dir).Replace("\r", "").Trim('\n').Split('\n');
            var w = new World(R, map.W, map.H, 7);
            w.SetLandRange(map.LandRange);
            for (int y = 0; y < map.H; y++)
                for (int x = 0; x < map.W; x++) PaintCell(w, x, y, rows[y][x]);
            w.SetLand(LandFile.Read(File.ReadAllText(Path.Combine(Dir, MapFile.LandName)), map.W, map.H));
            WorldPuzzles.Load(Dir).LayInto(w);
            Assert.That(MapStamps.Load(Dir).LayInto(w), Is.Empty);
            WorldBuildings.Load(Dir).LayInto(w);
            if (GolfCommand.Load(Dir) is { } hole) w.ApplyGolf(hole);
            if (WorldHold.Load(Dir) is { } hold) w.SetHold(hold);
            w.KeepStart();
            w.SetAllActive(false);
            int cx = map.Spawn.x / Chunks.Size, cy = map.Spawn.y / Chunks.Size;
            for (int j = cy - 1; j <= cy + 1; j++)
                for (int i = cx - 1; i <= cx + 1; i++) w.SetActive(i, j, true);
            return w;
        }

        // Fire on the start's grass, water poured on it, a trench dug from the beach into the sea, then the ticks.
        static string Run()
        {
            var w = Build();
            var casts = new System.Collections.Generic.Dictionary<int, CastCommand>
            {
                [5] = new CastCommand("fire-bolt", 270, 345, 0),
                [30] = new CastCommand("create-water", 272, 346, 0),
                [45] = new CastCommand("earthen-trench", 262, 357, 2),
            };
            var rules = RuleSet.Plain(R);
            for (int t = 1; t <= Ticks; t++)
                rules = (casts.TryGetValue(t, out var c) ? new TickBatch(t, new[] { c }) : new TickBatch(t)).RunOn(w, rules, out _);
            return w.CellHash();
        }

        [Test]
        public void TheFolderPassesMapCheck() => Assert.That(MapCheck.Errors(Dir), Is.Empty);

        [Test]
        public void ItsLandIsWhatItExpects()
        {
            var map = MapFile.Load(Dir);
            Assert.That(map.Id, Is.EqualTo(Id));
            Assert.That(map.Name, Is.EqualTo("The Drowned Chain"));
            Assert.That((map.W, map.H), Is.EqualTo((576, 576)));
            Assert.That(map.LandShare, Is.EqualTo(31), "about 31% of it above the sea");
            Assert.That(map.Highest * MapFile.LandUnitMetres, Is.EqualTo(154).Within(0.5), "a 154 m peak");
            var land = LandFile.Read(File.ReadAllText(Path.Combine(Dir, MapFile.LandName)), map.W, map.H);
            double sea = 100.0 * land.Count(u => u < map.SeaLevel) / land.Length;
            Assert.That(sea, Is.EqualTo(69).Within(1), "about 69% of it sea");
        }

        [Test]
        public void ItsCellsAreItsLand()
        {
            // Sea where the land is under the sea level and nowhere else: what MapCheck can't see, since it reads the two apart.
            var map = MapFile.Load(Dir);
            var rows = MapFile.ReadCells(Dir).Split('\n');
            var land = LandFile.Read(File.ReadAllText(Path.Combine(Dir, MapFile.LandName)), map.W, map.H);
            int off = Enumerable.Range(0, land.Length).Count(i => (rows[i / map.W][i % map.W] == 'w') != (land[i] < map.SeaLevel));
            Assert.That(off, Is.Zero);
        }

        [Test]
        public void ItHasNoGroundAndNoneOfTheOptionalFiles()
        {
            Assert.That(GroundPack.In(Dir), Is.False, "no ground/");
            foreach (string file in new[] { "golf.json", CourseLayout.FileName, WorldPuzzles.FileName, WorldHold.FileName, MapStamps.FileName, WorldBuildings.FileName })
                Assert.That(File.Exists(Path.Combine(Dir, file)), Is.False, file);
            Assert.That(GolfCommand.Load(Dir), Is.Null);
            Assert.That(WorldHold.Load(Dir), Is.Null);
            Assert.That(WorldPuzzles.Load(Dir).All, Is.Empty);
            Assert.That(WorldBuildings.Load(Dir).FileRows, Is.Empty);
            Assert.That(WorldBuildings.Load(Dir).Villages.Select(v => v.Name), Is.EqualTo(new[] { "Saltwick" }));
            Assert.That(WorldThings.Load(Dir).All.Count, Is.EqualTo(8));
        }

        [Test]
        public void TheWorldBuildsFromTheFolder()
        {
            var map = MapFile.Load(Dir);
            var w = Build();
            Assert.That((w.W, w.H), Is.EqualTo((map.W, map.H)));
            var (sx, sy) = map.Spawn;
            Assert.That(w.At(sx, sy).T, Is.EqualTo("grass"), "the start is on dry land");
            Assert.That(w.At(0, 0).Water, Is.GreaterThan(0), "the corner is sea");
            foreach (var thing in WorldThings.Load(Dir).All)
                Assert.That(w.At(thing.CellX, thing.CellY).Water, Is.Zero, $"{thing.Id} stands on dry land");
            Assert.That(w.CellHash(), Is.EqualTo(BuiltHash));
        }

        [Test]
        public void ItRunsTheSameOnEveryBuild()
        {
            string once = Run();
            Assert.That(Run(), Is.EqualTo(once), "two runs in one build");
            Assert.That(once, Is.EqualTo(RunHash), "the golden, the same on every machine");
        }

        [Test]
        public void ItsHashIsStable()
        {
            string hash = MapIdentity.Hash(Dir);
            Assert.That(MapIdentity.Hash(Dir), Is.EqualTo(hash));
            Assert.That(hash, Is.EqualTo(FolderHash), "the folder as committed; set it again when its files change");
            Assert.That(hash, Is.Not.EqualTo(MapIdentity.Hash(Path.Combine(ChemistryTests.Folders.Maps, GameFolders.DefaultMap))));
        }

        [Test]
        public void APeerWithOnlyTheValleyIsTurnedAway()
        {
            string valley = GameFolders.DefaultMap + ":" + MapIdentity.Hash(Path.Combine(ChemistryTests.Folders.Maps, GameFolders.DefaultMap));
            string chain = Id + ":" + MapIdentity.Hash(Dir);
            var name = MapFile.Load(Dir).Name;
            Assert.That(MapIdentity.Refusal(Id, name, MapIdentity.Hash(Dir), valley), Is.EqualTo("The host is on The Drowned Chain, and this copy doesn't have it."));
            Assert.That(MapIdentity.Refusal(Id, name, MapIdentity.Hash(Dir), valley + "," + Id + ":0123456789abcdef"), Is.EqualTo("The host is on The Drowned Chain, and this copy has a different The Drowned Chain."));
            Assert.That(MapIdentity.Refusal(Id, name, MapIdentity.Hash(Dir), valley + "," + chain), Is.Null);
            Assert.That(MapIdentity.Offered(ChemistryTests.Folders), Does.Contain(chain));
        }
    }
}
