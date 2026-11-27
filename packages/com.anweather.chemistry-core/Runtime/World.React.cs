// The rule interpreter: an element arrives at a cell (from a spell or
// spreading fire) and reactions.json decides what happens. Port of the cell
// half of sandbox/src/react.ts.
using System;
using System.Collections.Generic;

namespace Chem
{
    public sealed partial class World
    {
        public void Apply(int x, int y, string element, int power = 1, int dir = -1, bool direct = true)
        {
            var c = LocateActive(x, y, out var ch, out int i);
            if (c == null) return;
            if (direct && element == "fire") ch.FireGeneration[i] = 0;
            if (element == "fire") FlashGas(x, y);
            string beforeCover = CoverName(c), beforeTerrain = TerrainOf(c).Name.ToLowerInvariant();
            foreach (var rule in Rules.CellRulesFor(element))
            {
                if (!Applies(c, rule.If, power)) continue;
                bool changed = rule.Do.Count == 0;
                foreach (var eff in rule.Do) changed = RunEffect(x, y, c, eff, power, dir, element) || changed;
                if (changed && rule.Log != null && (direct || rule.Notable))
                {
                    var text = ReplaceFirst(ReplaceFirst(rule.Log, "{cover}", beforeCover), "{terrain}", beforeTerrain);
                    LogOnce(rule.Id, text, rule.Notable ? LogKind.Notable : LogKind.Info);
                }
                if (rule.Stop) break;
            }
            ReachRaider(x, y, element, direct);
        }

        void ReachRaider(int x, int y, string element, bool direct)
        {
            var raider = RaiderSystem.List.Count > 0 ? RaiderAt(x, y) : null;
            if (raider != null) RaiderSystem.ApplyRaiderElement(raider, element, direct);
        }

        bool RaiseUp(int x, int y, Cell c, string terrain)
        {
            if (RaiderAt(x, y) != null) return false;
            DisplaceWater(x, y);
            c.T = terrain;
            c.Cover = null;
            c.Fire = 0;
            return true;
        }

        bool Dissolve(Cell c, string what, string element)
        {
            if (what == "cover")
            {
                if (c.Cover == null || !Rules.Covers.TryGetValue(c.Cover, out var cv) || cv.DissolvedBy != element) return false;
                PutCover(c, cv.DissolvesTo);
                return true;
            }
            var t = TerrainOf(c);
            if (t.DissolvedBy != element || t.DissolvesTo == null) return false;
            c.T = t.DissolvesTo;
            return true;
        }

        bool Struck(int x, int y, Cell c)
        {
            int was = c.Cracked;
            return Shatter(x, y) || c.Cracked != was;
        }

        bool Scour(int x, int y, Cell c)
        {
            if (c.Cover != null && CoverHasTag(c, "scourable"))
            {
                PutCover(c, null);
                return true;
            }
            var t = TerrainOf(c);
            if (t.ScoursTo != null)
            {
                bool wall = IsBlocking(c);
                c.T = t.ScoursTo;
                if (wall && !IsBlocking(c)) WallLost(x, y);
                return true;
            }
            return t.ScourWears && c.Height > 0 && Reshape(x, y, -1, null, int.MaxValue);
        }

        bool PushCover(int x, int y, Cell c, int dir)
        {
            if (c.Cover == null || !Rules.Covers.TryGetValue(c.Cover, out var cv) || cv.FloatEvery <= 0) return false;
            int nx = x + DirX(dir), ny = y + DirY(dir);
            var n = LocateActive(nx, ny, out _, out _);
            if (n == null || IsBlocking(n) || n.Cover != null || n.Water == 0 || n.Frozen || !LiquidOf(n).Floats) return false;
            n.Cover = c.Cover;
            n.Slick = c.Slick;
            n.Fire = c.Fire;
            n.Fuel = c.Fuel;
            c.Cover = null;
            c.Slick = 0;
            c.Fire = 0;
            c.Fuel = 0;
            return true;
        }

        bool Bake(Cell c)
        {
            var to = TerrainOf(c).BakesTo;
            if (to == null) return false;
            c.T = to;
            return true;
        }

        bool MeltCover(Cell c)
        {
            if (c.Cover == null || !Rules.Covers.TryGetValue(c.Cover, out var cv) || cv.MeltWater <= 0) return false;
            PutCover(c, cv.MeltsTo);
            Melted(c, cv.MeltWater);
            return true;
        }

