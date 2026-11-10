using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    /// <summary>
    /// What the sparse World's packed chunks cost (World.Pack.cs, the prototype; docs/design/sparse-world.md, "Measured"),
    /// on the nine's valley square built as ChemGrid.MapWorld builds it (its cells.txt the source, then the puzzles,
    /// stamps, rows, golf and the Hold laid over): a plain chunk, a village one, one after a Fireball, a flood and earth
    /// shaping. For each, its changes against the source, the hash and the save reading it packed against unpacked, and
    /// pack and unpack; then five chunks activating at once, the whole world's hash, and how often a tick's read unpacks a
    /// packed chunk. Not a check and not in CI (Explicit): run it by name in Release and read the lines it prints.
    /// <code>dotnet test core/Game.Core.sln -c Release --filter "FullyQualifiedName~SparseWorldBench" --logger "console;verbosity=detailed"</code>
    /// </summary>
    [Explicit("a benchmark: run it by name")]
    [Category("Simulation")]
    public class SparseWorldBench
    {
        const int Runs = 15;
        static Rules R => ChemistryTests.R;
        static string Dir => Path.Combine(ChemistryTests.Folders.Maps, "valley");

        /// <summary>A baked map's cells as its source: one byte a cell from cells.txt, turned into a cell by the map legend.</summary>
        sealed class MapCellSource : IChunkSource
        {
            readonly int w;
            readonly byte[] chars;

            public MapCellSource(string dir, int w, int h)
            {
                this.w = w;
                var rows = MapFile.ReadCells(dir).Replace("\r", "").Trim('\n').Split('\n');
                chars = new byte[w * h];
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++) chars[y * w + x] = (byte)(x < rows[y].Length ? rows[y][x] : '.');
            }

            public string Version => "valley-cells-1";

            // ChemGrid.Legend, as the step-2 BakedSource will hold it in the core.
            public void Make(int x, int y, Cell cell)
            {
                switch ((char)chars[y * w + x])
                {
                    case ',': case '@': cell.T = "grass"; break;
                    case 'T': cell.T = "tree"; break;
                    case 'w': cell.T = "pit"; cell.Water = 4; break;
                    case '*': cell.T = "pit"; cell.Water = 4; cell.Frozen = true; break;
                    case 'r': cell.T = "river"; cell.Water = 4; break;
                    case '~': cell.Water = 2; break;
                    case 'o': cell.Cover = "oil"; break;
                    case '%': cell.T = "bedrock"; break;
                    case '#': cell.T = "stone"; break;
                    case '"': cell.T = "brush"; break;
                    case '=': cell.T = "wood"; break;
                    case '_': cell.T = "pit"; break;
                    case 'm': cell.T = "metal"; break;
                    case 'L': cell.T = "logwall"; break;
                    case 'P': cell.T = "pane"; break;
                    default: break; // '.' and anything else: dirt, as Reset left it
                }
            }
        }

        static World Build(out MapCellSource source)
        {
            var map = MapFile.Load(Dir);
            source = new MapCellSource(Dir, map.W, map.H);
            var w = new World(R, map.W, map.H, 7);
            w.SetLandRange(map.LandRange);
            w.MakeFrom(source);
            w.SetLand(LandFile.Read(File.ReadAllText(Path.Combine(Dir, MapFile.LandName)), map.W, map.H));
            WorldPuzzles.Load(Dir).LayInto(w);
            MapStamps.Load(Dir).LayInto(w);
            WorldBuildings.Load(Dir).LayInto(w);
            if (GolfCommand.Load(Dir) is { } hole) w.ApplyGolf(hole);
            if (WorldHold.Load(Dir) is { } hold) w.SetHold(hold);
            w.KeepStart();
            w.SetAllActive(false);
            return w;
        }

        // The best of Runs calls, in milliseconds, after one to warm up; prepare runs before each, untimed.
        static double Best(Action run, Action prepare = null)
        {
            prepare?.Invoke();
            run();
            double best = double.MaxValue;
            for (int i = 0; i < Runs; i++)
            {
                prepare?.Invoke();
                long at = Stopwatch.GetTimestamp();
                run();
                best = Math.Min(best, (Stopwatch.GetTimestamp() - at) * 1000.0 / Stopwatch.Frequency);
            }
            return best;
        }

        static void Activate(World w, int cx, int cy, int r, bool on)
        {
            for (int j = cy - r; j <= cy + r; j++)
                for (int i = cx - r; i <= cx + r; i++)
                    if (w.ChunkCoords.Contains((i, j))) w.SetActive(i, j, on);
        }

        static int Count(World w, int cx, int cy, Func<Cell, bool> test)
        {
            int n = 0;
            for (int y = cy * Chunks.Size; y < (cy + 1) * Chunks.Size; y++)
                for (int x = cx * Chunks.Size; x < (cx + 1) * Chunks.Size; x++)
                    if (test(w.At(x, y))) n++;
            return n;
        }

        static int Far((int cx, int cy) a, (int cx, int cy) b) => Math.Max(Math.Abs(a.cx - b.cx), Math.Abs(a.cy - b.cy));

        // Runs ticks on the active chunks until nothing burns in (cx, cy) or the most ticks have run; the ticks run.
        static int RunOut(World w, int cx, int cy, int most)
        {
            int t = 0;
            while (t < most && (t < 30 || Count(w, cx, cy, c => c.Fire > 0) > 0))
            {
                w.Step();
                t++;
            }
            return t;
        }

        [Test]
        public void PackedChunks()
        {
            var built = Stopwatch.StartNew();
            var w = Build(out var source);
            TestContext.Out.WriteLine($"SPARSE the valley, {w.W} x {w.H}, {w.ChunkCoords.Count} chunks, built in {built.ElapsedMilliseconds} ms");
            var strings = new PackStrings();
            var changes = w.ChunkCoords.ToDictionary(c => c, c => w.ChangesOf(c.cx, c.cy, source, strings).Count);
            TestContext.Out.WriteLine($"SPARSE as built: {changes.Count(c => c.Value == 0)} chunks no different from the source, "
                + $"{changes.Count(c => c.Value > 0)} with changes ({changes.Values.Sum()} cells, most {changes.Values.Max()})");

            var village = changes.OrderByDescending(c => c.Value).First().Key;
            bool Grassy((int cx, int cy) c) => changes[c] == 0 && Count(w, c.cx, c.cy, x => x.T == "grass" || x.T == "tree") > Chunks.Area * 9 / 10;
            var plain = w.ChunkCoords.Where(Grassy).OrderBy(c => c.cy).ThenBy(c => c.cx).First();
            // The worked chunks: grassy, two apart from each other and from the village, so each's work stays its own.
            var picked = new List<(int cx, int cy)> { village, plain };
            (int cx, int cy) Next() => w.ChunkCoords.Where(c => Grassy(c) && picked.All(p => Far(p, c) >= 3) && c.cx > 0 && c.cy > 0 && c.cx < 8 && c.cy < 8)
                .OrderBy(c => Far(c, (4, 4))).First();
            var burnt = Next();
            picked.Add(burnt);
            var flooded = Next();
            picked.Add(flooded);
            var shaped = Next();
            picked.Add(shaped);

            // A Fireball on the grass, burnt out with its neighbours active.
            Activate(w, burnt.cx, burnt.cy, 1, true);
            int bx = burnt.cx * Chunks.Size + 32, by = burnt.cy * Chunks.Size + 32;
            w.Cast("fireball", bx, by, 0, 3);
            int fireTicks = RunOut(w, burnt.cx, burnt.cy, 1800);
            Activate(w, burnt.cx, burnt.cy, 1, false);
            // A flood: eight Floods across the chunk, then the water left to run.
            Activate(w, flooded.cx, flooded.cy, 1, true);
            for (int k = 0; k < 8; k++)
            {
                w.Cast("flood", flooded.cx * Chunks.Size + 8 + 6 * k, flooded.cy * Chunks.Size + 10 + 5 * k, k % 4);
                w.Cast("create-water", flooded.cx * Chunks.Size + 50 - 5 * k, flooded.cy * Chunks.Size + 12 + 6 * k, 0, 3);
                for (int t = 0; t < 10; t++) w.Step();
            }
            for (int t = 0; t < 300; t++) w.Step();
            Activate(w, flooded.cx, flooded.cy, 1, false);
            // Earth shaping: trenches, walls, hills and dug patches over the chunk.
            Activate(w, shaped.cx, shaped.cy, 1, true);
            for (int k = 0; k < 6; k++)
            {
                int x0 = shaped.cx * Chunks.Size + 6 + 9 * k, y0 = shaped.cy * Chunks.Size + 6 + 9 * k;
                w.Cast("earthen-trench", x0, y0, k % 4);
                w.Cast("wall-of-stone", x0 + 5, y0 - 3, (k + 1) % 4);
                w.Cast("move-earth", x0 - 2, y0 + 6, 0);
                w.Cast("mold-earth", x0 + 3, y0 + 3, 0, 3);
                for (int t = 0; t < 10; t++) w.Step();
            }
            for (int t = 0; t < 60; t++) w.Step();
            Activate(w, shaped.cx, shaped.cy, 1, false);
            TestContext.Out.WriteLine($"SPARSE chunks: plain {plain}, village {village}, Fireball {burnt} (burnt out in {fireTicks} ticks), flood {flooded}, earth {shaped}");

            var kinds = new[] { ("plain", plain), ("village", village), ("Fireball", burnt), ("flood", flooded), ("earth", shaped) };
            // Once to warm the JIT up (its first calls run unoptimised), then the table.
            using (var quiet = new StringWriter())
                foreach (var (name, c) in kinds) Chunk(w, source, strings, name, c, quiet);
            TestContext.Out.WriteLine("SPARSE | chunk | changed cells | of them rare | packed | save text | hash, objects | hash, packed | save today | save packed | pack | unpack |");
            foreach (var (name, c) in kinds) Chunk(w, source, strings, name, c, TestContext.Out);

            int rowX = Math.Min(Math.Max(0, burnt.cx - 1), 4);
            AtOnce(w, source, strings, $"a chunk edge crossed, the row ({rowX}..{rowX + 4}, {burnt.cy})", Enumerable.Range(rowX, 5).Select(x => (x, burnt.cy)).ToList());
            AtOnce(w, source, strings, "a Misty Step, the 5 x 5 (2..6, 2..6)", w.ChunkCoords.Where(c => c.cx >= 2 && c.cx <= 6 && c.cy >= 2 && c.cy <= 6).ToList());
            Memory(w, source, strings);
            WholeWorld(w, source, strings);
            TickReads();
            Assert.Pass();
        }

        static void Chunk(World w, IChunkSource source, PackStrings strings, string name, (int cx, int cy) c, TextWriter output)
        {
            var p = w.ChangesOf(c.cx, c.cy, source, strings);
            int saveText = w.SavePacked(p, source, strings).ToString(Newtonsoft.Json.Formatting.None).Length;
            double hashObjects = Best(() => w.ChunkHash(c.cx, c.cy));
            double saveToday = Best(() => w.SaveChunkOf(c.cx, c.cy, source));
            var packed = w.Pack(c.cx, c.cy, source, strings);
            double hashPacked = Best(() => w.PackedChunkHash(packed, source, strings));
            double savePacked = Best(() => w.SavePacked(packed, source, strings));
            w.Unpack(c.cx, c.cy);
            double pack = Best(() => w.Pack(c.cx, c.cy, source, strings), () => w.Unpack(c.cx, c.cy));
            double unpack = Best(() => w.Unpack(c.cx, c.cy), () => { if (!w.IsPacked(c.cx, c.cy)) w.Pack(c.cx, c.cy, source, strings); });
            output.WriteLine($"SPARSE | {name} {c} | {p.Count} | {p.RareCount} | {p.Bytes / 1024.0:0.0} KB | {saveText / 1024.0:0.0} KB | "
                + $"{hashObjects:0.000} ms | {hashPacked:0.000} ms | {saveToday:0.000} ms | {savePacked:0.000} ms | {pack:0.000} ms | {unpack:0.000} ms |");
        }

        // Chunks activating at once, packed (unpack, then SetActive) and not: a character crossing a chunk edge (a row of
        // five) or arriving somewhere new (a Misty Step, a respawn: the 5 x 5).
        static void AtOnce(World w, IChunkSource source, PackStrings strings, string what, List<(int cx, int cy)> chunks)
        {
            int changed = chunks.Sum(c => w.ChangesOf(c.cx, c.cy, source, strings).Count);
            void Off()
            {
                foreach (var (cx, cy) in chunks) w.SetActive(cx, cy, false);
            }
            void PackAll()
            {
                Off();
                foreach (var (cx, cy) in chunks) w.Pack(cx, cy, source, strings);
            }
            double today = Best(() => { foreach (var (cx, cy) in chunks) w.SetActive(cx, cy, true); }, Off);
            double packed = Best(() =>
            {
                foreach (var (cx, cy) in chunks)
                {
                    w.Unpack(cx, cy);
                    w.SetActive(cx, cy, true);
                }
            }, PackAll);
            Off();
            TestContext.Out.WriteLine($"SPARSE {what}, {chunks.Count} chunks activating at once ({changed} changed cells): today {today:0.000} ms, packed {packed:0.000} ms (unpack and SetActive)");
        }

        // What's live on the heap, collected first: a measuring test's only use of the collector (as ScaleIslandTests).
