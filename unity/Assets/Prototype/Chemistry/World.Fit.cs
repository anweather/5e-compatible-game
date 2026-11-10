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
        // The rules the cells are made to fit now; a chunk with a lower Fit was made to fit older ones.
        int fits = 1;
        // Chunks whose Fit is behind fits.
        int unfitChunks;
        // The liquids' ids (Cell.Liquid's meaning) under each older fit number a chunk still holds.
        readonly Dictionary<int, string[]> fitLiquids = new Dictionary<int, string[]>();

        /// <summary>The fit number of the rules in force: one more for each switch that could unfit a cell.</summary>
        public int FitNumber => fits;

        /// <summary>The fit number a loaded chunk's cells were last made to fit: FitNumber, or lower while it waits to activate.</summary>
        public int FitOf(int cx, int cy) => ChunkOrThrow(cx, cy).Fit;

        /// <summary>A switch from <paramref name="before"/> that could unfit cells: the active chunks are made to fit now, the rest later.</summary>
        void Refit(Rules before)
        {
            fitLiquids[fits] = before.Liquids.Select(l => l.Id).ToArray();
            fits++;
            foreach (var ch in SortedChunks(true)) MakeFit(ch);
            unfitChunks = 0;
            var held = new HashSet<int>();
            foreach (var ch in chunks.Values)
            {
                if (ch.Fit == fits) continue;
                unfitChunks++;
                held.Add(ch.Fit);
            }
            foreach (int n in fitLiquids.Keys.Where(n => !held.Contains(n)).ToList()) fitLiquids.Remove(n);
        }

        // A chunk's cells made to fit the rules in force (its liquids remapped by id, then each cell sanitized), as the
        // pass over every cell did at the switch.
        void MakeFit(Chunk ch)
        {
            if (ch.Fit == fits) return;
            var map = LiquidMap(fitLiquids[ch.Fit]);
            foreach (var c in ch.Cells)
            {
                if (c == null) continue;
                if (map.Length > 0) RemapLiquid(c, map);
                Sanitize(c);
            }
            Fitted(ch);
        }

        // A chunk whose cells fit the rules in force now (made to fit, or made afresh under them).
        void Fitted(Chunk ch)
        {
            if (ch.Fit == fits) return;
            ch.Fit = fits;
            unfitChunks--;
        }

        // Every chunk's cells made afresh under the rules in force (World.MakeFrom).
        void AllFitted()
        {
            foreach (var ch in chunks.Values) ch.Fit = fits;
            unfitChunks = 0;
            fitLiquids.Clear();
        }

        // The liquids' ids a chunk's Cell.Liquid means: none (InForce) for the rules in force.
        string[] LiquidsOf(Chunk ch) => ch.Fit == fits ? InForce : fitLiquids[ch.Fit];

        // LiquidsOf for a chunk that fits the rules in force (the rules always have water, so a real list is never empty).
        static string[] InForce => Array.Empty<string>();

        // A cell's liquid id under the liquids its chunk holds (LiquidsOf).
        string LiquidIdIn(string[] liquids, Cell c) => liquids.Length == 0 ? LiquidId(c) : liquids[c.Kind];

        // The liquids a cell of Cells[i] holds, for a pass over Cells: InForce while every chunk fits, or for a dry cell.
        string[] LiquidsAt(int i, Cell c) =>
            unfitChunks == 0 || c.Kind == 0 ? InForce : LiquidsOf(FindChunk(Chunks.ChunkOf(i % W), Chunks.ChunkOf(i / W)));

        /// <summary>The id of the liquid at (x, y) ("water" when dry), in the liquids its chunk was made under; null off the world.</summary>
        public string LiquidIdAt(int x, int y)
        {
            var c = Locate(x, y, out var ch, out _);
            return c == null ? null : LiquidIdIn(LiquidsOf(ch), c);
        }

        /// <summary>
        /// The chunks not yet made to fit the rules in force, for the cell hash: "#cx,cy,fit" each, by row then column;
        /// "" when every chunk fits, so a world without such a switch hashes as it did.
        /// </summary>
        public string FitText()
        {
            if (unfitChunks == 0) return "";
            var sb = new StringBuilder();
            foreach (var ch in SortedChunks(false))
                if (ch.Fit != fits) sb.Append('#').Append(Whole(ch.X)).Append(',').Append(Whole(ch.Y)).Append(',').Append(Whole(ch.Fit));
            return sb.ToString();
        }

        /// <summary>A cell's raised or dug height as fitting the rules makes it: clamped into sim.json's height range.</summary>
        public int HeightOf(Cell c)
        {
            var range = Rules.Sim.Height;
            if (c.Height < range.Min) return range.Min;
            return c.Height > range.Max ? range.Max : c.Height;
        }
    }
}