        void Melted(Cell c, int units)
        {
            if (!TakesWater(c)) return;
            c.Water = Math.Min(Rules.Sim.WaterMaxLevel, c.Water + units);
            c.Liquid = 0;
        }

        bool Crust(Cell c)
        {
            var t = TerrainOf(c);
            if (t.CrustsTo == null) return false;
            c.T = t.CrustsTo;
            c.Life = t.CrustFor;
            return true;
        }

        bool Thaw(int x, int y, Cell c)
        {
            var t = TerrainOf(c);
            if (t.ThawsTo == null) return false;
            c.T = t.ThawsTo;
            Melted(c, t.ThawWater);
            WallLost(x, y);
            return true;
        }

        bool CoverMelts(Cell c) => c.Cover != null && Rules.Covers.TryGetValue(c.Cover, out var cv) && cv.MeltWater > 0;

        string CoverName(Cell c) =>
            c.Cover != null && Rules.Covers.TryGetValue(c.Cover, out var cv) ? cv.Name.ToLowerInvariant() : "";

        internal static string ReplaceFirst(string s, string find, string with)
        {
            int i = s.IndexOf(find, StringComparison.Ordinal);
            return i < 0 ? s : s.Substring(0, i) + with + s.Substring(i + find.Length);
        }

        bool Matches(Cell c, CellCond cond) =>
            cond == null || (MatchesCoverAndFire(c, cond) && MatchesWater(c, cond) && MatchesGround(c, cond));

        static bool MatchesCoverAndFire(Cell c, CellCond cond)
        {
            if (cond.Cover != null && !(c.Cover != null && Contains(cond.Cover, c.Cover))) return false;
            if (cond.FireFlag == true && c.Fire == 0) return false;
            if (cond.FireFlag == false && c.Fire > 0) return false;
            if (cond.FireMin.HasValue && c.Fire < cond.FireMin.Value) return false;
            return !(cond.FireBelow.HasValue && c.Fire >= cond.FireBelow.Value);
        }

        bool MatchesWater(Cell c, CellCond cond)
        {
            if (cond.Water.HasValue && IsOpenWater(c) != cond.Water.Value) return false;
            if (cond.Liquid != null && !(c.Water > 0 && !c.Frozen && Rules.LiquidIs(c.Liquid, cond.Liquid))) return false;
            if (cond.Frozen.HasValue && (c.Frozen && c.Water > 0) != cond.Frozen.Value) return false;
            if (cond.Freezes.HasValue && (LiquidFreezes(c) != null) != cond.Freezes.Value) return false;
            if (cond.Melts.HasValue && CoverMelts(c) != cond.Melts.Value) return false;
            return !(cond.Gas != null && (cond.Gas == "any" ? c.Gas == null : c.Gas != cond.Gas));
        }

        bool MatchesGround(Cell c, CellCond cond)
        {
            if (!string.IsNullOrEmpty(cond.TerrainTag) && !HasTag(c, cond.TerrainTag)) return false;
            if (!string.IsNullOrEmpty(cond.NotTerrainTag) && HasTag(c, cond.NotTerrainTag)) return false;
            if (cond.Conductive.HasValue && IsConductive(c) != cond.Conductive.Value) return false;
            return !(cond.Hot.HasValue && (c.Heat > 0) != cond.Hot.Value);
        }

        bool Applies(Cell c, CellCond cond, int power)
        {
            if (cond == null) return true;
            if (!Matches(c, cond)) return false;
            if (cond.Power.HasValue && power < cond.Power.Value) return false;
            return !cond.Chance.HasValue || Rand() < cond.Chance.Value;
        }

        void PutCover(Cell c, string coverId)
        {
            c.Cover = coverId;
            if (coverId != null && Rules.Covers.TryGetValue(coverId, out var cv) && cv.Lasts > 0) c.Life = cv.Lasts;
        }

        (int x, int y)? waveFrom;

        int GrowTicks(Effect eff, int x, int y)
        {
            int n = eff.TicksMin;
            if (eff.TicksMax > eff.TicksMin) n += (int)Math.Floor(Rand() * (eff.TicksMax - eff.TicksMin + 1));
            if (waveFrom.HasValue) n += eff.Wave * Math.Max(Math.Abs(DeltaX(waveFrom.Value.x, x)), Math.Abs(DeltaY(waveFrom.Value.y, y)));
            return Math.Max(1, n);
        }

