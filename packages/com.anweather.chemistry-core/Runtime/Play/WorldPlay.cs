// The game modes from outside the core (docs/design/architecture-review.md step 9): world.Play() is a world's
// PlayModes, and these are what the view, the probes, the tools and the runners ask of a mode, as they asked the world
// before the modes had their own assembly. The modes' state is the world's own (World.Hold, World.Defense, ...).
using System;

namespace Chem
{
    public static class WorldPlay
    {
        public static PlayModes Play(this World world)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            return world.Modes as PlayModes ?? throw new InvalidOperationException("This world has no game modes: install them with PlayModes.Install");
        }

        public static (int x, int y)? CourseRespawn(this World world, string player, int x, int y) => world.Play().Course.Respawn(player, x, y);

        public static (WavePlan plan, int ticks)? HoldWarning(this World world) => world.Play().Hold.Warning;

        public static (int x, int y)? HoldRespawn(this World world) => world.Play().Hold.Respawn();

        public static int? HoldNextWaveIn(this World world) => world.Play().Hold.NextWaveIn;

        public static (int x, int y)? HoldNext(this World world, int x, int y, bool road) => world.Play().Hold.Next(x, y, road);

        public static int PathCostAt(this World world, int x, int y) => world.Play().Defense.PathCostAt(x, y);
    }
}
