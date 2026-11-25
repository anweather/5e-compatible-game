// The open world's addresses and grounds (D20): ChunkKey's floor division, its layers as runs of rows, its order, and a
// chunk's ground on the wire.
using System.Linq;
using Chem.Lockstep;
using NUnit.Framework;

namespace Chem.Tests
{
    [Category("Unit")]
    public class OpenWorldKeyTests
    {
        [Test]
        public void CellsGoToChunksByFloorDivision()
        {
            Assert.That(ChunkKey.OfCell(0, 0, 0), Is.EqualTo(new ChunkKey(0, 0, 0)));
            Assert.That(ChunkKey.OfCell(0, 63, 64), Is.EqualTo(new ChunkKey(0, 0, 1)));
            Assert.That(ChunkKey.OfCell(2, -1, -64), Is.EqualTo(new ChunkKey(2, -1, -1)));
            Assert.That(ChunkKey.OfCell(0, -65, -129), Is.EqualTo(new ChunkKey(0, -2, -3)));
            Assert.That(ChunkKey.CellOf(-0.5, 1), Is.EqualTo(-1));
            Assert.That(ChunkKey.CellOf(2.9, 1.5), Is.EqualTo(1));
        }

        [Test]
        public void LayersAreRunsOfRowsThatComeBack()
        {
            foreach (int layer in new[] { ChunkKey.MinLayer, -1, 0, 1, ChunkKey.MaxLayer })
                foreach (int y in new[] { -ChunkKey.MostChunks * Chunks.Size, -1, 0, 63, ChunkKey.MostChunks * Chunks.Size + Chunks.Mask })
                {
                    Assert.That(ChunkKey.OfRow(ChunkKey.Row(layer, y)), Is.EqualTo((layer, y)), $"layer {layer}, y {y}");
                    var key = ChunkKey.OfCell(layer, 7, y);
                    Assert.That(key.Fits, Is.True);
                    Assert.That(ChunkKey.KeyOfChunk(key.Chunk.cx, key.Chunk.cy), Is.EqualTo(key));
                    Assert.That(Chunks.ChunkOf(ChunkKey.Row(layer, y)), Is.EqualTo(key.Chunk.cy), "a cell's row is in its chunk's row");
                }
            Assert.That(new ChunkKey(0, 0, ChunkKey.MostChunks + 1).Fits, Is.False, "past half a layer's rows would touch the next layer");
            Assert.That(new ChunkKey(ChunkKey.MaxLayer + 1, 0, 0).Fits, Is.False);
            // The last chunk row of one layer and the first of the next have a row between them that's never filled.
            int top = new ChunkKey(0, 0, ChunkKey.MostChunks).Chunk.cy, bottom = new ChunkKey(1, 0, -ChunkKey.MostChunks).Chunk.cy;
            Assert.That(bottom - top, Is.EqualTo(2));
        }

        [Test]
        public void KeysSortByLayerThenRowThenColumnAsTheSheetDoes()
        {
            var keys = new[] { new ChunkKey(1, -5, -5), new ChunkKey(0, 3, 0), new ChunkKey(0, -3, 1), new ChunkKey(-1, 0, 9), new ChunkKey(0, -9, 0) };
            var sorted = keys.OrderBy(k => k).ToList();
            Assert.That(sorted, Is.EqualTo(new[] { new ChunkKey(-1, 0, 9), new ChunkKey(0, -9, 0), new ChunkKey(0, 3, 0), new ChunkKey(0, -3, 1), new ChunkKey(1, -5, -5) }));
            Assert.That(keys.OrderBy(k => (k.Chunk.cy, k.Chunk.cx)).ToList(), Is.EqualTo(sorted), "the sheet's row-major order is the keys'");
            Assert.That(new ChunkKey(0, 1, 1).DistanceTo(new ChunkKey(0, -2, 3)), Is.EqualTo(3));
            Assert.That(new ChunkKey(0, 1, 1).DistanceTo(new ChunkKey(1, 1, 1)), Is.EqualTo(int.MaxValue), "layers are never near");
        }

        [Test]
        public void AGroundGoesOnTheWireSmallAndComesBackTheSame()
        {
            var plain = new ChunkGround("grass");
            Assert.That(plain.Bytes().Length, Is.LessThan(16), "one ground for a whole chunk is a few bytes");

            var g = new ChunkGround("grass");
            for (int ly = 0; ly < Chunks.Size; ly++)
                for (int lx = 0; lx < Chunks.Size; lx++)
                {
                    if ((lx - 40) * (lx - 40) + (ly - 20) * (ly - 20) < 80) g.Set(lx, ly, "pit", 4, -1);
                    else if ((lx * 7 + ly * 3) % 23 == 0) g.Set(lx, ly, "tree");
                    else if (lx < 10) g.Set(lx, ly, "dirt", 0, 1);
                }
            var bytes = g.Bytes();
            Assert.That(bytes.Length, Is.LessThan(4096), "a mixed chunk is a few KB at most");
            var back = ChunkGround.FromBytes(bytes);
            for (int i = 0; i < Chunks.Area; i++)
            {
                Assert.That(back.TerrainAt(i), Is.EqualTo(g.TerrainAt(i)));
                Assert.That(back.WaterAt(i), Is.EqualTo(g.WaterAt(i)));
                Assert.That(back.HeightAt(i), Is.EqualTo(g.HeightAt(i)));
            }
            Assert.That(back.Hash, Is.EqualTo(g.Hash));
            Assert.That(back.Bytes(), Is.EqualTo(bytes));
            Assert.Throws<LockstepWireException>(() => ChunkGround.FromBytes(bytes.Take(bytes.Length - 1).ToArray()));
        }

        [Test]
        public void AFillGoesOnItsChannelWhole()
        {
            var channel = LockstepChannels.Fills();
            var fill = new ChunkFill(new ChunkKey(-1, -400, 7), new ChunkGround("grass").Set(3, 4, "tree"));
            var w = new LockstepWriter();
            channel.Write(w, fill);
            var back = (ChunkFill)channel.Read(new LockstepReader(w.ToArray()));
            Assert.That(back.Key, Is.EqualTo(fill.Key));
            Assert.That(back.Ground.Bytes(), Is.EqualTo(fill.Ground.Bytes()));
            Assert.That(channel.HostOnly, Is.True, "the host's plan fills; a client never does");
        }
    }
}