        internal static bool Contains(IReadOnlyList<string> list, string s)
        {
            for (int i = 0; i < list.Count; i++) if (list[i] == s) return true;
            return false;
        }

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

        internal static int DampFuel(int fuel, double dryness) => dryness >= 1 ? fuel : Math.Max(1, (int)Math.Floor(fuel * dryness));

        public void DisplaceWater(int x, int y)
        {
            var c = At(x, y);
            int max = Rules.Sim.WaterMaxLevel;
            int guard = 16, kind = c.Liquid;
            while (c.Water > 0 && guard-- > 0)
            {
                bool moved = false;
                foreach (var (nx, ny, _) in ActiveNeighbours(x, y))
                {
                    var n = At(nx, ny);
                    if (c.Water == 0) break;
                    if (IsBlocking(n) || n.Water >= max || (n.Water > 0 && n.Liquid != kind)) continue;
                    Pushed(c, n, kind);
                    n.Water++;
                    n.Liquid = kind;
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
            var seen = new HashSet<long> { CellKey(x, y) };
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
                    if (!seen.Add(CellKey(nx, ny))) continue;
                    if (IsConductive(At(nx, ny))) queue.Enqueue((nx, ny));
                }
            }
            return true;
        }

        bool ShoveGas(int x, int y, int dir)
        {
            var c = At(x, y);
            int nx = x + DirX(dir), ny = y + DirY(dir);
            var n = At(nx, ny);
            if (n == null || !IsActiveAt(nx, ny) || IsBlocking(n)) return false;
            if (n.Gas != null && n.Gas != c.Gas && !ShoveGas(nx, ny, dir)) return false;
            AddGas(n, c.Gas, c.GasAmt);
            c.Gas = null;
            c.GasAmt = 0;
            return true;
        }

        bool RunEffect(int x, int y, Cell c, Effect eff, int power, int dir, string element)
        {
            if (TryFireOrWaterEffect(x, y, c, eff, power, out bool changed)) return changed;
            if (TryMaterialEffect(x, y, c, eff, dir, element, out changed)) return changed;
            return RunTerrainOrAirEffect(x, y, c, eff, dir);
        }

        bool TryFireOrWaterEffect(int x, int y, Cell c, Effect eff, int power, out bool changed)
        {
            if (eff.HasIgnite) changed = eff.IgniteCover ? IgniteCover(c) : Ignite(c, eff.IgniteN, Dryness(c));
            else if (eff.Boil.HasValue) changed = BoilOff(x, y, c, eff.Boil.Value);
            else if (eff.Melt) changed = MeltIce(c);
            else if (eff.Freeze) changed = FreezeWater(x, y, c);
            else if (eff.Extinguish) changed = Douse(c);
            else if (eff.Condense) changed = CondenseSteam(c);
            else if (eff.HasAddWater) changed = PourWater(c, eff.AddWaterPower ? power : eff.AddWaterN, eff.Liquid);
            else
            {
                changed = false;
                return false;
            }
            return true;
        }

        bool TryMaterialEffect(int x, int y, Cell c, Effect eff, int dir, string element, out bool changed)
        {
            if (eff.HasSetCover) changed = LayCover(c, eff.SetCover);
            else if (eff.SpreadCover.HasValue) changed = SpreadCoverOut(x, y, c, eff.SpreadCover.Value);
            else if (eff.Dissolve != null) changed = Dissolve(c, eff.Dissolve, element);
            else if (eff.Heat) changed = HeatUp(c);
            else if (eff.Shatter) changed = Struck(x, y, c);
            else if (eff.Scour) changed = Scour(x, y, c);
            else if (eff.PushCover) changed = dir >= 0 && PushCover(x, y, c, dir);
            else if (eff.Bake) changed = Bake(c);
            else if (eff.MeltCover) changed = MeltCover(c);
            else if (eff.Crust) changed = Crust(c);
            else if (eff.Thaw) changed = Thaw(x, y, c);
            else
            {
                changed = false;
                return false;
            }
            return true;
        }

