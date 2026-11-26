// What rolls cell by cell along a cast's aim (the wave, World.Waves.cs; the boulder, World.Boulders.cs).
// Port of the movers in sandbox/src/actions.ts.

namespace Chem
{
    public sealed partial class World
    {
        public abstract class Mover
        {
            public int X, Y, Dir, OX, OY, AX, AY, Step;

            public void SetOff(int x, int y, int dir, int aimX, int aimY)
            {
                (aimX, aimY) = SnapAim(aimX, aimY);
                if (aimX == 0 && aimY == 0) (aimX, aimY) = (DirX(dir), DirY(dir));
                (X, Y, OX, OY, AX, AY, Step) = (x, y, x, y, aimX, aimY, 0);
                Dir = DirOf(aimX, aimY, dir);
            }

            public (int x, int y) Ahead()
            {
                int major = System.Math.Max(System.Math.Abs(AX), System.Math.Abs(AY));
                if (major == 0) return (X, Y);
                return (OX + RoundDiv((Step + 1) * AX, major), OY + RoundDiv((Step + 1) * AY, major));
            }

            public void Advance()
            {
                (X, Y) = Ahead();
                Step++;
            }

            public void Turn(int dir) => SetOff(X + DirX(dir), Y + DirY(dir), dir, DirX(dir), DirY(dir));
        }
    }
}
