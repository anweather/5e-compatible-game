// What rolls cell by cell along a cast's aim (the wave, World.Waves.cs; the boulder, World.Boulders.cs).
// Port of the movers in sandbox/src/actions.ts.

namespace Chem
{
    public sealed partial class World
    {
        /// <summary>
        /// Something rolling along an aim: the cell it's on, the grid way nearest its aim (what its force pushes along),
        /// and the line it follows: from (OX, OY) along the whole-number aim (AX, AY), Step cells so far. Each step is
        /// a cell along the aim's longer part and the shorter part's share rounded, as a slanted line is (SlantCells),
        /// so it rolls the way it was aimed at any angle, in whole numbers.
        /// </summary>
        public abstract class Mover
        {
            public int X, Y, Dir, OX, OY, AX, AY, Step;

            /// <summary>Sets it off from (x, y) along the aim (SnapAim), or along <paramref name="dir"/> without one.</summary>
            public void SetOff(int x, int y, int dir, int aimX, int aimY)
            {
                (aimX, aimY) = SnapAim(aimX, aimY);
                if (aimX == 0 && aimY == 0) (aimX, aimY) = (DirX(dir), DirY(dir));
                (X, Y, OX, OY, AX, AY, Step) = (x, y, x, y, aimX, aimY, 0);
                Dir = DirOf(aimX, aimY, dir);
            }

            /// <summary>The next cell along its line.</summary>
            public (int x, int y) Ahead()
            {
                int major = System.Math.Max(System.Math.Abs(AX), System.Math.Abs(AY));
                if (major == 0) return (X, Y);
                return (OX + RoundDiv((Step + 1) * AX, major), OY + RoundDiv((Step + 1) * AY, major));
            }

            /// <summary>Rolls on to the next cell along its line.</summary>
            public void Advance()
            {
                (X, Y) = Ahead();
                Step++;
            }

            /// <summary>Turns onto a grid way (a boulder down a slope) and rolls a cell along it, its new line.</summary>
            public void Turn(int dir) => SetOff(X + DirX(dir), Y + DirY(dir), dir, DirX(dir), DirY(dir));
        }
    }
}
