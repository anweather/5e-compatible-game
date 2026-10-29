// The rule interpreter: an element arrives at a cell (from a spell or
// spreading fire) and reactions.json decides what happens. Port of the cell
// half of sandbox/src/react.ts.
using System;
using System.Collections.Generic;

namespace Chem
{
    public sealed partial class World
    {
        /// <summary>
        /// Applies one element to one cell (applyElement in the TS). dir -1 means no direction.
        /// direct: cast on purpose; indirect reactions (spreading) log only notable rules.
        /// </summary>
        public void Apply(int x, int y, string element, int power = 1, int dir = -1, bool direct = true)
        {
            // A paused chunk doesn't react (World.Chunks.cs).
            var c = LocateActive(x, y, out var ch, out int i);
            if (c == null) return;
            // A spell's fire (or a fresh fire from any direct source) starts at full strength.
            if (direct && element == "fire") ch.FireGeneration[i] = 0;
            string beforeCover = CoverName(c), beforeTerrain = TerrainOf(c).Name.ToLowerInvariant();
            foreach (var rule in Rules.CellRulesFor(element))
            {
                if (!Applies(c, rule.If, power)) continue;
                bool changed = rule.Do.Count == 0;
                foreach (var eff in rule.Do) changed = RunEffect(x, y, c, eff, power, dir) || changed;
                if (changed && rule.Log != null && (direct || rule.Notable))
                {
                    var text = ReplaceFirst(ReplaceFirst(rule.Log, "{cover}", beforeCover), "{terrain}", beforeTerrain);
                    LogOnce(rule.Id, text, rule.Notable ? LogKind.Notable : LogKind.Info);
                }
                if (rule.Stop) break;
            }
            // Then whatever stands there (a raider: World.Raiders.cs).
            ReachRaider(x, y, element, direct);
        }

        void ReachRaider(int x, int y, string element, bool direct)
        {
            var raider = raiders.Count > 0 ? RaiderAt(x, y) : null;
            if (raider != null) ApplyRaiderElement(raider, element, direct);
        }

        /// <summary>A raise effect (a pillar): the cell becomes that terrain, unless a raider stands there.</summary>
        bool RaiseUp(int x, int y, Cell c, string terrain)
        {
            if (RaiderAt(x, y) != null) return false;
            DisplaceWater(x, y);
            c.T = terrain;
            c.Cover = null;
            c.Fire = 0;
            return true;
        }

        string CoverName(Cell c) =>
            c.Cover != null && Rules.Covers.TryGetValue(c.Cover, out var cv) ? cv.Name.ToLowerInvariant() : "";

        static string ReplaceFirst(string s, string find, string with)
        {
            int i = s.IndexOf(find, StringComparison.Ordinal);
            return i < 0 ? s : s.Substring(0, i) + with + s.Substring(i + find.Length);
        }

        bool Matches(Cell c, CellCond cond)
        {
            if (cond == null) return true;
            if (cond.Cover != null && !(c.Cover != null && Contains(cond.Cover, c.Cover))) return false;
            if (cond.FireFlag == true && c.Fire == 0) return false;
            if (cond.FireFlag == false && c.Fire > 0) return false;
            if (cond.FireMin.HasValue && c.Fire < cond.FireMin.Value) return false;
            if (cond.FireBelow.HasValue && c.Fire >= cond.FireBelow.Value) return false;
            if (cond.Water.HasValue && (c.Water > 0 && !c.Frozen) != cond.Water.Value) return false;
            if (cond.Frozen.HasValue && (c.Frozen && c.Water > 0) != cond.Frozen.Value) return false;
            if (cond.Gas != null && (cond.Gas == "any" ? c.Gas == null : c.Gas != cond.Gas)) return false;
            if (!string.IsNullOrEmpty(cond.TerrainTag) && !HasTag(c, cond.TerrainTag)) return false;
            if (!string.IsNullOrEmpty(cond.NotTerrainTag) && HasTag(c, cond.NotTerrainTag)) return false;
            if (cond.Conductive.HasValue && IsConductive(c) != cond.Conductive.Value) return false;
            return true;
        }

        /// <summary>
        /// Whether a rule's "if" holds (applies in the TS): the cell matches, the element came with
        /// at least its power, and its chance comes up, rolled last so the generator only turns
        /// once the rule would otherwise run.
        /// </summary>
        bool Applies(Cell c, CellCond cond, int power)
        {
            if (cond == null) return true;
            if (!Matches(c, cond)) return false;
            if (cond.Power.HasValue && power < cond.Power.Value) return false;
            return !cond.Chance.HasValue || Rand() < cond.Chance.Value;
        }

