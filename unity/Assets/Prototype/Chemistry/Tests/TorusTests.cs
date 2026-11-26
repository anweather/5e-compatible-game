// A World that wraps both ways (a torus, map.json's "wrap": "torus": the hills, docs/design/hills.md): east joins west
// as on the Wide World (SeamTests), and south joins north the same way, so every cell and chunk y folds into [0, H) too
// (FoldY), and fire, casts, streaming, the hash and the save reach over the south seam and the corner where the seams meet.
using System;
using System.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    [Category("Simulation")]
    public class TorusTests
    {
        static Rules R => ChemistryTests.R;

        const int S = Chunks.Size;

        static Rules certain;
        static Rules CertainFire => certain ??= R.WithMods(new[] { ModPatch.Parse(
            "{ \"sim\": { \"fire\": { \"spreadChance\": 1000, \"smokeChance\": 0, \"heatChance\": 0, \"boilChance\": 0, \"slickBoilChance\": 0 } } }") });

        static World Torus(Rules rules, int chunksW, int chunksH, string fill = "dirt", uint seed = 7)
        {
            var w = new World(rules, chunksW * S, chunksH * S, seed, fill, wrapX: true, wrapY: true);
            w.WeatherSet = (WeatherKind.Clear, 0, 1000000);
            return w;
        }

        [Test]
        public void ATorusFoldsEveryCellBothWays()
        {
            var w = Torus(R, 4, 3);
            int wide = w.W, high = w.H;
            Assert.That((w.WrapX, w.WrapY), Is.EqualTo((true, true)));
            Assert.That(w.At(5, -1), Is.SameAs(w.At(5, high - 1)));
            Assert.That(w.At(5, high), Is.SameAs(w.At(5, 0)));
            Assert.That(w.At(-1, -1), Is.SameAs(w.At(wide - 1, high - 1)), "the corner");
            Assert.That(w.At(2 * wide + 3, -2 * high + 4), Is.SameAs(w.At(3, 4)));
            Assert.That(w.InBounds(-70, -70) && w.InBounds(wide + 70, high + 70), Is.True);
            Assert.That(w.Idx(1, -1), Is.EqualTo((high - 1) * wide + 1));
            Assert.That(w.CellKey(1, high), Is.EqualTo(w.CellKey(1, 0)));
            Assert.That(w.FoldY(-1), Is.EqualTo(high - 1));
            Assert.That(w.FoldChunkY(-1), Is.EqualTo(2));
            Assert.That(w.DeltaY(high - 1, 0), Is.EqualTo(1), "south over the seam");
            Assert.That(w.DeltaY(0, high - 1), Is.EqualTo(-1), "north over the seam");
            Assert.That(w.DeltaChunkY(2, 0), Is.EqualTo(1));
            Assert.That(w.WrapChunksY, Is.EqualTo(3));
            Assert.That(w.HasChunk(0, -1) && w.HasChunk(-1, 3), Is.True);
            Assert.That(w.Neighbours(0, 0).Select(n => (n.x, n.y)), Is.EqualTo(new[] { (0, high - 1), (1, 0), (0, 1), (wide - 1, 0) }));

            var ew = new World(R, 4 * S, 3 * S, 7, "dirt", wrapX: true);
            Assert.That(ew.WrapY, Is.False);
            Assert.That(ew.At(5, -1), Is.Null, "east-west only: north is a wall");
            Assert.That(ew.FoldY(-1), Is.EqualTo(-1));
            Assert.Throws<ArgumentException>(() => new World(R, 4 * S, 3 * S, 1, "dirt", wrapY: true), "a torus wraps east-west too");
            Assert.Throws<ArgumentException>(() => new World(R, 4 * S, 100, 1, "dirt", wrapX: true, wrapY: true), "a torus is whole chunks high");
        }

        [Test]
        public void ASwitchPastTheSouthSeamIsTheChunkItFoldsTo()
        {
            var w = Torus(R, 2, 3);
            w.SetActive(-1, -1, false);
            Assert.That(w.IsActive(1, 2), Is.False);
            Assert.That(w.IsActiveAt(-1, -1), Is.False);
            Assert.That(w.IsActiveAt(0, 0), Is.True);
        }

        static World FireColumn(int n, int x)
        {
            var w = Torus(CertainFire, 1, 4, "stone");
            for (int k = 0; k < n; k++)
            {
                w.Paint(x, w.H - 1 - k, "grass");
                w.Paint(x, k, "grass");
            }
            return w;
        }

        static string Cells(World w, bool mirrored)
        {
            var sb = new System.Text.StringBuilder();
            for (int k = 0; k < w.H; k++)
                for (int x = 0; x < w.W; x++)
                {
                    var c = w.At(x, mirrored ? w.H - 1 - k : k);
                    sb.Append(c.T).Append(',').Append(c.Fire).Append(',').Append(c.Fuel).Append(',').Append(c.Gas).Append(',').Append(c.Heat).Append(';');
                }
            return sb.ToString();
        }

        [Test]
        public void AFireLitNorthOfTheSouthSeamBurnsSouthOverItAsItsMirrorBurnsNorth()
        {
            const int x = 30, n = 6;
            var south = FireColumn(n, x);
            var north = FireColumn(n, x);
            south.Cast("fire-bolt", x, south.H - 2, 2);
            north.Cast("fire-bolt", x, 1, 0);
            bool crossed = false;
            for (int t = 0; t < 80; t++)
            {
                south.Step();
                north.Step();
                Assert.That(Cells(north, true), Is.EqualTo(Cells(south, false)), $"tick {t + 1}: against its mirror");
                crossed |= south.At(x, 1).Fire > 0;
            }
            Assert.That(crossed, Is.True, "the fire lit at H - 2 reached y = 1 over the south seam");
            Assert.That(south.At(x, south.H - n - 1).T, Is.EqualTo("stone"), "and stopped where the grass did");
            Assert.That(south.At(x, 1).T, Is.Not.EqualTo("grass"), "burnt");
        }

        [Test]
        public void ABurstCastOnTheCornerLandsOnAllFourSides()
        {
            var w = Torus(R, 3, 3, "grass");
            var cells = w.SpellCells(R.Spell("fireball"), 0, 0, 1);
            Assert.That(cells, Has.Member((w.W - 2, w.H - 2)).And.Member((2, 2)).And.Member((w.W - 2, 2)).And.Member((2, w.H - 2)));
            Assert.That(cells.All(c => c.x >= 0 && c.x < w.W && c.y >= 0 && c.y < w.H), Is.True, "every cell folded");
            w.Cast("fireball", 0, 0, 1);
            Assert.That(w.At(w.W - 2, w.H - 2).Fire, Is.GreaterThan(0));
            Assert.That(w.At(2, 2).Fire, Is.GreaterThan(0));
        }

        [Test]
        public void ACharacterAtTheCornerKeepsTheFourCornersActive()
        {
            var w = Torus(R, 10, 8);
            var at = new[] { (10, 10) };
            ChunkStreaming.Apply(w, ChunkStreaming.StartPlan(w, at));
            var rows = new[] { 0, 1, 2, 6, 7 };
            var cols = new[] { 0, 1, 2, 8, 9 };
            Assert.That(w.ActiveChunks.Select(c => (c.cx, c.cy)).ToArray(), Is.EqualTo(rows.SelectMany(cy => cols.Select(cx => (cx, cy))).ToArray()),
                "reach 2 round chunk (0, 0): the rows and columns over both seams, row by row in folded order");

            var planner = new ChunkPlanner();
            ChunkStreaming.Apply(w, planner.Plan(w, new[] { (10, -2 * S + 10) }));
            Assert.That(w.IsActive(0, 4) && w.IsActive(0, 5) && w.IsActive(0, 7) && w.IsActive(0, 1), Is.True);
            Assert.That(w.IsActive(0, 2), Is.False, "4 chunks south of row 6 the short way round: past keep 3");

            var within = ChunkStreaming.Within(w.ChunkCoords, 10, 10, 60, w.W, w.H);
            Assert.That(within, Has.Member((9, 7)).And.Member((0, 0)).And.Member((0, 7)).And.Member((9, 0)));
        }

        [Test]
        public void ATorussHashSaysSoAndItsSaveKeepsIt()
        {
            var source = new FillSource("grass");
            var w = Torus(R, 2, 2, "grass");
            var ew = new World(R, 2 * S, 2 * S, 7, "grass", wrapX: true);
            Assert.That(w.CellHash(), Is.Not.EqualTo(ew.CellHash()), "the same cells, but they meet at the south seam too");
            Assert.That(w.CellText(), Does.EndWith("~torus"));
            var sum = Fnv1aSum.Start();
            for (int k = 0; k < 3; k++) w.HashSlice(ref sum, k, 3);
            Assert.That(w.CloseHash(sum), Is.EqualTo(w.CellHash()), "the rolling hash agrees");

            var played = new World(R, 2 * S, 2 * S, 7, "grass", wrapX: true, wrapY: true);
            played.MakeFrom(source);
            played.Cast("fireball", 0, 0, 1);
            for (int t = 0; t < 20; t++) played.Step();
            string save = played.Save(source);
            Assert.That(save, Does.Contain("\"wrap\":\"torus\""));
            var loaded = World.Load(save, R, source);
            Assert.That((loaded.WrapX, loaded.WrapY), Is.EqualTo((true, true)));
            Assert.That(loaded.CellHash(), Is.EqualTo(played.CellHash()));
            for (int t = 0; t < 20; t++)
            {
                played.Step();
                loaded.Step();
            }
            Assert.That(loaded.CellHash(), Is.EqualTo(played.CellHash()), "and runs on the same");
            Assert.That(played.CellHash(), Is.EqualTo("2bc9fa40"), "the torus golden");
        }

        [Test]
        public void TheHillsWrapBothWaysAndTheWideWorldEastWestOnly()
        {
            string maps = System.IO.Path.Combine(ChemistryTests.RulesDir(), "..", "world", "maps");
            var hills = MapFile.Load(System.IO.Path.Combine(maps, "hills-12x10"));
            var wide = MapFile.Load(System.IO.Path.Combine(maps, "world-12x10"));
            Assert.That((hills.WrapX, hills.WrapY), Is.EqualTo((true, true)));
            Assert.That((wide.WrapX, wide.WrapY), Is.EqualTo((true, false)));
        }
    }
}
