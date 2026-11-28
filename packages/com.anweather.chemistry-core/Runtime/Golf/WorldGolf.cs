// Golf from outside its mode (D22): what the view, the probes, the tools and the tests ask of a world's golf, as they
// asked the world before golf was a mode of its own. A world without golf installed has no hole: Golf() is null and
// ApplyGolf throws.
using System;

namespace Chem
{
    public static class WorldGolf
    {
        public static GolfState Golf(this World world) => GolfMode.Of(world)?.State;

        public static void ApplyGolf(this World world, GolfCommand command) => Mode(world).Apply(command);

        public static bool GolfWaiting(this World world, GolfState g) => Mode(world).Waiting(g);

        static GolfMode Mode(World world) =>
            GolfMode.Of(world) ?? throw new InvalidOperationException("This world doesn't play golf: install it with GolfMode.Install");
    }
}
