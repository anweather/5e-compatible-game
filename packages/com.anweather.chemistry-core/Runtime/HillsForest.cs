// A hills map's woods (docs/design/hills.md, "Forest"): patches of forest made in the generator, like the roads. A cell's
// chance of a tree is a pure function of its position, the seed and map.json, in integers only, so every peer, save and
// late joiner makes the same trees in any chunk, in any order, on any thread, and the woods meet at every chunk's edge
// and over both seams of the torus.
//
// - Where: noise that repeats round the torus (BiomeMath.Noise with the map's periods), a few octaves summed to 0..100.
//   Where it's over "wood" there's a wood; its density rises from "least" at the wood's edge to "most" per thousand
//   cells "edge" points further in, so a wood thins out at its edge into the grassland. Outside the woods a cell is a
//   lone tree "lone" times in a thousand.
// - The foothills (HillsMassif's band between its foot and its reach) are wooded more: "foothills" points more there,
//   most half way.
// - The tree line: over "treeLine"[0] land units the density falls off, to nothing at "treeLine"[1]; so the woods
//   climb the foothills and thin out up the massif's flanks, and the high meadow has only a few.
// - Kept clear: the passes (where HillsMassif's pass weight is) and, cell by cell in ChunkGen, roads, rivers, lakes,
//   fords and their banks, and land steep enough to be scree or rock.
using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Chem
{
    public sealed class HillsForest
    {
        const long One = BiomeMath.One;
        const int SaltWood = 0x6f01, SaltTree = 0x6f10;

        public readonly IReadOnlyList<(int lattice, int weight)> Octaves;
        public readonly int Wood, Edge;
        public readonly int Least, Most, Lone;
        public readonly int Foothills;
        public readonly int TreeLineFrom, TreeLineTo;

        readonly int seed, w, h;
        readonly HillsMassif massif;

        public sealed class Settings
        {
            public List<(int lattice, int weight)> Octaves = new List<(int, int)> { (576, 55), (192, 30), (64, 15) };
            public int Wood = 60, Edge = 10, Least = 30, Most = 140, Lone = 1, Foothills;
            public int TreeLineFrom = int.MaxValue - 1, TreeLineTo = int.MaxValue;
        }

        public HillsForest(int seed, int cellsW, int cellsH, Settings s, HillsMassif massif)
        {
            if (s.Octaves == null || s.Octaves.Count == 0) throw new FormatException($"{MapFile.FileName}: \"forest\" needs at least one octave");
            int sum = 0;
            foreach (var (lattice, weight) in s.Octaves)
            {
                if (lattice < 2 || weight < 0) throw new FormatException($"{MapFile.FileName}: a forest octave needs a lattice of 2 cells or more and a weight of 0 or more");
                sum += weight;
            }
            if (sum != 100) throw new FormatException($"{MapFile.FileName}: the forest octaves' weights add up to {sum}, not 100");
            if (s.Edge < 1 || s.Least < 0 || s.Most < s.Least || s.Most > 1000 || s.Lone < 0 || s.Lone > s.Least)
                throw new FormatException($"{MapFile.FileName}: \"forest\" needs edge >= 1 and 0 <= lone <= least <= most <= 1000");
            if (s.TreeLineTo <= s.TreeLineFrom) throw new FormatException($"{MapFile.FileName}: the forest's \"treeLine\" must rise");
            this.seed = seed;
            w = cellsW;
            h = cellsH;
            this.massif = massif;
            Octaves = s.Octaves;
            Wood = s.Wood;
            Edge = s.Edge;
            Least = s.Least;
            Most = s.Most;
            Lone = s.Lone;
            Foothills = s.Foothills;
            TreeLineFrom = s.TreeLineFrom;
            TreeLineTo = s.TreeLineTo;
        }

        public static HillsForest Read(JObject o, int seed, int cellsW, int cellsH, HillsMassif massif)
        {
            if (o == null) return null;
            var s = new Settings();
            if (o["octaves"] is JArray octaves)
            {
                s.Octaves = new List<(int, int)>();
                foreach (var oct in octaves) s.Octaves.Add(((int)oct[0], (int)oct[1]));
            }
            s.Wood = (int?)o["wood"] ?? s.Wood;
            s.Edge = (int?)o["edge"] ?? s.Edge;
            s.Least = (int?)o["least"] ?? s.Least;
            s.Most = (int?)o["most"] ?? s.Most;
            s.Lone = (int?)o["lone"] ?? s.Lone;
            s.Foothills = (int?)o["foothills"] ?? s.Foothills;
            if (o["treeLine"] is JArray line)
            {
                s.TreeLineFrom = (int)line[0];
                s.TreeLineTo = (int)line[1];
            }
            return new HillsForest(seed, cellsW, cellsH, s, massif);
        }

        public int Noise(int x, int y)
        {
            long px = (long)PlanGrid.Mod(x, w) * One + One / 2, py = (long)PlanGrid.Mod(y, h) * One + One / 2;
            long sum = 0;
            for (int k = 0; k < Octaves.Count; k++)
            {
                var (lattice, weight) = Octaves[k];
                if (weight == 0) continue;
                sum += BiomeMath.Noise(seed, SaltWood + k, px, py, lattice, w, h) * weight;
            }
            return (int)(sum / One);
        }

        public int Density(int x, int y, int land)
        {
            if (land >= TreeLineTo) return 0;
            long n = (long)Noise(x, y) * One;
            long keep = One;
            if (massif != null && massif.Touches(x, y))
            {
                long r = massif.Radius(x, y);
                n += massif.FootBand(r) * Foothills;
                keep = One - massif.PassAt(x, y);
            }
            long wood = (long)Wood * One;
            long d = n <= wood ? (long)Lone * One
                : (long)Least * One + BiomeMath.Smoothstep((n - wood) / Edge) * (Most - Least);
            if (land > TreeLineFrom) keep = keep * ((long)(TreeLineTo - land) * One / (TreeLineTo - TreeLineFrom)) / One;
            return (int)(d / One * keep / One);
        }

        public bool Roll(int x, int y, int density) =>
            density > 0 && WorldHash.Hash4(seed, SaltTree, PlanGrid.Mod(x, w), PlanGrid.Mod(y, h)) % 1000 < (uint)density;

        public (int cx, int cy) Heart(Func<int, int, int> land)
        {
            const int step = 8, size = ChunkGen.Size;
            long best = -1;
            var at = (0, 0);
            for (int cy = 0; cy < h / size; cy++)
                for (int cx = 0; cx < w / size; cx++)
                {
                    long sum = 0;
                    for (int j = step / 2; j < size; j += step)
                        for (int i = step / 2; i < size; i += step)
                        {
                            int x = cx * size + i, y = cy * size + j;
                            sum += Density(x, y, land(x, y)) * 1000L + Noise(x, y);
                        }
                    if (sum <= best) continue;
                    best = sum;
                    at = (cx, cy);
                }
            return at;
        }

        public byte Cover(int density) => (byte)Math.Min(255, density * 255 / Math.Max(1, Most));
    }
}
