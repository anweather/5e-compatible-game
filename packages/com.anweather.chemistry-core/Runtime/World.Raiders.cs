// The raiders and the dragon on the world (docs/design/architecture-review.md step 9, D9): systems the world owns and
// runs itself, since spells, strikes, reactions, acts, the change audit and the save all reach them. These are their
// handles and what the view and the modes read of them; RaiderSystem.cs and DragonSystem.cs are the systems.
namespace Chem
{
    public sealed partial class World
    {
        public RaiderSystem RaiderSystem { get; private set; }

        public const string ShovedStatus = RaiderSystem.ShovedStatus, ThrownStatus = RaiderSystem.ThrownStatus;

        public DragonSystem DragonSystem { get; private set; }

        public const string ClimbRoute = "climb", FlyRoute = "fly";

        internal const string LandedStatus = "landed";

        void MakeSystems()
        {
            RaiderSystem = new RaiderSystem(this);
            DragonSystem = new DragonSystem(this);
        }

        public System.Collections.Generic.IReadOnlyList<Raider> Raiders => RaiderSystem.Raiders;

        public Raider RaiderAt(int x, int y) => RaiderSystem.RaiderAt(x, y);

        public Raider SpawnRaider(string kind, int x, int y, int hp) => RaiderSystem.SpawnRaider(kind, x, y, hp);

        public bool Flying(Raider r) => Flies(r, Rules.Entity(r.Kind));

        internal static bool Flies(Raider r, EntityDef def) => def.Raider?.Route == FlyRoute && r.StatusOf(LandedStatus) == 0;

        public DragonState Dragon => DragonSystem.State;

        public int DragonFlights => DragonSystem.Flights;

        public void ApplyDragon(DragonCommand command) => DragonSystem.Apply(command);

        public (int middle, bool back) PassLine(DragonState flight, int pass) => DragonSystem.PassLine(flight, pass);

        public bool BreathBox(out int x0, out int y0, out int x1, out int y1) => DragonSystem.BreathBox(out x0, out y0, out x1, out y1);

        public bool CaughtByBreath(int x, int y) => DragonSystem.CaughtByBreath(x, y);

        public bool ShelteredFromBreath(int x, int y) => DragonSystem.ShelteredFromBreath(x, y);
    }
}
