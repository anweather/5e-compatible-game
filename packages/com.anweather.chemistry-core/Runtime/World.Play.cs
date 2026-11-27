// The game modes' systems, which the world owns (docs/design/architecture-review.md step 9, D9): each is a class in
// Play/ over the world's data, made with the world and run at the place in the tick its mode always had. What's left
// here are the thin members code outside the core still calls on the world; they forward to a system.
namespace Chem
{
    public sealed partial class World
    {
        /// <summary>The spell zones and the course's runs (Play/CourseSystem.cs).</summary>
        public CourseSystem CourseSystem { get; private set; }

        // Made in both constructors (World.cs, OpenWorld.cs), before the chunks: none of them draws from the world's generator.
        void MakeSystems()
        {
            CourseSystem = new CourseSystem(this);
        }

        /// <summary>Where a wizard downed at (x, y) gets up (CourseSystem.Respawn); for WizardDown.</summary>
        public (int x, int y)? CourseRespawn(string player, int x, int y) => CourseSystem.Respawn(player, x, y);
    }
}
