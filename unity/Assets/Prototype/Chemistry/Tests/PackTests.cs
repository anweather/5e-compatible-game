using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    /// <summary>
    /// The sparse World's packed chunks (World.Pack.cs; docs/design/sparse-world.md, D17): a new world makes no cells until
    /// they're read, a chunk packed and unpacked again is what it was, the hash, the save and the audit read a packed chunk
    /// where it lies, chunks pack as play goes once their hold is up (never beside an active chunk or drawn), a load lays
    /// inactive chunks in packed, and a peer that packs other chunks at other ticks hashes the same as one that never packs.
    /// </summary>
    [Category("Simulation")]
    public class PackTests
    {
        /// <summary>A world's cells as they stood when it was made: the source a packed chunk's changes are against.</summary>
        internal sealed class KeptSource : IChunkSource
        {
            readonly int w;
            readonly Cell[] kept;

            public KeptSource(World world)
            {
                w = world.W;
                kept = world.AllCells().Select(c =>
                {
                    var k = new Cell();
                    Copy(c, k);
                    return k;
                }).ToArray();
            }

            public string Version => "kept-1";

            public void Make(int x, int y, Cell cell) => Copy(kept[y * w + x], cell);
        }

        /// <summary>Every field of a cell into another.</summary>
        internal static void Copy(Cell from, Cell to)
        {
            to.T = from.T;
            to.Water = from.Water;
            to.Liquid = from.Liquid;
            to.Frozen = from.Frozen;
            to.Fire = from.Fire;
            to.Height = from.Height;
            to.Cover = from.Cover;
            to.Gas = from.Gas;
            to.Sets = from.Sets;
            to.Fuel = from.Fuel;
            to.GasAmt = from.GasAmt;
            to.Charge = from.Charge;
            to.Life = from.Life;
            to.DampUntil = from.DampUntil;
            to.Rust = from.Rust;
            to.Heat = from.Heat;
            to.Lit = from.Lit;
            to.Slick = from.Slick;
            to.Star = from.Star;
            to.Cracked = from.Cracked;
        }

        /// <summary>Every field of a cell, the ones the hash leaves out (a dry cell's Liquid, DampUntil) too.</summary>
        static string Fields(Cell c) =>
            $"{c.T},{c.Water},{c.Liquid},{c.Frozen},{c.Fire},{c.Height},{c.Cover},{c.Gas},{c.Sets},{c.Fuel},{c.GasAmt},{c.Charge},{c.Life},"
            + $"{c.DampUntil},{c.Rust},{c.Heat},{c.Lit},{c.Slick},{c.Star},{c.Cracked}";

        static string AllFields(World w) => string.Join(";", w.AllCells().Select(Fields));

        // ChunkTests' valley (4 x 3 chunks, fires crossing chunk edges) a while into its script: fire, ash, ice, moved earth.
        static World Worked(int ticks = 150)
        {
            var w = ChunkTests.Valley();
            ChunkTests.ValleyScript(w, ticks);
            return w;
        }

        static Rules With(string streaming) => R.WithMods(new[] { ModPatch.Parse("{ \"sim\": { \"streaming\": " + streaming + " } }") });

        static Rules R => ChemistryTests.R;

        [Test]
        public void ANewWorldMakesNoCellsUntilAChunkIsRead()
        {
            var w = new World(R, 64 * 30, 64 * 30);
            Assert.That((w.UnpackedCount, w.ChunkCoords.Count(c => w.IsPacked(c.cx, c.cy))), Is.EqualTo((0, 900)), "900 chunks, every one packed with no changes");
            Assert.That(w.At(70, 70).T, Is.EqualTo("dirt"));
            Assert.That(w.UnpackedCount, Is.EqualTo(1), "a read unpacks its chunk only");
            Assert.That(w.ReadUnpacks, Is.Zero, "an active chunk unpacked by a read isn't a read unpack");
            w.SetAllActive(false);
            w.SetActive(0, 0, true);
            Assert.That(w.UnpackedCount, Is.EqualTo(4), "activating unpacks a chunk and the chunks round it");
            for (int t = 0; t < 5; t++) w.Step();
            Assert.That((w.UnpackedCount, w.ReadUnpacks), Is.EqualTo((4, 0)), "the tick reads only those");
        }

        [Test]
        public void AChunkPackedAndUnpackedIsWhatItWas()
        {
            var w = Worked();
            var kept = new KeptSource(ChunkTests.Valley());
            w.SetAllActive(false);
            string fields = AllFields(w), hash = w.CellHash();
            var saved = w.ChunkCoords.Select(c => w.SaveChunkOf(c.cx, c.cy, w.Source).ToString()).ToList();
            var savedKept = w.ChunkCoords.Select(c => w.SaveChunkOf(c.cx, c.cy, kept).ToString()).ToList();
            int pooled = w.PackPoolCount;
            var packed = w.ChunkCoords.Select(c => w.Pack(c.cx, c.cy)).ToList();
            Assert.That(packed.Sum(p => p.Count), Is.GreaterThan(1000), "the script changed cells");
            Assert.That(w.ChunkCoords.All(c => w.IsPacked(c.cx, c.cy)));
            Assert.That((w.UnpackedCount, w.PackPoolCount), Is.EqualTo((0, pooled + packed.Count)), "the blocks went to the pool");
            Assert.That(w.CellHash(), Is.EqualTo(hash), "the hash reads packed chunks");
            Assert.That(w.ChunkCoords.Select(c => w.SaveChunkOf(c.cx, c.cy, w.Source).ToString()), Is.EqualTo(saved), "a packed chunk's record against its source");
            Assert.That(w.ChunkCoords.Select(c => w.SaveChunkOf(c.cx, c.cy, kept).ToString()), Is.EqualTo(savedKept), "and against another source");
            Assert.That(w.UnpackedCount, Is.Zero, "neither unpacked a chunk");
            foreach (var p in packed) w.Unpack(p.X, p.Y);
            Assert.That(w.PackPoolCount, Is.EqualTo(pooled), "unpacking took the blocks back");
            Assert.That(w.ReadUnpacks, Is.Zero);
            Assert.That(AllFields(w), Is.EqualTo(fields), "every field of every cell");
            Assert.That(w.CellHash(), Is.EqualTo(hash));
            Assert.That(w.ChunkCoords.Select(c => w.SaveChunkOf(c.cx, c.cy, w.Source).ToString()), Is.EqualTo(saved), "the save's records, fire generations too");
        }

        [Test]
        public void TheHashTheSaveAndTheAuditReadAPackedChunkWhereItLies()
        {
            var w = Worked();
            w.SetAllActive(false);
            string hash = w.CellHash(), text = w.CellText();
            var chunkHashes = w.ChunkCoords.ToDictionary(c => c, c => w.ChunkHash(c.cx, c.cy));
            string save = w.Save(w.Source);
            // Every other chunk, so the walks cross from packed to unpacked along every row.
            foreach (var (cx, cy) in w.ChunkCoords.Where(c => (c.cx + c.cy) % 2 == 0)) w.Pack(cx, cy);
            int unpacked = w.UnpackedCount;
            Assert.That(w.CellHash(), Is.EqualTo(hash));
            Assert.That(w.CellText(), Is.EqualTo(text));
            Assert.That(w.ChunkCoords.ToDictionary(c => c, c => w.ChunkHash(c.cx, c.cy)), Is.EqualTo(chunkHashes));
            Assert.That(w.Save(w.Source), Is.EqualTo(save));
            Assert.That(w.ChangedChunks(w.Source).Count, Is.EqualTo(w.ChunkCoords.Count));
            w.KeepStart();
            Assert.That((w.UnpackedCount, w.ReadUnpacks), Is.EqualTo((unpacked, 0)), "nothing unpacked a chunk");
        }

        [Test]
        public void AReadUnpacksAPackedChunk()
        {
            var w = Worked(10);
            w.SetAllActive(false);
            string fields = Fields(w.At(70, 70));
            w.Pack(1, 1);
            Assert.That(Fields(w.At(70, 70)), Is.EqualTo(fields));
            Assert.That((w.IsPacked(1, 1), w.ReadUnpacks), Is.EqualTo((false, 1)));
        }

        [Test]
        public void OnlyAnInactiveChunkPacks()
        {
            var w = Worked(10);
            Assert.That(() => w.Pack(0, 0), Throws.InvalidOperationException);
        }

        // On another thread than the world's (a view worker: the terrains' arrays, the ground's Parallel.For).
        static Exception OffThread(Action act)
        {
            Exception caught = null;
            var t = new System.Threading.Thread(() =>
            {
                try
                {
                    act();
                }
                catch (Exception e)
                {
                    caught = e;
                }
            });
            t.Start();
            t.Join();
            return caught;
        }

        [Test]
        public void AReadOfAPackedChunkOffTheWorldsThreadThrowsAndLeavesItPacked()
        {
            var w = Worked(10);
            w.SetAllActive(false);
            string fields = Fields(w.At(70, 70)), hash = w.CellHash();
            w.Pack(1, 1);
            int unpacked = w.UnpackedCount, unpacks = w.Unpacks;
            var e = OffThread(() => w.At(70, 70));
            Assert.That(e, Is.InstanceOf<InvalidOperationException>().With.Message.Contains("only the thread that made the world"));
            Assert.That((w.IsPacked(1, 1), w.UnpackedCount, w.Unpacks, w.ReadUnpacks), Is.EqualTo((true, unpacked, unpacks, 0)), "nothing changed");
            Assert.That(OffThread(() => w.Unpack(1, 1)), Is.InstanceOf<InvalidOperationException>(), "nor does Unpack");
            Assert.That(w.IsPacked(1, 1));
            Assert.That(Fields(w.At(70, 70)), Is.EqualTo(fields), "the world's own thread reads it");
            Assert.That(w.CellHash(), Is.EqualTo(hash));
        }

        [Test]
        public void AnUnpackedChunkReadsOffTheWorldsThread()
        {
            var w = Worked(10);
            w.SetAllActive(false);
            string fields = Fields(w.At(70, 70));
            string read = null;
            bool own = true;
            Assert.That(OffThread(() =>
            {
                own = w.OnOwnThread;
                read = Fields(w.At(70, 70));
            }), Is.Null);
            Assert.That((own, w.OnOwnThread, read), Is.EqualTo((false, true, fields)));
            w.Pack(1, 1);
            Assert.That(OffThread(() => w.Pack(1, 1)), Is.Null, "a packed chunk packs to nothing on any thread");
            w.Unpack(1, 1);
            Assert.That(OffThread(() => w.Pack(1, 1)), Is.InstanceOf<InvalidOperationException>(), "packing off the thread throws too");
            Assert.That(w.IsPacked(1, 1), Is.False);
        }

        // ---------- packing as play goes (PackDue) ----------

        [Test]
        public void AnInactiveChunkPacksOnceItsHoldIsUpUnlessItIsBesideAnActiveOneOrDrawn()
        {
            Assert.That((R.Sim.Streaming.PackTicks, R.Sim.Streaming.PacksPerTick), Is.EqualTo((300, 2)), "sim.json streaming.packTicks and packsPerTick");
            var w = new World(With("{ \"packTicks\": 10, \"packsPerTick\": 2 }"), 64 * 6, 64 * 2, 3, "grass");
            foreach (var (cx, cy) in w.ChunkCoords) w.Paint(cx * 64 + 5, cy * 64 + 5, "stone");
            w.SetAllActive(false);
            w.SetActive(0, 0, true);
            w.SetDrawn(new[] { (5, 1) });
            var packedAt = new Dictionary<(int, int), int>();
            for (int t = 0; t < 30; t++)
            {
                w.Step();
                foreach (var c in w.ChunkCoords.Where(c => w.IsPacked(c.cx, c.cy) && !packedAt.ContainsKey(c))) packedAt[c] = w.Tick;
            }
            // (0, 0) is active; (1, 0), (0, 1), (1, 1) are beside it; (5, 1) is drawn. The other seven pack, two a tick, by row.
            var expect = new[] { (2, 0), (3, 0), (4, 0), (5, 0), (2, 1), (3, 1), (4, 1) };
            Assert.That(packedAt.Keys, Is.EquivalentTo(expect));
            Assert.That(expect.Select(c => packedAt[c]), Is.EqualTo(new[] { 10, 10, 11, 11, 12, 12, 13 }), "the hold counts from the last unpack, at tick 0");
            Assert.That(w.ReadUnpacks, Is.Zero, "the tick never read a packed chunk");
            w.SetDrawn(null);
            for (int t = 0; t < 11; t++) w.Step();
            Assert.That(w.IsPacked(5, 1), "out of the hint, its hold counted from then");
            Assert.That(w.At(5 * 64 + 5, 64 + 5).T, Is.EqualTo("stone"), "and it reads as it was");
        }

        [Test]
        public void ALoadLaysItsInactiveChunksInPacked()
        {
            var w = Worked();
            foreach (var (cx, cy) in w.ChunkCoords.Where(c => c.cx >= 2)) w.SetActive(cx, cy, false);
            string save = w.Save(w.Source);
            var loaded = World.Load(save, R, w.Source);
            Assert.That(loaded.ChunkCoords.Where(c => !loaded.IsActive(c.cx, c.cy)).All(c => loaded.IsPacked(c.cx, c.cy)), "every inactive chunk packed");
            Assert.That(loaded.CellHash(), Is.EqualTo(w.CellHash()));
            Assert.That(loaded.Save(loaded.Source), Is.EqualTo(save));
            for (int t = 0; t < 50; t++)
            {
                w.Step();
                loaded.Step();
            }
            Assert.That(loaded.CellHash(), Is.EqualTo(w.CellHash()));
        }

        [Test]
        public void AWriteToEveryCellPacksAgainWhatItUnpacks()
        {
            // The dragon's dive snuffs every fire on the map (SnuffFires), packed chunks' too, and leaves them packed.
            World Diving(Rules rules)
            {
                var w = ChunkTests.Valley();
                w.ApplyRules(rules);
                ChunkTests.ValleyScript(w, 30);
                w.KeepStart();
                w.SetAllActive(false);
                w.SetActive(0, 0, true);
                w.ApplyDragon(new DragonCommand());
                return w;
            }
            var plain = Diving(With("{ \"packsPerTick\": 0 }"));
            var packing = Diving(With("{ \"packsPerTick\": 0 }"));
            var far = packing.ChunkCoords.Where(c => c.cx >= 2).ToList();
            foreach (var (cx, cy) in far) packing.Pack(cx, cy);
            while (plain.Dragon.Phase == DragonPhase.Warning)
            {
                Assert.That(far.Sum(c => Burning(plain, c)), Is.GreaterThan(0), "fires burn on in the far chunks, let go while burning");
                plain.Step();
                packing.Step();
            }
            Assert.That(far.All(c => packing.IsPacked(c.cx, c.cy)), "the far chunks packed again after the dive");
            Assert.That(packing.CellHash(), Is.EqualTo(plain.CellHash()));
            Assert.That(far.Sum(c => Burning(packing, c)), Is.Zero, "and their fires are out");
        }

        static int Burning(World w, (int cx, int cy) c)
        {
            int n = 0;
            for (int y = c.cy * Chunks.Size; y < Math.Min(w.H, (c.cy + 1) * Chunks.Size); y++)
                for (int x = c.cx * Chunks.Size; x < Math.Min(w.W, (c.cx + 1) * Chunks.Size); x++)
                    if (w.At(x, y).Fire > 0) n++;
            return n;
        }

        // ---------- a fast flight (core-mem-scale) ----------

        /// <summary>
        /// A character flying fast along a meadow of 40 x 3 chunks (about 10 cells a tick, 90 m/s), the planner switching
        /// chunks round it and the view's drawn hint following it, under the shipped streaming rules (a 300-tick hold, 2
        /// packs a tick). The chunks it leaves more than streaming.keep behind pack without waiting their hold, so the
        /// only chunks still unpacked are those it needs (active, lingering, beside one, drawn) or near the drawn ones; the
        /// lingering active chunks behind it are the planner's (streaming.linger*), not packing's. A peer that never packs
        /// hashes the same.
        /// </summary>
        [Test]
        public void ChunksLeftFarBehindAFastFlightPackWithoutWaitingTheirHold()
        {
            const int size = 64, ticks = 230;
            // Grass only, so every chunk starts packed, as a map's do: painting would unpack them all before the flight.
            World Meadow(Rules rules) => new World(rules, 40 * size, 3 * size, 9, "grass");
            var flier = Meadow(R);
            var still = Meadow(With("{ \"packsPerTick\": 0 }"));
            var planner = new ChunkPlanner();
            int most = 0, mostLeft = 0;
            for (int t = 1; t <= ticks; t++)
            {
                (int x, int y) me = (40 + t * 10, size + 20);
                var plan = planner.Plan(flier, new[] { me });
                var drawn = new List<(int cx, int cy)>();
                for (int cy = 0; cy < 3; cy++)
                    for (int cx = Chunks.ChunkOf(me.x) - 4; cx <= Chunks.ChunkOf(me.x) + 4; cx++)
                        if (flier.HasChunk(cx, cy)) drawn.Add((cx, cy));
                foreach (var w in new[] { flier, still })
                {
                    ChunkStreaming.Apply(w, plan);
                    if (t % 20 == 1) w.Cast("fire-bolt", me.x + 30, me.y, 1);
                    w.Step();
                }
                flier.SetDrawn(drawn);
                most = Math.Max(most, flier.UnpackedCount);
                // Nothing far from the drawn chunks and unneeded stays unpacked past a few (a lingering column let go at once
                // packs over the next ticks, 2 a tick); with the hold alone, these piled up behind for 300 ticks.
                int left = flier.ChunkCoords.Count(c => !flier.IsPacked(c.cx, c.cy) && !Needed(flier, c.cx, c.cy)
                    && drawn.All(d => Math.Max(Math.Abs(d.cx - c.cx), Math.Abs(d.cy - c.cy)) > 3));
                Assert.That(left, Is.LessThanOrEqualTo(6), $"tick {t}: chunks left behind, unpacked");
                mostLeft = Math.Max(mostLeft, left);
                if (t % 30 == 0) Assert.That(flier.CellHash(), Is.EqualTo(still.CellHash()), $"tick {t}");
            }
            TestContext.Out.WriteLine($"PACK a fast flight over {ticks} ticks: at most {most} chunks unpacked ({mostLeft} left behind at most), {flier.UnpackedCount} at the end ({flier.ActiveChunks.Count} active), {flier.Packs} packs; never packing, {still.UnpackedCount}");
            Assert.That(flier.CellHash(), Is.EqualTo(still.CellHash()));
            Assert.That(still.UnpackedCount, Is.GreaterThan(flier.UnpackedCount), "where never packing piles them up");
        }

        // Active, or one of the eight round an active chunk: what the tick may read.
        static bool Needed(World w, int cx, int cy)
        {
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                    if (w.HasChunk(cx + dx, cy + dy) && w.IsActive(cx + dx, cy + dy)) return true;
            return false;
        }

        // ---------- a peer packing differently (sparse-world.md, step 2's test) ----------

        /// <summary>
        /// A late joiner replays the host's history (TickBatch.Timeline) to the host's state, whatever each peer packed on
        /// the way: the host packs soon after a chunk is let go, the joiner never, a third peer at once and many a tick. A
        /// wizard walks east across a meadow of 12 x 3 chunks for 900 ticks, casting fire ahead as it goes.
        /// </summary>
        [Test]
        public void ALateJoinerReplaysToTheHostsStateWhateverEachPeerPacked()
        {
            const int ticks = 900, size = 64;
            World Meadow(string streaming)
            {
                var w = new World(With(streaming), 12 * size, 3 * size, 9, "grass");
                for (int y = 0; y < w.H; y += 7)
                    for (int x = (y * 3) % 11; x < w.W; x += 11) w.Paint(x, y, "tree");
                return w;
            }
            var host = Meadow("{ \"packTicks\": 40, \"packsPerTick\": 2 }");
            var rules = RuleSet.Plain(host.Rules);
            var planner = new ChunkPlanner();
            var casts = new List<(int, CastCommand)>();
            var switches = new List<(int, ChunkSwitch)>();
            var hashes = new Dictionary<int, string>();
            for (int t = 1; t <= ticks; t++)
            {
                var batch = new TickBatch(t);
                (int x, int y) me = (40 + t * 3 / 4, size + 20);
                if (t % 30 == 1)
                {
                    var cast = new CastCommand("fire-bolt", me.x + 8, me.y, 1);
                    batch.Casts.Add(cast);
                    casts.Add((t, cast));
                }
                foreach (var s in planner.Plan(host, new[] { me }))
                {
                    batch.Chunks.Add(s);
                    switches.Add((t, s));
                }
                rules = batch.RunOn(host, rules, out _);
                if (t % 30 == 0) hashes[t] = host.CellHash();
            }
            Assert.That(host.Packs, Is.GreaterThan(10), "the host packed chunks behind the wizard");
            Assert.That(host.ReadUnpacks, Is.Zero, "and never read one packed");

            var timeline = TickBatch.Timeline(casts, null, ticks, null, switches);
            foreach (var streaming in new[] { "{ \"packsPerTick\": 0 }", "{ \"packTicks\": 0, \"packsPerTick\": 50 }" })
            {
                var joiner = Meadow(streaming);
                var joinerRules = RuleSet.Plain(joiner.Rules);
                for (int t = 1; t <= ticks; t++)
                {
                    joinerRules = timeline[t].RunOn(joiner, joinerRules, out _);
                    if (t % 30 == 0) Assert.That(joiner.CellHash(), Is.EqualTo(hashes[t]), $"{streaming}: tick {t}");
                }
                Assert.That(joiner.RngState, Is.EqualTo(host.RngState), streaming);
                Assert.That(joiner.ActiveChunks, Is.EqualTo(host.ActiveChunks), streaming);
                Assert.That(joiner.Save(joiner.Source).Replace(joiner.Rules.Fingerprint, "rules"), Is.EqualTo(host.Save(host.Source).Replace(host.Rules.Fingerprint, "rules")), streaming);
                Assert.That(AllFields(joiner), Is.EqualTo(AllFields(host)), streaming);
            }
        }


        // The chunks the script lets go and takes back, by tick: a column at a time, while the fires are still burning.
        static readonly (int tick, int cx, bool active)[] Switches =
        {
            (25, 3, false), (40, 0, false), (90, 3, true), (120, 1, false), (150, 0, true), (200, 1, true), (210, 2, false), (260, 2, true),
        };

        static Rules High(Rules rules) =>
            RuleSet.Plain(rules).WithMods(new[] { new ActiveMod("high", "{ \"patch\": { \"sim\": { \"height\": { \"max\": 12, \"blockAt\": 12 } } } }") }).Effective;

        /// <summary>
        /// One peer never packs; the other packs each inactive chunk at its own ticks (one pattern for every seed of
        /// <paramref name="pattern"/>) and unpacks it at others, and packs on its own (PackDue) with a short hold besides.
        /// The two must hash the same at every tick, across a rule switch that unfits the inactive chunks, and run on the same.
        /// </summary>
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void APeerPackingDifferentlyHashesTheSame(int pattern)
        {
            var plain = ChunkTests.Valley();
            var packing = ChunkTests.Valley();
            var never = With("{ \"packsPerTick\": 0 }");
            var soon = With("{ \"packTicks\": " + (pattern - 1) + ", \"packsPerTick\": " + pattern + " }");
            // A switch of the height range at tick 100 and back at 230: it unfits the inactive chunks, packed or not (World.Fit.cs).
            var highNever = High(never);
            var highSoon = High(soon);
            plain.ApplyRules(never);
            packing.ApplyRules(soon);
            int packs = 0, unpacks = 0;
            for (int t = 0; t < 300; t++)
            {
                if (t == 100)
                {
                    plain.ApplyRules(highNever);
                    packing.ApplyRules(highSoon);
                }
                if (t == 230)
                {
                    plain.ApplyRules(never);
                    packing.ApplyRules(soon);
                }
                foreach (var s in Switches.Where(s => s.tick == t))
                    for (int cy = 0; cy < 3; cy++)
                    {
                        plain.SetActive(s.cx, cy, s.active);
                        packing.SetActive(s.cx, cy, s.active);
                    }
                // Pack and unpack the inactive chunks at ticks of the pattern's own, never at the plain peer's.
                foreach (var (cx, cy) in packing.ChunkCoords.Where(c => !packing.IsActive(c.cx, c.cy)))
                {
                    int beat = (t * pattern + cx * 7 + cy * 3) % (5 + pattern);
                    if (beat == 0 && !packing.IsPacked(cx, cy))
                    {
                        packing.Pack(cx, cy);
                        packs++;
                    }
                    else if (beat == 2 && packing.IsPacked(cx, cy))
                    {
                        packing.Unpack(cx, cy);
                        unpacks++;
                    }
                }
                Assert.That(packing.CellHash(), Is.EqualTo(plain.CellHash()), $"tick {t}");
                ChunkTests.ValleyScriptTick(plain, t);
                ChunkTests.ValleyScriptTick(packing, t);
            }
            TestContext.Out.WriteLine($"PACK pattern {pattern}: {packs} packs and {unpacks} unpacks between ticks, {packing.Packs} packs in all, {packing.ReadUnpacks} unpacked by a read");
            Assert.That(packs, Is.GreaterThan(20), "it packed");
            Assert.That(unpacks, Is.GreaterThan(10), "and unpacked");
            Assert.That(packing.Packs, Is.GreaterThan(packs), "and packed on its own");
            Assert.That(packing.CellHash(), Is.EqualTo(plain.CellHash()));
            Assert.That(packing.RngState, Is.EqualTo(plain.RngState));
            Assert.That(packing.Save(packing.Source).Replace(soon.Fingerprint, "rules"), Is.EqualTo(plain.Save(plain.Source).Replace(never.Fingerprint, "rules")));
            Assert.That(AllFields(packing), Is.EqualTo(AllFields(plain)));
        }
    }
}