        /// <summary>Lays a cover (or with null clears it); one that lasts (thorns) starts its Cell.Life.</summary>
        void PutCover(Cell c, string coverId)
        {
            c.Cover = coverId;
            if (coverId != null && Rules.Covers.TryGetValue(coverId, out var cv) && cv.Lasts > 0) c.Life = cv.Lasts;
        }

        /// <summary>
        /// The landing square of the spell being applied (Land), for a grow effect's wave: what it
        /// grows ripens later the further it is from there. Null outside a landing.
        /// </summary>
        (int x, int y)? waveFrom;

        /// <summary>A grow effect's Cell.Life: a roll between its ticks, plus its wave per square from the landing.</summary>
        int GrowTicks(Effect eff, int x, int y)
        {
            int n = eff.TicksMin;
            if (eff.TicksMax > eff.TicksMin) n += (int)Math.Floor(Rand() * (eff.TicksMax - eff.TicksMin + 1));
            if (waveFrom.HasValue) n += eff.Wave * Math.Max(Math.Abs(x - waveFrom.Value.x), Math.Abs(y - waveFrom.Value.y));
            return Math.Max(1, n);
        }

        static bool Contains(IReadOnlyList<string> list, string s)
        {
            for (int i = 0; i < list.Count; i++) if (list[i] == s) return true;
            return false;
        }

        /// <summary>Lights the cell's flammable cover at the cover's burn intensity.</summary>
        public bool IgniteCover(Cell c)
        {
            if (!CoverBurns(c)) return false;
            var cv = Rules.Covers[c.Cover];
            int n = cv.BurnIntensity ?? 2;
            if (c.Fire >= n) return false;
            if (c.Fire == 0) c.Fuel = cv.Fuel ?? 5;
            c.Fire = n;
            return true;
        }

        /// <summary>Lights flammable terrain or cover at the given intensity; caught at <paramref name="dryness"/> (World.Dryness), it burns that much of its fuel.</summary>
        public bool Ignite(Cell c, int intensity, double dryness = 1)
        {
            var t = TerrainOf(c);
            bool coverFlammable = CoverBurns(c);
            if (!t.Burns && !coverFlammable) return false;
            if (c.Fire >= intensity) return false;
            if (c.Fire == 0) c.Fuel = DampFuel(coverFlammable ? (Rules.Covers[c.Cover].Fuel ?? 5) : (t.Fuel ?? 5), dryness);
            c.Fire = intensity;
            return true;
        }

        /// <summary>Fuel caught damp (soaked burns poorly, docs/MATERIALS.md): scaled by the dryness, rounded down, at least 1.</summary>
        static int DampFuel(int fuel, double dryness) => dryness >= 1 ? fuel : Math.Max(1, (int)Math.Floor(fuel * dryness));

        /// <summary>Moves water out of a cell that just became solid.</summary>
        public void DisplaceWater(int x, int y)
        {
            var c = At(x, y);
            int max = Rules.Sim.WaterMaxLevel;
            int guard = 16;
            while (c.Water > 0 && guard-- > 0)
            {
                bool moved = false;
                foreach (var (nx, ny, _) in ActiveNeighbours(x, y))
                {
                    var n = At(nx, ny);
                    if (c.Water == 0) break;
                    if (IsBlocking(n) || n.Water >= max) continue;
                    n.Water++;
                    c.Water--;
                    moved = true;
                }
                if (!moved) break;
            }
            c.Water = 0;
            c.Frozen = false;
        }

        bool Conduct(int x, int y, int charge)
        {
            if (!IsConductive(At(x, y))) return false;
            var seen = new HashSet<int> { Idx(x, y) };
            var queue = new Queue<(int x, int y)>();
            queue.Enqueue((x, y));
            int count = 0;
            while (queue.Count > 0 && count < Rules.Sim.ConductLimit)
            {
                var (cx, cy) = queue.Dequeue();
                var c = At(cx, cy);
                c.Charge = Math.Max(c.Charge, charge);
                if (CoverBurns(c)) IgniteCover(c);
                count++;
                foreach (var (nx, ny, _) in ActiveNeighbours(cx, cy))
                {
                    if (!seen.Add(Idx(nx, ny))) continue;
                    if (IsConductive(At(nx, ny))) queue.Enqueue((nx, ny));
                }
            }
            return true;
        }

