// The game modes from outside the core (docs/design/architecture-review.md step 9): world.Play() is a world's
// PlayModes, and these are what the view, the probes, the tools and the runners ask of a mode, as they asked the world
// before the modes had their own assembly and their own state (world.Hold(), world.Defense(), ...). A world without
// the modes has no Hold, defense field, run or points: those reads say so (null, false, 0, everything unlocked), as they
// did when the world kept them.
using System;
using System.Collections.Generic;

namespace Chem
{
    public static class WorldPlay
    {
        public static PlayModes Play(this World world)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            return world.Mode<PlayModes>() ?? throw new InvalidOperationException("This world has no game modes: install them with PlayModes.Install");
        }

        static PlayModes Modes(World world) => (world ?? throw new ArgumentNullException(nameof(world))).Mode<PlayModes>();

        public static HoldState Hold(this World world) => Modes(world)?.Hold.State;

        public static WorldHold HoldSite(this World world) => Modes(world)?.Hold.Site;

        public static bool HoldRunning(this World world) => Modes(world)?.Hold.Running == true;

        public static int HoldLeft(this World world) => Modes(world)?.Hold.Left ?? 0;

        public static void SetHold(this World world, WorldHold site) => world.Play().Hold.Set(site);

        public static HoldTower TowerOf(this World world, string player) => Modes(world)?.Hold.TowerOf(player);

        public static List<(int x, int y)> TowerGround(this World world, HoldTower tower) => world.Play().Hold.TowerGround(tower);

        public static RunWizard RunOf(this World world, string player) => Modes(world)?.Hold.RunOf(player);

        public static int? PointsOf(this World world, string player) => Modes(world)?.Progress.PointsOf(player);

        public static IEnumerable<UnlockDef> UnlocksOf(this World world, string player) =>
            Modes(world)?.Progress.UnlocksOf(player) ?? ProgressSystem.Through(world.Rules.Sim.Hold.Unlocks, ProgressSystem.AllPoints);

        public static int PointsAway(this World world, string player, UnlockDef row) => Modes(world)?.Progress.PointsAway(player, row) ?? 0;

        public static UnlockDef NextUnlock(this World world, string player) => Modes(world)?.Progress.NextUnlock(player);

        public static DefenseState Defense(this World world) => Modes(world)?.Defense.State;

        public static int DefenseLeft(this World world) => Modes(world)?.Defense.Left ?? 0;

        public static (int x, int y)? CourseRespawn(this World world, string player, int x, int y) => world.Play().Course.Respawn(player, x, y);

        public static (WavePlan plan, int ticks)? HoldWarning(this World world) => world.Play().Hold.Warning;

        public static (int x, int y)? HoldRespawn(this World world) => world.Play().Hold.Respawn();

        public static int? HoldNextWaveIn(this World world) => world.Play().Hold.NextWaveIn;

        public static (int x, int y)? HoldNext(this World world, int x, int y, bool road) => world.Play().Hold.Next(x, y, road);

        public static int PathCostAt(this World world, int x, int y) => world.Play().Defense.PathCostAt(x, y);
    }
}
