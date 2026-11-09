using System.Linq;
using Chem.Mcp;
using NUnit.Framework;

namespace Chem.Tests
{
    /// <summary>
    /// Named acts (docs/design/world-acts.md): a trigger with on: called, each row what act takes, fired by call_act at a
    /// place as one firing. It lives in a mod that's on, so a saved mod carries it. No firework or campsite is in the code:
    /// these rows are only tests of the tools.
    /// </summary>
    public class NamedActsTests
    {
        const string Rows = "[{\"act\": \"tree\", \"count\": 5, \"within\": 8}, {\"act\": \"land\", \"radius\": 10, \"rise\": 12, \"rough\": 0}]";

        static (Workbench bench, RulesTools tools) Tools()
        {
            var b = new Workbench(ChemistryTests.Folders);
            return (b, new RulesTools(b));
        }

        static void On(RulesTools tools, string rows = Rows) => tools.Apply(DraftOf(tools, rows), title: "Camp");

        static string DraftOf(RulesTools tools, string rows)
        {
            string said = tools.AddTrigger("my-camp", rows, on: "called");
            StringAssert.Contains("call_act fires it", said);
            return said.Split('\n').First(l => l.StartsWith("Draft ", System.StringComparison.Ordinal)).Substring(6).TrimEnd(':');
        }

        static int Trees(World w) => w.Cells.Count(c => c.T == "tree");

        [Test]
        public void DefinedOnceItFiresAtAPlaceAndAgainAtAnother()
        {
            var (b, tools) = Tools();
            On(tools);
            int trees = Trees(b.LiveWorld);
            StringAssert.Contains("called my-camp (act 1)", tools.CallAct("my-camp", near: "[120, 120]"));
            StringAssert.Contains("called my-camp (act 2)", tools.CallAct("my-camp", near: "[160, 120]"));
            Assert.That(Trees(b.LiveWorld) - trees, Is.EqualTo(10));
            Assert.That(b.LiveWorld.LandMovedAt(120, 120), Is.EqualTo(96));
            Assert.That(b.LiveWorld.LandMovedAt(160, 120), Is.EqualTo(96));
        }

        [Test]
        public void UndoTakesBackOneFiringWhole()
        {
            var (b, tools) = Tools();
            On(tools);
            int trees = Trees(b.LiveWorld);
            tools.CallAct("my-camp", near: "[120, 120]");
            tools.CallAct("my-camp", near: "[160, 120]");
            StringAssert.Contains("took back my-camp (act 2)", tools.UndoAct());
            Assert.That(Trees(b.LiveWorld) - trees, Is.EqualTo(5));
            Assert.That(b.LiveWorld.LandMovedAt(160, 120), Is.Zero);
            Assert.That(b.LiveWorld.LandMovedAt(120, 120), Is.EqualTo(96), "only that firing");
        }

        [Test]
        public void WhatCantBeTakenBackStaysAndSaysSo()
        {
            var (b, tools) = Tools();
            On(tools, "[{\"act\": \"tree\", \"count\": 3}, {\"act\": \"deer\", \"count\": 2}]");
            int trees = Trees(b.LiveWorld);
            tools.CallAct("my-camp", near: "[120, 120]");
            StringAssert.Contains("what can't be taken back stays", tools.UndoAct());
            Assert.That(Trees(b.LiveWorld), Is.EqualTo(trees));
        }

        [Test]
        public void AFiringOfOnlyAnimalsCantBeTakenBack()
        {
            var (_, tools) = Tools();
            On(tools, "{\"act\": \"deer\", \"count\": 2}");
            tools.CallAct("my-camp", near: "[120, 120]");
            StringAssert.Contains("can be taken back", tools.UndoAct());
        }

        [Test]
        public void APartWithNoRoomIsSkippedAndTheRestHappens()
        {
            var (b, tools) = Tools();
            On(tools);
            string said = tools.CallAct("my-camp", near: "lake");
            StringAssert.Contains("1 of it had no room", said);
            Assert.That(b.LiveWorld.LandHasMoved, Is.False, "a stamp keeps off the water");
        }

        [Test]
        public void AFiringNamesWhatLandedAndWhatDidNot()
        {
            var (_, tools) = Tools();
            On(tools, "[{\"act\": \"crate\", \"count\": 2}, {\"act\": \"land\", \"radius\": 10, \"rise\": 12}]");
            string said = tools.CallAct("my-camp", near: "lake");
            StringAssert.Contains("only part landed: 2 crate", said);
            StringAssert.Contains("1 of it had no room: land (that would cross water", said);
            StringAssert.DoesNotContain("It's done", said);
            StringAssert.Contains("tell the player what didn't", said);
        }