        bool RunEffect(int x, int y, Cell c, Effect eff, int power, int dir)
        {
            int max = Rules.Sim.WaterMaxLevel;
            if (eff.HasIgnite) return eff.IgniteCover ? IgniteCover(c) : Ignite(c, eff.IgniteN, Dryness(c));
            if (eff.Boil.HasValue)
            {
                if (c.Water == 0 || c.Frozen) return false;
                c.Water--;
                AddGas(c, "steam", eff.Boil.Value);
                Mark(x, y, SteamMark);
                return true;
            }
            if (eff.Melt)
            {
                if (!c.Frozen) return false;
                c.Frozen = false;
                return true;
            }
            if (eff.Freeze)
            {
                if (c.Water == 0 || c.Frozen) return false;
                c.Frozen = true;
                return true;
            }
            if (eff.Extinguish)
            {
                if (c.Fire == 0) return false;
                c.Fire = 0;
                c.Fuel = 0;
                return true;
            }
            if (eff.Condense)
            {
                if (c.Gas != "steam") return false;
                c.GasAmt -= 2;
                if (c.GasAmt <= 0)
                {
                    c.Gas = null;
                    c.GasAmt = 0;
                }
                if (!IsBlocking(c) || HasTag(c, "vessel")) c.Water = Math.Min(max, c.Water + 2);
                return true;
            }
            if (eff.HasAddWater)
            {
                int n = eff.AddWaterPower ? power : eff.AddWaterN;
                if (c.Water >= max) return false;
                c.Water = Math.Min(max, c.Water + n);
                return true;
            }
            if (eff.HasSetCover)
            {
                if (c.Cover == eff.SetCover) return false;
                PutCover(c, eff.SetCover);
                return true;
            }
            if (eff.SpreadCover.HasValue)
            {
                if (c.Cover == null) return false;
                var options = new List<(int x, int y)>();
                foreach (var (nx, ny, _) in ActiveNeighbours(x, y))
                    if (!IsBlocking(At(nx, ny))) options.Add((nx, ny));
                for (int i = 0; i < eff.SpreadCover.Value && options.Count > 0; i++)
                {
                    int k = (int)Math.Floor(Rand() * options.Count);
                    var (nx, ny) = options[k];
                    options.RemoveAt(k);
                    var n = At(nx, ny);
                    PutCover(n, c.Cover);
                    if (c.Fire > 0) IgniteCover(n);
                }
                return true;
            }
            if (eff.Break)
            {
                var to = TerrainOf(c).BreaksTo;
                if (to == null) return false;
                c.T = to;
                return true;
            }
            if (eff.SetTerrain != null)
            {
                if (c.T == eff.SetTerrain) return false;
                DisplaceWater(x, y);
                c.T = eff.SetTerrain;
                return true;
            }
            if (eff.Raise != null) return RaiseUp(x, y, c, eff.Raise);
            if (eff.Conduct.HasValue) return Conduct(x, y, eff.Conduct.Value);
            if (eff.Fan)
            {
                if (dir < 0) return false;
                c.Fire = Math.Min(3, c.Fire + 1);
                int nx = x + DirX(dir), ny = y + DirY(dir);
                if (InBounds(nx, ny)) Apply(nx, ny, "fire", 2, -1, false);
                return true;
            }
            if (eff.PushGas)
            {
                if (c.Gas == null || dir < 0) return false;
                var n = At(x + DirX(dir), y + DirY(dir));
                if (n != null && IsActiveAt(x + DirX(dir), y + DirY(dir)) && !IsBlocking(n)) AddGas(n, c.Gas, c.GasAmt);
                c.Gas = null;
                c.GasAmt = 0;
                return true;
            }
            if (eff.ClearGas)
            {
                if (c.Gas == null) return false;
                c.Gas = null;
                c.GasAmt = 0;
                return true;
            }
            if (eff.Mark == "wind")
            {
                Mark(x, y, WindMark);
                return false;
            }
            if (eff.Gas != null)
            {
                AddGas(c, eff.Gas, eff.N ?? 1);
                return true;
            }
            if (eff.Grow != null)
            {
                // Nothing takes root on a wall of raised earth.
                if (IsBlocking(c)) return false;
                c.T = eff.Grow;
                c.Life = GrowTicks(eff, x, y);
                return true;
            }
            return false;
        }
    }
}
