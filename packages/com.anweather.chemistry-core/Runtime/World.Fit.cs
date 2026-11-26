// Fitting chunks to the rules (J-086, #190). A rule switch that could leave a cell
// something the new rules don't define (World.Unfits: a terrain, cover or gas gone,
// the liquids changed, another height range) makes only the active chunks' cells fit
// on its tick, so a switch costs the active region, not the map. Every chunk keeps the
// fit number of the rules it was last made to fit (Chunk.Fit); the world's goes up at
// each such switch. A chunk behind it is made to fit when it activates (SetActive,
// before it catches up), by the rules in force then: a terrain dropped and brought back
// while nobody was near survives there. Reading a cell of a chunk not yet made to fit
// gives what fitting would (TerrainOf, CoverOf, LiquidOf, HeightOf), without writing it.
// Chunk switches are commands, so every peer fits the same chunks at the same tick;
// the fit numbers are in the cell hash (FitText) and the save.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Chem
{
    public sealed partial class World
    {
        int fits = 1;
        int unfitChunks;
        readonly Dictionary<int, string[]> fitLiquids = new Dictionary<int, string[]>();

        public int FitNumber => fits;

        public int FitOf(int cx, int cy) => ChunkOrThrow(cx, cy).Fit;

        void Refit(Rules before)
        {
            fitLiquids[fits] = before.Liquids.Select(l => l.Id).ToArray();
            fits++;
            foreach (var ch in SortedChunks(true)) MakeFit(ch);
            unfitChunks = 0;
            var held = new HashSet<int>();
            foreach (var ch in ChunksInOrder)
            {
                if (ch.Fit == fits) continue;
                unfitChunks++;
                held.Add(ch.Fit);
            }
            foreach (int n in fitLiquids.Keys.Where(n => !held.Contains(n)).ToList()) fitLiquids.Remove(n);
        }

        void MakeFit(Chunk ch)
        {
            if (ch.Fit == fits) return;
            var map = LiquidMap(fitLiquids[ch.Fit]);
            for (int i = 0; i < Chunks.Area; i++)
            {
                var c = ch.CellAt(i);
                if (c == null) continue;
                if (map.Length > 0) RemapLiquid(c, map);
                Sanitize(c);
            }
            Fitted(ch);
        }

        void Fitted(Chunk ch)
        {
            if (ch.Fit == fits) return;
            ch.Fit = fits;
            unfitChunks--;
        }

        void AllFitted()
        {
            foreach (var ch in ChunksInOrder) ch.Fit = fits;
            unfitChunks = 0;
            fitLiquids.Clear();
        }

        string[] LiquidsOf(Chunk ch) => ch.Fit == fits ? InForce : fitLiquids[ch.Fit];

        static string[] InForce => Array.Empty<string>();

        string LiquidIdIn(string[] liquids, Cell c) => liquids.Length == 0 ? LiquidId(c) : liquids[c.Kind];

        string[] LiquidsIn(Chunk ch, Cell c) => unfitChunks == 0 || c.Kind == 0 ? InForce : LiquidsOf(ch);

        public string LiquidIdAt(int x, int y)
        {
            var c = Locate(x, y, out var ch, out _);
            return c == null ? null : LiquidIdIn(LiquidsOf(ch), c);
        }

        public string FitText()
        {
            if (unfitChunks == 0) return "";
            var sb = new StringBuilder();
            foreach (var ch in SortedChunks(false))
                if (ch.Fit != fits) sb.Append('#').Append(Whole(ch.X)).Append(',').Append(Whole(ch.Y)).Append(',').Append(Whole(ch.Fit));
            return sb.ToString();
        }

        public int HeightOf(Cell c)
        {
            var range = Rules.Sim.Height;
            if (c.Height < range.Min) return range.Min;
            return c.Height > range.Max ? range.Max : c.Height;
        }
    }
}
