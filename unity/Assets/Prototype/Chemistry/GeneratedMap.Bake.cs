// A generated map's bake (GeneratedMap.Bake): the whole map written as a map folder a row of chunks at a time, each row
// dropped once it's written, so a bake holds two rows of chunks whatever the map's size (J-086: costs follow what's near
// players, never the map's size; bake-lean). A row's chunks are made on every thread at once while the row before is
// written on one, in order, so the files are the same bytes on any number of threads, and the same bytes as when the bake
// held the whole map and wrote it at the end (BakeTests).
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Chem
{
    public sealed partial class GeneratedMap
    {
        /// <summary>
        /// Writes the whole map as a map folder into <paramref name="outDir"/> (its map.json too, unless it's the map's own
        /// folder): the chunks' land and cells, the pinned valley's in its place, every chunk's things, and the valley's
        /// rows moved to the valley's place with the landmarks' rows added. Its chunks are made as it writes, on
        /// <paramref name="threads"/> threads (all of them for 0), or taken from <paramref name="chunks"/> (MakeAll's).
        /// </summary>
        public BakeReport Bake(string outDir, GeneratedChunk[] chunks = null, int threads = 0)
        {
            if (!Bakes) throw new InvalidOperationException(NoBake);
            var timer = Stopwatch.StartNew();
            // A pinned block's rows are read from its own bake, as a pinned map's are from its folder.
            if (Inner != null && !IsBaked(PinnedDir)) Inner.Bake(PinnedDir);
            int w = Gen.ChunksW, h = Gen.ChunksH;
            if (chunks != null && chunks.Length != w * h) throw new ArgumentException($"a bake of {w} x {h} chunks, given {chunks.Length}", nameof(chunks));
            Directory.CreateDirectory(outDir);
            var report = new BakeReport { W = Gen.CellsW, H = Gen.CellsH };
            // The rows first: the places.json they write says which water bodies the land is read for as it goes.
            WriteRows(outDir);
            var water = WaterRuns.For(outDir, Gen.CellsW, Gen.CellsH);
            var options = new ParallelOptions { MaxDegreeOfParallelism = threads > 0 ? threads : Environment.ProcessorCount };
            using (var files = new ChunkRows(outDir, Gen.CellsW, Gen.CellsH, water))
            {
                var writing = Task.CompletedTask;
                try
                {
                    for (int cy = 0; cy < h; cy++)
                    {
                        var row = new GeneratedChunk[w];
                        var making = Stopwatch.StartNew();
                        if (chunks != null) Array.Copy(chunks, cy * w, row, 0, w);
                        else
                        {
                            int y = cy;
                            Parallel.For(0, w, options, cx => row[cx] = Gen.Make(cx, y));
                        }
                        report.MakeMs += making.Elapsed.TotalMilliseconds;
                        writing.GetAwaiter().GetResult();
                        writing = Task.Run(() => files.Add(row));
                    }
                    writing.GetAwaiter().GetResult();
                }
                finally
                {
                    // A row still being written when making one threw: let it finish before its files close.
                    if (!writing.IsCompleted) writing.ContinueWith(_ => { }, TaskScheduler.Default).Wait();
                }
                files.Finish();
            }
            if (Path.GetFullPath(outDir).TrimEnd('/', '\\') != Path.GetFullPath(MapDir).TrimEnd('/', '\\'))
                Write(outDir, MapFile.FileName, OwnMapFile());
            Write(outDir, WorldPlaces.FileName, WorldPlaces.Generate(outDir, water));
            Write(outDir, BakeName, Json(new JObject { ["inputs"] = InputHash(MapDir), ["plan"] = Plan.Hash.ToString("x8", CultureInfo.InvariantCulture), ["generator"] = ChunkGen.Version, ["format"] = BakeFormat }));
            report.TotalMs = timer.Elapsed.TotalMilliseconds;
            return report;
        }

        // cells.txt, land.json and things.json, written a row of chunks at a time from the north: what the bake once
        // gathered whole (a char and an int a cell of the map, and every chunk's things) and wrote at the end, the same
        // bytes. Its two row buffers are a row of chunks' cells and land.
        sealed class ChunkRows : IDisposable
        {
            const int Size = ChunkGen.Size;
            readonly int w, h;
            readonly char[] cells;
            readonly int[] land;
            readonly string cellsPath, cellsTemp;
            readonly StreamWriter cellsOut, thingsOut;
            readonly FileStream landOut;
            readonly LandFile.Writer landRows;
            readonly JsonTextWriter things;
            readonly WaterRuns water;
            int y;
            bool finished;

            public ChunkRows(string dir, int w, int h, WaterRuns water)
            {
                this.w = w;
                this.h = h;
                this.water = water;
                cells = new char[w * Size];
                land = new int[w * Size];
                var utf8 = new UTF8Encoding(false);
                // cells.txt is put in place only if it changed (MapFile.WriteCells), so it's written beside it first.
                cellsPath = Path.Combine(dir, MapFile.CellsName);
                cellsTemp = cellsPath + ".baking";
                cellsOut = new StreamWriter(cellsTemp, false, utf8, 1 << 16);
                landOut = new FileStream(Path.Combine(dir, MapFile.LandName), FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16);
                landRows = new LandFile.Writer(landOut, w, h, 0.125);
                // As JObject.ToString(Formatting.Indented) wrote the whole file: the same writer, a chunk at a time.
                thingsOut = new StreamWriter(Path.Combine(dir, "things.json"), false, utf8, 1 << 16);
                things = new JsonTextWriter(thingsOut) { Formatting = Formatting.Indented };
                things.WriteStartObject();
                things.WritePropertyName("about");
                things.WriteValue($"Made by ChunkGen {ChunkGen.Version} from {MapFile.FileName}: the valley's things in its chunks, then each chunk's landmarks' and seeded things.");
                things.WritePropertyName("chunks");
                things.WriteStartArray();
            }

            /// <summary>The next row of chunks, west to east.</summary>
            public void Add(GeneratedChunk[] row)
            {
                foreach (var c in row)
                    for (int j = 0; j < Size; j++)
                    {
                        Array.Copy(c.Land, j * Size, land, j * w + c.X * Size, Size);
                        Array.Copy(c.Cells, j * Size, cells, j * w + c.X * Size, Size);
                    }
                for (int j = 0; j < Size; j++, y++)
                {
                    if (y > 0) cellsOut.Write('\n');
                    cellsOut.Write(cells, j * w, w);
                    var landRow = new ReadOnlySpan<int>(land, j * w, w);
                    landRows.Row(landRow);
                    water?.Row(landRow);
                }
                foreach (var c in row)
                    if (c.Things.Count > 0) new JObject { ["chunk"] = new JArray(c.X, c.Y), ["things"] = c.Things }.WriteTo(things);
            }

            /// <summary>Ends the three files, every row written.</summary>
            public void Finish()
            {
                if (y != h) throw new InvalidOperationException($"a bake wrote {y} of its {h} rows");
                things.WriteEndArray();
                things.WriteEndObject();
                things.Flush();
                thingsOut.Write('\n');
                landRows.Finish();
                finished = true;
                Dispose();
                // As MapFile.WriteCells: the file is left as it is when it holds these cells already.
                if (File.Exists(cellsPath) && Same(cellsPath, cellsTemp)) File.Delete(cellsTemp);
                else
                {
                    if (File.Exists(cellsPath)) File.Delete(cellsPath);
                    File.Move(cellsTemp, cellsPath);
                }
            }

            public void Dispose()
            {
                cellsOut.Dispose();
                ((IDisposable)things).Dispose();
                landRows.Dispose();
                landOut.Dispose();
                if (!finished && File.Exists(cellsTemp)) File.Delete(cellsTemp);
            }

            static bool Same(string a, string b)
            {
                using var fa = File.OpenRead(a);
                using var fb = File.OpenRead(b);
                if (fa.Length != fb.Length) return false;
                var ba = new byte[1 << 16];
                var bb = new byte[1 << 16];
                int n;
                while ((n = fa.Read(ba, 0, ba.Length)) > 0)
                {
                    int got = 0;
                    while (got < n)
                    {
                        int r = fb.Read(bb, got, n - got);
                        if (r <= 0) return false;
                        got += r;
                    }
                    if (!ba.AsSpan(0, n).SequenceEqual(bb.AsSpan(0, n))) return false;
                }
                return true;
            }
        }
    }
}
