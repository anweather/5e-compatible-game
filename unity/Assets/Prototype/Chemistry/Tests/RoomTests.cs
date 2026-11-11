using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    /// <summary>Live rule changes in a room (World.Room.cs, docs/RULES_MCP.md phase 3): change modes, every peer's own checks, active mods.</summary>
    [Category("Simulation")]
    public class RoomTests
    {
        static ModBudgets budgets;
        static ModBudgets Budgets => budgets ??= ModBudgets.Load(ChemistryTests.RulesDir());
        static Rules R => ChemistryTests.R;

        static string Mod(string name) => File.ReadAllText(Path.Combine(ChemistryTests.RulesDir(), "mods", name + ".json"));

        // A fireball over every cell: past the budget's radius, so the checks refuse it.
        const string Hostile = "{ \"title\": \"Huge fireball\", \"patch\": { \"spells\": [ { \"id\": \"fireball\", \"group\": \"fire\", \"name\": \"Fireball\", \"level\": 3, \"shape\": \"burst\", \"radius\": 20, \"element\": \"fire\" } ] } }";

        static World Room(ChangeMode mode, string host = "Ana")
        {
            var w = new World(R, 64, 64, 7, "grass");
            w.OpenRoom(mode, host, R, Budgets);
            return w;
        }

        static string Last(World w) => w.Log.Count == 0 ? "" : w.Log[w.Log.Count - 1].Text;

        static string State(World w) => $"{w.CellHash()} {w.CastHash()} {w.Rules.Fingerprint} {w.RngState} {Last(w)}";

        // #155: switching a mod on puts in force the rules its checks composed (the rules in force with it), not a second
        // compose of the base with every mod. They must be the same rules, to the byte, as that second compose made: every
        // shipped mod switched on in turn, each check against RuleSet.Compose of the mods on so far.
        [Test]
        public void ASwitchPutsInForceTheSameRulesAsComposingThemAgain()
        {
            var w = Room(ChangeMode.Open);
            var on = new List<ActiveMod>();
            var names = Directory.GetFiles(Path.Combine(ChemistryTests.RulesDir(), "mods"), "*.json").Select(Path.GetFileNameWithoutExtension).OrderBy(n => n, System.StringComparer.Ordinal).ToList();
            foreach (string name in names)
            {
                if (!w.ApplyRoom(RoomCommand.Apply("Ana", name, Mod(name))).Done) continue;
                on.Add(new ActiveMod(name, Mod(name)));
                var again = RuleSet.Compose(R, on);
                Assert.That(w.RoomRules.Effective.Fingerprint, Is.EqualTo(again.Effective.Fingerprint), name);
                Assert.That(w.RoomRules.Effective.ToJson(), Is.EqualTo(again.Effective.ToJson()), name);
                Assert.That(w.Rules.Fingerprint, Is.EqualTo(again.Effective.Fingerprint), name);
                Assert.That(w.RoomRules.Mods.Select(m => m.Id), Is.EqualTo(on.Select(m => m.Id)), name);
                w.Step();
            }
            Assert.That(on.Count, Is.GreaterThanOrEqualTo(5), "most shipped mods switch on together");
            // Taken off and on again, the same.
            Assert.That(w.ApplyRoom(RoomCommand.Revert("Ana", on[0].Id)).Done, Is.True);
            Assert.That(w.ApplyRoom(RoomCommand.Apply("Ana", on[0].Id, on[0].Json)).Done, Is.True);
            var reordered = on.Skip(1).Append(on[0]).ToList();
            Assert.That(w.RoomRules.Effective.ToJson(), Is.EqualTo(RuleSet.Compose(R, reordered).Effective.ToJson()));
        }

        // ---------- a rebuilt world ----------

        [Test]
        public void ARebuiltWorldKeepsTheModsOnAndEqualsTheOneThatHadThem()
        {
            var was = Room(ChangeMode.HostOnly);
            was.ApplyRoom(RoomCommand.Apply("Ana", "big-fireball", Mod("big-fireball")));
            var a = new World(R, 64, 64, 7, "grass");
            var b = new World(R, 64, 64, 7, "grass");
            a.OpenRoom(was.Room.Mode, was.Room.Host, R, Budgets, was.Room.Mods);
            b.OpenRoom(was.Room.Mode, was.Room.Host, R, Budgets, was.Room.Mods);
            Assert.That(a.Rules.Fingerprint, Is.EqualTo(was.Rules.Fingerprint));
            Assert.That(a.RoomHash(), Is.EqualTo(was.RoomHash()));
            Assert.That(b.CastHash(), Is.EqualTo(a.CastHash()));
        }

        [Test]
        public void ARoomTakesNoRuleSwitchFromABatch()
        {
            var w = Room(ChangeMode.Open);
            var rules = w.RoomRules;
            var batch = new TickBatch(1, mods: new[] { new ActiveMod("big-fireball", Mod("big-fireball")) });
            Assert.That(batch.Prepare(w, rules, out var errors), Is.SameAs(rules));
            Assert.That(errors, Is.Empty);
            Assert.That(w.Rules.Fingerprint, Is.EqualTo(R.Fingerprint));
        }

        [Test]
        public void TheModsOnComposeOverReloadedBaseRules()
        {
            var w = Room(ChangeMode.Open);
            w.ApplyRoom(RoomCommand.Apply("Ana", "big-fireball", Mod("big-fireball")));
            string before = w.Rules.Fingerprint;
            w.RebaseRoom(R);
            Assert.That(w.Rules.Fingerprint, Is.EqualTo(before));
            Assert.That(w.Room.Mods, Has.Count.EqualTo(1));
        }

        [Test]
        public void EveryDecisionIsAnnounced()
        {
            var w = Room(ChangeMode.Locked);
            var heard = new List<RoomVerdict>();
            w.RoomDecided += (command, verdict) => heard.Add(verdict);
            w.ApplyRoom(RoomCommand.Apply("Ana", "big-fireball", Mod("big-fireball")));
            Assert.That(heard, Has.Count.EqualTo(1));
            Assert.That(heard[0].Line, Is.EqualTo(RoomPolicy.RefusedLine(RoomChange.Apply, heard[0].Reason)));
        }

        // ---------- every peer validates ----------

        [Test]
        public void TwoPeersRefuseTheHostilePatchAndSwitchOnTheGoodOneAlike()
        {
            // Two peers, each with its own world and its own checks: only the batches cross between them.
            var a = Room(ChangeMode.Open);
            var b = Room(ChangeMode.Open);
            var ra = a.RoomRules;
            var rb = b.RoomRules;
            for (int t = 1; t <= 40; t++)
            {
                TickBatch Batch()
                {
                    var batch = new TickBatch(t);
                    if (t == 3) batch.Room.Add(RoomCommand.Apply("Bo", "huge-fireball", Hostile));
                    if (t == 5) batch.Room.Add(RoomCommand.Apply("Bo", "big-fireball", Mod("big-fireball")));
                    if (t == 6) batch.Casts.Add(new CastCommand("fireball", 32, 32, 0));
                    return batch;
                }
                ra = Batch().RunOn(a, ra, out _);
                rb = Batch().RunOn(b, rb, out _);
                Assert.That(State(b), Is.EqualTo(State(a)), $"tick {t}");
                if (t == 3)
                {
                    Assert.That(Last(a), Does.StartWith("Switch on refused: Huge fireball doesn't pass the checks: Spell fireball has a radius of 20"));
                    Assert.That(a.Room.Mods, Is.Empty);
                    Assert.That(a.Rules.Fingerprint, Is.EqualTo(R.Fingerprint), "a refused patch leaves the rules as they were");
                }
            }
            Assert.That(a.Room.Mods.Select(m => $"{m.Id} {m.Title} {m.By} {m.Tick}"), Is.EqualTo(new[] { "big-fireball Big fireball Bo 4" }));
            Assert.That(ra.Fingerprint, Is.EqualTo(a.Rules.Fingerprint), "the batch hands back the room's rules");
            Assert.That(a.Rules.Spell("fireball").Radius, Is.EqualTo(4));
            Assert.That(a.Log.Select(l => l.Text), Has.Member("Bo switched on Big fireball (Open room)"));
        }

        [Test]
        public void APatchThatIsntEvenJsonIsRefusedNotThrown()
        {
            var w = Room(ChangeMode.Open);
            var v = w.ApplyRoom(RoomCommand.Apply("Bo", "junk", "{ not json"));
            Assert.That(v.Done, Is.False);
            Assert.That(v.Line, Does.StartWith("Switch on refused: junk doesn't pass the checks: That isn't valid JSON"));
            v = w.ApplyRoom(RoomCommand.Apply("Bo", "odd", "{ \"reactions\": [1, 2] }"));
            Assert.That(v.Done, Is.False, v.Line);
            Assert.That(w.RoomText(), Is.EqualTo("#room 0 Ana"));
        }

        // ---------- change modes ----------

        [Test]
        public void EachChangeModeDecidesWhoseChangeGoesIn()
        {
            var open = Room(ChangeMode.Open);
            Assert.That(open.ApplyRoom(RoomCommand.Apply("Bo", "big-fireball", Mod("big-fireball"))).Line, Is.EqualTo("Bo switched on Big fireball (Open room)"));
            Assert.That(open.ApplyRoom(RoomCommand.Revert("Cy", "big-fireball")).Line, Is.EqualTo("Cy switched off Big fireball (Open room)"));

            var hostOnly = Room(ChangeMode.HostOnly);
            Assert.That(hostOnly.ApplyRoom(RoomCommand.Apply("Bo", "big-fireball", Mod("big-fireball"))).Line, Is.EqualTo("Switch on refused: only the host can change this room"));
            Assert.That(hostOnly.ApplyRoom(RoomCommand.Apply("Ana", "big-fireball", Mod("big-fireball"))).Line, Is.EqualTo("Ana switched on Big fireball (Host-only room)"));
            Assert.That(hostOnly.ApplyRoom(RoomCommand.Revert("Bo", "big-fireball")).Line, Is.EqualTo("Revert refused: only the host can change this room"));
            Assert.That(Last(hostOnly), Is.EqualTo("Revert refused: only the host can change this room"));
            Assert.That(hostOnly.Room.Mods.Count, Is.EqualTo(1));

            var locked = Room(ChangeMode.Locked);
            var v = locked.ApplyRoom(RoomCommand.Apply("Ana", "big-fireball", Mod("big-fireball")));
            Assert.That(v.Done, Is.False);
            Assert.That(v.Reason, Is.EqualTo("this room is locked, so its rules stay as it started"));
            Assert.That(locked.Rules.Fingerprint, Is.EqualTo(R.Fingerprint));
        }

        [Test]
        public void ARefusalChangesNothingTheChecksumSees()
        {
            var w = Room(ChangeMode.HostOnly);
            string before = $"{w.RoomHash()} {w.CastHash()} {w.Rules.Fingerprint}";
            w.ApplyRoom(RoomCommand.Apply("Bo", "big-fireball", Mod("big-fireball")));
            w.ApplyRoom(RoomCommand.Revert("Ana", "big-fireball"));
            w.ApplyRoom(RoomCommand.Apply("", "big-fireball", Mod("big-fireball")));
            Assert.That($"{w.RoomHash()} {w.CastHash()} {w.Rules.Fingerprint}", Is.EqualTo(before));
            Assert.That(Last(w), Is.EqualTo("Switch on refused: the change doesn't say which peer asked for it"));
        }

        [Test]
        public void AModAlreadyOnIsRefusedAndOneNotOnCantBeReverted()
        {
            var w = Room(ChangeMode.Open);
            w.ApplyRoom(RoomCommand.Apply("Ana", "big-fireball", Mod("big-fireball")));
            Assert.That(w.ApplyRoom(RoomCommand.Apply("Bo", "big-fireball", Mod("big-fireball"))).Line, Is.EqualTo("Switch on refused: Big fireball is already on; revert it first"));
            Assert.That(w.ApplyRoom(RoomCommand.Revert("Bo", "winter")).Line, Is.EqualTo("Revert refused: winter isn't on in this room"));
        }

        [Test]
        public void ARoomWithoutAModeCantBeChangedAndTheModeIsSetOnce()
        {
            var w = new World(R, 8, 8);
            Assert.Throws<System.InvalidOperationException>(() => w.ApplyRoom(RoomCommand.Revert("Ana", "x")));
            w.OpenRoom(ChangeMode.Open, "Ana", R, Budgets);
            Assert.Throws<System.InvalidOperationException>(() => w.OpenRoom(ChangeMode.Locked, "Ana", R, Budgets));
        }

        // ---------- active mods ----------

        [Test]
        public void RevertingTheMiddleOfThreeGivesTheRulesOfTheOtherTwo()
        {
            var three = Room(ChangeMode.Open);
            foreach (var m in new[] { "big-fireball", "winter", "bright-lantern" })
                Assert.That(three.ApplyRoom(RoomCommand.Apply("Ana", m, Mod(m))).Done, Is.True, Last(three));
            Assert.That(three.ApplyRoom(RoomCommand.Revert("Bo", "winter")).Line, Is.EqualTo("Bo switched off Winter (Open room)"));

            var two = Room(ChangeMode.Open);
            foreach (var m in new[] { "big-fireball", "bright-lantern" }) two.ApplyRoom(RoomCommand.Apply("Ana", m, Mod(m)));

            Assert.That(three.Rules.Fingerprint, Is.EqualTo(two.Rules.Fingerprint));
            Assert.That(three.Room.Mods.Select(m => m.Id), Is.EqualTo(new[] { "big-fireball", "bright-lantern" }));
            Assert.That(three.Rules.Fingerprint, Is.Not.EqualTo(R.Fingerprint));
        }

        [Test]
        public void TheRoomComesThroughASave()
        {
            var w = Room(ChangeMode.HostOnly);
            w.ApplyRoom(RoomCommand.Apply("Ana", "big-fireball", Mod("big-fireball")));
            for (int i = 0; i < 5; i++) w.Step();
            w.ApplyRoom(RoomCommand.Apply("Ana", "winter", Mod("winter")));
            var save = w.Save(new ComboTests.Blank());

            var back = World.Load(save, R, new ComboTests.Blank(), Budgets);
            Assert.That(back.RoomText(), Is.EqualTo(w.RoomText()));
            Assert.That(back.Rules.Fingerprint, Is.EqualTo(w.Rules.Fingerprint));
            Assert.That(back.Room.Mods.Select(m => $"{m.Title} {m.By} {m.Tick}"), Is.EqualTo(new[] { "Big fireball Ana 0", "Winter Ana 5" }));
            Assert.That(back.ApplyRoom(RoomCommand.Revert("Bo", "winter")).Reason, Is.EqualTo("only the host can change this room"));
            Assert.That(back.ApplyRoom(RoomCommand.Revert("Ana", "winter")).Done, Is.True);
            Assert.That(back.Rules.Fingerprint, Is.EqualTo(R.WithMods(new[] { ModPatch.Parse(Mod("big-fireball")) }).Fingerprint));

            Assert.Throws<SaveException>(() => World.Load(save, R, new ComboTests.Blank()), "a room's save needs the budgets");
            Assert.That(World.Load(new World(R, 8, 8).Save(new ComboTests.Blank()), R, new ComboTests.Blank()).Room, Is.Null, "a save without a room loads as before");
        }

        [Test]
        public void ALateJoinerReplaysTheRoomCommandsToTheSameWorld()
        {
            var commands = new List<(int, RoomCommand)>
            {
                (2, RoomCommand.Apply("Bo", "big-fireball", Mod("big-fireball"))),
                (4, RoomCommand.Apply("Bo", "huge-fireball", Hostile)),
                (6, RoomCommand.Apply("Cy", "winter", Mod("winter"))),
                (9, RoomCommand.Revert("Ana", "big-fireball")),
            };
            var casts = new[] { (3, new CastCommand("fireball", 20, 20, 0)), (10, new CastCommand("fireball", 40, 40, 0)) };
            World Play()
            {
                var w = Room(ChangeMode.Open);
                var batches = TickBatch.Timeline(casts, null, 20);
                TickBatch.AddRoom(batches, commands);
                var rules = w.RoomRules;
                for (int t = 1; t <= 20; t++) rules = batches[t].RunOn(w, rules, out _);
                return w;
            }
            var live = Play();
            var late = Play();
            Assert.That(State(late), Is.EqualTo(State(live)));
            Assert.That(live.Room.Mods.Select(m => m.Id), Is.EqualTo(new[] { "winter" }));
        }
    }
}
