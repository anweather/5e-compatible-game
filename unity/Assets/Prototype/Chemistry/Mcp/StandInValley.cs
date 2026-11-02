// What the tools act on when no game is attached (docs/RULES_MCP.md): the valley made
// from the world files alone, as here() stands in the host. The land and its water come
// from land.json and places.json, the buildings from buildings.json and villages.json.
// The trees, the yard and everything else only the Editor's bake knows aren't there. It
// makes the same cells every time. The game's own world answers instead once attached
// (Workbench.WorldDesk).
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Chem.Mcp
{
    public sealed class StandInValley : IChunkSource
    {
        // How deep the water stands, in units; how far a sea's beach reaches, and how far a village keeps from water, in cells.
        const int WaterUnits = 12, BeachCells = 5, VillageClear = 3;
        const string Grass = "grass", Sand = "sand", Bare = "dirt";

        /// <summary>Marks a rules folder with no world files beside it.</summary>
        internal static readonly StandInValley None = new StandInValley(0, 0);

        readonly bool[] water;
        readonly bool[] beach;
        readonly Dictionary<int, string> built = new Dictionary<int, string>();

        StandInValley(int w, int h)
        {
            W = w;
            H = h;
            water = new bool[w * h];
            beach = new bool[w * h];
        }

        public int W { get; }
        public int H { get; }
        public string Version => "stand-in-valley-1";

        /// <summary>The stand-in for the world/ folder beside a rules folder; null if that has no land or places.</summary>
        public static StandInValley Load(string rulesDir, ModBudgets budgets)
        {
            string worldDir = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(rulesDir).TrimEnd('/', '\\')) ?? "", "world");
            string landPath = Path.Combine(worldDir, "land.json");
            if (!File.Exists(landPath) || budgets.Places.Count == 0) return null;
            var doc = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(landPath));
            int w = (int)doc["w"], h = (int)doc["h"];
            var valley = new StandInValley(w, h);
            var land = LandFile.Read(doc.ToString(), w, h);
            int below = (int?)budgets.Places.Doc["generate"]?["water"]?["below"] ?? 0;
            for (int i = 0; i < land.Length; i++) valley.water[i] = land[i] < below;
            foreach (var place in budgets.Places.All.Where(p => p.Kind == "river"))
                foreach (var (x, y) in place.Cells())
                    if (x >= 0 && y >= 0 && x < w && y < h) valley.water[y * w + x] = true;
            valley.MarkBeach(budgets.Places);
            valley.LayBuildings(budgets.Buildings);
            return valley;
        }

        public void Make(int x, int y, Cell cell)
        {
            int i = y * W + x;
            if (built.TryGetValue(i, out var terrain))
            {
                cell.T = terrain;
                if (terrain == Bare && water[i]) cell.Water = WaterUnits;
            }
            else if (water[i])
            {
                cell.T = Bare;
                cell.Water = WaterUnits;
            }
            else cell.T = beach[i] ? Sand : Grass;
        }

        void MarkBeach(WorldPlaces places)
        {
            foreach (var sea in places.All.Where(p => p.Kind == "sea"))
                foreach (var (x, y) in sea.EdgeCells())
                    for (int dy = -BeachCells; dy <= BeachCells; dy++)
                        for (int dx = -BeachCells; dx <= BeachCells; dx++)
                        {
                            int nx = x + dx, ny = y + dy;
                            if (nx >= 0 && ny >= 0 && nx < W && ny < H) beach[ny * W + nx] = true;
                        }
        }

        void LayBuildings(WorldBuildings buildings)
        {
            foreach (var spec in buildings.Villages) LayVillage(VillageLayout.Lay(spec, MayBuild));
            foreach (var row in buildings.FileRows)
            {
                int ax = (int)row["at"][0], ay = (int)row["at"][1];
                foreach (var t in ((Newtonsoft.Json.Linq.JObject)row["built"]).Properties())
                    foreach (var r in t.Value)
                        for (int k = 1; k + 1 < r.Count(); k += 2)
                            for (int x = (int)r[k]; x <= (int)r[k + 1]; x++) Lay(ax + x, ay + (int)r[0], t.Name);
            }
        }

        void LayVillage(VillagePlan plan)
        {
            foreach (var (x, y) in plan.Square.Cells()) Lay(x, y, Bare);
            foreach (var lane in plan.Lanes)
                foreach (var (x, y) in lane.Cells()) Lay(x, y, Bare);
            foreach (var b in plan.Buildings)
            {
                foreach (var (x, y) in b.Footprint.Cells()) Lay(x, y, b.WallMaterial);
                foreach (var win in b.Windows) Lay(win.X, win.Y, plan.Spec.Materials.Window);
            }
            water[plan.Well.y * W + plan.Well.x] = true;
            Lay(plan.Well.x, plan.Well.y, Bare);
        }

        void Lay(int x, int y, string terrain)
        {
            if (x >= 0 && y >= 0 && x < W && y < H) built[y * W + x] = terrain;
        }

        bool MayBuild(int x, int y)
        {
            for (int dy = -VillageClear; dy <= VillageClear; dy++)
                for (int dx = -VillageClear; dx <= VillageClear; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= W || ny >= H || water[ny * W + nx]) return false;
                }
            return true;
        }
    }
}
