// The game modes a world plays (docs/design/architecture-review.md step 9, D9): the defense field, the Hold and the
// Gauntlet's course, Chem.Play's systems over the world's state, and the cost field the first two work out their ways
// in. The world can't see this assembly; it calls these through IWorldModes, at the places its tick and commands always
// had them. Install puts them on a world once, before it ticks or loads what a mode does: every place that makes a
// world to play (the view, the runners, the tools, a peer) installs them, and a joiner's world takes them from the one
// it replaces (World.InstallModesOf). World.Play() is how code outside the core reaches them.
using System;

namespace Chem
{
    public sealed class PlayModes : IWorldModes
    {
        readonly WayField ways = new WayField();

        PlayModes(World world)
        {
            World = world ?? throw new ArgumentNullException(nameof(world));
            Course = new CourseSystem(world);
            Defense = new DefenseSystem(world, ways);
            Hold = new HoldSystem(world, ways, Defense);
        }

        public static World Install(World world)
        {
            world.Install(new PlayModes(world));
            return world;
        }

        public World World { get; }

        public CourseSystem Course { get; }

        public DefenseSystem Defense { get; }

        public HoldSystem Hold { get; }

        IWorldModes IWorldModes.MadeFor(World world) => new PlayModes(world);

        void IWorldModes.TickAfterRaiders()
        {
            Defense.Tick();
            Hold.Tick();
        }

        bool IWorldModes.StartMoment(string id) => Hold.StartMoment(id);

        void IWorldModes.Apply(DefenseCommand command) => Defense.Apply(command);

        void IWorldModes.Apply(CourseCommand command) => Course.Apply(command);
    }
}
