// The world's modes and their bodies (D22, docs/design/minigames.md). The installed modes, in install order, and the
// places the world calls them: their slots in the save, the casts checksum text and the dragon's start; a map laid; a
// chunk the dragon put back; a spell striking or landing on a body; acts of a kind the world doesn't know. A world
// loads before its modes are installed, so it keeps the save it read until the first tick and gives each mode its
// part as it's installed. The bodies' step is golf's ball's, line for line from sandbox/src/golf.ts: each tick, after
// the boulders, a moving body of each mode (modes in install order, a mode's bodies in its order) moves a cell when its
// time comes. In the air it carries over water and hills and turns back off what blocks it; on the ground it rolls
// on, paying a cell and the climb, getting the drop back, turns back off rock (a cell of roll lost) and runs on
// downhill when it slows. It keeps to active cells in bounds that its mode lets it into. All whole numbers.
using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Chem
{
    public sealed partial class World
    {
        readonly List<WorldMode> installed = new List<WorldMode>();
        JObject unclaimed;

        public IReadOnlyList<WorldMode> InstalledModes => installed;

        public T Mode<T>() where T : WorldMode
        {
            foreach (var m in installed)
                if (m is T found) return found;
            return null;
        }

        public void Install(WorldMode mode)
        {
            if (mode == null) throw new ArgumentNullException(nameof(mode));
            if (mode.World != this) throw new ArgumentException("Those modes were made for another world", nameof(mode));
            foreach (var m in installed)
                if (m.GetType() == mode.GetType()) throw new InvalidOperationException($"This world already has its {mode.GetType().Name}");
            installed.Add(mode);
            mode.LoadSlot(unclaimed);
        }

        public void InstallModesOf(World other)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));
            foreach (var m in other.installed) Install(m.MadeFor(this));
        }

        void ModesStepping() => unclaimed = null;

        void SaveModes(JObject save)
        {
            foreach (var m in installed) m.SaveSlot(save);
        }

        string ModesText()
        {
            if (installed.Count == 0) return "";
            var sb = new System.Text.StringBuilder();
            foreach (var m in installed) sb.Append(m.SlotText());
            return sb.ToString();
        }

        void KeepModesStart()
        {
            foreach (var m in installed) m.KeepStart();
        }

        void SaveModesStart(JObject start)
        {
            foreach (var m in installed) m.SaveStart(start);
        }

        void ResetModesChunk(Chunk ch)
        {
            foreach (var m in installed) m.ChunkReset(ch.X, ch.Y);
        }

        public void MapLaid(string dir)
        {
            foreach (var m in installed) m.MapLaid(dir);
        }

        WorldMode ModeOfAct(string kind, string what)
        {
            foreach (var m in installed)
                if (m.KnowsAct(kind, what)) return m;
            return null;
        }

        string ModesActStays(string kind)
        {
            foreach (var m in installed)
                if (m.ActStays(kind) is string why) return why;
            return null;
        }

        internal void BodiesStruck(SpellDef s, int x, int y, int dir)
        {
            foreach (var m in installed)
                foreach (var b in m.Bodies)
                    if (b.X == x && b.Y == y) m.SpellStruck(b, s, dir);
        }

        void BodiesLanded(SpellDef s, List<(int x, int y)> cells, (int x, int y) landed, int dir, int charge)
        {
            foreach (var m in installed)
                foreach (var b in m.Bodies)
                    if (cells.Contains((b.X, b.Y))) m.SpellLanded(b, s, cells, landed, dir, charge);
        }

        void TickBodies()
        {
            foreach (var m in installed)
            {
                var bodies = m.Bodies;
                for (int i = 0; i < bodies.Count; i++)
                {
                    var b = bodies[i];
                    if (!b.Moving || !m.BodyMoves(b) || ++b.Age % m.BodyEvery(b) != 0) continue;
                    if (b.Carry > 0 ? Fly(m, b) : RollBody(m, b)) m.BodyStopped(b);
                }
            }
        }

        bool Fly(WorldMode m, Body b)
        {
            int nx = b.X + DirX(b.Dir), ny = b.Y + DirY(b.Dir);
            if (!MayEnter(m, b, nx, ny))
            {
                m.BodyLeft(b);
                return false;
            }
            if (IsBlocking(At(nx, ny))) b.Dir = (b.Dir + 2) % 4;
            else
            {
                b.X = FoldX(nx);
                b.Y = FoldY(ny);
            }
            if (--b.Carry != 0) return false;
            return !m.BodyArrived(b) && !b.Moving;
        }

        bool RollBody(WorldMode m, Body b)
        {
            int nx = b.X + DirX(b.Dir), ny = b.Y + DirY(b.Dir);
            if (!MayEnter(m, b, nx, ny))
            {
                m.BodyLeft(b);
                return false;
            }
            if (IsBlocking(At(nx, ny)))
            {
                b.Dir = (b.Dir + 2) % 4;
                b.Roll--;
                m.BodyBounced(b, nx, ny);
            }
            else
            {
                int step = Rules.Sim.WaterUnitsPerStep, rise = GroundUnits(nx, ny) - GroundUnits(b.X, b.Y);
                int cost = rise > 0 ? 1 + (rise + step - 1) / step : 1;
                if (cost > b.Roll) b.Roll = 0;
                else
                {
                    b.X = FoldX(nx);
                    b.Y = FoldY(ny);
                    b.Roll += -cost + (rise < 0 ? -rise / step : 0);
                    if (m.BodyArrived(b)) return false;
                }
            }
            if (b.Roll <= 0) Downhill(m, b);
            return !b.Moving;
        }

        void Downhill(WorldMode m, Body b)
        {
            b.Roll = 0;
            int best = -1, bestLevel = GroundUnits(b.X, b.Y);
            for (int dir = 0; dir < 4; dir++)
            {
                int nx = b.X + DirX(dir), ny = b.Y + DirY(dir);
                if (!MayEnter(m, b, nx, ny) || IsBlocking(At(nx, ny))) continue;
                int level = GroundUnits(nx, ny);
                if (level >= bestLevel) continue;
                best = dir;
                bestLevel = level;
            }
            if (best < 0) return;
            b.Dir = best;
            b.Roll = 1;
        }

        bool MayEnter(WorldMode m, Body b, int x, int y) => InBounds(x, y) && IsActiveAt(x, y) && m.BodyMayEnter(b, x, y);
    }
}
