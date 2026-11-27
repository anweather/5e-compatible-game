// The game modes' systems, which the world owns (docs/design/architecture-review.md step 9, D9): each is a class in
// Play/ over the world's data, made with the world and run at the place in the tick its mode always had. What's left
// here are the thin members code outside the core still calls on the world; they forward to a system.
namespace Chem
{
    public sealed partial class World
    {
        /// <summary>The spell zones and the course's runs (Play/CourseSystem.cs).</summary>
        public CourseSystem CourseSystem { get; private set; }

        /// <summary>The defense field's waves (Play/DefenseSystem.cs).</summary>
        public DefenseSystem DefenseSystem { get; private set; }

        // The raiders' cost field's scratch (Play/WayField.cs), shared by the defense field's march and the Hold's ways.
        internal readonly WayField Ways = new WayField();

        // Made in both constructors (World.cs, OpenWorld.cs), before the chunks: none of them draws from the world's generator.
        void MakeSystems()
        {
            CourseSystem = new CourseSystem(this);
            DefenseSystem = new DefenseSystem(this);
        }

        /// <summary>Where a wizard downed at (x, y) gets up (CourseSystem.Respawn); for WizardDown.</summary>
        public (int x, int y)? CourseRespawn(string player, int x, int y) => CourseSystem.Respawn(player, x, y);

        /// <summary>The defense field's waves, or null before anyone started them (DefenseSystem.State); for DefenseField and the probes.</summary>
        public DefenseState Defense => DefenseSystem.State;

        /// <summary>Raiders still to beat in the wave under way (DefenseSystem.Left); for DefenseField.</summary>
        public int DefenseLeft => DefenseSystem.Left;

        /// <summary>Runs a defense command (DefenseSystem.Apply); for DefenseField.</summary>
        public void ApplyDefense(DefenseCommand command) => DefenseSystem.Apply(command);

        /// <summary>The cheapest way's cost from a square to the flag (DefenseSystem.PathCostAt); for ProbeDefense.</summary>
        public int PathCostAt(int x, int y) => DefenseSystem.PathCostAt(x, y);
    }
}
