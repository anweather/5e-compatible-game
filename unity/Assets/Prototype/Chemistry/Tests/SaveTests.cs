using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace Chem.Tests
{
    /// <summary>
    /// Saving a world to disk (World.Save.cs, docs/WORLD_STREAMING.md "Part 2, third slice"):
    /// save, load and step equals stepping straight through; a save of a load is the save
    /// again; only changed chunks are saved; a save that doesn't fit is refused.
    /// </summary>
    [Category("Simulation")]
    public class SaveTests
    {
        static Rules R => ChemistryTests.R;

        /// <summary>A made-up land from a seed: grass and brush, stone outcrops, a pond and a bedrock rim.</summary>
        sealed class Meadow : IChunkSource
        {
            readonly int w, h;
            readonly uint seed;

            public Meadow(int w, int h, uint seed = 3, string version = "test-meadow-1")
            {
                this.w = w;
                this.h = h;
                this.seed = seed;
                Version = version;
            }

            public string Version { get; }

            public void Make(int x, int y, Cell cell)
            {
                if (x == 0 || y == 0 || x == w - 1 || y == h - 1)
                {
                    cell.T = "bedrock";
                    return;
                }
                uint n = Sky.Hash(seed, y * w + x, 9) % 100;
                cell.T = Lands.First(l => n < l.below).terrain;
                int dx = x - 150, dy = y - 40;
                if (dx * dx + dy * dy < 64) cell.Water = 6;
            }
        }

        const int W = 192, H = 128;

        // The meadow's terrains by a cell's hash out of 100.
        static readonly (uint below, string terrain)[] Lands = { (60, "grass"), (85, "dirt"), (95, "brush"), (100, "stone") };

        static World Fresh(IChunkSource source)
        {
            var w = new World(R, W, H, 5);
            w.MakeFrom(source);
            return w;
        }

        /// <summary>Fire across the meadow, water, dug and raised earth, a chunk let go and a defense field marching.</summary>
        static World Played(IChunkSource source)
        {
            var w = Fresh(source);
            w.ApplyDefense(new DefenseCommand(DefenseKind.Start, 4, 100, 60, 100));
            w.Cast("fireball", 30, 30, 1);
            w.Cast("create-water", 100, 20, 1);
            w.Cast("move-earth", 70, 90, 1);
            w.Cast("wall-of-stone", 20, 110, 1);
            for (int t = 0; t < 40; t++) w.Step();
            w.SetActive(2, 1, false);
            w.Cast("fire-bolt", 120, 70, 1);
            for (int t = 0; t < 20; t++) w.Step();
            return w;
        }

        static string State(World w) =>
            $"{w.Tick}/{w.RngState}/{w.CellHash()}/" +
            string.Join(",", w.ChunkCoords.Select(c => $"{(w.IsActive(c.cx, c.cy) ? 1 : 0)}:{w.LastLive(c.cx, c.cy)}")) + "/" +
            string.Join(",", w.Raiders.Select(r => $"{r.Id}@{r.X},{r.Y}:{r.Hp}"));

        [Test]
        public void FogPatchesComeBackTheSameFromASave()
        {
            var rules = R.WithMods(new[] { ModPatch.Parse("{ 'sim': { 'weather': { 'calmPeriods': 0, 'rampMinutes': 0.02, 'periodMinutes': 60, " +
                "'weights': { 'clear': 0, 'cloudy': 0, 'windy': 0, 'fog': 1, 'rain': 0, 'storm': 0, 'snow': 0 } } } }") });
            var source = new Meadow(W, H);
            var straight = new World(rules, W, H, 11);
            straight.MakeFrom(source);
            for (int i = 0; i < 120; i++) straight.Step();
            Assert.That(straight.AllCells().Any(c => c.Gas == "fog"), "it's foggy when saved");
            var loaded = World.Load(straight.Save(source), rules, source);
            for (int i = 0; i < 60; i++)
            {
                straight.Step();
                loaded.Step();
            }
            Assert.That(loaded.CellHash(), Is.EqualTo(straight.CellHash()));
            Assert.That(loaded.RngState, Is.EqualTo(straight.RngState));
        }

        [Test]
        public void SaveLoadAndStepIsTheSameAsSteppingStraightThrough()
        {
            var source = new Meadow(W, H);
            var straight = Played(source);
            var loaded = World.Load(straight.Save(source), R, source);
            Assert.That(State(loaded), Is.EqualTo(State(straight)), "as saved");
            Assert.That(straight.Raiders.Count, Is.GreaterThan(0), "raiders were marching when it saved");
            for (int t = 0; t < 120; t++)
            {
                straight.Step();
                loaded.Step();
                if (t % 20 == 0) Assert.That(State(loaded), Is.EqualTo(State(straight)), $"tick {straight.Tick}");
            }
            Assert.That(State(loaded), Is.EqualTo(State(straight)));
            Assert.That(loaded.Log.Count, Is.GreaterThan(0), "and it goes on logging");
        }

        // How to make a cell differ from the source in just one member, and what it reads back as. Every public member of
        // Cell that can be set is here, so a new one fails CellMembersAreAllInTheGuard until it is: then this test says
        // whether SameCell, SaveCell and LoadCell know it. Liquid and Sets mean something only while a liquid other than
        // water stands in the cell (Kind, Setting), so theirs set that too.
        static readonly Dictionary<string, (Action<Cell> set, object value)> Differs = new Dictionary<string, (Action<Cell>, object)>
        {
            ["T"] = (c => c.T = "sand", "sand"),
            ["Water"] = (c => c.Water = 5, 5),
            ["Liquid"] = (c => { c.Water = 5; c.Liquid = MagmaIndex; }, MagmaIndex),
            ["Sets"] = (c => { c.Water = 5; c.Liquid = MagmaIndex; c.Sets = 77; }, 77),
            ["Frozen"] = (c => { c.Water = 5; c.Frozen = true; }, true),
            ["Cover"] = (c => c.Cover = "oil", "oil"),
            ["Fire"] = (c => c.Fire = 2, 2),
            ["Fuel"] = (c => c.Fuel = 7, 7),
            ["Gas"] = (c => { c.Gas = "steam"; c.GasAmt = 3; }, "steam"),
            ["GasAmt"] = (c => { c.Gas = "steam"; c.GasAmt = 3; }, 3),
            ["Charge"] = (c => c.Charge = 2, 2),
            ["Height"] = (c => c.Height = 3, 3),
            ["Life"] = (c => c.Life = 9, 9),
            ["DampUntil"] = (c => c.DampUntil = 50, 50),
            ["Rust"] = (c => c.Rust = 4, 4),
            ["Heat"] = (c => c.Heat = 5, 5),
            ["Lit"] = (c => c.Lit = 6, 6),
            ["Slick"] = (c => c.Slick = 7, 7),
            ["Star"] = (c => c.Star = 8, 8),
            ["Cracked"] = (c => c.Cracked = 9, 9),
        };

        // The public members of Cell a save must keep: its fields, and its properties that can be set.
        static IEnumerable<MemberInfo> CellMembers() =>
            typeof(Cell).GetFields(BindingFlags.Public | BindingFlags.Instance).Cast<MemberInfo>()
                .Concat(typeof(Cell).GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.SetMethod?.IsPublic == true))
                .OrderBy(m => m.Name, StringComparer.Ordinal);

        static object ValueOf(MemberInfo m, Cell c) => m is FieldInfo f ? f.GetValue(c) : ((PropertyInfo)m).GetValue(c);

        const string MagmaRow = @"""magma"": { ""name"": ""Magma"", ""thick"": 2, ""heap"": 3, ""soaks"": false, ""floats"": false,
            ""sets"": { ""into"": ""rock"", ""after"": 300 } }";

        static Rules magma;
        static Rules Magma => magma ??= R.WithMods(new[] { ModPatch.Parse(@"{ ""liquids"": { " + MagmaRow + " } }") });
        static int MagmaIndex => Magma.Liquids.First(l => l.Id == "magma").Index;

        [Test]
        public void CellMembersAreAllInTheGuard()
        {
            var missing = CellMembers().Select(m => m.Name).Where(n => !Differs.ContainsKey(n)).ToList();
            Assert.That(missing, Is.Empty, "a new Cell member: add it to World.SameCell, SaveCell and LoadCell, then to Differs here");
        }

        // A cell that differs from its source in one member only comes back from a save with it: so SameCell sees it (or
        // the cell isn't saved at all), and SaveCell and LoadCell carry it.
        [TestCaseSource(nameof(DiffersNames))]
        public void EveryCellMemberComesThroughASave(string member)
        {
            var w = new World(Magma, 64, 64, 5);
            var source = new ComboTests.Blank();
            w.MakeFrom(source);
            var cell = w.At(20, 20);
            Differs[member].set(cell);
            var info = CellMembers().Single(m => m.Name == member);
            Assert.That(ValueOf(info, cell), Is.EqualTo(Differs[member].value), "set as the guard says");
            var back = World.Load(w.Save(source), Magma, source).At(20, 20);
            Assert.That(ValueOf(info, back), Is.EqualTo(Differs[member].value), $"Cell.{member} is lost in a save");
            Assert.That(back.Kind, Is.EqualTo(cell.Kind));
        }

        static IEnumerable<string> DiffersNames() => Differs.Keys.OrderBy(k => k, StringComparer.Ordinal);

        // A real map's world (the valley, as ChemGrid builds it from its folder over its BakedSource: puzzles, stamps,
        // buildings, golf, the Hold, then its land.json), played, saved at a tick, loaded over the same source and land:
        // the same cells, tick and RNG, the same save text again, and the same as the world never saved after more ticks,
        // its water (the Gauntlet's pits among it) running on the same land. The Meadow above is made up; this is what a
        // player plays.
        [TestCase(false)]
        [TestCase(true)]
        public void TheValleyComesBackFromASaveTheSame(bool cast)
        {
            string dir = Path.Combine(LandmarkIntentTests.GameWorldDir(), "maps", "valley");
            var straight = GameValley(dir, out var source, out var land);
            int mx = straight.W / 2, my = straight.H / 2;
            if (cast)
            {
                straight.Cast("fireball", mx, my, 1);
                straight.Cast("create-water", mx + 20, my, 1);
                straight.Cast("move-earth", mx, my + 20, 1);
            }
            for (int t = 0; t < 30; t++) straight.Step();

            string save = straight.Save(source);
            var loaded = World.Load(save, R, source, land: land);
            Assert.That(State(loaded), Is.EqualTo(State(straight)), "as saved");
            Assert.That(loaded.Save(source), Is.EqualTo(save), "a save of the load is the save again");
            Assert.That(LandOf(loaded), Is.EqualTo(LandOf(straight)), "the same land, NoLand where it was");

            for (int t = 0; t < 60; t++)
            {
                if (cast && t == 10)
                {
                    straight.Cast("fire-bolt", mx - 20, my, 1);
                    loaded.Cast("fire-bolt", mx - 20, my, 1);
                }
                straight.Step();
                loaded.Step();
            }
            Assert.That(State(loaded), Is.EqualTo(State(straight)), $"tick {straight.Tick}");
        }

        // Question 8 (late-join-snapshot): the valley keeps its start after its puzzles, map stamps and rows are laid
        // (ChemGrid), and a save keeps that start. Loaded, then summoned, the dragon puts back the map as laid (the
        // Gauntlet's pits with their water, the rows, the puzzles), as it does in the world never saved.
        [Test]
        public void ADragonAfterALoadPutsBackWhatTheMapLaid()
        {
            string dir = Path.Combine(LandmarkIntentTests.GameWorldDir(), "maps", "valley");
            var straight = GameValley(dir, out var source, out var land);
            straight.KeepStart();
            int mx = straight.W / 2, my = straight.H / 2;
            straight.Cast("fireball", 285, 433, 1); // in the Gauntlet: its pits are laid by puzzles.json
            straight.Cast("fireball", mx, my, 1);
            for (int t = 0; t < 30; t++) straight.Step();
            string save = straight.Save(source);
            Assert.That(save, Does.Contain("\"chunks\":[{\"x\""), "the save keeps the laid start");
            var loaded = World.Load(save, R, source, land: land);
            Assert.That(loaded.DragonText(), Is.EqualTo(straight.DragonText()));
            foreach (var w in new[] { straight, loaded }) w.ApplyDragon(new DragonCommand());
            bool came = false;
            for (int t = 0; t < 6000 && (!came || straight.Dragon != null); t++)
            {
                straight.Step();
                loaded.Step();
                came |= straight.Dragon != null;
                if (t % 200 == 0) Assert.That(loaded.DragonText(), Is.EqualTo(straight.DragonText()), $"tick {straight.Tick}");
            }
            Assert.That(came && straight.Dragon == null, Is.True, "the dragon came and went");
            Assert.That(State(loaded), Is.EqualTo(State(straight)));
            Assert.That(loaded.At(285, 433).T, Is.EqualTo(straight.At(285, 433).T));
        }

        // island-startup: land set a chunk at a time (SetLandByChunk: the chunks still their source's get theirs when first
        // read) is the land SetLand makes: the same save (its landSet's hash and marks) before any of it is read, the same
        // cells as water runs over it for 200 ticks, and the same land under every cell after.
        [Test]
        public void LandSetAChunkAtATimeIsTheSameLand()
        {
            string dir = Path.Combine(LandmarkIntentTests.GameWorldDir(), "maps", "valley");
            var eager = GameValley(dir, out var source, out _);
            var lazy = SparseWorldBench.Build(dir, R, out _);
            var map = MapFile.Load(dir);
            lazy.SetLandByChunk(LandChunks.Read(File.ReadAllText(Path.Combine(dir, MapFile.LandName)), map.W, map.H));
            int unmade = lazy.LandUnmadeChunks;
            Assert.That(unmade, Is.GreaterThan(lazy.ChunkCoords.Count / 2), "most chunks' land left for later");
            Assert.That(lazy.Save(source), Is.EqualTo(eager.Save(source)), "the save, before any land is read");
            Assert.That(lazy.LandUnmadeChunks, Is.EqualTo(unmade), "and the save made none");
            int mx = eager.W / 2, my = eager.H / 2;
            foreach (var w in new[] { eager, lazy })
            {
                ChunkStreaming.Apply(w, ChunkStreaming.StartPlan(w, new[] { (mx, my), (100, 100), (500, 480) }));
                w.Cast("create-water", mx, my, 1);
                w.Cast("create-water", 100, 100, 1);
                w.Cast("fireball", 500, 480, 1);
            }
            for (int t = 0; t < 200; t++)
            {
                eager.Step();
                lazy.Step();
                if (t % 20 == 0) Assert.That(lazy.CellHash(), Is.EqualTo(eager.CellHash()), $"tick {t}");
            }
            Assert.That(State(lazy), Is.EqualTo(State(eager)));
            Assert.That(LandOf(lazy), Is.EqualTo(LandOf(eager)), "the land under every cell");
            Assert.That(lazy.Save(source), Is.EqualTo(eager.Save(source)));
        }

        [Test]
        public void ASaveGivenLandIsRefusedWithoutItOrWithOther()
        {
            string dir = Path.Combine(LandmarkIntentTests.GameWorldDir(), "maps", "valley");
            var w = GameValley(dir, out var source, out _);
            string save = w.Save(source);
            Assert.That(save, Does.Contain("\"landSet\""));
            Assert.Throws<SaveException>(() => World.Load(save, R, source));
            var other = ValleyLand(dir);
            other[other.Length / 2] += 1;
            Assert.Throws<SaveException>(() => World.Load(save, R, source, land: other));
        }

        // The valley as the game has it: Build lays the map, and the land goes on last, as ValleyChemistry sets it once the
        // ground binds, so the water the lays put down (the Gauntlet's pits) is NoLand. Build sets it before the lays, as
        // a generated map's land is made (LandFromSource, GeneratedSourceTests); SetLand again makes it the game's.
        static World GameValley(string dir, out BakedSource source, out int[] land)
        {
            var w = SparseWorldBench.Build(dir, R, out source);
            land = ValleyLand(dir);
            w.SetLand(land);
            return w;
        }

        static int[] ValleyLand(string dir)
        {
            var map = MapFile.Load(dir);
            return LandFile.Read(File.ReadAllText(Path.Combine(dir, MapFile.LandName)), map.W, map.H);
        }

        static string LandOf(World w)
        {
            uint hash = 0x811c9dc5;
            for (int y = 0; y < w.H; y++)
                for (int x = 0; x < w.W; x++) hash = (hash ^ (uint)w.LandUnitsAt(x, y)) * 0x01000193;
            return hash.ToString("x8", System.Globalization.CultureInfo.InvariantCulture);
        }

        // A world save is one line of JSON (Formatting.None): no line ends at all, so nothing in it follows
        // Environment.NewLine, and a Mac host's save is a Windows joiner's byte for byte. Its land too (landSet).
        [Test]
        public void AWorldSaveHasNoLineEnds()
        {
            var source = new Meadow(W, H);
            var w = Played(source);
            w.SetLand(new int[W * H]);
            string save = w.Save(source);
            Assert.That(save, Does.Contain("\"landSet\""));
            Assert.That(save, Does.Not.Contain("\n").And.Not.Contain("\r"));
        }

        [Test]
        public void ASaveOfALoadIsTheSaveAgain()
        {
            var source = new Meadow(W, H);
            string save = Played(source).Save(source);
            Assert.That(World.Load(save, R, source).Save(source), Is.EqualTo(save));
            // The rules' fingerprint left out: any change to rules/ moves it, not just one to the format.
            string pinned = save.Replace(R.Fingerprint, "rules");
            uint hash = 0x811c9dc5;
            foreach (char c in pinned) hash = (hash ^ c) * 0x01000193;
            Assert.That(hash.ToString("x", System.Globalization.CultureInfo.InvariantCulture), Is.EqualTo("c1737215"), "the format (a change here means old saves need a new SaveFormat)");
        }

        [Test]
        public void ACellsRustHeatLightAndSlickAreSaved()
        {
            var source = new Meadow(W, H);
            var w = Fresh(source);
            w.At(5, 5).T = "metal";
            w.At(5, 5).Rust = 640;
            w.At(5, 5).Heat = 50;
            w.At(5, 5).Lit = 900;
            w.At(5, 5).Slick = 3;
            var loaded = World.Load(w.Save(source), R, source);
            Assert.That(loaded.At(5, 5).Rust, Is.EqualTo(640));
            Assert.That(loaded.At(5, 5).Heat, Is.EqualTo(50));
            Assert.That(loaded.At(5, 5).Lit, Is.EqualTo(900));
            Assert.That(loaded.At(5, 5).Slick, Is.EqualTo(3));
            Assert.That(loaded.CellHash(), Is.EqualTo(w.CellHash()));
        }

        [Test]
        public void OnlyChangedChunksAreSaved()
        {
            var source = new Meadow(W, H);
            var w = Fresh(source);
            Assert.That(w.ChangedChunks(source), Is.Empty);
            string empty = w.Save(source);
            Assert.That(empty, Does.Contain("\"chunks\":[]"), "an untouched world: nothing but the header");

            w.Cast("fire-bolt", 10, 10, 1);
            Assert.That(w.ChangedChunks(source), Is.EqualTo(new[] { (0, 0) }));
            string one = w.Save(source);
            Assert.That(one.Length, Is.LessThan(empty.Length + 400), "one burning cell is a few dozen bytes, not a chunk");
            Assert.That(World.Load(one, R, source).CellHash(), Is.EqualTo(w.CellHash()));

            w.SetActive(2, 1, false);
            Assert.That(w.Save(source), Does.Contain("\"x\":2,\"y\":1,\"inactive\":true"), "streaming state is saved even with no cells changed");
        }

        [Test]
        public void ASaveThatDoesntFitIsRefused()
        {
            var source = new Meadow(W, H);
            string save = Played(source).Save(source);
            Assert.Throws<SaveException>(() => World.Load(save, R, new Meadow(W, H, 4, "test-meadow-2")));
            var modded = R.WithMods(new[] { ModPatch.Parse("{ \"sim\": { \"fire\": { \"spreadChance\": 0.9 } } }") });
            Assert.That(Assert.Throws<SaveException>(() => World.Load(save, modded, source)).Message, Does.Contain("other rules"));
            Assert.Throws<SaveException>(() => World.Load(save.Replace("\"format\":1", "\"format\":99"), R, source));
            Assert.Throws<SaveException>(() => World.Load("not a save", R, source));
        }
    }
}
