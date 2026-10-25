// Spell shapes and the cell part of casting. Port of spellCells / castSpell
// in sandbox/src/actions.ts, without pushes, damage, levitate or carry.
using System.Collections.Generic;

namespace Chem
{
    public sealed partial class World
    {
        /// <summary>Cells a spell would touch, in the order it touches them (for previews too).</summary>
        public List<(int x, int y)> SpellCells(SpellDef s, int x, int y, int dir)
        {
            var output = new List<(int x, int y)>();
            if (s == null) return output;
            void Add(int cx, int cy) { if (InBounds(cx, cy)) output.Add((cx, cy)); }
            switch (s.Shape)
            {
                case "point":
                case "target":
                    Add(x, y);
                    break;
                case "line":
                    LineCells(s, x, y, dir, output);
                    break;
                case "square":
                {
                    int n = s.Size, off = FloorDiv(n - 1, 2);
                    for (int dy = 0; dy < n; dy++)
                        for (int dx = 0; dx < n; dx++) Add(x - off + dx, y - off + dy);
                    break;
                }
                case "burst":
                {
                    int r = s.Radius;
                    for (int dy = -r; dy <= r; dy++)
                        for (int dx = -r; dx <= r; dx++) Add(x + dx, y + dy);
                    break;
                }
            }
            return output;
        }

        /// <summary>
        /// A line from (x, y) up to its first blocking cell. A terraform line from the caster
        /// (Earthen Trench) starts under their feet, one square behind the aim, so a trench cut
        /// from a pond's bank opens into the pond.
        /// </summary>
        void LineCells(SpellDef s, int x, int y, int dir, List<(int x, int y)> output)
        {
            int from = s.Effect == "terraform" && s.Origin == "self" ? -1 : 0;
            for (int k = from; k < from + s.Length; k++)
            {
                int cx = x + DirX(dir) * k, cy = y + DirY(dir) * k;
                var c = At(cx, cy);
                if (c == null && k < 0) continue;
                if (c == null) break;
                output.Add((cx, cy));
                if (IsBlocking(c)) break;
            }
        }

        static int FloorDiv(int a, int b) => (int)System.Math.Floor((double)a / b);

        /// <summary>
        /// Applies the spell's element to its cells, like castSpell's cell part. Gust of
        /// Wind (air + wind) also sets the world's wind. A terraform spell raises or digs
        /// its cells first (World.Terraform.cs). The other effects (levitate, carry,
        /// teleport) do nothing to cells.
        /// </summary>
        public void Cast(string spellId, int x, int y, int dir)
        {
            var s = Rules.Spell(spellId);
            if (s == null || !InBounds(x, y)) return;
            if (s.Effect == "levitate" || s.Effect == "carry") return;
            var cells = SpellCells(s, x, y, dir);
            AddLog($"You cast {s.Name}", LogKind.Info);
            if (s.Element == "air" && s.Wind > 0) Wind = (dir, s.Wind);
            if (s.Effect == "terraform") Terraform(s, cells, x, y);
            if (s.Element == null) return;
            foreach (var (cx, cy) in cells) Apply(cx, cy, s.Element, s.Power, dir, true);
        }
    }
}