        // #149's case: the row is found to have no room before the command, so only the reply can say so.
        [Test]
        public void ARowWithNoRoomBeforeTheCommandIsStillNamed()
        {
            var (_, tools) = Tools();
            On(tools, "[{\"act\": \"crate\", \"count\": 1, \"within\": 2}, {\"act\": \"barrel\", \"count\": 1, \"within\": 0, \"onto\": [\"lava\"]}]");
            string said = tools.CallAct("my-camp", near: "[120, 120]");
            StringAssert.Contains("only part landed: 1 crate", said);
            StringAssert.Contains("1 of it had no room: barrel (No open cell", said);
            StringAssert.DoesNotContain("It's done", said);
        }

        [Test]
        public void AShortRowSaysHowShort()
        {
            var (_, tools) = Tools();
            On(tools, "[{\"act\": \"crate\", \"count\": 4, \"within\": 1, \"spacing\": 2}, {\"act\": \"barrel\", \"count\": 1, \"within\": 1}]");
            string said = tools.CallAct("my-camp", near: "[120, 120]");
            StringAssert.Contains("1 of 4 crate", said);
            StringAssert.DoesNotContain("It's done", said);
        }

        [Test]
        public void AShortActSaysHowShort()
        {
            var (_, tools) = Tools();
            string said = tools.Act("crate", 4, near: "[120, 120]", within: 1, spacing: 2);
            StringAssert.Contains("only part landed:", said);
            StringAssert.Contains("had no room", said);
            StringAssert.DoesNotContain("It's done", said);
        }

        [Test]
        public void ANothingFiringIsNotDone()
        {
            var (b, tools) = Tools();
            On(tools, "[{\"act\": \"crate\", \"count\": 1, \"within\": 0}, {\"act\": \"barrel\", \"count\": 1, \"within\": 0}]");
            int wet = System.Array.FindIndex(b.LiveWorld.Cells.ToArray(), c => c.Water > 0);
            string said = tools.CallAct("my-camp", near: $"[{wet % b.LiveWorld.W}, {wet / b.LiveWorld.W}]");
            StringAssert.Contains("None of my-camp landed", said);
            StringAssert.DoesNotContain("It's done", said);
        }

        [Test]
        public void AFiringAtAPlaceStandsTogether()
        {
            var (b, tools) = Tools();
            On(tools, "[{\"act\": \"crate\", \"count\": 1, \"within\": 3}, {\"act\": \"crate\", \"count\": 2, \"within\": 3}, {\"act\": \"barrel\", \"count\": 1, \"within\": 3}]");
            int before = b.LiveWorld.Placed.Count;
            string said = tools.CallAct("my-camp", near: "lake", edge: "shore");
            StringAssert.Contains("It's done", said);
            var placed = b.LiveWorld.Placed.Skip(before).ToList();
            Assert.That(placed.Count, Is.EqualTo(4));
            int across = placed.Max(p => placed.Max(q => System.Math.Max(System.Math.Abs(p.CellX - q.CellX), System.Math.Abs(p.CellY - q.CellY))));
            Assert.That(across, Is.LessThanOrEqualTo(6), "every row within 3 of the one spot");
        }

        // #156: a stamp that didn't fit where it was asked was taken to the first spot anywhere round the place that it
        // fit, far off. Now it moves at most land.maxMove (4) cells from the place's middle, the nearest it fits.
        [Test]
        public void AStampThatDoesNotFitMovesAFewCellsAtMost()
        {
            var (b, tools) = Tools();
            On(tools, "[{\"act\": \"crate\", \"count\": 1, \"within\": 6}, {\"act\": \"land\", \"radius\": 2, \"rise\": 3, \"rough\": 0}]");
            string said = tools.CallAct("my-camp", near: "hollowmere", side: "north");
            var off = System.Text.RegularExpressions.Regex.Match(said, @"The land stands (\d+) cells? off \[(\d+), (\d+)\], the nearest it fit");
            Assert.That(off.Success, Is.True, said);
            int Got(int k) => int.Parse(off.Groups[k].Value, System.Globalization.CultureInfo.InvariantCulture);
            var stamp = LandStamp.Read(Newtonsoft.Json.Linq.JArray.Parse(b.LiveWorld.LandHash()));
            int moved = System.Math.Max(System.Math.Abs(stamp.X - Got(2)), System.Math.Abs(stamp.Y - Got(3)));
            Assert.That(moved, Is.EqualTo(Got(1)).And.InRange(1, 4));
            Assert.That(b.LiveWorld.LandHash(), Is.EqualTo("[216,284,2,24,\"dome\",0,0,0,2]"), "3 cells off [216, 287], the shore's middle");
        }

