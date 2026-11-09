using NUnit.Framework;
using static Chem.Tests.ChatFixtures;

namespace Chem.Tests
{
    /// <summary>The chat bench's report (core/Chat.Bench, docs/design/chat-telemetry.md), on a stream fixture's record.</summary>
    public class ChatBenchTests
    {
        [Test]
        public void TheBenchRowAndTableSayTheDesignsNumbers()
        {
            var r = Record("blue");
            string row = Chem.Bench.Program.Row(r);
            Assert.That(row, Does.Contain("applied").And.Contain("$0.0047").And.Contain("out 764").And.Contain("6 turns"));
            Assert.That(Chem.Bench.Program.Table(new[] { r, Record("blue") }), Does.Contain("a prompt, 2, applied,"));
            Assert.That(Chem.Bench.Program.Prompts, Has.Length.EqualTo(5));
        }
    }
}
