// The game modes' systems, which the world owns (docs/design/architecture-review.md step 9, D9): each is a class in
// Play/ over the world's data, made with the world and run at the place in the tick its mode always had. What's left
// here are the thin members code outside the core still calls on the world; they forward to a system.
using System.Collections.Generic;

namespace Chem
{
    public sealed partial class World
    {
        /// <summary>The raiders on the board (Play/RaiderSystem.cs).</summary>
        public RaiderSystem RaiderSystem { get; private set; }

        /// <summary>The status counting the casts that have knocked a raider back, and a raider thrown off its feet (RaiderSystem).</summary>
        public const string ShovedStatus = RaiderSystem.ShovedStatus, ThrownStatus = RaiderSystem.ThrownStatus;

        /// <summary>The dragon's flight (Play/DragonSystem.cs); the starting state it puts back is the world's (World.Dragon.cs).</summary>
        public DragonSystem DragonSystem { get; private set; }

        /// <summary>The spell zones and the course's runs (Play/CourseSystem.cs).</summary>
        public CourseSystem CourseSystem { get; private set; }

        /// <summary>The hole being played (Play/GolfSystem.cs).</summary>
        public GolfSystem GolfSystem { get; private set; }

        /// <summary>What an act calls the golf ball and the cup; "ball" alone is the court's physics ball.</summary>
        public const string GolfBall = "golf-ball", GolfCup = "cup";

        /// <summary>The defense field's waves (Play/DefenseSystem.cs).</summary>
        public DefenseSystem DefenseSystem { get; private set; }

        /// <summary>The Hold's runs (Play/HoldSystem.cs).</summary>
        public HoldSystem HoldSystem { get; private set; }

        /// <summary>A Hold raider's way in (entities.json raider.route): up the walls, or over everything.</summary>
        public const string ClimbRoute = HoldSystem.ClimbRoute, FlyRoute = HoldSystem.FlyRoute;

        /// <summary>How many raiders a Hold wave sends (HoldSystem.WaveSize); the probes set a wave's size with it.</summary>
        public static int HoldWaveSize(HoldParams p, int wave, int peers = 1) => HoldSystem.WaveSize(p, wave, peers);

        /// <summary>How many barricades a Hold wave puts up (HoldSystem.BarricadesOf); the moments probe counts them.</summary>
        public static int BarricadesOf(HoldParams p, int wave) => HoldSystem.BarricadesOf(p, wave);

        // The raiders' cost field's scratch (Play/WayField.cs), shared by the defense field's march and the Hold's ways.
        internal readonly WayField Ways = new WayField();

        // Made in both constructors (World.cs, OpenWorld.cs), before the chunks: none of them draws from the world's generator.
        void MakeSystems()
        {
            RaiderSystem = new RaiderSystem(this);
            CourseSystem = new CourseSystem(this);
            DefenseSystem = new DefenseSystem(this);
            GolfSystem = new GolfSystem(this);
            HoldSystem = new HoldSystem(this);
            DragonSystem = new DragonSystem(this);
        }

        /// <summary>The raiders on the board, oldest first (RaiderSystem.Raiders); for the view, the probes and the tests.</summary>
        public IReadOnlyList<Raider> Raiders => RaiderSystem.Raiders;

        /// <summary>The raider standing on a square, or null (RaiderSystem.RaiderAt); for SpellCaster, and the world's own passes.</summary>
        public Raider RaiderAt(int x, int y) => RaiderSystem.RaiderAt(x, y);

        /// <summary>Puts a raider of a kind on a square (RaiderSystem.SpawnRaider); for Desk and the probes.</summary>
        public Raider SpawnRaider(string kind, int x, int y, int hp) => RaiderSystem.SpawnRaider(kind, x, y, hp);

        /// <summary>Where a wizard downed at (x, y) gets up (CourseSystem.Respawn); for WizardDown.</summary>
        public (int x, int y)? CourseRespawn(string player, int x, int y) => CourseSystem.Respawn(player, x, y);

        /// <summary>The hole being played, or null before anyone teed off (GolfSystem.State); for GolfCourse, ChemGrid and the probes.</summary>
        public GolfState Golf => GolfSystem.State;

        /// <summary>Puts the ball on the tee (GolfSystem.Apply); for ChemGrid.</summary>
        public void ApplyGolf(GolfCommand command) => GolfSystem.Apply(command);

