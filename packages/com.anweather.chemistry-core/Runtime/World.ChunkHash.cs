// The lockstep check's cell hash a chunk at a time (#168): a chunk's hash is made again only after its cells change, and the digest of them all follows each change, so a check costs the changed chunks, never the map.
using System;
using System.Collections.Generic;

namespace Chem
{
    public readonly struct ChunkHashEntry : IEquatable<ChunkHashEntry>
    {
        public ChunkHashEntry(int x, int y, bool held, uint hash)
        {
            X = x;
            Y = y;
            Held = held;
            Hash = held ? hash : 0;
        }

        public int X { get; }
        public int Y { get; }
        public bool Held { get; }
        public uint Hash { get; }

        public bool Equals(ChunkHashEntry other) => X == other.X && Y == other.Y && Held == other.Held && Hash == other.Hash;

        public override bool Equals(object obj) => obj is ChunkHashEntry o && Equals(o);

        public override int GetHashCode() => unchecked((X * 397) ^ (Y * 31) ^ (int)Hash);

        public static bool operator ==(ChunkHashEntry a, ChunkHashEntry b) => a.Equals(b);

        public static bool operator !=(ChunkHashEntry a, ChunkHashEntry b) => !a.Equals(b);

        public override string ToString() => Held ? $"({X}, {Y}) {Hash:x}" : $"({X}, {Y}) none";
    }

    public sealed partial class World
    {
        readonly List<Chunk> staleHashes = new List<Chunk>();
        ulong chunkDigest;

        public int StaleChunkHashes => staleHashes.Count;

        public long ChunkHashesMade { get; private set; }

        public ulong ChunkDigest => chunkDigest;

        internal void StaleHash(Chunk ch)
        {
            if (ch.HashStale) return;
            ch.HashStale = true;
            staleHashes.Add(ch);
        }

        public IReadOnlyList<ChunkHashEntry> RefreshChunkHashes()
        {
            if (staleHashes.Count == 0) return Array.Empty<ChunkHashEntry>();
            staleHashes.Sort(ByRowThenColumn);
            var changed = new List<ChunkHashEntry>();
            foreach (var ch in staleHashes)
            {
                ch.HashStale = false;
                var made = MakeChunkHash(ch);
                if (made.Held == ch.HashHeld && made.Hash == ch.HashValue) continue;
                unchecked
                {
                    if (ch.HashHeld) chunkDigest -= DigestPart(ch.X, ch.Y, ch.HashValue);
                    if (made.Held) chunkDigest += DigestPart(ch.X, ch.Y, made.Hash);
                }
                ch.HashHeld = made.Held;
                ch.HashValue = made.Hash;
                changed.Add(made);
            }
            staleHashes.Clear();
            return changed;
        }

        public ChunkHashEntry CachedChunkHash(int cx, int cy)
        {
            var ch = FindChunk(cx, cy);
            return ch == null ? new ChunkHashEntry(cx, cy, false, 0) : new ChunkHashEntry(ch.X, ch.Y, ch.HashHeld, ch.HashValue);
        }

        public ChunkHashEntry MakeChunkHashOf(int cx, int cy) => MakeChunkHash(ChunkOrThrow(cx, cy));

        public string ChunkDigestHash()
        {
            var sum = Fnv1aSum.Start();
            sum.Add('$');
            sum.AddDigits(chunkDigest);
            return CloseHash(sum);
        }

        public static ulong DigestPart(int cx, int cy, uint hash)
        {
            unchecked
            {
                ulong z = ((((ulong)(uint)cy) << 32) | (uint)cx) * 0x9E3779B97F4A7C15UL ^ hash;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }

        ChunkHashEntry MakeChunkHash(Chunk ch)
        {
            ChunkHashesMade++;
            var sink = new HashSink { World = this, Sum = Fnv1aSum.Start() };
            if (IsOpen)
            {
                sink.Chunk(ch, filled.Of(ch.X, ch.Y).Hash);
                WalkChanges(ref sink, ch);
                return new ChunkHashEntry(ch.X, ch.Y, true, sink.Sum.Value);
            }
            if (HashesChanges)
            {
                WalkChanges(ref sink, ch);
                return new ChunkHashEntry(ch.X, ch.Y, sink.Cells > 0, sink.Sum.Value);
            }
            var read = new ChunkReader(this, ch, walkScratch);
            var liquids = LiquidsOf(ch);
            for (int i = 0; i < Chunks.Area; i++)
                if (read.At(i) is Cell c) AddCell(ref sink.Sum, c, liquids);
            return new ChunkHashEntry(ch.X, ch.Y, true, sink.Sum.Value);
        }
    }
}
