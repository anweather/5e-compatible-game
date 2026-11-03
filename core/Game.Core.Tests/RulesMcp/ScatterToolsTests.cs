using System.Linq;
using Chem.Mcp;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    /// <summary>scatter (docs/design/events-and-placing.md 2b): "50 trees near the lake" picks cells now and writes them as a lay row.</summary>
    public class ScatterToolsTests
    {
        static Workbench Bench() => new Workbench(ChemistryTests.Folders);

        static JArray Lays(Workbench b, string said) => (JArray)b.DraftOf(DraftIn(said)).Patch["world"]["lay"];

        static string DraftIn(string said) => said.Split('\n').First(l => l.StartsWith("Draft ", System.StringComparison.Ordinal)).Substring(6).TrimEnd(':');

        [Test]
        public void FiftyTreesNearTheLakeAreALayRowThatValidates()
        {
            var b = Bench();
            var tools = new RulesTools(b);
            string said = tools.Scatter("tree", 50, near: "lake", edge: "shore", within: 12, spacing: 2);
            StringAssert.StartsWith("50 tree on ", said);
            StringAssert.Contains("near the lake's shore, 2 cells apart at least, the nearest 1 cells", said);
            var rows = Lays(b, said);
            Assert.That(rows, Has.Count.EqualTo(1));
            var cells = ((JArray)rows[0]["cells"]).Select(c => ((int)c[0], (int)c[1])).ToList();
            Assert.That(cells, Has.Count.EqualTo(50));
            Assert.That(cells.Distinct().Count(), Is.EqualTo(50));
            foreach (var a in cells)
                Assert.That(cells.Where(c => c != a).All(c => System.Math.Max(System.Math.Abs(c.Item1 - a.Item1), System.Math.Abs(c.Item2 - a.Item2)) > 2));
            Assert.That(b.Validate(DraftIn(said)), Is.Empty);
            var w = b.LiveWorld;
            Assert.That(cells.All(c => w.At(c.Item1, c.Item2).Water == 0 && w.HasTag(w.At(c.Item1, c.Item2), "ground")), "open dry ground");
        }

        [Test]
        public void TheSameSeedPicksTheSameCellsAndAnotherSeedOthers()
        {
            var b = Bench();
            var tools = new RulesTools(b);
            string Cells(string said) => Lays(b, said)[Lays(b, said).Count - 1]["cells"].ToString(Newtonsoft.Json.Formatting.None);
            string one = tools.Scatter("brush", 8, near: "lake", within: 10);
            Assert.That(Cells(tools.Scatter("brush", 8, near: "lake", within: 10)), Is.EqualTo(Cells(one)));
            string draft = DraftIn(one);
            tools.Scatter("brush", 8, near: "lake", within: 10, seed: 2, draft: draft);
            var rows = b.DraftOf(draft).Patch["world"]["lay"];
            Assert.That(rows.Count(), Is.EqualTo(2), "another seed is another row");
            Assert.That(rows[1]["cells"].ToString(), Is.Not.EqualTo(rows[0]["cells"].ToString()));
            tools.Scatter("brush", 4, near: "lake", within: 10, seed: 2, draft: draft);
            Assert.That(b.DraftOf(draft).Patch["world"]["lay"][1]["cells"].Count(), Is.EqualTo(4), "the same seed replaces its row");
            StringAssert.Contains("Draft", tools.Remove(draft, "lay", "brush#2"));
            Assert.That(b.DraftOf(draft).Patch["world"]["lay"].Count(), Is.EqualTo(1));
        }

        [Test]
        public void ThingsScatterAsAddRowsAndTheBudgetsHold()
        {
            var b = Bench();
            var tools = new RulesTools(b);
            string said = tools.Scatter("barrel", 3, near: "lake", within: 8, spacing: 1);
            Assert.That(b.DraftOf(DraftIn(said)).Patch["world"]["add"].Count(), Is.EqualTo(3));
            StringAssert.Contains("count is 1 to 256", Assert.Throws<RulesToolException>(() => tools.Scatter("tree", 300, near: "lake")).Message);
            StringAssert.Contains("within is 0 to 32", Assert.Throws<RulesToolException>(() => tools.Scatter("tree", 3, near: "lake", within: 40)).Message);
            StringAssert.Contains("no terrain, cover or thing called nope", Assert.Throws<RulesToolException>(() => tools.Scatter("nope", 3, near: "lake")).Message);
        }
    }
}