        /// <summary>Whether whoever hit the ball last is waiting for someone else's shot (GolfSystem.Waiting); for GolfCourse.</summary>
        public bool GolfWaiting(GolfState g) => GolfSystem.Waiting(g);

        /// <summary>The defense field's waves, or null before anyone started them (DefenseSystem.State); for DefenseField and the probes.</summary>
        public DefenseState Defense => DefenseSystem.State;

        /// <summary>Raiders still to beat in the wave under way (DefenseSystem.Left); for DefenseField.</summary>
        public int DefenseLeft => DefenseSystem.Left;

        /// <summary>Runs a defense command (DefenseSystem.Apply); for DefenseField.</summary>
        public void ApplyDefense(DefenseCommand command) => DefenseSystem.Apply(command);

        /// <summary>The map's Hold, or null (HoldSystem.Site); for the Hold's view, the probes and Hold.Runner.</summary>
        public WorldHold HoldSite => HoldSystem.Site;

        /// <summary>The run under way or last lost, or null before any (HoldSystem.State); for the view, the probes and Hold.Runner.</summary>
        public HoldState Hold => HoldSystem.State;

        /// <summary>Whether a run is under way (HoldSystem.Running); for the view, the probes and Hold.Runner.</summary>
        public bool HoldRunning => HoldSystem.Running;

        /// <summary>Raiders still to beat in the wave under way (HoldSystem.Left); for HoldShow.</summary>
        public int HoldLeft => HoldSystem.Left;

        /// <summary>Gives the world its map's Hold, before the first tick (HoldSystem.Set); for ChemGrid and Hold.Runner.</summary>
        public void SetHold(WorldHold site) => HoldSystem.Set(site);

        /// <summary>The wave warning, while a wave waits to start (HoldSystem.Warning); for HoldShow and ProgressKeeper.</summary>
        public (WavePlan plan, int ticks)? HoldWarning => HoldSystem.Warning;

        /// <summary>Where a downed wizard gets up during a run (HoldSystem.Respawn); for WizardDown.</summary>
        public (int x, int y)? HoldRespawn() => HoldSystem.Respawn();

        /// <summary>With timed waves, ticks until the next wave starts (HoldSystem.NextWaveIn); for HoldShow.</summary>
        public int? HoldNextWaveIn => HoldSystem.NextWaveIn;

        /// <summary>Whether this raider is in the air (HoldSystem.Flying); for the view's raiders, lock-on and the probes.</summary>
        public bool Flying(Raider r) => HoldSystem.Flying(r);

        /// <summary>The cell a Hold raider here steps to next (HoldSystem.Next); for lock-on and ProbeHold.</summary>
        public (int x, int y)? HoldNext(int x, int y, bool road) => HoldSystem.Next(x, y, road);

        /// <summary>The flight under way, or null (DragonSystem.State); for the dragon's view, the probes and the HUD.</summary>
        public DragonState Dragon => DragonSystem.State;

        /// <summary>Flights begun in this world (DragonSystem.Flights); for the dragon's view and the probes.</summary>
        public int DragonFlights => DragonSystem.Flights;

        /// <summary>The dragon's summons (DragonSystem.Apply); for ChemGrid.</summary>
        public void ApplyDragon(DragonCommand command) => DragonSystem.Apply(command);

        /// <summary>A pass's line, for the view to fly (DragonSystem.PassLine); for DragonFlight and the probes.</summary>
        public (int middle, bool back) PassLine(DragonState flight, int pass) => DragonSystem.PassLine(flight, pass);

        /// <summary>The cells under the breath this tick (DragonSystem.BreathBox); for DragonView and the probes.</summary>
        public bool BreathBox(out int x0, out int y0, out int x1, out int y1) => DragonSystem.BreathBox(out x0, out y0, out x1, out y1);

        /// <summary>Whether the breath catches what stands at (x, y) this tick (DragonSystem.CaughtByBreath); for WizardDown.</summary>
        public bool CaughtByBreath(int x, int y) => DragonSystem.CaughtByBreath(x, y);

        /// <summary>The breath's shelter rule (DragonSystem.ShelteredFromBreath); for ProbeDragon.</summary>
        public bool ShelteredFromBreath(int x, int y) => DragonSystem.ShelteredFromBreath(x, y);

        /// <summary>The cheapest way's cost from a square to the flag (DefenseSystem.PathCostAt); for ProbeDefense.</summary>
        public int PathCostAt(int x, int y) => DefenseSystem.PathCostAt(x, y);
    }
}
