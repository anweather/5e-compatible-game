// The forest-fire scenario: a Fireball into the valley's forest, on the world the
// game plays. The cells are core/fixtures/valley-map.txt, a copy of the map the
// valley's builder bakes into the scene (the real thing needs the Editor to make,
// so the headless core keeps a copy; a benchmark's map must not move under it);
// the land, puzzles and golf are world/'s and the rules are the scene's. One
// wizard stands by, and the chunks follow it as ChemGrid does solo: ChunkPlanner's
// plan before each tick. It settles for WarmTicks before the cast, as a world has
// by the time anyone reaches the forest.
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Chem.Mcp
{
    /// <summary>One run of the forest fire: its world, ticked by whoever holds it, PlanChunks before each tick.</summary>
    public sealed class ForestFire
    {
        public const string Name = "forest-fire";
        /// <summary>The densest 24 × 24 cells of forest on the walking route through it (chunk 6, 6).</summary>
        public const int TargetX = 384, TargetY = 388;
        /// <summary>Where the wizard stands: Fireball's range away, west of the fire, in chunk (5, 6).</summary>
        public const int WizardX = 368, WizardY = 388;
        public const int WarmTicks = 60;
        /// <summary>A wizard sprinting at 6 m/s, in cells of 1.5 m at 6 ticks a second, as thirds of a cell a tick.</summary>
        const int WalkThirdsPerTick = 2;
        // The chunks PerfStage walks through the forest (its route, from where the wizard stands).
        static readonly (int cx, int cy)[] Route = { (5, 6), (5, 5), (4, 5), (4, 4), (5, 4), (6, 4), (6, 5), (7, 5), (7, 6), (6, 6) };
        /// <summary>The scene's seed.</summary>
        public const uint Seed = 1;
        const string MapFile = "core/fixtures/valley-map.txt";

        // The scene's own tuning (ChemGrid.SceneTuning, from ValleyBuilder.WireChemistry).
        const string Tuning = "{ \"title\": \"Scene tuning\", \"patch\": { \"sim\": { \"fire\": { \"fadeCells\": 50 }, \"conductLimit\": 12000 } } }";

        // ChemGrid.Legend: what each character of the map is.
        static readonly Dictionary<char, (string t, int water, bool frozen, string cover)> Legend = new Dictionary<char, (string, int, bool, string)>
        {
            ['%'] = ("bedrock", 0, false, null),
            ['#'] = ("stone", 0, false, null),
            ['.'] = ("dirt", 0, false, null),
            [','] = ("grass", 0, false, null),
            ['"'] = ("brush", 0, false, null),
            ['T'] = ("tree", 0, false, null),
            ['='] = ("wood", 0, false, null),
            ['_'] = ("pit", 0, false, null),
            ['w'] = ("pit", 4, false, null),
            ['*'] = ("pit", 4, true, null),
            ['r'] = ("river", 4, false, null),
            ['~'] = ("dirt", 2, false, null),
            ['m'] = ("metal", 0, false, null),
            ['o'] = ("dirt", 0, false, "oil"),
            ['@'] = ("grass", 0, false, null),
            ['L'] = ("logwall", 0, false, null),
            ['P'] = ("pane", 0, false, null),
        };

        readonly ChunkPlanner planner = new ChunkPlanner();
        readonly (int x, int y)[] wizard = { (WizardX, WizardY) };
        readonly bool walks;
        int walked;

        /// <summary>
        /// The world, settled, with the Fireball just cast. The scene's tuning goes over the rules
        /// given; repoRoot is the folder holding world/ and core/.
        /// </summary>
        public ForestFire(Rules rules, string repoRoot, uint seed = Seed, bool walks = false)
        {
            this.walks = walks;
            World = Valley(RuleSet.BaseOf(rules, ModPatch.Parse(Tuning)), repoRoot, seed);
            for (int i = 0; i < WarmTicks; i++)
            {
                ChunkStreaming.Apply(World, planner.Plan(World, wizard));
                World.Step();
            }
            World.Cast("fireball", TargetX, TargetY, 1);
        }

        public World World { get; }

        /// <summary>Everything a fix must leave alone at this tick: the cells, the random stream, the active chunks, the log.</summary>
        public string Fingerprint() => string.Format(CultureInfo.InvariantCulture, "tick {0} cells {1} rng {2:x8} active {3} log {4} hidden {5}",
            World.Tick, World.CellHash(), World.RngState, World.ActiveChunks.Count, World.LogAppended, Hidden());

        // What the cell hash leaves out and a later tick reads: how damp each cell is, and when each chunk was last live.
        string Hidden()
        {
            var sb = new StringBuilder();
            foreach (var c in World.Cells) sb.Append(c.DampUntil).Append(',');
            foreach (var (cx, cy) in World.ChunkCoords) sb.Append(World.LastLive(cx, cy)).Append(';');
            return Fnv1a.Hash(sb.ToString());
        }

        /// <summary>The chunks follow the wizard, as ChemGrid.PlanChunks before a tick.</summary>
        public void PlanChunks() => ChunkStreaming.Apply(World, Plan());

        /// <summary>This tick's chunk plan alone, for timing it apart from applying it.</summary>
        public List<ChunkSwitch> Plan()
        {
            if (walks) Walk();
            return planner.Plan(World, wizard);
        }

        // On along the route, round and round, a straight line from chunk middle to chunk middle.
        void Walk()
        {
            int cells = walked++ * WalkThirdsPerTick / 3, leg = cells / Chunks.Size % Route.Length, along = cells % Chunks.Size;
            var (ax, ay) = Route[leg];
            var (bx, by) = Route[(leg + 1) % Route.Length];
            int x = ax * Chunks.Size + (bx - ax) * along, y = ay * Chunks.Size + (by - ay) * along;
            wizard[0] = (x + Chunks.Size / 2, y + Chunks.Size / 2);
        }

        /// <summary>The world as ChemGrid.StartWorld makes it, and the land as ValleyChemistry.Bind sets it.</summary>
        static World Valley(Rules rules, string repoRoot, uint seed)
        {
            string path = Path.Combine(repoRoot, MapFile);
            if (!File.Exists(path)) throw new RulesToolException($"The forest-fire scenario needs {MapFile} in the repo, and it isn't at {path}.");
            var rows = File.ReadAllText(path).Trim('\n').Split('\n');
            string worldDir = Path.Combine(repoRoot, "world");
            var w = new World(rules, rows[0].Length, rows.Length, seed);
            for (int y = 0; y < w.H; y++)
                for (int x = 0; x < w.W; x++) PaintCell(w, x, y, rows[y][x]);
            WorldPuzzles.Load(worldDir).LayInto(w);
            if (GolfCommand.Load(worldDir) is GolfCommand hole) w.ApplyGolf(hole);
            w.KeepStart();
            w.SetLand(LandFile.Read(File.ReadAllText(Path.Combine(worldDir, "land.json")), w.W, w.H));
            return w;
        }

        static void PaintCell(World w, int x, int y, char ch)
        {
            if (!Legend.TryGetValue(ch, out var spec)) spec = Legend['.'];
            w.Paint(x, y, spec.t);
            if (spec.water > 0) w.AddWater(x, y, spec.water);
            if (spec.frozen) w.At(x, y).Frozen = true;
            if (spec.cover != null) w.SetCover(x, y, spec.cover);
        }
    }
}
