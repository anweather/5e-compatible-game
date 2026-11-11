// The simulation across the east-west seam (sim-across-the-seam, docs/design/world-12x8-scope.md "The sim across the
// seam"): a World made with wrap folds every cell and chunk x into [0, W), so fire, water, gas, casts, the chunk links,
// streaming and packing reach across where east meets west, and north and south stay walls. A world without wrap is
// as it was (the goldens elsewhere hold).
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    [Category("Simulation")]
    public class SeamTests
    {
        static Rules R => ChemistryTests.R;

        const int S = Chunks.Size;

        // Fire made certain (every catch catches, no smoke, no heat on stone, no boiling), so a fire and its mirror burn
        // alike whatever order the tick visits them in.
        static Rules certain;
        static Rules CertainFire => certain ??= R.WithMods(new[] { ModPatch.Parse(
            "{ \"sim\": { \"fire\": { \"spreadChance\": 1000, \"smokeChance\": 0, \"heatChance\": 0, \"boilChance\": 0, \"slickBoilChance\": 0 } } }") });

        static World Wrapped(Rules rules, int chunksW, int chunksH, string fill = "dirt", uint seed = 7)
        {
            var w = new World(rules, chunksW * S, chunksH * S, seed, fill, wrapX: true);
            w.WeatherSet = (WeatherKind.Clear, 0, 1000000);
            return w;
        }

        [Test]
        public void AWorldThatWrapsFoldsEveryXIntoIt()
        {
            var w = Wrapped(R, 4, 2);
            int wide = w.W;
            Assert.That(w.WrapX, Is.True);
            Assert.That(w.At(-1, 5), Is.SameAs(w.At(wide - 1, 5)));
            Assert.That(w.At(wide, 5), Is.SameAs(w.At(0, 5)));
            Assert.That(w.At(-3 * wide + 2, 5), Is.SameAs(w.At(2, 5)));
            Assert.That(w.At(0, -1), Is.Null, "north is a wall");
            Assert.That(w.At(0, w.H), Is.Null, "south is a wall");
            Assert.That(w.InBounds(-70, 0) && w.InBounds(wide + 70, w.H - 1), Is.True);
            Assert.That(w.Idx(-1, 1), Is.EqualTo(wide + wide - 1));
            Assert.That(w.FoldX(-1), Is.EqualTo(wide - 1));
            Assert.That(w.FoldChunkX(-1), Is.EqualTo(3));
            Assert.That(w.DeltaX(wide - 1, 0), Is.EqualTo(1), "east over the seam");
            Assert.That(w.DeltaX(0, wide - 1), Is.EqualTo(-1), "west over the seam");
            Assert.That(w.DeltaX(0, wide / 2), Is.EqualTo(wide / 2), "half way round counts east");
            Assert.That(w.DeltaChunkX(3, 0), Is.EqualTo(1));
            Assert.That(w.HasChunk(-1, 0) && w.HasChunk(4, 1), Is.True);
            Assert.That(w.Neighbours(0, 3).Select(n => (n.x, n.y)), Is.EqualTo(new[] { (0, 2), (1, 3), (0, 4), (wide - 1, 3) }));

            var flat = new World(R, 4 * S, 2 * S, 7);
            Assert.That(flat.WrapX, Is.False);
            Assert.That(flat.At(-1, 5), Is.Null);
            Assert.That(flat.FoldX(-1), Is.EqualTo(-1));
            Assert.That(flat.DeltaX(flat.W - 1, 0), Is.EqualTo(1 - flat.W));
            Assert.That(flat.HasChunk(-1, 0), Is.False);
            Assert.Throws<ArgumentException>(() => new World(R, 100, 64, 1, "dirt", wrapX: true), "a world that wraps is whole chunks wide");
        }

        [Test]
        public void ASwitchPastTheSeamIsTheChunkItFoldsTo()
        {
            var w = Wrapped(R, 4, 1);
            var heard = new List<ChunkSwitch>();
            w.ChunkSwitched += heard.Add;
            w.SetActive(-1, 0, false);
            Assert.That(w.IsActive(3, 0), Is.False);
            Assert.That(heard, Is.EqualTo(new[] { new ChunkSwitch(3, 0, false) }), "raised with its own coordinates");
            Assert.That(w.IsActiveAt(-1, 10), Is.False);
            Assert.That(w.IsActiveAt(0, 10), Is.True);
        }

        // ---------- fire, water, gas ----------

        // A strip of grass along row y round the seam, x from W - n to n - 1, on stone: the same strip mirrored (x to W - 1 - x).
        static World FireStrip(int n, int y)
        {
            var w = Wrapped(CertainFire, 4, 1, "stone");
            for (int k = 0; k < n; k++)
            {
                w.Paint(w.W - 1 - k, y, "grass");
                w.Paint(k, y, "grass");
            }
            return w;
        }

        static string Burnt(World w, int y) => string.Concat(Enumerable.Range(0, w.W).Select(x => Mark(w.At(x, y))));

        static char Mark(Cell c)
        {
            if (c.Fire > 0) return '*';
            return c.T == "grass" ? 'g' : '.';
        }

        // Every cell's state, row by row, west to east (or, mirrored, east to west). The tick's draws aren't in it: which
        // neighbour a burning cell meets first goes by the tick's row order, so a fire and its mirror draw a different number
        // of times for catches that can't happen (on stone); with every catch certain, what burns is the same.
        static string Cells(World w, bool mirrored)
        {
            var sb = new System.Text.StringBuilder();
            for (int y = 0; y < w.H; y++)
                for (int k = 0; k < w.W; k++)
                {
                    var c = w.At(mirrored ? w.W - 1 - k : k, y);
                    sb.Append(c.T).Append(',').Append(c.Cover).Append(',').Append(c.Fire).Append(',').Append(c.Fuel).Append(',')
                      .Append(c.Gas).Append(',').Append(c.GasAmt).Append(',').Append(c.Water).Append(',').Append(c.Heat).Append(';');
                }
            return sb.ToString();
        }

        [Test]
        public void AFireLitWestOfTheSeamBurnsEastOverItAsItsMirrorBurnsWest()
        {
            const int y = 30, n = 6;
            var east = FireStrip(n, y);
            var west = FireStrip(n, y);
            int wide = east.W;
            east.Cast("fire-bolt", wide - 2, y, 1);
            west.Cast("fire-bolt", 1, y, 3);
            bool crossed = false;
            for (int t = 0; t < 80; t++)
            {
                east.Step();
                west.Step();
                Assert.That(Cells(west, true), Is.EqualTo(Cells(east, false)), $"tick {t + 1}: against its mirror\n{Burnt(east, y)}\n{Burnt(west, y)}");
                crossed |= east.At(1, y).Fire > 0;
            }
            Assert.That(crossed, Is.True, "the fire lit at W - 2 reached x = 1 over the seam");
            Assert.That(east.At(wide - n - 1, y).T, Is.EqualTo("stone"), "and stopped where the grass did");
            Assert.That(east.At(1, y).T, Is.Not.EqualTo("grass"), "burnt");
            Assert.That(east.CellHash(), Is.EqualTo("2ae46b89"), "the wrapped world's golden");
        }

        [Test]
        public void WaterPouredEastOfTheSeamRunsWestOverIt()
        {
            var w = Wrapped(ChemistryTests.NoSoak, 4, 1, "stone");
            int wide = w.W, y = 20;
            for (int x = -8; x < 8; x++) w.Paint(x, y, "dirt"); // a channel of dirt through the stone, over the seam
            for (int x = 0; x < 4; x++) w.AddWater(x, y, 32);
            // The same channel and water in the middle of a world without wrap: what it keeps (some dries off).
            var flat = new World(ChemistryTests.NoSoak, 4 * S, S, 7, "stone");
            flat.WeatherSet = w.WeatherSet;
            for (int x = 120; x < 136; x++) flat.Paint(x, y, "dirt");
            for (int x = 128; x < 132; x++) flat.AddWater(x, y, 32);
            int Total(World on, int from) => Enumerable.Range(from, 16).Sum(x => on.At(x, y).Water);
            for (int t = 0; t < 200; t++)
            {
                w.Step();
                flat.Step();
            }
            Assert.That(w.At(wide - 1, y).Water, Is.GreaterThan(0), "it crossed to the far east");
            Assert.That(w.At(-6, y).Water, Is.GreaterThan(0));
            Assert.That(Total(w, -8), Is.EqualTo(Total(flat, 120)), "none lost at the seam: as much as the same channel away from any edge");
        }

        [Test]
        public void GasDriftsOverTheSeam()
        {
            var w = Wrapped(R, 4, 1, "dirt");
            for (int y = 0; y < w.H; y++) w.AddGas(w.At(0, y), "smoke", 6);
            bool west = false, east = false;
            for (int t = 0; t < 30 && !(west && east); t++)
            {
                w.Step();
                for (int y = 0; y < w.H; y++)
                {
                    west |= w.At(w.W - 1, y).Gas != null;
                    east |= w.At(1, y).Gas != null;
                }
            }
            Assert.That(east, Is.True, "smoke along x = 0 drifts east");
            Assert.That(west, Is.True, "and west, over the seam into the far east column");
        }

        [Test]
        public void ABurstCastOnTheSeamLandsBothSides()
        {
            var w = Wrapped(R, 4, 1, "grass");
            var cells = w.SpellCells(R.Spell("fireball"), 0, 20, 1);
            Assert.That(cells, Has.Member((w.W - 2, 20)).And.Member((2, 20)));
            Assert.That(cells.All(c => c.x >= 0 && c.x < w.W), Is.True, "every cell folded");
            w.Cast("fireball", 0, 20, 1);
            // Its crater (radius 1) blows the grass off x = -1 to 1; the rest of the burst burns.
            Assert.That(w.At(w.W - 2, 20).Fire, Is.GreaterThan(0));
            Assert.That(w.At(2, 20).Fire, Is.GreaterThan(0));
        }

        [Test]
        public void ARaiderShovedOverTheSeamComesOnAtTheOtherSide()
        {
            var w = Wrapped(R, 4, 1, "dirt");
            var r = w.SpawnRaider("bone-hound", -1, 20, 10);
            Assert.That(r.X, Is.EqualTo(w.W - 1), "spawned where it folds to");
            Assert.That(w.RaiderAt(-1, 20), Is.SameAs(r));
            w.Cast("gust-of-wind", w.W - 3, 20, 1);
            Assert.That(r.X, Is.InRange(0, 8), $"pushed east over the seam, at {r.X}");
        }

        // ---------- streaming ----------

        static (int, int)[] Actives(World w) => w.ActiveChunks.Select(c => (c.cx, c.cy)).ToArray();

        [Test]
        public void ACharacterByTheSeamKeepsTheFarEastActive()
        {
            // The 12 x 10's width in chunks (108) is too many for a quick test; 12 chunks round shows the same.
            var w = Wrapped(R, 12, 3);
            var at = new[] { (10, 1 * S + 30) };
            ChunkStreaming.Apply(w, ChunkStreaming.StartPlan(w, at));
            int reach = R.Sim.Streaming.Reach;
            Assert.That(reach, Is.EqualTo(2));
            var cols = new[] { 0, 1, 2, 10, 11 };
            Assert.That(Actives(w), Is.EqualTo(new[] { 0, 1, 2 }.SelectMany(cy => cols.Select(cx => (cx, cy))).ToArray()),
                "reach 2 round chunk 0: chunks 10 and 11 across the seam, row by row in folded order");

            // Walking west over the seam the keep holds what's behind, and the reach brings on what's ahead.
            var planner = new ChunkPlanner();
            ChunkStreaming.Apply(w, planner.Plan(w, new[] { (-S * 2 + 10, 1 * S + 30) })); // chunk 10, given unfolded
            Assert.That(w.IsActive(8, 1) && w.IsActive(9, 1) && w.IsActive(0, 1) && w.IsActive(1, 1), Is.True);
            Assert.That(w.IsActive(2, 1), Is.False, "4 chunks east of chunk 10 the short way round: past keep 3");

            var flat = new World(R, 12 * S, 3 * S, 7);
            ChunkStreaming.Apply(flat, ChunkStreaming.StartPlan(flat, at));
            Assert.That(flat.IsActive(11, 1), Is.False, "without wrap the far east is far");
        }

        [Test]
        public void TheStaticPlansMeasureRoundTheSeamWhenAskedTo()
        {
            var chunks = Enumerable.Range(0, 12).Select(cx => (cx, 0)).ToList();
            var at = new[] { (5, 10) };
            var plain = ChunkStreaming.Plan(chunks, (cx, cy) => false, at, 1, 2);
            var round = ChunkStreaming.Plan(chunks, (cx, cy) => false, at, 1, 2, wrapChunks: 12);
            Assert.That(plain.Select(s => s.CX), Is.EqualTo(new[] { 0, 1 }));
            Assert.That(round.Select(s => s.CX), Is.EqualTo(new[] { 0, 1, 11 }));
            // Within 60 cells of x = 5: chunk 11 (5 cells west over the seam) and chunk 1 (59 east), not chunk 10 (69 west).
            var near = ChunkStreaming.Within(chunks, 5, 10, 60, wrapCells: 12 * S);
            Assert.That(near.Select(c => c.cx), Is.EqualTo(new[] { 0, 1, 11 }));
            Assert.That(ChunkStreaming.Within(chunks, 5, 10, 60).Select(c => c.cx), Is.EqualTo(new[] { 0, 1 }));
        }

        [Test]
        public void AFireOnTheSeamKnocksTheChunkAcrossItActive()
        {
            var w = Wrapped(R, 8, 1, "grass");
            var planner = new ChunkPlanner();
            var at = new[] { (3 * S + 30, 30) };
            ChunkStreaming.Apply(w, ChunkStreaming.StartPlan(w, at));
            // Reach 2 round chunk 3: chunks 1 to 5. Chunk 0's fire then runs to its west edge.
            ChunkStreaming.Apply(w, new[] { new ChunkSwitch(0, 0, true) });
            Assert.That(w.IsActive(7, 0), Is.False);
            for (int y = 0; y < S; y++) w.Paint(0, y, "grass");
            w.Cast("fire-bolt", 0, 30, 3);
            bool knocked = false;
            for (int t = 0; t < 30 && !knocked; t++)
            {
                ChunkStreaming.Apply(w, planner.Plan(w, at));
                w.Step();
                knocked = w.IsActive(7, 0);
            }
            Assert.That(knocked, Is.True, "live on chunk 0's west edge activates chunk 7 over the seam");
        }

        [Test]
        public void TheChunkOverTheSeamIsUnpackedAndKeptBesideAnActiveOne()
        {
            // Reach 2 round chunk 0 of 8: chunks 6, 7, 0, 1 and 2 active.
            var w = Wrapped(R, 8, 1, "dirt");
            ChunkStreaming.Apply(w, ChunkStreaming.StartPlan(w, new[] { (10, 30) }));
            Assert.That(w.IsActive(6, 0) && w.IsActive(7, 0) && !w.IsActive(5, 0), Is.True);
            for (int t = 0; t < 4 * R.Sim.Streaming.PackTicks + 10; t++) w.Step();
            Assert.That(w.IsPacked(7, 0) || w.IsPacked(6, 0), Is.False, "active over the seam, unpacked");
            Assert.That(w.IsPacked(5, 0), Is.False, "beside active chunk 6");
            Assert.That(w.IsPacked(4, 0), Is.True, "two chunks from any active one");
            Assert.That(w.IsPacked(3, 0), Is.False, "beside active chunk 2");
        }

        // ---------- hashes and saves ----------

        [Test]
        public void AWrappedWorldsHashSaysSoAndItsSaveKeepsIt()
        {
            var source = new FillSource("grass");
            var w = Wrapped(R, 2, 1, "grass");
            var flat = new World(R, 2 * S, S, 7, "grass");
            Assert.That(w.CellHash(), Is.Not.EqualTo(flat.CellHash()), "the same cells, but they meet at the seam");
            Assert.That(w.CellText(), Does.EndWith("~ew"));
            Assert.That(flat.CellText(), Does.Not.EndWith("~ew"));
            var sum = Fnv1aSum.Start();
            for (int k = 0; k < 3; k++) w.HashSlice(ref sum, k, 3);
            Assert.That(w.CloseHash(sum), Is.EqualTo(w.CellHash()), "the rolling hash agrees");

            var played = new World(R, 2 * S, S, 7, "grass", wrapX: true);
            played.MakeFrom(source);
            played.Cast("fireball", 0, 30, 1);
            for (int t = 0; t < 20; t++) played.Step();
            string save = played.Save(source);
            Assert.That(save, Does.Contain("\"wrap\":\"ew\""));
            var loaded = World.Load(save, R, source);
            Assert.That(loaded.WrapX, Is.True);
            Assert.That(loaded.CellHash(), Is.EqualTo(played.CellHash()));
            for (int t = 0; t < 20; t++)
            {
                played.Step();
                loaded.Step();
            }
            Assert.That(loaded.CellHash(), Is.EqualTo(played.CellHash()), "and runs on the same");
            Assert.That(flat.Save(source), Does.Not.Contain("wrap"), "a save without wrap is as it was");
        }

        [Test]
        public void TheWideWorldsMapWrapsAndTheNineDoesnt()
        {
            string maps = System.IO.Path.Combine(ChemistryTests.RulesDir(), "..", "world", "maps");
            Assert.That(MapFile.Load(System.IO.Path.Combine(maps, "world-12x10")).WrapX, Is.True);
            Assert.That(MapFile.Load(System.IO.Path.Combine(maps, "nine")).WrapX, Is.False);
            Assert.That(MapFile.Load(System.IO.Path.Combine(maps, "valley")).WrapX, Is.False);
        }
    }
}
