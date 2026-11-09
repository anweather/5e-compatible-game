using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Chem.Mcp;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    /// <summary>
    /// What a building mod says that nothing draws (#71): validate and apply name it in a line, so Claude
    /// can't claim a change the player won't see. Andrew's "Lit windows on the lake house" is the case.
    /// </summary>
    public class UnseenTests
    {
        const string Unseen = "Won't show:";

        static Workbench NewBench() => new Workbench(ChemistryTests.Folders);

        static BuildingTables Shipped => Rules.Load(ChemistryTests.RulesDir()).Buildings;

        // A box of walls at a site the stand-in valley has room for, with these openings on its north wall.
        static JObject House(int x, int y, JArray north, JObject extra = null)
        {
            var row = JObject.Parse("{ \"style\": \"plain\", \"joins\": { \"nw\": [0, 0], \"ne\": [12, 0], \"se\": [12, 10], \"sw\": [0, 10] }," +
                " \"walls\": { \"north\": { \"from\": \"nw\", \"to\": \"ne\" }, \"east\": { \"from\": \"ne\", \"to\": \"se\" }," +
                " \"south\": { \"from\": \"se\", \"to\": \"sw\", \"openings\": [{ \"kind\": \"door\", \"at\": 4 }] }, \"west\": { \"from\": \"sw\", \"to\": \"nw\" } }," +
                " \"roofs\": { \"main\": { \"shape\": \"gable\", \"pitch\": [6, 12], \"overhang\": 0.3, \"cover\": \"slate\", \"over\": [\"nw\", \"ne\", \"se\", \"sw\"] } } }");
            row["at"] = new JArray(x, y);
            row["walls"]["north"]["openings"] = north;
            if (extra != null) row.Merge(extra);
            return row;
        }

        static string Patch(JObject row, JObject more = null)
        {
            var patch = new JObject { ["world"] = new JObject { ["buildings"] = new JObject { ["test-house#1"] = row } } };
            if (more != null) patch.Merge(more);
            return patch.ToString();
        }

        // Where a 6 by 5 house can stand on the stand-in valley.
        static (int x, int y) Site(Workbench bench)
        {
            string sites = new HouseToolsTests.Client(bench).Call("find_site", new { width = 7, depth = 6 });
            var spot = Regex.Match(sites, @"\[(\d+), (\d+)\]");
            Assert.That(spot.Success, sites);
            return (int.Parse(spot.Groups[1].Value, CultureInfo.InvariantCulture), int.Parse(spot.Groups[2].Value, CultureInfo.InvariantCulture));
        }

        [Test]
        public void TheLakeHousesLitPaneIsNamedAsUnseenInValidateAndApply()
        {
            // The patch Claude applied in Andrew's play (chat record 51ba306f369d), moved to a free site.
            var bench = NewBench();
            var (x, y) = Site(bench);
            var lit = JObject.Parse("{ \"terrain\": { \"lit-pane\": { \"name\": \"Lit window\", \"tags\": [\"blocking\", \"seeThrough\"], \"look\": { \"family\": \"block\", \"color\": [1, 0.8, 0.4], \"height\": 1.2, \"glow\": 0.4 } } }," +
                " \"buildings\": { \"openings\": { \"lit-window\": { \"cells\": 1, \"becomes\": \"lit-pane\", \"look\": { \"width\": 0.9, \"height\": 1.2, \"sill\": 0.9 } } } } }");
            string patch = Patch(House(x, y, new JArray(new JObject { ["kind"] = "lit-window", ["at"] = 4 })), lit);
            var client = new HouseToolsTests.Client(bench);

            string validated = client.Call("validate", new { draft = patch });
            Assert.That(validated, Does.Contain("No problems"));
            Assert.That(validated, Does.Contain(Unseen));
            Assert.That(validated, Does.Contain("the look of lit-pane, which opening lit-window becomes"));
            Assert.That(validated, Does.Contain("lamp"), "it says what to do instead");

            string applied = client.Call("apply", new { mod = patch });
            Assert.That(applied, Does.Not.Contain("refused"), applied);
            Assert.That(applied, Does.Contain(Unseen));
        }

        [Test]
        public void ALitWindowFromTheTablesAndAColourOfItsOwnSayNothing()
        {
            var bench = NewBench();
            var (x, y) = Site(bench);
            var openings = new JArray(new JObject { ["kind"] = "window", ["at"] = 2 }, new JObject { ["kind"] = "wide-window", ["at"] = 6, ["lamp"] = "#8fc4ff" });
            string patch = Patch(House(x, y, openings));
            var client = new HouseToolsTests.Client(bench);
            string validated = client.Call("validate", new { draft = patch });
            Assert.That(validated, Does.Contain("No problems"));
            Assert.That(validated, Does.Not.Contain(Unseen));
            Assert.That(client.Call("apply", new { mod = patch }), Does.Not.Contain(Unseen));
        }

        [Test]
        public void FieldsTheViewNeverReadsAreNamedWithWhatItTakes()
        {
            var row = House(100, 100, new JArray(new JObject { ["kind"] = "window", ["at"] = 2, ["glow"] = 0.4 }),
                JObject.Parse("{ \"lights\": true, \"roofs\": { \"main\": { \"color\": \"#ff0000\" } }, \"parts\": [{ \"kind\": \"chimney\", \"on\": \"east\", \"smoke\": \"blue\" }, { \"kind\": \"lantern\", \"on\": \"east\" }] }"));
            var patch = JObject.Parse(Patch(row, JObject.Parse("{ \"buildings\": { \"openings\": { \"bright-window\": { \"cells\": 1, \"becomes\": \"pane\", \"look\": { \"width\": 0.9, \"height\": 1.2, \"sill\": 0.9, \"emissive\": 2 } } }, \"details\": { \"lantern\": { \"look\": { \"takes\": [\"on\"] } } } } }")));
            var rules = Rules.Load(ChemistryTests.RulesDir()).WithMods(new[] { ModPatch.Parse(patch.ToString()) });
            var unseen = BuildingsShown.Unseen(patch, rules.Buildings, Shipped);
            string all = string.Join("\n", unseen);
            Assert.That(all, Does.Contain("opening bright-window's look: emissive"));
            Assert.That(all, Does.Contain("detail lantern"));
            Assert.That(all, Does.Contain("building test-house#1: lights"));
            Assert.That(all, Does.Contain("wall north opening 1 (window): glow"));
            Assert.That(all, Does.Contain("roof main: color"));
            Assert.That(all, Does.Contain("part 1 (chimney): smoke"));
            Assert.That(all, Does.Contain("part 2 (lantern: the view draws"));
            Assert.That(all, Does.Contain("roof main: color (a gable roof takes"), "what the view reads isn't named: " + all);
            Assert.That(all, Does.Not.Contain("wall north:"), "a wall that says only what's read: " + all);
            Assert.That(BuildingsShown.Note(unseen), Does.StartWith("\n" + Unseen));
        }

        [Test]
        public void ALampIsAColourOrOff()
        {
            var bad = Patch(House(100, 100, new JArray(new JObject { ["kind"] = "window", ["at"] = 2, ["lamp"] = "yellow" })));
            var problems = ModValidator.Validate(Rules.Load(ChemistryTests.RulesDir()), ModBudgets.Load(ChemistryTests.RulesDir()), bad);
            Assert.That(problems.Select(p => p.Message), Has.Some.Contains("lamp is the colour its window glows at night"));
            var off = Patch(House(100, 100, new JArray(new JObject { ["kind"] = "window", ["at"] = 2, ["lamp"] = "off" })));
            Assert.That(ModValidator.Validate(Rules.Load(ChemistryTests.RulesDir()), ModBudgets.Load(ChemistryTests.RulesDir()), off).Select(p => p.Message), Has.None.Contains("lamp"));
        }

        [Test]
        public void EveryShippedModAndTheBaseRowsShowWhatTheySay()
        {
            var shipped = Shipped;
            var rules = Rules.Load(ChemistryTests.RulesDir());
            foreach (var file in Directory.GetFiles(Path.Combine(ChemistryTests.RulesDir(), "mods"), "*.json"))
            {
                string text = File.ReadAllText(file);
                var json = JObject.Parse(text);
                var unseen = BuildingsShown.Unseen(json["patch"] as JObject ?? json, rules.WithMods(new[] { ModPatch.Parse(text) }).Buildings, shipped);
                Assert.That(unseen, Is.Empty, Path.GetFileName(file));
            }
            // The tables' own rows, as if a mod restated them all.
            var tables = new JObject { ["buildings"] = JObject.Parse(File.ReadAllText(Path.Combine(ChemistryTests.RulesDir(), "buildings.json"))) };
            ((JObject)tables["buildings"]).Remove("about");
            ((JObject)tables["buildings"]).Remove("ground");
            ((JObject)tables["buildings"]).Remove("drawn");
            Assert.That(BuildingsShown.Unseen(tables, shipped, shipped), Is.Empty);
        }
    }
}