#pragma warning disable S1215
        static long Heap() => GC.GetTotalMemory(true);
#pragma warning restore S1215

        // The heap with every chunk unpacked, then with every one packed and the pool let go: what a packed chunk saves.
        static void Memory(World w, IChunkSource source, PackStrings strings)
        {
            long unpacked = Heap();
            var packs = new List<PackedChunk>();
            foreach (var (cx, cy) in w.ChunkCoords) packs.Add(w.Pack(cx, cy, source, strings));
            w.TrimPackPool(0);
            long packed = Heap();
            long held = packs.Sum(p => p.Bytes);
            int n = w.ChunkCoords.Count;
            TestContext.Out.WriteLine($"SPARSE memory: packing all {n} chunks let {(unpacked - packed) / 1024.0 / n:0} KB a chunk go; "
                + $"the packed chunks hold {held / 1024.0:0} KB in all ({held / 1024.0 / n:0.0} KB a chunk)");
            foreach (var (cx, cy) in w.ChunkCoords) w.Unpack(cx, cy);
        }

        // The whole world's hash, every chunk unpacked against every chunk packed: CellHash's cost a cell either way.
        static void WholeWorld(World w, IChunkSource source, PackStrings strings)
        {
            string hash = w.CellHash();
            double objects = Best(() => w.CellHash());
            foreach (var (cx, cy) in w.ChunkCoords) if (!w.IsPacked(cx, cy)) w.Pack(cx, cy, source, strings);
            Assert.That(w.PackedCellHash(), Is.EqualTo(hash));
            double packed = Best(() => w.PackedCellHash());
            int cells = w.W * w.H;
            TestContext.Out.WriteLine($"SPARSE whole-world hash, {w.ChunkCoords.Count} chunks: unpacked {objects:0.0} ms ({objects * 1e6 / cells:0.0} ns a cell), "
                + $"all packed {packed:0.0} ms ({packed * 1e6 / cells:0.0} ns a cell); RollingHash's slice a tick costs that a cell");
            foreach (var (cx, cy) in w.ChunkCoords) w.Unpack(cx, cy);
        }

        // How often the tick reads into a packed chunk: the 5 x 5 round the spawn active, everything else packed, a fire
        // and water at the region's edges. A tick's read unpacks (Chunk.CellAt); which chunks, and how far from the active.
        static void TickReads()
        {
            var w = Build(out var source);
            var spawn = MapFile.Load(Dir).Spawn;
            (int cx, int cy) mid = (Math.Min(Math.Max(spawn.x / Chunks.Size, 2), 6), Math.Min(Math.Max(spawn.y / Chunks.Size, 2), 6));
            Activate(w, mid.cx, mid.cy, 2, true);
            var own = new PackStrings();
            foreach (var c in w.ChunkCoords.Where(c => !w.IsActive(c.cx, c.cy))) w.Pack(c.cx, c.cy, source, own);
            int edgeW = (mid.cx - 2) * Chunks.Size + 2, edgeE = (mid.cx + 3) * Chunks.Size - 3, y = mid.cy * Chunks.Size + 20;
            w.Cast("fireball", edgeW, y, 0, 3);
            w.Cast("flood", edgeE, y, 1);
            w.Cast("create-water", edgeE - 4, y + 10, 0, 3);
            var unpackedAt = new Dictionary<(int, int), int>();
            for (int t = 0; t < 600; t++)
            {
                w.Step();
                foreach (var c in w.ChunkCoords)
                    if (!w.IsActive(c.cx, c.cy) && !w.IsPacked(c.cx, c.cy) && !unpackedAt.ContainsKey(c)) unpackedAt[c] = t;
            }
            var near = w.ChunkCoords.Where(c => !w.IsActive(c.cx, c.cy)).ToList();
            string where = string.Join(", ", unpackedAt.OrderBy(u => u.Value).Select(u => $"{u.Key} at tick {u.Value} ({Far(u.Key, mid) - 2} out)"));
            TestContext.Out.WriteLine($"SPARSE tick reads, 600 ticks, 25 active, {near.Count} packed: {w.ReadUnpacks} read unpacks; "
                + $"{near.Count(c => Far(c, mid) == 3)} chunks in the ring next to the active ones, {unpackedAt.Count} of all unpacked: {where}");
        }
    }
}
