using System.Linq;
using Chem.Mcp;
using NUnit.Framework;

namespace Chem.Tests
{
    /// <summary>The rules MCP's room tools (core/Rules.Mcp/Workbench.Room.cs): apply, revert and room, on a world in memory.</summary>
    public class RoomToolsTests
    {
        // Each test its own bench: the room is state.
        static Workbench NewBench() => new Workbench(ChemistryTests.RulesDir());

        [Test]
        public void ApplyAndRevertGoThroughTheRoomAndRoomListsWhatIsOn()
        {
            var b = NewBench();
            StringAssert.StartsWith("Open room, at tick 0; the host is host.", b.Room());
            StringAssert.Contains("No mods on", b.Room());

            StringAssert.StartsWith("Ana switched on Big fireball (Open room).", b.Apply("big-fireball", "Ana"));
            var draft = b.Tune(null, "push.slideCells", "5");
            b.Apply(draft.Id);
            var room = b.Room();
            StringAssert.Contains("2 mods on, in order:\n1. Big fireball (big-fireball), added by Ana at tick 0\n2. a new mod (" + draft.Id + "), added by host at tick 1", room);

            StringAssert.StartsWith("Bo switched off Big fireball (Open room).", b.Revert("big-fireball", "Bo"));
            Assert.That(b.RoomNow.Mods.Select(m => m.Id), Is.EqualTo(new[] { draft.Id }));
        }

        [Test]
        public void ARefusalComesBackAsTheLogLine()
        {
            var b = NewBench();
            b.Room("host-only");
            StringAssert.StartsWith("Switch on refused: only the host can change this room.", b.Apply("big-fireball", "Bo"));
            StringAssert.StartsWith("Switch on refused: Huge doesn't pass the checks: Spell fireball has a radius of 20",
                b.Apply("{ \"title\": \"Huge\", \"patch\": { \"spells\": [ { \"id\": \"fireball\", \"group\": \"fire\", \"name\": \"Fireball\", \"level\": 3, \"shape\": \"burst\", \"radius\": 20, \"element\": \"fire\" } ] } }"));
            StringAssert.StartsWith("Host-only room", b.Room());

            b.Room("locked");
            StringAssert.StartsWith("Switch on refused: this room is locked", b.Apply("winter"));
            Assert.Throws<RulesToolException>(() => b.Room("anarchy"));
        }
    }
}
