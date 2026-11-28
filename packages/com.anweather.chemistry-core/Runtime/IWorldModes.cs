// What the world asks of the game modes that live above it (docs/design/architecture-review.md step 9, D9): the Hold,
// the defense field and the Gauntlet's course are Chem.Play's systems, which this assembly can't see. The world keeps
// their state (World.Play.cs) and calls them here, each at its old place: the tick's Rest stage after the raiders (the
// defense field, then the Hold), a combo's Moment, and the two commands. Every peer installs the same modes, and a
// world made from another (a joiner's) gets them from it through MadeFor, so the order of what runs never differs.
namespace Chem
{
    public interface IWorldModes
    {
        World World { get; }

        IWorldModes MadeFor(World world);

        void TickAfterRaiders();

        bool StartMoment(string id);

        void Apply(DefenseCommand command);

        void Apply(CourseCommand command);
    }
}
