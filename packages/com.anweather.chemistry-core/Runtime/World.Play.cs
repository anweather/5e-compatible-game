// The game modes and the world (docs/design/architecture-review.md step 9, D9). The raiders, the dragon and golf are
// systems the world owns and runs itself: spells, strikes, reactions, acts, the change audit and the save all reach
// them. The Hold, the defense field and the Gauntlet's course are systems in their own assembly, Chem.Play, which this
// one doesn't reference: the world keeps their state (its data, saved, hashed and in the checksum here, whether the
// modes are on or not) and calls them through IWorldModes at the places in the tick and the commands they always had.
// A world gets them once, from Chem.Play (PlayModes.Install), and a world made from another (a joiner's) gets the
// same ones (InstallModesOf). A world without them is the chemistry alone: where it would have run a mode, it throws.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Chem
{
    public sealed partial class World
    {
        public RaiderSystem RaiderSystem { get; private set; }

        public const string ShovedStatus = RaiderSystem.ShovedStatus, ThrownStatus = RaiderSystem.ThrownStatus;

        public DragonSystem DragonSystem { get; private set; }

        public GolfSystem GolfSystem { get; private set; }

        public const string GolfBall = "golf-ball", GolfCup = "cup";

        public const string ClimbRoute = "climb", FlyRoute = "fly";

        internal const string LandedStatus = "landed";

        void MakeSystems()
        {
            RaiderSystem = new RaiderSystem(this);
            GolfSystem = new GolfSystem(this);
            DragonSystem = new DragonSystem(this);
        }

        IWorldModes modes;

        internal IWorldModes Modes => modes;

        public bool HasModes => modes != null;

        internal void Install(IWorldModes these)
        {
            if (these == null) throw new ArgumentNullException(nameof(these));
            if (these.World != this) throw new ArgumentException("Those modes were made for another world", nameof(these));
            if (modes != null) throw new InvalidOperationException("This world already has its game modes");
            modes = these;
        }

        public void InstallModesOf(World other)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));
            if (other.modes != null) Install(other.modes.MadeFor(this));
        }

        static InvalidOperationException NoModes(string what) =>
            new InvalidOperationException($"This world has no game modes, so it can't {what}: make it through Chem.Play (PlayModes.Install)");

        void TickModes()
        {
            if (modes != null) modes.TickAfterRaiders();
            else if (ModesWouldRun()) throw NoModes("run the Hold or the defense field it has");
        }

        bool ModesWouldRun() =>
            (Defense != null && Defense.Phase == DefensePhase.Running)
            || (HoldSite != null && (HoldRunning || HoldPlate != 0 || Stands.Values.Any(s => !s.Down && s.X == HoldSite.Plate.x && s.Y == HoldSite.Plate.y)));

        bool StartMoment(string id)
        {
            if (modes != null) return modes.StartMoment(id);
            if (HoldSite != null && Rules.Sim.Hold.Moment(id) != null) throw NoModes("start the Hold at a moment");
            return false;
        }

        public void ApplyDefense(DefenseCommand command)
        {
            if (modes != null)
            {
                modes.Apply(command);
                return;
            }
            if (command.Kind == DefenseKind.Start) throw NoModes("start the defense field");
            FinishTick();
        }

        public void ApplyCourse(CourseCommand command)
        {
            if (modes != null)
            {
                modes.Apply(command);
                return;
            }
            if (CourseLayout != null) throw NoModes("run the course");
            FinishTick();
        }

        public IReadOnlyList<Raider> Raiders => RaiderSystem.Raiders;

        public Raider RaiderAt(int x, int y) => RaiderSystem.RaiderAt(x, y);

        public Raider SpawnRaider(string kind, int x, int y, int hp) => RaiderSystem.SpawnRaider(kind, x, y, hp);

        public bool Flying(Raider r) => Flies(r, Rules.Entity(r.Kind));

        internal static bool Flies(Raider r, EntityDef def) => def.Raider?.Route == FlyRoute && r.StatusOf(LandedStatus) == 0;

        public GolfState Golf => GolfSystem.State;

        public void ApplyGolf(GolfCommand command) => GolfSystem.Apply(command);

        public bool GolfWaiting(GolfState g) => GolfSystem.Waiting(g);

        public DragonState Dragon => DragonSystem.State;

        public int DragonFlights => DragonSystem.Flights;

        public void ApplyDragon(DragonCommand command) => DragonSystem.Apply(command);

        public (int middle, bool back) PassLine(DragonState flight, int pass) => DragonSystem.PassLine(flight, pass);

        public bool BreathBox(out int x0, out int y0, out int x1, out int y1) => DragonSystem.BreathBox(out x0, out y0, out x1, out y1);

        public bool CaughtByBreath(int x, int y) => DragonSystem.CaughtByBreath(x, y);

        public bool ShelteredFromBreath(int x, int y) => DragonSystem.ShelteredFromBreath(x, y);

        public DefenseState Defense { get; internal set; }

        public int DefenseLeft => Defense == null ? 0 : RaiderSystem.List.Count + Defense.ToSpawn;

        public string DefenseHash() => Fnv1a.Hash(DefenseText());

        public string DefenseText()
        {
            var d = Defense;
            if (d == null) return "-";
            var inv = CultureInfo.InvariantCulture;
            var sb = new System.Text.StringBuilder();
            sb.Append(((int)d.Phase).ToString(inv)).Append('/').Append(d.Wave.ToString(inv)).Append('/').Append(d.Lives.ToString(inv)).Append('/')
              .Append(d.ToSpawn.ToString(inv)).Append('/').Append(d.NextSpawn.ToString(inv)).Append('/').Append(d.Spawned.ToString(inv)).Append('|');
            foreach (var r in RaiderSystem.List)
            {
                sb.Append(r.Id.ToString(inv)).Append(',').Append(r.Kind).Append(',').Append(r.X.ToString(inv)).Append(',').Append(r.Y.ToString(inv)).Append(',')
                  .Append(r.Hp.ToString(inv)).Append(',').Append(r.Wait.ToString(inv));
                foreach (var s in HashedStatuses) sb.Append(',').Append(r.StatusOf(s).ToString(inv));
                if (r.StatusOf(ThrownStatus) > 0) sb.Append(",t").Append(r.StatusOf(ThrownStatus).ToString(inv));
                if (r.StatusOf(DownedStatus) > 0) sb.Append(",d").Append(r.StatusOf(DownedStatus).ToString(inv));
                if (r.StatusOf(ShovedStatus) > 0) sb.Append(",s").Append(r.StatusOf(ShovedStatus).ToString(inv));
                sb.Append(';');
            }
            return sb.ToString();
        }

        static readonly string[] HashedStatuses = { "burning", "wet", "frozen", "snagged" };

        JToken SaveDefense() => Defense == null ? JValue.CreateNull() : new JObject
        {
            ["phase"] = (int)Defense.Phase,
            ["at"] = new JArray(Defense.Wave, Defense.Lives, Defense.ToSpawn, Defense.NextSpawn, Defense.Spawned),
            ["gate"] = new JArray(Defense.GateX, Defense.GateY, Defense.FlagX, Defense.FlagY),
            ["field"] = new JArray(Defense.X0, Defense.Y0, Defense.X1, Defense.Y1),
        };

        void LoadDefense(JObject d)
        {
            var at = d["at"];
            var gate = d["gate"];
            var field = d["field"];
            Defense = new DefenseState
            {
                Phase = (DefensePhase)(int)d["phase"],
                Wave = (int)at[0],
                Lives = (int)at[1],
                ToSpawn = (int)at[2],
                NextSpawn = (int)at[3],
                Spawned = (int)at[4],
                GateX = (int)gate[0],
                GateY = (int)gate[1],
                FlagX = (int)gate[2],
                FlagY = (int)gate[3],
                X0 = (int)field[0],
                Y0 = (int)field[1],
                X1 = (int)field[2],
                Y1 = (int)field[3],
            };
        }

        public WorldHold HoldSite { get; private set; }

        internal DefenseState HoldField { get; private set; }

        internal readonly HashSet<int> HoldRoad = new HashSet<int>();

        public HoldState Hold { get; internal set; }

        internal int HoldPlate { get; set; }

        internal readonly SortedDictionary<int, int> Battered = new SortedDictionary<int, int>();

        public bool HoldRunning => Hold != null && Hold.Phase == HoldPhase.Running;

        public int HoldLeft => Hold == null ? 0 : RaiderSystem.List.Count + Hold.ToSpawn;

        public void SetHold(WorldHold site)
        {
            HoldSite = site;
            if (site == null) return;
            var p = Rules.Sim.Hold;
            var cells = site.Spawners.SelectMany(s => s.Cells).Concat(new[] { site.Heart }).ToList();
            HoldField = new DefenseState
            {
                FlagX = site.Heart.x,
                FlagY = site.Heart.y,
                X0 = Math.Max(0, cells.Min(c => c.x) - p.Margin),
                X1 = Math.Min(W - 1, cells.Max(c => c.x) + p.Margin),
                Y0 = Math.Max(0, cells.Min(c => c.y) - p.Margin),
                Y1 = Math.Min(H - 1, cells.Max(c => c.y) + p.Margin),
            };
            HoldRoad.Clear();
            foreach (var cell in BuildingRaster.Cells(BuildingRow.Read(site.Castle, site.Row, Rules.Buildings), Rules.Buildings))
                if (cell.Part == BuildingPart.Road && cell.Terrain == site.RoadTerrain && InBounds(cell.X, cell.Y)) HoldRoad.Add(Idx(cell.X, cell.Y));
        }

        internal uint WaveRoll(int wave, int slot, uint salt)
        {
            unchecked
            {
                uint t = Seed + (uint)wave * 0x9e3779b1u + (uint)slot * 0x85ebca77u + salt * 0xc2b2ae3du;
                t = (t ^ (t >> 15)) * (t | 1u);
                t ^= t + (t ^ (t >> 7)) * (t | 61u);
                return t ^ (t >> 14);
            }
        }

        public string HoldText()
        {
            if (Hold == null && HoldPlate == 0) return "";
            var inv = CultureInfo.InvariantCulture;
            var sb = new System.Text.StringBuilder("|H").Append(HoldPlate.ToString(inv));
            if (Hold != null) sb.Append(':').Append((int)Hold.Phase).Append(':').Append(Hold.Wave).Append(':').Append(Hold.Health).Append(':').Append(Hold.ToSpawn).Append(':').Append(Hold.NextSpawn).Append(':').Append(Hold.Spawned).Append(':').Append(Hold.Peers).Append(':').Append(Hold.InWave).Append(':').Append(Hold.Theme).Append(':').Append(Hold.Moment);
            if (Hold != null && Hold.NextWave != 0) sb.Append(":n").Append(Hold.NextWave);
            foreach (var b in Battered) sb.Append("|b").Append(b.Key).Append(':').Append(b.Value);
            foreach (var r in RaiderSystem.List) sb.Append("|r").Append(r.Id).Append(':').Append(r.Kind).Append(':').Append(r.X).Append(':').Append(r.Y).Append(':').Append(r.Hp).Append(':').Append(r.Wait).Append(':').Append(r.StatusOf(LandedStatus));
            return sb.ToString();
        }

        JObject SaveHold()
        {
            var save = new JObject { ["plate"] = HoldPlate, ["battered"] = new JArray(Battered.Select(b => new JArray(b.Key, b.Value))) };
            if (Hold != null) save["at"] = new JArray((int)Hold.Phase, Hold.Wave, Hold.Health, Hold.ToSpawn, Hold.NextSpawn, Hold.Spawned, Hold.Peers, Hold.InWave, Hold.Theme, Hold.Moment, Hold.MomentTick, Hold.NextWave);
            if (Hold != null && Hold.Wizards.Count > 0) save["wizards"] = SaveWizards(Hold);
            return save;
        }

        void LoadHold(JToken s)
        {
            if (!(s is JObject o)) return;
            HoldPlate = (int)o["plate"];
            if (o["at"] is JArray at) Hold = new HoldState { Phase = (HoldPhase)(int)at[0], Wave = (int)at[1], Health = (int)at[2], ToSpawn = (int)at[3], NextSpawn = (int)at[4], Spawned = (int)at[5], Peers = (int)at[6], InWave = (int)at[7], Theme = (string)at[8], Moment = (string)at[9], MomentTick = (int)at[10], NextWave = at.Count > 11 ? (int)at[11] : 0 };
            if (Hold != null) LoadWizards(Hold, o["wizards"]);
            foreach (var b in o["battered"] ?? new JArray()) Battered[(int)b[0]] = (int)b[1];
        }

        internal readonly List<SpellZone> Zones = new List<SpellZone>();

        public IReadOnlyList<SpellZone> SpellZones => Zones;

        public CourseLayout CourseLayout { get; internal set; }

        internal readonly SortedDictionary<string, CourseRun> Runs = new SortedDictionary<string, CourseRun>(StringComparer.Ordinal);

        public IReadOnlyDictionary<string, CourseRun> CourseRuns => Runs;

        public SpellZone ZoneAgainst(SpellDef s, int x, int y)
        {
            if (s == null) throw new ArgumentNullException(nameof(s));
            foreach (var z in Zones)
                if (z.Area.Contains(x, y) && !Allows(z, s.Id)) return z;
            return null;
        }

        bool Allows(SpellZone z, string spell)
        {
            if (z.Spells.Contains(spell)) return true;
            foreach (var c in Rules.Combos.All)
                if (c.Spell == spell && c.Id.Split('+').All(e => z.Elements.Contains(e))) return true;
            var (slot, _) = HandRules.Find(spell);
            return slot != null && slot != HandsDef.Empty && slot.Split('+').All(e => z.Elements.Contains(e));
        }

        public string ZoneText() => string.Concat(Zones.Select(z =>
            $"|z{z.Id}:{CourseRect.Text(z.Area)}:{string.Join(",", z.Elements)}:{string.Join(",", z.Spells)}"));

        public string CourseText() => string.Concat(Runs.Values.Select(r =>
            $"|r{r.Player}:{CourseRect.W(r.Checkpoint)}:{CourseRect.W(r.Start)}:{CourseRect.W(r.Best)}:{CourseRect.W(r.Last)}"));

        public string CourseHash() => Fnv1a.Hash(CourseText() + ZoneText());

        void SaveCourse(JObject save)
        {
            if (Runs.Count > 0) save["course"] = new JArray(Runs.Values.Select(r => new JArray(r.Player, r.Checkpoint, r.Start, r.Best, r.Last)));
        }

        void LoadCourse(JObject s)
        {
            if (!(s["course"] is JArray saved)) return;
            foreach (var r in saved.OfType<JArray>())
                Runs[(string)r[0]] = new CourseRun { Player = (string)r[0], Checkpoint = (int)r[1], Start = (int)r[2], Best = (int)r[3], Last = (int)r[4] };
        }
    }
}
