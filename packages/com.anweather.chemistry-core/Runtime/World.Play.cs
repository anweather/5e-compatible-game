// The game modes' systems, which the world owns (docs/design/architecture-review.md step 9, D9): each is a class in
// Play/ over the world's data, made with the world and run at the place in the tick its mode always had. What's left
// here are the thin members code outside the core still calls on the world; they forward to a system.
using System.Collections.Generic;

namespace Chem
{
    public sealed partial class World
    {
        public RaiderSystem RaiderSystem { get; private set; }

        public const string ShovedStatus = RaiderSystem.ShovedStatus, ThrownStatus = RaiderSystem.ThrownStatus;

        public DragonSystem DragonSystem { get; private set; }

        public CourseSystem CourseSystem { get; private set; }

        public GolfSystem GolfSystem { get; private set; }

        public const string GolfBall = "golf-ball", GolfCup = "cup";

        public DefenseSystem DefenseSystem { get; private set; }

        public HoldSystem HoldSystem { get; private set; }

        public const string ClimbRoute = HoldSystem.ClimbRoute, FlyRoute = HoldSystem.FlyRoute;

        public static int HoldWaveSize(HoldParams p, int wave, int peers = 1) => HoldSystem.WaveSize(p, wave, peers);

        public static int BarricadesOf(HoldParams p, int wave) => HoldSystem.BarricadesOf(p, wave);

        internal readonly WayField Ways = new WayField();

        void MakeSystems()
        {
            RaiderSystem = new RaiderSystem(this);
            CourseSystem = new CourseSystem(this);
            DefenseSystem = new DefenseSystem(this);
            GolfSystem = new GolfSystem(this);
            HoldSystem = new HoldSystem(this);
            DragonSystem = new DragonSystem(this);
        }

        public IReadOnlyList<Raider> Raiders => RaiderSystem.Raiders;

        public Raider RaiderAt(int x, int y) => RaiderSystem.RaiderAt(x, y);

        public Raider SpawnRaider(string kind, int x, int y, int hp) => RaiderSystem.SpawnRaider(kind, x, y, hp);

        public (int x, int y)? CourseRespawn(string player, int x, int y) => CourseSystem.Respawn(player, x, y);

        public GolfState Golf => GolfSystem.State;

        public void ApplyGolf(GolfCommand command) => GolfSystem.Apply(command);

        public bool GolfWaiting(GolfState g) => GolfSystem.Waiting(g);

        public DefenseState Defense => DefenseSystem.State;

        public int DefenseLeft => DefenseSystem.Left;

        public void ApplyDefense(DefenseCommand command) => DefenseSystem.Apply(command);

        public WorldHold HoldSite => HoldSystem.Site;

        public HoldState Hold => HoldSystem.State;

        public bool HoldRunning => HoldSystem.Running;

        public int HoldLeft => HoldSystem.Left;

        public void SetHold(WorldHold site) => HoldSystem.Set(site);

        public (WavePlan plan, int ticks)? HoldWarning => HoldSystem.Warning;

        public (int x, int y)? HoldRespawn() => HoldSystem.Respawn();

        public int? HoldNextWaveIn => HoldSystem.NextWaveIn;

        public bool Flying(Raider r) => HoldSystem.Flying(r);

        public (int x, int y)? HoldNext(int x, int y, bool road) => HoldSystem.Next(x, y, road);

        public DragonState Dragon => DragonSystem.State;

        public int DragonFlights => DragonSystem.Flights;

        public void ApplyDragon(DragonCommand command) => DragonSystem.Apply(command);

        public (int middle, bool back) PassLine(DragonState flight, int pass) => DragonSystem.PassLine(flight, pass);

        public bool BreathBox(out int x0, out int y0, out int x1, out int y1) => DragonSystem.BreathBox(out x0, out y0, out x1, out y1);

        public bool CaughtByBreath(int x, int y) => DragonSystem.CaughtByBreath(x, y);

        public bool ShelteredFromBreath(int x, int y) => DragonSystem.ShelteredFromBreath(x, y);

        public int PathCostAt(int x, int y) => DefenseSystem.PathCostAt(x, y);
    }
}