        // #156: a stamp with nowhere to fit near where it was asked isn't moved: it's refused there, and says why.
        [Test]
        public void AStampThatFitsNowhereNearIsRefusedWhereAsked()
        {
            var (b, tools) = Tools();
            On(tools, "[{\"act\": \"crate\", \"count\": 1}, {\"act\": \"land\", \"radius\": 3, \"rise\": 3, \"rough\": 0}]");
            string said = tools.CallAct("my-camp", near: "lake");
            StringAssert.Contains("1 of it had no room: land (that would cross water", said);
            Assert.That(b.LiveWorld.LandHasMoved, Is.False, "not taken to dry ground round the lake");
        }

        [Test]
        public void TheSameFiringOnTheSameWorldPicksTheSameSpot()
        {
            string Fired()
            {
                var (_, tools) = Tools();
                On(tools, "[{\"act\": \"crate\", \"count\": 1, \"within\": 3}, {\"act\": \"barrel\", \"count\": 1, \"within\": 3}]");
                return tools.CallAct("my-camp", near: "lake", edge: "shore").Split('\n').First(l => l.StartsWith("At ", System.StringComparison.Ordinal));
            }
            Assert.That(Fired(), Is.EqualTo(Fired()));
        }

        [Test]
        public void ItLivesInAModThatsOn()
        {
            var (_, tools) = Tools();
            Assert.That(() => tools.CallAct("my-camp"), Throws.Exception.With.Message.Contain("no named act"));
            string draft = DraftOf(tools, Rows);
            Assert.That(() => tools.CallAct("my-camp"), Throws.Exception.With.Message.Contain("no named act"), "a draft isn't on");
            tools.Apply(draft, title: "Camp");
            StringAssert.Contains("a named act, for anyone to call", tools.List("triggers"));
        }

        [Test]
        public void ItsRowsAreHeldToWhatActTakes()
        {
            var (_, tools) = Tools();
            Assert.That(() => tools.AddTrigger("t", "{\"act\": \"tree\", \"cast\": \"fire-bolt\"}", on: "called"), Throws.Exception.With.Message.Contain("isn't a field of an act"));
            Assert.That(() => tools.AddTrigger("t", "{\"count\": 2}", on: "called"), Throws.Exception.With.Message.Contain("needs act"));
            Assert.That(() => tools.AddTrigger("t", "{\"act\": \"tree\"}", on: "called", every: 6), Throws.Exception.With.Message.Contain("takes no every"));
            Assert.That(() => tools.AddTrigger("t", "{\"act\": \"tree\"}"), Throws.Exception, "a step trigger doesn't take an act");
            On(tools, "{\"act\": \"land\", \"radius\": 33, \"rise\": 5}");
            Assert.That(() => tools.CallAct("my-camp", near: "[120, 120]"), Throws.Exception.With.Message.Contain("radius is 1 to 32"), "the same limits as act");
        }

        [Test]
        public void EveryPeerHasTheSameFiring()
        {
            var (a, tools) = Tools();
            On(tools);
            int commands = 0;
            System.Collections.Generic.List<RoomCommand> sent = new System.Collections.Generic.List<RoomCommand>();
            a.LiveWorld.RoomDecided += (c, v) =>
            {
                commands++;
                sent.Add(c);
            };
            tools.CallAct("my-camp", near: "[120, 120]");
            Assert.That(commands, Is.EqualTo(1), "one command for the whole firing");
            var (b, other) = Tools();
            On(other);
            b.LiveWorld.ApplyRoom(sent[0]);
            b.LiveWorld.Step(); // as the bench does after a command
            Assert.That(b.LiveWorld.CellHash(), Is.EqualTo(a.LiveWorld.CellHash()));
            Assert.That(b.LiveWorld.LandHash(), Is.EqualTo(a.LiveWorld.LandHash()));
            Assert.That(b.LiveWorld.ActText(), Is.EqualTo(a.LiveWorld.ActText()));
        }
    }
}