        bool RunTerrainOrAirEffect(int x, int y, Cell c, Effect eff, int dir)
        {
            if (eff.Break) return BreakWall(x, y, c);
            if (eff.SetTerrain != null) return SetTerrainTo(x, y, c, eff.SetTerrain);
            if (eff.Raise != null) return RaiseUp(x, y, c, eff.Raise);
            if (eff.Conduct.HasValue) return Conduct(x, y, eff.Conduct.Value);
            if (eff.Fan) return FanFire(x, y, c, dir);
            if (eff.PushGas) return c.Gas != null && dir >= 0 && ShoveGas(x, y, dir);
            if (eff.ClearGas) return ClearGas(c);
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
            return eff.Grow != null && GrowPlant(x, y, c, eff);
        }

        bool BoilOff(int x, int y, Cell c, int steam)
        {
            var l = LiquidOf(c);
            if (c.Water == 0 || c.Frozen || (c.Liquid != 0 && l.Boils == null)) return false;
            c.Water--;
            AddGas(c, BoilsOf(l), steam);
            Mark(x, y, SteamMark);
            return true;
        }

        static bool MeltIce(Cell c)
        {
            if (!c.Frozen) return false;
            c.Frozen = false;
            return true;
        }

        bool FreezeWater(int x, int y, Cell c)
        {
            if (HoldsOther(c)) return Locate(x, y, out var ch, out _) != null && FreezeLiquid(ch, c);
            if (c.Water == 0 || c.Frozen) return false;
            c.Frozen = true;
            return true;
        }

        static bool Douse(Cell c)
        {
            if (c.Fire == 0) return false;
            c.Fire = 0;
            c.Fuel = 0;
            return true;
        }

        bool CondenseSteam(Cell c)
        {
            if (c.Gas == null || c.Gas != Rules.Sim.WaterBoils) return false;
            c.GasAmt -= 2;
            if (c.GasAmt <= 0)
            {
                c.Gas = null;
                c.GasAmt = 0;
            }
            if ((!IsBlocking(c) || HasTag(c, "vessel")) && TakesWater(c))
            {
                c.Water = Math.Min(Rules.Sim.WaterMaxLevel, c.Water + 2);
                c.Liquid = 0;
            }
            return true;
        }

        bool PourWater(Cell c, int n, string liquid)
        {
            int kind = liquid == null ? 0 : Rules.LiquidIndex(liquid);
            if (kind < 0 || !Pour(c, kind, n, Rules.Sim.WaterMaxLevel)) return false;
            if (kind != 0) Fresh(c, Rules.Liquid(kind));
            return true;
        }

        bool LayCover(Cell c, string coverId)
        {
            if (c.Cover == coverId) return false;
            string laid = LaidOver(c, coverId);
            if (laid == null && coverId != null) return false;
            PutCover(c, laid);
            return true;
        }

        bool SpreadCoverOut(int x, int y, Cell c, int count)
        {
            if (c.Cover == null) return false;
            var options = new List<(int x, int y)>();
            foreach (var (nx, ny, _) in ActiveNeighbours(x, y))
                if (!IsBlocking(At(nx, ny))) options.Add((nx, ny));
            for (int i = 0; i < count && options.Count > 0; i++)
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

        bool HeatUp(Cell c)
        {
            int hot = TerrainOf(c).HotFor;
            if (hot <= 0 || c.Heat >= hot) return false;
            c.Heat = hot;
            return true;
        }

        internal bool BreakWall(int x, int y, Cell c)
        {
            var to = TerrainOf(c).BreaksTo;
            if (to == null) return false;
            c.T = to;
            WallLost(x, y);
            return true;
        }

        internal bool SetTerrainTo(int x, int y, Cell c, string terrain)
        {
            if (c.T == terrain) return false;
            DisplaceWater(x, y);
            c.T = terrain;
            return true;
        }

        bool FanFire(int x, int y, Cell c, int dir)
        {
            if (dir < 0) return false;
            c.Fire = Math.Min(3, c.Fire + 1);
            int nx = x + DirX(dir), ny = y + DirY(dir);
            if (InBounds(nx, ny)) Apply(nx, ny, "fire", 2, -1, false);
            return true;
        }

        static bool ClearGas(Cell c)
        {
            if (c.Gas == null) return false;
            c.Gas = null;
            c.GasAmt = 0;
            return true;
        }

        bool GrowPlant(int x, int y, Cell c, Effect eff)
        {
            if (IsBlocking(c)) return false;
            c.T = eff.Grow;
            c.Life = GrowTicks(eff, x, y);
            return true;
        }
    }
}
